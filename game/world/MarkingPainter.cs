using System;
using System.Collections.Generic;
using Godot;

namespace Pb.Game.World;

/// <summary>
/// Paints the atlas of worn paint markings for <see cref="Markings"/>: a 4 × 4 grid of cells, white
/// on clear (each card brings its colour):
/// <list type="bullet">
/// <item>cells 0–2: a band of paint filling the cell, its edges a little ragged, worn through in scuffs (lines are cards stretched along it);</item>
/// <item>cell 3: diagonal stripes, four across, tiling (hatched areas);</item>
/// <item>cell 4: the same, two across (stripes round a column);</item>
/// <item>cells 5–14: the digits 0–9 in a heavy stencil hand.</item>
/// </list>
/// Seeded, so it's the same every run.
/// </summary>
public sealed class MarkingPainter
{
    public const int Columns = 4;

    public const int CellPx = 256;

    public const int Bands = 3, Hatch = 3, CoarseHatch = 4, FirstDigit = 5;

    /// <summary>A digit's width as a share of its height (and of its cell).</summary>
    public const float DigitAspect = 0.62f;

    private readonly byte[] _pixels;
    private readonly int _size;
    private readonly uint _seed;

    public MarkingPainter(uint seed)
    {
        _seed = seed;
        _size = CellPx * Columns;
        _pixels = new byte[_size * _size * 4];
    }

    public ImageTexture Paint()
    {
        for (int cell = 0; cell < Columns * Columns; cell++)
        {
            Func<float, float, float>? shape = cell switch
            {
                < Bands => Band,
                Hatch => (x, y) => Stripes(x, y, 4f),
                CoarseHatch => (x, y) => Stripes(x, y, 2f),
                >= FirstDigit and < FirstDigit + 10 => DigitShape(cell - FirstDigit),
                _ => null,
            };
            if (shape is not null)
            {
                Fill(cell, shape);
            }
        }

        Image image = Image.CreateFromData(_size, _size, false, Image.Format.Rgba8, _pixels);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Paints one cell: coverage from <paramref name="shape"/> (cell coordinates 0–1, y up), worn by noise seeded per cell.</summary>
    private void Fill(int cell, Func<float, float, float> shape)
    {
        uint seed = _seed + (uint)cell * 7919u;
        int x0 = cell % Columns * CellPx, y0 = cell / Columns * CellPx;
        for (int py = 0; py < CellPx; py++)
        {
            for (int px = 0; px < CellPx; px++)
            {
                float u = (px + 0.5f) / CellPx, v = 1f - (py + 0.5f) / CellPx;
                float cover = shape(u, v) * Wear(u, v, seed);
                int i = ((y0 + py) * _size + x0 + px) * 4;
                // Worn paint is a little greyer where it's thin.
                byte shade = (byte)(215 + 40 * Math.Clamp(cover, 0f, 1f));
                _pixels[i] = shade;
                _pixels[i + 1] = shade;
                _pixels[i + 2] = shade;
                _pixels[i + 3] = (byte)Math.Clamp((int)MathF.Round(cover * 255f), 0, 255);
            }
        }
    }

    /// <summary>A band across the whole cell, its long edges (top and bottom) ragged.</summary>
    private float Band(float u, float v)
    {
        float edge = 0.06f + 0.05f * Noise(u * 9f, 3.1f, _seed + 11u);
        float lower = 0.06f + 0.05f * Noise(u * 9f, 7.7f, _seed + 12u);
        return Smooth(v - lower, 0.02f) * Smooth(1f - edge - v, 0.02f);
    }

    /// <summary>Diagonal stripes, <paramref name="across"/> pairs to the cell, tiling across its edges.</summary>
    private static float Stripes(float u, float v, float across)
    {
        float t = (u + v) * across;
        float f = t - MathF.Floor(t);
        // Half of each period is paint, centred a quarter of the way in; distance to the nearest centre.
        float distance = MathF.Min(MathF.Abs(f - 0.25f), 1.25f - f);
        return Smooth(0.25f - distance, 0.03f);
    }

    /// <summary>How much paint is left: scuffed through in patches and speckled, more at random spots.</summary>
    private static float Wear(float u, float v, uint seed)
    {
        float scuff = Noise(u * 5f, v * 5f, seed) * 0.6f + Noise(u * 13f, v * 13f, seed + 1u) * 0.4f;
        float speckle = Noise(u * 60f, v * 60f, seed + 2u);
        float left = Smooth(scuff - 0.28f, 0.12f) * (speckle > 0.82f ? 0.3f : 1f);
        return Math.Clamp(left, 0f, 1f);
    }

    /// <summary>A digit drawn as thick strokes along its outline points, in a box of the cell's height and <see cref="DigitAspect"/> of its width.</summary>
    private static Func<float, float, float> DigitShape(int digit)
    {
        List<Vector2[]> strokes = DigitStrokes(digit);
        const float Width = 0.085f;
        return (u, v) =>
        {
            // Into the digit's own box: x 0–1 across, y 0–1 up, with a margin round it.
            float x = (u - 0.5f) / DigitAspect + 0.5f, y = v;
            var p = new Vector2((x - 0.1f) / 0.8f, (y - 0.08f) / 0.84f);
            float distance = float.MaxValue;
            foreach (Vector2[] stroke in strokes)
            {
                for (int i = 0; i + 1 < stroke.Length; i++)
                {
                    distance = MathF.Min(distance, SegmentDistance(p, stroke[i], stroke[i + 1]));
                }
            }

            return Smooth(Width - distance, 0.012f);
        };
    }

    /// <summary>The strokes of a digit in its box (x 0–1 right, y 0–1 up).</summary>
    private static List<Vector2[]> DigitStrokes(int digit)
    {
        static Vector2[] P(params float[] xy)
        {
            var points = new Vector2[xy.Length / 2];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
            }

            return points;
        }

        static Vector2[] Oval(float cx, float cy, float rx, float ry)
        {
            var points = new Vector2[33];
            for (int i = 0; i <= 32; i++)
            {
                float a = Mathf.Tau * i / 32f;
                points[i] = new Vector2(cx + rx * MathF.Cos(a), cy + ry * MathF.Sin(a));
            }

            return points;
        }

        return digit switch
        {
            0 => new List<Vector2[]> { Oval(0.5f, 0.5f, 0.36f, 0.47f) },
            1 => new List<Vector2[]> { P(0.56f, 0.02f, 0.56f, 0.98f, 0.28f, 0.78f), P(0.3f, 0.02f, 0.82f, 0.02f) },
            2 => new List<Vector2[]> { P(0.14f, 0.74f, 0.26f, 0.93f, 0.5f, 0.99f, 0.74f, 0.93f, 0.86f, 0.74f, 0.8f, 0.55f, 0.14f, 0.03f, 0.88f, 0.03f) },
            3 => new List<Vector2[]> { P(0.13f, 0.86f, 0.35f, 0.99f, 0.68f, 0.97f, 0.85f, 0.82f, 0.8f, 0.62f, 0.46f, 0.53f, 0.84f, 0.43f, 0.88f, 0.2f, 0.7f, 0.03f, 0.36f, 0.01f, 0.12f, 0.14f) },
            4 => new List<Vector2[]> { P(0.7f, 0.02f, 0.7f, 0.98f, 0.1f, 0.32f, 0.92f, 0.32f) },
            5 => new List<Vector2[]> { P(0.85f, 0.98f, 0.2f, 0.98f, 0.15f, 0.56f, 0.45f, 0.62f, 0.75f, 0.58f, 0.88f, 0.35f, 0.8f, 0.1f, 0.55f, 0.01f, 0.3f, 0.03f, 0.1f, 0.15f) },
            6 => new List<Vector2[]> { P(0.82f, 0.94f, 0.52f, 0.99f, 0.26f, 0.88f, 0.13f, 0.56f, 0.14f, 0.24f, 0.3f, 0.03f, 0.55f, 0.01f, 0.8f, 0.11f, 0.88f, 0.32f, 0.78f, 0.55f, 0.5f, 0.62f, 0.26f, 0.55f, 0.14f, 0.38f) },
            7 => new List<Vector2[]> { P(0.1f, 0.98f, 0.9f, 0.98f, 0.42f, 0.02f) },
            8 => new List<Vector2[]> { Oval(0.5f, 0.76f, 0.29f, 0.22f), Oval(0.5f, 0.27f, 0.35f, 0.26f) },
            _ => new List<Vector2[]> { P(0.18f, 0.06f, 0.48f, 0.01f, 0.74f, 0.12f, 0.87f, 0.44f, 0.86f, 0.76f, 0.7f, 0.97f, 0.45f, 0.99f, 0.2f, 0.89f, 0.12f, 0.68f, 0.22f, 0.45f, 0.5f, 0.38f, 0.74f, 0.45f, 0.86f, 0.62f) },
        };
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp((p - a).Dot(ab) / MathF.Max(ab.LengthSquared(), 1e-8f), 0f, 1f);
        return p.DistanceTo(a + ab * t);
    }

    private static float Smooth(float x, float width) => Mathf.Clamp(x / width + 0.5f, 0f, 1f);

    /// <summary>Smooth value noise in 0–1, tiling every 64 units so cells can repeat side by side.</summary>
    private static float Noise(float x, float y, uint seed)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed), c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    private static float Hash(int x, int y, uint seed)
    {
        uint h = (uint)(x & 63) * 374761393u + (uint)(y & 63) * 668265263u + seed * 2246822519u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
    }
}
