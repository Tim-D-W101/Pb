using System.Numerics;
using Pb.Sim.Collision;

namespace Pb.Net.Packing;

/// <summary>Turns floats into a few bits and back. Every copy quantises the same way, so they agree on what was sent.</summary>
public static class Quant
{
    /// <summary><paramref name="value"/> clamped to [min, max] as one of 2^bits evenly spaced steps.</summary>
    public static uint Range(float value, float min, float max, int bits)
    {
        uint steps = bits >= 32 ? uint.MaxValue : (1u << bits) - 1u;
        float t = (value - min) / (max - min);
        if (!(t > 0f))
        {
            return 0;
        }

        return t >= 1f ? steps : (uint)MathF.Round(t * steps);
    }

    public static float FromRange(uint q, float min, float max, int bits)
    {
        uint steps = bits >= 32 ? uint.MaxValue : (1u << bits) - 1u;
        return min + (max - min) * (Math.Min(q, steps) / (float)steps);
    }

    /// <summary>An angle (rad, any turn) as one of 2^bits steps round the circle.</summary>
    public static uint Angle(float radians, int bits)
    {
        float turns = radians / (2f * MathF.PI);
        turns -= MathF.Floor(turns);
        uint count = 1u << bits;
        return (uint)MathF.Round(turns * count) & (count - 1u);
    }

    /// <summary>The angle back, in (−π, π].</summary>
    public static float FromAngle(uint q, int bits)
    {
        float a = q / (float)(1u << bits) * (2f * MathF.PI);
        return a > MathF.PI ? a - 2f * MathF.PI : a;
    }
}

/// <summary>
/// Positions in a level as whole steps along each axis, over the level's bounds and a margin: enough bits that a step is
/// at most <see cref="MaxStep"/> (2.5 mm). Both copies build it from the same level, so they share the grid.
/// </summary>
public readonly struct PositionQuant
{
    public const float MaxStep = 0.0025f;

    private const float Margin = 10f;

    public PositionQuant(Aabb bounds)
    {
        // Players stand between a little under the ground and the tops of the tallest things they can climb.
        Min = new Vector3(bounds.Min.X - Margin, -20f, bounds.Min.Z - Margin);
        Max = new Vector3(bounds.Max.X + Margin, 80f, bounds.Max.Z + Margin);
        BitsX = BitsFor(Max.X - Min.X);
        BitsY = BitsFor(Max.Y - Min.Y);
        BitsZ = BitsFor(Max.Z - Min.Z);
    }

    public Vector3 Min { get; }

    public Vector3 Max { get; }

    public int BitsX { get; }

    public int BitsY { get; }

    public int BitsZ { get; }

    public void Quantize(Vector3 p, out uint x, out uint y, out uint z)
    {
        x = Quant.Range(p.X, Min.X, Max.X, BitsX);
        y = Quant.Range(p.Y, Min.Y, Max.Y, BitsY);
        z = Quant.Range(p.Z, Min.Z, Max.Z, BitsZ);
    }

    public Vector3 Dequantize(uint x, uint y, uint z) => new(
        Quant.FromRange(x, Min.X, Max.X, BitsX), Quant.FromRange(y, Min.Y, Max.Y, BitsY), Quant.FromRange(z, Min.Z, Max.Z, BitsZ));

    /// <summary>The point as the grid has it.</summary>
    public Vector3 Snap(Vector3 p)
    {
        Quantize(p, out uint x, out uint y, out uint z);
        return Dequantize(x, y, z);
    }

    private static int BitsFor(float range)
    {
        int bits = 8;
        while (bits < 24 && range / ((1u << bits) - 1u) > MaxStep)
        {
            bits++;
        }

        return bits;
    }
}
