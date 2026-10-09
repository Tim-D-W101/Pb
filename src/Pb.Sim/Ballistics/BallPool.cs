using System.Numerics;
using Pb.Sim.Core;

namespace Pb.Sim.Ballistics;

/// <summary>
/// Live paintballs as a struct-of-arrays pool, densely packed in [0, <see cref="Count"/>).
/// Removal swaps the last ball into the hole, so iteration stays contiguous and nothing is
/// allocated after construction. A ball is identified by (<see cref="Owner"/>, <see cref="Sequence"/>),
/// not by its slot, because slots move.
/// </summary>
public sealed class BallPool
{
    public BallPool(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        Capacity = capacity;
        Position = new Vector3[capacity];
        PrevPosition = new Vector3[capacity];
        Velocity = new Vector3[capacity];
        Age = new float[capacity];
        FirstStep = new float[capacity];
        Owner = new int[capacity];
        Sequence = new uint[capacity];
        Team = new byte[capacity];
        Bounces = new byte[capacity];
        Bounced = new bool[capacity];
        Rng = new Pcg32[capacity];
        Rewind = new byte[capacity];
        Remote = new bool[capacity];
    }

    public int Capacity { get; }

    public int Count { get; private set; }

    public Vector3[] Position { get; }

    /// <summary>Position at the start of the latest tick (for render interpolation).</summary>
    public Vector3[] PrevPosition { get; }

    public Vector3[] Velocity { get; }

    public float[] Age { get; }

    /// <summary>Length of the first integration step for a ball spawned mid-tick (0 once used).</summary>
    public float[] FirstStep { get; }

    public int[] Owner { get; }

    public uint[] Sequence { get; }

    public byte[] Team { get; }

    public byte[] Bounces { get; }

    /// <summary>A ball that has bounced can never eliminate (spec §1.1).</summary>
    public bool[] Bounced { get; }

    /// <summary>Each ball's own random stream (seeded per shot) for its break rolls.</summary>
    public Pcg32[] Rng { get; }

    /// <summary>Lag compensation: how many ticks back its shooter was seeing; it's tested against players as they were then.</summary>
    public byte[] Rewind { get; }

    /// <summary>
    /// On a joining copy, a ball someone else fired, flown here for show: it passes through players (the server says
    /// whom it hit) and can't put anyone out.
    /// </summary>
    public bool[] Remote { get; }

    public int Add(Vector3 position, Vector3 velocity, int owner, uint sequence, byte team, in Pcg32 rng, float firstStep, byte rewind = 0,
        bool remote = false)
    {
        if (Count == Capacity)
        {
            return -1;
        }

        int i = Count++;
        Position[i] = position;
        PrevPosition[i] = position;
        Velocity[i] = velocity;
        Age[i] = 0f;
        FirstStep[i] = firstStep;
        Owner[i] = owner;
        Sequence[i] = sequence;
        Team[i] = team;
        Bounces[i] = 0;
        Bounced[i] = false;
        Rng[i] = rng;
        Rewind[i] = rewind;
        Remote[i] = remote;
        return i;
    }

    /// <summary>The slot of the ball <paramref name="owner"/> fired as <paramref name="sequence"/>, or −1.</summary>
    public int Find(int owner, uint sequence)
    {
        for (int i = 0; i < Count; i++)
        {
            if (Sequence[i] == sequence && Owner[i] == owner)
            {
                return i;
            }
        }

        return -1;
    }

    public void RemoveAt(int i)
    {
        int last = --Count;
        if (i == last)
        {
            return;
        }

        Position[i] = Position[last];
        PrevPosition[i] = PrevPosition[last];
        Velocity[i] = Velocity[last];
        Age[i] = Age[last];
        FirstStep[i] = FirstStep[last];
        Owner[i] = Owner[last];
        Sequence[i] = Sequence[last];
        Team[i] = Team[last];
        Bounces[i] = Bounces[last];
        Bounced[i] = Bounced[last];
        Rng[i] = Rng[last];
        Rewind[i] = Rewind[last];
        Remote[i] = Remote[last];
    }

    public void Clear() => Count = 0;
}
