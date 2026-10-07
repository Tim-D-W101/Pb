using System;
using Godot;
using Pb.Sim.Core;

namespace Pb.Game.Audio;

/// <summary>
/// Building blocks for the synthesised sounds (<see cref="SoundBank"/>): a buffer of samples at a rate, noise, filters,
/// struck resonators and envelopes. Recipes add layers into a buffer; it's then levelled and turned into a Godot stream.
/// Pure C# on float arrays, so the bank can be rendered off the main thread.
/// </summary>
public sealed class Synth
{
    public Synth(float seconds, int rate, ulong seed)
    {
        Rate = rate;
        Samples = new float[Math.Max(1, (int)(seconds * rate))];
        Random = new Pcg32(seed, 0x50D0);
    }

    public int Rate { get; }

    public float[] Samples { get; }

    public Pcg32 Random;

    public int Length => Samples.Length;

    public float Seconds => Samples.Length / (float)Rate;

    /// <summary>The time of sample <paramref name="i"/> (s).</summary>
    public float T(int i) => i / (float)Rate;

    public int Index(float t) => Math.Clamp((int)(t * Rate), 0, Samples.Length);

    /// <summary>White noise in −1..1.</summary>
    public float Noise() => Random.NextFloat() * 2f - 1f;

    /// <summary>A uniform number in [a, b).</summary>
    public float Range(float a, float b) => Random.Range(a, b);

    /// <summary>
    /// A burst of noise from <paramref name="start"/> through a band-pass at <paramref name="frequency"/>, rising over
    /// <paramref name="attack"/> and decaying with time constant <paramref name="decay"/>, lasting about seven of them.
    /// </summary>
    public void NoiseBurst(float start, float frequency, float q, float attack, float decay, float gain)
    {
        var filter = Biquad.BandPass(Rate, frequency, q);
        float level = gain * BandGain(frequency / q);
        int from = Index(start), to = Index(start + attack + decay * 7f);
        for (int i = from; i < to; i++)
        {
            float t = T(i) - start;
            Samples[i] += filter.Process(Noise()) * Envelope.AttackDecay(t, attack, decay) * level;
        }
    }

    /// <summary>Like <see cref="NoiseBurst"/> but low-passed: a dull, wet or soft hit.</summary>
    public void DullBurst(float start, float cutoff, float attack, float decay, float gain)
    {
        var filter = Biquad.LowPass(Rate, cutoff, 0.7f);
        float level = gain * BandGain(cutoff);
        int from = Index(start), to = Index(start + attack + decay * 7f);
        for (int i = from; i < to; i++)
        {
            float t = T(i) - start;
            Samples[i] += filter.Process(Noise()) * Envelope.AttackDecay(t, attack, decay) * level;
        }
    }

    /// <summary>
    /// How much to boost noise filtered down to <paramref name="bandwidth"/> Hz so it's about as loud as the whole band
    /// (a narrow slice of white noise carries only that share of its power), so a recipe's gains mean the same whatever the filter.
    /// </summary>
    public float BandGain(float bandwidth) => Math.Clamp(MathF.Sqrt(0.5f * Rate / MathF.Max(bandwidth, 1f)), 1f, 12f);

    /// <summary>A struck mode: a damped sine at <paramref name="frequency"/> from <paramref name="start"/>.</summary>
    public void Mode(float start, float frequency, float decay, float gain, float phase = 0f)
    {
        int from = Index(start), to = Index(start + decay * 7f);
        float w = MathF.Tau * frequency / Rate;
        for (int i = from; i < to; i++)
        {
            float t = T(i) - start;
            Samples[i] += MathF.Sin(w * (i - from) + phase) * MathF.Exp(-t / decay) * gain;
        }
    }

    /// <summary>A struck object: several inharmonic modes (ratios of <paramref name="fundamental"/>), each decaying faster the higher it is.</summary>
    public void Struck(float start, float fundamental, ReadOnlySpan<float> ratios, float decay, float gain)
    {
        for (int k = 0; k < ratios.Length; k++)
        {
            float jitter = Range(0.985f, 1.015f);
            Mode(start, fundamental * ratios[k] * jitter, decay / (1f + 0.6f * k), gain / (1f + 0.5f * k), Range(0f, MathF.Tau));
        }
    }

    /// <summary>A sine sweeping from <paramref name="from"/> to <paramref name="to"/> Hz over its life: a thump or a membrane.</summary>
    public void Sweep(float start, float from, float to, float attack, float decay, float gain)
    {
        int a = Index(start), b = Index(start + attack + decay * 7f);
        float phase = 0f, life = attack + decay * 7f;
        for (int i = a; i < b; i++)
        {
            float t = T(i) - start;
            float f = from + (to - from) * MathF.Min(1f, t / (life * 0.4f));
            phase += MathF.Tau * f / Rate;
            Samples[i] += MathF.Sin(phase) * Envelope.AttackDecay(t, attack, decay) * gain;
        }
    }

    /// <summary>
    /// Grains: <paramref name="count"/> tiny clicks scattered from <paramref name="start"/> over <paramref name="spread"/> seconds
    /// (denser early), band-passed round <paramref name="frequency"/>: gravel, shards, pellets, crinkling.
    /// </summary>
    public void Grains(float start, float spread, int count, float frequency, float q, float grain, float gain)
    {
        var filter = Biquad.BandPass(Rate, frequency, q);
        var clicks = new float[Length];
        for (int k = 0; k < count; k++)
        {
            float at = start + spread * MathF.Pow(Random.NextFloat(), 1.6f);
            float level = Range(0.3f, 1f) * gain;
            int from = Index(at), to = Index(at + grain * 6f);
            for (int i = from; i < to; i++)
            {
                clicks[i] += Noise() * MathF.Exp(-(T(i) - at) / grain) * level;
            }
        }

        float boost = BandGain(frequency / q);
        for (int i = Index(start); i < Length; i++)
        {
            Samples[i] += filter.Process(clicks[i]) * boost;
        }
    }

    /// <summary>Runs the whole buffer through a filter (a rumble cut, a tone shaping).</summary>
    public void Filter(Biquad filter)
    {
        for (int i = 0; i < Length; i++)
        {
            Samples[i] = filter.Process(Samples[i]);
        }
    }

    /// <summary>Soft clipping by tanh: rounds the peaks, thickens a hit.</summary>
    public void Saturate(float drive)
    {
        float norm = 1f / MathF.Tanh(drive);
        for (int i = 0; i < Length; i++)
        {
            Samples[i] = MathF.Tanh(Samples[i] * drive) * norm;
        }
    }

    /// <summary>Scales the buffer so its loudest sample is <paramref name="peak"/>; false if it's silent.</summary>
    public bool Level(float peak)
    {
        float max = 0f;
        foreach (float s in Samples)
        {
            max = MathF.Max(max, MathF.Abs(s));
        }

        if (max < 1e-6f || float.IsNaN(max))
        {
            return false;
        }

        float scale = peak / max;
        for (int i = 0; i < Length; i++)
        {
            Samples[i] *= scale;
        }

        return true;
    }

    /// <summary>
    /// Fades at both ends, so nothing clicks on or off: a short one in, and out over the last eighth (at least
    /// <paramref name="fadeOut"/>), so a ring still sounding at the end dies away instead of stopping dead.
    /// </summary>
    public void Fades(float fadeIn = 0.0005f, float fadeOut = 0.006f)
    {
        int a = Math.Max(1, Index(fadeIn)), b = Math.Max(1, Math.Max(Index(fadeOut), Length / 8));
        for (int i = 0; i < Math.Min(a, Length); i++)
        {
            Samples[i] *= i / (float)a;
        }

        for (int i = 0; i < Math.Min(b, Length); i++)
        {
            float f = i / (float)b;
            Samples[Length - 1 - i] *= f * f;
        }
    }

    /// <summary>
    /// Makes the buffer loop seamlessly: its last <paramref name="overlap"/> seconds are folded over its start, so the
    /// sample after the end carries straight on from it. The buffer gets that much shorter.
    /// </summary>
    public float[] Loop(float overlap)
    {
        int f = Math.Clamp(Index(overlap), 1, Length / 2);
        int n = Length - f;
        var loop = new float[n];
        Array.Copy(Samples, loop, n);
        for (int i = 0; i < f; i++)
        {
            // Equal-power: the two halves are uncorrelated noise, so their powers add.
            float a = MathF.Sin(0.5f * MathF.PI * i / f);
            float b = MathF.Cos(0.5f * MathF.PI * i / f);
            loop[i] = Samples[i] * a + Samples[n + i] * b;
        }

        return loop;
    }

    /// <summary>16-bit mono PCM bytes of <paramref name="samples"/>.</summary>
    public static byte[] Pcm16(float[] samples)
    {
        var data = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short value = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            data[i * 2] = (byte)(value & 0xFF);
            data[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }

        return data;
    }

    /// <summary>A Godot stream of 16-bit PCM (made on the main thread from bytes rendered anywhere).</summary>
    public static AudioStreamWav Stream(byte[] pcm, int rate, bool loop)
    {
        var stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = rate,
            Stereo = false,
            Data = pcm,
        };
        if (loop)
        {
            stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            stream.LoopBegin = 0;
            stream.LoopEnd = pcm.Length / 2;
        }

        return stream;
    }
}

/// <summary>Envelope shapes.</summary>
public static class Envelope
{
    /// <summary>A linear rise over <paramref name="attack"/>, then an exponential fall with time constant <paramref name="decay"/>.</summary>
    public static float AttackDecay(float t, float attack, float decay)
    {
        if (t < 0f)
        {
            return 0f;
        }

        return t < attack ? t / MathF.Max(attack, 1e-5f) : MathF.Exp(-(t - attack) / decay);
    }

    /// <summary>Rises over <paramref name="attack"/>, holds, and falls over <paramref name="release"/> to end at <paramref name="length"/>.</summary>
    public static float Swell(float t, float attack, float release, float length)
    {
        if (t < 0f || t > length)
        {
            return 0f;
        }

        float rise = MathF.Min(1f, t / MathF.Max(attack, 1e-5f));
        float fall = MathF.Min(1f, (length - t) / MathF.Max(release, 1e-5f));
        return MathF.Min(rise, fall);
    }
}

/// <summary>A two-pole, two-zero filter (the RBJ audio cookbook's).</summary>
public struct Biquad
{
    private float _b0, _b1, _b2, _a1, _a2;
    private float _x1, _x2, _y1, _y2;

    public static Biquad LowPass(int rate, float frequency, float q)
    {
        (float cos, float alpha) = Prepare(rate, frequency, q);
        return Make((1f - cos) / 2f, 1f - cos, (1f - cos) / 2f, 1f + alpha, -2f * cos, 1f - alpha);
    }

    public static Biquad HighPass(int rate, float frequency, float q)
    {
        (float cos, float alpha) = Prepare(rate, frequency, q);
        return Make((1f + cos) / 2f, -(1f + cos), (1f + cos) / 2f, 1f + alpha, -2f * cos, 1f - alpha);
    }

    /// <summary>A band-pass whose peak is at 0 dB.</summary>
    public static Biquad BandPass(int rate, float frequency, float q)
    {
        (float cos, float alpha) = Prepare(rate, frequency, q);
        return Make(alpha, 0f, -alpha, 1f + alpha, -2f * cos, 1f - alpha);
    }

    /// <summary>Moves a band-pass to a new centre without losing what it's ringing with (re-making it would click).</summary>
    public void TuneBandPass(int rate, float frequency, float q)
    {
        (float cos, float alpha) = Prepare(rate, frequency, q);
        float a0 = 1f + alpha;
        _b0 = alpha / a0;
        _b1 = 0f;
        _b2 = -alpha / a0;
        _a1 = -2f * cos / a0;
        _a2 = (1f - alpha) / a0;
    }

    public float Process(float x)
    {
        float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
        _x2 = _x1;
        _x1 = x;
        _y2 = _y1;
        _y1 = y;
        return y;
    }

    private static (float Cos, float Alpha) Prepare(int rate, float frequency, float q)
    {
        float f = Math.Clamp(frequency, 10f, rate * 0.45f);
        float w = MathF.Tau * f / rate;
        return (MathF.Cos(w), MathF.Sin(w) / (2f * MathF.Max(q, 0.05f)));
    }

    private static Biquad Make(float b0, float b1, float b2, float a0, float a1, float a2) => new()
    {
        _b0 = b0 / a0,
        _b1 = b1 / a0,
        _b2 = b2 / a0,
        _a1 = a1 / a0,
        _a2 = a2 / a0,
    };
}

/// <summary>A one-pole low-pass: cheap smoothing (noise colouring, parameter glides).</summary>
public struct OnePole
{
    private float _a, _y;

    public static OnePole LowPass(int rate, float frequency) => new() { _a = 1f - MathF.Exp(-MathF.Tau * frequency / rate) };

    public float Process(float x) => _y += _a * (x - _y);
}
