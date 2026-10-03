using System;
using Godot;

namespace Pb.Game.World;

/// <summary>
/// Paints the creeper atlas for <see cref="Creepers"/>: four tall cells side by side, each a patch of
/// ivy climbing from the bottom edge: woody stems wandering up and branching, thick with leaves low
/// down and thinning towards the top, some leaves dried brown. Colour with coverage; the shader cuts
/// it out at half coverage. Clear pixels take a mid leaf colour so mipmaps don't fringe.
/// </summary>
public sealed class CreeperPainter
{
    public const int Variants = 4;

    public const int CellWidth = 256, CellHeight = 512;

    private static readonly Color Stem = new(0.27f, 0.22f, 0.16f);
    private static readonly Color[] Greens = { new(0.16f, 0.24f, 0.1f), new(0.21f, 0.3f, 0.12f), new(0.27f, 0.34f, 0.14f), new(0.13f, 0.2f, 0.09f) };
    private static readonly Color[] Browns = { new(0.42f, 0.3f, 0.16f), new(0.5f, 0.38f, 0.2f), new(0.35f, 0.26f, 0.15f) };

    private readonly float[] _rgba;
    private readonly int _width;
    private Random _random;

    public CreeperPainter(int seed)
    {
        _random = new Random(seed);
        _width = CellWidth * Variants;
        _rgba = new float[_width * CellHeight * 4];
    }

    public ImageTexture Paint()
    {
        for (int v = 0; v < Variants; v++)
        {
            int x0 = v * CellWidth;
            float dry = v == Variants - 1 ? 0.55f : 0.12f + 0.08f * v;
            int stems = _random.Next(3, 6);
            for (int s = 0; s < stems; s++)
            {
                float start = x0 + CellWidth * (0.15f + 0.7f * (s + (float)_random.NextDouble()) / stems);
                Climb(x0, start, CellHeight - 2f, R(0.5f, 0.97f) * CellHeight, R(5f, 8f), dry, depth: 0, R(-0.35f, 0.35f));
            }
        }

        var bytes = new byte[_width * CellHeight * 4];
        for (int i = 0; i < _width * CellHeight; i++)
        {
            float a = _rgba[i * 4 + 3];
            Color c = a > 0.01f ? new Color(_rgba[i * 4] / a, _rgba[i * 4 + 1] / a, _rgba[i * 4 + 2] / a) : new Color(0.2f, 0.26f, 0.12f);
            bytes[i * 4] = ToByte(c.R);
            bytes[i * 4 + 1] = ToByte(c.G);
            bytes[i * 4 + 2] = ToByte(c.B);
            bytes[i * 4 + 3] = ToByte(a);
        }

        Image image = Image.CreateFromData(_width, CellHeight, false, Image.Format.Rgba8, bytes);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// A stem from (x, y) climbing <paramref name="rise"/> pixels, wandering side to side, with leaves
    /// along it (more low down) and branches off it.
    /// </summary>
    private void Climb(int cellX, float x, float y, float rise, float width, float dry, int depth, float lean)
    {
        // Up, leaning off to one side; branches strike out sideways and slowly turn upwards.
        float heading = -Mathf.Pi / 2f + lean;
        float pull = depth == 0 ? 0.06f : 0.03f;
        float top = MathF.Max(8f, y - rise);
        float step = 4f;
        float sinceLeaf = 0f, sinceBranch = 0f;
        while (y > top && x > cellX + 10f && x < cellX + CellWidth - 10f)
        {
            float nx = x + MathF.Cos(heading) * step, ny = y + MathF.Sin(heading) * step;
            float t = (CellHeight - ny) / CellHeight;
            Line(x, y, nx, ny, width * (1f - 0.6f * t), Stem);
            x = nx;
            y = ny;
            heading += R(-0.22f, 0.22f) + (-Mathf.Pi / 2f - heading) * pull;
            sinceLeaf += step;
            sinceBranch += step;
            float spacing = Mathf.Lerp(5f, 14f, t);
            if (sinceLeaf > spacing)
            {
                sinceLeaf = 0f;
                for (int k = 0; k < (t < 0.4f ? 3 : t < 0.7f ? 2 : 1); k++)
                {
                    float side = _random.Next(2) == 0 ? -1f : 1f;
                    float size = R(13f, 23f) * (1f - 0.35f * t) * (depth > 0 ? 0.85f : 1f);
                    Color leaf = (float)_random.NextDouble() < dry ? Browns[_random.Next(Browns.Length)] : Greens[_random.Next(Greens.Length)];
                    Leaf(x + side * R(3f, 9f), y + R(-4f, 4f), size, heading + side * R(0.6f, 1.6f), leaf * R(0.85f, 1.15f));
                }
            }

            if (depth < 2 && sinceBranch > R(35f, 80f))
            {
                sinceBranch = 0f;
                float branchRise = (y - top) * R(0.3f, 0.8f);
                float off = (_random.Next(2) == 0 ? -1f : 1f) * R(0.6f, 1.2f);
                Climb(cellX, x, y, branchRise, width * 0.6f, dry, depth + 1, heading + Mathf.Pi / 2f + off);
            }
        }
    }

    /// <summary>An ivy leaf: three to five pointed lobes round a heart, on a short stalk, turned to <paramref name="angle"/>.</summary>
    private void Leaf(float cx, float cy, float size, float angle, Color color)
    {
        int lobes = _random.Next(3, 6);
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        float reach = size * 1.15f;
        int x0 = (int)(cx - reach), x1 = (int)(cx + reach), y0 = (int)(cy - reach), y1 = (int)(cy + reach);
        for (int py = Math.Max(0, y0); py <= Math.Min(CellHeight - 1, y1); py++)
        {
            for (int px = Math.Max(0, x0); px <= Math.Min(_width - 1, x1); px++)
            {
                float dx = px + 0.5f - cx, dy = py + 0.5f - cy;
                // In the leaf's frame: u out along the midrib, w across it.
                float u = dx * c + dy * s, w = -dx * s + dy * c;
                float a = MathF.Atan2(w, u);
                float lobe = 0.62f + 0.38f * MathF.Pow(MathF.Abs(MathF.Cos(a * lobes * 0.5f)), 0.6f);
                float r = MathF.Sqrt(u * u + w * w) / (size * 0.55f * lobe);
                float cover = Mathf.Clamp((1f - r) * size * 0.4f, 0f, 1f);
                if (cover <= 0f)
                {
                    continue;
                }

                // Veins and a lighter middle.
                float vein = MathF.Abs(MathF.Sin(a * lobes * 0.5f)) < 0.08f ? 1.2f : 1f;
                Over(px, py, color * (vein * (1.05f - 0.25f * r)), cover);
            }
        }
    }

    private void Line(float x0, float y0, float x1, float y1, float width, Color color)
    {
        float half = MathF.Max(width * 0.5f, 0.6f);
        int minX = (int)(MathF.Min(x0, x1) - half - 1f), maxX = (int)(MathF.Max(x0, x1) + half + 1f);
        int minY = (int)(MathF.Min(y0, y1) - half - 1f), maxY = (int)(MathF.Max(y0, y1) + half + 1f);
        float dx = x1 - x0, dy = y1 - y0, length2 = MathF.Max(dx * dx + dy * dy, 1e-4f);
        for (int py = Math.Max(0, minY); py <= Math.Min(CellHeight - 1, maxY); py++)
        {
            for (int px = Math.Max(0, minX); px <= Math.Min(_width - 1, maxX); px++)
            {
                float t = Mathf.Clamp(((px + 0.5f - x0) * dx + (py + 0.5f - y0) * dy) / length2, 0f, 1f);
                float ex = px + 0.5f - (x0 + dx * t), ey = py + 0.5f - (y0 + dy * t);
                float cover = Mathf.Clamp(half - MathF.Sqrt(ex * ex + ey * ey) + 0.5f, 0f, 1f);
                if (cover > 0f)
                {
                    Over(px, py, color, cover);
                }
            }
        }
    }

    /// <summary>Lays premultiplied colour over what's there.</summary>
    private void Over(int px, int py, Color color, float cover)
    {
        int i = (py * _width + px) * 4;
        float keep = 1f - cover;
        _rgba[i] = color.R * cover + _rgba[i] * keep;
        _rgba[i + 1] = color.G * cover + _rgba[i + 1] * keep;
        _rgba[i + 2] = color.B * cover + _rgba[i + 2] * keep;
        _rgba[i + 3] = cover + _rgba[i + 3] * keep;
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    private float R(float a, float b) => a + (float)_random.NextDouble() * (b - a);
}
