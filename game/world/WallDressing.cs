using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Data;
using Pb.Sim.Level;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.World;

/// <summary>
/// What a free-standing wall run carries besides the wall (level wall "dressing"): piers standing a
/// little proud of it at regular spacing, at its corners and either side of each break; a coping
/// course along its top; barbed wire on angle-iron brackets leaning out (away from the middle of a
/// closed run), three strands sagging between them and hanging loose at the breaks; and broken blocks
/// on the ground either side of each break, and on top of a stretch knocked down to waist height.
/// Presentation only: paint and walking see just the wall. Varied by a seed from the level.
/// </summary>
public static class WallDressing
{
    private const float CapHeight = 0.07f, CapOverhang = 0.035f, CopingHeight = 0.07f, CopingOverhang = 0.03f;

    /// <summary>The wire brackets: arm length, its angle out from upright (rad), and where the strands sit along it.</summary>
    private const float ArmLength = 0.55f, ArmLean = 0.7f, WireRadius = 0.0055f;

    private static readonly float[] Strands = { 0.3f, 0.63f, 0.96f };

    /// <summary>
    /// Adds each dressed wall run's details to <paramref name="meshFor"/> (the mesh for a world
    /// position); <paramref name="material"/> resolves a kit material id. Where rain will run off the
    /// coping and the wire's brackets goes in <paramref name="drips"/>, where the piers stand (their
    /// middles and half-widths, cap included) in <paramref name="piers"/>, and where the wire's strands
    /// run in <paramref name="strands"/>. Returns how many runs were dressed.
    /// </summary>
    public static int Build(LevelLayout level, Func<string, int> material, Func<Vector3, ShapeMesh> meshFor, List<Drip>? drips = null, List<(Vector3 At, float Half)>? piers = null, List<WireStrand>? strands = null)
    {
        int dressed = 0;
        foreach (PlacedWall wall in level.Walls)
        {
            if (wall.Def.Dressing is null || float.IsNaN(wall.Def.Height_m))
            {
                continue;
            }

            new Run(wall, material, meshFor, new Random(LevelBuilder.StableHash(level.Id) ^ wall.Owner * 104729), drips, piers, strands).Build();
            dressed++;
        }

        return dressed;
    }

    private sealed class Run
    {
        private readonly WallDef _def;
        private readonly WallDressingDef _dressing;
        private readonly PlanFrame _frame;
        private readonly Func<Vector3, ShapeMesh> _meshFor;
        private readonly Random _random;
        private readonly int _wall, _pier, _coping, _wire;
        private readonly float _base, _height;
        private readonly Vector2 _middle;
        private readonly List<Drip>? _drips;
        private readonly List<(Vector3 At, float Half)>? _piers;
        private readonly List<WireStrand>? _strands;

        public Run(PlacedWall placed, Func<string, int> material, Func<Vector3, ShapeMesh> meshFor, Random random, List<Drip>? drips, List<(Vector3 At, float Half)>? piers, List<WireStrand>? strands)
        {
            _piers = piers;
            _strands = strands;
            _def = placed.Def;
            _drips = drips;
            _dressing = placed.Def.Dressing!;
            _frame = placed.Frame;
            _meshFor = meshFor;
            _random = random;
            _wall = material(_def.Material);
            _pier = _dressing.PierMaterial is { } p ? material(p) : _wall;
            _coping = _dressing.Coping is { } c ? material(c) : -1;
            _wire = _dressing.Wire is { } w ? material(w) : -1;
            _base = float.IsNaN(_def.BaseElevation_m) ? 0f : _def.BaseElevation_m;
            _height = _def.Height_m;
            foreach (float[] point in _def.Points_m)
            {
                _middle += new Vector2(point[0], point[1]) / _def.Points_m.Length;
            }
        }

        public void Build()
        {
            for (int s = 0; s < _def.SegmentCount; s++)
            {
                float[] pa = _def.Points_m[s], pb = _def.Points_m[(s + 1) % _def.Points_m.Length];
                var a = new Vector2(pa[0], pa[1]);
                var b = new Vector2(pb[0], pb[1]);
                float length = a.DistanceTo(b);
                if (length < 0.2f)
                {
                    continue;
                }

                Segment(s, a, (b - a) / length, length);
            }
        }

        private void Segment(int index, Vector2 a, Vector2 dir, float length)
        {
            // Out: away from the middle of a closed run (open runs have no outside; their wire stands up).
            var across = new Vector2(-dir.Y, dir.X);
            float side = _def.Closed ? (across.Dot(a + dir * length * 0.5f - _middle) >= 0f ? 1f : -1f) : 0f;
            Vector2 outward = across * (side == 0f ? 1f : side);

            // Breaks in the top of the wall: gaps, and openings up to the top (stretches knocked down to a sill).
            var breaks = new List<(float From, float To, float Sill)>();
            foreach (OpeningDef o in _def.Openings ?? Array.Empty<OpeningDef>())
            {
                if (o.Segment != index)
                {
                    continue;
                }

                bool gap = o.Kind == OpeningKind.Gap;
                float sill = gap ? 0f : o.Sill_m;
                bool toTop = gap || float.IsNaN(o.Height_m) || sill + o.Height_m >= _height - 0.05f;
                if (toTop)
                {
                    breaks.Add((o.At_m - o.Width_m * 0.5f, o.At_m + o.Width_m * 0.5f, sill));
                }
            }

            breaks.Sort((x, y) => x.From.CompareTo(y.From));
            // The stretches whose top is whole, between the breaks.
            var whole = new List<(float From, float To)>();
            float start = 0f;
            foreach ((float from, float to, _) in breaks)
            {
                if (from > start)
                {
                    whole.Add((start, MathF.Min(from, length)));
                }

                start = MathF.Max(start, to);
            }

            if (start < length)
            {
                whole.Add((start, length));
            }

            float size = _dressing.PierSize_m;
            Vector3 Plan(float t, float y, float off) => new(a.X + dir.X * t + outward.X * off, y, a.Y + dir.Y * t + outward.Y * off);
            // Boxes lie with X along the wall and Z across it.
            var turn = new Basis(new Vector3(dir.X, 0f, dir.Y), Vector3.Up, new Vector3(-dir.Y, 0f, dir.X));
            float top = _base + _height;

            foreach ((float from, float to) in whole)
            {
                // Piers at each end of a whole stretch (the corner, or beside a break) and evenly between.
                var piers = new List<float>();
                if (_dressing.PierSpacing_m > 0f)
                {
                    float first = from + size * 0.5f, last = to - size * 0.5f;
                    bool corner = from <= 0f && _def.Closed;
                    piers.Add(corner ? 0f : first);
                    int count = Math.Max(0, (int)MathF.Round((last - first) / _dressing.PierSpacing_m));
                    for (int k = 1; k < count; k++)
                    {
                        piers.Add(Mathf.Lerp(first, last, (float)k / count));
                    }

                    // A closed run's last corner is the next segment's first.
                    if (!(to >= length && _def.Closed) && last > first + size)
                    {
                        piers.Add(last);
                    }
                }

                foreach (float t in piers)
                {
                    ShapeMesh mesh = Mesh(Plan(t, 0f, 0f));
                    _piers?.Add((World(Plan(t, 0f, 0f)), size * 0.5f + CapOverhang));
                    float h = _height + CopingHeight;
                    Box(mesh, _pier, Plan(t, _base + h * 0.5f, 0f), new Vector3(size, h, size), turn);
                    Box(mesh, _pier, Plan(t, _base + h + CapHeight * 0.5f, 0f), new Vector3(size + 2f * CapOverhang, CapHeight, size + 2f * CapOverhang), turn);
                }

                if (_coping >= 0)
                {
                    float mid = (from + to) * 0.5f;
                    Box(Mesh(Plan(mid, 0f, 0f)), _coping, Plan(mid, top + CopingHeight * 0.5f, 0f),
                        new Vector3(to - from, CopingHeight, _def.Thickness_m + 2f * CopingOverhang), turn);
                    // Stains down both faces where rain drips off it.
                    foreach (float face in new[] { -1f, 1f })
                    {
                        Drip(Plan(mid, top - 0.02f, face * _def.Thickness_m * 0.5f), new Vector3(outward.X, 0f, outward.Y) * face, 0f, _dressing.Coping!, to - from - 0.3f);
                    }
                }

                if (_wire >= 0)
                {
                    Wire(from, to, length, piers, Plan, side != 0f);
                    // Rust runs from each bracket down the outside of the pier under it.
                    foreach (float t in piers)
                    {
                        Drip(Plan(t, top + CopingHeight - 0.01f, size * 0.5f), new Vector3(outward.X, 0f, outward.Y), 0.08f, _dressing.Wire!);
                    }
                }
            }

            if (_dressing.Rubble)
            {
                foreach ((float from, float to, float sill) in breaks)
                {
                    Rubble(from, to, sill, Plan, turn);
                }
            }

            foreach (int gate in _dressing.FallenGates ?? Array.Empty<int>())
            {
                OpeningDef o = _def.Openings![gate];
                if (o.Segment == index && o.Kind == OpeningKind.Gap)
                {
                    FallenGate(o.At_m - o.Width_m * 0.5f, o.At_m + o.Width_m * 0.5f, Plan, turn);
                }
            }
        }

        /// <summary>
        /// One leaf of the gate that hung in a gap, off its hinges and lying on the ground outside it, in
        /// front of one half of the gap and turned a little off square: a rusty frame of box section with a
        /// rail across its middle and bars between, a brace across it, a bar or two gone, one end propped on
        /// a broken block. A few centimetres high, so it's looks only like the rest.
        /// </summary>
        private void FallenGate(float from, float to, Func<float, float, float, Vector3> plan, Basis turn)
        {
            float width = MathF.Min((to - from) * 0.5f + 0.15f, 3.8f), depth = 1.9f;
            int steel = _wire >= 0 ? _wire : _pier;
            bool left = _random.NextDouble() < 0.5;
            float t = left ? from + width * 0.5f + R(-0.2f, 0.5f) : to - width * 0.5f - R(-0.2f, 0.5f);
            Vector3 centre = plan(t, 0f, depth * 0.5f + R(0.7f, 1.4f));
            ShapeMesh mesh = Mesh(centre);
            float lift = R(0.03f, 0.08f);
            Basis leaf = turn * new Basis(Vector3.Up, R(-0.45f, 0.45f)) * new Basis(Vector3.Right, -lift);
            float rest = 0.03f + depth * 0.5f * MathF.Sin(lift);
            Vector3 At(float x, float z) => centre + Vector3.Up * rest + leaf * new Vector3(x, 0f, z);
            void Member(Vector3 a, Vector3 b, float size) => mesh.Bar(steel, World(a), World(b), size, size);
            float hx = width * 0.5f, hz = depth * 0.5f;
            Member(At(-hx, -hz), At(hx, -hz), 0.05f);
            Member(At(-hx, hz), At(hx, hz), 0.05f);
            Member(At(-hx, -hz), At(-hx, hz), 0.05f);
            Member(At(hx, -hz), At(hx, hz), 0.05f);
            Member(At(-hx, 0f), At(hx, 0f), 0.04f);
            Member(At(-hx, -hz), At(hx, hz), 0.035f);
            for (float x = -hx + 0.13f; x < hx - 0.06f; x += 0.13f)
            {
                if (_random.NextDouble() < 0.08)
                {
                    continue;
                }

                Member(At(x, -hz), At(x, hz), 0.018f);
            }

            // The block its raised end rests on.
            Block(mesh, centre + leaf * new Vector3(R(-hx * 0.6f, hx * 0.6f), 0f, hz - 0.1f), 0f, turn);
        }

        /// <summary>
        /// Brackets along a whole stretch (on the piers, or every few metres without them), three strands
        /// sagging between them, and loose ends hanging down where the stretch ends at a break.
        /// </summary>
        private void Wire(float from, float to, float length, List<float> piers, Func<float, float, float, Vector3> plan, bool lean)
        {
            var posts = new List<float>(piers);
            if (posts.Count < 2)
            {
                posts.Clear();
                int count = Math.Max(1, (int)MathF.Ceiling((to - from) / 3f));
                for (int k = 0; k <= count; k++)
                {
                    posts.Add(Mathf.Lerp(from + 0.1f, to - 0.1f, (float)k / count));
                }
            }

            float foot = _base + _height + CopingHeight + (piers.Count > 0 ? CapHeight : 0f);
            float angle = lean ? ArmLean : 0f;
            Vector3 Arm(float t, float along) => plan(t, foot + MathF.Cos(angle) * ArmLength * along, MathF.Sin(angle) * ArmLength * along);
            foreach (float t in posts)
            {
                Vector3 root = Arm(t, 0f), tip = Arm(t, 1f);
                Mesh(root).Bar(_wire, World(root), World(tip), 0.035f, 0.008f);
            }

            // Anything caught on a strand can hang no lower than the top of the wall under it.
            float under = _base + _height + (_coping >= 0 ? CopingHeight : 0f);
            for (int k = 0; k + 1 < posts.Count; k++)
            {
                foreach (float along in Strands)
                {
                    Vector3 p = Arm(posts[k], along), q = Arm(posts[k + 1], along);
                    float sag = 0.025f + 0.04f * (float)_random.NextDouble();
                    Sag(p, q, sag);
                    _strands?.Add(new WireStrand(World(p), World(q), sag, World(new Vector3(p.X, under, p.Z)).Y));
                }
            }

            // Cut ends hang loose where the stretch ends at a break.
            foreach ((float t, float dir, bool atBreak) in new[] { (posts[0], -1f, from > 0f), (posts[^1], 1f, to < length) })
            {
                if (!atBreak)
                {
                    continue;
                }

                foreach (float along in Strands)
                {
                    if (_random.NextDouble() < 0.35)
                    {
                        continue;
                    }

                    Vector3 p = Arm(t, along);
                    float reach = 0.3f + 0.8f * (float)_random.NextDouble(), drop = 0.6f + 1.1f * (float)_random.NextDouble();
                    Vector3 end = plan(t + dir * reach, p.Y - drop, (float)(_random.NextDouble() - 0.5) * 0.4f);
                    Hang(p, end);
                }
            }

        }

        /// <summary>A strand from p to q, sagging by <paramref name="sag"/> in the middle.</summary>
        private void Sag(Vector3 p, Vector3 q, float sag)
        {
            const int pieces = 4;
            ShapeMesh mesh = Mesh((p + q) * 0.5f);
            Vector3 previous = p;
            for (int i = 1; i <= pieces; i++)
            {
                float f = (float)i / pieces;
                Vector3 next = p.Lerp(q, f) + Vector3.Down * (sag * 4f * f * (1f - f));
                mesh.Rod(_wire, World(previous), World(next), WireRadius, 4, caps: false);
                previous = next;
            }
        }

        /// <summary>A loose end from <paramref name="p"/> curling down to <paramref name="end"/>.</summary>
        private void Hang(Vector3 p, Vector3 end)
        {
            const int pieces = 4;
            ShapeMesh mesh = Mesh(p);
            Vector3 previous = p;
            for (int i = 1; i <= pieces; i++)
            {
                float f = (float)i / pieces;
                // Out first, then down: it falls away from where it was cut.
                Vector3 next = new(Mathf.Lerp(p.X, end.X, MathF.Sqrt(f)), Mathf.Lerp(p.Y, end.Y, f * f), Mathf.Lerp(p.Z, end.Z, MathF.Sqrt(f)));
                mesh.Rod(_wire, World(previous), World(next), WireRadius, 4, caps: false);
                previous = next;
            }
        }

        /// <summary>
        /// Broken blocks either side of a break: by the ends of a gap (its middle left clear to walk),
        /// and along a stretch knocked down to a sill, with a few still sitting on what's left of it.
        /// </summary>
        private void Rubble(float from, float to, float sill, Func<float, float, float, Vector3> plan, Basis turn)
        {
            bool knocked = sill > 0.05f;
            ShapeMesh mesh = Mesh(plan((from + to) * 0.5f, 0f, 0f));
            float width = to - from;
            int count = knocked ? (int)(width * 3f) : 14;
            for (int i = 0; i < count; i++)
            {
                float t = knocked ? Mathf.Lerp(from, to, (float)_random.NextDouble())
                    : _random.NextDouble() < 0.5 ? from + R(-1.2f, 0.25f * width) : to - R(-1.2f, 0.25f * width);
                float off = (_random.NextDouble() < 0.5 ? -1f : 1f) * R(_def.Thickness_m * 0.5f + 0.1f, 1.7f);
                Block(mesh, plan(t, 0f, off), 0f, turn);
            }

            if (knocked)
            {
                // What's left of the courses above the sill: a few broken blocks on top.
                for (float t = from + R(0.1f, 0.4f); t < to - 0.2f; t += R(0.35f, 1.1f))
                {
                    Block(mesh, plan(t, 0f, R(-0.05f, 0.05f)), _base + sill, turn, onTop: true);
                }
            }
        }

        private void Block(ShapeMesh mesh, Vector3 at, float ground, Basis turn, bool onTop = false)
        {
            var size = new Vector3(R(0.22f, 0.45f), R(0.14f, 0.22f), onTop ? MathF.Min(_def.Thickness_m, R(0.18f, 0.3f)) : R(0.15f, 0.26f));
            Basis tilt = onTop ? new Basis(Vector3.Up, R(-0.15f, 0.15f)) * new Basis(Vector3.Right, R(-0.12f, 0.12f))
                : new Basis(Vector3.Up, R(0f, Mathf.Tau)) * new Basis(Vector3.Right, R(-0.35f, 0.35f)) * new Basis(Vector3.Forward, R(-0.35f, 0.35f));
            float sink = onTop ? 0.25f : 0.35f;
            Box(mesh, _wall, at with { Y = ground + size.Y * (0.5f - sink) }, size, (onTop ? turn : Basis.Identity) * tilt);
        }

        private ShapeMesh Mesh(Vector3 plan)
        {
            ShapeMesh mesh = _meshFor(World(plan));
            mesh.Place(Transform3D.Identity, _height);
            return mesh;
        }

        private Vector3 World(Vector3 plan) => _frame.ToWorld(new SVector3(plan.X, plan.Y, plan.Z)).ToGodot();

        /// <summary>Reports where rain runs off down the face at plan point <paramref name="plan"/>, facing plan direction <paramref name="outward"/>.</summary>
        private void Drip(Vector3 plan, Vector3 outward, float width, string material, float run = 0f) =>
            _drips?.Add(new Drip(World(plan), (new Basis(Vector3.Up, _frame.Yaw) * outward).Normalized(), width, material, run));

        private void Box(ShapeMesh mesh, int material, Vector3 plan, Vector3 size, Basis rotation) =>
            mesh.Box(material, World(plan), size, new Basis(Vector3.Up, _frame.Yaw) * rotation);

        private float R(float a, float b) => a + (float)_random.NextDouble() * (b - a);
    }
}
