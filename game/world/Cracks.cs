using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Collision;
using Pb.Sim.Level;

namespace Pb.Game.World;

/// <summary>
/// Cracks in the asphalt and concrete (presentation.jsonc "cracks"): the network the weeds grow along
/// (<see cref="CrackNetwork"/>, from the weeds' <c>crackMaterials</c> and <c>crackSpacing_m</c>) drawn
/// as dark lines wandering a little either side of it, open wide in some patches and close to hairlines
/// in others, each in a band of grime, with fine spurs branching off. A crack stops where the ground
/// changes. Ribbons in squares of ground (so they cull), multiplied over the ground and the paint on it;
/// looks only.
/// </summary>
public partial class Cracks : Node3D
{
    /// <summary>Points along a crack are about this far apart.</summary>
    private const float Step = 0.15f;

    /// <summary>A cut end narrows to nothing over this length.</summary>
    private const float Taper = 0.4f;

    private const float ChunkSize = 24f;

    private readonly List<MeshInstance3D> _chunks = new();

    /// <summary>Lines drawn (cracks and spurs).</summary>
    public int Count { get; private set; }

    /// <summary>Their length in all, in metres.</summary>
    public float Length_m { get; private set; }

    private readonly record struct Point(Vector2 At, float Y, float Width, float Strength);

    public void Build(LevelLayout level, ICollisionWorld world, WeedsDef weeds, CracksDef def)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        _chunks.Clear();
        Count = 0;
        Length_m = 0f;
        uint seed = (uint)LevelBuilder.StableHash(level.Id);
        var network = new CrackNetwork(weeds.CrackSpacing_m, seed);
        var survey = new GroundSurvey(level, world);
        var cracked = new HashSet<string>(weeds.CrackMaterials, StringComparer.Ordinal);
        var random = new Random((int)(seed ^ 0x3C7A5u));
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);

        // How open the cracks are here: patches where they gape, others where they're hairlines.
        float Open(float x, float z, string material)
        {
            float n = Noise(x / def.PatchSize_m, z / def.PatchSize_m, seed + 21) * 0.7f + Noise(x * 2.7f / def.PatchSize_m, z * 2.7f / def.PatchSize_m, seed + 22) * 0.3f;
            float scale = def.Open.TryGetValue(material, out float k) ? k : 1f;
            return Math.Clamp((n - 0.36f) / 0.42f, 0f, 1f) * scale;
        }

        bool On(Vector2 at, out float y, out string material)
        {
            material = survey.SurfaceAt(at.X, at.Y, out y);
            return cracked.Contains(material);
        }

        Pb.Sim.Collision.Aabb b = level.Bounds;
        var lines = new List<List<Point>>();
        var run = new List<Point>();
        foreach ((Vector2 a, Vector2 e) in network.Edges(new Rect2(b.Min.X, b.Min.Z, b.Max.X - b.Min.X, b.Max.Z - b.Min.Z)))
        {
            float length = a.DistanceTo(e);
            if (length < 0.05f || random.NextDouble() >= def.Drawn)
            {
                continue;
            }

            // The crack wanders either side of the network's straight edge, pinned at its ends, where it
            // meets the others.
            Vector2 dir = (e - a) / length, side = new(-dir.Y, dir.X);
            // Most edges fine, a few the main cracks.
            float r = (float)random.NextDouble();
            float character = 0.4f + 1.3f * r * r;
            int noise = random.Next();
            int steps = Math.Max(2, (int)MathF.Ceiling(length / Step));
            bool startCut = false;
            run.Clear();
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float pin = MathF.Min(1f, 6f * t * (1f - t));
                float wander = (Wave(t * length / 0.9f, noise) - 0.5f) * 2f + (Wave(t * length / 0.27f, noise + 1) - 0.5f) * 0.7f;
                Vector2 at = a + (e - a) * t + side * (wander * def.Wander_m * pin);
                bool on = On(at, out float y, out string material);
                if (on && run.Count > 0 && MathF.Abs(y - run[^1].Y) > 0.01f)
                {
                    // A step from one slab to another: the crack stops at the joint.
                    Keep(run, startCut, endCut: true);
                    startCut = true;
                }

                if (on)
                {
                    float open = MathF.Min(Open(at.X, at.Y, material) * character, 1.2f);
                    float width = Mathf.Lerp(def.Width_m[0], def.Width_m[1], MathF.Min(open, 1f)) * (0.75f + 0.5f * Wave(t * length / 0.6f, noise + 2));
                    run.Add(new Point(at, y, width, Mathf.Lerp(0.18f, 1f, MathF.Min(open, 1f))));
                }
                else
                {
                    Keep(run, startCut, endCut: true);
                    startCut = true;
                }
            }

            Keep(run, startCut, endCut: false);
        }

        // Fine spurs branching off the cracks, narrowing to nothing.
        int cracks = lines.Count;
        for (int c = 0; c < cracks; c++)
        {
            List<Point> parent = lines[c];
            float along = 0f;
            for (int i = 1; i < parent.Count; i++)
            {
                along += parent[i].At.DistanceTo(parent[i - 1].At);
            }

            int spurs = (int)(along * def.Spurs_perM + random.NextDouble());
            for (int k = 0; k < spurs && parent.Count > 4; k++)
            {
                int i = random.Next(2, parent.Count - 2);
                Point root = parent[i];
                Vector2 tangent = (parent[i + 1].At - parent[i - 1].At).Normalized();
                float turn = R(0.45f, 1.2f) * (random.Next(2) == 0 ? -1f : 1f);
                Vector2 dir = tangent.Rotated(turn);
                float length = R(def.SpurLength_m[0], def.SpurLength_m[1]) * (0.6f + root.Strength * 0.6f);
                int steps = Math.Max(3, (int)MathF.Ceiling(length / (Step * 0.7f)));
                int noise = random.Next();
                var spur = new List<Point> { root with { Width = root.Width * 0.6f } };
                for (int s = 1; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    // Curling a little as it goes.
                    Vector2 bend = dir.Rotated((Wave(t * 2.5f, noise) - 0.5f) * 0.8f);
                    Vector2 at = spur[^1].At + bend * (length / steps);
                    if (!On(at, out float y, out _) || MathF.Abs(y - root.Y) > 0.01f)
                    {
                        break;
                    }

                    spur.Add(new Point(at, y, root.Width * 0.6f * (1f - t), root.Strength * 0.85f));
                }

                if (spur.Count >= 3)
                {
                    lines.Add(spur);
                }
            }
        }

        // Alligator cracking: patches where the asphalt has broken up into small pieces, a fine network
        // of its own fading out towards the patch's edge.
        AlligatorDef al = def.Alligator;
        for (int placed = 0, attempt = 0; placed < al.Patches && attempt < al.Patches * 40; attempt++)
        {
            var centre = new Vector2(R(b.Min.X, b.Max.X), R(b.Min.Z, b.Max.Z));
            // On the ground that cracks most (asphalt rather than concrete).
            if (!On(centre, out float cy, out string under) || (def.Open.TryGetValue(under, out float k) ? k : 1f) < 1f)
            {
                continue;
            }

            float radius = R(al.Radius_m[0], al.Radius_m[1]);
            var fine = new CrackNetwork(al.Spacing_m, seed + 101u + (uint)placed);
            int noise = random.Next();
            foreach ((Vector2 a, Vector2 e) in fine.Edges(new Rect2(centre - Vector2.One * radius, Vector2.One * radius * 2f)))
            {
                Vector2 mid = (a + e) * 0.5f;
                // A ragged edge: the patch reaches further one way than another.
                float angle = MathF.Atan2(mid.Y - centre.Y, mid.X - centre.X);
                float reach = radius * (0.7f + 0.5f * Wave(angle * 1.6f + 10f, noise));
                float t = mid.DistanceTo(centre) / reach;
                if (t > 1f || random.NextDouble() < t * t * 0.6f)
                {
                    continue;
                }

                if (!On(a, out float ya, out _) || !On(e, out float ye, out _) || MathF.Abs(ya - cy) > 0.01f || MathF.Abs(ye - cy) > 0.01f)
                {
                    continue;
                }

                float strength = (1f - t * t) * R(0.5f, 0.9f);
                float width = Mathf.Lerp(def.Width_m[0], def.Width_m[1] * 0.6f, strength);
                lines.Add(new List<Point> { new(a, ya, width, strength), new((a + e) * 0.5f, cy, width * 1.1f, strength), new(e, ye, width, strength) });
            }

            placed++;
        }

        // A run of a crack: cut ends (where the ground changed) narrow to nothing.
        void Keep(List<Point> points, bool startCut, bool endCut)
        {
            if (points.Count >= 2)
            {
                var line = new List<Point>(points);
                float total = 0f;
                var distance = new float[line.Count];
                for (int i = 1; i < line.Count; i++)
                {
                    total += line[i].At.DistanceTo(line[i - 1].At);
                    distance[i] = total;
                }

                for (int i = 0; i < line.Count; i++)
                {
                    float k = 1f;
                    if (startCut)
                    {
                        k = MathF.Min(k, Smooth(distance[i] / Taper));
                    }

                    if (endCut)
                    {
                        k = MathF.Min(k, Smooth((total - distance[i]) / Taper));
                    }

                    line[i] = line[i] with { Width = line[i].Width * k };
                }

                if (total > 0.1f)
                {
                    lines.Add(line);
                }
            }

            points.Clear();
        }

        Count = lines.Count;
        if (lines.Count == 0)
        {
            return;
        }

        var shader = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/cracks.gdshader"), RenderPriority = 1 };
        shader.SetShaderParameter("halo", def.Halo_m);
        shader.SetShaderParameter("halo_darkness", def.HaloDarkness);
        shader.SetShaderParameter("halo_color", Color.FromHtml(def.HaloColor));
        shader.SetShaderParameter("darkness", def.Darkness);
        shader.SetShaderParameter("core_color", Color.FromHtml(def.Color));
        shader.SetShaderParameter("rim", def.Rim);
        shader.SetShaderParameter("fade_start", def.FadeStart_m);
        shader.SetShaderParameter("fade_end", def.FadeEnd_m);

        var groups = new SortedDictionary<(int X, int Z), Ribbons>();
        foreach (List<Point> line in lines)
        {
            Vector2 first = line[0].At;
            var key = ((int)MathF.Floor(first.X / ChunkSize), (int)MathF.Floor(first.Y / ChunkSize));
            if (!groups.TryGetValue(key, out Ribbons? ribbons))
            {
                groups[key] = ribbons = new Ribbons();
            }

            // Each line's jaggedness starts somewhere different.
            Length_m += ribbons.Add(line, def.Halo_m, R(0f, 100f));
        }

        foreach (((int cx, int cz), Ribbons ribbons) in groups)
        {
            var node = new MeshInstance3D
            {
                Name = $"Cracks_{cx}_{cz}",
                Mesh = ribbons.Commit(),
                MaterialOverride = shader,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                // Measured to the square's centre: drawn while any crack in it could show.
                VisibilityRangeEnd = def.FadeEnd_m + ChunkSize,
            };
            AddChild(node);
            _chunks.Add(node);
        }
    }

    private static float Smooth(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Smooth 1D value noise in 0–1 with features one unit apart.</summary>
    private static float Wave(float x, int seed)
    {
        int i = (int)MathF.Floor(x);
        float f = Smooth(x - i);
        return Mathf.Lerp(CrackNetwork.Hash01(i, seed, 0x51u), CrackNetwork.Hash01(i + 1, seed, 0x51u), f);
    }

    /// <summary>Smooth 2D value noise in 0–1 with features one unit across.</summary>
    private static float Noise(float x, float z, uint seed)
    {
        int ix = (int)MathF.Floor(x), iz = (int)MathF.Floor(z);
        float fx = Smooth(x - ix), fz = Smooth(z - iz);
        float top = Mathf.Lerp(CrackNetwork.Hash01(ix, iz, seed), CrackNetwork.Hash01(ix + 1, iz, seed), fx);
        float bottom = Mathf.Lerp(CrackNetwork.Hash01(ix, iz + 1, seed), CrackNetwork.Hash01(ix + 1, iz + 1, seed), fx);
        return Mathf.Lerp(top, bottom, fz);
    }

    /// <summary>
    /// Flat ribbons along crack lines, a hair above the ground. UV: metres along the line (from a random
    /// start, so no two lines jag alike), and −1 to 1 across the ribbon; UV2: the crack's half-width in
    /// metres and how strong it is; colour red: how far the ribbon reaches past the end of its line (each
    /// end is carried on by the band's width, so the shader can round it off and cracks meeting at a
    /// corner join up), green: how far the nearer end is (the line's jitter dies away towards its ends,
    /// so it meets the others there).
    /// </summary>
    private sealed class Ribbons
    {
        private const float Lift = 0.004f;

        private readonly List<Vector3> _vertices = new();
        private readonly List<Vector2> _uv = new();
        private readonly List<Vector2> _uv2 = new();
        private readonly List<Color> _colors = new();
        private readonly List<int> _indices = new();

        /// <summary>Adds one line and returns its length.</summary>
        public float Add(List<Point> line, float halo, float offset)
        {
            int start = _vertices.Count;
            int n = line.Count;
            var along = new float[n];
            for (int i = 1; i < n; i++)
            {
                along[i] = along[i - 1] + line[i].At.DistanceTo(line[i - 1].At);
            }

            float total = along[n - 1];
            for (int i = 0; i < n; i++)
            {
                Vector2 tangent = (line[Math.Min(i + 1, n - 1)].At - line[Math.Max(i - 1, 0)].At).Normalized();
                float end = MathF.Min(along[i], total - along[i]);
                if (i == 0)
                {
                    Pair(line[i], line[i].At - tangent * halo, tangent, halo, offset - halo, halo, 0f);
                }

                Pair(line[i], line[i].At, tangent, halo, offset + along[i], 0f, end);
                if (i == n - 1)
                {
                    Pair(line[i], line[i].At + tangent * halo, tangent, halo, offset + total + halo, halo, 0f);
                }
            }

            int pairs = (_vertices.Count - start) / 2;
            for (int k = 0; k < pairs - 1; k++)
            {
                int v = start + k * 2;
                _indices.Add(v);
                _indices.Add(v + 1);
                _indices.Add(v + 2);
                _indices.Add(v + 1);
                _indices.Add(v + 3);
                _indices.Add(v + 2);
            }

            return total;
        }

        private void Pair(Point p, Vector2 centre, Vector2 tangent, float halo, float along, float beyond, float end)
        {
            var normal = new Vector2(-tangent.Y, tangent.X);
            foreach (float side in new[] { -1f, 1f })
            {
                Vector2 at = centre + normal * (side * halo);
                _vertices.Add(new Vector3(at.X, p.Y + Lift, at.Y));
                _uv.Add(new Vector2(along, side));
                _uv2.Add(new Vector2(p.Width * 0.5f, p.Strength));
                _colors.Add(new Color(beyond, end, 0f));
            }
        }

        public ArrayMesh Commit()
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray();
            var normals = new Vector3[_vertices.Count];
            Array.Fill(normals, Vector3.Up);
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.TexUV] = _uv.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV2] = _uv2.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = _colors.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = _indices.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            return mesh;
        }
    }
}
