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

    /// <summary>Truss members: chords and webs (square sections, m), and the purlins on top (width, depth) and their spacing.</summary>
    private const float Chord = 0.12f, Web = 0.06f, PurlinWidth = 0.07f, PurlinDepth = 0.12f, PurlinSpacing = 1.6f;

    /// <summary>
    /// Adds every building's details to <paramref name="meshFor"/> (the mesh for a world position);
    /// <paramref name="material"/> resolves a kit material id. Returns how many buildings got any.
    /// </summary>
    public static int Build(LevelLayout level, Func<string, int> material, Func<Vector3, ShapeMesh> meshFor)
    {
        int dressed = 0;
        foreach (PlacedBuilding building in level.Buildings)
        {
            BuildingDef def = building.Template.Def;
            if (def.Roof is null || def.Gutters is null && def.Trusses is null)
            {
                continue;
            }

            var on = new Building(def, building.Frame, meshFor, new Random(LevelBuilder.StableHash(level.Id) ^ building.Owner * 7919));
            if (def.Gutters is not null && material(def.Gutters) is var gutters and >= 0)
            {
                on.Gutters(gutters);
            }

            if (def.Trusses is not null && material(def.Trusses.Material) is var trusses and >= 0)
            {
                on.Trusses(def.Trusses, trusses);
            }

            dressed++;
        }

        return dressed;
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

        public Building(BuildingDef def, PlanFrame frame, Func<Vector3, ShapeMesh> meshFor, Random random)
        {
            _def = def;
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
        public void Trusses(TrussesDef def, int material)
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
            for (int t = 1; t <= trusses; t++)
            {
                float u = start + spacing * t;
                ShapeMesh mesh = Mesh(Plan(u, 0f, (lo + hi) * 0.5f), _roofBottom);
                // Where a hole crosses this truss, the span it loses (along s).
                var broken = new List<(float From, float To, float Droop, float Hang)>();
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
                // The bracket back to the wall.
                float reach = MathF.Max(standOff - PipeRadius, 0.01f);
                Box(mesh, material, at - outward * (PipeRadius + reach * 0.5f), new Vector3(0.012f, 0.03f, 0.012f) + Abs(outward) * reach, Basis.Identity);
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

        private ShapeMesh Mesh(Vector2 plan, float height) => Mesh(new Vector3(plan.X, 0f, plan.Y), height);

        private ShapeMesh Mesh(Vector3 plan, float height)
        {
            ShapeMesh mesh = _meshFor(World(plan));
            mesh.Place(Transform3D.Identity, height);
            return mesh;
        }

        private Vector3 World(Vector3 plan) => _frame.ToWorld(new SVector3(plan.X, plan.Y, plan.Z)).ToGodot();

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
