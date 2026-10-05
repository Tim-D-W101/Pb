using System;
using Godot;

namespace Pb.Game.World;

/// <summary>
/// Paints the damp once at load (<see cref="Damp"/>): a band of rising damp that repeats along a wall
/// (a wavy tide mark with a crust of salt, older tide marks under it, the stain darkening towards the
/// floor), a band of splash-back for the foot of outside walls, and an atlas of 2 × 2 cells: two patches
/// of black mould spreading from a corner (the cell's top left) and two brown water stains, ringed where
/// the water stopped spreading each time.
/// </summary>
public sealed class DampPainter
{
    public const int CellPx = 512;

    /// <summary>The band is two units wide for one unit tall, and repeats across its width.</summary>
    public const int BandWidth = 1024, BandHeight = 512;

    /// <summary>The splash-back is four units wide for one unit tall, and repeats across its width.</summary>
    public const int SplashWidth = 1024, SplashHeight = 256;

    private readonly int _seed;

    public DampPainter(int seed)
    {
        _seed = seed;
    }

    /// <summary>The rising damp band (sRGB, colour and alpha), with its top at v = 0 and the floor at v = 1.</summary>
    public ImageTexture Band()
    {
        var rgba = new byte[BandWidth * BandHeight * 4];
        // Dark, so the stain darkens any wall it's on; the salt light, so it shows on any wall too.
        var salt = new Color("#f2eee2");
        var edge = new Color("#2a1f13");
        var stain = new Color("#3b2f20");
        var wet = new Color("#231a10");
        for (int x = 0; x < BandWidth; x++)
        {
            float u = (float)x / BandWidth;
            // Each tide mark wavers along the wall, repeating with the band.
            float t0 = 0.12f + 0.05f * Periodic(u, 5, 1) + 0.025f * Periodic(u, 17, 2) + 0.01f * Periodic(u, 53, 3);
            float t1 = t0 + 0.2f + 0.05f * Periodic(u, 7, 4) + 0.02f * Periodic(u, 23, 5);
            float t2 = t1 + 0.22f + 0.05f * Periodic(u, 9, 6);
            for (int y = 0; y < BandHeight; y++)
            {
                float v = (float)y / BandHeight;
                float mottle = 0.5f + 0.5f * (0.6f * Periodic2(u, v, 11, 7) + 0.4f * Periodic2(u, v, 37, 8));
                Color c = stain;
                float a = 0f;
                if (v >= t0)
                {
                    // The stain under the top mark, patchy, darker and wetter towards the floor.
                    float depth = v - t0;
                    a = 0.34f + 0.2f * mottle + 0.3f * Smooth((v - 0.6f) / 0.4f);
                    c = stain.Lerp(wet, Smooth((v - 0.6f) / 0.4f));
                    // A brown edge just under the top mark, and fainter ones under the older marks.
                    float rim = MathF.Pow(1f - Math.Clamp(depth / 0.06f, 0f, 1f), 1.6f);
                    float older = Band(v, t1, 0.03f) * 0.6f + Band(v, t2, 0.03f) * 0.45f;
                    float dark = MathF.Max(rim, older);
                    c = c.Lerp(edge, dark);
                    a = MathF.Max(a, 0.85f * rim + 0.4f * older * (0.6f + 0.4f * mottle));
                }

                // A crust of salt along each mark, broken in places.
                float crust = Band(v, t0 - 0.008f, 0.024f) * (0.6f + 0.4f * Periodic(u, 61, 9)) + Band(v, t1 - 0.005f, 0.012f) * 0.5f;
                if (crust > 0.05f)
                {
                    c = c.Lerp(salt, Math.Clamp(crust * 1.3f, 0f, 1f));
                    a = MathF.Max(a, 0.85f * crust);
                }

                int i = (y * BandWidth + x) * 4;
                rgba[i] = (byte)(Math.Clamp(c.R, 0f, 1f) * 255f);
                rgba[i + 1] = (byte)(Math.Clamp(c.G, 0f, 1f) * 255f);
                rgba[i + 2] = (byte)(Math.Clamp(c.B, 0f, 1f) * 255f);
                rgba[i + 3] = (byte)(Math.Clamp(a, 0f, 1f) * 255f);
            }
        }

        Image image = Image.CreateFromData(BandWidth, BandHeight, false, Image.Format.Rgba8, rgba);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// Splash-back (sRGB, colour and alpha, four units wide for one tall, repeating across): the grime
    /// rain splashes up the foot of an outside wall, rising to a soft, wavering top at v ≈ 0.15–0.3,
    /// streaked where water has run down it, specked with splashes and darkest at the ground (v = 1).
    /// </summary>
    public ImageTexture Splash()
    {
        const int width = SplashWidth, height = SplashHeight;
        var alpha = new float[width * height];
        var dirt = new Color("#3d3427");
        var foot = new Color("#241d14");
        for (int x = 0; x < width; x++)
        {
            float u = (float)x / width;
            float top = 0.2f + 0.08f * Periodic(u, 9, 21) + 0.05f * Periodic(u, 29, 22) + 0.025f * Periodic(u, 83, 23);
            float streak = 0.5f + 0.5f * Periodic(u, 157, 24);
            for (int y = 0; y < height; y++)
            {
                float v = (float)y / height;
                float soft = Smooth((v - top + 0.06f) / 0.22f);
                float t = Math.Clamp((v - top) / (1f - top), 0f, 1f);
                float mottle = 0.5f + 0.5f * Periodic2(u, v, 31, 25);
                alpha[y * width + x] = soft * (0.16f + 0.42f * MathF.Pow(t, 1.4f) + 0.14f * streak * t + 0.1f * mottle);
            }
        }

        // Splashes: dots thickest low down, wrapping round so the band still repeats.
        var random = new Random(_seed + 9);
        for (int k = 0; k < 2600; k++)
        {
            float cx = (float)random.NextDouble() * width;
            float cy = height * (1f - 0.75f * MathF.Pow((float)random.NextDouble(), 1.8f));
            float radius = 0.8f + (float)random.NextDouble() * 2.2f;
            float strength = 0.25f + 0.35f * (float)random.NextDouble();
            for (int y = (int)(cy - radius - 1); y <= (int)(cy + radius + 1); y++)
            {
                for (int xi = (int)(cx - radius - 1); xi <= (int)(cx + radius + 1); xi++)
                {
                    if (y < 0 || y >= height)
                    {
                        continue;
                    }

                    float d = MathF.Sqrt((xi - cx) * (xi - cx) + (y - cy) * (y - cy));
                    int i = y * width + ((xi % width) + width) % width;
                    alpha[i] = MathF.Min(1f, alpha[i] + strength * Math.Clamp(radius + 0.5f - d, 0f, 1f));
                }
            }
        }

        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color c = dirt.Lerp(foot, Smooth(((float)y / height - 0.55f) / 0.45f));
                Put(rgba, width, x, y, c, alpha[y * width + x]);
            }
        }

        Image image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>The atlas: mould in cells 0 and 1 (spreading from the top-left corner), water stains in 2 and 3.</summary>
    public ImageTexture Atlas()
    {
        const int size = CellPx * 2;
        var rgba = new byte[size * size * 4];
        Mould(rgba, size, 0, 0, new Random(_seed + 1), new Color("#1b2117"));
        Mould(rgba, size, CellPx, 0, new Random(_seed + 2), new Color("#22231a"));
        Stain(rgba, size, 0, CellPx, new Random(_seed + 3), 1f);
        Stain(rgba, size, CellPx, CellPx, new Random(_seed + 4), 0.7f);
        Image image = Image.CreateFromData(size, size, false, Image.Format.Rgba8, rgba);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Black mould: a haze and thousands of specks, thickest in the corner and thinning out.</summary>
    private static void Mould(byte[] rgba, int stride, int ox, int oy, Random random, Color color)
    {
        var alpha = new float[CellPx * CellPx];
        float reach = CellPx * 0.95f;
        for (int y = 0; y < CellPx; y++)
        {
            for (int x = 0; x < CellPx; x++)
            {
                float r = MathF.Sqrt(x * x + y * y) / reach;
                float n = 0.5f + 0.5f * Periodic2(x / (float)CellPx, y / (float)CellPx, 6, 31);
                alpha[y * CellPx + x] = MathF.Max(0f, 0.32f * MathF.Pow(MathF.Max(0f, 1f - r), 2.2f) * (0.5f + n));
            }
        }

        for (int k = 0; k < 4200; k++)
        {
            // Specks thick near the corner, spreading out along the two edges.
            float angle = (float)random.NextDouble() * MathF.PI * 0.5f;
            float r = reach * MathF.Pow((float)random.NextDouble(), 1.7f) * (0.75f + 0.25f * MathF.Abs(MathF.Cos(angle * 2f)));
            float cx = MathF.Cos(angle) * r, cy = MathF.Sin(angle) * r;
            float radius = 1.2f + (float)random.NextDouble() * 5f * (1f - r / reach);
            float strength = 0.5f + 0.45f * (float)random.NextDouble();
            for (int y = (int)(cy - radius - 1); y <= (int)(cy + radius + 1); y++)
            {
                for (int x = (int)(cx - radius - 1); x <= (int)(cx + radius + 1); x++)
                {
                    if (x < 0 || y < 0 || x >= CellPx || y >= CellPx)
                    {
                        continue;
                    }

                    float d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float a = strength * Math.Clamp(radius + 0.5f - d, 0f, 1f);
                    alpha[y * CellPx + x] = MathF.Max(alpha[y * CellPx + x], a);
                }
            }
        }

        for (int y = 0; y < CellPx; y++)
        {
            for (int x = 0; x < CellPx; x++)
            {
                // Clear at the cell's far edges, so nothing shows a border.
                float edge = Smooth((CellPx - 1 - x) / 24f) * Smooth((CellPx - 1 - y) / 24f);
                Put(rgba, stride, ox + x, oy + y, color, alpha[y * CellPx + x] * edge);
            }
        }
    }

    /// <summary>A water stain: a pale brown blot ringed darker where the water stopped spreading each time.</summary>
    private static void Stain(byte[] rgba, int stride, int ox, int oy, Random random, float squash)
    {
        var fill = new Color("#7a5a30");
        var ring = new Color("#3e2a14");
        int seed = random.Next(1000);
        var rings = new[] { 1f, 0.84f - 0.05f * (float)random.NextDouble(), 0.66f - 0.06f * (float)random.NextDouble(), 0.45f };
        for (int y = 0; y < CellPx; y++)
        {
            for (int x = 0; x < CellPx; x++)
            {
                float dx = (x + 0.5f) / CellPx - 0.5f, dy = ((y + 0.5f) / CellPx - 0.5f) / squash;
                float r = MathF.Sqrt(dx * dx + dy * dy);
                float angle = (MathF.Atan2(dy, dx) + MathF.PI) / (2f * MathF.PI);
                float bound = 0.42f * (1f + 0.16f * Periodic(angle, 5, seed) + 0.07f * Periodic(angle, 13, seed + 1) + 0.03f * Periodic(angle, 37, seed + 2));
                float s = r / bound;
                if (s > 1.03f)
                {
                    continue;
                }

                float mottle = 0.5f + 0.5f * Periodic2(x / (float)CellPx, y / (float)CellPx, 9, seed + 3);
                float a = s <= 1f ? 0.14f + 0.1f * mottle + 0.08f * s : 0f;
                Color c = fill;
                for (int k = 0; k < rings.Length; k++)
                {
                    // Each ring wavers a little on its own, sharp outside and soft inside.
                    float at = rings[k] * (1f + 0.025f * Periodic(angle, 11 + k * 6, seed + 10 + k));
                    float width = 0.035f - 0.006f * k;
                    float t = s > at ? Math.Clamp(1f - (s - at) / 0.006f, 0f, 1f) : MathF.Pow(Math.Clamp(1f - (at - s) / width, 0f, 1f), 2f);
                    float strength = (0.55f - 0.1f * k) * t;
                    if (strength > 0f)
                    {
                        c = c.Lerp(ring, t);
                        a = MathF.Max(a, strength);
                    }
                }

                Put(rgba, stride, ox + x, oy + y, c, a);
            }
        }
    }

    private static void Put(byte[] rgba, int stride, int x, int y, Color c, float a)
    {
        int i = (y * stride + x) * 4;
        rgba[i] = (byte)(Math.Clamp(c.R, 0f, 1f) * 255f);
        rgba[i + 1] = (byte)(Math.Clamp(c.G, 0f, 1f) * 255f);
        rgba[i + 2] = (byte)(Math.Clamp(c.B, 0f, 1f) * 255f);
        rgba[i + 3] = (byte)(Math.Clamp(a, 0f, 1f) * 255f);
    }

    /// <summary>1 on a line at <paramref name="at"/>, falling to 0 a <paramref name="width"/> either side.</summary>
    private static float Band(float v, float at, float width) => MathF.Max(0f, 1f - MathF.Abs(v - at) / width);

    private static float Smooth(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Smooth noise in −1 to 1 along <paramref name="u"/> (0–1) that repeats with it, <paramref name="cells"/> features across.</summary>
    private static float Periodic(float u, int cells, int seed)
    {
        float x = u * cells;
        int i = (int)MathF.Floor(x);
        float f = Smooth(x - i);
        float a = CrackNetwork.Hash01(((i % cells) + cells) % cells, seed, 0xDA3Bu);
        float b = CrackNetwork.Hash01((((i + 1) % cells) + cells) % cells, seed, 0xDA3Bu);
        return (a + (b - a) * f) * 2f - 1f;
    }

    /// <summary>Smooth 2D noise in −1 to 1 over the unit square that repeats across it, <paramref name="cells"/> features each way.</summary>
    private static float Periodic2(float u, float v, int cells, int seed)
    {
        float x = u * cells, y = v * cells;
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = Smooth(x - ix), fy = Smooth(y - iy);
        int x0 = ((ix % cells) + cells) % cells, x1 = (x0 + 1) % cells;
        int y0 = ((iy % cells) + cells) % cells, y1 = (y0 + 1) % cells;
        float h(int a, int b) => CrackNetwork.Hash01(a + b * 977, seed, 0x2D7Fu);
        float top = h(x0, y0) + (h(x1, y0) - h(x0, y0)) * fx;
        float bottom = h(x0, y1) + (h(x1, y1) - h(x0, y1)) * fx;
        return (top + (bottom - top) * fy) * 2f - 1f;
    }
}
