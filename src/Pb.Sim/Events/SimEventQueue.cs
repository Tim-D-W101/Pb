namespace Pb.Sim.Events;

/// <summary>
/// Events produced during <see cref="SimWorld.Step"/>. The host (Godot driver, test, benchmark)
/// reads <see cref="Items"/> after each step and calls <see cref="Clear"/>. Capacity is reused, so
/// steady-state stepping does not allocate.
/// </summary>
public sealed class SimEventQueue
{
    private SimEvent[] _items;

    public SimEventQueue(int initialCapacity = 4096)
    {
        _items = new SimEvent[Math.Max(16, initialCapacity)];
    }

    public int Count { get; private set; }

    public ReadOnlySpan<SimEvent> Items => new(_items, 0, Count);

    public void Add(in SimEvent e)
    {
        if (Count == _items.Length)
        {
            Array.Resize(ref _items, _items.Length * 2);
        }

        _items[Count++] = e;
    }

    public void Clear() => Count = 0;

    public int CountOf(SimEventType type)
    {
        int n = 0;
        for (int i = 0; i < Count; i++)
        {
            if (_items[i].Type == type)
            {
                n++;
            }
        }

        return n;
    }
}
