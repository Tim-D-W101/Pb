using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Collision;
using Pb.Sim.Level;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.World;

/// <summary>
/// Ivy and dead vines climbing the outside walls (presentation.jsonc "creepers"): cards from a painted
/// atlas (<see cref="CreeperPainter"/>) standing on the ground against the faces of ground-level
/// walls of the listed materials, under the open sky, each checked to have wall behind it all the way
/// up and across (so none climbs over a window or round a corner). One MultiMesh, seeded by the level
/// id. Presentation only.
/// </summary>
public partial class Creepers : Node3D
{
    /// <summary>Cards sit this far off the wall.</summary>
    private const float Lift = 0.02f;

    private static readonly Dictionary<int, ImageTexture> Atlases = new();

    public int PatchCount { get; private set; }

    public void Build(LevelLayout level, CollisionWorld world, CreepersDef def)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        int seed = LevelBuilder.StableHash(level.Id) ^ 0xC12EE9;
        var random = new Random(seed);
        var on = new HashSet<string>(def.On, StringComparer.Ordinal);
        var survey = new GroundSurvey(level, world);

        // The faces they can climb: both big faces of each ground-level wall box of a listed material.
        var faces = new List<(Vector3 Center, Vector3 Normal, Vector3 Along, float Half, float Height)>();
        foreach (LevelPrimitive p in level.Primitives)
        {
            Pb.Sim.Collision.Aabb b = p.Bounds;
            if (p.Role != PrimitiveRole.Wall || p.Kind != PrimitiveKind.Box || b.Min.Y > GroundSurvey.GroundTop || p.Height < def.Height_m[0] + 0.2f ||
                !on.Contains(level.Materials[p.Material].Id))
            {
                continue;
            }

            Vector3 x = SVector3.Transform(SVector3.UnitX, p.Rotation).ToGodot(), z = SVector3.Transform(SVector3.UnitZ, p.Rotation).ToGodot();
            bool longX = p.HalfExtents.X >= p.HalfExtents.Z;
            Vector3 along = longX ? x : z, normal = longX ? z : x;
            float half = longX ? p.HalfExtents.X : p.HalfExtents.Z, thick = longX ? p.HalfExtents.Z : p.HalfExtents.X;
            if (half < def.Width_m[0] * 0.5f)
            {
                continue;
            }

            Vector3 center = p.Center.ToGodot();
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 face = center + normal * (side * thick);
                faces.Add((new Vector3(face.X, b.Min.Y, face.Z), normal * side, along, half, p.Height));
            }
        }

        var cards = new List<(Transform3D Transform, int Variant, Color Tint)>();
        for (int attempt = 0; attempt < def.Count * 10 && cards.Count < def.Count && faces.Count > 0; attempt++)
        {
            (Vector3 center, Vector3 normal, Vector3 along, float half, float height) = faces[random.Next(faces.Count)];
            float width = Mathf.Lerp(def.Width_m[0], def.Width_m[1], (float)random.NextDouble());
            float tall = MathF.Min(Mathf.Lerp(def.Height_m[0], def.Height_m[1], MathF.Pow((float)random.NextDouble(), 0.8f)), height - 0.15f);
            if (half < width * 0.5f)
            {
                continue;
            }

            float t = Mathf.Lerp(-half + width * 0.5f, half - width * 0.5f, (float)random.NextDouble());
            Vector3 foot = center + along * t;
            // Outdoors, on ground at the wall's foot.
            Vector3 front = foot + normal * 0.5f;
            if (!survey.OpenGround(front.X, front.Z, 0.15f, out float ground) || MathF.Abs(ground - foot.Y) > 0.12f)
            {
                continue;
            }

            foot.Y = ground;
            if (!Backed(world, foot, normal, along, width, tall))
            {
                continue;
            }

            // Right-handed: X along the wall as seen from in front, Z out of it.
            Vector3 right = Vector3.Up.Cross(normal).Normalized();
            var basis = new Basis(right * width, Vector3.Up * tall, normal);
            Vector3 position = foot + normal * (Lift + cards.Count % 4 * 0.004f) + Vector3.Up * (tall * 0.5f - 0.03f);
            float shade = 0.85f + 0.3f * (float)random.NextDouble();
            cards.Add((new Transform3D(basis, position), random.Next(CreeperPainter.Variants), new Color(shade, shade, shade)));
        }

        PatchCount = cards.Count;
        if (cards.Count == 0)
        {
            return;
        }

        if (!Atlases.TryGetValue(seed, out ImageTexture? atlas))
        {
            atlas = new CreeperPainter(seed).Paint();
            Atlases[seed] = atlas;
        }

        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/creepers.gdshader") };
        material.SetShaderParameter("atlas", atlas);
        material.SetShaderParameter("variants", (float)CreeperPainter.Variants);
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
            multimesh.SetInstanceColor(i, cards[i].Tint);
            multimesh.SetInstanceCustomData(i, new Color(cards[i].Variant, 0f, 0f, 0f));
        }

        AddChild(new MultiMeshInstance3D { Name = "CreeperCards", Multimesh = multimesh, MaterialOverride = material });
    }

    /// <summary>Whether there's wall behind the card all the way: at its top corners, its middle and halfway up each side.</summary>
    private static bool Backed(CollisionWorld world, Vector3 foot, Vector3 normal, Vector3 along, float width, float tall)
    {
        foreach ((float x, float y) in new[] { (-0.45f, 0.95f), (0.45f, 0.95f), (0f, 0.5f), (-0.45f, 0.45f), (0.45f, 0.45f), (0f, 0.1f) })
        {
            Vector3 at = foot + along * (x * width) + Vector3.Up * (y * tall);
            if (!world.SweepSphere((at + normal * 0.25f).ToSim(), (at - normal * 0.1f).ToSim(), 0f, out SweepHit hit) ||
                hit.Normal.ToGodot().Dot(normal) < 0.9f || MathF.Abs((hit.Point.ToGodot() - at).Dot(normal)) > 0.03f)
            {
                return false;
            }
        }

        return true;
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
