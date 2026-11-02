using System;
using System.Numerics;
using Pb.Sim.Ballistics;

namespace Pb.Game.Ballistics;

/// <summary>A ball that passed close by someone's head: where it passed nearest, how fast it was going, how close it came.</summary>
public readonly record struct Flyby(Vector3 Point, Vector3 Velocity, float Distance, int Owner, uint Sequence);

/// <summary>
/// Finds the balls in flight that pass within <see cref="Radius"/> of a head (presentation only: the whizz of a near miss
/// and the incoming-fire arc). Run once a tick after the sim's step: each ball's latest move (its previous position to
/// its position) is tested for its nearest approach to the head, and a ball is reported on the tick it comes nearest,
/// once (balls are known by shooter and shot number, as in the sim, since their slots move). Plain C#, so it's tested
/// without the engine; nothing is allocated per tick.
/// </summary>
public sealed class FlybyScan
{
    private readonly (int Owner, uint Sequence)[] _reported;
    private int _next;

    /// <param name="radius">How close a ball has to pass (m).</param>
    /// <param name="minSpeed">Slower balls (bounced, spent) don't count (m/s).</param>
    /// <param name="memory">How many reported balls are remembered, so none is reported twice.</param>
    public FlybyScan(float radius, float minSpeed, int memory = 64)
    {
        Radius = radius;
        MinSpeed = minSpeed;
        _reported = new (int, uint)[Math.Max(8, memory)];
        Array.Fill(_reported, (int.MinValue, 0u));
    }

    public float Radius { get; }

    public float MinSpeed { get; }

    /// <summary>
    /// The balls in <paramref name="pool"/> (other than <paramref name="skipOwner"/>'s) that came nearest to
    /// <paramref name="head"/> during their latest move, within <see cref="Radius"/>: written to <paramref name="found"/>,
    /// as many as fit. Returns how many.
    /// </summary>
    public int Scan(BallPool pool, Vector3 head, int skipOwner, Span<Flyby> found)
    {
        int n = 0;
        float r2 = Radius * Radius, slow2 = MinSpeed * MinSpeed;
        for (int i = 0; i < pool.Count && n < found.Length; i++)
        {
            if (pool.Owner[i] == skipOwner || pool.Velocity[i].LengthSquared() < slow2)
            {
                continue;
            }

            if (!Nearest(pool.PrevPosition[i], pool.Position[i], head, out Vector3 point, out float d2) || d2 > r2)
            {
                continue;
            }

            if (Seen(pool.Owner[i], pool.Sequence[i]))
            {
                continue;
            }

            Remember(pool.Owner[i], pool.Sequence[i]);
            found[n++] = new Flyby(point, pool.Velocity[i], MathF.Sqrt(d2), pool.Owner[i], pool.Sequence[i]);
        }

        return n;
    }

    /// <summary>
    /// The nearest point to <paramref name="head"/> on the move from <paramref name="from"/> to <paramref name="to"/>,
    /// and its squared distance; false when the ball is still closing in at the end of the move (it comes nearer on the
    /// next) or the move is nothing.
    /// </summary>
    public static bool Nearest(Vector3 from, Vector3 to, Vector3 head, out Vector3 point, out float distance2)
    {
        Vector3 move = to - from;
        float length2 = move.LengthSquared();
        point = to;
        distance2 = float.MaxValue;
        if (length2 < 1e-10f)
        {
            return false;
        }

        float t = Vector3.Dot(head - from, move) / length2;
        if (t >= 1f)
        {
            return false;
        }

        point = from + move * MathF.Max(t, 0f);
        distance2 = Vector3.DistanceSquared(point, head);
        return true;
    }

    private bool Seen(int owner, uint sequence)
    {
        foreach ((int o, uint s) in _reported)
        {
            if (o == owner && s == sequence)
            {
                return true;
            }
        }

        return false;
    }

    private void Remember(int owner, uint sequence)
    {
        _reported[_next] = (owner, sequence);
        _next = (_next + 1) % _reported.Length;
    }
}
