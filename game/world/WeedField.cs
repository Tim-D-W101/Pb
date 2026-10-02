using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Level;
using Aabb = Pb.Sim.Collision.Aabb;
using SQuaternion = System.Numerics.Quaternion;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.World;

/// <summary>
/// Weeds and dry grass wherever rain falls: crossed cards in one MultiMesh per square of ground (so
/// they cull), swaying in the wind and shrinking into the ground with distance. Tufts are placed at
/// load, seeded by the level id: thick on grassland, patchy on dirt and gravel, in a crack network on
/// asphalt and concrete, and along the foot of walls and props. A tuft goes only where a small sphere
/// dropped from the sky lands on the ground, so nothing grows indoors except under holes in the roof.
/// Presentation only: paint and players pass through weeds.
/// </summary>
public partial class WeedField : Node3D
{
    /// <summary>The atlas holds four tuft variants side by side, each this many pixels wide.</summary>
    private const int Variants = 4;

    private const int CellPx = 128;

    private const int HeightPx = 256;

    /// <summary>Weeds also grow this far past the level bounds, thinning out, so they don't stop at a line.</summary>
    private const float Margin = 24f;

    /// <summary>Radius of the sphere dropped from the sky to find open ground.</summary>
    private const float Clearance = 0.08f;

    private const float GroundTop = GroundSurvey.GroundTop;

    /// <summary>
    /// Variant mix (fine grass, seeding grass, broadleaf weed, wiry weed) and height factor for each kind
    /// of spot: grassland, other open ground, cracks, and the foot of walls and props.
    /// </summary>
    private static readonly float[][] Mix =
    {
        new[] { 0.45f, 0.35f, 0.1f, 0.1f },
        new[] { 0.25f, 0.15f, 0.3f, 0.3f },
        new[] { 0.3f, 0.1f, 0.35f, 0.25f },
        new[] { 0.3f, 0.2f, 0.25f, 0.25f },
    };

    private static readonly float[] HeightFactor = { 1.25f, 0.85f, 0.6f, 1.1f };

    private readonly List<(MultiMeshInstance3D Node, int Count)> _chunks = new();
    private ShaderMaterial? _material;
    private float _chunkSize = 16f;

    private enum Site
    {
        Grass,
        Ground,
        Crack,
        Edge,
    }

    public int TuftCount { get; private set; }

    /// <summary>Weeds over a level; nothing grows where <paramref name="clear"/> says (the worn paths).</summary>
    public void Build(LevelLayout level, ICollisionWorld world, WeedsDef def, Func<float, float, bool>? clear = null) =>
        Build(new LevelGround(level, world, clear), (uint)LevelBuilder.StableHash(level.Id), def);

    /// <summary>Weeds over any place that can say what its ground is (the training ground's), seeded by <paramref name="seed"/>.</summary>
    public void Build(IWeedGround ground, uint seed, WeedsDef def)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        _chunks.Clear();
        _chunkSize = def.ChunkSize_m;
        var rng = new Pcg32(seed, 0x5EED);
        var placer = new Placer(ground, def, seed);
        var tufts = new List<Tuft>();
        placer.Scatter(tufts, ref rng);
        placer.Edges(tufts, ref rng);
        TuftCount = tufts.Count;

        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/weeds.gdshader") };
        _material.SetShaderParameter("atlas", Atlas(seed));
        _material.SetShaderParameter("variants", (float)Variants);
        _material.SetShaderParameter("sway", def.WindSway_m);
        _material.SetShaderParameter("wind_speed", def.WindSpeed);
        _material.SetShaderParameter("gust_size", def.GustSize_m);

        ArrayMesh mesh = TuftMesh();
        var groups = new SortedDictionary<(int X, int Z), List<Tuft>>();
        foreach (Tuft t in tufts)
        {
            var key = ((int)MathF.Floor(t.Position.X / _chunkSize), (int)MathF.Floor(t.Position.Z / _chunkSize));
            if (!groups.TryGetValue(key, out List<Tuft>? list))
            {
                groups[key] = list = new List<Tuft>();
            }

            list.Add(t);
        }

        foreach (((int cx, int cz), List<Tuft> list) in groups)
        {
            // Shuffled, so drawing the first N of a chunk thins it evenly (the preset's weed density).
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = (int)(rng.NextUInt() % (uint)(i + 1));
                (list[i], list[j]) = (list[j], list[i]);
            }

            var multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                UseCustomData = true,
                Mesh = mesh,
            };
            multimesh.InstanceCount = list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                Tuft t = list[i];
                float w = t.Height * def.Width;
                Basis basis = Basis.FromEuler(new Vector3(t.TiltX, 0f, t.TiltZ)) * new Basis(Vector3.Up, t.Yaw) * Basis.FromScale(new Vector3(w, t.Height, w));
                multimesh.SetInstanceTransform(i, new Transform3D(basis, t.Position));
                multimesh.SetInstanceColor(i, t.Tint);
                multimesh.SetInstanceCustomData(i, new Color(t.Variant, 0f, 0f, 0f));
            }

            var node = new MultiMeshInstance3D
            {
                Name = $"Weeds_{cx}_{cz}",
                Multimesh = multimesh,
                MaterialOverride = _material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(node);
            _chunks.Add((node, list.Count));
        }
    }

    /// <summary>Thins the weeds and sets how far away they're drawn.</summary>
    public void ApplyPreset(GraphicsPresetDef preset)
    {
        Visible = preset.WeedDensity > 0f;
        foreach ((MultiMeshInstance3D node, int count) in _chunks)
        {
            node.Multimesh.VisibleInstanceCount = Mathf.RoundToInt(count * preset.WeedDensity);
            // Measured to the chunk's centre: a chunk stays drawn while any tuft in it could be.
            node.VisibilityRangeEnd = preset.WeedDistance_m + _chunkSize * 0.75f;
        }

        _material?.SetShaderParameter("fade_end", preset.WeedDistance_m);
        _material?.SetShaderParameter("fade_start", preset.WeedDistance_m * 0.7f);
    }

    private readonly record struct Tuft(Vector3 Position, float Height, float Yaw, float TiltX, float TiltZ, int Variant, Color Tint);

    /// <summary>
    /// What a weed field needs to know about a place: where to scatter, the ground's material at a point,
    /// whether rain falls on open ground there (and its height), the outlines of what stands on the ground
    /// (weeds grow along their feet), and the spots to keep clear.
    /// </summary>
    public interface IWeedGround
    {
        Aabb Bounds { get; }

        string MaterialAt(float x, float z);

        bool OpenGround(float x, float z, float clearance, out float y);

        /// <summary>Each outline on the ground with its middle (edge tufts grow on the side away from it); the list is reused.</summary>
        IEnumerable<(List<Vector2> Outline, Vector2 Centre)> Feet();

        bool KeepClear(float x, float z, float radius);
    }

    /// <summary>A compound level's ground: the ground survey, the walls, columns and props standing on it, and its pickups.</summary>
    private sealed class LevelGround : IWeedGround
    {
        private readonly LevelLayout _level;
        private readonly GroundSurvey _survey;

        private readonly Func<float, float, bool>? _clear;

        public LevelGround(LevelLayout level, ICollisionWorld world, Func<float, float, bool>? clear)
        {
            _clear = clear;
            _level = level;
            _survey = new GroundSurvey(level, world);
        }

        public Aabb Bounds => _level.Bounds;

        public string MaterialAt(float x, float z) => _survey.MaterialAt(x, z);

        public bool OpenGround(float x, float z, float clearance, out float y) => _survey.OpenGround(x, z, clearance, out y);

        public IEnumerable<(List<Vector2> Outline, Vector2 Centre)> Feet()
        {
            var outline = new List<Vector2>();
            foreach (LevelPrimitive p in _level.Primitives)
            {
                Aabb bounds = p.Bounds;
                if (p.Role is not (PrimitiveRole.Wall or PrimitiveRole.Prop or PrimitiveRole.Column) || !p.Has(PrimitiveFlags.Paint) ||
                    bounds.Min.Y > GroundTop || bounds.Max.Y - bounds.Min.Y < 0.2f)
                {
                    continue;
                }

                Footprint(p, outline);
                yield return (outline, new Vector2(p.Center.X, p.Center.Z));
            }
        }

        public bool KeepClear(float x, float z, float radius)
        {
            if (_clear?.Invoke(x, z) == true)
            {
                return true;
            }

            float r2 = radius * radius;
            foreach (PickupSpec p in _level.Pickups)
            {
                float dx = p.Position.X - x, dz = p.Position.Z - z;
                if (dx * dx + dz * dz < r2)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The outline of a primitive's foot on the ground (upright boxes and cylinders exactly, anything else by its bounds).</summary>
        private static void Footprint(LevelPrimitive p, List<Vector2> outline)
        {
            outline.Clear();
            SVector3 up = SVector3.Transform(SVector3.UnitY, p.Rotation);
            if (up.Y > 0.95f && p.Kind == PrimitiveKind.Box)
            {
                SVector3 ax = SVector3.Transform(SVector3.UnitX, p.Rotation) * p.HalfExtents.X;
                SVector3 az = SVector3.Transform(SVector3.UnitZ, p.Rotation) * p.HalfExtents.Z;
                foreach ((float u, float w) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                {
                    SVector3 c = p.Center + ax * u + az * w;
                    outline.Add(new Vector2(c.X, c.Z));
                }
            }
            else if (up.Y > 0.95f)
            {
                float r = p.HalfExtents.X;
                int segments = Math.Clamp((int)(r * 12f), 8, 24);
                for (int i = 0; i < segments; i++)
                {
                    float a = Mathf.Tau * i / segments;
                    outline.Add(new Vector2(p.Center.X + MathF.Cos(a) * r, p.Center.Z + MathF.Sin(a) * r));
                }
            }
            else
            {
                Aabb b = p.Bounds;
                outline.Add(new Vector2(b.Min.X, b.Min.Z));
                outline.Add(new Vector2(b.Max.X, b.Min.Z));
                outline.Add(new Vector2(b.Max.X, b.Max.Z));
                outline.Add(new Vector2(b.Min.X, b.Max.Z));
            }
        }
    }

    /// <summary>Decides where tufts grow: material, patchiness, cracks and the open-sky test.</summary>
    private sealed class Placer
    {
        private readonly IWeedGround _ground;
        private readonly WeedsDef _def;
        private readonly uint _seed;
        private readonly HashSet<string> _cracked;
        private readonly CrackNetwork _network;
        private readonly HashSet<string> _grass;
        private readonly Color[] _colors;

        public Placer(IWeedGround ground, WeedsDef def, uint seed)
        {
            _ground = ground;
            _def = def;
            _seed = seed;
            _cracked = new HashSet<string>(def.CrackMaterials, StringComparer.Ordinal);
            _network = new CrackNetwork(def.CrackSpacing_m, seed);
            _grass = new HashSet<string>(def.GrassMaterials, StringComparer.Ordinal);
            _colors = def.Colors.Select(c => Color.FromHtml(c)).ToArray();
        }

        /// <summary>Tufts over open ground: one chance per cell of a jittered grid, weighted by the ground's density.</summary>
        public void Scatter(List<Tuft> tufts, ref Pcg32 rng)
        {
            float maxDensity = 0.01f;
            foreach (float d in _def.Density_perM2.Values)
            {
                maxDensity = MathF.Max(maxDensity, d);
            }

            float cell = Math.Clamp(1f / MathF.Sqrt(maxDensity), 0.2f, 0.6f);
            Aabb b = _ground.Bounds;
            for (float z = b.Min.Z - Margin; z < b.Max.Z + Margin; z += cell)
            {
                for (float x = b.Min.X - Margin; x < b.Max.X + Margin; x += cell)
                {
                    float px = x + rng.NextFloat() * cell;
                    float pz = z + rng.NextFloat() * cell;
                    float roll = rng.NextFloat();
                    string material = MaterialAt(px, pz);
                    if (!_def.Density_perM2.TryGetValue(material, out float density) || density <= 0f)
                    {
                        continue;
                    }

                    bool crack = _cracked.Contains(material);
                    float chance = density * cell * cell * Clump(px, pz) * Inside(px, pz) * (crack ? Crack(px, pz) : 1f);
                    if (roll < chance && !NearPickup(px, pz) && OpenGround(px, pz, out float y))
                    {
                        Site site = crack ? Site.Crack : _grass.Contains(material) ? Site.Grass : Site.Ground;
                        tufts.Add(Make(px, y, pz, site, ref rng));
                    }
                }
            }
        }

        /// <summary>Tufts along the foot of walls, columns and props that stand on open ground.</summary>
        public void Edges(List<Tuft> tufts, ref Pcg32 rng)
        {
            foreach ((List<Vector2> outline, Vector2 centre) in _ground.Feet())
            {
                for (int i = 0; i < outline.Count; i++)
                {
                    Vector2 a = outline[i];
                    Vector2 edge = outline[(i + 1) % outline.Count] - a;
                    float length = edge.Length();
                    if (length < 0.05f)
                    {
                        continue;
                    }

                    var normal = new Vector2(edge.Y, -edge.X) / length;
                    if (normal.Dot(a + edge * 0.5f - centre) < 0f)
                    {
                        normal = -normal;
                    }

                    int count = (int)(length * _def.EdgeDensity_perM + rng.NextFloat());
                    for (int k = 0; k < count; k++)
                    {
                        Vector2 q = a + edge * rng.NextFloat() + normal * (Clearance + 0.01f + rng.NextFloat() * _def.EdgeReach_m);
                        float roll = rng.NextFloat();
                        if (roll < 0.15f + 0.85f * Clump(q.X, q.Y) && !NearPickup(q.X, q.Y) && OpenGround(q.X, q.Y, out float y))
                        {
                            tufts.Add(Make(q.X, y, q.Y, Site.Edge, ref rng));
                        }
                    }
                }
            }
        }

        private Tuft Make(float x, float y, float z, Site site, ref Pcg32 rng)
        {
            float[] mix = Mix[(int)site];
            float pick = rng.NextFloat();
            int variant = 0;
            while (variant < Variants - 1 && pick >= mix[variant])
            {
                pick -= mix[variant];
                variant++;
            }

            float height = Mathf.Lerp(_def.HeightMin_m, _def.HeightMax_m, MathF.Pow(rng.NextFloat(), 1.6f)) * HeightFactor[(int)site];
            Color tint = _colors[(int)(rng.NextUInt() % (uint)_colors.Length)] * rng.Range(0.9f, 1.1f);
            tint.A = 1f;
            // Sunk a little so the base never shows a seam; the scrubland past the bounds lies 3 cm lower.
            float sink = Inside(x, z) < 1f ? 0.05f : 0.02f;
            return new Tuft(new Vector3(x, y - sink, z), MathF.Max(height, 0.05f), rng.NextFloat() * Mathf.Tau,
                rng.Symmetric(0.12f), rng.Symmetric(0.12f), variant, tint.SrgbToLinear());
        }

        private string MaterialAt(float x, float z) => _ground.MaterialAt(x, z);

        /// <summary>True when a small sphere dropped from the sky lands flat on the ground at (x, z).</summary>
        private bool OpenGround(float x, float z, out float y) => _ground.OpenGround(x, z, Clearance, out y);

        private bool NearPickup(float x, float z) => _ground.KeepClear(x, z, _def.PickupClearance_m);

        /// <summary>1 inside the level bounds, falling to 0 at the edge of the margin past them.</summary>
        private float Inside(float x, float z)
        {
            Aabb b = _ground.Bounds;
            float dx = MathF.Max(0f, MathF.Max(b.Min.X - x, x - b.Max.X));
            float dz = MathF.Max(0f, MathF.Max(b.Min.Z - z, z - b.Max.Z));
            return 1f - Math.Clamp(MathF.Max(dx, dz) / Margin, 0f, 1f);
        }

        /// <summary>Patches: ~0 on the bare share of the ground, 1 in the thick of a patch.</summary>
        private float Clump(float x, float z)
        {
            float n = ValueNoise(x / _def.ClumpSize_m, z / _def.ClumpSize_m, _seed) * 0.7f +
                      ValueNoise(x * 2.3f / _def.ClumpSize_m, z * 2.3f / _def.ClumpSize_m, _seed + 1) * 0.3f;
            return Smoothstep(_def.Bare - 0.12f, _def.Bare + 0.12f, n);
        }

        /// <summary>1 on a crack, 0 away from one (the network <see cref="Cracks"/> draws).</summary>
        private float Crack(float x, float z) =>
            1f - Smoothstep(_def.CrackWidth_m * 0.25f, _def.CrackWidth_m * 0.5f, _network.Distance(x, z));
    }

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Smooth value noise in 0–1 with features one unit across.</summary>
    private static float ValueNoise(float x, float z, uint seed)
    {
        int ix = (int)MathF.Floor(x), iz = (int)MathF.Floor(z);
        float fx = x - ix, fz = z - iz;
        fx = fx * fx * (3f - 2f * fx);
        fz = fz * fz * (3f - 2f * fz);
        float top = Mathf.Lerp(CrackNetwork.Hash01(ix, iz, seed), CrackNetwork.Hash01(ix + 1, iz, seed), fx);
        float bottom = Mathf.Lerp(CrackNetwork.Hash01(ix, iz + 1, seed), CrackNetwork.Hash01(ix + 1, iz + 1, seed), fx);
        return Mathf.Lerp(top, bottom, fz);
    }

    /// <summary>
    /// Three cards crossing at 60°, one unit wide and tall, standing on the origin. UV v runs from 0 at
    /// the tips to 1 at the base; the vertex colour darkens the base (the tuft shades itself).
    /// </summary>
    private static ArrayMesh TuftMesh()
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        void V(Vector3 p, float u, float v)
        {
            float shade = Mathf.Lerp(1f, 0.5f, v);
            tool.SetNormal(Vector3.Up);
            tool.SetColor(new Color(shade, shade, shade));
            tool.SetUV(new Vector2(u, v));
            tool.AddVertex(p);
        }

        for (int k = 0; k < 3; k++)
        {
            float a = k * Mathf.Pi / 3f;
            var half = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.5f;
            Vector3 bl = -half, br = half, tl = -half + Vector3.Up, tr = half + Vector3.Up;
            V(bl, 0f, 1f);
            V(tl, 0f, 0f);
            V(tr, 1f, 0f);
            V(bl, 0f, 1f);
            V(tr, 1f, 0f);
            V(br, 1f, 1f);
        }

        return tool.Commit();
    }

    /// <summary>The four tuft variants, painted procedurally (sRGB, with the colour bled into the clear pixels for clean mipmaps).</summary>
    private static ImageTexture Atlas(uint seed)
    {
        var painter = new TuftPainter(CellPx * Variants, HeightPx, new Pcg32(seed, 0xA71A5));
        for (int v = 0; v < Variants; v++)
        {
            painter.Paint(v, v * CellPx, CellPx);
        }

        Image image = Image.CreateFromData(CellPx * Variants, HeightPx, false, Image.Format.Rgba8, painter.ToBytes(CellPx));
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }
}
