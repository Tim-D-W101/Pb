using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Data;
using Pb.Sim.Level;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.World;

/// <summary>
/// Details on the buildings, built from their templates (kit/buildings "gutters" and "trusses"):
/// half-round gutters along the eaves with downpipes from their ends down the walls (clear of the
/// doors and windows, with a swan neck, collars and a shoe), rainwater heads and downpipes on a
/// parapet roof, and steel trusses under a big roof with purlins between them: over a hole in the
/// roof they're broken off and hang down. Presentation only: paint and walking don't see them.
/// Each building is varied by a seed from where it stands.
/// </summary>
public static class BuildingDetails
{
    private const float GutterRadius = 0.065f, GutterWall = 0.006f;

    private const float PipeRadius = 0.04f;

    /// <summary>How far a downpipe stands off its wall (pipe centre to wall face, m), and its collars' spacing.</summary>
    private const float PipeStandOff = 0.07f, CollarSpacing = 1.8f;

    /// <summary>A downpipe keeps this far from the side of a door or window (m).</summary>
    private const float OpeningClearance = 0.3f;

    /// <summary>What the cables slung under the trusses are sheathed in.</summary>
    private const string CableMaterial = "plastic_black";

    /// <summary>Truss members: chords and webs (square sections, m), and the purlins on top (width, depth) and their spacing.</summary>
    private const float Chord = 0.12f, Web = 0.06f, PurlinWidth = 0.07f, PurlinDepth = 0.12f, PurlinSpacing = 1.6f;

    /// <summary>A roof sheet: its width, thickness, and the pitch and depth of its corrugations (m).</summary>
    private const float SheetWidth = 0.9f, SheetThickness = 0.004f, CorrugationPitch = 0.076f, CorrugationDepth = 0.009f;

    /// <summary>A corrugated sheet's cross-section (x across, y up), counter-clockwise, centred on its middle.</summary>
    private static readonly Vector2[] Corrugation = CorrugatedOutline();

    private static Vector2[] CorrugatedOutline()
    {
        int waves = (int)MathF.Round(SheetWidth / CorrugationPitch), steps = waves * 6;
        var outline = new Vector2[(steps + 1) * 2];
        for (int i = 0; i <= steps; i++)
        {
            float x = -SheetWidth * 0.5f + SheetWidth * i / steps;
            float y = CorrugationDepth * MathF.Sin(Mathf.Tau * i / 6f);
            // The underside left to right, then the top right to left.
            outline[i] = new Vector2(x, y - SheetThickness);
            outline[(steps + 1) * 2 - 1 - i] = new Vector2(x, y);
        }

        return outline;
    }

    /// <summary>
    /// Adds every building's details to <paramref name="meshFor"/> (the mesh for a world position);
    /// <paramref name="material"/> resolves a kit material id. Where rain will run off them down the
    /// walls goes in <paramref name="drips"/>. Returns how many buildings got any.
    /// </summary>
    public static int Build(LevelLayout level, Func<string, int> material, Func<Vector3, ShapeMesh> meshFor, List<Drip>? drips = null)
    {
        int dressed = 0;
        var ground = new Floors(level);
        foreach (PlacedBuilding building in level.Buildings)
        {
            BuildingDef def = building.Template.Def;
            if (def.Roof is null || def.Gutters is null && def.Trusses is null && def.Fittings is null && def.CeilingLights is null)
            {
                continue;
            }

            var on = new Building(def, building.Frame, meshFor, new Random(LevelBuilder.StableHash(level.Id) ^ building.Owner * 7919), drips);
            if (def.Gutters is not null && material(def.Gutters) is var gutters and >= 0)
            {
                on.Gutters(gutters);
            }

            if (def.Trusses is not null && material(def.Trusses.Material) is var trusses and >= 0)
            {
                // Lamp shades in the fittings' paint where there is one.
                int shades = def.Fittings is not null && material(def.Fittings) is var f and >= 0 ? f : trusses;
                on.Trusses(def.Trusses, trusses, shades, material(LevelBuilder.GlassMaterial), material(CableMaterial));
            }

            if (def.CeilingLights is not null && material(def.CeilingLights) is var lights and >= 0)
            {
                on.CeilingLights(lights, material(LevelBuilder.GlassMaterial));
            }

            if (def.Fittings is not null && material(def.Fittings) is var fittings and >= 0)
            {
                int pipes = def.Gutters is not null && material(def.Gutters) is var g and >= 0 ? g : fittings;
                on.Fittings(fittings, pipes, material(LevelBuilder.GlassMaterial));
            }

            if (def.Roof.Holes_m is { Length: > 0 } && material(def.Roof.Material) is var sheets and >= 0)
            {
                int steel = def.Trusses is not null && material(def.Trusses.Material) is var t and >= 0 ? t : sheets;
                on.FallenRoof(sheets, steel, ground, new Random(LevelBuilder.StableHash(level.Id) ^ building.Owner * 6007 ^ 0xFA11));
            }

            dressed++;
        }

        return dressed;
    }

    /// <summary>
    /// The floors and what stands on them, for things lying on a floor: the top of the highest floor under
    /// a point below a height, and whether anything standing there (a wall, column, prop or stair) is in
    /// the way.
    /// </summary>
    private sealed class Floors
    {
        private readonly List<Pb.Sim.Collision.Aabb> _floors = new();
        private readonly List<Pb.Sim.Collision.Aabb> _standing = new();

        public Floors(LevelLayout level)
        {
            foreach (LevelPrimitive p in level.Primitives)
            {
                if (p.Role == PrimitiveRole.Floor)
                {
                    _floors.Add(p.Bounds);
                }
                else if (p.Role is PrimitiveRole.Wall or PrimitiveRole.Column or PrimitiveRole.Prop or PrimitiveRole.Stair or PrimitiveRole.Ramp)
                {
                    _standing.Add(p.Bounds);
                }
            }
        }

        /// <summary>The top of the highest floor at (x, z) that lies below <paramref name="under"/>, or null where there's none.</summary>
        public float? Below(float x, float z, float under)
        {
            float? top = null;
            foreach (Pb.Sim.Collision.Aabb b in _floors)
            {
                if (x >= b.Min.X && x <= b.Max.X && z >= b.Min.Z && z <= b.Max.Z && b.Max.Y < under && (top is null || b.Max.Y > top))
                {
                    top = b.Max.Y;
                }
            }

            return top;
        }

        /// <summary>Whether something standing on the floor at <paramref name="y"/> covers (x, z), give or take <paramref name="margin"/>.</summary>
        public bool Blocked(float x, float z, float y, float margin)
        {
            foreach (Pb.Sim.Collision.Aabb b in _standing)
            {
                if (x >= b.Min.X - margin && x <= b.Max.X + margin && z >= b.Min.Z - margin && z <= b.Max.Z + margin && b.Min.Y < y + 0.3f && b.Max.Y > y)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>One building's details, built in its plan frame: x and z as in its template, y up from its origin.</summary>
    private sealed class Building
    {
        private readonly BuildingDef _def;
        private readonly PlanFrame _frame;
        private readonly Func<Vector3, ShapeMesh> _meshFor;
        private readonly Random _random;
        private readonly Basis _turn;
        private readonly float _roofBottom;
        private readonly float _roofTop;
        private readonly float _wall;
        private readonly Rect2 _roof;
        private readonly Rect2 _footprint;
        private readonly List<Rect2> _holes = new();
        private readonly List<Drip>? _drips;

        public Building(BuildingDef def, PlanFrame frame, Func<Vector3, ShapeMesh> meshFor, Random random, List<Drip>? drips)
        {
            _def = def;
            _drips = drips;
            _frame = frame;
            _meshFor = meshFor;
            _random = random;
            _turn = new Basis(Vector3.Up, frame.Yaw);
            RoofDef roof = def.Roof!;
            float thickness = float.IsNaN(roof.Thickness_m) ? def.SlabThickness_m : roof.Thickness_m;
            _roofBottom = def.TotalHeight;
            _roofTop = _roofBottom + thickness;
            _roof = Rect(roof.Rect_m);
            _footprint = Rect(def.Footprint_m);
            _wall = 0.3f;
            foreach (WallDef w in def.Walls)
            {
                if (w.Storey == 0 && w.Closed)
                {
                    _wall = w.Thickness_m;
                    break;
                }
            }

            foreach (float[] h in roof.Holes_m ?? Array.Empty<float[]>())
            {
                _holes.Add(Rect(h));
            }
        }

        /// <summary>
        /// Gutters along the long eaves with a downpipe from each end; on a parapet roof, rainwater heads
        /// on the parapet and downpipes, at the ends of the long sides.
        /// </summary>
        public void Gutters(int material)
        {
            bool alongX = _roof.Size.X >= _roof.Size.Y;
            bool parapet = _def.Roof!.Parapet_m > 0f;
            float length = alongX ? _roof.Size.X : _roof.Size.Y;
            ShapeMesh mesh = Mesh(_roof.GetCenter(), _roofTop);
            foreach (float side in new[] { -1f, 1f })
            {
                // The eave's line in plan: along the run (u) at this side's edge (v), facing out.
                float edge = alongX ? (side < 0f ? _roof.Position.Y : _roof.End.Y) : (side < 0f ? _roof.Position.X : _roof.End.X);
                float wallFace = alongX
                    ? (side < 0f ? _footprint.Position.Y : _footprint.End.Y) + side * _wall * 0.5f
                    : (side < 0f ? _footprint.Position.X : _footprint.End.X) + side * _wall * 0.5f;
                float start = alongX ? _roof.Position.X : _roof.Position.Y;
                Vector3 run = alongX ? Vector3.Right : Vector3.Back;
                Vector3 outward = (alongX ? Vector3.Back : Vector3.Right) * side;
                Vector3 Plan(float u, float y, float v) => alongX ? new Vector3(u, y, v) : new Vector3(v, y, u);

                if (!parapet)
                {
                    // Half-round, its top just under the roof's edge, half under the overhang.
                    Vector3 middle = Plan(start + length * 0.5f, _roofBottom - 0.01f, edge);
                    Extrude(mesh, material, middle, run, GutterProfile(), length - 0.02f);
                    // Where it overflows, down the wall under it.
                    Drip(Plan(start + length * 0.5f, _roofBottom - 0.02f, wallFace), outward, 0f, _def.Gutters!, length - 0.6f);
                }

                foreach (float end in new[] { 0.45f, length - 0.45f })
                {
                    float u = Clear(start + end, end < length * 0.5f ? 1f : -1f, wallFace, alongX);
                    if (float.IsNaN(u))
                    {
                        continue;
                    }

                    // Clear of the wall, and of a parapet roof's edge where it stands proud of the wall.
                    float pipeV = parapet ? edge + side * (PipeRadius + 0.03f) : wallFace + side * PipeStandOff;
                    float standOff = MathF.Abs(pipeV - wallFace);
                    if (parapet)
                    {
                        // A rainwater head on the parapet's face, the pipe straight down from it.
                        Box(mesh, material, Plan(u, _roofTop + 0.05f, edge + side * 0.09f), Plan(0.26f, 0.22f, 0.18f), Basis.Identity);
                        Drip(Plan(u, _roofTop - 0.06f, edge), outward, 0.26f, _def.Gutters!);
                        Pipe(mesh, material, Plan(u, _roofTop - 0.06f, pipeV), Plan(u, 0f, pipeV), outward, standOff);
                    }
                    else
                    {
                        // Out of the gutter's bottom, a swan neck back to the wall, then down.
                        Vector3 outlet = Plan(u, _roofBottom - 0.01f - GutterRadius, edge);
                        Vector3 bend = outlet + Vector3.Down * 0.08f;
                        Vector3 neck = Plan(u, outlet.Y - 0.08f - MathF.Abs(edge - pipeV) * 1.1f - 0.05f, pipeV);
                        Rod(mesh, material, outlet + Vector3.Up * 0.02f, bend, PipeRadius * 0.9f);
                        Rod(mesh, material, bend, neck + Vector3.Up * 0.05f, PipeRadius);
                        Pipe(mesh, material, neck + Vector3.Up * 0.05f, Plan(u, 0f, pipeV), outward, standOff);
                    }
                }
            }
        }

        /// <summary>
        /// Trusses across the short span, under the roof; purlins on top of them along the long way.
        /// Over a hole each is broken off: what's left near the edge hangs down into it.
        /// </summary>
        public void Trusses(TrussesDef def, int material, int shades, int glass, int cable)
        {
            bool alongX = _footprint.Size.X >= _footprint.Size.Y;
            float length = alongX ? _footprint.Size.X : _footprint.Size.Y;
            float lo = (alongX ? _footprint.Position.Y : _footprint.Position.X) + _wall * 0.5f;
            float hi = (alongX ? _footprint.End.Y : _footprint.End.X) - _wall * 0.5f;
            float start = alongX ? _footprint.Position.X : _footprint.Position.Y;
            Vector3 Plan(float u, float y, float s) => alongX ? new Vector3(u, y, s) : new Vector3(s, y, u);

            float top = _roofBottom - PurlinDepth - Chord * 0.5f;
            float bottom = top - def.Depth_m;
            int panels = Math.Max(2, (int)MathF.Round((hi - lo) / def.Depth_m));
            float panel = (hi - lo) / panels;
            int trusses = Math.Max(1, (int)MathF.Floor(length / def.Spacing_m - 0.5f));
            float spacing = length / (trusses + 1);
            var spans = new List<(float U, List<(float From, float To, float Droop, float Hang)> Broken)>();
            for (int t = 1; t <= trusses; t++)
            {
                float u = start + spacing * t;
                ShapeMesh mesh = Mesh(Plan(u, 0f, (lo + hi) * 0.5f), _roofBottom);
                // Where a hole crosses this truss, the span it loses (along s).
                var broken = new List<(float From, float To, float Droop, float Hang)>();
                spans.Add((u, broken));
                foreach (Rect2 hole in _holes)
                {
                    (float h0, float h1, float s0, float s1) = alongX
                        ? (hole.Position.X, hole.End.X, hole.Position.Y, hole.End.Y)
                        : (hole.Position.Y, hole.End.Y, hole.Position.X, hole.End.X);
                    if (u > h0 && u < h1 && s1 > lo && s0 < hi)
                    {
                        broken.Add((MathF.Max(s0, lo), MathF.Min(s1, hi), Mathf.DegToRad(R(18f, 42f)), R(1.2f, 2.6f)));
                    }
                }

                void Member(float s0, float y0, float s1, float y1, float size)
                {
                    float mid = (s0 + s1) * 0.5f;
                    foreach ((float from, float to, float droop, float hang) in broken)
                    {
                        if (mid <= from || mid >= to)
                        {
                            continue;
                        }

                        // Inside the hole: near either edge it hangs from the break; further in, it's gone.
                        bool near = mid - from <= hang, far = to - mid <= hang;
                        if (!near && !far)
                        {
                            return;
                        }

                        float pivot = near ? from : to;
                        float turn = near ? -droop : droop;
                        (s0, y0) = Turn(s0, y0, pivot, top, turn);
                        (s1, y1) = Turn(s1, y1, pivot, top, turn);
                        break;
                    }

                    Bar(mesh, material, Plan(u, y0, s0), Plan(u, y1, s1), size);
                }

                for (int i = 0; i < panels; i++)
                {
                    float s0 = lo + panel * i, s1 = s0 + panel;
                    Member(s0, top, s1, top, Chord);
                    Member(s0, bottom, s1, bottom, Chord);
                    Member(s0, bottom, s0, top, Web);
                    // A Warren truss: diagonals alternate.
                    if (i % 2 == 0)
                    {
                        Member(s0, bottom, s1, top, Web);
                    }
                    else
                    {
                        Member(s0, top, s1, bottom, Web);
                    }
                }

                Member(hi, bottom, hi, top, Web);

                // Lamps hanging from the bottom chord on cables, where the truss is still whole.
                for (int k = 1; k <= def.LampsPerTruss; k++)
                {
                    float at = lo + (hi - lo) * k / (def.LampsPerTruss + 1) + R(-0.4f, 0.4f);
                    bool whole = true;
                    foreach ((float from, float to, _, _) in broken)
                    {
                        whole &= at < from - 0.6f || at > to + 0.6f;
                    }

                    if (whole)
                    {
                        PendantLamp(mesh, shades, glass, Plan(u, bottom - Chord * 0.5f, at), R(1.0f, 1.9f));
                    }
                }
            }

            if (cable >= 0)
            {
                Cables(spans, cable, lo, hi, bottom - Chord * 0.5f - 0.02f, Plan);
            }

            // Purlins along the building on top of the trusses, wall to wall, but not across a hole.
            ShapeMesh purlins = Mesh(_footprint.GetCenter(), _roofBottom);
            float y = _roofBottom - PurlinDepth * 0.5f;
            int rows = Math.Max(1, (int)MathF.Round((hi - lo) / PurlinSpacing));
            for (int r = 0; r <= rows; r++)
            {
                float s = lo + 0.1f + (hi - lo - 0.2f) * r / rows;
                var cuts = new List<(float From, float To)>();
                foreach (Rect2 hole in _holes)
                {
                    (float h0, float h1, float s0, float s1) = alongX
                        ? (hole.Position.X, hole.End.X, hole.Position.Y, hole.End.Y)
                        : (hole.Position.Y, hole.End.Y, hole.Position.X, hole.End.X);
                    if (s > s0 && s < s1)
                    {
                        cuts.Add((h0, h1));
                    }
                }

                cuts.Sort((a, b) => a.From.CompareTo(b.From));
                float from = start + _wall * 0.5f, end = start + length - _wall * 0.5f;
                foreach ((float h0, float h1) in cuts)
                {
                    if (h0 > from)
                    {
                        PurlinPiece(purlins, material, Plan(from, y, s), Plan(MathF.Min(h0, end), y, s), alongX);
                    }

                    from = MathF.Max(from, h1);
                }

                if (end > from)
                {
                    PurlinPiece(purlins, material, Plan(from, y, s), Plan(end, y, s), alongX);
                }
            }
        }

        /// <summary>
        /// Cables slung from truss to truss under their bottom chords, two or three runs the length of the
        /// building, sagging between them; where a truss is broken off over a hole, the cable's end hangs
        /// down from the last whole one. Their own random, so the rest of the building stays as it was.
        /// </summary>
        private void Cables(List<(float U, List<(float From, float To, float Droop, float Hang)> Broken)> spans, int material, float lo, float hi, float y,
            Func<float, float, float, Vector3> plan)
        {
            if (spans.Count < 2)
            {
                return;
            }

            var random = new Random((int)(_footprint.Position.X * 73f + _footprint.Position.Y * 19f) ^ 0xCAB1E);
            float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
            static bool Whole(List<(float From, float To, float Droop, float Hang)> broken, float s)
            {
                foreach ((float from, float to, _, _) in broken)
                {
                    if (s > from - 0.3f && s < to + 0.3f)
                    {
                        return false;
                    }
                }

                return true;
            }

            ShapeMesh mesh = Mesh(_footprint.GetCenter(), _roofBottom);
            int runs = 2 + random.Next(2);
            for (int c = 0; c < runs; c++)
            {
                float s = R(lo + 0.6f, hi - 0.6f);
                for (int k = 0; k + 1 < spans.Count; k++)
                {
                    Vector3 a = plan(spans[k].U, y, s), b = plan(spans[k + 1].U, y, s);
                    bool fromA = Whole(spans[k].Broken, s), fromB = Whole(spans[k + 1].Broken, s);
                    if (fromA && fromB)
                    {
                        Hang(mesh, material, a, b, R(0.25f, 0.75f));
                    }
                    else if (fromA || fromB)
                    {
                        // Torn: a loose end swinging down from the whole truss towards the hole.
                        Vector3 hook = fromA ? a : b, toward = (fromA ? b : a) - hook;
                        Vector3 end = hook + toward * R(0.15f, 0.35f) + Vector3.Down * R(1.2f, 3f);
                        Hang(mesh, material, hook, end, R(0.1f, 0.3f));
                    }
                }
            }
        }

        /// <summary>A cable from <paramref name="a"/> to <paramref name="b"/> (plan points), sagging by <paramref name="sag"/> in the middle.</summary>
        private void Hang(ShapeMesh mesh, int material, Vector3 a, Vector3 b, float sag)
        {
            const int pieces = 8;
            Vector3 previous = a;
            for (int i = 1; i <= pieces; i++)
            {
                float f = (float)i / pieces;
                Vector3 next = a.Lerp(b, f) + Vector3.Down * (sag * 4f * f * (1f - f));
                mesh.Rod(material, World(previous), World(next), 0.011f, 5, caps: false);
                previous = next;
            }
        }

        /// <summary>
        /// Fittings on the outside walls: a lamp on a bracket over each door and loading bay, and spread
        /// along the walls clear of the openings, junction boxes with a conduit running up the wall,
        /// louvred vents, and pipes along the wall turning down into the ground.
        /// </summary>
        public void Fittings(int material, int pipes, int glass)
        {
            WallDef? outline = null;
            foreach (WallDef w in _def.Walls)
            {
                if (w.Storey == 0 && w.Closed && w.Style != WallStyle.Railing)
                {
                    outline = w;
                    break;
                }
            }

            if (outline is null)
            {
                return;
            }

            // The tops of the doors and bays in every ground-storey run on this outline, by segment and place.
            var tops = new Dictionary<(int Segment, int At), (float Top, float Width)>();
            foreach (WallDef w in _def.Walls)
            {
                if (w.Storey != 0 || !w.Closed || w.Openings is null || w.Points_m.Length != outline.Points_m.Length)
                {
                    continue;
                }

                float runBase = float.IsNaN(w.BaseElevation_m) ? 0f : w.BaseElevation_m;
                float runHeight = float.IsNaN(w.Height_m) ? _roofBottom - runBase : w.Height_m;
                foreach (OpeningDef o in w.Openings)
                {
                    if (o.Kind == OpeningKind.Window)
                    {
                        continue;
                    }

                    float top = runBase + o.Sill_m + (float.IsNaN(o.Height_m) || o.Kind == OpeningKind.Gap ? runHeight - o.Sill_m : o.Height_m);
                    var key = (o.Segment, (int)MathF.Round(o.At_m * 10f));
                    if (!tops.TryGetValue(key, out (float Top, float Width) known) || top > known.Top)
                    {
                        tops[key] = (top, o.Width_m);
                    }
                }
            }

            Vector2 middle = _footprint.GetCenter();
            float face = outline.Thickness_m * 0.5f;
            for (int s = 0; s < outline.SegmentCount; s++)
            {
                float[] pa = outline.Points_m[s], pb = outline.Points_m[(s + 1) % outline.Points_m.Length];
                var a = new Vector2(pa[0], pa[1]);
                var b = new Vector2(pb[0], pb[1]);
                float length = a.DistanceTo(b);
                if (length < 1f)
                {
                    continue;
                }

                Vector2 dir = (b - a) / length;
                var across = new Vector2(-dir.Y, dir.X);
                float sign = across.Dot(a + dir * (length * 0.5f) - middle) >= 0f ? 1f : -1f;
                Vector2 outward = across * sign;
                Vector3 Plan(float t, float y, float off) =>
                    new(a.X + dir.X * t + outward.X * (face + off), y, a.Y + dir.Y * t + outward.Y * (face + off));
                // X along the wall, Y up, Z out of it.
                var turn = new Basis(new Vector3(dir.X, 0f, dir.Y) * sign, Vector3.Up, new Vector3(outward.X, 0f, outward.Y));

                foreach (((int segment, int at10), (float top, float width)) in tops)
                {
                    if (segment == s && top + 0.7f < _roofBottom)
                    {
                        Lamp(material, glass, Plan(at10 / 10f, top + 0.35f + 0.05f * width, 0f), turn);
                    }
                }

                int count = (int)(length / 7f);
                for (int i = 0; i < count; i++)
                {
                    float t = R(1f, length - 1f);
                    double kind = _random.NextDouble();
                    float reach = kind < 0.4 ? 0.3f : kind < 0.7 ? 0.35f : 2.5f;
                    if (Opening(Flat(Plan(t - reach, 0f, -face))) || Opening(Flat(Plan(t, 0f, -face))) || Opening(Flat(Plan(t + reach, 0f, -face))))
                    {
                        continue;
                    }

                    if (kind < 0.4)
                    {
                        JunctionBox(material, Plan(t, R(1.35f, 1.8f), 0f), turn, MathF.Min(_roofBottom - 0.3f, R(3f, 5f)));
                    }
                    else if (kind < 0.7)
                    {
                        Vent(material, Plan(t, MathF.Min(_roofBottom - 0.6f, R(2.1f, 3.2f)), 0f), turn);
                    }
                    else
                    {
                        float y = MathF.Min(_roofBottom - 0.45f, R(2.4f, 3.4f));
                        float end = t + (_random.NextDouble() < 0.5 ? -1f : 1f) * R(1.5f, 2.5f);
                        PipeRun(pipes, Plan(t, y, 0.06f), Plan(end, y, 0.06f), Plan(end, 0f, 0.06f));
                    }
                }
            }

            static Vector2 Flat(Vector3 plan) => new(plan.X, plan.Z);
        }

        /// <summary>A lamp over a doorway: a bracket out from the wall and a head angled down, its glass underneath (no light: it's long dead).</summary>
        private void Lamp(int material, int glass, Vector3 at, Basis turn)
        {
            ShapeMesh mesh = Mesh(at, _roofBottom);
            Vector3 outward = turn.Z, wall = at;
            Vector3 tip = wall + outward * 0.34f + Vector3.Down * 0.08f;
            Box(mesh, material, wall + outward * 0.015f, new Vector3(0.12f, 0.16f, 0.03f), turn);
            Drip(wall + Vector3.Down * 0.08f, outward, 0.12f, _def.Fittings!);
            mesh.Bar(material, World(wall + outward * 0.02f), World(tip), 0.03f, 0.03f);
            Basis head = turn * new Basis(Vector3.Right, -0.35f);
            Box(mesh, material, tip + Vector3.Down * 0.05f, new Vector3(0.26f, 0.1f, 0.2f), head);
            if (glass >= 0)
            {
                Box(mesh, glass, tip + Vector3.Down * 0.105f + outward * 0.02f, new Vector3(0.22f, 0.012f, 0.16f), head);
            }
        }

        /// <summary>A grey box on the wall with a conduit running up the wall from it.</summary>
        private void JunctionBox(int material, Vector3 at, Basis turn, float conduitTop)
        {
            ShapeMesh mesh = Mesh(at, _roofBottom);
            Vector3 outward = turn.Z, up = Vector3.Up;
            var size = new Vector3(R(0.26f, 0.38f), R(0.34f, 0.48f), R(0.1f, 0.15f));
            Box(mesh, material, at + outward * (size.Z * 0.5f), size, turn);
            Drip(at + Vector3.Down * (size.Y * 0.5f), outward, size.X, _def.Fittings!);
            // A lid seam and a hinge pin.
            Box(mesh, material, at + outward * (size.Z + 0.004f), new Vector3(size.X - 0.03f, size.Y - 0.03f, 0.008f), turn);
            Vector3 from = at + up * (size.Y * 0.5f) + outward * 0.03f;
            mesh.Rod(material, World(from), World(new Vector3(from.X, conduitTop, from.Z)), 0.013f, 6);
        }

        /// <summary>A louvred vent: a frame and slats tilted down and out.</summary>
        private void Vent(int material, Vector3 at, Basis turn)
        {
            ShapeMesh mesh = Mesh(at, _roofBottom);
            Vector3 outward = turn.Z, along = turn.X;
            float w = R(0.38f, 0.55f), h = w * R(0.8f, 1.1f);
            Drip(at + Vector3.Down * (h * 0.5f), outward, w, _def.Fittings!);
            foreach (float side in new[] { -1f, 1f })
            {
                Box(mesh, material, at + along * (side * (w * 0.5f - 0.02f)) + outward * 0.03f, new Vector3(0.04f, h, 0.06f), turn);
                Box(mesh, material, at + Vector3.Up * (side * (h * 0.5f - 0.02f)) + outward * 0.03f, new Vector3(w, 0.04f, 0.06f), turn);
            }

            Basis slat = turn * new Basis(Vector3.Right, 0.6f);
            int slats = (int)(h / 0.07f);
            for (int i = 1; i < slats; i++)
            {
                float y = -h * 0.5f + h * i / slats;
                Box(mesh, material, at + Vector3.Up * y + outward * 0.03f, new Vector3(w - 0.06f, 0.06f, 0.006f), slat);
            }
        }

        /// <summary>A pipe along the wall from <paramref name="start"/> to <paramref name="corner"/>, then down to <paramref name="foot"/>, clipped to the wall.</summary>
        private void PipeRun(int material, Vector3 start, Vector3 corner, Vector3 foot)
        {
            ShapeMesh mesh = Mesh(start, _roofBottom);
            const float radius = 0.035f;
            mesh.Rod(material, World(start), World(corner), radius, 8);
            mesh.Rod(material, World(corner), World(foot), radius, 8);
            mesh.Cylinder(material, World(corner), Basis.Identity, radius * 1.25f, radius * 2.6f, 8);
            for (float f = 0.1f; f < 1f; f += 0.3f)
            {
                Vector3 clip = start.Lerp(corner, f);
                mesh.Cylinder(material, World(clip), ShapeMesh.BasisAlong((World(corner) - World(start)).Normalized()), radius * 1.3f, 0.03f, 8);
            }

            for (float y = 0.5f; y < corner.Y - 0.3f; y += 1.2f)
            {
                mesh.Cylinder(material, World(new Vector3(corner.X, y, corner.Z)), Basis.Identity, radius * 1.3f, 0.03f, 8);
            }
        }

        /// <summary>A pendant lamp on a cable <paramref name="drop"/> below <paramref name="hook"/>: a conical shade with its bulb.</summary>
        private void PendantLamp(ShapeMesh mesh, int material, int glass, Vector3 hook, float drop)
        {
            Vector3 top = hook + Vector3.Down * drop;
            Rod(mesh, material, hook, top, 0.008f, 4);
            // Inside down to the rim, across the rim, then outside back up, so both faces show.
            var shade = new[]
            {
                new Vector2(0.025f, -0.02f), new Vector2(0.255f, -0.27f), new Vector2(0.255f, -0.27f),
                new Vector2(0.27f, -0.27f), new Vector2(0.27f, -0.27f), new Vector2(0.04f, 0f),
            };
            mesh.Lathe(material, World(top), _turn, shade, 12);
            if (glass >= 0)
            {
                mesh.Cylinder(glass, World(top + Vector3.Down * 0.1f), _turn, 0.045f, 0.1f, 8);
            }
        }

        /// <summary>
        /// Fluorescent fittings on each storey's ceiling, in a row down the middle of each strip between
        /// walls running the building's long way, about every 3 m, clear of the walls and only where the
        /// ceiling is whole above them. Most are fixed flat; some hang from one end where the other came
        /// away, and some are gone but for their mounting plates and a dangling wire.
        /// </summary>
        public void CeilingLights(int material, int glass)
        {
            bool alongX = _footprint.Size.X >= _footprint.Size.Y;
            Vector3 Plan(float u, float y, float v) => alongX ? new Vector3(u, y, v) : new Vector3(v, y, u);
            float u0 = alongX ? _footprint.Position.X : _footprint.Position.Y, u1 = alongX ? _footprint.End.X : _footprint.End.Y;
            float v0 = alongX ? _footprint.Position.Y : _footprint.Position.X, v1 = alongX ? _footprint.End.Y : _footprint.End.X;
            float elevation = 0f;
            for (int storey = 0; storey < _def.Storeys_m.Length; storey++)
            {
                elevation += _def.Storeys_m[storey];
                bool top = storey == _def.Storeys_m.Length - 1;
                // The ceiling: the floor above (its slabs, less their holes), or the roof.
                var cover = new List<(Rect2 Rect, List<Rect2> Holes, float Underside)>();
                if (top)
                {
                    cover.Add((_roof, _holes, _roofBottom));
                }
                else
                {
                    foreach (SlabDef slab in _def.Floors ?? Array.Empty<SlabDef>())
                    {
                        if (MathF.Abs(slab.Elevation_m - elevation) < 0.05f)
                        {
                            var holes = new List<Rect2>();
                            foreach (float[] h in slab.Holes_m ?? Array.Empty<float[]>())
                            {
                                holes.Add(Rect(h));
                            }

                            float thickness = float.IsNaN(slab.Thickness_m) ? _def.SlabThickness_m : slab.Thickness_m;
                            cover.Add((Rect(slab.Rect_m), holes, slab.Elevation_m - thickness));
                        }
                    }
                }

                // Strips between walls running the long way (and the outline), a row of fittings down each.
                var lines = new List<float> { v0, v1 };
                foreach (WallDef w in _def.Walls)
                {
                    if (w.Storey != storey || w.Style == WallStyle.Railing)
                    {
                        continue;
                    }

                    for (int s = 0; s < w.SegmentCount; s++)
                    {
                        float[] pa = w.Points_m[s], pb = w.Points_m[(s + 1) % w.Points_m.Length];
                        (float da, float db) = alongX ? (pa[1], pb[1]) : (pa[0], pb[0]);
                        if (MathF.Abs(da - db) < 0.01f)
                        {
                            lines.Add(da);
                        }
                    }
                }

                lines.Sort();
                for (int l = 0; l + 1 < lines.Count; l++)
                {
                    if (lines[l + 1] - lines[l] < 1.6f)
                    {
                        continue;
                    }

                    float v = (lines[l] + lines[l + 1]) * 0.5f;
                    int count = Math.Max(1, (int)MathF.Round((u1 - u0) / 3f));
                    for (int i = 0; i < count; i++)
                    {
                        float u = u0 + (u1 - u0) * (i + 0.5f) / count;
                        Vector3 a = Plan(u - 0.65f, 0f, v), b = Plan(u + 0.65f, 0f, v);
                        float underside = float.NaN;
                        foreach ((Rect2 rect, List<Rect2> holes, float under) in cover)
                        {
                            if (Covered(rect, holes, a) && Covered(rect, holes, b) && Covered(rect, holes, (a + b) * 0.5f))
                            {
                                underside = under;
                            }
                        }

                        if (float.IsNaN(underside) || !ClearOfWalls(storey, a, b, 0.3f))
                        {
                            continue;
                        }

                        FluorescentFitting(material, glass, Plan(u, underside, v), alongX ? Vector3.Right : Vector3.Back);
                    }
                }
            }

            static bool Covered(Rect2 rect, List<Rect2> holes, Vector3 p)
            {
                var flat = new Vector2(p.X, p.Z);
                if (!rect.HasPoint(flat))
                {
                    return false;
                }

                foreach (Rect2 h in holes)
                {
                    if (h.Grow(0.15f).HasPoint(flat))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>Whether plan segment a–b keeps <paramref name="margin"/> clear of every wall of the storey.</summary>
        private bool ClearOfWalls(int storey, Vector3 a, Vector3 b, float margin)
        {
            foreach (WallDef w in _def.Walls)
            {
                if (w.Storey != storey)
                {
                    continue;
                }

                for (int s = 0; s < w.SegmentCount; s++)
                {
                    float[] pa = w.Points_m[s], pb = w.Points_m[(s + 1) % w.Points_m.Length];
                    var wa = new Vector2(pa[0], pa[1]);
                    var wb = new Vector2(pb[0], pb[1]);
                    for (int k = 0; k <= 4; k++)
                    {
                        Vector3 p = a.Lerp(b, k / 4f);
                        var flat = new Vector2(p.X, p.Z);
                        if (Geometry2D.GetClosestPointToSegment(flat, wa, wb).DistanceTo(flat) < w.Thickness_m * 0.5f + margin)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>One fluorescent fitting under the ceiling at <paramref name="at"/> (its underside), along plan direction <paramref name="along"/>.</summary>
        private void FluorescentFitting(int material, int glass, Vector3 at, Vector3 along)
        {
            ShapeMesh mesh = Mesh(at, _roofBottom);
            const float length = 1.25f, half = length * 0.5f;
            // X along the fitting, Y up, Z across it.
            var frame = new Basis(along, Vector3.Up, along.Cross(Vector3.Up));
            double state = _random.NextDouble();
            if (state < 0.12)
            {
                // Gone: the mounting plates and a wire hanging down.
                foreach (float end in new[] { -0.45f, 0.45f })
                {
                    Box(mesh, material, at + along * end + Vector3.Down * 0.005f, new Vector3(0.12f, 0.01f, 0.08f), frame);
                }

                Rod(mesh, material, at, at + Vector3.Down * R(0.2f, 0.5f) + along * R(-0.1f, 0.1f), 0.006f, 4);
                return;
            }

            float drop = state < 0.32 ? R(0.3f, 0.95f) : 0f;
            float side = _random.NextDouble() < 0.5 ? -1f : 1f;
            // Hanging from one end (side), the other dropped: turned about the level axis across it.
            Vector3 pivot = at + along * (side * half) + Vector3.Down * 0.02f;
            float angle = MathF.Asin(Mathf.Clamp(drop / length, 0f, 1f));
            Vector3 axis = along.Cross(Vector3.Up).Normalized();
            Basis tilt = drop > 0f ? new Basis(axis, side * angle) : Basis.Identity;
            Vector3 middle = drop > 0f ? pivot + tilt * (along * (-side * half)) + Vector3.Down * 0.035f : at + Vector3.Down * 0.045f;
            Box(mesh, material, middle, new Vector3(length, 0.07f, 0.16f), tilt * frame);
            if (glass >= 0 && _random.NextDouble() < 0.7)
            {
                Box(mesh, glass, middle + tilt * (Vector3.Down * 0.043f), new Vector3(length - 0.05f, 0.015f, 0.13f), tilt * frame);
            }

            if (drop > 0f)
            {
                Rod(mesh, material, at + along * (side * half), pivot, 0.006f, 4);
            }
        }

        /// <summary>Turns plan point (s, y) about (pivot, pivotY) in the truss's plane.</summary>
        private static (float S, float Y) Turn(float s, float y, float pivot, float pivotY, float angle)
        {
            float ds = s - pivot, dy = y - pivotY;
            float c = MathF.Cos(angle), n = MathF.Sin(angle);
            return (pivot + ds * c - dy * n, pivotY + ds * n + dy * c);
        }

        private void PurlinPiece(ShapeMesh mesh, int material, Vector3 a, Vector3 b, bool alongX)
        {
            Vector3 center = (a + b) * 0.5f;
            float length = a.DistanceTo(b);
            Vector3 size = alongX ? new Vector3(length, PurlinDepth, PurlinWidth) : new Vector3(PurlinWidth, PurlinDepth, length);
            Box(mesh, material, center, size, Basis.Identity);
        }

        /// <summary>
        /// Where along the eave (u) a downpipe can run down the wall at <paramref name="u"/>, stepping
        /// towards <paramref name="inward"/> past doors and windows; NaN when there's no room within 3 m.
        /// </summary>
        private float Clear(float u, float inward, float wallFace, bool alongX)
        {
            for (float shift = 0f; shift <= 3f; shift += 0.25f)
            {
                float at = u + inward * shift;
                Vector2 plan = alongX ? new Vector2(at, wallFace) : new Vector2(wallFace, at);
                if (!Opening(plan))
                {
                    return at;
                }
            }

            return float.NaN;
        }

        /// <summary>Whether a door, window or gap in any wall runs within reach of plan point <paramref name="p"/>.</summary>
        private bool Opening(Vector2 p)
        {
            foreach (WallDef w in _def.Walls)
            {
                if (w.Openings is null)
                {
                    continue;
                }

                for (int s = 0; s < w.SegmentCount; s++)
                {
                    var a = new Vector2(w.Points_m[s][0], w.Points_m[s][1]);
                    var b = new Vector2(w.Points_m[(s + 1) % w.Points_m.Length][0], w.Points_m[(s + 1) % w.Points_m.Length][1]);
                    Vector2 d = b - a;
                    float len = d.Length();
                    if (len < 1e-3f)
                    {
                        continue;
                    }

                    float at = (p - a).Dot(d) / len;
                    float off = MathF.Abs((p - a).Cross(d) / len);
                    if (off > w.Thickness_m * 0.5f + 0.25f || at < -0.5f || at > len + 0.5f)
                    {
                        continue;
                    }

                    foreach (OpeningDef o in w.Openings)
                    {
                        if (o.Segment == s && MathF.Abs(at - o.At_m) < o.Width_m * 0.5f + OpeningClearance)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// A downpipe from <paramref name="top"/> down to <paramref name="foot"/>, <paramref name="standOff"/>
        /// out from the wall, with collars bracketed to the wall and a shoe.
        /// </summary>
        private void Pipe(ShapeMesh mesh, int material, Vector3 top, Vector3 foot, Vector3 outward, float standOff)
        {
            Vector3 shoe = foot + Vector3.Up * 0.16f;
            Rod(mesh, material, top, shoe, PipeRadius);
            Rod(mesh, material, shoe, shoe + outward * 0.11f + Vector3.Down * 0.1f, PipeRadius);
            for (float y = shoe.Y + 0.5f; y < top.Y - 0.2f; y += CollarSpacing)
            {
                Vector3 at = new(top.X, y, top.Z);
                Rod(mesh, material, at + Vector3.Down * 0.025f, at + Vector3.Up * 0.025f, PipeRadius + 0.008f, 8);
                // The bracket back to the wall, rust running from where it's fixed.
                float reach = MathF.Max(standOff - PipeRadius, 0.01f);
                Box(mesh, material, at - outward * (PipeRadius + reach * 0.5f), new Vector3(0.012f, 0.03f, 0.012f) + Abs(outward) * reach, Basis.Identity);
                Drip(at - outward * (PipeRadius + reach) + Vector3.Down * 0.015f, outward, 0.09f, _def.Gutters ?? _def.Fittings ?? "");
            }
        }

        private static Vector3 Abs(Vector3 v) => new(MathF.Abs(v.X), MathF.Abs(v.Y), MathF.Abs(v.Z));

        /// <summary>A half-round gutter's section: a thin half ring, open at the top, its lips level with its centre.</summary>
        private static List<Vector2> GutterProfile()
        {
            const int steps = 9;
            var outline = new List<Vector2>(steps * 2 + 2);
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Pi + Mathf.Pi * i / steps;
                outline.Add(new Vector2(MathF.Cos(a), MathF.Sin(a)) * GutterRadius);
            }

            for (int i = steps; i >= 0; i--)
            {
                float a = Mathf.Pi + Mathf.Pi * i / steps;
                outline.Add(new Vector2(MathF.Cos(a), MathF.Sin(a)) * (GutterRadius - GutterWall));
            }

            return outline;
        }

        /// <summary>
        /// Roof sheets fallen in through each hole, lying on whatever floor is below it (the ground floor or
        /// the mezzanine): corrugated, some flat, some with an end propped up on the rubble, some bent up
        /// across the middle, with a broken purlin or two among them. Each lies wholly on one floor, clear
        /// of everything standing on it and of the other sheets.
        /// </summary>
        public void FallenRoof(int sheet, int steel, Floors floors, Random random)
        {
            float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
            float under = World(new Vector3(0f, _roofBottom, 0f)).Y - 0.5f;
            var placed = new List<Vector3>();
            foreach (Rect2 hole in _holes)
            {
                int count = Math.Clamp((int)(hole.Area / 9f), 3, 14);
                for (int k = 0, tries = 0; k < count && tries < count * 12; tries++)
                {
                    float length = R(1.8f, 3.0f);
                    var plan = new Vector2(R(hole.Position.X - 0.8f, hole.End.X + 0.8f), R(hole.Position.Y - 0.8f, hole.End.Y + 0.8f));
                    Vector3 at = World(new Vector3(plan.X, 0f, plan.Y));
                    float yaw = R(0f, Mathf.Tau);
                    var turn = new Basis(Vector3.Up, yaw);
                    Vector3 along = turn * Vector3.Back, across = turn * Vector3.Right;
                    if (floors.Below(at.X, at.Z, under) is not float floor || placed.Exists(o => o.DistanceTo(at) < 1.1f))
                    {
                        continue;
                    }

                    bool fits = true;
                    foreach ((float u, float v) in new[] { (-0.5f, -0.5f), (0.5f, -0.5f), (0.5f, 0.5f), (-0.5f, 0.5f), (0f, 0f) })
                    {
                        Vector3 c = at + along * (u * length) + across * (v * SheetWidth);
                        if (floors.Below(c.X, c.Z, under) is not float f || MathF.Abs(f - floor) > 0.03f || floors.Blocked(c.X, c.Z, floor, 0.08f))
                        {
                            fits = false;
                            break;
                        }
                    }

                    if (!fits)
                    {
                        continue;
                    }

                    ShapeMesh mesh = _meshFor(at);
                    mesh.Place(Transform3D.Identity, 0.3f);
                    float rest = floor + CorrugationDepth + SheetThickness + 0.004f;
                    double kind = random.NextDouble();
                    if (kind < 0.45)
                    {
                        // Flat, a little out of true.
                        Basis lie = turn * new Basis(Vector3.Right, R(-0.03f, 0.03f)) * new Basis(Vector3.Back, R(-0.02f, 0.02f));
                        mesh.Extrude(sheet, new Vector3(at.X, rest + 0.01f, at.Z), lie, Corrugation, length);
                    }
                    else if (kind < 0.75)
                    {
                        // One end propped up on the rubble.
                        float tilt = R(0.1f, 0.28f);
                        Basis lie = turn * new Basis(Vector3.Right, -tilt);
                        mesh.Extrude(sheet, new Vector3(at.X, rest + length * 0.5f * MathF.Sin(tilt), at.Z), lie, Corrugation, length);
                    }
                    else
                    {
                        // Bent up across the middle where it caught on the way down: two halves meeting at a ridge.
                        float bend = R(0.18f, 0.5f), half = length * 0.5f;
                        foreach (float side in new[] { -1f, 1f })
                        {
                            Basis lie = turn * new Basis(Vector3.Right, side * bend);
                            Vector3 centre = at + along * (side * half * 0.5f * MathF.Cos(bend)) + Vector3.Up * (rest - at.Y + half * 0.5f * MathF.Sin(bend));
                            mesh.Extrude(sheet, centre, lie, Corrugation, half);
                        }
                    }

                    placed.Add(at);
                    k++;
                }

                // A purlin or two, broken off, lying across the floor.
                for (int k = 0, bars = random.Next(1, 3), tries = 0; k < bars && tries < 20; tries++)
                {
                    float length = R(1.4f, 3.2f);
                    Vector3 at = World(new Vector3(R(hole.Position.X, hole.End.X), 0f, R(hole.Position.Y, hole.End.Y)));
                    Vector3 dir = new Basis(Vector3.Up, R(0f, Mathf.Tau)) * Vector3.Back;
                    Vector3 a = at - dir * (length * 0.5f), b = at + dir * (length * 0.5f);
                    if (floors.Below(at.X, at.Z, under) is not float floor ||
                        floors.Below(a.X, a.Z, under) is not float fa || floors.Below(b.X, b.Z, under) is not float fb ||
                        MathF.Abs(fa - floor) > 0.03f || MathF.Abs(fb - floor) > 0.03f ||
                        floors.Blocked(a.X, a.Z, floor, 0.05f) || floors.Blocked(b.X, b.Z, floor, 0.05f) || floors.Blocked(at.X, at.Z, floor, 0.05f))
                    {
                        continue;
                    }

                    ShapeMesh mesh = _meshFor(at);
                    mesh.Place(Transform3D.Identity, 0.3f);
                    // Resting on the flange, one end a little up on a sheet or a lump.
                    Vector3 lift = Vector3.Up * (floor + PurlinWidth * 0.5f);
                    mesh.Bar(steel, new Vector3(a.X, 0f, a.Z) + lift, new Vector3(b.X, 0f, b.Z) + lift + Vector3.Up * R(0f, 0.12f), PurlinDepth, PurlinWidth);
                    k++;
                }
            }
        }

        private ShapeMesh Mesh(Vector2 plan, float height) => Mesh(new Vector3(plan.X, 0f, plan.Y), height);

        private ShapeMesh Mesh(Vector3 plan, float height)
        {
            ShapeMesh mesh = _meshFor(World(plan));
            mesh.Place(Transform3D.Identity, height);
            return mesh;
        }

        private Vector3 World(Vector3 plan) => _frame.ToWorld(new SVector3(plan.X, plan.Y, plan.Z)).ToGodot();

        /// <summary>Reports where rain runs off down the face at plan point <paramref name="plan"/>, facing plan direction <paramref name="outward"/>.</summary>
        private void Drip(Vector3 plan, Vector3 outward, float width, string material, float run = 0f) =>
            _drips?.Add(new Drip(World(plan), (_turn * outward).Normalized(), width, material, run));

        private void Box(ShapeMesh mesh, int material, Vector3 plan, Vector3 size, Basis rotation) =>
            mesh.Box(material, World(plan), size, _turn * rotation);

        private void Rod(ShapeMesh mesh, int material, Vector3 a, Vector3 b, float radius, int segments = 8) =>
            mesh.Rod(material, World(a), World(b), radius, segments);

        private void Bar(ShapeMesh mesh, int material, Vector3 a, Vector3 b, float size) =>
            mesh.Bar(material, World(a), World(b), size, size);

        /// <summary>Extrudes <paramref name="outline"/> (x across, y up) along plan direction <paramref name="along"/>.</summary>
        private void Extrude(ShapeMesh mesh, int material, Vector3 center, Vector3 along, List<Vector2> outline, float length)
        {
            Vector3 z = _turn * along;
            Vector3 x = Vector3.Up.Cross(z);
            mesh.Extrude(material, World(center), new Basis(x, Vector3.Up, z), outline, length);
        }

        private float R(float a, float b) => a + (float)_random.NextDouble() * (b - a);

        private static Rect2 Rect(float[] r) => new(r[0], r[1], r[2] - r[0], r[3] - r[1]);
    }
}
