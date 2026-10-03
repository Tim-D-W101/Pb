using System;
using System.Collections.Generic;
using Godot;

namespace Pb.Game.World;

/// <summary>
/// Paints the graffiti once at load (<see cref="Graffiti"/>): an atlas of 4 × 4 cells, each twice as wide
/// as it is tall. The top two rows are throw-ups: a word in fat, overlapping bubble letters, each letter
/// tipped and bobbing off the line, filled with a colour fading down, outlined, shaded below and to the
/// right, with a shine along their top-left edges and paint run down from them. The bottom two are tags:
/// a word scrawled in one thin, slanting line, joined up and underlined with a flourish, with a drip or
/// two. Words come from the data (invented ones, so nothing is anyone's mark); letters are the stencil
/// strokes (<see cref="MarkingPainter"/>) drawn freehand.
/// </summary>
public sealed class GraffitiPainter
{
    public const int Columns = 4, Rows = 4, CellWidth = 512, CellHeight = 256;

    private const int Width = Columns * CellWidth, Height = Rows * CellHeight;

    private readonly Random _random;
    private readonly string[] _words;
    private readonly Color[] _fills;
    private readonly Color[] _outlines;
    private readonly byte[] _rgba = new byte[Width * Height * 4];

    public GraffitiPainter(int seed, string[] words, Color[] fills, Color[] outlines)
    {
        _random = new Random(seed);
        _words = words;
        _fills = fills;
        _outlines = outlines;
    }

    /// <summary>Whether a cell holds a throw-up (the top two rows) rather than a tag.</summary>
    public static bool IsThrowUp(int cell) => cell < Columns * 2;

    public ImageTexture Paint()
    {
        for (int cell = 0; cell < Columns * Rows; cell++)
        {
            string word = _words[_random.Next(_words.Length)];
            if (IsThrowUp(cell))
            {
                ThrowUp(cell, word);
            }
            else
            {
                Tag(cell, word);
            }
        }

        Image image = Image.CreateFromData(Width, Height, false, Image.Format.Rgba8, _rgba);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private float R(float a, float b) => a + (float)_random.NextDouble() * (b - a);

    /// <summary>
    /// A word's strokes laid out in a cell (pixels, y down): each letter in its own box, tipped and bobbing,
    /// sheared for a slant; a freehand hand wobbles each point by up to <paramref name="wobble"/> (px) and
    /// rounds the corners off (<paramref name="round"/> passes of corner cutting).
    /// </summary>
    private List<(Vector2 A, Vector2 B)> Lay(string word, float boxHeight, float aspect, float advance, float tip, float bob, float shear, float wobble = 0f, int round = 0)
    {
        var segments = new List<(Vector2 A, Vector2 B)>();
        float boxWidth = boxHeight * aspect;
        float total = boxWidth + (word.Length - 1) * boxWidth * advance;
        float x0 = (CellWidth - total) * 0.5f;
        for (int i = 0; i < word.Length; i++)
        {
            char c = char.ToUpperInvariant(word[i]);
            if (c < 'A' || c > 'Z')
            {
                continue;
            }

            float scale = R(0.9f, 1.1f);
            float angle = R(-tip, tip);
            var centre = new Vector2(x0 + boxWidth * 0.5f + i * boxWidth * advance, CellHeight * 0.5f + R(-bob, bob) * boxHeight);
            var turn = new Transform2D(angle, Vector2.Zero);
            foreach (Vector2[] stroke in MarkingPainter.LetterStrokes(c))
            {
                // Unit box (y up) to pixels (y down) round the letter's centre, sheared for a slant.
                var points = new List<Vector2>(stroke.Length);
                foreach (Vector2 p in stroke)
                {
                    var local = new Vector2((p.X - 0.5f) * boxWidth, (0.5f - p.Y) * boxHeight) * scale;
                    local.X -= local.Y * shear;
                    points.Add(centre + turn * local + new Vector2(R(-wobble, wobble), R(-wobble, wobble)));
                }

                for (int pass = 0; pass < round && points.Count > 2; pass++)
                {
                    var cut = new List<Vector2> { points[0] };
                    for (int k = 0; k + 1 < points.Count; k++)
                    {
                        cut.Add(points[k].Lerp(points[k + 1], 0.25f));
                        cut.Add(points[k].Lerp(points[k + 1], 0.75f));
                    }

                    cut.Add(points[^1]);
                    points = cut;
                }

                for (int k = 0; k + 1 < points.Count; k++)
                {
                    segments.Add((points[k], points[k + 1]));
                }
            }
        }

        return segments;
    }

    /// <summary>The distance (px) from each pixel of a cell to the nearest stroke, out to <paramref name="reach"/>.</summary>
    private static float[] Distances(List<(Vector2 A, Vector2 B)> segments, float reach)
    {
        var distance = new float[CellWidth * CellHeight];
        Array.Fill(distance, float.MaxValue);
        foreach ((Vector2 a, Vector2 b) in segments)
        {
            int x0 = Math.Max(0, (int)(MathF.Min(a.X, b.X) - reach)), x1 = Math.Min(CellWidth - 1, (int)(MathF.Max(a.X, b.X) + reach) + 1);
            int y0 = Math.Max(0, (int)(MathF.Min(a.Y, b.Y) - reach)), y1 = Math.Min(CellHeight - 1, (int)(MathF.Max(a.Y, b.Y) + reach) + 1);
            Vector2 ab = b - a;
            float length2 = MathF.Max(ab.LengthSquared(), 1e-6f);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float t = Math.Clamp((p - a).Dot(ab) / length2, 0f, 1f);
                    float d = p.DistanceTo(a + ab * t);
                    int i = y * CellWidth + x;
                    if (d < distance[i])
                    {
                        distance[i] = d;
                    }
                }
            }
        }

        return distance;
    }

    private static float Inside(float[] distance, int x, int y, float radius)
    {
        if (x < 0 || y < 0 || x >= CellWidth || y >= CellHeight)
        {
            return 0f;
        }

        return Math.Clamp(radius - distance[y * CellWidth + x] + 0.5f, 0f, 1f);
    }

    /// <summary>Fat bubble letters: shadow, outline, a fill fading down, a shine along the top-left edges, and runs.</summary>
    private void ThrowUp(int cell, string word)
    {
        float box = CellHeight * 0.6f;
        float fat = box * R(0.1f, 0.13f), rim = fat + box * 0.045f;
        List<(Vector2 A, Vector2 B)> segments = Lay(word, box, R(0.62f, 0.72f), R(0.72f, 0.82f), 0.14f, 0.06f, R(-0.05f, 0.15f), round: 1);
        float[] distance = Distances(segments, rim + 8f);
        Color fill = _fills[_random.Next(_fills.Length)];
        Color fillLow = fill.Darkened(R(0.15f, 0.35f));
        Color outline = _outlines[_random.Next(_outlines.Length)];
        var shadow = new Color(0.05f, 0.05f, 0.06f);
        var shine = new Color(1f, 1f, 0.97f);
        var colour = new Color[CellWidth * CellHeight];
        var alpha = new float[CellWidth * CellHeight];
        (int sx, int sy) = (6, 7);
        for (int y = 0; y < CellHeight; y++)
        {
            for (int x = 0; x < CellWidth; x++)
            {
                int i = y * CellWidth + x;
                float back = Inside(distance, x - sx, y - sy, rim) * 0.6f;
                float edge = Inside(distance, x, y, rim);
                float body = Inside(distance, x, y, fat);
                float gleam = body * (1f - Inside(distance, x - 5, y - 5, fat)) * 0.85f;
                Color c = shadow;
                float a = back;
                c = c.Lerp(outline, edge);
                a = MathF.Max(a, edge);
                Color inner = fill.Lerp(fillLow, Math.Clamp((float)y / CellHeight * 1.4f - 0.2f, 0f, 1f));
                c = c.Lerp(inner, body);
                c = c.Lerp(shine, gleam);
                colour[i] = c;
                alpha[i] = a;
            }
        }

        Drips(colour, alpha, distance, fat, fill, R(3f, 8f));
        Store(cell, colour, alpha);
    }

    /// <summary>A tag: one thin slanting line, joined up, underlined with a flourish, with a drip or two.</summary>
    private void Tag(int cell, string word)
    {
        float box = CellHeight * R(0.42f, 0.55f);
        float pen = R(3.5f, 5.5f);
        List<(Vector2 A, Vector2 B)> segments = Lay(word, box, R(0.5f, 0.6f), R(0.62f, 0.75f), 0.08f, 0.04f, R(0.25f, 0.45f), wobble: box * 0.03f, round: 2);
        // Joined up: a stroke from each letter's last point to the next one's first, and a swoosh under it all.
        float left = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
        foreach ((Vector2 a, Vector2 b) in segments)
        {
            left = MathF.Min(left, MathF.Min(a.X, b.X));
            right = MathF.Max(right, MathF.Max(a.X, b.X));
            bottom = MathF.Max(bottom, MathF.Max(a.Y, b.Y));
        }

        var swoosh = new List<Vector2>();
        float lift = R(6f, 16f), dip = R(8f, 18f);
        for (int k = 0; k <= 20; k++)
        {
            float t = k / 20f;
            float x = Mathf.Lerp(left - 10f, right + 30f, t);
            float y = bottom + lift + MathF.Sin(t * MathF.PI) * dip - t * t * R(18f, 26f);
            swoosh.Add(new Vector2(x, y));
        }

        for (int k = 0; k + 1 < swoosh.Count; k++)
        {
            segments.Add((swoosh[k], swoosh[k + 1]));
        }

        float[] distance = Distances(segments, pen + 4f);
        Color ink = _random.NextDouble() < 0.5 ? _outlines[_random.Next(_outlines.Length)] : _fills[_random.Next(_fills.Length)];
        var colour = new Color[CellWidth * CellHeight];
        var alpha = new float[CellWidth * CellHeight];
        for (int i = 0; i < colour.Length; i++)
        {
            int x = i % CellWidth, y = i / CellWidth;
            colour[i] = ink;
            alpha[i] = Inside(distance, x, y, pen);
        }

        Drips(colour, alpha, distance, pen, ink, R(1f, 3f));
        Store(cell, colour, alpha);
    }

    /// <summary>Paint run down from the bottom edges of the letters: thin lines ending in a bead.</summary>
    private void Drips(Color[] colour, float[] alpha, float[] distance, float radius, Color paint, float count)
    {
        int drips = (int)count;
        for (int k = 0, tries = 0; k < drips && tries < 400; tries++)
        {
            int x = _random.Next(CellWidth), y = _random.Next(CellHeight);
            // A bottom edge: inside the paint, with no paint a few pixels below.
            if (Inside(distance, x, y, radius) < 1f || Inside(distance, x, y + 3, radius) > 0f)
            {
                continue;
            }

            int length = _random.Next(10, 44);
            float width = MathF.Max(1.2f, radius * 0.18f);
            for (int d = 0; d < length && y + d < CellHeight; d++)
            {
                float w = width * (1f - 0.3f * d / length) + (d > length - 4 ? 1.2f : 0f);
                for (int dx = (int)-w - 1; dx <= (int)w + 1; dx++)
                {
                    int px = x + dx;
                    if (px < 0 || px >= CellWidth)
                    {
                        continue;
                    }

                    float a = Math.Clamp(w + 0.5f - MathF.Abs(dx), 0f, 1f);
                    int i = (y + d) * CellWidth + px;
                    colour[i] = colour[i].Lerp(paint, a);
                    alpha[i] = MathF.Max(alpha[i], a);
                }
            }

            k++;
        }
    }

    private void Store(int cell, Color[] colour, float[] alpha)
    {
        int ox = cell % Columns * CellWidth, oy = cell / Columns * CellHeight;
        for (int y = 0; y < CellHeight; y++)
        {
            for (int x = 0; x < CellWidth; x++)
            {
                int i = y * CellWidth + x;
                int o = ((oy + y) * Width + ox + x) * 4;
                Color c = colour[i];
                _rgba[o] = (byte)(Math.Clamp(c.R, 0f, 1f) * 255f);
                _rgba[o + 1] = (byte)(Math.Clamp(c.G, 0f, 1f) * 255f);
                _rgba[o + 2] = (byte)(Math.Clamp(c.B, 0f, 1f) * 255f);
                _rgba[o + 3] = (byte)(Math.Clamp(alpha[i], 0f, 1f) * 255f);
            }
        }
    }
}
