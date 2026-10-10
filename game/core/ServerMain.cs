using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Pb.Game.Ai;
using Pb.Game.Net;
using Pb.Game.Player;
using Pb.Game.World;
using Pb.Net;
using Pb.Net.Lobby;
using Pb.Net.Protocol;
using Pb.Net.Server;
using Pb.Sim;
using Pb.Sim.AI;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;

namespace Pb.Game.Core;

/// <summary>
/// The dedicated server: the game started with <c>-- --server</c>, headless. It hosts a game that nobody of its own
/// plays in, set up from server.jsonc (<see cref="ServerConfig"/>).
/// <list type="bullet">
/// <item>Between rounds it keeps the lobby. The countdown starts once everyone in is ready, or once the lobby's wait
/// has passed since the first person readied up.</item>
/// <item>Each round it builds only what walking and paint need: no dressing, sound or HUD. It casts the round with
/// everyone in and bots for the places left, sends it, waits for every copy to build it, shows the briefing, and runs
/// it.</item>
/// <item>After the summary it moves on to the rotation's next round, or to the vote. A speedball match goes point
/// after point instead, everyone still in, until a side has won it.</item>
/// </list>
/// It logs who joined and left, each round and how it went, and anything it dropped (<see cref="ServerLog"/>).
/// <c>--host-wait=N</c> starts the first countdown once N people are in (and the next ones with whoever's in);
/// <c>--rounds=N</c> stops after N rounds; and <c>--time-limit=S</c> shortens rounds (CI).
/// </summary>
public partial class ServerMain : Node3D, ISimEventListener
{
    /// <summary>Where the rotation has got to, and the rounds served, across the scene being built afresh each round.</summary>
    private static int _rotation;
    private static int _served;
    private bool _autoStarted;

    private readonly List<ClientLink> _people = new();
    private readonly List<int> _members = new();
    private readonly List<BotBrain> _bots = new();
    private readonly List<ServerPawn> _pawns = new();
    private readonly Dictionary<ClientLink, long> _bytesAtLive = new();
    private GameData _data = null!;
    private PresentationDef _view = null!;
    private ServerConfig _config = null!;
    private NetSession _session = null!;
    private LobbyHost _lobby = null!;
    private NetServer _server = null!;
    private SimDriver _driver = null!;
    private SimWorld? _sim;
    private MatchState? _match;
    private BotSquad? _squad;
    private CastRound? _cast;
    private DoorBodies? _doors;
    private int[] _calloutsSent = Array.Empty<int>();
    private double _readySince = -1;
    private double _waited;
    private double _briefing = -1;
    private double _summaryLeft = -1;
    private double _liveAt;
    private bool _overSent;
    private bool _scored;
    private bool _moving;
    private string _botTier = "normal";

    public override void _Ready()
    {
        _driver = GetNode<SimDriver>("SimDriver");
        try
        {
            var source = new GodotDataSource();
            _data = GameData.Load(source);
            _view = Jsonc.Load<PresentationDef>(source, PresentationDef.File);
            _config = ServerConfig.Load(_data);
        }
        catch (DataException ex)
        {
            ServerLog.Line($"can't start: {ex.Message}");
            GetTree().Quit(1);
            return;
        }

        try
        {
            _session = NetSession.Current is { Dedicated: true } running ? running : Serve();
        }
        catch (InvalidOperationException ex)
        {
            ServerLog.Line($"can't start: {ex.Message}");
            GetTree().Quit(1);
            return;
        }

        _lobby = _session.Lobby!;
        _server = _session.Server!;
        _session.InRound = false;
        _lobby.CountdownFinished += BuildRound;
        _server.Left += OnLeft;
        if (_lobby.Phase == LobbyPhase.Loading && _lobby.MatchOn)
        {
            // A speedball match's next point, straight after the last.
            Callable.From(BuildRound).CallDeferred();
        }
    }

    public override void _ExitTree()
    {
        if (_lobby is not null)
        {
            _lobby.CountdownFinished -= BuildRound;
        }

        if (_server is not null)
        {
            _server.Left -= OnLeft;
        }

        StopNetwork();
    }

    /// <summary>Starts serving: the game named and on the port server.jsonc says, the lobby on the rotation's first round.</summary>
    private NetSession Serve()
    {
        var identity = new ServerIdentity(_config.Name, BuildStamp.Build, BuildStamp.DataHash, _config.Password);
        LobbyChoices first = _config.Rotation[_rotation % _config.Rotation.Count];
        NetSession session = NetSession.Serve(GetTree(), NetStart.Settings(), identity, _config.Port, _data.Config.Rules, first, _config.MaxPeople, _data.Gear);
        session.Announce(() => NetStart.Announcement(_data, session, _config.Port));
        ServerLog.Line($"settings from {_config.Source}: {_config.Rotation.Count} round{(_config.Rotation.Count == 1 ? "" : "s")} in turn, " +
                       $"bots {(_config.Bots ? "on" : "off")}, vote {(_config.Vote ? "on" : "off")}, password {(_config.Password.Length > 0 ? "set" : "none")}");
        foreach (string warning in _config.Warnings)
        {
            ServerLog.Line(warning);
        }
        return session;
    }

    public override void _Process(double delta)
    {
        if (_lobby is null || _moving)
        {
            return;
        }

        if (_sim is null)
        {
            WaitInLobby();
            return;
        }

        if (_match is { Phase: MatchPhase.Briefing })
        {
            Brief(delta);
        }
        else if (_match is { Phase: MatchPhase.Ended } && _summaryLeft >= 0)
        {
            _summaryLeft -= delta;
            if (_summaryLeft < 0)
            {
                NextRound();
            }
        }
    }

    /// <summary>Between rounds: the countdown starts once everyone's ready (the lobby does that), or the lobby's wait after the first readied.</summary>
    private void WaitInLobby()
    {
        if (_lobby.Phase != LobbyPhase.Lobby)
        {
            _readySince = -1;
            return;
        }

        double now = _session.Now;
        int people = _lobby.State.Members.Count;
        // --host-wait=N (CI): the first countdown once N people are in; after a round, straight away with whoever's still in.
        if (!_autoStarted && Args.Ticks("--host-wait", 0) is > 0 and int wanted && (people >= wanted || (_lobby.State.RoundsPlayed > 0 && people > 0)))
        {
            _autoStarted = true;
            ServerLog.Line($"{people} in: counting down");
            _lobby.Start();
            return;
        }

        bool anyReady = _lobby.State.Members.Any(m => m.Ready);
        _readySince = !anyReady ? -1 : _readySince < 0 ? now : _readySince;
        if (_config.LobbyWait > 0 && _readySince >= 0 && now - _readySince >= _config.LobbyWait)
        {
            ServerLog.Line($"{_config.LobbyWait:0} s since the first ready: counting down");
            _lobby.Start();
        }
    }

    /// <summary>The countdown ran out: the round is cast with everyone in, built and sent.</summary>
    private void BuildRound()
    {
        ChosenRound chosen = RoundChoices.Resolve(_data, _lobby.State.Choices);
        var people = new List<Person>();
        _people.Clear();
        _members.Clear();
        foreach (LobbyMember m in _lobby.State.Members)
        {
            if (_server.Clients.FirstOrDefault(c => c.Welcomed && c.Peer == m.Id) is not { } link)
            {
                continue;
            }

            people.Add(new Person(m.Name, m.Look, m.Side, m.Kit));
            _people.Add(link);
            _members.Add(m.Id);
        }

        bool alone = !_config.Bots && chosen.Mode.Kind != MatchModeKind.Solo && people.Count < 2;
        if (people.Count == 0 || alone)
        {
            ServerLog.Line(people.Count == 0 ? "nobody in: back to the lobby" : "bots are off and one person can't play alone: back to the lobby");
            _lobby.BackToLobby();
            return;
        }

        ulong seed = ulong.TryParse(Args.Value("--seed"), out ulong given) ? given : (ulong)System.Random.Shared.NextInt64();
        LevelLayout level = chosen.Area.ForPlace(chosen.Place);
        var sim = new SimWorld(_data.Config, seed);
        sim.LoadLevel(level);
        sim.Collision.SkipDynamic = true;
        BotSquad squad = BotSquad.ForLevel(sim, _data.Bots, level);
        int largest = Math.Max(people.Count(p => p.Side == 0), people.Count(p => p.Side == 1));
        int size = RoundCasting.FitSize(chosen.Mode, chosen.Size, people.Count, _data.Config.Rules.MaxPlayers, largest);
        float? limit = float.TryParse(Args.Value("--time-limit"), NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds) ? seconds : null;
        CastRound cast = RoundCasting.Cast(level, squad.Cover, sim.Collision, _data.Config, _data.Bots, chosen.Mode, size, chosen.Objective, chosen.Tier,
            people, seed, _session.RoundsPlayed + 1, _view.Hud.Callsigns, chosen.Entry.Id, chosen.Place.Whole ? null : chosen.Place.Id, limit,
            fillWithBots: _config.Bots, match: _lobby.Speedball ? _lobby.Score : null);
        RoundSetupMessage setup = cast.Setup;
        foreach (RosterEntry e in setup.Roster)
        {
            sim.AddPlayer(e.PlayerId, e.Team, e.Position, e.Yaw).Name = e.Name;
        }

        _sim = sim;
        _squad = squad;
        _cast = cast;
        var world = new LevelBuilder { Name = "World" };
        AddChild(world);
        world.BuildWalking(level);
        _doors = new DoorBodies { Name = "Doors" };
        AddChild(_doors);
        _doors.Build(sim);
        for (int i = 0; i < setup.Roster.Count; i++)
        {
            RosterEntry entry = setup.Roster[i];
            PlayerState state = sim.FindPlayer(entry.PlayerId)!;
            ICommandSource pilot;
            if (entry.Person)
            {
                pilot = new NetCommandSource(_server);
            }
            else
            {
                OpponentSpawn spawn = cast.BotStarts[i]!;
                BotBrain brain = squad.Add(state, _data.Bots.ArchetypeFor(spawn.Roles)!, _data.Bots.Difficulty[chosen.Tier.Bots], spawn);
                brain.RestlessAfter = chosen.Mode.RestlessAfter;
                _bots.Add(brain);
                pilot = new BotPilot(brain);
            }

            var pawn = new ServerPawn { Name = $"{(entry.Person ? "Person" : "Bot")}_{entry.PlayerId}" };
            AddChild(pawn);
            pawn.Initialize(sim, state, pilot);
            _pawns.Add(pawn);
        }

        _calloutsSent = Enumerable.Repeat(-1, _bots.Count).ToArray();
        _botTier = chosen.Tier.Bots;
        MatchState match = sim.StartMatch(RoundWorld.MatchSetupOf(_data.Config, setup));
        _match = match;
        sim.Collision.SkipDynamic = false;
        _driver.Initialize(sim);
        DoorBodies doors = _doors;
        _driver.Ticked += _ => doors.Capture();
        foreach (PlayerState p in sim.Players)
        {
            _driver.AddDriver(_pawns.First(o => o.State == p));
        }

        _driver.AddListener(this);
        _server.LagCompensation = true;
        _driver.BeforeTick = _server.Poll;
        _driver.AfterStep = () =>
        {
            SendCallouts();
            _server.AfterStep(sim);
            if (match.Phase == MatchPhase.Ended && !_overSent)
            {
                _overSent = true;
                _server.EndRound(sim);
            }
        };
        _session.InRound = true;
        _server.BeginRound(sim, setup, link =>
        {
            int at = _people.IndexOf(link);
            return at >= 0 ? cast.PersonIds[at] : -1;
        });
        _session.Round = setup;
        _lobby.RoundStarted();
        ServerLog.Line($"round {setup.Round}: {chosen.Where} · {new Pb.Game.Ui.RoundInfo(level, chosen.Tier, chosen.Mode, size, chosen.Objective).Line} " +
                       $"with {string.Join(", ", setup.Roster.Where(e => e.Person).Select(e => e.Name))} and {setup.Roster.Count(e => !e.Person)} bots (seed {seed})");
    }

    /// <summary>The briefing: it waits for every copy to have the round built (or as long as it may), then shows for a moment and goes live.</summary>
    private void Brief(double delta)
    {
        NetSettings settings = _session.Settings;
        if (_briefing < 0)
        {
            _waited += delta;
            if (!_server.AllLoaded() && _waited < settings.LoadTimeout)
            {
                return;
            }

            string late = string.Join(", ", _people.Where(l => l.PlayerId >= 0 && l.LoadedRound != _server.Round).Select(l => l.Name));
            ServerLog.Line(late.Length == 0 ? $"everyone has the round built after {_waited:0.0} s" : $"starting without {late} after {_waited:0} s");
            _briefing = settings.Briefing;
        }

        _briefing -= delta;
        if (_briefing <= 0)
        {
            _sim!.GoLive();

            // What each player is sent from here on, for the round's traffic in the log.
            _liveAt = _session.Now;
            _bytesAtLive.Clear();
            foreach (ClientLink link in _server.Clients.Where(c => c.Welcomed))
            {
                _bytesAtLive[link] = link.BytesSent;
            }
        }
    }

    public void OnSimEvent(in SimEvent e)
    {
        _squad?.Hear(e);
        if (e.Type == SimEventType.PlayerEliminated && e.Extra >= 0 && _sim?.FindPlayer(e.TargetId) is { } victim &&
            _pawns.FirstOrDefault(p => p.State == victim)?.Pilot is NetCommandSource person)
        {
            // A person who's out walks off the field the way the bots go.
            var spot = new OpponentSpawn { Id = $"out_{victim.Id}", Position = victim.Position, Yaw = victim.Yaw, Roles = new[] { "sentry" } };
            person.WalkOff = new BotPilot(_squad!.Add(victim, _data.Bots.ArchetypeFor(spot.Roles)!, _data.Bots.Difficulty[_botTier], spot));
        }
        else if (e.Type == SimEventType.MatchPhaseChanged && e.Extra == (int)MatchPhase.Live)
        {
            ServerLog.Line($"round {_session.Round?.Round} live");
        }
        else if (e.Type == SimEventType.RoundEnded)
        {
            RoundOver();
        }
    }

    /// <summary>The round's over: its result in the log and the session's score, then the summary's time.</summary>
    private void RoundOver()
    {
        if (_scored || _match is null || _cast is null || _sim is null)
        {
            return;
        }

        _scored = true;
        var people = new List<PersonInRound>();
        for (int i = 0; i < _cast.PersonIds.Count && i < _members.Count; i++)
        {
            int playerId = _cast.PersonIds[i];
            people.Add(new PersonInRound(_members[i], playerId, _sim.FindPlayer(playerId)?.Team ?? -1));
        }

        var stats = _match.Stats.Select(p => new StatsEntry(p.PlayerId, p.Shots, p.Hits, p.Eliminations, p.Pickups, p.TimeIn, p.OutTick)).ToList();
        _lobby.RoundOver(_match.Result, people, stats);
        string winner = _match.Result.Winner < 0 ? "nobody" : $"side {_match.Result.Winner}";
        ServerLog.Line($"round {_session.Round?.Round} over: {_match.Result.Reason}, won by {winner} after {_match.Elapsed:0} s; " +
                       string.Join(", ", _sim.Players.Select(p => $"{p.Name} {p.Eliminations} out/{p.Hits} hits")));
        // Every copy prints the round as it ended there; CI compares these lines with the joiners'.
        GD.Print($"NET RESULT round {_session.Round?.Round}: {_match.Result.Reason} won by {_match.Result.Winner} at tick {_match.EndTick}; " +
                 string.Join(" ", _match.Stats.Select(p => $"{p.PlayerId}:{p.Shots}/{p.Hits}/{p.Eliminations}/{p.Pickups}/{p.OutTick}")));
        // Each player's connection: what they were sent while the round was live (KB/s, a KB being 1,000 bytes), how their
        // commands came, and how fast they fired against the cap (CI checks both).
        double live = Math.Max(1.0, _session.Now - _liveAt);
        foreach (ClientLink link in _server.Clients.Where(c => c.Welcomed))
        {
            long from = _bytesAtLive.TryGetValue(link, out long atLive) ? atLive : link.BytesSent;
            double rate = (link.BytesSent - from) / 1000.0 / live;
            string shots = link.PlayerId >= 0 && _match.StatsFor(link.PlayerId) is { } s
                ? $"{s.Shots} shots in {s.TimeIn:0.0} s ({(s.TimeIn > 0f ? s.Shots / s.TimeIn : 0f):0.0}/s, cap {_data.Config.Fire.RateCap:0.0}/s)"
                : "watching";
            ServerLog.Line($"  {link.Name}: round trip {link.RoundTrip * 1000f:0} ms, {rate:0.0} KB/s live, commands missing {link.Commands.Missing}, " +
                           $"late {link.Commands.Late}, too far ahead {link.Commands.TooFarAhead} ({link.Commands.Skipped} skipped), merged {link.Commands.Merged}, " +
                           $"violations {link.Violations}, {shots}");
        }

        // Between a speedball match's points, the rules' wait; after a round (or the match), the summary's.
        _summaryLeft = _lobby.MatchOn ? _data.Config.Rules.Speedball.BetweenPoints : Math.Max(0.5, _config.Summary);
        if (_lobby.Speedball)
        {
            ServerLog.Line($"match: {_lobby.State.MatchPoints[0]}–{_lobby.State.MatchPoints[1]} after {_lobby.State.MatchPlayed} point" +
                           $"{(_lobby.State.MatchPlayed == 1 ? "" : "s")}, first to {_lobby.RaceTo}{(_lobby.MatchOn ? "" : ": won")}");
        }
    }

    /// <summary>
    /// After the summary: the rotation's next round (or the vote), everyone back in the lobby, the scene built afresh. A
    /// speedball match still on has its next point built instead (it counts as one round served, once it's won).
    /// </summary>
    private void NextRound()
    {
        _moving = true;
        RoundSetupMessage? setup = _session.Round;
        if (_lobby.MatchOn)
        {
            _session.RoundsPlayed = setup?.Round ?? _session.RoundsPlayed;
            _lobby.NextPoint();
            StopNetwork();
            GetTree().ReloadCurrentScene();
            return;
        }

        _served++;
        if (Args.Ticks("--rounds", 0) is > 0 and int most && _served >= most)
        {
            ServerLog.Line($"{_served} round{(_served == 1 ? "" : "s")} served: stopping");
            StopNetwork();
            _session.Leave("The server is stopping.");
            GetTree().CreateTimer(0.5).Timeout += () => GetTree().Quit(0);
            return;
        }

        _rotation = (_rotation + 1) % _config.Rotation.Count;
        _session.RoundsPlayed = setup?.Round ?? _session.RoundsPlayed;
        IReadOnlyList<VoteOption>? options = _config.Vote
            ? RoundChoices.VoteOptions(_data, _lobby.State.Choices, _session.Settings.VoteOptions, (ulong)System.Random.Shared.NextInt64())
            : null;
        _lobby.BackToLobby(options);
        if (!_config.Vote)
        {
            _lobby.SetChoices(_config.Rotation[_rotation]);
        }

        StopNetwork();
        GetTree().ReloadCurrentScene();
    }

    /// <summary>Someone left mid-round: they count as out and the round goes on without them.</summary>
    private void OnLeft(ClientLink link)
    {
        if (_sim is not null && link.PlayerId >= 0 && _match is { Phase: not MatchPhase.Ended })
        {
            _sim.Withdraw(link.PlayerId);
            ServerLog.Line($"{link.Name} left mid-round: player {link.PlayerId} is out");
        }

        _people.Remove(link);
    }

    /// <summary>The bots' new callouts go to everyone with the step's events (the lines the game would play).</summary>
    private void SendCallouts()
    {
        for (int i = 0; i < _bots.Count; i++)
        {
            BotBrain bot = _bots[i];
            if (bot.CalloutTick <= _calloutsSent[i])
            {
                continue;
            }

            _calloutsSent[i] = bot.CalloutTick;
            string[] lines = _view.Hud.Callouts.For(bot.Callout);
            if (lines.Length > 0)
            {
                _server.AddEvent(new SimEvent
                {
                    Type = NetEventTypes.Callout, Tick = _sim!.Tick - 1, PlayerId = bot.Self.Id, Extra = (int)bot.Callout,
                    TargetId = (bot.Self.Id * 31 + bot.CalloutTick) % lines.Length, Position = bot.Self.EyePosition, ColliderId = -1,
                });
            }
        }
    }

    private void StopNetwork()
    {
        if (_driver is not null)
        {
            _driver.BeforeTick = null;
            _driver.AfterStep = null;
        }

        if (_session is not null)
        {
            _session.InRound = false;
        }
    }
}
