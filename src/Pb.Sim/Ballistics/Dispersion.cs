using System.Numerics;
using Pb.Sim.Core;

namespace Pb.Sim.Ballistics;

public static class Dispersion
{
    /// <summary>
    /// Random direction uniformly distributed over the solid angle of a cone of
    /// <paramref name="halfAngle"/> (radians) around <paramref name="axis"/> (unit vector).
    /// </summary>
    public static Vector3 SampleCone(Vector3 axis, float halfAngle, ref Pcg32 rng)
    {
        if (halfAngle <= 0f)
        {
            return axis;
        }

        // Work with x = 1 − cosθ directly: it keeps precision for sub-degree cones.
        float sinHalf = MathF.Sin(halfAngle * 0.5f);
        float maxX = 2f * sinHalf * sinHalf;
        float x = rng.NextFloat() * maxX;
        float cosTheta = 1f - x;
        float sinTheta = MathF.Sqrt(MathF.Max(0f, x * (2f - x)));
        float phi = rng.NextFloat() * (2f * MathF.PI);
        (float sinPhi, float cosPhi) = MathF.SinCos(phi);

        VectorMath.OrthonormalBasis(axis, out Vector3 b1, out Vector3 b2);
        return Vector3.Normalize(axis * cosTheta + (b1 * cosPhi + b2 * sinPhi) * sinTheta);
    }

    public static float AngleBetween(Vector3 a, Vector3 b)
    {
        // atan2 form stays accurate for tiny angles, unlike acos(dot).
        return MathF.Atan2(Vector3.Cross(a, b).Length(), Vector3.Dot(a, b));
    }
}
