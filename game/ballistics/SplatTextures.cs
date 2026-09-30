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
    public static ImageTexture[] Create(int variants, int size, ulong seed)
    {
        var textures = new ImageTexture[variants];
        for (int v = 0; v < variants; v++)
        {
            textures[v] = ImageTexture.CreateFromImage(Generate(size, new Pcg32(SeedHash.Combine(seed, (ulong)v))));
        }

        return textures;
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
