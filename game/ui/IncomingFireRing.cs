using System;
using Godot;
using Pb.Game.Core;

namespace Pb.Game.Ui;

/// <summary>
/// Draws the incoming-fire arcs (<see cref="IncomingFire"/>) on a ring round the crosshair, each turned to where its paint
/// came from as you turn: pale for a ball whizzing past, amber for one breaking close by, orange for one bouncing off you,
/// red and widest for the one that put you out, with a chevron pointing out along it. Each fades before it goes, and
/// shows stronger the closer the ball came.
/// </summary>
public partial class IncomingFireRing : Control
{
    private IncomingArcsDef _def = null!;
    private Func<float> _viewYaw = () => 0f;
    private Color[] _colours = Array.Empty<Color>();
    private bool _drawn;

    public IncomingFire Model { get; private set; } = null!;

    /// <summary>Arcs added so far, by kind (for the smoke tests' report).</summary>
    public int[] Added { get; } = new int[4];

    /// <param name="viewYaw">Where you're looking (the camera's yaw), read every frame.</param>
    public void Configure(IncomingArcsDef def, Func<float> viewYaw)
    {
        _def = def;
        _viewYaw = viewYaw;
        _colours = Array.ConvertAll(def.Colors, c => Color.FromHtml(c));
        Model = new IncomingFire(def.Max, def.Merge_deg, def.Life_s);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>Paint travelling along <paramref name="travel"/> (world, Godot axes) came at you: an arc of <paramref name="kind"/>.</summary>
    public void Add(System.Numerics.Vector3 travel, IncomingKind kind, float strength)
    {
        if (Model is not null && Model.Add(travel, kind, strength))
        {
            Added[(int)kind]++;
        }
    }

    public override void _Process(double delta)
    {
        if (Model is null)
        {
            return;
        }

        Model.Update((float)delta);
        if (Model.Count > 0 || _drawn)
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        _drawn = Model is not null && Model.Count > 0;
        if (!_drawn)
        {
            return;
        }

        Vector2 c = Size * 0.5f;
        float radius = Size.Y * _def.Radius;
        float yaw = _viewYaw();
        foreach (IncomingArc arc in Model!.Arcs)
        {
            int k = (int)arc.Kind;
            float alpha = arc.Fade * (0.55f + 0.45f * arc.Strength);
            if (alpha <= 0.01f)
            {
                continue;
            }

            // Clockwise from straight up the screen; Godot's arcs run clockwise from +X (screen y points down).
            float screen = IncomingFire.ScreenAngle(arc.Bearing, yaw) - Mathf.Pi * 0.5f;
            float half = Mathf.DegToRad(_def.Width_deg[k]) * 0.5f;
            float width = _def.Thickness_px[k];
            Color colour = _colours[k] with { A = alpha };
            DrawArc(c, radius, screen - half, screen + half, 24, new Color(0f, 0f, 0f, 0.6f * alpha), width + 3f, true);
            DrawArc(c, radius, screen - half, screen + half, 24, colour, width, true);
            if (arc.Kind == IncomingKind.Hit)
            {
                // A chevron out along it: that way.
                Vector2 dir = new(Mathf.Cos(screen), Mathf.Sin(screen));
                Vector2 side = new(-dir.Y, dir.X);
                Vector2 tip = c + dir * (radius + width + 16f), back = c + dir * (radius + width + 3f);
                DrawColoredPolygon(new[] { tip, back + side * 9f, back - side * 9f }, colour);
            }
        }
    }
}
