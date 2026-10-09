using Pb.Net.Packing;
using Pb.Net.Protocol;
using Pb.Net.Transport;
using Pb.Sim.Events;
using Pb.Sim.Players;

namespace Pb.Net.Client;

public enum ClientState : byte
{
    /// <summary>Asked to join; waiting for the server's answer.</summary>
    Joining,

    /// <summary>In: in the lobby or a round.</summary>
    Joined,

    /// <summary>Turned away (see <see cref="NetClient.Refusal"/>).</summary>
    Refused,

    /// <summary>The server went, or sent us away (see <see cref="NetClient.GoneReason"/>).</summary>
    Gone,
}

/// <summary>A snapshot as received: kept to draw others between two of them, and as the baseline the next is sent against.</summary>
internal sealed class ReceivedSnapshot
{
    public int Tick = -1;

    public uint[] World = Array.Empty<uint>();

    public readonly uint[] Own = new uint[OwnFields.Count];

    public bool HasOwn;

    public int LastRun = -1;

    public double ReceivedAt;
}

/// <summary>
/// The player's side of the network, without the engine: it asks to join, takes in snapshots (decoding each against the
/// one it was sent as a difference from), keeps the newest few to draw others between, collects the events in order,
/// keeps the display clock a little behind the server, and sends the latest commands, several to a packet.
/// </summary>
public sealed class NetClient : IDisposable
{
    internal const int Ring = 64;
    private const int SentRing = 256;

    private readonly ITransport _transport;
    private readonly Func<double> _clock;
    private readonly float _tickRate;
    private readonly BitWriter _writer = new(1024);
    private readonly ReceivedSnapshot[] _snapshots = new ReceivedSnapshot[Ring];
    private readonly List<(uint Seq, SimEvent Event)> _events = new();
    private readonly InputCommand[] _commands = new InputCommand[SentRing];
    private readonly int[] _commandSeqs = new int[SentRing];
    private readonly int[] _views = new int[SentRing];
    private readonly double[] _sentAt = new double[SentRing];
    private uint[] _zeros = new uint[OwnFields.Count];
    private WorldFields? _fields;
    private RoundSetupMessage? _setup;
    private RoundOverMessage? _over;
    private int _round = -1;
    private uint _lastEvent;
    private int _newestCommand = -1;
    private int _unsent;
    private double _offset;
    private bool _offsetKnown;
    private bool _ownFresh;
    private int _ownSnapshot = -1;
    private bool _clockSet;

    public NetClient(ITransport transport, NetSettings settings, Func<double> clock, HelloMessage hello, float tickRate)
    {
        _transport = transport;
        Settings = settings;
        _clock = clock;
        _tickRate = tickRate;
        Hello = hello;
        for (int i = 0; i < Ring; i++)
        {
            _snapshots[i] = new ReceivedSnapshot();
        }

        Array.Fill(_commandSeqs, -1);
        JoinedAt = clock();
    }

    public NetSettings Settings { get; }

    public HelloMessage Hello { get; }

    public ClientState State { get; private set; } = ClientState.Joining;

    public WelcomeMessage? Welcome { get; private set; }

    public RefusedMessage? Refusal { get; private set; }

    /// <summary>Why we're out, once <see cref="State"/> is <see cref="ClientState.Gone"/>.</summary>
    public string GoneReason { get; private set; } = "";

    /// <summary>The round trip to the server (s) and how much it varies (s).</summary>
    public float RoundTrip { get; private set; }

    public float Jitter { get; private set; }

    /// <summary>The newest snapshot's tick (−1: none this round).</summary>
    public int NewestTick { get; private set; } = -1;

    /// <summary>
    /// The server tick everyone else is drawn at (fractional): a little behind the newest snapshot, so there's always a
    /// snapshot either side of it.
    /// </summary>
    public double RenderTick { get; private set; } = -1;

    /// <summary>Bytes received so far.</summary>
    public long BytesReceived { get; private set; }

    /// <summary>Snapshots that couldn't be read (their baseline gone) and dropped.</summary>
    public int Undecodable { get; private set; }

    /// <summary>A reliable message for the game to deal with (lobby, chat), its type byte first.</summary>
    public event Action<ReadOnlyMemory<byte>>? Message;

    internal double JoinedAt { get; }

    internal WorldFields? Fields => _fields;

    internal List<(uint Seq, SimEvent Event)> Events => _events;

    /// <summary>Sends the request to join.</summary>
    public void Join()
    {
        _writer.Reset();
        Hello.Write(_writer);
        _transport.Send(0, NetChannel.Reliable, _writer.Finish());
    }

    /// <summary>A new round's setup, once (null if none has come since the last call).</summary>
    public RoundSetupMessage? TakeRoundSetup()
    {
        RoundSetupMessage? setup = _setup;
        _setup = null;
        return setup;
    }

    /// <summary>The round's result and numbers, once.</summary>
    public RoundOverMessage? TakeRoundOver()
    {
        RoundOverMessage? over = _over;
        _over = null;
        return over;
    }

    /// <summary>A round is starting on this copy: snapshots from now on are decoded with its layout.</summary>
    public void BeginRound(int round, WorldFields fields)
    {
        _round = round;
        _fields = fields;
        _zeros = new uint[Math.Max(OwnFields.Count, fields.Count)];
        foreach (ReceivedSnapshot s in _snapshots)
        {
            s.Tick = -1;
            s.World = new uint[fields.Count];
            s.HasOwn = false;
        }

        _events.Clear();
        _lastEvent = 0;
        NewestTick = -1;
        RenderTick = -1;
        _clockSet = false;
        _offsetKnown = false;
        _ownFresh = false;
        _ownSnapshot = -1;
        _newestCommand = -1;
        _unsent = 0;
        Array.Fill(_commandSeqs, -1);
    }

    /// <summary>Tells the server this copy has the round built.</summary>
    public void SendLoaded(int round)
    {
        _writer.Reset();
        SimpleMessage.WriteLoaded(_writer, round);
        _transport.Send(0, NetChannel.Reliable, _writer.Finish());
    }

    public void SendReliable(ReadOnlySpan<byte> message) => _transport.Send(0, NetChannel.Reliable, message);

    /// <summary>Leaves the game, telling the server.</summary>
    public void Leave(string why = "left")
    {
        if (State is ClientState.Joined or ClientState.Joining)
        {
            _writer.Reset();
            SimpleMessage.WriteBye(_writer, RefusedReason.Removed, why);
            _transport.Send(0, NetChannel.Reliable, _writer.Finish());
            _transport.Disconnect(0);
        }

        State = ClientState.Gone;
        GoneReason = why;
    }

    public void Dispose() => _transport.Dispose();

    /// <summary>Takes in everything that has arrived.</summary>
    public void Poll()
    {
        while (_transport.Poll(out TransportEvent e))
        {
            switch (e.Kind)
            {
                case TransportEventKind.Disconnected:
                    if (State != ClientState.Refused)
                    {
                        State = ClientState.Gone;
                        GoneReason = GoneReason.Length > 0 ? GoneReason : "The connection to the host was lost.";
                    }

                    break;
                case TransportEventKind.Data:
                    BytesReceived += e.Data.Length;
                    Receive(e.Channel, e.Data);
                    break;
            }
        }

        if (State == ClientState.Joining && _clock() - JoinedAt > Settings.ConnectTimeout)
        {
            State = ClientState.Gone;
            GoneReason = "The host didn't answer.";
        }
    }

    /// <summary>The newest snapshot held (null: none yet).</summary>
    internal ReceivedSnapshot? Newest => Find(NewestTick);

    /// <summary>The newest snapshot's own state, once each: the last command the server ran, and its state after it.</summary>
    internal bool TakeOwn(out int lastRun, out ReadOnlySpan<uint> fields)
    {
        if (!_ownFresh || Find(_ownSnapshot) is not { HasOwn: true } s)
        {
            lastRun = -1;
            fields = default;
            return false;
        }

        _ownFresh = false;
        lastRun = s.LastRun;
        fields = s.Own;
        return true;
    }

    /// <summary>The two snapshots either side of a tick (the newest twice past it), and how far between them it is.</summary>
    internal bool Bracket(double tick, out ReceivedSnapshot a, out ReceivedSnapshot b, out float t)
    {
        ReceivedSnapshot? below = null, above = null, newest = null, second = null;
        foreach (ReceivedSnapshot s in _snapshots)
        {
            if (s.Tick < 0)
            {
                continue;
            }

            if (s.Tick <= tick && (below is null || s.Tick > below.Tick))
            {
                below = s;
            }

            if (s.Tick > tick && (above is null || s.Tick < above.Tick))
            {
                above = s;
            }

            if (newest is null || s.Tick > newest.Tick)
            {
                second = newest;
                newest = s;
            }
            else if (second is null || s.Tick > second.Tick)
            {
                second = s;
            }
        }

        if (below is not null && above is not null)
        {
            a = below;
            b = above;
            t = (float)((tick - below.Tick) / (above.Tick - below.Tick));
            return true;
        }

        if (newest is null)
        {
            a = b = _snapshots[0];
            t = 0f;
            return false;
        }

        if (above is not null)
        {
            // Before anything we hold: the oldest there is.
            a = b = above;
            t = 0f;
            return true;
        }

        // Past the newest: carry on from the last two, for a little while.
        a = second ?? newest;
        b = newest;
        float span = Math.Max(1, b.Tick - a.Tick);
        float beyond = (float)Math.Min(tick - b.Tick, Settings.Extrapolation * _tickRate);
        t = a == b ? 0f : 1f + beyond / span;
        return true;
    }

    /// <summary>
    /// Moves the display clock on by <paramref name="dt"/>, easing it towards a little behind the newest snapshot (the
    /// interpolation delay, plus the jitter measured).
    /// </summary>
    public void AdvanceClock(float dt)
    {
        if (NewestTick < 0)
        {
            return;
        }

        double now = _clock();
        int every = Math.Max(1, Settings.SendEveryTicks);
        double delay = Math.Min(Settings.InterpolationSnapshots * every + Jitter * _tickRate * 2.0, Settings.InterpolationMostSnapshots * every);
        double target = (now - _offset) * _tickRate - delay;
        if (!_clockSet || Math.Abs(target - RenderTick) > _tickRate)
        {
            RenderTick = target;
            _clockSet = true;
            return;
        }

        RenderTick += dt * _tickRate;
        RenderTick += (target - RenderTick) * Math.Min(1.0, dt * 2.0);
    }

    /// <summary>
    /// Keeps this tick's command (as predicted with: quantised) and the server tick it was made seeing, and every
    /// <see cref="NetSettings.SendEveryTicks"/> sends the latest few in one packet.
    /// </summary>
    public void SendCommand(int seq, in InputCommand command, int viewTick)
    {
        int slot = seq & (SentRing - 1);
        _commands[slot] = command;
        _commandSeqs[slot] = seq;
        _views[slot] = viewTick;
        _sentAt[slot] = _clock();
        _newestCommand = Math.Max(_newestCommand, seq);
        if (++_unsent < Math.Max(1, Settings.SendEveryTicks))
        {
            return;
        }

        _unsent = 0;
        int ack = NewestTick;
        _writer.Reset();
        _writer.WriteByte((byte)MessageType.Commands);
        _writer.WriteVarInt(ack);
        _writer.WriteVarInt(_newestCommand);
        int count = 0;
        for (int k = 0; k < Settings.CommandsPerPacket && _commandSeqs[(_newestCommand - k) & (SentRing - 1)] == _newestCommand - k; k++)
        {
            count++;
        }

        _writer.WriteBits((uint)count, 4);
        for (int k = 0; k < count; k++)
        {
            int s = (_newestCommand - k) & (SentRing - 1);
            uint behind = ack < 0 || _views[s] < 0 ? 255u : (uint)Math.Clamp(ack - _views[s], 0, 255);
            CommandCodec.Write(_writer, _commands[s], behind);
        }

        _transport.Send(0, NetChannel.Unreliable, _writer.Finish());
    }

    private void Receive(NetChannel channel, ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> packet = data.Span;
        switch (NetProtocol.TypeOf(packet))
        {
            case MessageType.Welcome when WelcomeMessage.Read(packet) is { } welcome:
                Welcome = welcome;
                State = ClientState.Joined;
                break;
            case MessageType.Refused when RefusedMessage.Read(packet) is { } refused:
                Refusal = refused;
                State = ClientState.Refused;
                break;
            case MessageType.Bye:
                var r = new BitReader(packet);
                r.ReadByte();
                r.ReadByte();
                GoneReason = r.ReadString(200);
                State = ClientState.Gone;
                break;
            case MessageType.RoundSetup when RoundSetupMessage.Read(packet) is { } setup:
                _setup = setup;
                break;
            case MessageType.RoundOver when RoundOverMessage.Read(packet) is { } over:
                _over = over;
                break;
            case MessageType.Snapshot:
                ReadSnapshot(packet);
                break;
            case MessageType.None or MessageType.Welcome or MessageType.Refused or MessageType.RoundSetup or MessageType.RoundOver:
                break;
            default:
                if (channel == NetChannel.Reliable)
                {
                    Message?.Invoke(data);
                }

                break;
        }
    }

    private void ReadSnapshot(ReadOnlySpan<byte> packet)
    {
        if (_fields is not { } fields)
        {
            return;
        }

        var r = new BitReader(packet);
        r.ReadByte();
        int round = (int)r.ReadVarUInt();
        int tick = (int)r.ReadVarUInt();
        uint baseDelta = r.ReadVarUInt();
        int lastRun = r.ReadVarInt();
        uint newestDelta = r.ReadVarUInt();
        byte heldMs = r.ReadByte();
        bool hasOwn = r.ReadBool();
        if (r.Overflowed || round != _round || Find(tick) is not null)
        {
            return;
        }

        ReceivedSnapshot? baseline = null;
        if (baseDelta != 0)
        {
            baseline = Find(tick - (int)baseDelta);
            if (baseline is null)
            {
                Undecodable++;
                return;
            }
        }

        ReceivedSnapshot slot = _snapshots[(tick / Math.Max(1, Settings.SendEveryTicks)) % Ring];
        if (slot == baseline)
        {
            Undecodable++;
            return;
        }

        slot.Tick = -1;
        if (hasOwn)
        {
            ReadOnlySpan<uint> ownBase = baseline is { HasOwn: true } ? baseline.Own : _zeros.AsSpan(0, OwnFields.Count);
            ownBase.CopyTo(slot.Own);
            Delta.Read(ref r, slot.Own, OwnFields.Widths);
        }

        ReadOnlySpan<uint> worldBase = baseline is not null ? baseline.World : _zeros.AsSpan(0, fields.Count);
        worldBase.CopyTo(slot.World);
        int puppet = PuppetFields.Count;
        for (int i = 0; i < fields.Players; i++)
        {
            Delta.Read(ref r, slot.World.AsSpan(i * puppet, puppet), fields.Widths.Slice(i * puppet, puppet));
        }

        int rest = fields.Players * puppet;
        Delta.Read(ref r, slot.World.AsSpan(rest), fields.Widths.Slice(rest));

        uint seq = r.ReadVarUInt() - 1;
        while (r.ReadBool() && !r.Overflowed)
        {
            seq += r.ReadVarUInt();
            if (!EventCodec.Read(ref r, tick, fields.Grid, out SimEvent e))
            {
                break;
            }

            if (seq > _lastEvent)
            {
                _events.Add((seq, e));
                _lastEvent = seq;
            }
        }

        if (r.Overflowed)
        {
            Undecodable++;
            return;
        }

        double now = _clock();
        slot.Tick = tick;
        slot.HasOwn = hasOwn;
        slot.LastRun = lastRun;
        slot.ReceivedAt = now;
        if (tick > NewestTick)
        {
            NewestTick = tick;
            if (hasOwn)
            {
                _ownSnapshot = tick;
                _ownFresh = true;
            }

            // How late the snapshot came against the server's clock: its offset steadies the display clock, and how
            // much it wanders is the jitter.
            double offset = now - tick / (double)_tickRate;
            if (!_offsetKnown)
            {
                _offset = offset;
                _offsetKnown = true;
            }
            else
            {
                double deviation = offset - _offset;
                Jitter += (float)((Math.Abs(deviation) - Jitter) * 0.05);
                // The offset follows the latest arrivals slowly, and late ones more slowly still.
                _offset += deviation * (deviation > 0 ? 0.01 : 0.1);
            }

            int newestSeq = lastRun + (int)newestDelta;
            int sentSlot = newestSeq & (SentRing - 1);
            if (newestSeq >= 0 && _commandSeqs[sentSlot] == newestSeq)
            {
                float sample = (float)Math.Max(0.0, now - _sentAt[sentSlot] - heldMs / 1000.0);
                RoundTrip = RoundTrip <= 0f ? sample : RoundTrip + (sample - RoundTrip) * 0.1f;
            }
        }
    }

    private ReceivedSnapshot? Find(int tick)
    {
        if (tick < 0)
        {
            return null;
        }

        ReceivedSnapshot s = _snapshots[(tick / Math.Max(1, Settings.SendEveryTicks)) % Ring];
        return s.Tick == tick ? s : null;
    }
}
