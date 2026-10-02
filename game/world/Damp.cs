using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Collision;
using Pb.Sim.Level;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.World;

/// <summary>
/// Damp indoors (presentation.jsonc "damp"): tide marks of rising damp along the foot of the walls that
/// stand on the ground, black mould spreading from the top corners of the rooms' walls, and brown water
/// stains on the ceilings, painted at load (<see cref="DampPainter"/>). Each lies flat on a wall face
/// that looks into an indoor area and stands on a floor, or on a ceiling with room for all of it, seeded
/// by the level. Two MultiMeshes under the old paint; looks only (paint lands on the wall beneath).
/// </summary>
public partial class Damp : Node3D
{
    /// <summary>Cards lie this far off the wall or ceiling.</summary>
    private const float Lift = 0.003f;

    private static ImageTexture? _band, _atlas;

    public int Count { get; private set; }

    private readonly record struct Card(Transform3D Transform, Color Data, Color Shade);

    public void Build(LevelLayout level, ICollisionWorld world, DampDef def)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        var random = new Random(LevelBuilder.StableHash(level.Id) ^ 0x0DA4B);
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
        Color Shade()
        {
            float s = R(0.85f, 1.05f);
            return new Color(s, s, s);
        }

        var floors = new List<LevelPrimitive>();
        foreach (LevelPrimitive p in level.Primitives)
        {
            if (p.Role == PrimitiveRole.Floor && p.Kind == PrimitiveKind.Box)
            {
                floors.Add(p);
            }
        }

        var bands = new List<Card>();
        var spots = new List<Card>();
        foreach (LevelPrimitive p in level.Primitives)
        {
            if (p.Role != PrimitiveRole.Wall || p.Kind != PrimitiveKind.Box)
            {
                continue;
            }

            Vector3 x = SVector3.Transform(SVector3.UnitX, p.Rotation).ToGodot(), z = SVector3.Transform(SVector3.UnitZ, p.Rotation).ToGodot();
            if (MathF.Abs(x.Y) > 0.02f || MathF.Abs(z.Y) > 0.02f)
            {
                continue;
            }

            bool longX = p.HalfExtents.X >= p.HalfExtents.Z;
            Vector3 along = longX ? x : z, normal = longX ? z : x;
            float half = longX ? p.HalfExtents.X : p.HalfExtents.Z, thick = longX ? p.HalfExtents.Z : p.HalfExtents.X;
            float foot = p.Bounds.Min.Y;
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 n = normal * side;
                Vector3 face = p.Center.ToGodot() + n * thick;
                if (!Indoor(level, new Vector3(face.X, foot + 1.2f, face.Z) + n * 0.4f) || !OnFloor(floors, face + n * 0.2f, foot))
                {
                    continue;
                }

                Vector3 right = Vector3.Up.Cross(n);
                Vector3 bottom = new Vector3(face.X, foot, face.Z) + n * Lift;

                // Rising damp along the foot of a wall that stands on the ground.
                if (foot < def.RisingBelow_m && random.NextDouble() < def.Rising)
                {
                    float height = MathF.Min(R(def.RisingHeight_m[0], def.RisingHeight_m[1]), p.Height - 0.05f);
                    float length = 2f * half - 0.01f;
                    if (height > 0.15f && length > 0.2f)
                    {
                        var basis = new Basis(right * length, Vector3.Up * height, n);
                        bands.Add(new Card(new Transform3D(basis, bottom + Vector3.Up * (height * 0.5f)),
                            new Color(length / (2f * height), R(0f, 1f), R(def.Opacity[0], def.Opacity[1]), 0f), Shade()));
                    }
                }

                // Mould spreading from the top corners of a room-height wall.
                if (p.Height < def.MouldWalls_m[0] || p.Height > def.MouldWalls_m[1])
                {
                    continue;
                }

                foreach (float end in new[] { -1f, 1f })
                {
                    if (random.NextDouble() >= def.Mould)
                    {
                        continue;
                    }

                    float size = MathF.Min(R(def.MouldSize_m[0], def.MouldSize_m[1]), MathF.Min(half * 2f, p.Height) * 0.8f);
                    Vector3 corner = bottom + along * (end * half) + Vector3.Up * p.Height;
                    Vector3 at = corner - along * (end * size * 0.5f) - Vector3.Up * (size * 0.5f);
                    // The cells spread from their top-left corner: mirrored for a corner on the card's right.
                    bool mirror = along.Dot(right) * end > 0f;
                    var basis = new Basis(right * size, Vector3.Up * size, n);
                    spots.Add(new Card(new Transform3D(basis, at), new Color(random.Next(2), mirror ? 1f : 0f, R(def.Opacity[0], def.Opacity[1]), 0f), Shade()));
                }
            }
        }

        // Water stains on the ceilings, each where all of it is under one flat ceiling clear of the walls.
        foreach (AreaSpec area in level.Areas)
        {
            if (!area.Indoor)
            {
                continue;
            }

            Pb.Sim.Collision.Aabb box = area.Box;
            float floor = MathF.Max(box.Min.Y, 0f);
            float sizeX = box.Max.X - box.Min.X, sizeZ = box.Max.Z - box.Min.Z;
            int count = (int)(sizeX * sizeZ * def.Stains_perM2 + random.NextDouble());
            for (int placed = 0, attempt = 0; placed < count && attempt < count * 10; attempt++)
            {
                float size = R(def.StainSize_m[0], def.StainSize_m[1]);
                var centre = new Vector3(R(box.Min.X, box.Max.X), floor + 2f, R(box.Min.Z, box.Max.Z));
                if (!Ceiling(world, centre, out float y) || y - floor > def.CeilingMaxHeight_m)
                {
                    continue;
                }

                bool fits = true;
                foreach ((float dx, float dz) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                {
                    Vector3 corner = centre + new Vector3(dx, 0f, dz) * (size * 0.4f);
                    if (world.SweepSphere(centre.ToSim(), corner.ToSim(), 0.05f, out _) || !Ceiling(world, corner, out float cy) || MathF.Abs(cy - y) > 0.02f)
                    {
                        fits = false;
                        break;
                    }
                }

                if (!fits)
                {
                    continue;
                }

                float yaw = R(0f, Mathf.Tau);
                var right = new Vector3(MathF.Cos(yaw), 0f, MathF.Sin(yaw));
                Vector3 down = Vector3.Down;
                var basis = new Basis(right * size, down.Cross(right) * size, down);
                spots.Add(new Card(new Transform3D(basis, new Vector3(centre.X, y - Lift, centre.Z)),
                    new Color(2 + random.Next(2), random.Next(2), R(def.Opacity[0], def.Opacity[1]), 0f), Shade()));
                placed++;
            }
        }

        Count = bands.Count + spots.Count;
        if (Count == 0)
        {
            return;
        }

        var painter = new DampPainter(0xDA4B);
        _band ??= painter.Band();
        _atlas ??= painter.Atlas();
        Add("RisingDamp", bands, _band, band: true, def);
        Add("MouldAndStains", spots, _atlas, band: false, def);
    }

    private void Add(string name, List<Card> cards, ImageTexture texture, bool band, DampDef def)
    {
        if (cards.Count == 0)
        {
            return;
        }

        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/damp.gdshader"), RenderPriority = -1 };
        material.SetShaderParameter("tex", texture);
        material.SetShaderParameter("band", band);
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
            multimesh.SetInstanceTransform(i, cards[i].Transform);
            multimesh.SetInstanceColor(i, cards[i].Shade);
            multimesh.SetInstanceCustomData(i, cards[i].Data);
        }

        AddChild(new MultiMeshInstance3D { Name = name, Multimesh = multimesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    /// <summary>The ceiling over a point: a sweep up from it meets a surface facing down.</summary>
    private static bool Ceiling(ICollisionWorld world, Vector3 from, out float y)
    {
        y = 0f;
        SVector3 start = from.ToSim();
        if (!world.SweepSphere(start, start + new SVector3(0f, 8f, 0f), 0.05f, out SweepHit hit) || hit.Normal.Y > -0.9f)
        {
            return false;
        }

        y = hit.Point.Y + 0.05f;
        return true;
    }

    private static bool Indoor(LevelLayout level, Vector3 at)
    {
        foreach (AreaSpec area in level.Areas)
        {
            Pb.Sim.Collision.Aabb b = area.Box;
            if (area.Indoor && at.X >= b.Min.X && at.X <= b.Max.X && at.Y >= b.Min.Y && at.Y <= b.Max.Y && at.Z >= b.Min.Z && at.Z <= b.Max.Z)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a floor's top lies within a few centimetres of <paramref name="foot"/> under this point.</summary>
    private static bool OnFloor(List<LevelPrimitive> floors, Vector3 at, float foot)
    {
        foreach (LevelPrimitive f in floors)
        {
            Pb.Sim.Collision.Aabb b = f.Bounds;
            if (at.X >= b.Min.X && at.X <= b.Max.X && at.Z >= b.Min.Z && at.Z <= b.Max.Z && MathF.Abs(b.Max.Y - foot) < 0.08f)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A unit card on the origin in the XY plane, facing +Z, UV across it (v down).</summary>
    private static ArrayMesh CardMesh()
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 a = new(-0.5f, 0.5f, 0f), b = new(0.5f, 0.5f, 0f), c = new(0.5f, -0.5f, 0f), d = new(-0.5f, -0.5f, 0f);
        foreach ((Vector3 p, Vector2 uv) in new[] { (a, Vector2.Zero), (b, Vector2.Right), (c, Vector2.One), (a, Vector2.Zero), (c, Vector2.One), (d, Vector2.Down) })
        {
            tool.SetNormal(Vector3.Back);
            tool.SetUV(uv);
            tool.AddVertex(p);
        }

        return tool.Commit();
    }
}
