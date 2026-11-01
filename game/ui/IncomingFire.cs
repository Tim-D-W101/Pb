using System;
using System.Numerics;

namespace Pb.Game.Ui;

/// <summary>What an incoming-fire arc is showing, least to most urgent.</summary>
public enum IncomingKind : byte
{
    /// <summary>A ball whizzed past close to your head.</summary>
    Flyby,
    /// <summary>A ball broke (or bounced) on something close to you.</summary>
    NearBreak,
    /// <summary>A ball bounced off you (it doesn't count, but it was on target).</summary>
    BounceOffYou,
    /// <summary>The ball that put you out.</summary>
    Hit,
}

/// <summary>One arc: the bearing paint came from (fixed in the world, so it turns with you), its kind, age and strength.</summary>
public struct IncomingArc
{
    /// <summary>The yaw you'd face to look where the paint came from (radians, the sim's convention).</summary>
    public float Bearing;
    public IncomingKind Kind;
    public float Age;
    public float Life;
    /// <summary>0-1: how close the ball came (a ball that hit you is 1).</summary>
    public float Strength;

    /// <summary>How visible it still is, 0-1: full for most of its life, then fading out.</summary>
    public readonly float Fade => Life <= 0f ? 0f : Math.Clamp((Life - Age) / (Life * 0.6f), 0f, 1f);
}

/// <summary>
/// The incoming-fire arcs round the crosshair (the damage-direction indicator): each says where paint came from, the
/// reverse of the ball's own travel, so it shows where you're being shot from and never where anyone is (a ball that came
/// round a corner points along its last stretch, the way it would look). New paint from much the same direction
/// refreshes the arc already there rather than adding one. Plain C#, tested without the engine; the HUD draws it
/// (<see cref="IncomingFireRing"/>).
/// </summary>
public sealed class IncomingFire
{
    private readonly IncomingArc[] _arcs;
    private readonly float[] _life;
    private readonly float _merge;

    /// <param name="max">At most this many arcs at once (the oldest gives way).</param>
    /// <param name="mergeDeg">Paint from within this many degrees of an arc refreshes it.</param>
    /// <param name="life">How long each kind shows (s), by <see cref="IncomingKind"/>.</param>
    public IncomingFire(int max, float mergeDeg, ReadOnlySpan<float> life)
    {
        _arcs = new IncomingArc[Math.Max(1, max)];
        _merge = mergeDeg * MathF.PI / 180f;
        _life = new float[4];
        for (int i = 0; i < _life.Length; i++)
        {
            _life[i] = i < life.Length ? life[i] : 1f;
        }
    }

    public int Count { get; private set; }

    public ReadOnlySpan<IncomingArc> Arcs => new(_arcs, 0, Count);

    /// <summary>The yaw that faces along <paramref name="direction"/> in the ground plane (yaw 0 faces −Z, positive turns left).</summary>
    public static float BearingOf(Vector3 direction) => MathF.Atan2(-direction.X, -direction.Z);

    /// <summary>
    /// Where an arc at <paramref name="bearing"/> sits round the crosshair for someone facing <paramref name="viewYaw"/>:
    /// radians clockwise from straight up the screen (0 ahead, π/2 to the right, ±π behind).
    /// </summary>
    public static float ScreenAngle(float bearing, float viewYaw) => Wrap(viewYaw - bearing);

    /// <summary>
    /// Paint travelling along <paramref name="travel"/> (the ball's velocity) came from the opposite way: an arc there.
    /// A ball coming straight down or up gives no bearing and no arc. Returns whether one was added or refreshed.
    /// </summary>
    public bool Add(Vector3 travel, IncomingKind kind, float strength = 1f)
    {
        var flat = new Vector3(-travel.X, 0f, -travel.Z);
        if (flat.LengthSquared() < 1e-6f)
        {
            return false;
        }

        float bearing = BearingOf(flat);
        strength = Math.Clamp(strength, 0f, 1f);
        for (int i = 0; i < Count; i++)
        {
            ref IncomingArc arc = ref _arcs[i];
            if (MathF.Abs(Wrap(arc.Bearing - bearing)) <= _merge)
            {
                // The same direction again: the arc takes the newer bearing, the more urgent kind and the stronger strength.
                arc.Bearing = bearing;
                if (kind >= arc.Kind)
                {
                    arc.Kind = kind;
                    arc.Life = _life[(int)kind];
                }

                arc.Age = 0f;
                arc.Strength = MathF.Max(arc.Strength, strength);
                return true;
            }
        }

        if (Count == _arcs.Length)
        {
            // Full: the oldest gives way.
            int oldest = 0;
            for (int i = 1; i < Count; i++)
            {
                if (_arcs[i].Age > _arcs[oldest].Age)
                {
                    oldest = i;
                }
            }

            _arcs[oldest] = _arcs[--Count];
        }

        _arcs[Count++] = new IncomingArc { Bearing = bearing, Kind = kind, Age = 0f, Life = _life[(int)kind], Strength = strength };
        return true;
    }

    /// <summary>Ages the arcs by <paramref name="dt"/> seconds and drops those whose time is up.</summary>
    public void Update(float dt)
    {
        for (int i = Count - 1; i >= 0; i--)
        {
            _arcs[i].Age += dt;
            if (_arcs[i].Age >= _arcs[i].Life)
            {
                _arcs[i] = _arcs[--Count];
            }
        }
    }

    public void Clear() => Count = 0;

    /// <summary>
    /// Which way a ball that bounced off a surface with <paramref name="normal"/> was going before it hit, from its
    /// <paramref name="outgoing"/> velocity (the sim reports a bounce's outgoing velocity): mirrored back through the
    /// surface. Only the direction is right (a bounce loses speed into the surface), which is all an arc needs.
    /// </summary>
    public static Vector3 BeforeBounce(Vector3 outgoing, Vector3 normal)
    {
        float into = Vector3.Dot(outgoing, normal);
        return into > 0f ? outgoing - 2f * into * normal : outgoing;
    }

    /// <summary>An angle wrapped into (−π, π].</summary>
    public static float Wrap(float a)
    {
        a %= MathF.Tau;
        if (a > MathF.PI)
        {
            a -= MathF.Tau;
        }
        else if (a <= -MathF.PI)
        {
            a += MathF.Tau;
        }

        return a;
    }
}
