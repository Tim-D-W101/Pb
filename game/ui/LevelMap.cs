using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Pb.Sim.Match;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Ui;

/// <summary>
/// A plan of the level for the briefing card, drawn from its primitives (north up): the ground and its
/// patches in their materials' colours, the buildings' ground floors, upper floors faintly over them,
/// walls and columns dark, props in their own colours, where you (and your team) start, and the objective
/// (the case's building and the ways out, or the room to hold), with a north arrow and a scale bar. A round in
/// part of the area frames that part, with the rest dimmed round it and its edge taped red and white. Drawn
/// once, nothing loaded.
/// </summary>
public partial class LevelMap : Control
{
    private const float Margin = 14f;

    /// <summary>How much of the area round a part of it the map shows (m).</summary>
    private const float AroundPlace = 8f;

    private static readonly Color Tape = new(0.8f, 0.2f, 0.15f);

    private readonly List<(Vector2[] Outline, Color Fill, bool Wall)> _shapes = new();
    private Pb.Sim.Collision.Aabb _bounds;
    private Rect2 _frame;
    private Rect2? _place;
    private Vector2 _you;
    private float _youYaw;
    private bool _hasYou;
    private readonly List<Vector2> _team = new();
    private readonly List<Rect2> _objectiveAreas = new();
    private readonly List<Vector2> _exits = new();
    private readonly List<(Vector2 At, Color Colour)> _flags = new();
    private Color _accent = UiKit.Accent;
    private Color _objectiveColour = UiKit.Accent;

    /// <summary>
    /// Draws <paramref name="level"/> at <paramref name="size"/>, with your start (plan position and yaw)
    /// and your teammates' if given, in <paramref name="colour"/>.
    /// </summary>
    public void Configure(LevelLayout level, Vector2 size, (Vector3 At, float Yaw)? you = null, IReadOnlyList<Vector3>? team = null, Color? colour = null)
    {
        CustomMinimumSize = size;
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
        _bounds = level.Bounds;
        var whole = new Rect2(_bounds.Min.X, _bounds.Min.Z, _bounds.Max.X - _bounds.Min.X, _bounds.Max.Z - _bounds.Min.Z);
        _place = level.Place is { Bounds: { } pb } ? new Rect2(pb.Min.X, pb.Min.Z, pb.Max.X - pb.Min.X, pb.Max.Z - pb.Min.Z) : null;
        _frame = _place is { } place ? place.Grow(AroundPlace).Intersection(whole) : whole;
        _accent = colour ?? UiKit.Accent;
        _shapes.Clear();
        Color Of(int material, float shade = 1f) => (Color.FromHtml(level.Materials[material].Def.Color) * shade) with { A = 1f };

        // Patches of ground, then ground floors, upper floors over them faintly, then stairs, walls, columns and props.
        var layers = new List<(int Order, Vector2[] Outline, Color Fill)>();
        var wallColour = new Color(0.1f, 0.1f, 0.11f);
        foreach (LevelPrimitive p in level.Primitives)
        {
            Pb.Sim.Collision.Aabb b = p.Bounds;
            Vector2[] outline = Outline(p);
            switch (p.Role)
            {
                case PrimitiveRole.GroundPatch:
                    layers.Add((0, outline, Of(p.Material, 0.9f)));
                    break;
                case PrimitiveRole.Floor when b.Max.Y < 1f:
                    layers.Add((1, outline, Of(p.Material, 1.15f)));
                    break;
                case PrimitiveRole.Floor:
                    layers.Add((2, outline, Of(p.Material, 1.15f) with { A = 0.35f }));
                    break;
                case PrimitiveRole.Stair or PrimitiveRole.Ramp:
                    layers.Add((3, outline, new Color(0.62f, 0.6f, 0.56f)));
                    break;
                case PrimitiveRole.Wall or PrimitiveRole.Column when b.Min.Y < 2.5f && p.Height > 0.6f:
                    layers.Add((4, outline, wallColour));
                    break;
                case PrimitiveRole.Prop:
                    layers.Add((5, outline, Of(p.Material, 0.85f)));
                    break;
            }
        }

        layers.Sort((a, b) => a.Order.CompareTo(b.Order));
        foreach ((int order, Vector2[] outline, Color fill) in layers)
        {
            _shapes.Add((outline, fill, order == 4));
        }

        _shapes.Insert(0, (new[]
        {
            new Vector2(_bounds.Min.X, _bounds.Min.Z), new Vector2(_bounds.Max.X, _bounds.Min.Z),
            new Vector2(_bounds.Max.X, _bounds.Max.Z), new Vector2(_bounds.Min.X, _bounds.Max.Z),
        }, Of(level.GroundMaterial.Index, 0.85f), false));

        _hasYou = you is not null;
        if (you is { } y)
        {
            _you = new Vector2(y.At.X, y.At.Z);
            _youYaw = y.Yaw;
        }

        _team.Clear();
        foreach (Vector3 t in team ?? Array.Empty<Vector3>())
        {
            _team.Add(new Vector2(t.X, t.Z));
        }

        QueueRedraw();
    }

    /// <summary>Marks the objective: the case's building and the ways out (Retrieve), or the room's parts (Hold).</summary>
    public void MarkObjective(ObjectiveState objective, Color colour)
    {
        _objectiveColour = colour;
        _objectiveAreas.Clear();
        _exits.Clear();
        static Rect2 Plan(Pb.Sim.Collision.Aabb box) => new(box.Min.X, box.Min.Z, box.Max.X - box.Min.X, box.Max.Z - box.Min.Z);
        if (objective.Kind == ObjectiveKind.Retrieve)
        {
            _objectiveAreas.Add(Plan(objective.Spot.AreaBox));
            foreach (ExitSpec exit in objective.Level.Exits)
            {
                _exits.Add(new Vector2(exit.Position.X, exit.Position.Z));
            }
        }
        else if (objective.Room is { } room)
        {
            foreach (Pb.Sim.Collision.Aabb box in room.Boxes)
            {
                _objectiveAreas.Add(Plan(box));
            }
        }

        QueueRedraw();
    }

    /// <summary>Capture the flag: each flag where it stands, in its colour (<paramref name="colourOf"/> a side's; −1 the one in the middle).</summary>
    public void MarkFlags(FlagSet flags, Func<int, Color> colourOf)
    {
        _flags.Clear();
        for (int i = 0; i < flags.Count; i++)
        {
            _flags.Add((new Vector2(flags.Home(i).X, flags.Home(i).Z), colourOf(flags.Owner(i))));
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.09f, 0.095f, 0.1f));
        float w = _frame.Size.X, d = _frame.Size.Y;
        if (w <= 0f || d <= 0f)
        {
            return;
        }

        float scale = MathF.Min((size.X - 2f * Margin) / w, (size.Y - 2f * Margin) / d);
        var origin = new Vector2((size.X - w * scale) * 0.5f, (size.Y - d * scale) * 0.5f);
        // North (−z) is up the page, east (+x) to the right.
        Vector2 Map(Vector2 plan) => origin + (plan - _frame.Position) * scale;

        foreach ((Vector2[] outline, Color fill, bool wall) in _shapes)
        {
            var points = new Vector2[outline.Length + 1];
            for (int i = 0; i < outline.Length; i++)
            {
                points[i] = Map(outline[i]);
            }

            points[^1] = points[0];
            DrawColoredPolygon(points[..^1], fill);
            // Walls are thinner than a pixel at this scale: draw round them too, so they read.
            if (wall)
            {
                DrawPolyline(points, fill, 1.6f, antialiased: true);
            }
        }

        // A part of the area: the rest dimmed, the edge taped.
        if (_place is { } place)
        {
            Vector2 a = Map(place.Position), b = Map(place.End);
            var dim = new Color(0.05f, 0.05f, 0.06f, 0.62f);
            DrawRect(new Rect2(0f, 0f, size.X, a.Y), dim);
            DrawRect(new Rect2(0f, b.Y, size.X, size.Y - b.Y), dim);
            DrawRect(new Rect2(0f, a.Y, a.X, b.Y - a.Y), dim);
            DrawRect(new Rect2(b.X, a.Y, size.X - b.X, b.Y - a.Y), dim);
            Vector2[] corners = { a, new(b.X, a.Y), b, new(a.X, b.Y) };
            for (int e = 0; e < 4; e++)
            {
                Vector2 from = corners[e], to = corners[(e + 1) % 4];
                float length = from.DistanceTo(to);
                int dashes = Math.Max(1, (int)(length / 7f));
                for (int k = 0; k < dashes; k++)
                {
                    DrawLine(from.Lerp(to, (float)k / dashes), from.Lerp(to, (float)(k + 1) / dashes), k % 2 == 0 ? Tape : UiKit.Text, 2.5f);
                }
            }
        }

        // The objective: its area outlined and lightly filled, the ways out as rings.
        foreach (Rect2 area in _objectiveAreas)
        {
            var r = new Rect2(Map(area.Position), area.Size * scale);
            DrawRect(r, _objectiveColour with { A = 0.22f });
            DrawRect(r, _objectiveColour, filled: false, width: 2f);
        }

        foreach (Vector2 exit in _exits)
        {
            DrawArc(Map(exit), 7f, 0f, Mathf.Tau, 20, _objectiveColour, 2.5f, antialiased: true);
            DrawCircle(Map(exit), 2.5f, _objectiveColour);
        }

        // The flags: a pole and its pennant.
        foreach ((Vector2 at, Color colour) in _flags)
        {
            Vector2 pole = Map(at);
            DrawLine(pole + new Vector2(-4f, 6f), pole + new Vector2(-4f, -9f), new Color(0f, 0f, 0f, 0.85f), 3f, antialiased: true);
            Vector2[] pennant = { pole + new Vector2(-4f, -9f), pole + new Vector2(8f, -5f), pole + new Vector2(-4f, -1f) };
            DrawColoredPolygon(pennant, colour);
            DrawPolyline(new[] { pennant[0], pennant[1], pennant[2], pennant[0] }, new Color(0f, 0f, 0f, 0.75f), 1.5f, antialiased: true);
        }

        // Teammates, then you: an arrow the way you face.
        foreach (Vector2 t in _team)
        {
            DrawCircle(Map(t), 4.5f, _accent.Darkened(0.2f));
        }

        if (_hasYou)
        {
            Vector2 at = Map(_you);
            // Yaw 0 faces −z (up the page); positive yaw turns left (anticlockwise on the page).
            var forward = new Vector2(-MathF.Sin(_youYaw), -MathF.Cos(_youYaw));
            var side = new Vector2(-forward.Y, forward.X);
            DrawColoredPolygon(new[] { at + forward * 11f, at - forward * 6f + side * 7f, at - forward * 3f, at - forward * 6f - side * 7f }, _accent);
            DrawCircle(at, 2.2f, Colors.White);
        }

        // North arrow, top right; a 20 m scale bar, bottom left.
        var north = new Vector2(size.X - Margin - 10f, Margin + 18f);
        DrawColoredPolygon(new[] { north + new Vector2(0f, -14f), north + new Vector2(6f, 4f), north + new Vector2(-6f, 4f) }, UiKit.Text);
        DrawString(ThemeDB.FallbackFont, north + new Vector2(-5f, 20f), "N", HorizontalAlignment.Left, -1, 14, UiKit.Text);
        float bar = 20f * scale;
        var foot = new Vector2(Margin, size.Y - Margin);
        DrawLine(foot, foot + new Vector2(bar, 0f), UiKit.Text, 2f);
        DrawLine(foot + new Vector2(0f, -4f), foot + new Vector2(0f, 2f), UiKit.Text, 2f);
        DrawLine(foot + new Vector2(bar, -4f), foot + new Vector2(bar, 2f), UiKit.Text, 2f);
        DrawString(ThemeDB.FallbackFont, foot + new Vector2(bar + 6f, 4f), "20 m", HorizontalAlignment.Left, -1, 13, UiKit.Text);
    }

    /// <summary>The primitive's footprint in plan (x, z): a turned box's four corners, or a circle for an upright cylinder.</summary>
    private static Vector2[] Outline(LevelPrimitive p)
    {
        Vector3 centre = p.Center.ToGodot();
        if (p.Kind == PrimitiveKind.Cylinder)
        {
            Vector3 axis = SVector3.Transform(SVector3.UnitY, p.Rotation).ToGodot();
            if (MathF.Abs(axis.Y) > 0.7f)
            {
                var circle = new Vector2[16];
                for (int i = 0; i < circle.Length; i++)
                {
                    float a = Mathf.Tau * i / circle.Length;
                    circle[i] = new Vector2(centre.X + MathF.Cos(a) * p.HalfExtents.X, centre.Z + MathF.Sin(a) * p.HalfExtents.X);
                }

                return circle;
            }

            // Lying down: its bounds.
            Pb.Sim.Collision.Aabb b = p.Bounds;
            return new[] { new Vector2(b.Min.X, b.Min.Z), new Vector2(b.Max.X, b.Min.Z), new Vector2(b.Max.X, b.Max.Z), new Vector2(b.Min.X, b.Max.Z) };
        }

        // The box's corners seen from above: the hull of its eight corners' plan positions.
        var corners = new List<Vector2>(8);
        for (int i = 0; i < 8; i++)
        {
            var local = new SVector3((i & 1) == 0 ? -p.HalfExtents.X : p.HalfExtents.X, (i & 2) == 0 ? -p.HalfExtents.Y : p.HalfExtents.Y,
                (i & 4) == 0 ? -p.HalfExtents.Z : p.HalfExtents.Z);
            SVector3 world = p.Center + SVector3.Transform(local, p.Rotation);
            corners.Add(new Vector2(world.X, world.Z));
        }

        Vector2[] hull = Geometry2D.ConvexHull(corners.ToArray());
        // Godot closes the hull by repeating its first point.
        return hull.Length > 3 && hull[0].IsEqualApprox(hull[^1]) ? hull[..^1] : hull;
    }
}
