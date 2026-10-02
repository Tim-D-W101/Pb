using System.Numerics;

namespace Pb.Sim.Collision;

public struct Aabb
{
    public Vector3 Min;
    public Vector3 Max;

    public Aabb(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
    }

    public static Aabb FromCenterExtents(Vector3 center, Vector3 halfExtents) =>
        new(center - halfExtents, center + halfExtents);

    public static Aabb FromSegment(Vector3 a, Vector3 b, float inflate)
    {
        Vector3 pad = new(inflate);
        return new Aabb(Vector3.Min(a, b) - pad, Vector3.Max(a, b) + pad);
    }

    public static Aabb FromPoints(ReadOnlySpan<Vector3> points)
    {
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);
        foreach (Vector3 p in points)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        return new Aabb(min, max);
    }

    public readonly Aabb Union(in Aabb other) => new(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));

    public readonly Aabb Expanded(float amount) => new(Min - new Vector3(amount), Max + new Vector3(amount));

    public readonly bool Overlaps(in Aabb o) =>
        Min.X <= o.Max.X && Max.X >= o.Min.X &&
        Min.Y <= o.Max.Y && Max.Y >= o.Min.Y &&
        Min.Z <= o.Max.Z && Max.Z >= o.Min.Z;

    public readonly bool Contains(Vector3 p) =>
        p.X >= Min.X && p.X <= Max.X &&
        p.Y >= Min.Y && p.Y <= Max.Y &&
        p.Z >= Min.Z && p.Z <= Max.Z;
}
