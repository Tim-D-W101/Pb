using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Level;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.World;

/// <summary>
/// Things left hanging on the inside walls of the buildings with finished interiors (a template's
/// <c>"skirting"</c>: the offices and the guardhouse; presentation.jsonc "wallHangings"): noticeboards,
/// whiteboards, a stopped clock, wall planners, exit and hazard signs, a picture, a site plan, from an
/// atlas painted at load (<see cref="HangingPainter"/>). At most one to a length of wall, on full-height
/// pieces facing a room, clear of their ends; some hang crooked. Flat on the wall and looks only: paint
/// lands on them as on the wall. Seeded by the level, one MultiMesh.
/// </summary>
public partial class WallHangings : Node3D
{
    private static ImageTexture? _atlas;

    public int Count { get; private set; }

    public void Build(LevelLayout level, WallHangingsDef def)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        var finished = new HashSet<int>();
        foreach (PlacedBuilding b in level.Buildings)
        {
            if (b.Template.Def.Skirting is not null)
            {
                finished.Add(b.Owner);
            }
        }

        var floors = new List<LevelPrimitive>();
        foreach (LevelPrimitive p in level.Primitives)
        {
            if (p.Role == PrimitiveRole.Floor && p.Kind == PrimitiveKind.Box)
            {
                floors.Add(p);
            }
        }

        var kinds = new List<(HangingKind Kind, float Weight)>();
        float total = 0f;
        foreach ((string name, float weight) in def.Kinds)
        {
            if (Enum.TryParse(name, true, out HangingKind kind) && weight > 0f)
            {
                kinds.Add((kind, weight));
                total += weight;
            }
        }

        var random = new Random(LevelBuilder.StableHash(level.Id) ^ 0x4A9);
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
        var cards = new List<(Transform3D Transform, Color Rect, float Shade)>();
        foreach (LevelPrimitive p in level.Primitives)
        {
            if (p.Role != PrimitiveRole.Wall || p.Kind != PrimitiveKind.Box || !finished.Contains(p.Owner) || p.Height < def.MinWallHeight_m || total <= 0f)
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
                if (random.NextDouble() >= def.Chance)
                {
                    continue;
                }

                Vector3 n = normal * side;
                Vector3 face = p.Center.ToGodot() + n * thick;
                if (!Indoor(level, new Vector3(face.X, foot + 1.5f, face.Z) + n * 0.4f) || !OnFloor(floors, face + n * 0.2f, foot))
                {
                    continue;
                }

                // A kind by weight that fits this length of wall with room either side.
                HangingKind kind = kinds[0].Kind;
                float roll = R(0f, total);
                foreach ((HangingKind k, float w) in kinds)
                {
                    kind = k;
                    roll -= w;
                    if (roll <= 0f)
                    {
                        break;
                    }
                }

                Vector2 size = HangingPainter.Size(kind);
                float room = half - size.X * 0.5f - def.EndClearance_m;
                if (room < 0f)
                {
                    continue;
                }

                float centre = kind is HangingKind.Clock or HangingKind.ExitSign ? def.HighCentre_m : def.EyeCentre_m + R(-0.08f, 0.08f);
                if (centre + size.Y * 0.5f > p.Height - 0.1f)
                {
                    continue;
                }

                Vector3 right = Vector3.Up.Cross(n), up = Vector3.Up;
                // Some hang crooked.
                float tilt = random.NextDouble() < def.Crooked ? R(-0.12f, 0.12f) : R(-0.015f, 0.015f);
                var basis = new Basis(n, tilt) * new Basis(right * size.X, up * size.Y, n);
                Vector3 at = face + along * R(-room, room) + Vector3.Up * (foot + centre - face.Y) + n * def.StandOff_m;
                cards.Add((new Transform3D(basis, at), HangingPainter.Rect(kind), R(0.8f, 1f)));
            }
        }

        Count = cards.Count;
        if (cards.Count == 0)
        {
            return;
        }

        _atlas ??= new HangingPainter(0x4A9).Paint();
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/wall_hangings.gdshader") };
        material.SetShaderParameter("atlas", _atlas);
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
            multimesh.SetInstanceColor(i, new Color(cards[i].Shade, cards[i].Shade, cards[i].Shade));
            multimesh.SetInstanceCustomData(i, cards[i].Rect);
        }

        AddChild(new MultiMeshInstance3D { Name = "Hangings", Multimesh = multimesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
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
