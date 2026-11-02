using System;
using Godot;
using Pb.Sim.Core;

namespace Pb.Game.Ballistics;

/// <summary>
/// Procedurally generated paint-splat alpha masks (white RGB, shape in alpha). Decals tint them
/// with the team colour. No texture files needed for the greybox phase.
/// </summary>
public static class SplatTextures
{
    /// <summary>
    /// Lets go of textures made here when their owner leaves the tree, so none waits for the garbage collector past the
    /// game's quitting (whatever still uses one, a decal, keeps its own hold on it).
    /// </summary>
    public static void Release(ImageTexture[] textures)
    {
        foreach (ImageTexture texture in textures)
        {
            texture.Dispose();
        }
    }

    public static ImageTexture[] Create(int variants, int size, ulong seed)
    {
        var textures = new ImageTexture[variants];
        for (int v = 0; v < variants; v++)
        {
            // The texture keeps its own copy: the image is let go of at once rather than whenever the collector gets to it
            // (an image still waiting for it when the game quits is reported as leaked).
            using Image image = Generate(size, new Pcg32(SeedHash.Combine(seed, (ulong)v)));
            textures[v] = ImageTexture.CreateFromImage(image);
        }

        return textures;
    }

    /// <summary>
    /// Streaks: what a ball breaking at a glance leaves (Phase 5). The ball travels along +u (the texture's x): a head
    /// where it burst, a tail drawn out along its travel, narrowing, and spatter thrown on ahead of it in a fan, smaller
    /// the further it flew. The decal is stretched along the travel too.
    /// </summary>
    public static ImageTexture[] CreateStreaks(int variants, int size, ulong seed)
    {
        var textures = new ImageTexture[variants];
        for (int v = 0; v < variants; v++)
        {
            using Image image = GenerateStreak(size, new Pcg32(SeedHash.Combine(seed, (ulong)v + 101)));
            textures[v] = ImageTexture.CreateFromImage(image);
        }

        return textures;
    }

    private static Image GenerateStreak(int size, Pcg32 rng)
    {
        const int harmonics = 4;
        Span<float> amp = stackalloc float[harmonics];
        Span<float> phase = stackalloc float[harmonics];
        for (int h = 0; h < harmonics; h++)
        {
            amp[h] = rng.Range(0.01f, 0.05f) / (h + 1) * 2f;
            phase[h] = rng.Range(0f, MathF.Tau);
        }

        float headX = rng.Range(-0.2f, -0.14f), headR = rng.Range(0.13f, 0.17f), tailEnd = rng.Range(0.3f, 0.4f);
        int dropCount = 10 + (int)(rng.NextFloat() * 8);
        Span<float> dropX = stackalloc float[18];
        Span<float> dropY = stackalloc float[18];
        Span<float> dropR = stackalloc float[18];
        for (int d = 0; d < dropCount; d++)
        {
            // On ahead along the travel, in a fan that widens a little, smaller the further out.
            float along = rng.Range(0.05f, 0.47f);
            dropX[d] = along;
            dropY[d] = rng.Range(-1f, 1f) * (0.05f + 0.22f * along);
            dropR[d] = rng.Range(0.006f, 0.028f) * (1.15f - along);
        }

        var data = new byte[size * size * 4];
        float pixel = 1f / size;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) * pixel - 0.5f;
                float w = (y + 0.5f) * pixel - 0.5f;
                float du = u - headX;
                float r = MathF.Sqrt(du * du + w * w);
                float angle = MathF.Atan2(w, du);
                float edge = headR;
                for (int h = 0; h < harmonics; h++)
                {
                    edge += amp[h] * MathF.Sin((h + 2) * angle + phase[h]);
                }

                float coverage = Coverage(edge - r, pixel);

                // The tail: from the head's middle on along +u, narrowing to nothing at its end, its edge a little ragged.
                if (u > headX && u < tailEnd)
                {
                    float t = (u - headX) / (tailEnd - headX);
                    float half = headR * 0.85f * (1f - t) * (1f - 0.35f * t) + 0.01f * MathF.Sin(u * 61f + phase[0]);
                    coverage = MathF.Max(coverage, Coverage(half - MathF.Abs(w), pixel));
                }

                for (int d = 0; d < dropCount; d++)
                {
                    float dx = (u - dropX[d]) * 0.8f; // drawn out a little along the travel
                    float dy = w - dropY[d];
                    coverage = MathF.Max(coverage, Coverage(dropR[d] - MathF.Sqrt(dx * dx + dy * dy), pixel));
                }

                int o = (y * size + x) * 4;
                byte shade = (byte)(230 + 25 * Math.Clamp((edge - r) * 6f, 0f, 1f));
                data[o] = shade;
                data[o + 1] = shade;
                data[o + 2] = shade;
                data[o + 3] = (byte)(Math.Clamp(coverage, 0f, 1f) * 255f);
            }
        }

        Image image = Image.CreateFromData(size, size, false, Image.Format.Rgba8, data);
        image.GenerateMipmaps();
        return image;
    }

    private static Image Generate(int size, Pcg32 rng)
    {
        // Blob radius varies with angle (a few random harmonics); spikes and droplets add splash.
        const int harmonics = 5;
        Span<float> amp = stackalloc float[harmonics];
        Span<float> phase = stackalloc float[harmonics];
        for (int h = 0; h < harmonics; h++)
        {
            amp[h] = rng.Range(0.02f, 0.09f) / (h + 1) * 2f;
            phase[h] = rng.Range(0f, MathF.Tau);
        }

        int spikeCount = 5 + (int)(rng.NextFloat() * 5);
        Span<float> spikeAngle = stackalloc float[10];
        Span<float> spikeLength = stackalloc float[10];
        for (int s = 0; s < spikeCount; s++)
        {
            spikeAngle[s] = rng.Range(0f, MathF.Tau);
            spikeLength[s] = rng.Range(0.36f, 0.48f);
        }

        int dropCount = 6 + (int)(rng.NextFloat() * 7);
        Span<float> dropX = stackalloc float[13];
        Span<float> dropY = stackalloc float[13];
        Span<float> dropR = stackalloc float[13];
        for (int d = 0; d < dropCount; d++)
        {
            float a = rng.Range(0f, MathF.Tau);
            float r = rng.Range(0.3f, 0.46f);
            dropX[d] = MathF.Cos(a) * r;
            dropY[d] = MathF.Sin(a) * r;
            dropR[d] = rng.Range(0.012f, 0.035f);
        }

        var data = new byte[size * size * 4];
        float pixel = 1f / size;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) * pixel - 0.5f;
                float w = (y + 0.5f) * pixel - 0.5f;
                float r = MathF.Sqrt(u * u + w * w);
                float angle = MathF.Atan2(w, u);

                float edge = 0.24f;
                for (int h = 0; h < harmonics; h++)
                {
                    edge += amp[h] * MathF.Sin((h + 2) * angle + phase[h]);
                }

                float coverage = Coverage(edge - r, pixel);

                for (int s = 0; s < spikeCount; s++)
                {
                    float diff = MathF.Abs(MathF.IEEERemainder(angle - spikeAngle[s], MathF.Tau));
                    float along = r / spikeLength[s];
                    if (along < 1f)
                    {
                        float halfWidth = 0.06f * (1f - along) * (1f - along);
                        coverage = MathF.Max(coverage, Coverage(halfWidth - diff * r, pixel));
                    }
                }

                for (int d = 0; d < dropCount; d++)
                {
                    float dx = u - dropX[d];
                    float dy = w - dropY[d];
                    coverage = MathF.Max(coverage, Coverage(dropR[d] - MathF.Sqrt(dx * dx + dy * dy), pixel));
                }

                int o = (y * size + x) * 4;
                byte shade = (byte)(230 + 25 * Math.Clamp((edge - r) * 6f, 0f, 1f));
                data[o] = shade;
                data[o + 1] = shade;
                data[o + 2] = shade;
                data[o + 3] = (byte)(Math.Clamp(coverage, 0f, 1f) * 255f);
            }
        }

        Image image = Image.CreateFromData(size, size, false, Image.Format.Rgba8, data);
        image.GenerateMipmaps();
        return image;
    }

    private static float Coverage(float signedDistance, float pixel) => Math.Clamp(signedDistance / (1.5f * pixel) + 0.5f, 0f, 1f);
}
