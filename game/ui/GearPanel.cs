using System;
using Godot;
using Pb.Sim.Core;
using Pb.Sim.Gear;

namespace Pb.Game.Ui;

/// <summary>
/// The gear readout at the bottom right (spec §6: loader count, pods remaining, air gauge in bar),
/// drawn rather than written:
/// <list type="bullet">
/// <item>the fire mode;</item>
/// <item>a loader whose bowl holds balls of your paint up to how full it is, with its count, and a thin bar under the count while you refill;</item>
/// <item>a pod for each pod, filled to what's left in it, and the balls left in them all;</item>
/// <item>the tank's pressure as a bar marked where the regulator gives out (below it the ball slows), amber at the low warning, red below the regulator, with the figure in bar.</item>
/// </list>
/// It reads the marker for display only.
/// </summary>
public partial class GearPanel : Control
{
    public static readonly Vector2 PanelSize = new(300f, 150f);

    private static readonly Color Back = new(0f, 0f, 0f, 0.38f);
    private static readonly Color Line = new(0.86f, 0.86f, 0.82f, 0.9f);
    private static readonly Color Dim = new(0.66f, 0.66f, 0.62f);
    private static readonly Color Empty = new(0.1f, 0.1f, 0.1f, 0.55f);
    private static readonly Color AirGood = new(0.35f, 0.8f, 0.45f);
    private static readonly Color AirLow = new(0.95f, 0.7f, 0.2f);
    private static readonly Color AirOut = new(0.95f, 0.3f, 0.25f);

    private Marker? _marker;
    private Color _paint = Colors.Orange;
    private Font _font = null!;
    private double _redraw;

    public void Initialize(Marker marker, Color paint)
    {
        _marker = marker;
        _paint = paint;
        _font = ThemeDB.FallbackFont;
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = PanelSize;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        // Ten times a second is plenty for counts; the refill bar moves smoothly enough at that too.
        if ((_redraw -= delta) <= 0)
        {
            _redraw = 0.1;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_marker is not { } m)
        {
            return;
        }

        DrawStyleBox(new StyleBoxFlat
        {
            BgColor = Back,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
        }, new Rect2(Vector2.Zero, PanelSize));

        string mode = m.Fire.Mode == FireMode.Ramping ? (m.Fire.IsRamping ? "RAMPING ››" : "RAMPING") : "SEMI";
        Text(new Vector2(14f, 24f), mode, 15, Dim);

        // The loader: a bowl of balls on its neck, the count beside it.
        PaintSupply paint = m.Paint;
        float full = paint.Params.Capacity > 0 ? (float)paint.Loader / paint.Params.Capacity : 0f;
        Loader(new Vector2(38f, 58f), 20f, full);
        string count = paint.Loader.ToString();
        Text(new Vector2(72f, 74f), count, 34, Colors.White);
        float countWidth = _font.GetStringSize(count, HorizontalAlignment.Left, -1, 34).X;
        Text(new Vector2(78f + countWidth, 74f), "/ " + paint.Params.Capacity, 16, Dim);
        if (m.Refill.Active)
        {
            Bar(new Rect2(72f, 82f, 150f, 4f), m.Refill.Progress(paint.Params), AirLow, Array.Empty<float>());
            Text(new Vector2(228f, 88f), "REFILL", 12, AirLow);
        }

        // The pods, each filled to what's left in it, and the balls left in them all.
        ReadOnlySpan<int> pods = paint.Pods;
        for (int i = 0; i < pods.Length; i++)
        {
            float left = paint.Params.PodCapacity > 0 ? (float)pods[i] / paint.Params.PodCapacity : 0f;
            Pod(new Rect2(14f + i * 17f, 96f, 11f, 24f), left);
        }

        Text(new Vector2(22f + pods.Length * 17f, 116f), paint.PodsRemaining + " in pods", 15, Dim);

        // The air: a little bottle, the bar marked at the regulator and the low warning, the figure.
        AirTank air = m.Air;
        float fill = air.Params.FillPressure;
        Color state = air.BelowRegulator ? AirOut : air.Pressure < air.Params.LowWarningPressure ? AirLow : AirGood;
        Bottle(new Vector2(14f, 132f), state);
        Bar(new Rect2(46f, 130f, 168f, 9f), air.FillFraction, state,
            new[] { air.Params.RegulatorPressure / fill, air.Params.LowWarningPressure / fill });
        Text(new Vector2(222f, 140f), (air.Pressure / Units.BarToPascals).ToString("0") + " bar", 15, Colors.White);
    }

    /// <summary>A loader's bowl (a circle on a short neck) with balls stacked in it up to <paramref name="full"/>.</summary>
    private void Loader(Vector2 centre, float radius, float full)
    {
        DrawRect(new Rect2(centre.X - 5f, centre.Y + radius - 2f, 10f, 9f), Line);
        DrawCircle(centre, radius, Empty);
        // Balls in rows from the bottom, packed in a hexagonal pattern, up to the fill level.
        const float Ball = 3.1f;
        float level = centre.Y + radius - full * radius * 2f;
        Color shade = _paint.Darkened(0.25f), shine = _paint.Lightened(0.45f);
        int row = 0;
        for (float y = centre.Y + radius - Ball - 1f; y > centre.Y - radius; y -= Ball * 1.75f, row++)
        {
            if (y < level)
            {
                break;
            }

            for (float x = centre.X - radius + Ball + (row % 2 == 0 ? 0f : Ball); x < centre.X + radius; x += Ball * 2.05f)
            {
                var p = new Vector2(x, y);
                if (p.DistanceTo(centre) > radius - Ball - 0.5f)
                {
                    continue;
                }

                DrawCircle(p, Ball, shade);
                DrawCircle(p - new Vector2(0.6f, 0.6f), Ball * 0.78f, _paint);
                DrawCircle(p - new Vector2(1.2f, 1.2f), Ball * 0.3f, shine);
            }
        }

        DrawArc(centre, radius, 0f, Mathf.Tau, 40, Line, 1.6f, true);
        // The lid's hinge across the top.
        DrawLine(centre + new Vector2(-radius * 0.5f, -radius * 0.86f), centre + new Vector2(radius * 0.5f, -radius * 0.86f), Line, 2f, true);
    }

    /// <summary>A pod: a rounded tube with a cap, filled from the bottom.</summary>
    private void Pod(Rect2 r, float left)
    {
        var body = new Rect2(r.Position + new Vector2(0f, 3f), r.Size - new Vector2(0f, 3f));
        DrawRect(body, Empty);
        if (left > 0f)
        {
            float h = body.Size.Y * left;
            DrawRect(new Rect2(body.Position.X, body.End.Y - h, body.Size.X, h), _paint);
        }

        DrawRect(body, Line, false, 1.4f);
        DrawRect(new Rect2(r.Position.X + 1.5f, r.Position.Y, r.Size.X - 3f, 3.5f), Line);
    }

    /// <summary>An air bottle lying down: its body, the regulator on its end.</summary>
    private void Bottle(Vector2 at, Color state)
    {
        DrawRect(new Rect2(at.X, at.Y - 5f, 20f, 10f), state.Darkened(0.35f));
        DrawRect(new Rect2(at.X, at.Y - 5f, 20f, 10f), Line, false, 1.4f);
        DrawRect(new Rect2(at.X + 20f, at.Y - 3f, 6f, 6f), Line);
    }

    /// <summary>A bar filled to <paramref name="value"/>, with ticks at the given fractions.</summary>
    private void Bar(Rect2 r, float value, Color color, float[] ticks)
    {
        DrawRect(r, Empty);
        DrawRect(new Rect2(r.Position, new Vector2(r.Size.X * Math.Clamp(value, 0f, 1f), r.Size.Y)), color);
        foreach (float t in ticks)
        {
            float x = r.Position.X + r.Size.X * Math.Clamp(t, 0f, 1f);
            DrawLine(new Vector2(x, r.Position.Y - 3f), new Vector2(x, r.End.Y + 3f), Line, 1.5f);
        }

        DrawRect(r, Line, false, 1.2f);
    }

    private void Text(Vector2 at, string text, int size, Color color)
    {
        DrawStringOutline(_font, at, text, HorizontalAlignment.Left, -1, size, 4, new Color(0f, 0f, 0f, 0.7f));
        DrawString(_font, at, text, HorizontalAlignment.Left, -1, size, color);
    }
}
