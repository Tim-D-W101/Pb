using Pb.Sim.Events;

namespace Pb.Net.Server;

/// <summary>
/// The events a server sends, each numbered in order (from 1). Each player is sent the ones they haven't confirmed yet,
/// with every snapshot until they do; the log keeps the latest few thousand.
/// </summary>
public sealed class EventLog
{
    public const int Capacity = 8192;

    private readonly SimEvent[] _events = new SimEvent[Capacity];

    /// <summary>The newest event's number (0: none yet).</summary>
    public uint Newest { get; private set; }

    /// <summary>The oldest number still kept.</summary>
    public uint Oldest => Newest >= Capacity ? Newest - Capacity + 1 : 1;

    public void Add(in SimEvent e)
    {
        Newest++;
        _events[Newest % Capacity] = e;
    }

    public bool TryGet(uint seq, out SimEvent e)
    {
        if (seq == 0 || seq > Newest || seq < Oldest)
        {
            e = default;
            return false;
        }

        e = _events[seq % Capacity];
        return true;
    }

    public void Clear() => Newest = 0;
}
