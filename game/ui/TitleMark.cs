using System;
using Godot;

namespace Pb.Game.Ui;

/// <summary>
/// The game's title on the main menu: its letters stencilled in dark over a splat of paint in the menu's
/// accent colour (a blob with fingers thrown out round it, droplets beyond, drips running down), with a
/// darker splat a little behind it for depth. The splat is painted once at load. The title is the
/// working one; nothing here is anyone's mark.
/// </summary>
public partial class TitleMark : Control
{
    private const int Width = 360, Height = 200;

    private static ImageTexture? _splat;

    private string _text = "";
    private Color _paint;

    public void Configure(string text, Color paint)
    {
        _text = text;
        _paint = paint;
        CustomMinimumSize = new Vector2(Width, Height);
        MouseFilter = MouseFilterEnum.Ignore;
        _splat ??= Paint(new Random(0x5B1A7));
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_splat is null)
        {
            return;
        }

        var rect = new Rect2(Vector2.Zero, new Vector2(Width, Height));
        DrawTextureRect(_splat, new Rect2(rect.Position + new Vector2(6f, 7f), rect.Size), false, _paint.Darkened(0.55f) with { A = 0.85f });
        DrawTextureRect(_splat, rect, false, _paint);
        Font font = ThemeDB.FallbackFont;
        const int Size = 92;
        Vector2 size = font.GetStringSize(_text, HorizontalAlignment.Left, -1, Size);
        // On the blob's middle (where the painter puts it), the baseline a little below centre.
        var at = new Vector2(Width * 0.44f - size.X * 0.5f, Height * 0.46f + size.Y * 0.32f);
        DrawString(font, at, _text, HorizontalAlignment.Left, -1, Size, new Color(0.09f, 0.09f, 0.1f));
    }

    /// <summary>
    /// The splat in white on clear: coverage from the union of a lobed blob, fingers (tapered capsules
    /// reaching out from it), droplets round it and drips running down from its lower edge.
    /// </summary>
    private static ImageTexture Paint(Random random)
    {
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
        var centre = new Vector2(Width * 0.44f, Height * 0.46f);
        float radius = 60f;
        float p1 = R(0f, Mathf.Tau), p2 = R(0f, Mathf.Tau), p3 = R(0f, Mathf.Tau);
        var fingers = new (Vector2 A, Vector2 B, float R0, float R1)[11];
        for (int i = 0; i < fingers.Length; i++)
        {
            float a = Mathf.Tau * (i + R(-0.3f, 0.3f)) / fingers.Length;
            var d = new Vector2(Mathf.Cos(a) * 1.2f, Mathf.Sin(a) * 0.76f);
            fingers[i] = (centre + d * radius * 0.7f, centre + d * radius * R(1.2f, 1.6f), R(9f, 14f), R(2f, 4.5f));
        }

        var drops = new (Vector2 At, float R)[18];
        for (int i = 0; i < drops.Length; i++)
        {
            float a = R(0f, Mathf.Tau);
            drops[i] = (centre + new Vector2(Mathf.Cos(a) * 1.2f, Mathf.Sin(a) * 0.62f) * radius * R(1.45f, 2.0f), R(2.5f, 7f));
        }

        var drips = new (Vector2 A, Vector2 B, float R)[4];
        for (int i = 0; i < drips.Length; i++)
        {
            float x = centre.X + R(-0.6f, 0.6f) * radius;
            float top = centre.Y + radius * 0.5f;
            drips[i] = (new Vector2(x, top), new Vector2(x + R(-2f, 2f), top + R(30f, 52f)), R(3.5f, 5.5f));
        }

        var bytes = new byte[Width * Height * 4];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                Vector2 d = p - centre;
                float angle = MathF.Atan2(d.Y, d.X);
                float edge = radius * (1f + 0.1f * MathF.Sin(5f * angle + p1) + 0.07f * MathF.Sin(9f * angle + p2) + 0.04f * MathF.Sin(17f * angle + p3));
                // Signed distance, roughly: negative inside. The blob is squashed a little to suit the letters.
                float inside = new Vector2(d.X / 1.25f, d.Y / 0.8f).Length() - edge;
                foreach ((Vector2 a, Vector2 b, float r0, float r1) in fingers)
                {
                    inside = MathF.Min(inside, Capsule(p, a, b, r0, r1));
                }

                foreach ((Vector2 at, float r) in drops)
                {
                    inside = MathF.Min(inside, p.DistanceTo(at) - r);
                }

                foreach ((Vector2 a, Vector2 b, float r) in drips)
                {
                    inside = MathF.Min(inside, Capsule(p, a, b, r, r * 0.8f));
                    inside = MathF.Min(inside, p.DistanceTo(b) - r * 1.35f);
                }

                float cover = Math.Clamp(0.5f - inside, 0f, 1f);
                int i = (y * Width + x) * 4;
                bytes[i] = bytes[i + 1] = bytes[i + 2] = 255;
                bytes[i + 3] = (byte)(cover * 255f);
            }
        }

        Image image = Image.CreateFromData(Width, Height, false, Image.Format.Rgba8, bytes);
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Distance to a capsule from <paramref name="a"/> (radius <paramref name="r0"/>) tapering to <paramref name="b"/> (radius <paramref name="r1"/>).</summary>
    private static float Capsule(Vector2 p, Vector2 a, Vector2 b, float r0, float r1)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0f, 1f);
        return p.DistanceTo(a + ab * t) - Mathf.Lerp(r0, r1, t);
    }
}
