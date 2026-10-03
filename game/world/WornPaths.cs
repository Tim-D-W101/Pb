using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Level;
using Aabb = Pb.Sim.Collision.Aabb;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.World;

/// <summary>
/// Paths worn across the soft ground (presentation.jsonc "wornPaths"): where people have walked between
/// the doors and the gaps in the walls for years, across the dirt, dry grass and gravel, the ground is
/// trodden bare, smoother and dustier than round it, and nothing grows down the middle (the weeds keep off
/// <see cref="Clear"/>). The paths join the doorways and gaps and the clusters of cover out on the soft
/// ground (a spanning tree with a few loops, never through anything standing), wandering a little; drawn
/// as ribbons over the listed ground only, so they fade out where they reach asphalt or a floor. Looks only.
/// </summary>
public partial class WornPaths : Node3D
{
    private const float Step = 0.5f, GridCell = 0.25f;

    private bool[] _clear = Array.Empty<bool>();
    private Rect2 _area;
    private int _columns, _rows;

    /// <summary>Paths drawn, and their length in all (m).</summary>
    public int Count { get; private set; }

    public float Length_m { get; private set; }

    /// <summary>The paths' centre lines over the listed ground (X, Z), worked out by <see cref="Plan"/>.</summary>
    public List<List<Vector2>> Lines { get; } = new();

    /// <summary>
    /// Works out where the paths run and which ground they keep clear of weeds; call before the weeds are
    /// grown, then <see cref="Draw"/>.
    /// </summary>
    public void Plan(LevelLayout level, ICollisionWorld world, WornPathsDef def, IReadOnlyList<CoverPoint>? cover = null)
    {
        Lines.Clear();
        var survey = new GroundSurvey(level, world);
        var on = new HashSet<string>(def.On, StringComparer.Ordinal);
        var random = new Random(LevelBuilder.StableHash(level.Id) ^ 0x9A75);
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);

        // Where people come and go: either side of every doorway and gap at ground level (or low enough
        // to climb over).
        var nodes = new List<Vector2>();
        foreach (Aperture a in level.Apertures)
        {
            if (a.Kind is not (ApertureKind.Door or ApertureKind.Gap) || a.Center.Y - a.HalfHeight > (a.Kind == ApertureKind.Gap ? 1.2f : 0.3f))
            {
                continue;
            }

            SVector3 n = a.Normal;
            var flat = new Vector2(n.X, n.Z);
            if (flat.LengthSquared() < 0.5f)
            {
                continue;
            }

            flat = flat.Normalized();
            var centre = new Vector2(a.Center.X, a.Center.Z);
            foreach (float side in new[] { -1f, 1f })
            {
                Vector2 p = centre + flat * (side * 1.4f);
                if (!nodes.Exists(o => o.DistanceTo(p) < 2f))
                {
                    nodes.Add(p);
                }
            }
        }

        // And where people go to fight from on the soft ground: the middle of each cluster of cover spots
        // there, in squares of hubSpacing_m.
        var hubs = new List<Vector2>();
        if (cover is not null)
        {
            var cells = new SortedDictionary<(int X, int Z), (Vector2 Sum, int Count)>();
            foreach (CoverPoint c in cover)
            {
                if (c.Position.Y > 0.3f || !on.Contains(survey.SurfaceAt(c.Position.X, c.Position.Z, out _)))
                {
                    continue;
                }

                var key = ((int)MathF.Floor(c.Position.X / def.HubSpacing_m), (int)MathF.Floor(c.Position.Z / def.HubSpacing_m));
                cells.TryGetValue(key, out (Vector2 Sum, int Count) sum);
                cells[key] = (sum.Sum + new Vector2(c.Position.X, c.Position.Z), sum.Count + 1);
            }

            foreach ((Vector2 sum, int count) in cells.Values)
            {
                Vector2 hub = sum / count;
                if (count >= 3 && !nodes.Exists(o => o.DistanceTo(hub) < 2f))
                {
                    nodes.Add(hub);
                    hubs.Add(hub);
                }
            }
        }

        // Anything standing that a path goes round, as footprints.
        var standing = new List<Rect2>();
        foreach (LevelPrimitive p in level.Primitives)
        {
            if (p.Role is PrimitiveRole.Wall or PrimitiveRole.Column or PrimitiveRole.Prop && p.Bounds.Min.Y < 0.5f && p.Height > 0.6f)
            {
                Aabb b = p.Bounds;
                standing.Add(new Rect2(b.Min.X - 0.3f, b.Min.Z - 0.3f, b.Max.X - b.Min.X + 0.6f, b.Max.Z - b.Min.Z + 0.6f));
            }
        }

        bool Open(Vector2 a, Vector2 b)
        {
            foreach (Rect2 r in standing)
            {
                if (SegmentHitsRect(a, b, r))
                {
                    return false;
                }
            }

            return true;
        }

        // A spanning tree (Prim's) over the nodes, joining only those within reach with nothing between.
        var joined = new bool[nodes.Count];
        var edges = new List<(int A, int B)>();
        for (int start = 0; start < nodes.Count; start++)
        {
            if (joined[start])
            {
                continue;
            }

            joined[start] = true;
            var tree = new List<int> { start };
            while (true)
            {
                (int a, int b, float d) best = (-1, -1, float.MaxValue);
                foreach (int i in tree)
                {
                    for (int j = 0; j < nodes.Count; j++)
                    {
                        float d = nodes[i].DistanceTo(nodes[j]);
                        if (!joined[j] && d < best.d && d <= def.Longest_m && Open(nodes[i], nodes[j]))
                        {
                            best = (i, j, d);
                        }
                    }
                }

                if (best.a < 0)
                {
                    break;
                }

                joined[best.b] = true;
                tree.Add(best.b);
                edges.Add((best.a, best.b));
            }
        }

        // A few loops: each hub also takes the nearest node it isn't already joined to, if the way is open.
        foreach (Vector2 hub in hubs)
        {
            int h = nodes.IndexOf(hub);
            int best = -1;
            float bestDistance = def.Longest_m * 0.6f;
            for (int j = 0; j < nodes.Count; j++)
            {
                float d = nodes[h].DistanceTo(nodes[j]);
                if (j != h && d < bestDistance && !edges.Exists(e => e.A == h && e.B == j || e.A == j && e.B == h) && Open(nodes[h], nodes[j]))
                {
                    (best, bestDistance) = (j, d);
                }
            }

            if (best >= 0)
            {
                edges.Add((h, best));
            }
        }

        // Each edge wanders a little either side of the straight line; only the stretches over the listed
        // ground are kept.
        foreach ((int ia, int ib) in edges)
        {
            Vector2 a = nodes[ia], b = nodes[ib];
            float length = a.DistanceTo(b);
            Vector2 dir = (b - a) / length, side = new(-dir.Y, dir.X);
            float amplitude = MathF.Min(def.Wander_m, length * 0.08f);
            int seed = random.Next();
            int steps = Math.Max(2, (int)MathF.Ceiling(length / Step));
            var run = new List<Vector2>();
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float pin = MathF.Sin(MathF.PI * t);
                float wander = (Wave(t * length / 9f, seed) - 0.5f) * 2f * 0.7f + (Wave(t * length / 3.5f, seed + 1) - 0.5f) * 0.6f;
                Vector2 p = a + (b - a) * t + side * (wander * amplitude * pin);
                if (on.Contains(survey.SurfaceAt(p.X, p.Y, out _)))
                {
                    run.Add(p);
                }
                else
                {
                    Keep(run);
                }
            }

            Keep(run);
        }

        void Keep(List<Vector2> run)
        {
            // Stubs of a metre or two, where a path just clips the soft ground, aren't worth drawing.
            if (run.Count >= 7)
            {
                Lines.Add(new List<Vector2>(run));
            }

            run.Clear();
        }

        // Which ground the weeds keep off: a grid over the level marking everything near a centre line.
        Aabb bounds = level.Bounds;
        _area = new Rect2(bounds.Min.X, bounds.Min.Z, bounds.Max.X - bounds.Min.X, bounds.Max.Z - bounds.Min.Z);
        _columns = (int)MathF.Ceiling(_area.Size.X / GridCell);
        _rows = (int)MathF.Ceiling(_area.Size.Y / GridCell);
        _clear = new bool[_columns * _rows];
        foreach (List<Vector2> line in Lines)
        {
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Vector2 a = line[i], b = line[i + 1];
                float reach = def.WeedFree_m + R(-0.05f, 0.1f);
                int x0 = Cell(MathF.Min(a.X, b.X) - reach, _area.Position.X, _columns), x1 = Cell(MathF.Max(a.X, b.X) + reach, _area.Position.X, _columns);
                int z0 = Cell(MathF.Min(a.Y, b.Y) - reach, _area.Position.Y, _rows), z1 = Cell(MathF.Max(a.Y, b.Y) + reach, _area.Position.Y, _rows);
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        var p = new Vector2(_area.Position.X + (x + 0.5f) * GridCell, _area.Position.Y + (z + 0.5f) * GridCell);
                        if (SegmentDistance(p, a, b) < reach)
                        {
                            _clear[z * _columns + x] = true;
                        }
                    }
                }
            }
        }
    }

    /// <summary>Whether (x, z) is on a path's trodden middle, where nothing grows.</summary>
    public bool Clear(float x, float z)
    {
        if (_clear.Length == 0)
        {
            return false;
        }

        int cx = (int)((x - _area.Position.X) / GridCell), cz = (int)((z - _area.Position.Y) / GridCell);
        return cx >= 0 && cz >= 0 && cx < _columns && cz < _rows && _clear[cz * _columns + cx];
    }

    /// <summary>Draws the planned paths as ribbons a hair above the ground.</summary>
    public void Draw(LevelLayout level, ICollisionWorld world, WornPathsDef def)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        Count = Lines.Count;
        Length_m = 0f;
        if (Lines.Count == 0)
        {
            return;
        }

        var survey = new GroundSurvey(level, world);
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();
        var random = new Random(LevelBuilder.StableHash(level.Id) ^ 0x9A76);
        foreach (List<Vector2> line in Lines)
        {
            int start = vertices.Count;
            float along = (float)random.NextDouble() * 100f, total = 0f;
            for (int i = 0; i < line.Count; i++)
            {
                if (i > 0)
                {
                    float d = line[i].DistanceTo(line[i - 1]);
                    along += d;
                    total += d;
                }

                Vector2 tangent = (line[Math.Min(i + 1, line.Count - 1)] - line[Math.Max(i - 1, 0)]).Normalized();
                var normal = new Vector2(-tangent.Y, tangent.X);
                // Narrow where it runs on or off the soft ground, so it doesn't end in a hard edge.
                float ends = Math.Clamp(MathF.Min(i, line.Count - 1 - i) / 4f, 0.25f, 1f);
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector2 p = line[i] + normal * (side * def.Width_m * 0.5f * 1.6f);
                    survey.SurfaceAt(p.X, p.Y, out float y);
                    vertices.Add(new Vector3(p.X, y + 0.006f, p.Y));
                    uvs.Add(new Vector2(along, side / ends));
                }
            }

            Length_m += total;
            for (int k = 0; k + 1 < (vertices.Count - start) / 2; k++)
            {
                int v = start + k * 2;
                indices.AddRange(new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2 });
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        var normals = new Vector3[vertices.Count];
        Array.Fill(normals, Vector3.Up);
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/worn_paths.gdshader"), RenderPriority = -3 };
        material.SetShaderParameter("color", Color.FromHtml(def.Color));
        material.SetShaderParameter("opacity", def.Opacity);
        material.SetShaderParameter("width_share", 1f / 1.6f);
        material.SetShaderParameter("fade_start", def.FadeStart_m);
        material.SetShaderParameter("fade_end", def.FadeEnd_m);
        AddChild(new MeshInstance3D { Name = "Paths", Mesh = mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    private static int Cell(float v, float origin, int count) => Math.Clamp((int)((v - origin) / GridCell), 0, count - 1);

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Math.Clamp((p - a).Dot(ab) / MathF.Max(ab.LengthSquared(), 1e-8f), 0f, 1f);
        return p.DistanceTo(a + ab * t);
    }

    /// <summary>Whether the segment a–b crosses the rectangle (slab test).</summary>
    private static bool SegmentHitsRect(Vector2 a, Vector2 b, Rect2 r)
    {
        float t0 = 0f, t1 = 1f;
        Vector2 d = b - a;
        for (int axis = 0; axis < 2; axis++)
        {
            float origin = axis == 0 ? a.X : a.Y, delta = axis == 0 ? d.X : d.Y;
            float lo = axis == 0 ? r.Position.X : r.Position.Y, hi = axis == 0 ? r.End.X : r.End.Y;
            if (MathF.Abs(delta) < 1e-6f)
            {
                if (origin < lo || origin > hi)
                {
                    return false;
                }

                continue;
            }

            float u0 = (lo - origin) / delta, u1 = (hi - origin) / delta;
            if (u0 > u1)
            {
                (u0, u1) = (u1, u0);
            }

            t0 = MathF.Max(t0, u0);
            t1 = MathF.Min(t1, u1);
            if (t0 > t1)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Smooth 1D value noise in 0–1 with features one unit apart.</summary>
    private static float Wave(float x, int seed)
    {
        int i = (int)MathF.Floor(x);
        float f = x - i;
        f = f * f * (3f - 2f * f);
        return Mathf.Lerp(CrackNetwork.Hash01(i, seed, 0x77u), CrackNetwork.Hash01(i + 1, seed, 0x77u), f);
    }
}
