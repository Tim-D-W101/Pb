using System;

namespace Pb.Game.Tools;

/// <summary>An RGB image with channels in 0–1, row by row from the top.</summary>
public sealed class RgbImage
{
    public RgbImage(int width, int height)
    {
        Width = width;
        Height = height;
        Data = new float[width * height * 3];
    }

    public int Width { get; }

    public int Height { get; }

    public float[] Data { get; }

    public int Index(int x, int y) => (y * Width + x) * 3;
}

/// <summary>
/// Turns a generated picture into a tiling material: it blends out the seams and derives normal and
/// roughness maps from the picture's brightness. Plain C#, no engine types, so <c>ArtImport</c> can
/// check its own work.
/// </summary>
public static class TextureMaker
{
    /// <summary>
    /// Makes <paramref name="source"/> tile. Within <paramref name="band"/> (a share of the size) of each
    /// edge, it blends in a copy shifted by half the image, whose middle has no seam. The blend keeps
    /// the picture's contrast (variance-preserving), so the bands don't look washed out. Everything
    /// outside the bands is left as it was.
    /// </summary>
    public static RgbImage MakeTileable(RgbImage source, float band)
    {
        int w = source.Width, h = source.Height;
        var mean = new float[3];
        for (int i = 0; i < source.Data.Length; i++)
        {
            mean[i % 3] += source.Data[i];
        }

        for (int c = 0; c < 3; c++)
        {
            mean[c] /= w * h;
        }

        var result = new RgbImage(w, h);
        float bx = MathF.Max(1f, band * w), by = MathF.Max(1f, band * h);
        Span<float> weights = stackalloc float[4];
        Span<int> sources = stackalloc int[4];
        for (int y = 0; y < h; y++)
        {
            // Weight of the unshifted picture: 0 on its own seam (the edge), 1 from the band inwards,
            // where the shifted copy's seam (the middle) lies.
            float ky = Smooth(MathF.Min(y + 0.5f, h - y - 0.5f) / by);
            for (int x = 0; x < w; x++)
            {
                float kx = Smooth(MathF.Min(x + 0.5f, w - x - 0.5f) / bx);
                weights[0] = kx * ky;
                weights[1] = (1f - kx) * ky;
                weights[2] = kx * (1f - ky);
                weights[3] = (1f - kx) * (1f - ky);
                sources[0] = source.Index(x, y);
                sources[1] = source.Index((x + w / 2) % w, y);
                sources[2] = source.Index(x, (y + h / 2) % h);
                sources[3] = source.Index((x + w / 2) % w, (y + h / 2) % h);
                float norm = 0f;
                foreach (float k in weights)
                {
                    norm += k * k;
                }

                norm = MathF.Sqrt(norm);
                int o = result.Index(x, y);
                for (int c = 0; c < 3; c++)
                {
                    float v = 0f;
                    for (int k = 0; k < 4; k++)
                    {
                        v += weights[k] * (source.Data[sources[k] + c] - mean[c]);
                    }

                    result.Data[o + c] = Math.Clamp(mean[c] + v / norm, 0f, 1f);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Evens out broad shading: a generated photo is often lit unevenly or vignetted, which shows as a
    /// repeating light and dark pattern once the texture tiles. Takes away <paramref name="strength"/>
    /// of the picture's broad brightness (a blur <paramref name="radius"/> pixels wide) and puts back
    /// its average, leaving the detail alone.
    /// </summary>
    public static RgbImage Flatten(RgbImage image, float strength, int radius)
    {
        int w = image.Width, h = image.Height;
        var result = new RgbImage(w, h);
        var channel = new float[w * h];
        var scratch = new float[Math.Max(w, h)];
        for (int c = 0; c < 3; c++)
        {
            float mean = 0f;
            for (int i = 0; i < channel.Length; i++)
            {
                channel[i] = image.Data[i * 3 + c];
                mean += channel[i];
            }

            mean /= channel.Length;
            // Three box blurs each way come close to a Gaussian.
            for (int pass = 0; pass < 3; pass++)
            {
                for (int y = 0; y < h; y++)
                {
                    BoxBlur(channel, y * w, 1, w, radius, scratch);
                }

                for (int x = 0; x < w; x++)
                {
                    BoxBlur(channel, x, w, h, radius, scratch);
                }
            }

            for (int i = 0; i < channel.Length; i++)
            {
                result.Data[i * 3 + c] = Math.Clamp(image.Data[i * 3 + c] + strength * (mean - channel[i]), 0f, 1f);
            }
        }

        return result;
    }

    /// <summary>Brightness (luma) of every pixel, softened by a 3 × 3 blur that wraps round the edges.</summary>
    public static float[] Height(RgbImage image)
    {
        int w = image.Width, h = image.Height;
        var luma = new float[w * h];
        for (int i = 0; i < luma.Length; i++)
        {
            luma[i] = 0.2126f * image.Data[i * 3] + 0.7152f * image.Data[i * 3 + 1] + 0.0722f * image.Data[i * 3 + 2];
        }

        var blurred = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float sum = 0f;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        sum += luma[Wrap(y + dy, h) * w + Wrap(x + dx, w)] * ((dx == 0 ? 2 : 1) * (dy == 0 ? 2 : 1));
                    }
                }

                blurred[y * w + x] = sum / 16f;
            }
        }

        return blurred;
    }

    /// <summary>
    /// A tangent-space normal map (OpenGL convention, green up, as Godot expects) from a height map, by
    /// Sobel gradients that wrap round the edges so the normals tile too.
    /// </summary>
    public static RgbImage NormalMap(float[] height, int w, int h, float strength)
    {
        var normals = new RgbImage(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float H(int dx, int dy) => height[Wrap(y + dy, h) * w + Wrap(x + dx, w)];
                float gx = (H(1, -1) + 2f * H(1, 0) + H(1, 1)) - (H(-1, -1) + 2f * H(-1, 0) + H(-1, 1));
                float gy = (H(-1, 1) + 2f * H(0, 1) + H(1, 1)) - (H(-1, -1) + 2f * H(0, -1) + H(1, -1));
                // Image rows run down and the normal's +y points up the texture.
                float nx = -gx * strength, ny = gy * strength, nz = 1f;
                float length = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                int o = normals.Index(x, y);
                normals.Data[o] = nx / length * 0.5f + 0.5f;
                normals.Data[o + 1] = ny / length * 0.5f + 0.5f;
                normals.Data[o + 2] = nz / length * 0.5f + 0.5f;
            }
        }

        return normals;
    }

    /// <summary>
    /// Roughness around <paramref name="baseRoughness"/>: darker pixels (pits, grime, cracks) rougher,
    /// lighter ones smoother, by up to <paramref name="variation"/> at two standard deviations.
    /// </summary>
    public static float[] Roughness(float[] height, float baseRoughness, float variation)
    {
        float mean = 0f;
        foreach (float v in height)
        {
            mean += v;
        }

        mean /= height.Length;
        float variance = 0f;
        foreach (float v in height)
        {
            variance += (v - mean) * (v - mean);
        }

        float std = MathF.Max(1e-4f, MathF.Sqrt(variance / height.Length));
        var roughness = new float[height.Length];
        for (int i = 0; i < height.Length; i++)
        {
            roughness[i] = Math.Clamp(baseRoughness + variation * 0.5f * (mean - height[i]) / std, 0.04f, 1f);
        }

        return roughness;
    }

    /// <summary>
    /// How much worse the wrap-round edges are than the picture inside: the mean colour step across
    /// the right/left and bottom/top edges, divided by the mean step between neighbours elsewhere.
    /// About 1 for a texture that tiles; a hard seam scores well above it.
    /// </summary>
    public static float SeamRatio(RgbImage image)
    {
        int w = image.Width, h = image.Height;
        float edge = 0f, inside = 0f;
        int edges = 0, insides = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float step = Step(image, x, y, (x + 1) % w, y) + Step(image, x, y, x, (y + 1) % h);
                if (x == w - 1 || y == h - 1)
                {
                    edge += step;
                    edges++;
                }
                else
                {
                    inside += step;
                    insides++;
                }
            }
        }

        return edge / edges / MathF.Max(1e-6f, inside / insides);
    }

    private static float Step(RgbImage image, int x0, int y0, int x1, int y1)
    {
        int a = image.Index(x0, y0), b = image.Index(x1, y1);
        return MathF.Abs(image.Data[a] - image.Data[b]) + MathF.Abs(image.Data[a + 1] - image.Data[b + 1]) + MathF.Abs(image.Data[a + 2] - image.Data[b + 2]);
    }

    /// <summary>A running-sum box blur along one row or column (clamped at its ends), in place.</summary>
    private static void BoxBlur(float[] values, int start, int stride, int count, int radius, float[] scratch)
    {
        float sum = 0f;
        for (int i = -radius; i <= radius; i++)
        {
            sum += values[start + Math.Clamp(i, 0, count - 1) * stride];
        }

        for (int i = 0; i < count; i++)
        {
            scratch[i] = sum / (2 * radius + 1);
            sum += values[start + Math.Min(i + radius + 1, count - 1) * stride] - values[start + Math.Max(i - radius, 0) * stride];
        }

        for (int i = 0; i < count; i++)
        {
            values[start + i * stride] = scratch[i];
        }
    }

    private static float Smooth(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static int Wrap(int i, int n) => ((i % n) + n) % n;
}
