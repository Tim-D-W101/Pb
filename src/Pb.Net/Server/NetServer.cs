using Pb.Net.Packing;
using Pb.Net.Protocol;
using Pb.Net.Transport;
using Pb.Sim;
using Pb.Sim.Events;
using Pb.Sim.Players;

namespace Pb.Net.Server;

/// <summary>What a server tells players who join, and checks them against.</summary>
public sealed record ServerIdentity(string Name, string Build, string DataHash, string Password = "");

/// <summary>
/// The server's side of the network, without the engine: it lets players in (same protocol, build and data, the password
/// if any, room for them), takes their commands, and after each sim step sends each of them a snapshot (their own state
/// whole, everyone else quantised, both as differences from one they confirmed) carrying the events they haven't
/// confirmed yet. The host's loop asks it for each remote player's command, steps the sim, then calls
/// <see cref="AfterStep"/>.
/// </summary>
public sealed class NetServer : IDisposable
{
    /// <summary>Commands run two to a tick before that's logged (two seconds of a clock running at double speed), and between logs after.</summary>
    private const int MergesBeforeLog = 240;
    private const int MergesBetweenLogs = 1200;

    private const int WorldRing = ClientLink.Ring;

    private readonly ITransport _transport;
    private readonly Func<double> _clock;
    private readonly float _dt;
    private readonly List<ClientLink> _clients = new();
    private readonly BitWriter _writer = new(2048);
    private readonly EventLog _log = new();
    private uint[] _zeros = new uint[OwnFields.Count];
    private uint[][] _world = Array.Empty<uint[]>();
    private int[] _worldTicks = Array.Empty<int>();
    private WorldFields? _fields;
    private int _round = -1;

    public NetServer(ITransport transport, NetSettings settings, ServerIdentity identity, Func<double> clock, float dt)
    {
        _transport = transport;
        Settings = settings;
        Identity = identity;
        _clock = clock;
        _dt = dt;
    }

    public NetSettings Settings { get; }

    public ServerIdentity Identity { get; set; }

    /// <summary>The name the host plays under in their own game, which nobody joining may take too.</summary>
    public string? HostName { get; set; }

    /// <summary>A dedicated server: nobody of its own plays, so every place is for someone joining.</summary>
    public bool Dedicated { get; set; }

    public IReadOnlyList<ClientLink> Clients => _clients;

    /// <summary>The round under way (−1: none).</summary>
    public int Round => _round;

    /// <summary>Everything sent so far (bytes).</summary>
    public long BytesSent { get; private set; }

    /// <summary>
    /// Whether players' shots are checked against where they saw everyone (on by default; off only to show what it does,
    /// in tests and with <c>--no-lag-compensation</c>).
    /// </summary>
    public bool LagCompensation { get; set; } = true;

    /// <summary>A player got in (past the checks).</summary>
    public event Action<ClientLink>? Joined;

    /// <summary>A player went (hung up, timed out or was removed).</summary>
    public event Action<ClientLink>? Left;

    /// <summary>A reliable message for the host to deal with (lobby requests, chat), the type byte first.</summary>
    public event Action<ClientLink, ReadOnlyMemory<byte>>? Message;

    /// <summary>Something a player sent that the server dropped, for the host's log.</summary>
    public event Action<ClientLink, string>? Violation;

    /// <summary>Someone turned away as they tried to join (the name they gave, and what they were told), for the host's log.</summary>
    public event Action<string, string>? Refused;

    /// <summary>The connection's own measure of the round trip to a player (s; 0 if it has none).</summary>
    public float TransportRoundTrip(ClientLink link) => Math.Max(0f, _transport.RoundTrip(link.Peer));

    public ClientLink? LinkOf(int playerId)
    {
        foreach (ClientLink c in _clients)
        {
            if (c.PlayerId == playerId && playerId >= 0)
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>Takes in everything that has arrived; drops players not heard from for too long.</summary>
    public void Poll()
    {
        double now = _clock();
        while (_transport.Poll(out TransportEvent e))
        {
            switch (e.Kind)
            {
                case TransportEventKind.Connected:
                    _clients.Add(new ClientLink(e.Peer, Settings, _dt, now));
                    break;
                case TransportEventKind.Disconnected:
                    if (Find(e.Peer) is { } gone)
                    {
                        Drop(gone, notify: false);
                    }

                    break;
                case TransportEventKind.Data when Find(e.Peer) is { } link:
                    link.LastHeard = now;
                    Receive(link, e.Channel, e.Data);
                    break;
            }
        }

        // Silence drops someone only while they play a round they've built: building one, or between rounds, a copy needn't
        // send anything (the connection itself notices one that has gone).
        for (int i = _clients.Count - 1; i >= 0; i--)
        {
            ClientLink c = _clients[i];
            bool playing = _round >= 0 && c.PlayerId >= 0 && c.LoadedRound == _round;
            if (playing && now - c.LastHeard > Settings.DropAfter)
            {
                Drop(c, notify: true);
            }
        }
    }

    /// <summary>Sends a reliable message to one player.</summary>
    public void Send(ClientLink link, ReadOnlySpan<byte> message)
    {
        _transport.Send(link.Peer, NetChannel.Reliable, message);
        Count(link, message.Length);
    }

    /// <summary>Sends a reliable message to every player let in.</summary>
    public void Broadcast(ReadOnlySpan<byte> message)
    {
        foreach (ClientLink c in _clients)
        {
            if (c.Welcomed)
            {
                Send(c, message);
            }
        }
    }

    /// <summary>Sends a player away with a reason they're shown.</summary>
    public void Remove(ClientLink link, RefusedReason reason, string text)
    {
        _writer.Reset();
        SimpleMessage.WriteBye(_writer, reason, text);
        Send(link, _writer.Finish());
        Drop(link, notify: true);
    }

    /// <summary>
    /// A round begins on <paramref name="sim"/> (built, its players added in roster order, the match started): every player
    /// let in is sent the setup, naming the player they play (<paramref name="playerOf"/>; −1 to watch).
    /// </summary>
    public void BeginRound(SimWorld sim, RoundSetupMessage setup, Func<ClientLink, int> playerOf)
    {
        _round = setup.Round;
        _fields = new WorldFields(sim.Players.Count, sim.Doors.Count, Grid(sim));
        _zeros = new uint[Math.Max(OwnFields.Count, _fields.Count)];
        _world = new uint[WorldRing][];
        _worldTicks = new int[WorldRing];
        for (int i = 0; i < WorldRing; i++)
        {
            _world[i] = new uint[_fields.Count];
            _worldTicks[i] = -1;
        }

        _log.Clear();
        foreach (ClientLink c in _clients)
        {
            if (!c.Welcomed)
            {
                continue;
            }

            c.ResetForRound();
            c.PlayerId = playerOf(c);
            setup.YourPlayerId = c.PlayerId;
            _writer.Reset();
            setup.Write(_writer);
            Send(c, _writer.Finish());
        }

        setup.YourPlayerId = -1;
    }

    /// <summary>Whether every player in the round has said they have it built.</summary>
    public bool AllLoaded()
    {
        foreach (ClientLink c in _clients)
        {
            if (c.Welcomed && c.PlayerId >= 0 && c.LoadedRound != _round)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The command a remote player runs this tick (<paramref name="tick"/>, before the step), with its lag compensation:
    /// how far behind they were seeing. False if none has come yet: they stand still.
    /// </summary>
    public bool TryCommand(int playerId, int tick, out InputCommand command)
    {
        ClientLink? link = LinkOf(playerId);
        if (link is null || !link.Commands.TryNext(out command, out int viewTick))
        {
            command = default;
            return false;
        }

        // Two commands run in one tick, for seconds on end: a copy whose clock runs fast (a hiccup's backlog is gone in a moment).
        int merged = link.Commands.Merged - link.MergesLogged;
        if (merged >= (link.MergesLogged == 0 ? MergesBeforeLog : MergesBetweenLogs))
        {
            link.MergesLogged = link.Commands.Merged;
            Flag(link, "commands faster than the clock (run two to a tick)");
        }

        command.Tick = tick;
        command.Rewind = viewTick < 0 || !LagCompensation ? (byte)0 : (byte)Math.Clamp(tick - viewTick, 0, PlayerHitboxes.HistoryTicks - 1);
        return true;
    }

    /// <summary>An event of the host's own (a bot's callout) to send with the sim's.</summary>
    public void AddEvent(in SimEvent e)
    {
        if (_fields is not null)
        {
            _log.Add(e);
        }
    }

    /// <summary>After each sim step: files the step's events, and on a sending tick sends everyone their snapshot.</summary>
    public void AfterStep(SimWorld sim)
    {
        if (_fields is null)
        {
            return;
        }

        ReadOnlySpan<SimEvent> events = sim.Events.Items;
        for (int i = 0; i < events.Length; i++)
        {
            if (EventCodec.Carried(events[i].Type))
            {
                _log.Add(events[i]);
            }
        }

        int tick = sim.Tick - 1;
        if (tick % Math.Max(1, Settings.SendEveryTicks) != 0)
        {
            return;
        }

        int slot = (tick / Math.Max(1, Settings.SendEveryTicks)) % WorldRing;
        _worldTicks[slot] = tick;
        _fields.Capture(sim, _world[slot]);
        foreach (ClientLink c in _clients)
        {
            if (c.Welcomed)
            {
                SendSnapshot(sim, c, tick, _world[slot]);
            }
        }
    }

    /// <summary>The round is over: everyone is sent the result and the numbers the server kept.</summary>
    public void EndRound(SimWorld sim)
    {
        if (sim.Match is not { } match)
        {
            return;
        }

        var over = new RoundOverMessage { Round = _round, Result = match.Result, Elapsed = match.Elapsed, EndTick = match.EndTick };
        foreach (Pb.Sim.Match.PlayerStats s in match.Stats)
        {
            over.Stats.Add(new StatsEntry(s.PlayerId, s.Shots, s.Hits, s.Eliminations, s.Pickups, s.TimeIn, s.OutTick));
        }

        _writer.Reset();
        over.Write(_writer);
        Broadcast(_writer.Finish());
    }

    public void Dispose() => _transport.Dispose();

    /// <summary>The position grid both ends use for a round's level (or open ground, without one).</summary>
    public static PositionQuant Grid(SimWorld sim) =>
        new(sim.Level?.Bounds ?? new Pb.Sim.Collision.Aabb(new System.Numerics.Vector3(-200f), new System.Numerics.Vector3(200f)));

    private void SendSnapshot(SimWorld sim, ClientLink c, int tick, uint[] world)
    {
        WorldFields fields = _fields!;
        double now = _clock();
        int baseTick = -1;
        uint[]? worldBase = null;
        OwnRecord? ownBase = null;
        if (c.AckedTick >= 0 && c.AckedTick < tick)
        {
            int baseSlot = (c.AckedTick / Math.Max(1, Settings.SendEveryTicks)) % WorldRing;
            ownBase = c.Record(c.AckedTick);
            if (_worldTicks[baseSlot] == c.AckedTick && ownBase is not null)
            {
                baseTick = c.AckedTick;
                worldBase = _world[baseSlot];
            }
            else
            {
                ownBase = null;
            }
        }

        PlayerState? own = c.PlayerId >= 0 ? sim.FindPlayer(c.PlayerId) : null;
        OwnRecord record = c.Slot(tick);
        record.Tick = tick;
        record.HasOwn = own is not null;
        record.SentAt = now;
        if (own is not null)
        {
            PredictedState state = PredictedState.Capture(own, sim.Time);
            OwnFields.Capture(state, record.Own);
        }

        BitWriter w = _writer;
        w.Reset();
        w.WriteByte((byte)MessageType.Snapshot);
        w.WriteVarUInt((uint)_round);
        w.WriteVarUInt((uint)tick);
        w.WriteVarUInt(baseTick < 0 ? 0u : (uint)(tick - baseTick));
        w.WriteVarInt(c.Commands.LastRun);
        // Their newest command, from the last one run (unclamped: if their queue stalls, their round trip still reads true).
        w.WriteVarUInt((uint)Math.Max(0, c.NewestSeen - c.Commands.LastRun));
        w.WriteByte((byte)Math.Clamp((now - c.NewestAt) * 1000.0, 0.0, 255.0));
        w.WriteBool(record.HasOwn);
        if (record.HasOwn)
        {
            ReadOnlySpan<uint> ownWas = ownBase is { HasOwn: true } ? ownBase.Own : _zeros.AsSpan(0, OwnFields.Count);
            Delta.Write(w, record.Own, ownWas, OwnFields.Widths);
        }

        ReadOnlySpan<uint> baseWorld = worldBase is not null ? worldBase : _zeros.AsSpan(0, fields.Count);

        int puppetFields = PuppetFields.Count;
        for (int i = 0; i < fields.Players; i++)
        {
            int at = i * puppetFields;
            Delta.Write(w, world.AsSpan(at, puppetFields), baseWorld.Slice(at, puppetFields), fields.Widths.Slice(at, puppetFields));
        }

        int rest = fields.Players * puppetFields;
        Delta.Write(w, world.AsSpan(rest), baseWorld.Slice(rest, fields.Count - rest), fields.Widths.Slice(rest));

        // The events they haven't confirmed, as many as fit.
        uint first = Math.Max(c.AckedEvent + 1, _log.Oldest);
        uint last = first - 1;
        w.WriteVarUInt(first);
        uint previous = first - 1;
        for (uint seq = first; seq <= _log.Newest; seq++)
        {
            if (!_log.TryGet(seq, out SimEvent e))
            {
                continue;
            }

            if (w.ByteCount > Settings.PacketBudget)
            {
                break;
            }

            last = seq;
            if (!For(c, e))
            {
                continue;
            }

            w.WriteBool(true);
            w.WriteVarUInt(seq - previous);
            EventCodec.Write(w, e, tick, fields.Grid);
            previous = seq;
        }

        w.WriteBool(false);
        record.LastEvent = last;
        ReadOnlySpan<byte> packet = w.Finish();
        _transport.Send(c.Peer, NetChannel.Unreliable, packet);
        Count(c, packet.Length);
    }

    /// <summary>Whether a player needs this event: not paint on someone else's mask.</summary>
    private static bool For(ClientLink c, in SimEvent e) => e.Type != SimEventType.MaskSprayed || e.TargetId == c.PlayerId;

    private void Receive(ClientLink link, NetChannel channel, ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> packet = data.Span;
        MessageType type = NetProtocol.TypeOf(packet);
        if (!link.Welcomed)
        {
            if (type == MessageType.Hello)
            {
                Greet(link, HelloMessage.Read(packet));
            }

            return;
        }

        switch (type)
        {
            case MessageType.Commands:
                ReceiveCommands(link, packet);
                break;
            case MessageType.Loaded:
                int round = SimpleMessage.ReadLoaded(packet);
                if (round >= 0)
                {
                    link.LoadedRound = round;
                }

                break;
            case MessageType.Bye:
                Drop(link, notify: true);
                break;
            case MessageType.Hello or MessageType.None:
                break;
            default:
                if (channel == NetChannel.Reliable)
                {
                    Message?.Invoke(link, data);
                }

                break;
        }
    }

    private void Greet(ClientLink link, HelloMessage? hello)
    {
        (RefusedReason reason, string text)? refusal = hello is null ? (RefusedReason.Protocol, "That isn't a copy of this game.")
            : hello.Protocol != NetProtocol.Version ? (RefusedReason.Protocol, $"The host speaks version {NetProtocol.Version} and you {hello.Protocol}: update with Play.bat.")
            : hello.Build != Identity.Build || hello.DataHash != Identity.DataHash
                ? (RefusedReason.Version, Dedicated
                    ? $"The server runs build {Identity.Build} and you {hello.Build}: update with Play.bat, and whoever runs the server starts it again."
                    : $"The host runs build {Identity.Build} and you {hello.Build}: both of you update with Play.bat.")
            : Identity.Password.Length > 0 && hello.Password != Identity.Password ? (RefusedReason.Password, "Wrong password.")
            : _clients.Count(c => c.Welcomed) >= Settings.MaxPeople - (Dedicated ? 0 : 1) ? (RefusedReason.Full, "The game is full.")
            : null;
        if (refusal is { } r)
        {
            _writer.Reset();
            new RefusedMessage { Reason = r.reason, Text = r.text }.Write(_writer);
            Send(link, _writer.Finish());
            _transport.Disconnect(link.Peer);
            _clients.Remove(link);
            Refused?.Invoke(hello is null ? "?" : Printable(hello.Name), r.text);
            return;
        }

        link.Welcomed = true;
        link.Name = UniqueName(hello!.Name, link);
        link.Look = hello.Look;
        link.Key = Printable(hello.Key, 64);
        _writer.Reset();
        new WelcomeMessage { ClientId = link.Peer, ServerName = Identity.Name, Name = link.Name }.Write(_writer);
        Send(link, _writer.Finish());
        Joined?.Invoke(link);
    }

    private string UniqueName(string wanted, ClientLink self)
    {
        string name = Printable(wanted);
        if (name.Length == 0)
        {
            name = "Player";
        }

        string candidate = name;
        for (int n = 2; candidate == HostName || _clients.Any(c => c != self && c.Welcomed && c.Name == candidate); n++)
        {
            candidate = $"{name} {n}";
        }

        return candidate;
    }

    /// <summary>Text as given, without anything unprintable (line breaks, control codes), at most <paramref name="most"/> characters.</summary>
    private static string Printable(string given, int most = 24)
    {
        var name = new System.Text.StringBuilder(Math.Min(given.Length, most));
        foreach (char c in given.Trim())
        {
            if (name.Length >= most)
            {
                break;
            }

            if (!char.IsControl(c))
            {
                name.Append(c);
            }
        }

        return name.ToString().Trim();
    }

    private void ReceiveCommands(ClientLink link, ReadOnlySpan<byte> packet)
    {
        var r = new BitReader(packet);
        r.ReadByte();
        int ack = r.ReadVarInt();
        int newest = r.ReadVarInt();
        int count = (int)r.ReadBits(4);
        if (r.Overflowed)
        {
            return;
        }

        double now = _clock();
        for (int k = 0; k < count; k++)
        {
            InputCommand cmd = CommandCodec.Read(ref r, newest - k, out uint viewBehind);
            if (r.Overflowed)
            {
                Flag(link, "a cut-off commands packet");
                return;
            }

            if (link.PlayerId >= 0)
            {
                link.Commands.Receive(newest - k, cmd, ack < 0 ? -1 : ack - (int)viewBehind);
            }
        }

        if (newest > link.NewestSeen)
        {
            link.NewestSeen = newest;
            link.NewestAt = now;
        }

        // Their confirmation: the newest snapshot they have. It's the baseline for the next, and the events it carried are delivered.
        if (ack > link.AckedTick && link.Record(ack) is { } record)
        {
            link.AckedTick = ack;
            link.AckedEvent = Math.Max(link.AckedEvent, record.LastEvent);
            float sample = (float)(now - record.SentAt);
            link.RoundTrip = link.RoundTrip <= 0f ? sample : link.RoundTrip + (sample - link.RoundTrip) * 0.1f;
        }

        if (link.Commands.TooFarAhead > 0 && link.Commands.TooFarAhead % 60 == 1)
        {
            Flag(link, "commands too far ahead of their turn");
        }
    }

    private void Flag(ClientLink link, string what)
    {
        link.Violations++;
        Violation?.Invoke(link, what);
    }

    private void Count(ClientLink link, int bytes)
    {
        BytesSent += bytes;
        link.BytesSent += bytes;
        double now = _clock();
        link.SecondBytes += bytes;
        if (now - link.SecondFrom >= 1.0)
        {
            link.BytesPerSecond = (float)(link.SecondBytes / Math.Max(1e-3, now - link.SecondFrom));
            link.SecondBytes = 0;
            link.SecondFrom = now;
        }
    }

    private ClientLink? Find(int peer)
    {
        foreach (ClientLink c in _clients)
        {
            if (c.Peer == peer)
            {
                return c;
            }
        }

        return null;
    }

    private void Drop(ClientLink link, bool notify)
    {
        if (!_clients.Remove(link))
        {
            return;
        }

        if (notify)
        {
            _transport.Disconnect(link.Peer);
        }

        if (link.Welcomed)
        {
            Left?.Invoke(link);
        }
    }
}
