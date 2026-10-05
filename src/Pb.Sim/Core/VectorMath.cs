using System.Numerics;

namespace Pb.Sim.Core;

public static class VectorMath
{
    /// <summary>Two unit vectors perpendicular to <paramref name="n"/> and to each other.</summary>
    public static void OrthonormalBasis(Vector3 n, out Vector3 b1, out Vector3 b2)
    {
        // Duff et al. 2017, "Building an Orthonormal Basis, Revisited".
        float sign = MathF.CopySign(1f, n.Z);
        float a = -1f / (sign + n.Z);
        float b = n.X * n.Y * a;
        b1 = new Vector3(1f + sign * n.X * n.X * a, sign * b, -sign * n.X);
        b2 = new Vector3(b, sign + n.Y * n.Y * a, -n.Y);
    }

    public static Vector3 NormalizeOr(Vector3 v, Vector3 fallback)
    {
        float lengthSquared = v.LengthSquared();
        return lengthSquared > 1e-12f ? v / MathF.Sqrt(lengthSquared) : fallback;
    }

    public static Vector2 NormalizeOr(Vector2 v, Vector2 fallback)
    {
        float lengthSquared = v.LengthSquared();
        return lengthSquared > 1e-12f ? v / MathF.Sqrt(lengthSquared) : fallback;
    }

    public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDelta)
    {
        Vector3 delta = target - current;
        float distance = delta.Length();
        return distance <= maxDelta || distance < 1e-9f ? target : current + delta / distance * maxDelta;
    }

    public static Vector3 Horizontal(Vector3 v) => new(v.X, 0f, v.Z);
}
