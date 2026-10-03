using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Collision;
using Pb.Sim.Range;

namespace Pb.Game.World;

/// <summary>
/// The training ground's props built in code for <see cref="RangeBuilder"/>, each in its own frame
/// (feet or footprint centre at the origin) on a <see cref="ShapeMesh"/>:
/// <list type="bullet">
/// <item>inflatable bunkers over their colliders (a can, a brick, a wedge), with seams, tethers and pegs;</item>
/// <item>a thin board on stakes;</item>
/// <item>a target dummy round its hitbox: a padded body on a weighted tyre (or a trolley, if it moves) and a head in a mask;</item>
/// <item>a moving target's track.</item>
/// </list>
/// Faces sit on or just inside the colliders, so paint lands on what you see.
/// </summary>
public static class RangeShapes
{
    /// <summary>An inflatable's fabric (in its colour), its seams (a shade darker), and its tethers and pegs.</summary>
    public readonly record struct InflatableMaterials(int Fabric, int Seams, int Tethers);

    /// <summary>A dummy's padded body, straps, head, mask, lens, tyre and its concrete, and a moving one's trolley.</summary>
    public readonly record struct DummyMaterials(int Body, int Straps, int Head, int Mask, int Lens, int Base, int Fill, int Trolley);

    private const float SeamRadius = 0.006f;

    /// <summary>A rails-and-sleepers track this wide (between the rails) under a moving target.</summary>
    private const float Gauge = 0.36f;

    public static void Inflatable(ShapeMesh m, PropSpec prop, InflatableMaterials mat)
    {
        Vector3 size = prop.Size.ToGodot();
        switch (prop.Kind)
        {
            case PropShapeKind.Cylinder:
                Can(m, size.X * 0.5f, size.Y, mat);
                break;
            case PropShapeKind.Box:
                Brick(m, size, mat);
                break;
            case PropShapeKind.Wedge:
                Wedge(m, size, mat);
                break;
        }
    }

    /// <summary>A thin board standing on its edge, held up by two stakes behind it with braces to the ground.</summary>
    public static void Panel(ShapeMesh m, PropSpec prop, int board, int stakes)
    {
        Vector3 size = prop.Size.ToGodot();
        m.Box(board, new Vector3(0f, size.Y * 0.5f, 0f), size);
        float back = -size.Z * 0.5f - 0.02f, top = size.Y * 0.8f;
        foreach (float side in new[] { -1f, 1f })
        {
            float x = side * size.X * 0.32f;
            m.Box(stakes, new Vector3(x, top * 0.5f - 0.05f, back), new Vector3(0.035f, top + 0.1f, 0.035f));
            m.Bar(stakes, new Vector3(x, top * 0.45f, back - 0.01f), new Vector3(x, 0f, back - top * 0.45f), 0.03f, 0.022f);
        }
    }

    /// <summary>
    /// A dummy built round its kind's hitbox parts: the body capsule padded, the mask sphere a head in a
    /// mask (its front −Z, as players face), anything else as plain padding. It stands on a tyre filled
    /// with concrete, or on a trolley running along <paramref name="along"/> if it moves.
    /// </summary>
    public static void Dummy(ShapeMesh m, TargetKindSpec kind, bool moving, Vector3 along, DummyMaterials mat)
    {
        float standTop = moving ? Trolley(m, along, mat) : Tyre(m, mat);
        foreach (TargetPartSpec part in kind.Parts)
        {
            switch (part.Kind)
            {
                case PartShapeKind.Capsule:
                    Body(m, part.From.ToGodot(), part.To.ToGodot(), part.Radius, standTop, mat);
                    break;
                case PartShapeKind.Sphere when part.Part == HitboxPart.Mask:
                    Head(m, part.Center.ToGodot(), part.Radius, mat);
                    break;
                case PartShapeKind.Sphere:
                    m.Pillow(mat.Body, part.Center.ToGodot(), Vector3.One * part.Radius * 2f, Basis.Identity, 2f, 8, 14);
                    break;
                case PartShapeKind.Box:
                    m.Pillow(mat.Body, part.Center.ToGodot(), part.Size.ToGodot(), Basis.Identity, 8f, 8, 14);
                    break;
            }
        }
    }

    /// <summary>
    /// Two rails and the sleepers under them along a moving target's run (world space): from one end of
    /// its swing to the other, with a little to spare.
    /// </summary>
    public static void Track(ShapeMesh m, TargetSpec target, int rails, int sleepers)
    {
        if (target.Motion is not { } motion)
        {
            return;
        }

        Vector3 axis = motion.Axis.ToGodot();
        axis = new Vector3(axis.X, 0f, axis.Z).Normalized();
        Vector3 across = axis.Cross(Vector3.Up).Normalized();
        Vector3 middle = target.BasePosition.ToGodot();
        float reach = motion.Amplitude + 0.7f;
        var basis = new Basis(axis, Vector3.Up, axis.Cross(Vector3.Up));
        for (float s = -reach; s <= reach + 0.01f; s += 0.6f)
        {
            m.Box(sleepers, middle + axis * s + Vector3.Up * 0.015f, new Vector3(0.12f, 0.035f, Gauge + 0.28f), basis);
        }

        foreach (float side in new[] { -0.5f, 0.5f })
        {
            Vector3 offset = across * (side * Gauge) + Vector3.Up * 0.055f;
            m.Box(rails, middle + offset, new Vector3(reach * 2f, 0.04f, 0.035f), basis);
        }

        // Buffers at each end.
        foreach (float end in new[] { -reach, reach })
        {
            m.Box(rails, middle + axis * end + Vector3.Up * 0.11f, new Vector3(0.08f, 0.16f, Gauge + 0.12f), basis);
        }
    }

    /// <summary>An upright inflatable cylinder, its edges rounded, with seams down it and round both ends, tethered at four points.</summary>
    private static void Can(ShapeMesh m, float r, float h, InflatableMaterials mat)
    {
        float e = MathF.Min(0.12f, MathF.Min(r * 0.25f, h * 0.2f));
        var profile = new List<Vector2> { new(0f, 0f) };
        Arc(profile, new Vector2(r - e, e), e, -Mathf.Pi / 2f, 0f, 5);
        Arc(profile, new Vector2(r - e, h - e), e, 0f, Mathf.Pi / 2f, 5);
        profile.Add(new Vector2(0f, h));
        m.Lathe(mat.Fabric, Vector3.Zero, Basis.Identity, profile, 36);

        for (int k = 0; k < 6; k++)
        {
            float a = Mathf.Tau * k / 6f + 0.3f;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            m.Rod(mat.Seams, d * (r + 0.001f) + Vector3.Up * e, d * (r + 0.001f) + Vector3.Up * (h - e), SeamRadius, 4, caps: false);
        }

        // Round the top and bottom panels, where the rounding meets the side.
        foreach (float y in new[] { e, h - e })
        {
            m.Lathe(mat.Seams, Vector3.Up * y, Basis.Identity, new[] { new Vector2(r + 0.003f, -0.007f), new Vector2(r + 0.006f, 0f), new Vector2(r + 0.003f, 0.007f) }, 36);
        }

        for (int k = 0; k < 4; k++)
        {
            float a = Mathf.Tau * (k + 0.5f) / 4f + 0.3f;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Tether(m, mat.Tethers, d * r + Vector3.Up * 0.13f, d, 0.38f);
        }
    }

    /// <summary>An inflatable block: a rounded box with seams down its upright edges and round its top and bottom, tethered at its corners.</summary>
    private static void Brick(ShapeMesh m, Vector3 size, InflatableMaterials mat)
    {
        const float Squareness = 9f;
        Vector3 h = size * 0.5f;
        var center = new Vector3(0f, h.Y, 0f);
        m.Pillow(mat.Fabric, center, size, Basis.Identity, Squareness, 14, 24);

        var seam = new List<Vector3>();
        for (int k = 0; k < 4; k++)
        {
            float lon = Mathf.Pi * (0.25f + 0.5f * k);
            seam.Clear();
            for (int i = 0; i <= 12; i++)
            {
                seam.Add(Super(center, h, Squareness, Mathf.Lerp(-0.42f, 0.42f, i / 12f) * Mathf.Pi, lon, 0.003f));
            }

            Seam(m, mat.Seams, seam, closed: false);
            Tether(m, mat.Tethers, Super(center, h, Squareness, -0.2f * Mathf.Pi, lon, 0f), new Vector3(Mathf.Cos(lon), 0f, Mathf.Sin(lon)), 0.38f);
        }

        foreach (float lat in new[] { -0.25f * Mathf.Pi, 0.25f * Mathf.Pi })
        {
            seam.Clear();
            for (int i = 0; i < 32; i++)
            {
                seam.Add(Super(center, h, Squareness, lat, Mathf.Tau * i / 32f, 0.003f));
            }

            Seam(m, mat.Seams, seam, closed: true);
        }
    }

    /// <summary>
    /// An inflatable wedge: a rounded triangle (its apex up, as the collider's) run along Z, with seams
    /// along its three edges and round both ends, tethered at its four bottom corners.
    /// </summary>
    private static void Wedge(ShapeMesh m, Vector3 size, InflatableMaterials mat)
    {
        float hw = size.X * 0.5f, hz = size.Z * 0.5f;
        var corners = new[] { new Vector2(-hw, 0f), new Vector2(hw, 0f), new Vector2(0f, size.Y) };
        List<Vector2> outline = Rounded(corners, new[] { 0.06f, 0.06f, 0.1f }, 5, out Vector2[] edges);
        m.Extrude(mat.Fabric, Vector3.Zero, Basis.Identity, outline, size.Z);

        foreach (Vector2 edge in edges)
        {
            m.Rod(mat.Seams, new Vector3(edge.X, edge.Y, -hz + 0.02f), new Vector3(edge.X, edge.Y, hz - 0.02f), SeamRadius, 4, caps: false);
        }

        var ring = new List<Vector3>();
        foreach (float z in new[] { -hz, hz })
        {
            ring.Clear();
            foreach (Vector2 p in Grow(outline, 0.002f))
            {
                ring.Add(new Vector3(p.X, p.Y, z + 0.004f * MathF.Sign(z)));
            }

            Seam(m, mat.Seams, ring, closed: true);
            foreach (float side in new[] { -1f, 1f })
            {
                Tether(m, mat.Tethers, new Vector3(side * (hw - 0.12f), 0.1f, z), new Vector3(side * 0.6f, 0f, MathF.Sign(z)).Normalized(), 0.36f);
            }
        }
    }

    /// <summary>A strap from a loop on the fabric down and out to a peg in the ground.</summary>
    private static void Tether(ShapeMesh m, int material, Vector3 from, Vector3 outward, float reach)
    {
        Vector3 peg = new Vector3(from.X, 0f, from.Z) + outward * reach;
        m.Rod(material, from - Vector3.Up * 0.03f, from + Vector3.Up * 0.03f, 0.012f, 5);
        m.Bar(material, from, peg + Vector3.Up * 0.05f, 0.024f, 0.005f);
        m.Rod(material, peg + Vector3.Up * 0.07f, peg - Vector3.Up * 0.03f, 0.007f, 5);
        m.Rod(material, peg + Vector3.Up * 0.07f, peg + Vector3.Up * 0.07f + outward * 0.035f, 0.006f, 4);
    }

    /// <summary>A bead along a run of points on a surface.</summary>
    private static void Seam(ShapeMesh m, int material, List<Vector3> points, bool closed)
    {
        int n = closed ? points.Count : points.Count - 1;
        for (int i = 0; i < n; i++)
        {
            m.Rod(material, points[i], points[(i + 1) % points.Count], SeamRadius, 4, caps: false);
        }
    }

    /// <summary>
    /// A point on the superellipsoid <see cref="ShapeMesh.Pillow"/> draws (half extents <paramref name="h"/>,
    /// <paramref name="squareness"/>) at a latitude and longitude, pushed <paramref name="lift"/> out along its normal.
    /// </summary>
    private static Vector3 Super(Vector3 center, Vector3 h, float squareness, float lat, float lon, float lift)
    {
        float e = 2f / squareness, g = 2f - e;
        float cl = Mathf.Cos(lat), sl = Mathf.Sin(lat), co = Mathf.Cos(lon), so = Mathf.Sin(lon);
        static float SPow(float v, float p) => MathF.Sign(v) * MathF.Pow(MathF.Abs(v), p);
        var unit = new Vector3(SPow(cl, e) * SPow(co, e), SPow(sl, e), SPow(cl, e) * SPow(so, e));
        var grad = new Vector3(SPow(cl, g) * SPow(co, g) / h.X, SPow(sl, g) / h.Y, SPow(cl, g) * SPow(so, g) / h.Z);
        Vector3 normal = grad.LengthSquared() > 1e-12f ? grad.Normalized() : Vector3.Up;
        return center + unit * h + normal * lift;
    }

    /// <summary>
    /// A padded column round a capsule hitbox, from the top of its stand (if the capsule stands upright)
    /// to just inside its top, its ends rounded, with two straps round it.
    /// </summary>
    private static void Body(ShapeMesh m, Vector3 from, Vector3 to, float radius, float standTop, DummyMaterials mat)
    {
        Vector3 axis = to - from;
        float half = axis.Length() * 0.5f;
        Vector3 middle = (from + to) * 0.5f;
        Basis basis = half > 1e-4f ? ShapeMesh.BasisAlong(axis / (half * 2f)) : Basis.Identity;
        bool upright = basis.Y.Dot(Vector3.Up) > 0.99f;
        float y0 = upright ? MathF.Max(-half - radius, standTop - middle.Y) : -half - radius * 0.85f;
        float y1 = half + radius * 0.85f;
        float r = radius * 0.98f, e = MathF.Min(0.08f, r * 0.45f);
        var profile = new List<Vector2> { new(0f, y0) };
        Arc(profile, new Vector2(r - e, y0 + e), e, -Mathf.Pi / 2f, 0f, 4);
        Arc(profile, new Vector2(r - e, y1 - e), e, 0f, Mathf.Pi / 2f, 5);
        profile.Add(new Vector2(0f, y1));
        m.Lathe(mat.Body, middle, basis, profile, 24);

        foreach (float t in new[] { 0.3f, 0.72f })
        {
            float y = Mathf.Lerp(y0 + e, y1 - e, t);
            m.Lathe(mat.Straps, middle, basis, new[] { new Vector2(r + 0.002f, y - 0.024f), new Vector2(r + 0.006f, y - 0.018f), new Vector2(r + 0.006f, y + 0.018f), new Vector2(r + 0.002f, y + 0.024f) }, 24);
        }
    }

    /// <summary>
    /// A head in a paintball mask, its front −Z: goggles (a frame behind a lens, a peak over them), a
    /// vented mouth guard flaring up to them, ear pieces, and the strap round the back.
    /// </summary>
    private static void Head(ShapeMesh m, Vector3 c, float r, DummyMaterials mat)
    {
        m.Pillow(mat.Head, c, new Vector3(1.94f, 2.06f, 1.94f) * r, Basis.Identity, 2f, 10, 16);
        const float Front = -Mathf.Pi / 2f;
        Basis up = Basis.Identity;
        // The goggles' frame, then the lens a little proud of it.
        m.Lathe(mat.Mask, c, up, new[] { new Vector2(r * 0.98f, -0.03f), new Vector2(r * 1.08f, -0.024f), new Vector2(r * 1.08f, 0.06f), new Vector2(r * 0.98f, 0.066f) }, 18, Front - 1.3f, Front + 1.3f);
        m.Lathe(mat.Lens, c, up, new[] { new Vector2(r * 1.085f, -0.014f), new Vector2(r * 1.1f, 0.019f), new Vector2(r * 1.085f, 0.05f) }, 16, Front - 1.05f, Front + 1.05f);
        // The peak over the goggles: its top and underside.
        m.Lathe(mat.Mask, c + Vector3.Up * 0.067f, up, new[] { new Vector2(r * 1.22f, 0f), new Vector2(r * 0.96f, 0.012f) }, 16, Front - 1.1f, Front + 1.1f);
        m.Lathe(mat.Mask, c + Vector3.Up * 0.067f, up, new[] { new Vector2(r * 0.96f, -0.004f), new Vector2(r * 1.22f, -0.002f) }, 16, Front - 1.1f, Front + 1.1f);
        // The mouth guard, narrowing to the chin, with three vent slots across its front.
        m.Lathe(mat.Mask, c, up, new[] { new Vector2(r * 0.7f, -0.13f), new Vector2(r * 0.86f, -0.122f), new Vector2(r * 1.05f, -0.06f), new Vector2(r * 1.07f, -0.03f) }, 16, Front - 1.0f, Front + 1.0f);
        for (int i = 0; i < 3; i++)
        {
            float y = -0.105f + 0.019f * i;
            float reach = Mathf.Lerp(r * 0.9f, r * 1.04f, (y + 0.122f) / 0.092f);
            m.Box(mat.Straps, c + new Vector3(0f, y, -reach - 0.004f), new Vector3(0.05f - 0.006f * i, 0.006f, 0.01f));
        }

        foreach (float side in new[] { -1f, 1f })
        {
            m.Cylinder(mat.Mask, c + new Vector3(side * r * 1.0f, 0.004f, 0.012f), ShapeMesh.BasisAlong(Vector3.Right), 0.036f, 0.024f, 12);
        }

        m.Lathe(mat.Straps, c, up, new[] { new Vector2(r * 1.01f, 0.004f), new Vector2(r * 1.02f, 0.02f), new Vector2(r * 1.01f, 0.036f) }, 20, Front + 1.25f, Front + Mathf.Tau - 1.25f);
    }

    /// <summary>A tyre lying flat, filled with concrete with the dummy's post set in it; returns the height the body starts at.</summary>
    private static float Tyre(ShapeMesh m, DummyMaterials mat)
    {
        // Round its section: up the outside, in over the top, down the inside, out under the bottom.
        var section = new List<Vector2>
        {
            new(0.24f, 0f), new(0.29f, 0.012f), new(0.31f, 0.05f), new(0.31f, 0.12f), new(0.29f, 0.158f), new(0.24f, 0.17f),
            new(0.19f, 0.162f), new(0.175f, 0.14f), new(0.175f, 0.03f), new(0.19f, 0.008f), new(0.24f, 0f),
        };
        m.Lathe(mat.Base, Vector3.Zero, Basis.Identity, section, 28);
        m.Lathe(mat.Fill, Vector3.Zero, Basis.Identity, new[] { new Vector2(0.178f, 0.13f), new Vector2(0f, 0.135f) }, 20);
        m.Cylinder(mat.Trolley, new Vector3(0f, 0.16f, 0f), Basis.Identity, 0.035f, 0.06f, 10);
        return 0.17f;
    }

    /// <summary>A flat trolley on four wheels running on the track along <paramref name="along"/>; returns the height the body starts at.</summary>
    private static float Trolley(ShapeMesh m, Vector3 along, DummyMaterials mat)
    {
        along = new Vector3(along.X, 0f, along.Z).Normalized();
        Vector3 across = along.Cross(Vector3.Up).Normalized();
        var basis = new Basis(along, Vector3.Up, along.Cross(Vector3.Up));
        m.Box(mat.Trolley, new Vector3(0f, 0.185f, 0f), new Vector3(0.52f, 0.03f, Gauge + 0.12f), basis);
        m.Box(mat.Trolley, new Vector3(0f, 0.215f, 0f), new Vector3(0.24f, 0.03f, 0.24f), basis);
        foreach (float a in new[] { -0.17f, 0.17f })
        {
            foreach (float b in new[] { -0.5f, 0.5f })
            {
                m.Cylinder(mat.Base, along * a + across * (b * Gauge) + Vector3.Up * 0.12f, ShapeMesh.BasisAlong(across), 0.045f, 0.03f, 12);
            }
        }

        return 0.23f;
    }

    /// <summary>Points round an arc about <paramref name="center"/> from angle a0 to a1 (radians), both ends included.</summary>
    private static void Arc(List<Vector2> points, Vector2 center, float radius, float a0, float a1, int steps)
    {
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.Lerp(a0, a1, (float)i / steps);
            points.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
        }
    }

    /// <summary>
    /// A counter-clockwise polygon with each corner rounded by its radius; <paramref name="edges"/>
    /// gets the middle of each rounded corner (where an edge seam runs).
    /// </summary>
    private static List<Vector2> Rounded(Vector2[] corners, float[] radii, int steps, out Vector2[] edges)
    {
        var outline = new List<Vector2>();
        edges = new Vector2[corners.Length];
        int n = corners.Length;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = corners[i], prev = corners[(i + n - 1) % n], next = corners[(i + 1) % n];
            Vector2 a = (prev - p).Normalized(), b = (next - p).Normalized();
            float half = MathF.Acos(Mathf.Clamp(a.Dot(b), -1f, 1f)) * 0.5f;
            float r = radii[i];
            // The arc's centre sits on the bisector; its ends touch the two edges.
            Vector2 center = p + (a + b).Normalized() * (r / MathF.Sin(half));
            Vector2 from = p + a * (r / MathF.Tan(half)), to = p + b * (r / MathF.Tan(half));
            float a0 = MathF.Atan2(from.Y - center.Y, from.X - center.X), a1 = MathF.Atan2(to.Y - center.Y, to.X - center.X);
            // Counter-clockwise outline: the arc turns left, so its angle grows.
            while (a1 < a0)
            {
                a1 += Mathf.Tau;
            }

            Arc(outline, center, r, a0, a1, steps);
            float mid = (a0 + a1) * 0.5f;
            edges[i] = center + new Vector2(Mathf.Cos(mid), Mathf.Sin(mid)) * (r + 0.002f);
        }

        return outline;
    }

    /// <summary>A closed outline pushed out by <paramref name="by"/> along its corners' bisectors.</summary>
    private static IEnumerable<Vector2> Grow(List<Vector2> outline, float by)
    {
        int n = outline.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 prev = outline[(i + n - 1) % n], p = outline[i], next = outline[(i + 1) % n];
            Vector2 e0 = (p - prev).Normalized(), e1 = (next - p).Normalized();
            // Outward normals of a counter-clockwise outline point right of each edge.
            Vector2 n0 = new(e0.Y, -e0.X), n1 = new(e1.Y, -e1.X);
            Vector2 dir = (n0 + n1).LengthSquared() > 1e-8f ? (n0 + n1).Normalized() : n0;
            yield return p + dir * by;
        }
    }
}
