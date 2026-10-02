using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Collision;
using Pb.Sim.Level;
using Aabb = Pb.Sim.Collision.Aabb;

namespace Pb.Game.World;

/// <summary>
/// Where rain runs down an outside face from something on it: the point on the face it leaves from and
/// the face's outward normal, the width of what it runs off, the kit material it runs off (which decides
/// how rusty the stain is) and, for a line it runs off along (a gutter, a coping), the line's length
/// along the face, centred on <see cref="At"/>.
/// </summary>
public readonly record struct Drip(Vector3 At, Vector3 Normal, float Width, string Material, float Run = 0f);

/// <summary>
/// Run-off streaks down the outside walls (presentation.jsonc "runOff"): grime running down from under
/// every window's sill, and from the sources the builders report (<see cref="Drip"/>): rust under the
/// wall fittings, the downpipes' collars and the wire's brackets, and stains scattered under the gutters
/// and copings. Each is a card on the face, cut short above an opening below it and at the ground, one of
/// a few streak patterns painted at load (<see cref="Paint"/>), coloured between grime and rust by what
/// it runs off. One MultiMesh, drawn before old paint and markings so they stay on top; presentation only.
/// </summary>
public partial class RunOff : Node3D
{
    public const int Variants = 4;

    private const int CellWidth = 128, CellHeight = 512;

    /// <summary>Cards sit this far off the face, each a hair further than the one before so overlaps don't flicker.</summary>
    private const float Lift = 0.0025f, Layer = 0.0002f;

    private static ImageTexture? _atlas;

    public int Count { get; private set; }

    public void Build(LevelLayout level, CollisionWorld world, IReadOnlyList<Drip> drips, RunOffDef def)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        var random = new Random(LevelBuilder.StableHash(level.Id) ^ 0xD121);
        Color grime = Color.FromHtml(def.Grime).SrgbToLinear(), rust = Color.FromHtml(def.Rust).SrgbToLinear();
        var cards = new List<(Transform3D Transform, Color Color, float Variant, float Opacity)>();
        float R(float[] range) => Mathf.Lerp(range[0], range[1], (float)random.NextDouble());

        void Streak(Vector3 top, Vector3 normal, float width, float length, string material)
        {
            if (random.NextDouble() >= def.Chance || Inside(level, top + normal * 0.6f))
            {
                return;
            }

            length = Clip(level, world, top, normal, width, length);
            if (length < 0.25f)
            {
                return;
            }

            float share = def.RustFrom.TryGetValue(material, out float s) ? s : 0f;
            Color color = grime.Lerp(rust, share * (0.75f + 0.25f * (float)random.NextDouble()));
            // Right × up is the face's normal, so the card faces out of the wall.
            Vector3 right = Vector3.Up.Cross(normal);
            Vector3 centre = top + normal * (Lift + (cards.Count % 8) * Layer) + Vector3.Down * (length * 0.5f);
            cards.Add((new Transform3D(new Basis(right * width, Vector3.Up * length, normal), centre), color, random.Next(Variants), R(def.Opacity)));
        }

        // Under the windows' sills, on the outside.
        foreach (Aperture a in level.Apertures)
        {
            if (a.Kind != ApertureKind.Window || level.Owners[a.Owner].StartsWith("wall", StringComparison.Ordinal))
            {
                continue;
            }

            string frames = FramesOf(level, a.Owner);
            Vector3 sill = a.At(0f, -1f).ToGodot(), n = a.Normal.ToGodot();
            foreach (float side in new[] { -1f, 1f })
            {
                // The wall's face just under the sill, found from outside.
                Vector3 probe = sill + Vector3.Down * 0.06f;
                if (!world.SweepSphere((probe + n * side).ToSim(), (probe - n * (side * 0.3f)).ToSim(), 0f, out SweepHit hit)
                    || hit.Normal.ToGodot().Dot(n * side) < 0.9f)
                {
                    continue;
                }

                Vector3 face = hit.Point.ToGodot() + Vector3.Up * 0.06f;
                Streak(face, n * side, 2f * a.HalfWidth * Mathf.Lerp(0.85f, 1.1f, (float)random.NextDouble()), R(def.SillLength_m), frames);
            }
        }

        foreach (Drip d in drips)
        {
            if (d.Run <= 0f)
            {
                Streak(d.At, d.Normal, d.Width * Mathf.Lerp(1.3f, 2.2f, (float)random.NextDouble()), R(def.FittingLength_m), d.Material);
                continue;
            }

            // Scattered along a line, as water finds its way over the edge.
            Vector3 along = Vector3.Up.Cross(d.Normal);
            for (float x = -d.Run * 0.5f + R(def.LineSpacing_m) * 0.5f; x < d.Run * 0.5f; x += R(def.LineSpacing_m))
            {
                Streak(d.At + along * x, d.Normal, R(def.LineWidth_m), R(def.LineLength_m), d.Material);
            }
        }

        Count = cards.Count;
        if (cards.Count == 0)
        {
            return;
        }

        _atlas ??= Paint(new Random(0xD121));
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/run_off.gdshader"), RenderPriority = -1 };
        material.SetShaderParameter("atlas", _atlas);
        material.SetShaderParameter("cells", (float)Variants);
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
            multimesh.SetInstanceColor(i, cards[i].Color);
            multimesh.SetInstanceCustomData(i, new Color(cards[i].Variant, cards[i].Opacity, 0f, 0f));
        }

        AddChild(new MultiMeshInstance3D
        {
            Name = "RunOffCards",
            Multimesh = multimesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = material,
        });
    }

    /// <summary>
    /// How far a streak from <paramref name="top"/> can run: to the ground or floor below it, and not over
    /// an opening below it in the same face (it stops a little above its head).
    /// </summary>
    private static float Clip(LevelLayout level, CollisionWorld world, Vector3 top, Vector3 normal, float width, float length)
    {
        Vector3 from = top + normal * 0.04f;
        if (world.SweepSphere(from.ToSim(), (from + Vector3.Down * (length + 0.5f)).ToSim(), 0f, out SweepHit ground))
        {
            length = MathF.Min(length, top.Y - ground.Point.Y - 0.02f);
        }

        Vector3 along = Vector3.Up.Cross(normal);
        foreach (Aperture a in level.Apertures)
        {
            if (a.Kind == ApertureKind.RoofHole)
            {
                continue;
            }

            Vector3 centre = a.Center.ToGodot();
            float head = centre.Y + a.HalfHeight;
            if (MathF.Abs(a.Normal.ToGodot().Dot(normal)) < 0.95f || MathF.Abs((centre - top).Dot(normal)) > 0.6f
                || head > top.Y + 0.01f || MathF.Abs((centre - top).Dot(along)) > a.HalfWidth + width * 0.5f)
            {
                continue;
            }

            length = MathF.Min(length, top.Y - head - 0.03f);
        }

        return length;
    }

    /// <summary>The frames' material of the building that owns this opening (empty if none).</summary>
    private static string FramesOf(LevelLayout level, int owner)
    {
        foreach (PlacedBuilding b in level.Buildings)
        {
            if (b.Owner == owner)
            {
                return b.Template.Def.Frames ?? "";
            }
        }

        return "";
    }

    /// <summary>
    /// Whether this point is indoors, or above somewhere indoors (a room's area may stop below a high
    /// window): tried just under it and down to near the floor.
    /// </summary>
    private static bool Inside(LevelLayout level, Vector3 at)
    {
        foreach (float y in new[] { at.Y - 0.3f, 1.5f, 0.5f })
        {
            if (y <= at.Y && Indoor(level, at with { Y = y }))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Indoor(LevelLayout level, Vector3 at)
    {
        foreach (AreaSpec area in level.Areas)
        {
            Aabb b = area.Box;
            if (area.Indoor && at.X >= b.Min.X && at.X <= b.Max.X && at.Y >= b.Min.Y && at.Y <= b.Max.Y && at.Z >= b.Min.Z && at.Z <= b.Max.Z)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Four streak patterns side by side, each running down its tall cell from the top: a soft stain
    /// along the top where the water leaves the edge, with a ragged lower edge; two to four broad plumes
    /// where it runs off most, soft at their sides, fading as they go and striated down their length;
    /// and thin drips wavering a little as they run, darker in their cores. Luminance (a little darker
    /// where they're thick) and alpha.
    /// </summary>
    private static ImageTexture Paint(Random random)
    {
        const int width = CellWidth * Variants;
        var cover = new float[width * CellHeight];
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
        void Put(int x, int y, float c)
        {
            int i = y * width + x;
            cover[i] = 1f - (1f - cover[i]) * (1f - Math.Clamp(c, 0f, 1f));
        }

        var stria = new float[CellWidth];
        for (int v = 0; v < Variants; v++)
        {
            int left = v * CellWidth + 3, right = (v + 1) * CellWidth - 3;
            float Margin(int x) => Math.Clamp(MathF.Min(x - left, right - x) / 8f, 0f, 1f);

            // Striations down the stain: a strength per column, smoothed a little across.
            for (int x = 0; x < CellWidth; x++)
            {
                stria[x] = R(0.35f, 1f);
            }

            for (int pass = 0; pass < 2; pass++)
            {
                for (int x = 1; x + 1 < CellWidth; x++)
                {
                    stria[x] = (stria[x - 1] + 2f * stria[x] + stria[x + 1]) * 0.25f;
                }
            }

            // Along the top, the lower edge ragged.
            float depth = R(26f, 60f), top = R(0.4f, 0.6f);
            for (int x = left; x < right; x++)
            {
                float reach = depth * (0.55f + 0.9f * stria[x - v * CellWidth]);
                for (int y = 0; y < (int)(reach * 2.5f) && y < CellHeight; y++)
                {
                    Put(x, y, top * MathF.Exp(-y / reach) * Margin(x));
                }
            }

            // Plumes: broad soft runs where most of the water goes.
            int plumes = random.Next(2, 5);
            for (int k = 0; k < plumes; k++)
            {
                float half = R(9f, 24f);
                float xc = R(left + half * 0.6f, right - half * 0.6f);
                float length = CellHeight * R(0.5f, 0.98f), strength = R(0.35f, 0.65f);
                for (int y = 0; y < (int)length; y++)
                {
                    float t = y / length;
                    float w = half * (1f - 0.35f * t);
                    float along = strength * MathF.Pow(1f - t, 1.25f);
                    int x1 = Math.Max(left, (int)(xc - w * 2f)), x2 = Math.Min(right - 1, (int)(xc + w * 2f));
                    for (int x = x1; x <= x2; x++)
                    {
                        float d = (x + 0.5f - xc) / w;
                        Put(x, y, along * MathF.Exp(-d * d * 1.6f) * stria[x - v * CellWidth] * Margin(x));
                    }
                }
            }

            // Drips.
            int drips = random.Next(5, 11);
            for (int k = 0; k < drips; k++)
            {
                float half = R(1.2f, 4.5f);
                float x0 = R(left + half + 1f, right - half - 1f);
                float length = CellHeight * MathF.Pow(R(0.2f, 1f), 0.8f) * 0.96f;
                float s = R(0.4f, 0.9f);
                float wobble = R(0.4f, 2.2f), freq = R(0.015f, 0.05f), phase = R(0f, Mathf.Tau);
                for (int y = 0; y < (int)length; y++)
                {
                    float t = y / length;
                    float w = half * (1f - 0.5f * t);
                    float cx = x0 + MathF.Sin(y * freq + phase) * wobble;
                    float along = s * (1f - MathF.Pow(t, 1.4f));
                    int x1 = Math.Max(left, (int)(cx - w * 3f)), x2 = Math.Min(right - 1, (int)(cx + w * 3f));
                    for (int x = x1; x <= x2; x++)
                    {
                        float d = MathF.Abs(x + 0.5f - cx);
                        float core = Math.Clamp(w + 0.5f - d, 0f, 1f);
                        float halo = 0.25f * MathF.Max(0f, 1f - d / (w * 3f));
                        Put(x, y, along * MathF.Max(core, halo));
                    }
                }
            }
        }

        var bytes = new byte[width * CellHeight * 2];
        for (int i = 0; i < width * CellHeight; i++)
        {
            float c = cover[i];
            bytes[i * 2] = (byte)((1f - 0.35f * c) * 255f);
            bytes[i * 2 + 1] = (byte)(c * 255f);
        }

        Image image = Image.CreateFromData(width, CellHeight, false, Image.Format.La8, bytes);
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
