using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Collision;
using Pb.Sim.Level;
using Aabb = Pb.Sim.Collision.Aabb;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.World;

/// <summary>
/// Things lying on the ground (presentation.jsonc "groundDetail"): oil stains round the car and the
/// machines, puddles under the open sky, damp patches, rust run-off by rusting things, tyre tracks,
/// drifts of leaves against walls, litter and broken chips. One atlas painted at load
/// (<see cref="StainPainter"/>), flat cards in one MultiMesh, placed on ground-level surfaces of the
/// listed materials, seeded by the level id so they're the same every run. Presentation only.
/// </summary>
public partial class GroundDetail : Node3D
{
    /// <summary>Cards lie this far above the ground, each a hair above the one before so overlaps don't flicker.</summary>
    private const float Lift = 0.006f, Layer = 0.0002f;

    private static readonly Dictionary<uint, (ImageTexture Albedo, ImageTexture Surface)> Atlases = new();

    public int CardCount { get; private set; }

    public void Build(LevelLayout level, ICollisionWorld world, GroundDetailDef def)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        uint seed = (uint)LevelBuilder.StableHash(level.Id) ^ 0x57A1Bu;
        var placer = new Placer(level, new GroundSurvey(level, world), new Random((int)(seed & 0x7fffffff)));
        var cards = new List<Card>();
        foreach (GroundDetailKindDef kind in def.Kinds)
        {
            placer.Place(kind, cards);
        }

        CardCount = cards.Count;
        if (cards.Count == 0)
        {
            return;
        }

        // Painted once per level and kept, so a retry doesn't paint it again.
        if (!Atlases.TryGetValue(seed, out (ImageTexture Albedo, ImageTexture Surface) atlas))
        {
            atlas = new StainPainter(seed).Paint();
            Atlases[seed] = atlas;
        }

        (ImageTexture albedo, ImageTexture surface) = atlas;
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ground_detail.gdshader") };
        material.SetShaderParameter("atlas", albedo);
        material.SetShaderParameter("surface", surface);
        material.SetShaderParameter("columns", (float)StainPainter.Columns);
        material.SetShaderParameter("fade_start", def.FadeStart_m);
        material.SetShaderParameter("fade_end", def.FadeEnd_m);

        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = CardMesh(),
        };
        multimesh.InstanceCount = cards.Count;
        for (int i = 0; i < cards.Count; i++)
        {
            Card c = cards[i];
            var basis = new Basis(Vector3.Up, c.Yaw) * Basis.FromScale(new Vector3(c.Width, 1f, c.Length));
            multimesh.SetInstanceTransform(i, new Transform3D(basis, c.Position + Vector3.Up * (Lift + (i % 16) * Layer)));
            multimesh.SetInstanceColor(i, new Color(c.Shade, c.Shade, c.Shade));
            multimesh.SetInstanceCustomData(i, new Color(c.Cell, c.Opacity, 0f, 0f));
        }

        AddChild(new MultiMeshInstance3D
        {
            Name = "GroundCards",
            Multimesh = multimesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    private readonly record struct Card(Vector3 Position, float Yaw, float Width, float Length, int Cell, float Opacity, float Shade);

    /// <summary>A flat unit square lying on the origin, facing up, UV across it.</summary>
    private static ArrayMesh CardMesh()
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 a = new(-0.5f, 0f, -0.5f), b = new(0.5f, 0f, -0.5f), c = new(0.5f, 0f, 0.5f), d = new(-0.5f, 0f, 0.5f);
        // Clockwise seen from above (Godot's front faces).
        foreach ((Vector3 p, Vector2 uv) in new[] { (a, Vector2.Zero), (b, Vector2.Right), (c, Vector2.One), (a, Vector2.Zero), (c, Vector2.One), (d, Vector2.Down) })
        {
            tool.SetNormal(Vector3.Up);
            tool.SetUV(uv);
            tool.AddVertex(p);
        }

        return tool.Commit();
    }

    /// <summary>Finds room for each kind: round the props it gathers by, along the foot of walls, or anywhere it may lie.</summary>
    private sealed class Placer
    {
        private readonly LevelLayout _level;
        private readonly GroundSurvey _survey;
        private readonly Random _random;
        private readonly List<(SVector3 Center, SVector3 Along, SVector3 Out, float Half, float Thick)> _walls = new();

        public Placer(LevelLayout level, GroundSurvey survey, Random random)
        {
            _level = level;
            _survey = survey;
            _random = random;
            foreach (LevelPrimitive p in level.Primitives)
            {
                Aabb b = p.Bounds;
                if (p.Role != PrimitiveRole.Wall || p.Kind != PrimitiveKind.Box || b.Min.Y > GroundSurvey.GroundTop || b.Max.Y - b.Min.Y < 0.5f)
                {
                    continue;
                }

                SVector3 x = SVector3.Transform(SVector3.UnitX, p.Rotation), z = SVector3.Transform(SVector3.UnitZ, p.Rotation);
                bool longX = p.HalfExtents.X >= p.HalfExtents.Z;
                _walls.Add((p.Center, longX ? x : z, longX ? z : x, longX ? p.HalfExtents.X : p.HalfExtents.Z, longX ? p.HalfExtents.Z : p.HalfExtents.X));
            }
        }

        public void Place(GroundDetailKindDef def, List<Card> cards)
        {
            GroundDetailKind kind = Enum.Parse<GroundDetailKind>(def.Kind, true);
            var on = new HashSet<string>(def.On, StringComparer.Ordinal);
            List<PropInstance> near = def.Near is { Length: > 0 }
                ? _level.Props.Where(p => def.Near.Contains(p.Type.Id)).ToList()
                : new List<PropInstance>();
            int placed = 0;
            for (int attempt = 0; attempt < def.Count * 12 && placed < def.Count; attempt++)
            {
                float length = Mathf.Lerp(def.Size_m[0], def.Size_m[1], MathF.Pow(R(0f, 1f), 1.5f));
                float width = length * def.Aspect;
                float roll = R(0f, 1f);
                (float x, float z) = roll < def.NearShare && near.Count > 0 ? NearProp(near, length)
                    : roll < def.NearShare + def.EdgeShare && _walls.Count > 0 ? AlongWall(length)
                    : Anywhere();
                if (!Fits(on, def.Sky, x, z, width, length, out float y, out float yaw))
                {
                    continue;
                }

                cards.Add(new Card(new Vector3(x, y, z), yaw, width, length, StainPainter.Cell(kind, _random.Next(2)),
                    def.Opacity * R(0.75f, 1f), R(0.85f, 1.1f)));
                placed++;
            }
        }

        /// <summary>
        /// Whether a card fits at (x, z): every corner on a listed material at the same height, at ground
        /// level, under the sky (or not) as asked.
        /// </summary>
        private bool Fits(HashSet<string> on, string sky, float x, float z, float width, float length, out float y, out float yaw)
        {
            yaw = R(0f, Mathf.Tau);
            if (!on.Contains(_survey.SurfaceAt(x, z, out y)) || y >= GroundSurvey.GroundTop)
            {
                return false;
            }

            float c = MathF.Cos(yaw), s = MathF.Sin(yaw);
            foreach ((float u, float w) in new[] { (-0.45f, -0.45f), (0.45f, -0.45f), (0.45f, 0.45f), (-0.45f, 0.45f) })
            {
                float px = x + c * u * width + s * w * length, pz = z - s * u * width + c * w * length;
                if (!on.Contains(_survey.SurfaceAt(px, pz, out float cy)) || MathF.Abs(cy - y) > 0.01f)
                {
                    return false;
                }
            }

            if (sky != "any")
            {
                bool open = _survey.OpenGround(x, z, 0.06f, out _);
                if (open != (sky == "open"))
                {
                    return false;
                }
            }

            return true;
        }

        private (float X, float Z) NearProp(List<PropInstance> props, float length)
        {
            PropInstance p = props[_random.Next(props.Count)];
            float reach = 0.3f;
            for (int i = p.FirstPrimitive; i < p.FirstPrimitive + p.PrimitiveCount; i++)
            {
                Aabb b = _level.Primitives[i].Bounds;
                reach = MathF.Max(reach, MathF.Max(MathF.Max(b.Max.X - p.Position.X, p.Position.X - b.Min.X), MathF.Max(b.Max.Z - p.Position.Z, p.Position.Z - b.Min.Z)));
            }

            // Round its edge, partly underneath, as if it ran out from under it.
            float a = R(0f, Mathf.Tau), r = reach * R(0.75f, 1.15f) + length * 0.2f;
            return (p.Position.X + MathF.Cos(a) * r, p.Position.Z + MathF.Sin(a) * r);
        }

        private (float X, float Z) AlongWall(float length)
        {
            (SVector3 center, SVector3 along, SVector3 outward, float half, float thick) = _walls[_random.Next(_walls.Count)];
            float side = _random.Next(2) == 0 ? -1f : 1f;
            SVector3 p = center + along * (R(-1f, 1f) * half) + outward * (side * (thick + length * 0.5f + R(0f, 0.25f)));
            return (p.X, p.Z);
        }

        private (float X, float Z) Anywhere()
        {
            Aabb b = _level.Bounds;
            return (R(b.Min.X, b.Max.X), R(b.Min.Z, b.Max.Z));
        }

        private float R(float a, float b) => a + (float)_random.NextDouble() * (b - a);
    }
}
