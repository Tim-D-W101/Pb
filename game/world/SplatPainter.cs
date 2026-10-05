using System;
using Godot;

namespace Pb.Game.World;

/// <summary>
/// Paints the atlas of old paint splats for <see cref="OldPaint"/>: a 4 × 4 grid of cells, white
/// shapes on clear (the colour comes from each card), shaded a little darker where the paint dried
/// thick at the rim. The top two rows are for walls, with drips running down the cell; the bottom two
/// for the ground, without. In each row of eight: four round splats, two glancing ones sprayed
/// one way, a cluster of small hits, and one half washed away.
/// </summary>
public sealed class SplatPainter
{
    public const int Columns = 4;

    public const int CellPx = 256;

    /// <summary>Cells per surface: the first eight have drips (walls), the next eight don't (the ground).</summary>
    public const int Variants = 8;

    private readonly float[] _shade = new float[CellPx * CellPx];
    private readonly float[] _alpha = new float[CellPx * CellPx];
    private readonly byte[] _pixels;
    private readonly int _size;
    private uint _seed;
    private Random _random = new(1);

    public SplatPainter(uint seed)
    {
        _seed = seed;
        _size = CellPx * Columns;
        _pixels = new byte[_size * _size * 4];
    }

    /// <summary>The cell for a variant (0–7) on a wall or on the ground.</summary>
    public static int Cell(int variant, bool wall) => (wall ? 0 : Variants) + Math.Clamp(variant, 0, Variants - 1);

    public ImageTexture Paint()
    {
        for (int cell = 0; cell < Columns * Columns; cell++)
        {
            Array.Clear(_shade);
            Array.Clear(_alpha);
            _seed = _seed * 747796405u + 2891336453u;
            _random = new Random((int)(_seed & 0x7fffffff));
            bool drips = cell < Variants;
            switch (cell % Variants)
            {
                case 4:
                case 5:
                    Glancing(drips);
                    break;
                case 6:
                    for (int i = _random.Next(3, 6); i > 0; i--)
                    {
                        Splat(R(-0.5f, 0.5f), R(-0.5f, 0.45f), R(0.12f, 0.22f), drips && R(0f, 1f) < 0.5f, R(0f, 1f));
                    }

                    break;
                case 7:
                    Splat(0f, -0.05f, R(0.28f, 0.36f), drips, 1f);
                    WashOut();
                    break;
                default:
                    Splat(0f, -0.08f, R(0.26f, 0.36f), drips, 1f);
                    break;
            }

            Store(cell);
        }

        Image image = Image.CreateFromData(_size, _size, false, Image.Format.Rgba8, _pixels);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    // ---------------------------------------------------------------- shapes

    /// <summary>
    /// A ball's splat at (cx, cy) (u right, v down the cell, −1 to 1): a ragged middle, fingers
    /// thrown out round it, droplets beyond, and drips running down from its lower edge.
    /// </summary>
    private void Splat(float cx, float cy, float radius, bool drips, float weight)
    {
        int fingers = _random.Next(9, 17);
        var angles = new float[fingers];
        var lengths = new float[fingers];
        var widths = new float[fingers];
        for (int i = 0; i < fingers; i++)
        {
            angles[i] = Mathf.Tau * (i + R(-0.35f, 0.35f)) / fingers;
            lengths[i] = radius * R(0.35f, 1.3f) * (R(0f, 1f) < 0.25f ? 1.6f : 1f);
            widths[i] = R(0.10f, 0.22f);
        }

        float reach = radius * 2.6f;
        Stamp(cx, cy, reach, (x, y) =>
        {
            float d = MathF.Sqrt(x * x + y * y);
            float a = MathF.Atan2(y, x);
            float wobble = 1f + 0.18f * (Fbm(MathF.Cos(a) * 2.2f + cx * 7f, MathF.Sin(a) * 2.2f + cy * 7f, 1) - 0.5f) * 2f;
            float body = 1f - Smooth(radius * 0.92f, radius * 1.02f, d / wobble);
            // Fingers: thin wedges from the rim, tapering to a point.
            float finger = 0f;
            for (int i = 0; i < fingers; i++)
            {
                float da = MathF.Abs(Mathf.Wrap(a - angles[i], -Mathf.Pi, Mathf.Pi));
                float t = (d - radius * 0.8f) / lengths[i];
                if (t < 0f || t > 1f)
                {
                    continue;
                }

                float halfWidth = widths[i] * (1f - t) * radius / MathF.Max(d, 1e-3f);
                finger = MathF.Max(finger, 1f - Smooth(halfWidth * 0.75f, halfWidth, da));
            }

            float coverage = MathF.Max(body, finger);
            // Dried thicker at the rim, thin and lighter in the middle; a grain over all of it.
            float rim = Smooth(radius * 0.6f, radius * 0.98f, d) * body;
            float shade = 0.92f - 0.22f * rim - 0.12f * finger * (1f - body) + 0.08f * (Fbm(x * 9f, y * 9f, 2) - 0.5f);
            return (coverage, shade);
        }, weight);

        // Droplets thrown beyond the fingers, most along them.
        for (int i = _random.Next(10, 26); i > 0; i--)
        {
            float a = R(0f, 1f) < 0.6f ? angles[_random.Next(fingers)] + R(-0.15f, 0.15f) : R(0f, Mathf.Tau);
            float r = radius * R(1.25f, 2.4f), size = radius * R(0.03f, 0.1f);
            Drop(cx + MathF.Cos(a) * r, cy + MathF.Sin(a) * r, size, weight);
        }

        if (!drips)
        {
            return;
        }

        // Runs down the wall from the lower edge before it dried, each ending in a bead.
        for (int i = _random.Next(1, 5); i > 0; i--)
        {
            float x = cx + radius * R(-0.7f, 0.7f);
            float top = cy + MathF.Sqrt(MathF.Max(0f, radius * radius - (x - cx) * (x - cx))) * 0.8f;
            float length = R(0.15f, 0.62f) * (1f - MathF.Abs(x - cx) / radius * 0.5f);
            float width = radius * R(0.07f, 0.13f);
            Drip(x, top, MathF.Min(length, 0.96f - top), width, weight);
        }
    }

    /// <summary>A ball that hit at an angle: a long splat sprayed out one way.</summary>
    private void Glancing(bool drips)
    {
        float turn = R(-0.6f, 0.6f) + (_random.Next(2) == 0 ? 0f : Mathf.Pi);
        float c = MathF.Cos(turn), s = MathF.Sin(turn);
        float radius = R(0.16f, 0.22f);
        Stamp(0f, 0f, 1f, (x, y) =>
        {
            // Along the spray (a) and across it (b); the splat stretches forward and thins out.
            float a = x * c + y * s, b = -x * s + y * c;
            float along = a < 0f ? a / (radius * 1.1f) : a / (radius * 3.2f);
            float across = b / (radius * (1f - 0.55f * Mathf.Clamp(a / (radius * 3.2f), 0f, 1f)));
            float d = MathF.Sqrt(along * along + across * across);
            d *= 1f + 0.25f * (Fbm(x * 5f, y * 5f, 3) - 0.5f) * 2f;
            float coverage = 1f - Smooth(0.85f, 1f, d);
            float shade = 0.9f - 0.2f * Smooth(0.5f, 0.95f, d) + 0.08f * (Fbm(x * 9f, y * 9f, 4) - 0.5f);
            return (coverage, shade);
        }, 1f);

        // The spray ahead of it.
        for (int i = _random.Next(16, 34); i > 0; i--)
        {
            float a = radius * R(2.2f, 4.4f), b = radius * R(-1.4f, 1.4f) * a / (radius * 4.4f);
            Drop(a * c - b * s, a * s + b * c, radius * R(0.04f, 0.12f), 1f);
        }

        if (drips)
        {
            for (int i = _random.Next(0, 3); i > 0; i--)
            {
                float x = R(-radius, radius);
                Drip(x, radius * 0.6f, R(0.15f, 0.45f), radius * R(0.08f, 0.14f), 1f);
            }
        }
    }

    /// <summary>Rain and sun have taken patches of the paint off.</summary>
    private void WashOut()
    {
        for (int py = 0; py < CellPx; py++)
        {
            for (int px = 0; px < CellPx; px++)
            {
                (float u, float v) = Uv(px, py);
                int i = py * CellPx + px;
                float keep = Smooth(0.35f, 0.62f, Fbm(u * 3.2f + 11f, v * 3.2f + 5f, 5));
                _alpha[i] *= 0.35f + 0.65f * keep;
            }
        }
    }

    private void Drop(float cx, float cy, float radius, float weight) =>
        Stamp(cx, cy, radius * 1.5f, (x, y) => (1f - Smooth(radius * 0.7f, radius, MathF.Sqrt(x * x + y * y)), 0.82f), weight);

    /// <summary>A drip from (x, top) down the cell: a thin run, narrowing, with a bead at its end.</summary>
    private void Drip(float x, float top, float length, float width, float weight)
    {
        if (length <= 0.02f)
        {
            return;
        }

        float wander = R(-0.02f, 0.02f);
        Stamp(x, top + length * 0.5f, length * 0.5f + width * 2f, (dx, dy) =>
        {
            float t = Mathf.Clamp((dy + length * 0.5f) / length, 0f, 1f);
            float halfWidth = width * (1f - 0.45f * t);
            float off = MathF.Abs(dx - wander * t);
            float run = (1f - Smooth(halfWidth * 0.7f, halfWidth, off)) * (dy <= length * 0.5f ? 1f : 0f);
            float bead = 1f - Smooth(width * 1.05f, width * 1.35f, MathF.Sqrt((dx - wander) * (dx - wander) + (dy - length * 0.5f) * (dy - length * 0.5f)));
            return (MathF.Max(run, bead), 0.8f - 0.1f * t);
        }, weight);
    }

    /// <summary>Lays a shape over the cell: <paramref name="shape"/> gives coverage and shade at an offset from (cx, cy).</summary>
    private void Stamp(float cx, float cy, float reach, Func<float, float, (float Coverage, float Shade)> shape, float weight)
    {
        int x0 = Px(cx - reach), x1 = Px(cx + reach), y0 = Px(cy - reach), y1 = Px(cy + reach);
        for (int py = Math.Max(0, y0); py <= Math.Min(CellPx - 1, y1); py++)
        {
            for (int px = Math.Max(0, x0); px <= Math.Min(CellPx - 1, x1); px++)
            {
                (float u, float v) = Uv(px, py);
                float edge = 1f - Smooth(0.9f, 0.98f, MathF.Max(MathF.Abs(u), MathF.Abs(v)));
                (float coverage, float shade) = shape(u - cx, v - cy);
                float a = Mathf.Clamp(coverage * edge * weight, 0f, 1f);
                if (a <= 0.002f)
                {
                    continue;
                }

                int i = py * CellPx + px;
                // Over what's there: paint on paint dries a little darker.
                float before = _alpha[i];
                _shade[i] = before > 0f ? Mathf.Lerp(_shade[i], shade * 0.94f, a) : shade;
                _alpha[i] = before + a * (1f - before);
            }
        }
    }

    private void Store(int cell)
    {
        int ox = cell % Columns * CellPx, oy = cell / Columns * CellPx;
        for (int py = 0; py < CellPx; py++)
        {
            for (int px = 0; px < CellPx; px++)
            {
                int i = py * CellPx + px;
                int o = ((oy + py) * _size + ox + px) * 4;
                // Clear pixels keep a mid shade, so mipmaps don't pull a dark fringe in.
                byte shade = ToByte(_alpha[i] > 0.01f ? _shade[i] : 0.85f);
                _pixels[o] = shade;
                _pixels[o + 1] = shade;
                _pixels[o + 2] = shade;
                _pixels[o + 3] = ToByte(_alpha[i]);
            }
        }
    }

    private static (float U, float V) Uv(int px, int py) => ((px + 0.5f) / CellPx * 2f - 1f, (py + 0.5f) / CellPx * 2f - 1f);

    private static int Px(float u) => (int)((u + 1f) * 0.5f * CellPx);

    private float Fbm(float x, float y, uint salt)
    {
        float sum = 0f, amplitude = 0.5f;
        for (int i = 0; i < 3; i++)
        {
            sum += amplitude * Noise(x, y, _seed ^ (salt * 0x9E3779B9u) + (uint)i * 101u);
            x = x * 2.03f + 7.1f;
            y = y * 2.03f + 3.7f;
            amplitude *= 0.5f;
        }

        return sum / 0.875f;
    }

    private static float Noise(float x, float y, uint seed)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        float ux = fx * fx * (3f - 2f * fx), uy = fy * fy * (3f - 2f * fy);
        float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed), c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
    }

    private static float Hash(int x, int y, uint seed)
    {
        uint h = (uint)x * 374761393u + (uint)y * 668265263u + seed * 2246822519u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return (h ^ (h >> 16)) / (float)uint.MaxValue;
    }

    private static float Smooth(float a, float b, float x)
    {
        float t = Mathf.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    private float R(float a, float b) => a + (float)_random.NextDouble() * (b - a);
}
