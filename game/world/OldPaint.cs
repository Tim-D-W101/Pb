using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Level;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.World;

/// <summary>
/// Old paint from games played here before (presentation.jsonc "oldPaint"): faded splats where people
/// shooting from one cover spot at another would have hit. Each shot is cast from a cover point (round
/// its edge or over the top, at head height) at another one facing it across their cover, straying
/// round where its owner would peek; where it lands on a wall, a column, a listed prop or the ground,
/// beyond the shooter's own cover, a card is laid flat on the surface, with drips on a wall. The atlas
/// is painted at load (<see cref="SplatPainter"/>) and the cards drawn in one MultiMesh, seeded by the
/// level id so they're the same every run. Presentation only.
/// </summary>
public partial class OldPaint : Node3D
{
    /// <summary>Cards sit this far off the surface, each a hair further than the one before so overlaps don't flicker.</summary>
    private const float Lift = 0.004f, Layer = 0.0003f;

    /// <summary>A shot lands at least this far from where it was fired (closer is the shooter's own cover).</summary>
    private const float MinDistance = 3f;

    private static readonly Color Weathered = new(0.56f, 0.54f, 0.5f);
    private static readonly Dictionary<uint, ImageTexture> Atlases = new();

    public int SplatCount { get; private set; }

    public void Build(LevelLayout level, CollisionWorld world, IReadOnlyList<CoverPoint> cover, OldPaintDef def)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        uint seed = (uint)LevelBuilder.StableHash(level.Id) ^ 0x01D7A1u;
        var random = new Random((int)(seed & 0x7fffffff));
        var colors = new List<Color>();
        foreach (string c in def.Colors)
        {
            colors.Add(Color.FromHtml(c));
        }

        var cards = new List<Card>();
        int shots = 0;
        while (cards.Count < def.Count && shots < def.Count * 8 && cover.Count > 1)
        {
            shots++;
            if (Shot(world, cover, def, random) is not { } hit)
            {
                continue;
            }

            float size = Mathf.Lerp(def.Size_m[0], def.Size_m[1], MathF.Pow((float)random.NextDouble(), 2f));
            bool wall = MathF.Abs(hit.Normal.Y) < 0.7f;
            (Vector3 right, Vector3 up) = Frame(hit.Normal, wall, random);
            if (!Fits(world, hit, right, up, size))
            {
                size *= 0.55f;
                if (size < def.Size_m[0] * 0.5f || !Fits(world, hit, right, up, size))
                {
                    continue;
                }
            }

            Color paint = colors[random.Next(colors.Count)].Lerp(Weathered, Mathf.Lerp(def.Fade[0], def.Fade[1], (float)random.NextDouble()));
            paint = (paint * (0.85f + 0.2f * (float)random.NextDouble())).SrgbToLinear();
            Vector3 at = hit.Point + hit.Normal * (Lift - hit.Inset + (cards.Count % 8) * Layer);
            cards.Add(new Card(at, right * size, up * size, hit.Normal, SplatPainter.Cell(random.Next(SplatPainter.Variants), wall), paint,
                Mathf.Lerp(def.Opacity[0], def.Opacity[1], (float)random.NextDouble())));
        }

        SplatCount = cards.Count;
        if (cards.Count == 0)
        {
            return;
        }

        if (!Atlases.TryGetValue(seed, out ImageTexture? atlas))
        {
            atlas = new SplatPainter(seed).Paint();
            Atlases[seed] = atlas;
        }

        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/old_paint.gdshader") };
        material.SetShaderParameter("atlas", atlas);
        material.SetShaderParameter("columns", (float)SplatPainter.Columns);
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
            multimesh.SetInstanceTransform(i, new Transform3D(new Basis(c.Right, c.Up, c.Normal), c.Position));
            multimesh.SetInstanceColor(i, c.Color);
            multimesh.SetInstanceCustomData(i, new Color(c.Cell, c.Opacity, 0f, 0f));
        }

        AddChild(new MultiMeshInstance3D
        {
            Name = "PaintCards",
            Multimesh = multimesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <summary>
    /// One shot from a cover point at another facing it across their cover: where it lands, if that's
    /// somewhere paint shows (a wall, a column, the ground or a listed prop) far enough from the shooter.
    /// </summary>
    private static Hit? Shot(CollisionWorld world, IReadOnlyList<CoverPoint> cover, OldPaintDef def, Random random)
    {
        CoverPoint from = cover[random.Next(cover.Count)];
        for (int tries = 0; tries < 24; tries++)
        {
            CoverPoint to = cover[random.Next(cover.Count)];
            SVector3 d = to.Position - from.Position;
            float distance = d.Length();
            if (distance < MinDistance * 2f || distance > def.Range_m)
            {
                continue;
            }

            d /= distance;
            // Each faces the other across its cover: the threat side of a spot is away from its normal.
            if (SVector3.Dot(d, from.Normal) > -0.5f || SVector3.Dot(d, to.Normal) < 0.5f)
            {
                continue;
            }

            // Standing or crouched, at whatever of the other shows: a head round an edge, a body over a wall, legs.
            SVector3 eye = Peek(from, 0.55f) + new SVector3(0f, R(random, 1.0f, 1.55f), 0f);
            SVector3 aim = Peek(to, 0.45f) + new SVector3(0f, R(random, 0.35f, to.Height == CoverHeight.Full ? 1.6f : 1.35f), 0f);
            aim += new SVector3(Gauss(random) * def.Spread_m, Gauss(random) * def.Spread_m * 0.6f, Gauss(random) * def.Spread_m);
            SVector3 ray = aim - eye;
            float length = ray.Length();
            SVector3 end = eye + ray / length * (length + 3f);
            if (!world.SweepSphere(eye, end, 0f, out SweepHit hit) || hit.T * (length + 3f) < MinDistance)
            {
                return null;
            }

            float inset = 0f;
            string name = world.Colliders[hit.ColliderId].Name;
            int prop = name.IndexOf("prop:", StringComparison.Ordinal);
            if (prop >= 0)
            {
                int hash = name.IndexOf('#', prop);
                string type = hash > prop ? name[(prop + 5)..hash] : name[(prop + 5)..];
                if (!def.Props.TryGetValue(type, out inset))
                {
                    return null;
                }
            }

            return new Hit(hit.Point.ToGodot(), hit.Normal.ToGodot().Normalized(), hit.ColliderId, inset);
        }

        return null;
    }

    /// <summary>Where a cover point's owner shows themselves: round its edge if it has one, else where they stand.</summary>
    private static SVector3 Peek(CoverPoint p, float reach) => p.Position + (p.HasEdge ? p.PeekDirection * reach : SVector3.Zero);

    /// <summary>The card's right and up on the surface: up the wall (drips run down it), or any way round on the flat.</summary>
    private static (Vector3 Right, Vector3 Up) Frame(Vector3 normal, bool wall, Random random)
    {
        Vector3 up = wall ? (Vector3.Up - normal * normal.Dot(Vector3.Up)).Normalized() : normal.Cross(Vector3.Right).LengthSquared() > 0.01f
            ? normal.Cross(Vector3.Right).Normalized() : normal.Cross(Vector3.Forward).Normalized();
        float roll = wall ? ((float)random.NextDouble() - 0.5f) * 0.5f : (float)random.NextDouble() * Mathf.Tau;
        up = up.Rotated(normal, roll);
        return (up.Cross(normal).Normalized(), up);
    }

    /// <summary>Whether a card this size lies flat on what the shot hit: all four corners on the same plane of the same thing.</summary>
    private static bool Fits(CollisionWorld world, Hit hit, Vector3 right, Vector3 up, float size)
    {
        foreach ((float x, float y) in new[] { (-0.42f, -0.42f), (0.42f, -0.42f), (0.42f, 0.42f), (-0.42f, 0.42f) })
        {
            Vector3 corner = hit.Point + (right * x + up * y) * size;
            if (!world.SweepSphere((corner + hit.Normal * 0.08f).ToSim(), (corner - hit.Normal * 0.08f).ToSim(), 0f, out SweepHit h) ||
                h.ColliderId != hit.Collider && hit.Inset > 0f ||
                h.Normal.ToGodot().Dot(hit.Normal) < 0.95f ||
                MathF.Abs((h.Point.ToGodot() - hit.Point).Dot(hit.Normal)) > 0.015f)
            {
                return false;
            }
        }

        return true;
    }

    private static float R(Random random, float a, float b) => a + (float)random.NextDouble() * (b - a);

    /// <summary>About normally distributed, mean 0 and spread 1 (the sum of three uniforms).</summary>
    private static float Gauss(Random random) =>
        ((float)random.NextDouble() + (float)random.NextDouble() + (float)random.NextDouble() - 1.5f) * 2f;

    /// <summary>A unit square on the origin in the XY plane, facing +Z, UV across it (v down).</summary>
    private static ArrayMesh CardMesh()
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 a = new(-0.5f, 0.5f, 0f), b = new(0.5f, 0.5f, 0f), c = new(0.5f, -0.5f, 0f), d = new(-0.5f, -0.5f, 0f);
        // Clockwise seen from the front (Godot's front faces).
        foreach ((Vector3 p, Vector2 uv) in new[] { (a, Vector2.Zero), (b, Vector2.Right), (c, Vector2.One), (a, Vector2.Zero), (c, Vector2.One), (d, Vector2.Down) })
        {
            tool.SetNormal(Vector3.Back);
            tool.SetUV(uv);
            tool.AddVertex(p);
        }

        return tool.Commit();
    }

    private readonly record struct Hit(Vector3 Point, Vector3 Normal, int Collider, float Inset);

    private readonly record struct Card(Vector3 Position, Vector3 Right, Vector3 Up, Vector3 Normal, int Cell, Color Color, float Opacity);
}
