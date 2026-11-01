using Pb.Game.Audio;

namespace Pb.Sim.Tests;

/// <summary>
/// The synthesised sounds' shapes (Phase 5, M5.1): every recipe renders and none is silent, and the ones that carry the
/// feel do what they're for: the report hits at once and is over fast, with more body at full pressure; the whizz
/// rises to its pass in the middle and falls in pitch as it goes; the confirm is bright, the "didn't count" dull;
/// the ringing in your ears lasts.
/// </summary>
public class SoundTests
{
    private static readonly Lazy<SoundBank> Bank = new(() => SoundBank.Render(variations: 2));

    private static float[] Samples(Sfx sfx, int variation = 0)
    {
        byte[] pcm = Bank.Value.Pcm(sfx, variation);
        var samples = new float[pcm.Length / 2];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8)) / 32768f;
        }

        return samples;
    }

    private static float Rate(Sfx sfx) => Bank.Value.RateOf(sfx);

    /// <summary>The time the sound is loudest (s), from a 2 ms window's energy.</summary>
    private static float PeakTime(float[] x, float rate)
    {
        int window = (int)(0.002f * rate), best = 0;
        double bestEnergy = -1;
        for (int start = 0; start + window <= x.Length; start += window / 2)
        {
            double e = 0;
            for (int i = start; i < start + window; i++)
            {
                e += x[i] * x[i];
            }

            if (e > bestEnergy)
            {
                bestEnergy = e;
                best = start;
            }
        }

        return best / rate;
    }

    /// <summary>The share of the sound's energy that comes before <paramref name="seconds"/>.</summary>
    private static double EnergyBefore(float[] x, float rate, float seconds)
    {
        double before = 0, all = 0;
        int cut = (int)(seconds * rate);
        for (int i = 0; i < x.Length; i++)
        {
            double e = x[i] * x[i];
            all += e;
            if (i < cut)
            {
                before += e;
            }
        }

        return before / all;
    }

    /// <summary>Brightness: how often the signal crosses zero, as a frequency (Hz), over a stretch.</summary>
    private static float Crossings(float[] x, float rate, float from, float to)
    {
        int a = (int)(from * rate), b = Math.Min(x.Length, (int)(to * rate)), n = 0;
        for (int i = a + 1; i < b; i++)
        {
            if ((x[i - 1] < 0f) != (x[i] < 0f))
            {
                n++;
            }
        }

        return n * rate / (2f * (b - a));
    }

    /// <summary>The share of the energy below <paramref name="hz"/>, by a fourth-order low-pass (two of the bank's biquads).</summary>
    private static double LowShare(float[] x, float rate, float hz)
    {
        Biquad a = Biquad.LowPass((int)rate, hz, 0.707f), b = Biquad.LowPass((int)rate, hz, 0.707f);
        double low = 0, all = 0;
        foreach (float v in x)
        {
            float y = b.Process(a.Process(v));
            low += y * y;
            all += v * v;
        }

        return low / all;
    }

    [Fact]
    public void Every_sound_renders_and_none_is_silent()
    {
        Assert.Empty(Bank.Value.Problems);
        foreach (Sfx sfx in Enum.GetValues<Sfx>())
        {
            Assert.True(Bank.Value.VariationsOf(sfx) >= 1, $"{sfx} has no variations");
            Assert.True(Samples(sfx).Any(v => MathF.Abs(v) > 0.05f), $"{sfx} is near silent");
        }
    }

    [Fact]
    public void The_report_hits_at_once_and_is_over_fast_with_more_body_at_full_pressure()
    {
        float rate = Rate(Sfx.ReportFull);
        foreach (Sfx report in new[] { Sfx.ReportFull, Sfx.ReportMid, Sfx.ReportLow })
        {
            float[] x = Samples(report);
            Assert.InRange(PeakTime(x, rate), 0f, 0.012f);
            Assert.True(EnergyBefore(x, rate, 0.06f) > 0.8, $"{report}: {EnergyBefore(x, rate, 0.06f):P0} of it in the first 60 ms");
        }

        // The gas's thump weakens as the tank empties: less of the sound below 200 Hz.
        double full = LowShare(Samples(Sfx.ReportFull), rate, 200f), low = LowShare(Samples(Sfx.ReportLow), rate, 200f);
        Assert.True(full > 1.3 * low, $"below 200 Hz: {full:P1} of the full-pressure report, {low:P1} of the low");
    }

    [Fact]
    public void The_whizz_swells_to_its_pass_and_falls_in_pitch_as_it_goes()
    {
        float rate = Rate(Sfx.Whizz);
        for (int v = 0; v < Bank.Value.VariationsOf(Sfx.Whizz); v++)
        {
            float[] x = Samples(Sfx.Whizz, v);
            Assert.InRange(PeakTime(x, rate), 0.04f, 0.11f);
            float coming = Crossings(x, rate, 0.02f, 0.06f), going = Crossings(x, rate, 0.11f, 0.16f);
            Assert.True(coming > 1.4f * going, $"variation {v}: {coming:0} Hz coming, {going:0} Hz going");
        }
    }

    [Fact]
    public void The_confirm_is_bright_and_the_bounce_that_didnt_count_is_dull()
    {
        float confirm = Crossings(Samples(Sfx.HitConfirm), Rate(Sfx.HitConfirm), 0.01f, 0.2f);
        float bounce = Crossings(Samples(Sfx.HitBounce), Rate(Sfx.HitBounce), 0.002f, 0.1f);
        Assert.True(confirm > 1200f, $"confirm {confirm:0} Hz");
        Assert.True(bounce < 1000f, $"bounce {bounce:0} Hz");
    }

    [Fact]
    public void Your_ears_ring_on_after_the_hit()
    {
        float rate = Rate(Sfx.EarRing);
        float[] x = Samples(Sfx.EarRing);
        Assert.True(x.Length / rate >= 2f);
        // Still ringing a second and a half in, high.
        int at = (int)(1.5f * rate);
        Assert.True(x.Skip(at).Take((int)(0.1f * rate)).Max(MathF.Abs) > 0.02f);
        Assert.InRange(Crossings(x, rate, 0.2f, 1.0f), 3000f, 4200f);
    }

    [Fact]
    public void The_loader_rattles_briefly_and_the_hit_you_take_is_heavier_than_any_ball_breaking_near_you()
    {
        float rate = Rate(Sfx.LoaderRattle);
        Assert.True(EnergyBefore(Samples(Sfx.LoaderRattle), rate, 0.16f) > 0.95);
        Assert.True(LowShare(Samples(Sfx.HitTaken), Rate(Sfx.HitTaken), 200f) > LowShare(Samples(Sfx.BreakPlayer), Rate(Sfx.BreakPlayer), 200f) * 1.5);
    }
}
