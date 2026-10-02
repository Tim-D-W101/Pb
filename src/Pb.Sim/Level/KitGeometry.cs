using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Data;

namespace Pb.Sim.Level;

/// <summary>Where a building or prop sits: the world position of its local origin and a yaw about +Y.</summary>
public readonly struct PlanFrame
{
    public PlanFrame(Vector3 origin, float yaw)
    {
        Origin = origin;
        Yaw = yaw;
        Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
    }

    public Vector3 Origin { get; }

    public float Yaw { get; }

    public Quaternion Rotation { get; }

    public static PlanFrame Identity => new(Vector3.Zero, 0f);

    public Vector3 ToWorld(Vector3 local) => Origin + Vector3.Transform(local, Rotation);

    /// <summary>A rotation expressed in this frame, as a world rotation (local first, then the frame's yaw).</summary>
    public Quaternion ToWorld(Quaternion local) => Quaternion.Normalize(Quaternion.Concatenate(local, Rotation));

    /// <summary>Plan point [x, z] at elevation <paramref name="y"/> in this frame.</summary>
    public Vector3 PlanToWorld(Vector2 plan, float y) => ToWorld(new Vector3(plan.X, y, plan.Y));
}

public readonly record struct MaterialRef(int Index, SurfaceId Surface);

/// <summary>One opening in a wall segment, in metres along the segment's centreline.</summary>
public readonly record struct OpeningSpec(int Segment, float At, float Width, float Sill, float Height, OpeningKind Kind);

/// <summary>Collects generated primitives for a level.</summary>
public sealed class PrimitiveSink
{
    private const float MinExtent = 1e-4f;

    public List<LevelPrimitive> Items { get; } = new();

    /// <summary>Openings through walls and roofs, in world space.</summary>
    public List<Aperture> Apertures { get; } = new();

    public void AddBox(PlanFrame frame, Vector3 center, Quaternion rotation, Vector3 half, MaterialRef material,
        PrimitiveFlags flags, PrimitiveRole role, int owner)
    {
        if (half.X < MinExtent || half.Y < MinExtent || half.Z < MinExtent)
        {
            return;
        }

        Items.Add(new LevelPrimitive
        {
            Kind = PrimitiveKind.Box,
            Center = frame.ToWorld(center),
            Rotation = frame.ToWorld(rotation),
            HalfExtents = half,
            Material = material.Index,
            Surface = material.Surface,
            Flags = flags,
            Role = role,
            Owner = owner,
        });
    }

    public void AddCylinder(PlanFrame frame, Vector3 center, Quaternion rotation, float radius, float halfHeight,
        MaterialRef material, PrimitiveFlags flags, PrimitiveRole role, int owner)
    {
        if (radius < MinExtent || halfHeight < MinExtent)
        {
            return;
        }

        Items.Add(new LevelPrimitive
        {
            Kind = PrimitiveKind.Cylinder,
            Center = frame.ToWorld(center),
            Rotation = frame.ToWorld(rotation),
            HalfExtents = new Vector3(radius, halfHeight, radius),
            Material = material.Index,
            Surface = material.Surface,
            Flags = flags,
            Role = role,
            Owner = owner,
        });
    }
}

/// <summary>
/// Turns kit elements (wall runs, slabs, stairs, columns) into boxes. Everything is generated in the
/// element's frame and transformed once, so a building can be placed at any yaw.
/// </summary>
public static class KitGeometry
{
    /// <summary>Pieces at least this tall (from the floor they stand on) count as cover.</summary>
    public const float CoverHeight = 0.9f;

    /// <summary>Wall pieces at least this tall and this long hide what's behind them (occlusion culling).</summary>
    public const float OccluderMinHeight = 2f;

    public const float OccluderMinLength = 1f;

    /// <summary>Railings: mid rail height as a fraction of the railing's height, and toe board height (m).</summary>
    public const float RailingMidRail = 0.5f;

    public const float RailingToeBoard = 0.1f;

    private const float Epsilon = 1e-3f;

    /// <summary>Builds a wall run in its style (solid wall or open railing).</summary>
    public static void WallRun(PrimitiveSink sink, PlanFrame frame, WallDef wall, float baseY, float height, MaterialRef material, int owner)
    {
        if (wall.Style == WallStyle.Railing)
        {
            Railing(sink, frame, Points(wall), wall.Closed, wall.Thickness_m, wall.PostSpacing_m, baseY, height, material, Openings(wall), owner);
        }
        else
        {
            Wall(sink, frame, Points(wall), wall.Closed, wall.Thickness_m, baseY, height, material, Openings(wall), owner);
        }
    }

    /// <summary>
    /// Builds an open railing: posts on the corners, beside each gap and at most
    /// <paramref name="postSpacing"/> apart, joined by a top rail, a mid rail and a toe board. Every
    /// member is <paramref name="size"/> thick (the toe board is thinner). Railings block walking but
    /// aren't cover or occluders.
    /// </summary>
    public static void Railing(PrimitiveSink sink, PlanFrame frame, IReadOnlyList<Vector2> points, bool closed, float size,
        float postSpacing, float baseY, float height, MaterialRef material, IReadOnlyList<OpeningSpec> openings, int owner)
    {
        const PrimitiveFlags flags = PrimitiveFlags.Paint | PrimitiveFlags.Walk | PrimitiveFlags.Render;
        int n = points.Count;
        int segments = closed ? n : n - 1;
        float half = size * 0.5f;
        float toeThickness = MathF.Min(size, 0.02f);
        var spans = new List<(float From, float To, bool PostAtFrom, bool PostAtTo)>();

        for (int s = 0; s < segments; s++)
        {
            Vector2 a = points[s];
            Vector2 b = points[(s + 1) % n];
            float length = Vector2.Distance(a, b);
            if (length < Epsilon)
            {
                continue;
            }

            Vector2 u = (b - a) / length;
            Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(-u.Y, u.X));

            // Solid spans between gaps. Corner posts sit on the corner point and belong to the
            // segment that ends there (the first segment of an open run also gets one at its start).
            spans.Clear();
            float cursor = 0f;
            bool postAtCursor = s == 0 && !closed;
            foreach (OpeningSpec o in openings.Where(o => o.Segment == s).OrderBy(o => o.At))
            {
                spans.Add((cursor, o.At - o.Width * 0.5f, postAtCursor, true));
                cursor = o.At + o.Width * 0.5f;
                postAtCursor = true;
            }

            spans.Add((cursor, length, postAtCursor, true));

            foreach ((float from, float to, bool postAtFrom, bool postAtTo) in spans)
            {
                if (to - from < Epsilon)
                {
                    continue;
                }

                // A post beside a gap stands just inside the span; one on a corner stands on the point.
                bool fromIsCorner = from <= Epsilon;
                bool toIsCorner = to >= length - Epsilon;
                float p0 = fromIsCorner ? 0f : from + half;
                float p1 = toIsCorner ? length : to - half;
                int bays = Math.Max(1, (int)MathF.Ceiling((p1 - p0) / postSpacing - 1e-4f));
                for (int k = 0; k <= bays; k++)
                {
                    if ((k == 0 && !postAtFrom) || (k == bays && !postAtTo))
                    {
                        continue;
                    }

                    float at = p0 + (p1 - p0) * k / bays;
                    Vector2 p = a + u * at;
                    sink.AddBox(frame, new Vector3(p.X, baseY + height * 0.5f, p.Y), rotation,
                        new Vector3(half, height * 0.5f, half), material, flags, PrimitiveRole.Wall, owner);
                }

                // Rails run the span's length, reaching the far side of corner posts so corners meet.
                float r0 = fromIsCorner && (closed || s > 0) ? -half : from;
                float r1 = toIsCorner && (closed || s < segments - 1) ? length + half : to;
                Vector2 mid = a + u * ((r0 + r1) * 0.5f);
                float railHalf = (r1 - r0) * 0.5f;
                void Rail(float centerY, float halfHeight, float halfThickness) =>
                    sink.AddBox(frame, new Vector3(mid.X, centerY, mid.Y), rotation, new Vector3(railHalf, halfHeight, halfThickness),
                        material, flags, PrimitiveRole.Wall, owner);

                Rail(baseY + height - half, half, half);
                Rail(baseY + height * RailingMidRail, half, half);
                Rail(baseY + RailingToeBoard * 0.5f, RailingToeBoard * 0.5f, toeThickness * 0.5f);
            }
        }
    }

    /// <summary>
    /// Builds a wall run. Segments are extended by half the thickness at every joint so corners are
    /// solid; the overlap is invisible because both pieces share a material and world-space mapping.
    /// </summary>
    public static void Wall(PrimitiveSink sink, PlanFrame frame, IReadOnlyList<Vector2> points, bool closed, float thickness,
        float baseY, float height, MaterialRef material, IReadOnlyList<OpeningSpec> openings, int owner)
    {
        int n = points.Count;
        int segments = closed ? n : n - 1;
        float topY = baseY + height;
        var sorted = new List<OpeningSpec>();

        for (int s = 0; s < segments; s++)
        {
            Vector2 a = points[s];
            Vector2 b = points[(s + 1) % n];
            Vector2 along = b - a;
            float length = along.Length();
            if (length < Epsilon)
            {
                continue;
            }

            Vector2 u = along / length;
            float extStart = closed || s > 0 ? thickness * 0.5f : 0f;
            float extEnd = closed || s < segments - 1 ? thickness * 0.5f : 0f;
            Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(-u.Y, u.X));

            sorted.Clear();
            foreach (OpeningSpec o in openings)
            {
                if (o.Segment == s)
                {
                    sorted.Add(o);
                }
            }

            sorted.Sort((x, y) => x.At.CompareTo(y.At));

            void Piece(float s0, float s1, float y0, float y1)
            {
                if (s1 - s0 < Epsilon || y1 - y0 < Epsilon)
                {
                    return;
                }

                Vector2 mid = a + u * ((s0 + s1) * 0.5f);
                var flags = PrimitiveFlags.Paint | PrimitiveFlags.Walk | PrimitiveFlags.Render;
                if (y1 - y0 >= OccluderMinHeight && s1 - s0 >= OccluderMinLength)
                {
                    flags |= PrimitiveFlags.Occluder;
                }

                if (y0 <= baseY + Epsilon && y1 - y0 >= CoverHeight)
                {
                    flags |= PrimitiveFlags.Cover;
                }

                sink.AddBox(frame, new Vector3(mid.X, (y0 + y1) * 0.5f, mid.Y), rotation,
                    new Vector3((s1 - s0) * 0.5f, (y1 - y0) * 0.5f, thickness * 0.5f), material, flags, PrimitiveRole.Wall, owner);
            }

            float cursor = -extStart;
            foreach (OpeningSpec o in sorted)
            {
                float o0 = o.At - o.Width * 0.5f;
                float o1 = o.At + o.Width * 0.5f;
                Piece(cursor, o0, baseY, topY);
                float sillTop = baseY + o.Sill;
                float openingTop = float.IsNaN(o.Height) ? topY : MathF.Min(topY, sillTop + o.Height);
                Piece(o0, o1, baseY, sillTop);
                Piece(o0, o1, openingTop, topY);
                cursor = o1;
                if (openingTop - sillTop > Epsilon && o.Width > Epsilon)
                {
                    Vector3 axis = Vector3.Transform(new Vector3(u.X, 0f, u.Y), frame.Rotation);
                    sink.Apertures.Add(new Aperture(ApertureOf(o.Kind), frame.PlanToWorld(a + u * o.At, (sillTop + openingTop) * 0.5f),
                        axis, Vector3.UnitY, o.Width * 0.5f, (openingTop - sillTop) * 0.5f, owner));
                }
            }

            Piece(cursor, length + extEnd, baseY, topY);
        }
    }

    private static ApertureKind ApertureOf(OpeningKind kind) => kind switch
    {
        OpeningKind.Door => ApertureKind.Door,
        OpeningKind.Window => ApertureKind.Window,
        _ => ApertureKind.Gap,
    };

    /// <summary>
    /// Splits <paramref name="rect"/> minus <paramref name="holes"/> into rectangles: vertical strips at
    /// every hole edge, with identical neighbouring strips merged. Rects are (x0, z0, x1, z1).
    /// </summary>
    public static List<Vector4> SubtractHoles(Vector4 rect, IReadOnlyList<Vector4> holes)
    {
        var clipped = new List<Vector4>();
        foreach (Vector4 h in holes)
        {
            var c = new Vector4(MathF.Max(h.X, rect.X), MathF.Max(h.Y, rect.Y), MathF.Min(h.Z, rect.Z), MathF.Min(h.W, rect.W));
            if (c.Z - c.X > Epsilon && c.W - c.Y > Epsilon)
            {
                clipped.Add(c);
            }
        }

        var xs = new List<float> { rect.X, rect.Z };
        foreach (Vector4 h in clipped)
        {
            xs.Add(h.X);
            xs.Add(h.Z);
        }

        xs.Sort();
        var result = new List<Vector4>();
        var previous = new List<int>();
        var previousFree = new List<Vector2>();
        var free = new List<Vector2>();
        var blocked = new List<Vector2>();

        for (int i = 0; i < xs.Count - 1; i++)
        {
            float sx0 = xs[i];
            float sx1 = xs[i + 1];
            if (sx1 - sx0 < Epsilon)
            {
                continue;
            }

            blocked.Clear();
            foreach (Vector4 h in clipped)
            {
                if (h.X < sx1 - Epsilon && h.Z > sx0 + Epsilon)
                {
                    blocked.Add(new Vector2(h.Y, h.W));
                }
            }

            blocked.Sort((p, q) => p.X.CompareTo(q.X));
            free.Clear();
            float z = rect.Y;
            foreach (Vector2 interval in blocked)
            {
                if (interval.X > z + Epsilon)
                {
                    free.Add(new Vector2(z, interval.X));
                }

                z = MathF.Max(z, interval.Y);
            }

            if (rect.W > z + Epsilon)
            {
                free.Add(new Vector2(z, rect.W));
            }

            if (SameIntervals(free, previousFree) && previous.Count == free.Count && previous.Count > 0)
            {
                foreach (int index in previous)
                {
                    Vector4 r = result[index];
                    result[index] = new Vector4(r.X, r.Y, sx1, r.W);
                }

                continue;
            }

            previous.Clear();
            foreach (Vector2 interval in free)
            {
                previous.Add(result.Count);
                result.Add(new Vector4(sx0, interval.X, sx1, interval.Y));
            }

            previousFree.Clear();
            previousFree.AddRange(free);
        }

        return result;
    }

    /// <summary>A horizontal slab whose top surface is at <paramref name="topY"/>.</summary>
    public static void Slab(PrimitiveSink sink, PlanFrame frame, Vector4 rect, IReadOnlyList<Vector4> holes, float topY,
        float thickness, MaterialRef material, PrimitiveFlags flags, PrimitiveRole role, int owner)
    {
        foreach (Vector4 r in SubtractHoles(rect, holes))
        {
            var center = new Vector3((r.X + r.Z) * 0.5f, topY - thickness * 0.5f, (r.Y + r.W) * 0.5f);
            var half = new Vector3((r.Z - r.X) * 0.5f, thickness * 0.5f, (r.W - r.Y) * 0.5f);
            PrimitiveFlags f = flags;
            if ((f & PrimitiveFlags.Occluder) != 0 && (half.X < 1f || half.Z < 1f))
            {
                f &= ~PrimitiveFlags.Occluder;
            }

            sink.AddBox(frame, center, Quaternion.Identity, half, material, f, role, owner);
        }
    }

    /// <summary>
    /// A straight flight: solid stepped boxes for paint and looks, plus one invisible ramp for
    /// walking so the character glides up instead of catching on every step edge.
    /// </summary>
    public static void Stairs(PrimitiveSink sink, PlanFrame frame, Vector2 start, float yaw, float width, float run, float rise,
        int steps, float baseY, MaterialRef material, int owner)
    {
        Quaternion stairRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        var origin = new Vector3(start.X, baseY, start.Y);
        float depth = run / steps;
        float stepHeight = rise / steps;
        for (int k = 0; k < steps; k++)
        {
            float top = (k + 1) * stepHeight;
            var local = new Vector3(0f, top * 0.5f, -(k + 0.5f) * depth);
            sink.AddBox(frame, origin + Vector3.Transform(local, stairRotation), stairRotation,
                new Vector3(width * 0.5f, top * 0.5f, depth * 0.5f), material,
                PrimitiveFlags.Paint | PrimitiveFlags.Render, PrimitiveRole.Stair, owner);
        }

        // The ramp's top surface runs through the middle of every tread: from the ground half a tread in
        // front of the first step to the top tread's middle, so feet sink at most half a step into the
        // drawn stairs. A flat piece over the rest of the top tread meets the floor above.
        const float rampThickness = 0.3f;
        float slopeLength = MathF.Sqrt(run * run + rise * rise);
        float angle = MathF.Atan2(rise, run);
        var normal = new Vector3(0f, run, rise) / slopeLength;
        Vector3 rampCenter = new Vector3(0f, rise * 0.5f, -(run - depth) * 0.5f) - normal * (rampThickness * 0.5f);
        Quaternion rampRotation = Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitX, angle), stairRotation);
        sink.AddBox(frame, origin + Vector3.Transform(rampCenter, stairRotation), rampRotation,
            new Vector3(width * 0.5f, rampThickness * 0.5f, slopeLength * 0.5f), material,
            PrimitiveFlags.Walk, PrimitiveRole.Ramp, owner);

        var topCenter = new Vector3(0f, rise - 0.05f, -(run - depth * 0.25f));
        sink.AddBox(frame, origin + Vector3.Transform(topCenter, stairRotation), stairRotation,
            new Vector3(width * 0.5f, 0.05f, depth * 0.25f), material, PrimitiveFlags.Walk, PrimitiveRole.Ramp, owner);
    }

    public static void Column(PrimitiveSink sink, PlanFrame frame, Vector2 at, Vector2 size, float baseY, float height,
        MaterialRef material, int owner)
    {
        var flags = PrimitiveFlags.Paint | PrimitiveFlags.Walk | PrimitiveFlags.Render;
        if (height >= CoverHeight && MathF.Min(size.X, size.Y) >= 0.3f)
        {
            flags |= PrimitiveFlags.Cover;
        }

        sink.AddBox(frame, new Vector3(at.X, baseY + height * 0.5f, at.Y), Quaternion.Identity,
            new Vector3(size.X * 0.5f, height * 0.5f, size.Y * 0.5f), material, flags, PrimitiveRole.Column, owner);
    }

    /// <summary>Places every collider of a prop. A prop with a model isn't drawn from its colliders.</summary>
    public static void Prop(PrimitiveSink sink, PlanFrame frame, PropType type, int owner)
    {
        foreach (PropColliderTemplate c in type.Colliders)
        {
            var flags = PrimitiveFlags.Paint;
            if (c.Walk)
            {
                flags |= PrimitiveFlags.Walk;
            }

            if (!type.HasModel)
            {
                flags |= PrimitiveFlags.Render;
            }

            float top = c.Kind == PrimitiveKind.Cylinder
                ? c.Center.Y + MathF.Abs(Vector3.Transform(Vector3.UnitY, c.Rotation).Y) * c.HalfExtents.Y
                : c.Center.Y + BoxHalfHeight(c.Rotation, c.HalfExtents);
            if (type.Def.Cover && top >= CoverHeight)
            {
                flags |= PrimitiveFlags.Cover;
            }

            if (c.Kind == PrimitiveKind.Cylinder)
            {
                sink.AddCylinder(frame, c.Center, c.Rotation, c.HalfExtents.X, c.HalfExtents.Y, c.Material, flags, PrimitiveRole.Prop, owner);
            }
            else
            {
                sink.AddBox(frame, c.Center, c.Rotation, c.HalfExtents, c.Material, flags, PrimitiveRole.Prop, owner);
            }
        }
    }

    /// <summary>
    /// Checks a wall run against its own geometry: openings must fit inside their segment without
    /// overlapping, and corners may turn by at most 90°.
    /// </summary>
    public static void CheckWall(WallDef wall, float height, Validator v)
    {
        if (wall.Points_m.Length < 2 || wall.Points_m.Any(p => p is not { Length: 2 }))
        {
            return; // already reported
        }

        var points = wall.Points_m.Select(p => new Vector2(p[0], p[1])).ToArray();
        int segments = wall.SegmentCount;
        var lengths = new float[segments];
        for (int s = 0; s < segments; s++)
        {
            Vector2 a = points[s];
            Vector2 b = points[(s + 1) % points.Length];
            lengths[s] = Vector2.Distance(a, b);
            if (lengths[s] < 0.05f)
            {
                v.Error(nameof(WallDef.Points_m), $"segment {s} is shorter than 5 cm");
            }
        }

        int joints = wall.Closed ? segments : segments - 1;
        for (int j = 0; j < joints; j++)
        {
            int s0 = j;
            int s1 = (j + 1) % segments;
            Vector2 u0 = VectorMath.NormalizeOr(points[(s0 + 1) % points.Length] - points[s0], Vector2.Zero);
            Vector2 u1 = VectorMath.NormalizeOr(points[(s1 + 1) % points.Length] - points[s1], Vector2.Zero);
            if (Vector2.Dot(u0, u1) < -1e-3f)
            {
                v.Error(nameof(WallDef.Points_m), $"the corner after segment {s0} turns by more than 90°");
            }
        }

        if (wall.Openings is null)
        {
            return;
        }

        float margin = wall.Thickness_m * 0.5f;
        for (int i = 0; i < wall.Openings.Length; i++)
        {
            OpeningDef o = wall.Openings[i];
            Validator item = v.Item(nameof(WallDef.Openings), i);
            if (o.Segment < 0 || o.Segment >= segments)
            {
                continue; // already reported
            }

            float o0 = o.At_m - o.Width_m * 0.5f;
            float o1 = o.At_m + o.Width_m * 0.5f;
            bool touchesStart = o0 < -Epsilon || (o0 < margin - Epsilon && (wall.Closed || o.Segment > 0));
            bool touchesEnd = o1 > lengths[o.Segment] + Epsilon ||
                              (o1 > lengths[o.Segment] - margin + Epsilon && (wall.Closed || o.Segment < segments - 1));
            if (touchesStart || touchesEnd)
            {
                item.Error(nameof(OpeningDef.At_m),
                    $"opening runs past segment {o.Segment} (length {lengths[o.Segment]:0.##} m; keep {margin:0.##} m from corners)");
            }

            if (o.Sill_m >= height)
            {
                item.Error(nameof(OpeningDef.Sill_m), $"sill is above the top of the wall ({height:0.##} m)");
            }

            if (wall.Style == WallStyle.Railing && (o.Kind != OpeningKind.Gap || o.Sill_m != 0f || !float.IsNaN(o.Height_m)))
            {
                item.Error(nameof(OpeningDef.Kind), "railings only take gap openings, without sill_m or height_m");
            }

            for (int k = 0; k < i; k++)
            {
                OpeningDef other = wall.Openings[k];
                if (other.Segment == o.Segment &&
                    MathF.Abs(other.At_m - o.At_m) < (other.Width_m + o.Width_m) * 0.5f - Epsilon)
                {
                    item.Error(nameof(OpeningDef.At_m), $"overlaps opening {k}");
                }
            }
        }
    }

    public static IReadOnlyList<OpeningSpec> Openings(WallDef wall) =>
        wall.Openings is null
            ? Array.Empty<OpeningSpec>()
            : wall.Openings.Select(o => new OpeningSpec(o.Segment, o.At_m, o.Width_m, o.Sill_m, o.Height_m, o.Kind)).ToArray();

    public static Vector2[] Points(WallDef wall) => wall.Points_m.Select(p => new Vector2(p[0], p[1])).ToArray();

    public static Vector4 Rect(float[] r) => new(r[0], r[1], r[2], r[3]);

    public static IReadOnlyList<Vector4> Rects(float[][]? rects) =>
        rects is null ? Array.Empty<Vector4>() : rects.Select(Rect).ToArray();

    private static float BoxHalfHeight(Quaternion rotation, Vector3 half) =>
        MathF.Abs(Vector3.Transform(Vector3.UnitX, rotation).Y) * half.X +
        MathF.Abs(Vector3.Transform(Vector3.UnitY, rotation).Y) * half.Y +
        MathF.Abs(Vector3.Transform(Vector3.UnitZ, rotation).Y) * half.Z;

    private static bool SameIntervals(List<Vector2> a, List<Vector2> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (MathF.Abs(a[i].X - b[i].X) > Epsilon || MathF.Abs(a[i].Y - b[i].Y) > Epsilon)
            {
                return false;
            }
        }

        return true;
    }
}
