using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Data;
using Pb.Sim.Level;

namespace Pb.Game.World;

/// <summary>
/// Worn paint markings on the buildings (each template's <c>"markings"</c>) and the open ground (the
/// level's): lines and hatched areas on the ground floor or the ground, digits stencilled on a floor or a
/// wall, and stripes round the foot of a building's ground-floor columns. Cards cut from an atlas painted at load (<see cref="MarkingPainter"/>), drawn in one
/// MultiMesh with the old paint's shader, faded out with distance (presentation.jsonc "markings").
/// Presentation only.
/// </summary>
public partial class Markings : Node3D
{
    /// <summary>Cards sit this far off what they're painted on.</summary>
    private const float Lift = 0.004f;

    /// <summary>A line is drawn in cards about this long, each worn differently.</summary>
    private const float Dash = 2f;

    private static readonly Dictionary<uint, ImageTexture> Atlases = new();

    public int CardCount { get; private set; }

    public void Build(LevelLayout level, MarkingsViewDef view)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        uint seed = (uint)LevelBuilder.StableHash(level.Id) ^ 0x3A2C1u;
        var random = new Random((int)(seed & 0x7fffffff));
        var cards = new List<Card>();
        var painted = new List<(MarkingsDef Markings, PlanFrame Frame, ColumnDef[] Columns)>();
        if (level.Markings is { } ground)
        {
            painted.Add((ground, PlanFrame.Identity, Array.Empty<ColumnDef>()));
        }

        foreach (PlacedBuilding building in level.Buildings)
        {
            if (building.Template.Def.Markings is { } m)
            {
                painted.Add((m, building.Frame, building.Template.Def.Columns ?? Array.Empty<ColumnDef>()));
            }
        }

        foreach ((MarkingsDef m, PlanFrame frame, ColumnDef[] columns) in painted)
        {
            Color paint = Color.FromHtml(m.Color);
            Vector3 At(float x, float z, float y = 0.002f) => frame.PlanToWorld(new System.Numerics.Vector2(x, z), y).ToGodot();
            foreach (float[] line in m.Lines_m ?? Array.Empty<float[]>())
            {
                Line(cards, At(line[0], line[1]), At(line[2], line[3]), m.LineWidth_m, paint, view, random);
            }

            foreach (float[] r in m.Hatches_m ?? Array.Empty<float[]>())
            {
                Hatch(cards, frame, r, m.LineWidth_m, paint, view, random);
            }

            foreach (StencilDef stencil in m.Stencils ?? Array.Empty<StencilDef>())
            {
                Stencil(cards, frame, stencil, stencil.Color is { } c ? Color.FromHtml(c) : paint, view, random);
            }

            if (m.ColumnStripes_m > 0f)
            {
                foreach (ColumnDef column in columns)
                {
                    if (column.BaseElevation_m < 0.01f)
                    {
                        Stripes(cards, frame, column, MathF.Min(m.ColumnStripes_m, column.Height_m), paint, view, random);
                    }
                }
            }
        }

        // A field's banners: their words stencilled on the backing FieldDressing hangs on the nets.
        foreach (FieldBannerDef banner in level.FieldDressing?.Banners ?? Array.Empty<FieldBannerDef>())
        {
            Vector3 normal = new Basis(Vector3.Up, Mathf.DegToRad(banner.Yaw_deg)) * Vector3.Back;
            Vector3 face = new Vector3(banner.At_m[0], banner.At_m[1], banner.At_m[2]) + normal * FieldDressing.BannerStandoff;
            float size = MathF.Min(banner.Size_m[1] * 0.62f, banner.Size_m[0] / (banner.Text.Length * MarkingPainter.DigitAspect * 0.92f + 0.6f));
            var words = new StencilDef { Text = banner.Text, At_m = new[] { face.X, face.Y, face.Z }, Size_m = size, Yaw_deg = banner.Yaw_deg, Wall = true };
            Stencil(cards, PlanFrame.Identity, words, Color.FromHtml(banner.TextColor), view, random, printed: true);
        }

        CardCount = cards.Count;
        if (cards.Count == 0)
        {
            return;
        }

        if (!Atlases.TryGetValue(seed, out ImageTexture? atlas))
        {
            atlas = new MarkingPainter(seed).Paint();
            Atlases[seed] = atlas;
        }

        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/old_paint.gdshader") };
        material.SetShaderParameter("atlas", atlas);
        material.SetShaderParameter("columns", (float)MarkingPainter.Columns);
        material.SetShaderParameter("fade_start", view.FadeStart_m);
        material.SetShaderParameter("fade_end", view.FadeEnd_m);
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
            multimesh.SetInstanceTransform(i, new Transform3D(new Basis(c.Right, c.Up, c.Normal), c.Position + c.Normal * (i % 6) * 0.0002f));
            multimesh.SetInstanceColor(i, c.Color);
            multimesh.SetInstanceCustomData(i, new Color(c.Cell, c.Opacity, 0f, 0f));
        }

        AddChild(new MultiMeshInstance3D
        {
            Name = "MarkingCards",
            Multimesh = multimesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <summary>A painted line on the floor from <paramref name="a"/> to <paramref name="b"/>, in cards about <see cref="Dash"/> long.</summary>
    private static void Line(List<Card> cards, Vector3 a, Vector3 b, float width, Color paint, MarkingsViewDef view, Random random)
    {
        float length = a.DistanceTo(b);
        if (length < 0.01f)
        {
            return;
        }

        Vector3 along = (b - a) / length;
        // Right along the line, up across it, so right × up is the floor's normal.
        Vector3 across = Vector3.Up.Cross(along);
        int pieces = Math.Max(1, Mathf.RoundToInt(length / Dash));
        float piece = length / pieces;
        for (int i = 0; i < pieces; i++)
        {
            Vector3 middle = a + along * (piece * (i + 0.5f)) + Vector3.Up * Lift;
            cards.Add(new Card(middle, along * (piece + 0.01f), across * width, Vector3.Up, random.Next(MarkingPainter.Bands), Shade(paint, random), Opacity(view, random)));
        }
    }

    /// <summary>A hatched area on the floor: diagonal stripes in square-ish tiles, edged with a line.</summary>
    private static void Hatch(List<Card> cards, PlanFrame frame, float[] r, float width, Color paint, MarkingsViewDef view, Random random)
    {
        float w = r[2] - r[0], d = r[3] - r[1];
        int nx = Math.Max(1, Mathf.RoundToInt(w)), nz = Math.Max(1, Mathf.RoundToInt(d));
        Vector3 east = frame.PlanToWorld(new System.Numerics.Vector2(1f, 0f), 0f).ToGodot() - frame.PlanToWorld(System.Numerics.Vector2.Zero, 0f).ToGodot();
        Vector3 north = Vector3.Up.Cross(east);
        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < nz; j++)
            {
                float x = r[0] + w * (i + 0.5f) / nx, z = r[1] + d * (j + 0.5f) / nz;
                Vector3 middle = frame.PlanToWorld(new System.Numerics.Vector2(x, z), 0.002f).ToGodot() + Vector3.Up * Lift;
                cards.Add(new Card(middle, east * (w / nx), north * (d / nz), Vector3.Up, MarkingPainter.Hatch, Shade(paint, random), Opacity(view, random)));
            }
        }

        Vector3 Corner(float x, float z) => frame.PlanToWorld(new System.Numerics.Vector2(x, z), 0.002f).ToGodot();
        Line(cards, Corner(r[0], r[1]), Corner(r[2], r[1]), width, paint, view, random);
        Line(cards, Corner(r[2], r[1]), Corner(r[2], r[3]), width, paint, view, random);
        Line(cards, Corner(r[2], r[3]), Corner(r[0], r[3]), width, paint, view, random);
        Line(cards, Corner(r[0], r[3]), Corner(r[0], r[1]), width, paint, view, random);
    }

    /// <summary>Stencilled characters side by side, centred on the stencil's point, on the floor or upright on a wall.</summary>
    /// <summary>
    /// A stencil's characters, each a card; <paramref name="printed"/> ones (a banner's words) are solid and all one shade,
    /// not sprayed on by hand.
    /// </summary>
    private static void Stencil(List<Card> cards, PlanFrame frame, StencilDef stencil, Color paint, MarkingsViewDef view, Random random, bool printed = false)
    {
        var at = new Vector3(stencil.At_m[0], stencil.At_m[1], stencil.At_m[2]);
        var turn = new Basis(Vector3.Up, Mathf.DegToRad(stencil.Yaw_deg));
        Basis plan = new Basis(Vector3.Up, frame.Yaw) * turn;
        // Read from +Z at yaw 0: on the floor their tops point −Z; on a wall they face +Z, tops up.
        Vector3 right = plan * Vector3.Right;
        Vector3 up = stencil.Wall ? Vector3.Up : plan * Vector3.Forward;
        Vector3 normal = stencil.Wall ? plan * Vector3.Back : Vector3.Up;
        Vector3 middle = frame.ToWorld(at.ToSim()).ToGodot() + normal * Lift;
        float size = stencil.Size_m, step = size * MarkingPainter.DigitAspect * 0.92f;
        for (int i = 0; i < stencil.Text.Length; i++)
        {
            int cell = MarkingPainter.Cell(stencil.Text[i]);
            if (cell < 0)
            {
                continue;
            }

            float offset = (i - (stencil.Text.Length - 1) * 0.5f) * step;
            cards.Add(printed
                ? new Card(middle + right * offset, right * size, up * size, normal, cell, paint.SrgbToLinear(), 1f)
                : new Card(middle + right * offset, right * size, up * size, normal, cell, Shade(paint, random), Opacity(view, random)));
        }
    }

    /// <summary>Stripes round the foot of a column: a stack of square cards up each face.</summary>
    private static void Stripes(List<Card> cards, PlanFrame frame, ColumnDef column, float height, Color paint, MarkingsViewDef view, Random random)
    {
        Vector3 center = frame.PlanToWorld(new System.Numerics.Vector2(column.At_m[0], column.At_m[1]), 0f).ToGodot();
        var plan = new Basis(Vector3.Up, frame.Yaw);
        float hx = column.Size_m[0] * 0.5f, hz = column.Size_m[1] * 0.5f;
        Color shade = Shade(paint, random);
        float opacity = Opacity(view, random);
        foreach ((Vector3 normal, float half, float across) in new[] { (Vector3.Back, hz, hx), (Vector3.Forward, hz, hx), (Vector3.Right, hx, hz), (Vector3.Left, hx, hz) })
        {
            Vector3 n = plan * normal;
            Vector3 right = Vector3.Up.Cross(n);
            float face = across * 2f;
            int rows = Math.Max(1, Mathf.RoundToInt(height / face));
            float rise = height / rows;
            for (int k = 0; k < rows; k++)
            {
                Vector3 middle = center + n * (half + Lift) + Vector3.Up * (rise * (k + 0.5f));
                cards.Add(new Card(middle, right * face, Vector3.Up * rise, n, MarkingPainter.CoarseHatch, shade, opacity));
            }
        }
    }

    /// <summary>The paint, a shade lighter or darker from card to card, in linear colour.</summary>
    private static Color Shade(Color paint, Random random) => (paint * (0.85f + 0.25f * (float)random.NextDouble())).SrgbToLinear();

    private static float Opacity(MarkingsViewDef view, Random random) => Mathf.Lerp(view.Opacity[0], view.Opacity[1], (float)random.NextDouble());

    /// <summary>A unit square on the origin in the XY plane, facing +Z, UV across it (v down).</summary>
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

    private readonly record struct Card(Vector3 Position, Vector3 Right, Vector3 Up, Vector3 Normal, int Cell, Color Color, float Opacity);
}
