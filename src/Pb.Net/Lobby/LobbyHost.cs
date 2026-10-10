using Pb.Net.Packing;
using Pb.Net.Protocol;
using Pb.Net.Server;
using Pb.Sim.Data;
using Pb.Sim.Match;

namespace Pb.Net.Lobby;

/// <summary>Someone who played in a round, for the session's score: their lobby member, their player in it and their side.</summary>
public readonly record struct PersonInRound(int MemberId, int PlayerId, int Team);

/// <summary>
/// The lobby on the host, without the engine: who's in (people come and go through the <see cref="NetServer"/>), their
/// sides, characters and ready flags, the host's choices, the countdown, the vote, text chat and the session's score
/// (kept for someone who leaves and comes back, known by their identity's id).
/// It decides every request (a switch that would put the sides more than one apart is refused while balance is on;
/// chat is cut to length and limited), and sends the lobby to everyone whenever it changes, and every few seconds
/// during a round for the pings. The host's own player, if they play, is member 0; on a dedicated server there's none.
/// A match of points (speedball, capture the flag) is played point after point without the lobby in between (<see cref="NextPoint"/>), its score kept
/// here until it's won.
/// </summary>
public sealed class LobbyHost : IDisposable
{
    public const int HostId = 0;

    private readonly NetServer _server;
    private readonly NetSettings _settings;
    private readonly MatchRules _rules;
    private readonly Pb.Sim.Gear.GearCatalog? _gear;
    private readonly Func<double> _clock;
    private readonly BitWriter _writer = new(4096);
    private readonly Dictionary<int, Queue<double>> _said = new();
    private readonly Dictionary<string, (int Eliminations, int RoundsWon)> _gone = new(StringComparer.Ordinal);
    private readonly List<ChatLine> _heard = new();
    private double _deadline;
    private double _sentAt = double.NegativeInfinity;
    private bool _dirty = true;

    /// <param name="gear">The gear catalogue everyone's kit is checked against (null: kit is passed on as it comes).</param>
    public LobbyHost(NetServer server, NetSettings settings, MatchRules rules, LobbyChoices choices, Func<double> clock, LobbyMember? host = null,
        Pb.Sim.Gear.GearCatalog? gear = null)
    {
        _server = server;
        _settings = settings;
        _rules = rules;
        _gear = gear;
        _clock = clock;
        State.ServerName = server.Identity.Name;
        State.Choices = choices;
        State.MaxPeople = settings.MaxPeople;
        if (host is not null)
        {
            host.Id = HostId;
            host.Host = true;
            host.Kit = Checked(host.Kit);
            State.Members.Add(host);
        }

        PlaceOnSides();
        server.Joined += Join;
        server.Left += Leave;
        server.Message += Receive;
    }

    public LobbyState State { get; } = new();

    public LobbyPhase Phase => State.Phase;

    /// <summary>The countdown's or the vote's time left now (s; 0 otherwise).</summary>
    public float TimeLeft => State.Phase is LobbyPhase.Countdown or LobbyPhase.Vote ? (float)Math.Max(0.0, _deadline - _clock()) : 0f;

    /// <summary>The countdown ran out: the host builds the round (the lobby is <see cref="LobbyPhase.Loading"/> now).</summary>
    public event Action? CountdownFinished;

    /// <summary>The lobby changed (for the host's own screen).</summary>
    public event Action? Changed;

    /// <summary>The mode chosen plays sides that people choose (teams).</summary>
    public bool HasSides => KindOf(State.Choices.ModeId) == MatchModeKind.Teams;

    /// <summary>The mode chosen plays a match of points (speedball, capture the flag).</summary>
    public bool PlaysMatch => Points is not null;

    /// <summary>The points a side needs to win the match.</summary>
    public int RaceTo => State.Choices.RaceTo > 0 ? State.Choices.RaceTo : Points?.RaceTo ?? 1;

    /// <summary>A match has begun and nobody has won it yet: its next point comes straight after the last.</summary>
    public bool MatchOn => PlaysMatch && State.MatchPlayed > 0 && State.MatchPoints[0] < RaceTo && State.MatchPoints[1] < RaceTo;

    /// <summary>The match's rules (null when the mode chosen plays one round).</summary>
    private IPointRules? Points => _rules.FindMode(State.Choices.ModeId) is { } mode ? _rules.PointsFor(mode.Format) : null;

    /// <summary>The match as it stands, for its next point's setup.</summary>
    public MatchScore Score => new(RaceTo, State.MatchPoints[0], State.MatchPoints[1], State.MatchPlayed);

    /// <summary>Chat lines for the host's own screen (everything said that the host may read), each once.</summary>
    public IReadOnlyList<ChatLine> TakeChat()
    {
        if (_heard.Count == 0)
        {
            return Array.Empty<ChatLine>();
        }

        ChatLine[] lines = _heard.ToArray();
        _heard.Clear();
        return lines;
    }

    /// <summary>The host's choices (only between rounds; a running countdown starts again from the lobby).</summary>
    public void SetChoices(LobbyChoices choices)
    {
        if (State.Phase is not (LobbyPhase.Lobby or LobbyPhase.Countdown or LobbyPhase.Vote) || choices == State.Choices)
        {
            return;
        }

        State.Choices = choices;
        PlaceOnSides();
        if (State.Phase == LobbyPhase.Countdown)
        {
            State.Phase = LobbyPhase.Lobby;
        }

        Touch();
    }

    /// <summary>
    /// A member's kit, from their gear locker, at any time (it's worn from the next round): an item that isn't one of its
    /// slot's becomes the slot's default, and their character becomes the kit's.
    /// </summary>
    public void SetKit(int memberId, Pb.Sim.Gear.Loadout kit)
    {
        if (State.Find(memberId) is not { } member)
        {
            return;
        }

        member.Kit = Checked(kit);
        member.Look = (byte)Math.Clamp(member.Kit!.Character, 0, 255);
        Touch();
    }

    /// <summary>
    /// A member asks for something: a side, ready or not, a character, a vote. False if it was refused (the wrong time,
    /// or a side switch that the balance or the size of a side won't allow).
    /// </summary>
    public bool Ask(int memberId, LobbyAsk ask, int value)
    {
        if (State.Find(memberId) is not { } member)
        {
            return false;
        }

        bool between = State.Phase is LobbyPhase.Lobby or LobbyPhase.Countdown or LobbyPhase.Vote;
        switch (ask)
        {
            case LobbyAsk.Side when between && HasSides && value is (0 or 1) && member.Side != value:
                if (!CanJoinSide(member, value))
                {
                    return false;
                }

                member.Side = value;
                break;
            case LobbyAsk.Ready when between:
                member.Ready = value != 0;
                if (!member.Ready && State.Phase == LobbyPhase.Countdown && !State.Forced)
                {
                    State.Phase = LobbyPhase.Lobby;
                }

                break;
            case LobbyAsk.Look when between && value is >= 0 and <= 255:
                member.Look = (byte)value;
                if (member.Kit is { } kit)
                {
                    kit.Character = value;
                }

                break;
            case LobbyAsk.Vote when State.Phase == LobbyPhase.Vote && value >= -1 && value < State.VoteOptions.Count:
                member.Vote = value;
                break;
            default:
                return false;
        }

        if (State.Phase == LobbyPhase.Lobby && State.Members.Count > 0 && State.Members.All(m => m.Ready))
        {
            Countdown(forced: false);
        }

        Touch();
        return true;
    }

    /// <summary>Someone says something: to everyone, or (in teams) only their side. Cut to length; too much too fast is dropped.</summary>
    public bool Say(int memberId, string text, bool teamOnly)
    {
        if (State.Find(memberId) is not { } member)
        {
            return false;
        }

        string line = Clean(text);
        if (line.Length == 0)
        {
            return false;
        }

        double now = _clock();
        if (!_said.TryGetValue(memberId, out Queue<double>? times))
        {
            _said[memberId] = times = new Queue<double>();
        }

        while (times.Count > 0 && now - times.Peek() > _settings.ChatPer)
        {
            times.Dequeue();
        }

        if (times.Count >= _settings.ChatLines)
        {
            return false;
        }

        times.Enqueue(now);
        bool team = teamOnly && HasSides && member.Side >= 0;
        var said = new ChatLine(member.Id, member.Name, member.Side, team, line);
        _writer.Reset();
        ChatMessage.WriteLine(_writer, said);
        ReadOnlySpan<byte> packet = _writer.Finish();
        foreach (ClientLink link in _server.Clients)
        {
            if (link.Welcomed && (!team || State.Find(link.Peer)?.Side == member.Side))
            {
                _server.Send(link, packet);
            }
        }

        if (State.Find(HostId) is not { } host || !team || host.Side == member.Side)
        {
            _heard.Add(said);
        }

        return true;
    }

    /// <summary>The host sends someone away.</summary>
    public void Remove(int memberId)
    {
        foreach (ClientLink link in _server.Clients.ToArray())
        {
            if (link.Peer == memberId && link.Welcomed)
            {
                _server.Remove(link, RefusedReason.Removed, "The host removed you from the game.");
            }
        }
    }

    /// <summary>The host starts the countdown without waiting for everyone to be ready.</summary>
    public void Start() => Countdown(forced: true);

    /// <summary>The round is being built now (straight after the countdown, or when the host skips it).</summary>
    public void BeginLoading()
    {
        State.Phase = LobbyPhase.Loading;
        Touch();
    }

    /// <summary>The round has started (its briefing or live).</summary>
    public void RoundStarted()
    {
        State.Phase = LobbyPhase.Round;
        Touch();
    }

    /// <summary>
    /// The round's over: its result goes in the session's score (a win for the winning side, or the person last standing,
    /// and everyone's eliminations), and everyone sees their summary.
    /// </summary>
    public void RoundOver(MatchResult result, IReadOnlyList<PersonInRound> people, IReadOnlyList<StatsEntry> stats)
    {
        MatchModeKind kind = KindOf(State.Choices.ModeId);
        if (PlaysMatch)
        {
            // A point of the match: the side that reaches the target wins it, and that's what the session counts.
            State.MatchPlayed++;
            if (result.Winner is 0 or 1 && ++State.MatchPoints[result.Winner] >= RaceTo)
            {
                State.SideWins[result.Winner]++;
            }
        }
        else if (kind != MatchModeKind.FreeForAll && result.Winner is 0 or 1)
        {
            State.SideWins[result.Winner]++;
        }

        foreach (PersonInRound p in people)
        {
            if (State.Find(p.MemberId) is not { } member)
            {
                continue;
            }

            if (result.Winner >= 0 && p.Team == result.Winner)
            {
                member.RoundsWon++;
            }

            foreach (StatsEntry s in stats)
            {
                if (s.PlayerId == p.PlayerId)
                {
                    member.Eliminations += s.Eliminations;
                }
            }
        }

        State.RoundsPlayed++;
        State.Phase = LobbyPhase.Summary;
        Touch();
    }

    /// <summary>
    /// After the summary: back to the lobby, or first to the vote when the host has it on (with the places offered),
    /// everyone not ready again.
    /// </summary>
    public void BackToLobby(IReadOnlyList<VoteOption>? options = null)
    {
        _server.LeaveRound();
        foreach (LobbyMember m in State.Members)
        {
            m.Ready = false;
            m.Vote = -1;
        }

        // A match won (or left unfinished) is over: the next one starts from nothing.
        State.MatchPoints[0] = State.MatchPoints[1] = 0;
        State.MatchPlayed = 0;

        State.VoteOptions.Clear();
        State.Forced = false;
        if (State.Choices.Vote && options is { Count: >= 2 })
        {
            State.VoteOptions.AddRange(options.Take(_settings.VoteOptions));
            State.Phase = LobbyPhase.Vote;
            _deadline = _clock() + _settings.VoteTime;
        }
        else
        {
            State.Phase = LobbyPhase.Lobby;
        }

        Touch();
    }

    /// <summary>A point's over and the match goes on: its next point is being built now, everyone still in.</summary>
    public void NextPoint()
    {
        _server.LeaveRound();
        State.Phase = LobbyPhase.Loading;
        Touch();
    }

    /// <summary>Each frame on the host: the countdown and the vote run, the pings are refreshed, and the lobby goes out if it changed.</summary>
    public void Update()
    {
        double now = _clock();
        if (State.Phase == LobbyPhase.Countdown && now >= _deadline)
        {
            State.Phase = LobbyPhase.Loading;
            Touch();
            Send(now);
            CountdownFinished?.Invoke();
        }
        else if (State.Phase == LobbyPhase.Vote && now >= _deadline)
        {
            DecideVote();
        }

        bool refresh = State.Phase is (LobbyPhase.Round or LobbyPhase.Loading) && now - _sentAt >= _settings.LobbyRefresh;
        if (_dirty || refresh || (State.Phase is (LobbyPhase.Countdown or LobbyPhase.Vote) && now - _sentAt >= 1.0))
        {
            Send(now);
        }
    }

    public void Dispose()
    {
        _server.Joined -= Join;
        _server.Left -= Leave;
        _server.Message -= Receive;
    }

    /// <summary>The fewest people on either side, and which side that is (0 on a tie).</summary>
    public int SmallerSide()
    {
        int zero = State.Members.Count(m => m.Side == 0), one = State.Members.Count(m => m.Side == 1);
        return one < zero ? 1 : 0;
    }

    private bool CanJoinSide(LobbyMember member, int side)
    {
        int there = State.Members.Count(m => m.Side == side && m != member);
        int other = State.Members.Count(m => m.Side == 1 - side && m != member);
        // A side holds at most half the round's places; with balance on, no more than one ahead of the other.
        if (there + 1 > _rules.MaxPlayers / 2)
        {
            return false;
        }

        return !State.Choices.Balance || there + 1 - other <= 1;
    }

    private void Countdown(bool forced)
    {
        if (State.Phase is not (LobbyPhase.Lobby or LobbyPhase.Countdown) || (State.Members.Count == 0 && forced))
        {
            return;
        }

        State.Phase = LobbyPhase.Countdown;
        State.Forced = forced;
        _deadline = _clock() + _settings.Countdown;
        Touch();
    }

    private void DecideVote()
    {
        int best = -1, most = -1;
        for (int i = 0; i < State.VoteOptions.Count; i++)
        {
            int votes = State.Members.Count(m => m.Vote == i);
            if (votes > most)
            {
                best = i;
                most = votes;
            }
        }

        if (best >= 0)
        {
            VoteOption won = State.VoteOptions[best];
            State.Choices = State.Choices with { LevelId = won.LevelId, PlaceId = won.PlaceId };
        }

        foreach (LobbyMember m in State.Members)
        {
            m.Vote = -1;
        }

        State.VoteOptions.Clear();
        State.Phase = LobbyPhase.Lobby;
        Touch();
    }

    /// <summary>Sides for everyone who needs one (teams: the smaller side; otherwise none).</summary>
    private void PlaceOnSides()
    {
        bool sides = HasSides;
        foreach (LobbyMember m in State.Members)
        {
            if (!sides)
            {
                m.Side = -1;
            }
        }

        if (!sides)
        {
            return;
        }

        foreach (LobbyMember m in State.Members)
        {
            if (m.Side is not (0 or 1))
            {
                m.Side = SmallerSide();
            }
        }
    }

    private void Join(ClientLink link)
    {
        var member = new LobbyMember { Id = link.Peer, Name = link.Name, Look = link.Look, Kit = Checked(link.Kit) };
        if (HasSides)
        {
            member.Side = SmallerSide();
        }

        // Someone back this session has their score again (unless another copy with the same id is still in: two
        // copies on one computer share it).
        if (link.Key.Length > 0 && !_server.Clients.Any(c => c != link && c.Welcomed && c.Key == link.Key)
            && _gone.Remove(link.Key, out (int Eliminations, int RoundsWon) score))
        {
            member.Eliminations = score.Eliminations;
            member.RoundsWon = score.RoundsWon;
        }

        State.Members.Add(member);
        Touch();
    }

    private void Leave(ClientLink link)
    {
        if (link.Key.Length > 0 && State.Find(link.Peer) is { Host: false } member && (member.Eliminations > 0 || member.RoundsWon > 0))
        {
            _gone[link.Key] = (member.Eliminations, member.RoundsWon);
        }

        State.Members.RemoveAll(m => m.Id == link.Peer && !m.Host);
        _said.Remove(link.Peer);
        Touch();
    }

    private void Receive(ClientLink link, ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> packet = data.Span;
        switch (NetProtocol.TypeOf(packet))
        {
            case MessageType.LobbyRequest when LobbyRequest.Read(packet) is { } ask:
                Ask(link.Peer, ask.Ask, ask.Value);
                break;
            case MessageType.Chat when ChatMessage.ReadSay(packet) is { } say:
                Say(link.Peer, say.Text, say.TeamOnly);
                break;
            case MessageType.Kit when KitMessage.Read(packet) is { } kit:
                SetKit(link.Peer, kit);
                break;
        }
    }

    /// <summary>A kit as the catalogue allows it (each slot one of its own items), or as it came without a catalogue.</summary>
    private Pb.Sim.Gear.Loadout? Checked(Pb.Sim.Gear.Loadout? kit) => kit is null ? null : _gear?.Normalised(kit) ?? kit;

    private string Clean(string text)
    {
        var line = new System.Text.StringBuilder(Math.Min(text.Length, _settings.ChatMostChars));
        foreach (char c in text)
        {
            if (line.Length >= _settings.ChatMostChars)
            {
                break;
            }

            line.Append(char.IsControl(c) ? ' ' : c);
        }

        return line.ToString().Trim();
    }

    private MatchModeKind KindOf(string modeId) => _rules.FindMode(modeId)?.Kind ?? MatchModeKind.Solo;

    private void Touch()
    {
        _dirty = true;
        Changed?.Invoke();
    }

    private void Send(double now)
    {
        foreach (LobbyMember m in State.Members)
        {
            if (!m.Host && _server.Clients.FirstOrDefault(c => c.Peer == m.Id) is { } link)
            {
                float roundTrip = link.RoundTrip > 0f ? link.RoundTrip : _server.TransportRoundTrip(link);
                m.Ping_ms = (int)MathF.Round(roundTrip * 1000f);
            }
        }

        State.TimeLeft = State.Phase is LobbyPhase.Countdown or LobbyPhase.Vote ? (float)Math.Max(0.0, _deadline - now) : 0f;
        _writer.Reset();
        State.Write(_writer);
        _server.Broadcast(_writer.Finish());
        _sentAt = now;
        _dirty = false;
    }
}
