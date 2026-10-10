using System.Numerics;
using Pb.Sim.Core;

namespace Pb.Sim.Collision;

/// <summary>
/// Continuous-collision primitive. <see cref="Sweep"/> moves a sphere of radius <c>r</c> from
/// <c>p0</c> along the segment <c>d</c> (positions <c>p0 + d·t</c>, t ∈ [0, 1]) by ray-testing
/// against the shape inflated by <c>r</c>. It returns the earliest contact; a sphere that starts
/// inside the shape reports t = 0 with the normal of the nearest face.
/// </summary>
public abstract class Shape
{
    public abstract Aabb Bounds { get; }

    public abstract bool Sweep(Vector3 p0, Vector3 d, float r, out float t, out Vector3 normal);

    protected static Vector3 Backwards(Vector3 d) => VectorMath.NormalizeOr(-d, Vector3.UnitY);
}

/// <summary>Infinite half-space, solid where <c>Dot(Normal, x) &lt; Offset</c> (e.g. the ground).</summary>
public sealed class PlaneShape : Shape
{
    public PlaneShape(Vector3 normal, float offset)
    {
        Normal = Vector3.Normalize(normal);
        Offset = offset;
    }

    public Vector3 Normal { get; }

    public float Offset { get; }

    public override Aabb Bounds => new(new Vector3(float.MinValue), new Vector3(float.MaxValue));

    public override bool Sweep(Vector3 p0, Vector3 d, float r, out float t, out Vector3 normal)
    {
        normal = Normal;
        float distance0 = Vector3.Dot(Normal, p0) - (Offset + r);
        if (distance0 <= 0f)
        {
            t = 0f;
            return true;
        }

        float approach = Vector3.Dot(Normal, d);
        if (approach >= 0f)
        {
            t = 0f;
            return false;
        }

        t = -distance0 / approach;
        return t <= 1f;
    }
}

public sealed class SphereShape : Shape
{
    public SphereShape(Vector3 center, float radius)
    {
        Center = center;
        Radius = radius;
        Bounds = Aabb.FromCenterExtents(center, new Vector3(radius));
    }

    public Vector3 Center { get; }

    public float Radius { get; }

    public override Aabb Bounds { get; }

    public override bool Sweep(Vector3 p0, Vector3 d, float r, out float t, out Vector3 normal)
    {
        float radius = Radius + r;
        Vector3 m = p0 - Center;
        float c = Vector3.Dot(m, m) - radius * radius;
        if (c <= 0f)
        {
            t = 0f;
            normal = VectorMath.NormalizeOr(m, Backwards(d));
            return true;
        }

        float a = Vector3.Dot(d, d);
        float b = Vector3.Dot(m, d);
        float discriminant = b * b - a * c;
        if (a < 1e-12f || b >= 0f || discriminant < 0f)
        {
            t = 0f;
            normal = default;
            return false;
        }

        t = (-b - MathF.Sqrt(discriminant)) / a;
        if (t > 1f)
        {
            normal = default;
            return false;
        }

        normal = Vector3.Normalize(p0 + d * t - Center);
        return true;
    }
}

public sealed class CapsuleShape : Shape
{
    public CapsuleShape(Vector3 a, Vector3 b, float radius)
    {
        A = a;
        B = b;
        Radius = radius;
        Vector3 pad = new(radius);
        Bounds = new Aabb(Vector3.Min(a, b) - pad, Vector3.Max(a, b) + pad);
    }

    public Vector3 A { get; }

    public Vector3 B { get; }

    public float Radius { get; }

    public override Aabb Bounds { get; }

    public override bool Sweep(Vector3 p0, Vector3 d, float r, out float t, out Vector3 normal)
    {
        float radius = Radius + r;
        Vector3 ba = B - A;
        float baba = Vector3.Dot(ba, ba);

        // Start inside?
        float along = baba > 0f ? Math.Clamp(Vector3.Dot(p0 - A, ba) / baba, 0f, 1f) : 0f;
        Vector3 away = p0 - (A + ba * along);
        if (away.LengthSquared() <= radius * radius)
        {
            t = 0f;
            normal = VectorMath.NormalizeOr(away, Backwards(d));
            return true;
        }

        float length = d.Length();
        if (length < 1e-9f)
        {
            t = 0f;
            normal = default;
            return false;
        }

        Vector3 rd = d / length;
        float best = float.MaxValue;
        Vector3 bestNormal = default;

        // Cylindrical body (Inigo Quilez's capsule intersection, body part).
        Vector3 oa = p0 - A;
        float bard = Vector3.Dot(ba, rd);
        float baoa = Vector3.Dot(ba, oa);
        float a = baba - bard * bard;
        if (a > 1e-9f * MathF.Max(baba, 1e-9f))
        {
            float b = baba * Vector3.Dot(rd, oa) - baoa * bard;
            float c = baba * Vector3.Dot(oa, oa) - baoa * baoa - radius * radius * baba;
            float h = b * b - a * c;
            if (h >= 0f)
            {
                float s = (-b - MathF.Sqrt(h)) / a;
                float y = baoa + s * bard;
                if (s >= 0f && y > 0f && y < baba)
                {
                    best = s;
                    bestNormal = oa + rd * s - ba * (y / baba);
                }
            }
        }

        // End caps.
        RaySphere(p0, rd, A, radius, ref best, ref bestNormal);
        RaySphere(p0, rd, B, radius, ref best, ref bestNormal);

        if (best > length)
        {
            t = 0f;
            normal = default;
            return false;
        }

        t = best / length;
        normal = VectorMath.NormalizeOr(bestNormal, Backwards(d));
        return true;
    }

    private static void RaySphere(Vector3 origin, Vector3 rd, Vector3 center, float radius, ref float best, ref Vector3 bestNormal)
    {
        Vector3 m = origin - center;
        float b = Vector3.Dot(m, rd);
        float c = Vector3.Dot(m, m) - radius * radius;
        if (c > 0f && b > 0f)
        {
            return;
        }

        float discriminant = b * b - c;
        if (discriminant < 0f)
        {
            return;
        }

        float s = MathF.Max(0f, -b - MathF.Sqrt(discriminant));
        if (s < best)
        {
            best = s;
            bestNormal = origin + rd * s - center;
        }
    }
}

/// <summary>Capped cylinder around <see cref="Axis"/> through <see cref="Center"/>.</summary>
public sealed class CylinderShape : Shape
{
    public CylinderShape(Vector3 center, Vector3 axis, float halfHeight, float radius)
    {
        Center = center;
        Axis = Vector3.Normalize(axis);
        HalfHeight = halfHeight;
        Radius = radius;
        Vector3 u = Axis;
        Vector3 extents = new(
            halfHeight * MathF.Abs(u.X) + radius * MathF.Sqrt(MathF.Max(0f, 1f - u.X * u.X)),
            halfHeight * MathF.Abs(u.Y) + radius * MathF.Sqrt(MathF.Max(0f, 1f - u.Y * u.Y)),
            halfHeight * MathF.Abs(u.Z) + radius * MathF.Sqrt(MathF.Max(0f, 1f - u.Z * u.Z)));
        Bounds = Aabb.FromCenterExtents(center, extents);
    }

    public Vector3 Center { get; }

    public Vector3 Axis { get; }

    public float HalfHeight { get; }

    public float Radius { get; }

    public override Aabb Bounds { get; }

    public override bool Sweep(Vector3 p0, Vector3 d, float r, out float t, out Vector3 normal)
    {
        float halfHeight = HalfHeight + r;
        float radius = Radius + r;
        Vector3 rel = p0 - Center;
        float y0 = Vector3.Dot(rel, Axis);
        float dy = Vector3.Dot(d, Axis);
        Vector3 w0 = rel - Axis * y0;
        Vector3 wd = d - Axis * dy;
        float w0Squared = w0.LengthSquared();

        if (MathF.Abs(y0) <= halfHeight && w0Squared <= radius * radius)
        {
            t = 0f;
            float capGap = halfHeight - MathF.Abs(y0);
            float sideGap = radius - MathF.Sqrt(w0Squared);
            normal = capGap < sideGap
                ? Axis * (y0 >= 0f ? 1f : -1f)
                : VectorMath.NormalizeOr(w0, Backwards(d));
            return true;
        }

        t = 0f;
        normal = default;
        float enter = float.NegativeInfinity;
        float exit = float.PositiveInfinity;
        bool enteredThroughCap = false;

        // Slab between the caps.
        if (MathF.Abs(dy) < 1e-12f)
        {
            if (MathF.Abs(y0) > halfHeight)
            {
                return false;
            }
        }
        else
        {
            float s1 = (-halfHeight - y0) / dy;
            float s2 = (halfHeight - y0) / dy;
            if (s1 > s2)
            {
                (s1, s2) = (s2, s1);
            }

            enter = s1;
            exit = s2;
            enteredThroughCap = true;
        }

        // Infinite cylinder.
        float a = Vector3.Dot(wd, wd);
        float c = w0Squared - radius * radius;
        if (a < 1e-12f)
        {
            if (c > 0f)
            {
                return false;
            }
        }
        else
        {
            float b = Vector3.Dot(w0, wd);
            float discriminant = b * b - a * c;
            if (discriminant < 0f)
            {
                return false;
            }

            float root = MathF.Sqrt(discriminant);
            float s1 = (-b - root) / a;
            float s2 = (-b + root) / a;
            if (s1 > enter)
            {
                enter = s1;
                enteredThroughCap = false;
            }

            exit = MathF.Min(exit, s2);
        }

        if (enter > exit || exit < 0f || enter > 1f)
        {
            return false;
        }

        t = MathF.Max(enter, 0f);
        normal = enteredThroughCap
            ? Axis * (dy > 0f ? -1f : 1f)
            : VectorMath.NormalizeOr(w0 + wd * t, Backwards(d));
        return true;
    }
}

/// <summary>Oriented box.</summary>
public sealed class BoxShape : Shape
{
    private readonly Vector3 _axisX;
    private readonly Vector3 _axisY;
    private readonly Vector3 _axisZ;

    public BoxShape(Vector3 center, Quaternion rotation, Vector3 halfExtents)
    {
        Center = center;
        Rotation = rotation;
        HalfExtents = halfExtents;
        _axisX = Vector3.Transform(Vector3.UnitX, rotation);
        _axisY = Vector3.Transform(Vector3.UnitY, rotation);
        _axisZ = Vector3.Transform(Vector3.UnitZ, rotation);
        Vector3 extents = Vector3.Abs(_axisX) * halfExtents.X + Vector3.Abs(_axisY) * halfExtents.Y + Vector3.Abs(_axisZ) * halfExtents.Z;
        Bounds = Aabb.FromCenterExtents(center, extents);
    }

    public Vector3 Center { get; }

    public Quaternion Rotation { get; }

    public Vector3 HalfExtents { get; }

    public override Aabb Bounds { get; }

    public override bool Sweep(Vector3 p0, Vector3 d, float r, out float t, out Vector3 normal) =>
        SweepOriented(Center, _axisX, _axisY, _axisZ, HalfExtents, p0, d, r, out t, out normal);

    /// <summary>
    /// Sphere sweep against an oriented box given directly by centre, unit axes and half extents,
    /// for boxes that move every tick (player hitboxes) without allocating a shape.
    /// </summary>
    public static bool SweepOriented(Vector3 center, Vector3 axisX, Vector3 axisY, Vector3 axisZ, Vector3 halfExtents,
        Vector3 p0, Vector3 d, float r, out float t, out Vector3 normal)
    {
        Vector3 rel = p0 - center;
        Vector3 e = halfExtents + new Vector3(r);
        Vector3 p = new(Vector3.Dot(rel, axisX), Vector3.Dot(rel, axisY), Vector3.Dot(rel, axisZ));
        Vector3 v = new(Vector3.Dot(d, axisX), Vector3.Dot(d, axisY), Vector3.Dot(d, axisZ));

        float enter = float.NegativeInfinity;
        float exit = float.PositiveInfinity;
        int enterAxis = -1;
        float enterSign = 0f;
        t = 0f;
        normal = default;

        if (!Slab(p.X, v.X, e.X, 0, ref enter, ref exit, ref enterAxis, ref enterSign) ||
            !Slab(p.Y, v.Y, e.Y, 1, ref enter, ref exit, ref enterAxis, ref enterSign) ||
            !Slab(p.Z, v.Z, e.Z, 2, ref enter, ref exit, ref enterAxis, ref enterSign) ||
            exit < 0f)
        {
            return false;
        }

        if (enter < 0f)
        {
            // Started inside: push out through the nearest face.
            Vector3 gap = e - Vector3.Abs(p);
            if (gap.X <= gap.Y && gap.X <= gap.Z)
            {
                normal = axisX * (p.X >= 0f ? 1f : -1f);
            }
            else if (gap.Y <= gap.Z)
            {
                normal = axisY * (p.Y >= 0f ? 1f : -1f);
            }
            else
            {
                normal = axisZ * (p.Z >= 0f ? 1f : -1f);
            }

            return true;
        }

        if (enter > 1f)
        {
            return false;
        }

        t = enter;
        Vector3 axis = enterAxis switch { 0 => axisX, 1 => axisY, _ => axisZ };
        normal = axis * enterSign;
        return true;
    }

    private static bool Slab(float p, float v, float e, int axis, ref float enter, ref float exit, ref int enterAxis, ref float enterSign)
    {
        if (MathF.Abs(v) < 1e-12f)
        {
            return MathF.Abs(p) <= e;
        }

        float t1 = (-e - p) / v;
        float t2 = (e - p) / v;
        float sign = -1f;
        if (t1 > t2)
        {
            (t1, t2) = (t2, t1);
            sign = 1f;
        }

        if (t1 > enter)
        {
            enter = t1;
            enterAxis = axis;
            enterSign = sign;
        }

        exit = MathF.Min(exit, t2);
        return enter <= exit;
    }
}

/// <summary>
/// Convex polyhedron given by outward face planes (<c>Dot(n, x) ≤ offset</c> inside) plus its
/// vertices (used only for bounds). Inflating the planes by the ball radius slightly squares off
/// edges, which errs on the side of a hit by at most a few millimetres.
/// </summary>
public sealed class ConvexShape : Shape
{
    private readonly Vector3[] _normals;
    private readonly float[] _offsets;

    public ConvexShape(ReadOnlySpan<Vector3> normals, ReadOnlySpan<float> offsets, ReadOnlySpan<Vector3> vertices)
    {
        if (normals.Length != offsets.Length || normals.Length < 4)
        {
            throw new ArgumentException("A convex shape needs at least four planes, one offset per normal.");
        }

        Vertices = vertices.ToArray();
        Bounds = Aabb.FromPoints(vertices);

        // Append the six bounding-box planes as "bevels". They don't change the shape, but once
        // every plane is pushed out by the ball radius they stop sharp edges (a wedge's apex) from
        // bloating far beyond the true rounded Minkowski sum, and keep the inflated shape inside
        // its bounds + radius so the broadphase never skips a hit.
        int count = normals.Length + 6;
        _normals = new Vector3[count];
        _offsets = new float[count];
        for (int i = 0; i < normals.Length; i++)
        {
            _normals[i] = Vector3.Normalize(normals[i]);
            _offsets[i] = offsets[i];
        }

        Vector3 min = Bounds.Min;
        Vector3 max = Bounds.Max;
        int b = normals.Length;
        (_normals[b], _offsets[b]) = (Vector3.UnitX, max.X);
        (_normals[b + 1], _offsets[b + 1]) = (-Vector3.UnitX, -min.X);
        (_normals[b + 2], _offsets[b + 2]) = (Vector3.UnitY, max.Y);
        (_normals[b + 3], _offsets[b + 3]) = (-Vector3.UnitY, -min.Y);
        (_normals[b + 4], _offsets[b + 4]) = (Vector3.UnitZ, max.Z);
        (_normals[b + 5], _offsets[b + 5]) = (-Vector3.UnitZ, -min.Z);
    }

    public IReadOnlyList<Vector3> Vertices { get; }

    public override Aabb Bounds { get; }

    /// <summary>
    /// A-frame prism: triangular cross-section (base <paramref name="width"/> on the ground, apex at
    /// <paramref name="height"/>) extruded along local Z for <paramref name="length"/>, rotated by
    /// <paramref name="yaw"/> about +Y, with the centre of its footprint at <paramref name="baseCenter"/>.
    /// </summary>
    public static ConvexShape Wedge(Vector3 baseCenter, float yaw, float width, float height, float length) =>
        Wedge(baseCenter, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw), width, height, length);

    /// <summary>The same prism turned by <paramref name="q"/> about its footprint's centre (a level's wedge piece).</summary>
    public static ConvexShape Wedge(Vector3 baseCenter, Quaternion q, float width, float height, float length)
    {
        float hw = width * 0.5f;
        float hl = length * 0.5f;

        Span<Vector3> localVertices = stackalloc Vector3[]
        {
            new(-hw, 0f, -hl), new(hw, 0f, -hl), new(0f, height, -hl),
            new(-hw, 0f, hl), new(hw, 0f, hl), new(0f, height, hl),
        };

        Vector3 rightNormal = Vector3.Normalize(new Vector3(height, hw, 0f));
        Vector3 leftNormal = Vector3.Normalize(new Vector3(-height, hw, 0f));
        Span<Vector3> localNormals = stackalloc Vector3[]
        {
            -Vector3.UnitY, -Vector3.UnitZ, Vector3.UnitZ, rightNormal, leftNormal,
        };
        Span<float> localOffsets = stackalloc float[] { 0f, hl, hl, rightNormal.X * hw, -leftNormal.X * hw };

        Span<Vector3> normals = stackalloc Vector3[localNormals.Length];
        Span<float> offsets = stackalloc float[localNormals.Length];
        for (int i = 0; i < localNormals.Length; i++)
        {
            normals[i] = Vector3.Transform(localNormals[i], q);
            offsets[i] = localOffsets[i] + Vector3.Dot(normals[i], baseCenter);
        }

        Span<Vector3> vertices = stackalloc Vector3[localVertices.Length];
        for (int i = 0; i < localVertices.Length; i++)
        {
            vertices[i] = Vector3.Transform(localVertices[i], q) + baseCenter;
        }

        return new ConvexShape(normals, offsets, vertices);
    }

    public override bool Sweep(Vector3 p0, Vector3 d, float r, out float t, out Vector3 normal)
    {
        float enter = float.NegativeInfinity;
        float exit = float.PositiveInfinity;
        int enterPlane = -1;
        int nearestPlane = 0;
        float nearestDistance = float.NegativeInfinity;
        t = 0f;
        normal = default;

        for (int i = 0; i < _normals.Length; i++)
        {
            Vector3 n = _normals[i];
            float distance0 = Vector3.Dot(n, p0) - (_offsets[i] + r);
            if (distance0 > nearestDistance)
            {
                nearestDistance = distance0;
                nearestPlane = i;
            }

            float approach = Vector3.Dot(n, d);
            if (MathF.Abs(approach) < 1e-12f)
            {
                if (distance0 > 0f)
                {
                    return false;
                }

                continue;
            }

            float s = -distance0 / approach;
            if (approach < 0f)
            {
                if (s > enter)
                {
                    enter = s;
                    enterPlane = i;
                }
            }
            else
            {
                exit = MathF.Min(exit, s);
            }

            if (enter > exit)
            {
                return false;
            }
        }

        if (exit < 0f)
        {
            return false;
        }

        if (enterPlane < 0 || enter < 0f)
        {
            normal = _normals[nearestPlane];
            return true;
        }

        if (enter > 1f)
        {
            return false;
        }

        t = enter;
        normal = _normals[enterPlane];
        return true;
    }
}
