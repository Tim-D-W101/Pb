using System;
using Godot;
using Pb.Sim.Core;

namespace Pb.Game.Audio;

/// <summary>
/// Placeholder sound effects synthesised at startup (no audio files in the greybox phase).
/// The Phase 5 audio pass replaces these with recorded, per-surface sets.
/// </summary>
public static class ProceduralSounds
{
    private const int Rate = 22050;

    public static AudioStreamWav Shot() => Build(0.14f, 11, (t, rng) =>
        Noise(rng) * MathF.Exp(-t / 0.012f) * 0.8f +
        MathF.Sin(MathF.Tau * 150f * t) * MathF.Exp(-t / 0.035f) * 0.7f +
        MathF.Sin(MathF.Tau * 900f * t) * MathF.Exp(-t / 0.004f) * 0.4f);

    public static AudioStreamWav Splat() => Build(0.22f, 12, (t, rng, state) =>
    {
        state = state * 0.8f + Noise(rng) * 0.2f; // low-passed noise: wet thud
        return (state * 2.2f * MathF.Exp(-t / 0.04f) + MathF.Sin(MathF.Tau * (380f - 700f * t) * t) * 0.35f * MathF.Exp(-t / 0.05f), state);
    });

    public static AudioStreamWav Bounce() => Build(0.08f, 13, (t, rng) =>
        MathF.Sin(MathF.Tau * 430f * t) * MathF.Exp(-t / 0.012f) * 0.6f + Noise(rng) * 0.25f * MathF.Exp(-t / 0.005f));

    public static AudioStreamWav DryFire() => Build(0.04f, 14, (t, rng) =>
        Noise(rng) * MathF.Exp(-t / 0.002f) * 0.7f + MathF.Sin(MathF.Tau * 2400f * t) * MathF.Exp(-t / 0.004f) * 0.3f);

    public static AudioStreamWav Refill()
    {
        var clicks = new float[10];
        var rng = new Pcg32(15);
        for (int i = 0; i < clicks.Length; i++)
        {
            clicks[i] = rng.Range(0f, 0.55f);
        }

        return Build(0.6f, 16, (t, r) =>
        {
            float sample = 0f;
            foreach (float c in clicks)
            {
                float dt = t - c;
                if (dt >= 0f && dt < 0.03f)
                {
                    sample += Noise(r) * MathF.Exp(-dt / 0.004f) * 0.35f;
                }
            }

            return sample;
        });
    }

    /// <summary>A hinge creaking: stick-slip squeaks, their rate wandering as the leaf swings, swelling and fading.</summary>
    public static AudioStreamWav DoorCreak()
    {
        float phase = 0f, ring = 0f, ringPhase = 0f;
        return Build(0.75f, 17, (t, rng, state) =>
        {
            float rate = 230f + 110f * MathF.Sin(MathF.Tau * 1.3f * t + 0.8f * MathF.Sin(MathF.Tau * 0.45f * t));
            phase += rate / Rate;
            if (phase >= 1f)
            {
                phase -= 1f;
                ring = 0.55f + 0.45f * rng.NextFloat();
            }

            ring *= MathF.Exp(-1f / (Rate * 0.0035f));
            ringPhase += 1450f / Rate;
            float envelope = MathF.Sin(MathF.PI * MathF.Min(1f, t / 0.75f));
            state = state * 0.97f + Noise(rng) * 0.03f;
            return ((ring * MathF.Sin(MathF.Tau * ringPhase) * 0.5f + state * 0.6f) * envelope, state);
        });
    }

    /// <summary>A wooden door banging shut: a low thump, the latch's click and a rattle.</summary>
    public static AudioStreamWav DoorSlam() => Build(0.5f, 18, (t, rng, state) =>
    {
        state = state * 0.85f + Noise(rng) * 0.15f;
        float thump = MathF.Sin(MathF.Tau * (85f - 30f * t) * t) * MathF.Exp(-t / 0.07f);
        float click = t > 0.012f ? Noise(rng) * MathF.Exp(-(t - 0.012f) / 0.003f) * 0.5f : 0f;
        return (thump * 0.9f + state * 1.6f * MathF.Exp(-t / 0.035f) + click, state);
    });

    /// <summary>A steel door clanging shut: a struck sheet's clashing partials over the thump.</summary>
    public static AudioStreamWav DoorClank() => Build(0.8f, 19, (t, rng, state) =>
    {
        state = state * 0.8f + Noise(rng) * 0.2f;
        float clang = MathF.Sin(MathF.Tau * 213f * t) * MathF.Exp(-t / 0.32f) * 0.35f +
                      MathF.Sin(MathF.Tau * 557f * t) * MathF.Exp(-t / 0.21f) * 0.25f +
                      MathF.Sin(MathF.Tau * 1013f * t) * MathF.Exp(-t / 0.12f) * 0.18f +
                      MathF.Sin(MathF.Tau * 1688f * t) * MathF.Exp(-t / 0.07f) * 0.12f;
        float thump = MathF.Sin(MathF.Tau * 70f * t) * MathF.Exp(-t / 0.06f) * 0.6f;
        return (clang + thump + state * 1.2f * MathF.Exp(-t / 0.025f), state);
    });

    private static AudioStreamWav Build(float seconds, ulong seed, Func<float, Pcg32, float> generator) =>
        Build(seconds, seed, (t, rng, state) => (generator(t, rng), state));

    private static AudioStreamWav Build(float seconds, ulong seed, Func<float, Pcg32, float, (float Sample, float State)> generator)
    {
        int count = (int)(seconds * Rate);
        var data = new byte[count * 2];
        var rng = new Pcg32(seed);
        float state = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            (float sample, float next) = generator(t, rng, state);
            rng.NextUInt();
            state = next;
            float fade = MathF.Min(1f, (count - i) / (Rate * 0.005f)); // avoid end clicks
            short value = (short)(Math.Clamp(sample * fade, -1f, 1f) * short.MaxValue);
            data[i * 2] = (byte)(value & 0xFF);
            data[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }

        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = Rate,
            Stereo = false,
            Data = data,
        };
    }

    private static float Noise(Pcg32 rng) => rng.NextFloat() * 2f - 1f;
}
