using System.Numerics;
using Pb.Net.Packing;
using Pb.Sim.Players;

namespace Pb.Net.Protocol;

/// <summary>
/// Another player as a snapshot carries them, quantised to a list of fields: where they stand, how they move, look and
/// lean, and what they're doing (on a ladder, refilling, out). It's everything their character is drawn from, and their
/// hitboxes on a joining copy.
/// </summary>
public static class PuppetFields
{
    public const int Count = 20;

    private const int X = 0, Y = 1, Z = 2, VX = 3, VY = 4, VZ = 5, Yaw = 6, Pitch = 7, Head = 8, Stance = 9, Eye = 10, Lean = 11,
        Shoulder = 12, Tuck = 13, Flags = 14, Ladder = 15, Phase = 16, Refill = 17, Eliminations = 18, Hits = 19;

    private const float MaxSpeed = 16f;
    private const float HalfTurn = MathF.PI * 0.5f;

    private const uint AliveFlag = 1, PresentFlag = 2, GroundedFlag = 4, SprintingFlag = 8, RefillingFlag = 16;

    public static void Widths(in PositionQuant grid, Span<byte> into)
    {
        into[X] = (byte)grid.BitsX;
        into[Y] = (byte)grid.BitsY;
        into[Z] = (byte)grid.BitsZ;
        into[VX] = into[VY] = into[VZ] = 12;
        into[Yaw] = 16;
        into[Pitch] = 14;
        into[Head] = 12;
        into[Stance] = 2;
        into[Eye] = 8;
        into[Lean] = 8;
        into[Shoulder] = 8;
        into[Tuck] = 8;
        into[Flags] = 5;
        into[Ladder] = 7;
        into[Phase] = 2;
        into[Refill] = 8;
        into[Eliminations] = 7;
        into[Hits] = 8;
    }

    public static void Capture(PlayerState p, in PositionQuant grid, Span<uint> into)
    {
        grid.Quantize(p.Position, out into[X], out into[Y], out into[Z]);
        into[VX] = Quant.Range(p.Velocity.X, -MaxSpeed, MaxSpeed, 12);
        into[VY] = Quant.Range(p.Velocity.Y, -MaxSpeed, MaxSpeed, 12);
        into[VZ] = Quant.Range(p.Velocity.Z, -MaxSpeed, MaxSpeed, 12);
        into[Yaw] = Quant.Angle(p.Yaw, 16);
        into[Pitch] = Quant.Range(p.Pitch, -HalfTurn, HalfTurn, 14);
        into[Head] = Quant.Range(p.HeadYaw, -HalfTurn, HalfTurn, 12);
        into[Stance] = (uint)p.Stance;
        into[Eye] = Quant.Range(p.EyeHeight, 0f, 2f, 8);
        into[Lean] = Quant.Range(p.Lean, -1f, 1f, 8);
        into[Shoulder] = Quant.Range(p.Shoulder, -1f, 1f, 8);
        into[Tuck] = Quant.Range(p.Tuck, 0f, HalfTurn, 8);
        bool refilling = p.Marker.Refill.Active;
        into[Flags] = (p.Alive ? AliveFlag : 0) | (p.Present ? PresentFlag : 0) | (p.Grounded ? GroundedFlag : 0) |
                      (p.Sprinting ? SprintingFlag : 0) | (refilling ? RefillingFlag : 0);
        into[Ladder] = (uint)(p.Ladder + 1);
        into[Phase] = (uint)p.LadderPhase;
        into[Refill] = refilling ? Quant.Range(p.Marker.Refill.Progress(p.Marker.Paint.Params), 0f, 1f, 8) : 0;
        into[Eliminations] = (uint)Math.Clamp(p.Eliminations, 0, 127);
        into[Hits] = (uint)Math.Clamp(p.Hits, 0, 255);
    }

    /// <summary>A player's state, as a joining copy draws them, from two snapshots' fields <paramref name="t"/> of the way from a to b.</summary>
    public static void Pose(PlayerState p, ReadOnlySpan<uint> a, ReadOnlySpan<uint> b, float t, in PositionQuant grid, MovementParams move)
    {
        Vector3 pa = grid.Dequantize(a[X], a[Y], a[Z]);
        Vector3 pb = grid.Dequantize(b[X], b[Y], b[Z]);
        // A jump of more than a few metres between snapshots is a teleport (a new start): no sliding across the map.
        p.Position = Vector3.DistanceSquared(pa, pb) > 9f ? (t < 0.5f ? pa : pb) : Vector3.Lerp(pa, pb, t);
        p.Velocity = Vector3.Lerp(Velocity(a), Velocity(b), t);
        p.Yaw = LerpAngle(Quant.FromAngle(a[Yaw], 16), Quant.FromAngle(b[Yaw], 16), t);
        p.Pitch = Lerp(Quant.FromRange(a[Pitch], -HalfTurn, HalfTurn, 14), Quant.FromRange(b[Pitch], -HalfTurn, HalfTurn, 14), t);
        p.HeadYaw = Lerp(Quant.FromRange(a[Head], -HalfTurn, HalfTurn, 12), Quant.FromRange(b[Head], -HalfTurn, HalfTurn, 12), t);
        p.EyeHeight = Lerp(Quant.FromRange(a[Eye], 0f, 2f, 8), Quant.FromRange(b[Eye], 0f, 2f, 8), t);
        p.Lean = Lerp(Quant.FromRange(a[Lean], -1f, 1f, 8), Quant.FromRange(b[Lean], -1f, 1f, 8), t);
        p.Shoulder = Lerp(Quant.FromRange(a[Shoulder], -1f, 1f, 8), Quant.FromRange(b[Shoulder], -1f, 1f, 8), t);
        p.ShoulderTarget = p.Shoulder >= 0f ? 1f : -1f;
        p.Tuck = Lerp(Quant.FromRange(a[Tuck], 0f, HalfTurn, 8), Quant.FromRange(b[Tuck], 0f, HalfTurn, 8), t);
        p.LeanRoll = p.Lean * move.LeanAngle;
        p.LeanOffset = PlayerPose.LeanOffset(p.LeanRoll, p.Yaw, move.LeanPivotBelowEye);

        // What can't be in between comes from the nearer snapshot.
        ReadOnlySpan<uint> near = t < 0.5f ? a : b;
        p.Stance = (Stance)near[Stance];
        uint flags = near[Flags];
        p.Alive = (flags & AliveFlag) != 0;
        p.Present = (flags & PresentFlag) != 0;
        p.Grounded = (flags & GroundedFlag) != 0;
        p.Sprinting = (flags & SprintingFlag) != 0;
        p.Ladder = (int)near[Ladder] - 1;
        p.LadderPhase = (LadderPhase)near[Phase];
        Scores(p, near);
        bool refilling = (flags & RefillingFlag) != 0;
        float progress = Lerp(Quant.FromRange(a[Refill], 0f, 1f, 8), Quant.FromRange(b[Refill], 0f, 1f, 8), t);
        p.Marker.Refill.ShowFromServer(refilling, refilling ? progress * p.Marker.Paint.Params.RefillTime : 0f);
    }

    /// <summary>A player's numbers so far as the server has them (a joining copy's own player too, which it doesn't pose).</summary>
    public static void Scores(PlayerState p, ReadOnlySpan<uint> fields)
    {
        p.Eliminations = (int)fields[Eliminations];
        p.Hits = (int)fields[Hits];
    }

    private static Vector3 Velocity(ReadOnlySpan<uint> f) => new(
        Quant.FromRange(f[VX], -MaxSpeed, MaxSpeed, 12), Quant.FromRange(f[VY], -MaxSpeed, MaxSpeed, 12),
        Quant.FromRange(f[VZ], -MaxSpeed, MaxSpeed, 12));

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float LerpAngle(float a, float b, float t)
    {
        float d = b - a;
        d -= MathF.Floor((d + MathF.PI) / (2f * MathF.PI)) * 2f * MathF.PI;
        return a + d * t;
    }
}
