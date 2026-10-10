using Pb.Sim.Players;

namespace Pb.Net.Server;

/// <summary>
/// One player's commands on the server, run one a tick in the order they were made (by the sequence each carries, its
/// tick on the player's copy). It fills to a little depth before the first runs, to even out uneven arrival. A command
/// that hasn't come when its turn does is replaced by the last one run (commands are mostly the same tick to tick, so
/// that's usually exactly right) and the real one, if it comes, is dropped. If commands keep coming too late, it waits a
/// tick for them (a tick more delay); if the queue stays too long, it runs two in each tick (one tick's moves, buttons of
/// both) until it's back to its depth. A command further ahead than the queue reaches means the copy's clock has run on
/// (the host stalled, or the copy runs fast): the queue skips on to it, and the commands it skips are never run. So nobody
/// gets more moves than ticks have passed, however fast they send, and nobody is shut out for long, however far ahead.
/// </summary>
public sealed class CommandQueue
{
    private const int Capacity = 512;

    private readonly NetSettings _settings;
    private readonly float _dt;
    private readonly InputCommand[] _commands = new InputCommand[Capacity];
    private readonly int[] _views = new int[Capacity];
    private readonly int[] _seqs = new int[Capacity];
    private InputCommand _last;
    private int _lastView = -1;
    private int _first = -1;
    private bool _started;
    private int _next;
    private float _longFor;
    private bool _catchingUp;
    private float _missingFor;
    private float _lateFor;
    private int _lateSince;
    private int _skippedBelow = -1;

    public CommandQueue(NetSettings settings, float dt)
    {
        _settings = settings;
        _dt = dt;
        Array.Fill(_seqs, -1);
    }

    /// <summary>The newest command's sequence that has arrived (−1: none yet).</summary>
    public int Newest { get; private set; } = -1;

    /// <summary>The last sequence run: a snapshot says so, and the player's copy checks its prediction against it.</summary>
    public int LastRun { get; private set; } = -1;

    /// <summary>Commands waiting their turn.</summary>
    public int Depth => _started ? Math.Max(0, Newest - _next + 1) : (_first < 0 ? 0 : Newest - _first + 1);

    /// <summary>Commands that didn't come in time (the last one ran in their place).</summary>
    public int Missing { get; private set; }

    /// <summary>Commands that came after their turn and were dropped.</summary>
    public int Late { get; private set; }

    /// <summary>Commands that came too far ahead of their turn (a fault, a stall or a cheat): the queue skipped on to each.</summary>
    public int TooFarAhead { get; private set; }

    /// <summary>Commands passed over by those skips, never run.</summary>
    public int Skipped { get; private set; }

    /// <summary>Times two commands were run as one, the queue having stayed too long.</summary>
    public int Merged { get; private set; }

    /// <summary>Ticks it waited for late commands.</summary>
    public int Waited { get; private set; }

    /// <summary>Takes a command from a packet (repeats and stale ones are dropped).</summary>
    public void Receive(int seq, in InputCommand command, int viewTick)
    {
        if (seq < 0)
        {
            return;
        }

        int floor = _started ? _next : _first;
        if (floor >= 0 && seq < floor)
        {
            // A repeat of one already here is the packets' redundancy, and one from before a skip was passed over; one that's
            // new missed its turn (noted, to wait for them).
            if (_seqs[seq & (Capacity - 1)] != seq && seq >= _skippedBelow)
            {
                Late++;
                _lateSince += _started ? 1 : 0;
            }

            return;
        }

        int ceiling = (_started ? _next : Math.Max(_first, 0)) + _settings.CommandAheadTicks;
        if (floor >= 0 && seq > ceiling)
        {
            SkipTo(seq);
        }

        int slot = seq & (Capacity - 1);
        if (_seqs[slot] == seq)
        {
            return;
        }

        _seqs[slot] = seq;
        _commands[slot] = command;
        _views[slot] = viewTick;
        if (_first < 0 || (!_started && seq < _first))
        {
            _first = seq;
        }

        Newest = Math.Max(Newest, seq);
    }

    /// <summary>
    /// The command to run this tick and the server tick the player was seeing when they made it (−1 if not known). False
    /// before the first one has come (or while the queue first fills): the player stands still.
    /// </summary>
    public bool TryNext(out InputCommand command, out int viewTick)
    {
        if (!_started)
        {
            if (_first < 0 || Newest - _first + 1 < Math.Max(1, _settings.CommandQueueTicks))
            {
                command = default;
                viewTick = -1;
                return false;
            }

            _started = true;
            _next = _first;
        }

        // Too long for too long: two in one tick, every tick, until it's back to its depth.
        _longFor = Depth > _settings.CommandQueueMostTicks ? _longFor + _dt : 0f;
        _catchingUp = (_catchingUp || _longFor >= _settings.CommandQueuePatience) && Depth > Math.Max(1, _settings.CommandQueueTicks);
        if (_catchingUp && Has(_next) && Has(_next + 1))
        {
            _longFor = 0f;
            Merged++;
            InputCommand a = Take(_next, out int _);
            InputCommand b = Take(_next + 1, out int viewB);
            b.Buttons |= a.Buttons;
            _next += 2;
            return Run(b, viewB, out command, out viewTick);
        }

        // Commands keep coming after their turn: wait a tick for them.
        _lateFor += _dt;
        if (_lateFor >= _settings.CommandQueuePatience)
        {
            bool wait = _lateSince >= 3 && !Has(_next);
            _lateFor = 0f;
            _lateSince = 0;
            if (wait)
            {
                Waited++;
                command = _last;
                viewTick = _lastView;
                return true;
            }
        }

        if (Has(_next))
        {
            InputCommand next = Take(_next, out int view);
            _next++;
            _missingFor = 0f;
            return Run(next, view, out command, out viewTick);
        }

        // Not here in time: the last one again, in its place; after a while with none, standing still.
        Missing++;
        _missingFor += _dt;
        _next++;
        InputCommand stand = _last;
        if (_missingFor > _settings.RepeatMissing)
        {
            stand.Move = default;
            stand.Buttons = InputButtons.None;
        }

        return Run(stand, _lastView, out command, out viewTick);
    }

    /// <summary>
    /// A command further ahead of its turn than the queue reaches: the queue goes on from its usual depth before it, the
    /// commands in between passed over. The player loses those moves (they stood still meanwhile), never gains any.
    /// </summary>
    private void SkipTo(int seq)
    {
        TooFarAhead++;
        int from = seq - Math.Max(1, _settings.CommandQueueTicks) + 1;
        if (_started)
        {
            Skipped += from - _next;
            _next = from;
        }
        else
        {
            _first = from;
        }

        _skippedBelow = from;
        _longFor = 0f;
        _catchingUp = false;
        _lateFor = 0f;
        _lateSince = 0;
    }

    private bool Run(in InputCommand next, int view, out InputCommand command, out int viewTick)
    {
        _last = next;
        _lastView = view;
        LastRun = _next - 1;
        command = next;
        viewTick = view;
        return true;
    }

    private bool Has(int seq) => _seqs[seq & (Capacity - 1)] == seq;

    private InputCommand Take(int seq, out int view)
    {
        int slot = seq & (Capacity - 1);
        view = _views[slot];
        return _commands[slot];
    }
}
