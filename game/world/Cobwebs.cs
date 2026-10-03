using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Level;

namespace Pb.Game.World;

/// <summary>
/// Cobwebs in the corners of the buildings' doorways (the top ones) and windows (any), from the level's
/// apertures (presentation.jsonc "cobwebs"): cards in the opening's plane, each a web painted at load
/// (<see cref="Paint"/>: threads fanning out of the corner, a spiral strung between them, a few broken
/// strands hanging), anchored in its corner. Openings in free-standing walls (the perimeter's breaks)
/// get none. One MultiMesh; presentation only (paint and players pass through).
/// </summary>
public partial class Cobwebs : Node3D
{
    public const int Variants = 4;

    private const int CellPx = 256;

    /// <summary>Webs start this far in from the opening's edge, inside its frame (m).</summary>
    private const float FrameInset = 0.07f;

    private static ImageTexture? _atlas;

    public int Count { get; private set; }

    public void Build(LevelLayout level, CobwebsDef def)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        var random = new Random(LevelBuilder.StableHash(level.Id) ^ 0xC0B3B);
        var cards = new List<(Transform3D Transform, int Variant, float Opacity)>();
        foreach (Aperture a in level.Apertures)
        {
            if (a.Kind is not (ApertureKind.Door or ApertureKind.Window) || level.Owners[a.Owner].StartsWith("wall", StringComparison.Ordinal))
            {
                continue;
            }

            Vector3 u = a.U.ToGodot(), v = a.V.ToGodot(), n = a.Normal.ToGodot();
            // Corners as (across, up): doors only at the top, windows anywhere.
            foreach ((float cu, float cv) in new[] { (-1f, 1f), (1f, 1f), (-1f, -1f), (1f, -1f) })
            {
                if (a.Kind == ApertureKind.Door && cv < 0f || random.NextDouble() >= def.Chance)
                {
                    continue;
                }

                float size = MathF.Min(Mathf.Lerp(def.Size_m[0], def.Size_m[1], (float)random.NextDouble()), MathF.Min(a.HalfWidth, a.HalfHeight) * 1.4f);
                // Inside the frame drawn round the opening.
                Vector3 corner = a.At(cu, cv).ToGodot() - u * (cu * FrameInset) - v * (cv * FrameInset);
                // The web's anchor is the card's top-left: point its right and up into the opening.
                Vector3 right = u * -cu * size, up = v * cv * size;
                Vector3 depth = n * (((float)random.NextDouble() - 0.5f) * 0.12f);
                Vector3 centre = corner + (right - up) * 0.5f + depth;
                cards.Add((new Transform3D(new Basis(right, up, right.Cross(up).Normalized()), centre), random.Next(Variants),
                    Mathf.Lerp(def.Opacity[0], def.Opacity[1], (float)random.NextDouble())));
            }
        }

        Count = cards.Count;
        if (cards.Count == 0)
        {
            return;
        }

        _atlas ??= Paint(new Random(0xC0B3B));
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/cobwebs.gdshader") };
        material.SetShaderParameter("atlas", _atlas);
        material.SetShaderParameter("variants", (float)Variants);
        material.SetShaderParameter("color", Color.FromHtml(def.Color));
        material.SetShaderParameter("fade_start", def.FadeStart_m);
        material.SetShaderParameter("fade_end", def.FadeEnd_m);
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = CardMesh(),
        };
        multimesh.InstanceCount = cards.Count;
        for (int i = 0; i < cards.Count; i++)
        {
            multimesh.SetInstanceTransform(i, cards[i].Transform);
            multimesh.SetInstanceCustomData(i, new Color(cards[i].Variant, cards[i].Opacity, 0f, 0f));
        }

        AddChild(new MultiMeshInstance3D
        {
            Name = "CobwebCards",
            Multimesh = multimesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <summary>
    /// Four webs side by side, each anchored at its cell's top-left corner: radial threads fanning across the
    /// quarter, a spiral strung between them from near the corner outwards, sagging a little between the
    /// spokes, some spans broken and a strand or two hanging loose. Luminance and alpha.
    /// </summary>
    private static ImageTexture Paint(Random random)
    {
        int width = CellPx * Variants;
        var cover = new float[width * CellPx];
        void Line(Vector2 a, Vector2 b, float strength, float thickness)
        {
            float length = a.DistanceTo(b);
            int steps = Math.Max(1, (int)(length * 1.5f));
            for (int s = 0; s <= steps; s++)
            {
                Vector2 p = a.Lerp(b, (float)s / steps);
                int x0 = (int)MathF.Floor(p.X - thickness), x1 = (int)MathF.Ceiling(p.X + thickness);
                int y0 = (int)MathF.Floor(p.Y - thickness), y1 = (int)MathF.Ceiling(p.Y + thickness);
                for (int y = Math.Max(0, y0); y <= Math.Min(CellPx - 1, y1); y++)
                {
                    for (int x = Math.Max(0, x0); x <= Math.Min(width - 1, x1); x++)
                    {
                        float d = p.DistanceTo(new Vector2(x + 0.5f, y + 0.5f));
                        float c = Math.Clamp(thickness + 0.5f - d, 0f, 1f) * strength;
                        int i = y * width + x;
                        cover[i] = MathF.Max(cover[i], c);
                    }
                }
            }
        }

        for (int v = 0; v < Variants; v++)
        {
            var anchor = new Vector2(v * CellPx + 2f, 2f);
            int spokes = random.Next(6, 10);
            var angles = new float[spokes];
            var reach = new float[spokes];
            for (int s = 0; s < spokes; s++)
            {
                angles[s] = Mathf.Lerp(0.06f, Mathf.Pi / 2f - 0.06f, (s + (float)random.NextDouble() * 0.6f) / spokes);
                reach[s] = CellPx * (0.72f + 0.26f * (float)random.NextDouble());
            }

            Vector2 Spoke(int s, float t) => anchor + new Vector2(MathF.Cos(angles[s]), MathF.Sin(angles[s])) * reach[s] * t;
            for (int s = 0; s < spokes; s++)
            {
                Line(anchor, Spoke(s, 1f), 0.9f, 1.3f);
            }

            // The spiral: rings of spans between neighbouring spokes, sagging towards the corner.
            for (float t = 0.12f; t < 0.95f; t += 0.07f + 0.03f * (float)random.NextDouble())
            {
                for (int s = 0; s + 1 < spokes; s++)
                {
                    if (random.NextDouble() < 0.12)
                    {
                        continue; // broken
                    }

                    Vector2 a = Spoke(s, t), b = Spoke(s + 1, t + 0.015f);
                    Vector2 mid = (a + b) * 0.5f;
                    Vector2 sag = (anchor - mid).Normalized() * a.DistanceTo(b) * 0.08f;
                    Line(a, mid + sag, 0.75f, 1.0f);
                    Line(mid + sag, b, 0.75f, 1.0f);
                }
            }

            // A strand or two hanging loose from the outer edge.
            for (int k = random.Next(1, 3); k > 0; k--)
            {
                int s = random.Next(spokes);
                Vector2 from = Spoke(s, 0.9f);
                Line(from, from + new Vector2(((float)random.NextDouble() - 0.5f) * 20f, 25f + (float)random.NextDouble() * 50f), 0.6f, 0.9f);
            }
        }

        var bytes = new byte[width * CellPx * 2];
        for (int i = 0; i < width * CellPx; i++)
        {
            bytes[i * 2] = 255;
            bytes[i * 2 + 1] = (byte)(Math.Clamp(cover[i], 0f, 1f) * 255f);
        }

        Image image = Image.CreateFromData(width, CellPx, false, Image.Format.La8, bytes);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>A unit card on the origin in the XY plane, UV across it (v down).</summary>
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
