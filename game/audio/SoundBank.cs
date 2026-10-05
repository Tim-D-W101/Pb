using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Pb.Game.Audio;

/// <summary>Every synthesised sound in the game.</summary>
public enum Sfx
{
    // The marker's report at three tank pressures (full, half, low), and its loader feeding the next ball.
    ReportFull,
    ReportMid,
    ReportLow,
    LoaderFeed,
    DryFire,
    FireMode,
    AirLow,
    RefillPop,
    RefillPour,
    RefillSnap,
    RefillDenied,
    PickupPod,
    PickupAir,

    // A ball breaking, by what it broke on.
    BreakStone,
    BreakMetal,
    BreakWood,
    BreakGlass,
    BreakGround,
    BreakGravel,
    BreakTarp,
    BreakInflatable,
    BreakRubber,
    BreakPlayer,
    BreakMask,

    // A ball bouncing off.
    BounceHard,
    BounceMetal,
    BounceWood,
    BounceGlass,
    BounceSoft,

    // Feet, by what they come down on, and sliding, jumping and landing.
    StepStone,
    StepMetal,
    StepWood,
    StepGravel,
    StepGround,
    StepGrass,
    StepTarp,
    StepWater,
    StepRubber,
    SlideHard,
    SlideSoft,
    SlideMetal,
    SlideGravel,
    Jump,
    LandHard,
    LandSoft,
    LandMetal,

    // Doors by kind.
    DoorCreak,
    DoorSlam,
    DoorSteelOpen,
    DoorClank,
    SwingOpen,
    SwingFlap,
    ColdOpen,
    ColdShut,
    SlideRoll,
    SlideBang,

    // You: hit, and hitting someone.
    HitTaken,
    HitMarker,

    // The objectives.
    CaseTake,
    CaseDrop,
    CaseOut,
    CaseBeep,
    CaseAlarm,
    HoldOurs,
    HoldContested,
    HoldTheirs,

    // The round: the breakout horn, the referee's whistle.
    Horn,
    WhistleLong,
    WhistleTriple,
    WhistlePips,

    // Menus.
    UiClick,
    UiHover,
    UiToggle,
    UiBack,
    UiReward,

    // Ambience: loops, and things now and then.
    Wind,
    Traffic,
    ToneRoom,
    ToneHall,
    ToneDrip,
    TonePigeons,
    ToneDraught,
    ToneCold,
    ToneHum,
    Caw,
    WingFlap,
    Train,
}

/// <summary>
/// The game's sound effects, synthesised from recipes when the game starts (no recorded sounds): several variations of
/// each, rendered off the main thread and shared by every scene, as samples (each scene makes its own Godot streams of
/// them, a <see cref="SoundSet"/>, and frees them when it goes). Each variation is levelled to the same peak; how loud each kind plays is set in
/// presentation.jsonc ("audio" → "mix"). The recipes are physical sketches: struck modes for metal, glass and wood,
/// low-passed noise for wet and soft hits, grains for gravel and crinkling, a stick-slip squeak for hinges.
/// </summary>
public sealed class SoundBank
{
    /// <summary>One-shot effects' sample rate; the ambience loops use <see cref="LoopRate"/>.</summary>
    public const int Rate = 32000;

    public const int LoopRate = 22050;

    private const float DefaultPeak = 0.9f;

    private static readonly float[] MetalRatios = [1f, 2.76f, 5.40f, 8.93f];
    private static readonly float[] GlassRatios = [1f, 1.58f, 2.31f, 3.17f];
    private static readonly float[] WoodRatios = [1f, 2.3f, 3.9f];
    private static readonly float[] DeckRatios = [1f, 2.4f, 4.1f, 6.3f];
    private static readonly float[] PlasticRatios = [1f, 2.1f, 3.3f];

    private readonly byte[][][] _pcm;
    private readonly int[] _rates;
    private readonly bool[] _loops;

    private SoundBank(byte[][][] pcm, int[] rates, bool[] loops, List<string> problems, double milliseconds)
    {
        _pcm = pcm;
        _rates = rates;
        _loops = loops;
        Problems = problems;
        RenderMilliseconds = milliseconds;
    }

    /// <summary>Sounds that came out silent or broken (empty when all is well).</summary>
    public IReadOnlyList<string> Problems { get; }

    public double RenderMilliseconds { get; }

    public int SoundCount => _pcm.Length;

    public int VariationCount
    {
        get
        {
            int n = 0;
            foreach (byte[][] v in _pcm)
            {
                n += v.Length;
            }

            return n;
        }
    }

    /// <summary>How many seconds of sound the bank holds.</summary>
    public float Seconds
    {
        get
        {
            double s = 0;
            for (int i = 0; i < _pcm.Length; i++)
            {
                foreach (byte[] v in _pcm[i])
                {
                    s += v.Length / 2.0 / _rates[i];
                }
            }

            return (float)s;
        }
    }

    private static Task<SoundBank>? _shared;

    /// <summary>Starts rendering the game's bank, once: the first scene that needs sound does it, and every scene after shares it.</summary>
    public static void Warm(int variations) => _shared ??= RenderAsync(variations);

    /// <summary>The shared bank once it's rendered, else null (it takes a moment after <see cref="Warm"/>).</summary>
    public static SoundBank? Ready => _shared is { IsCompletedSuccessfully: true } done ? done.Result : null;

    /// <summary>Why the shared bank couldn't be rendered, if it couldn't.</summary>
    public static string? Failure => _shared is { IsFaulted: true } failed ? failed.Exception?.GetBaseException().Message : null;

    /// <summary>The shared bank, waiting for it if need be.</summary>
    public static SoundBank Shared(int variations)
    {
        Warm(variations);
        return _shared!.GetAwaiter().GetResult();
    }

    /// <summary>Renders every sound with <paramref name="variations"/> variations of each one-shot, on a worker thread.</summary>
    public static Task<SoundBank> RenderAsync(int variations) => Task.Run(() => Render(variations));

    public static SoundBank Render(int variations)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Sfx[] all = Enum.GetValues<Sfx>();
        var pcm = new byte[all.Length][][];
        var rates = new int[all.Length];
        var loops = new bool[all.Length];
        var problems = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.ForEach(all, sfx =>
        {
            Recipe r = RecipeFor(sfx, variations);
            int index = (int)sfx;
            rates[index] = r.Loop ? LoopRate : Rate;
            loops[index] = r.Loop;
            pcm[index] = new byte[r.Variations][];
            for (int v = 0; v < r.Variations; v++)
            {
                var s = new Synth(r.Seconds, rates[index], (ulong)(index * 977 + v * 31 + 7));
                r.Make(s, v);
                float[] samples;
                if (r.Loop)
                {
                    samples = s.Loop(1f);
                    float max = 0f;
                    foreach (float x in samples)
                    {
                        max = MathF.Max(max, MathF.Abs(x));
                    }

                    if (max < 1e-6f || float.IsNaN(max))
                    {
                        problems.Add($"{sfx} is silent");
                    }
                    else
                    {
                        for (int i = 0; i < samples.Length; i++)
                        {
                            samples[i] *= r.Peak / max;
                        }
                    }
                }
                else
                {
                    if (!s.Level(r.Peak))
                    {
                        problems.Add($"{sfx} variation {v} is silent");
                    }

                    s.Fades();
                    samples = s.Samples;
                }

                pcm[index][v] = Synth.Pcm16(samples);
            }
        });

        var sorted = new List<string>(problems);
        sorted.Sort(StringComparer.Ordinal);
        return new SoundBank(pcm, rates, loops, sorted, watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>How many variations of <paramref name="sfx"/> there are.</summary>
    public int VariationsOf(Sfx sfx) => _pcm[(int)sfx].Length;

    /// <summary>One variation's 16-bit mono samples, at <see cref="RateOf"/>.</summary>
    public byte[] Pcm(Sfx sfx, int variation) => _pcm[(int)sfx][variation];

    public int RateOf(Sfx sfx) => _rates[(int)sfx];

    public bool Loops(Sfx sfx) => _loops[(int)sfx];

    /// <summary>
    /// How to make a sound: its length, how many variations, the recipe, whether it loops, and the peak it's levelled to
    /// (steady tones are levelled lower than hits, being so much louder for the same peak).
    /// </summary>
    private readonly record struct Recipe(float Seconds, int Variations, Action<Synth, int> Make, bool Loop = false, float Peak = DefaultPeak);

    private static Recipe RecipeFor(Sfx sfx, int n) => sfx switch
    {
        Sfx.ReportFull => new(0.34f, n, (s, _) => Report(s, 1f)),
        Sfx.ReportMid => new(0.34f, n, (s, _) => Report(s, 0.75f)),
        Sfx.ReportLow => new(0.34f, n, (s, _) => Report(s, 0.5f)),
        Sfx.LoaderFeed => new(0.16f, 2, (s, _) => LoaderFeed(s), Peak: 0.5f),
        Sfx.DryFire => new(0.06f, 2, (s, _) => DryFire(s)),
        Sfx.FireMode => new(0.1f, 1, (s, _) => FireMode(s)),
        Sfx.AirLow => new(0.6f, 1, (s, _) => AirLow(s)),
        Sfx.RefillPop => new(0.18f, 2, (s, _) => RefillPop(s)),
        Sfx.RefillPour => new(0.95f, 2, (s, _) => RefillPour(s)),
        Sfx.RefillSnap => new(0.14f, 2, (s, _) => RefillSnap(s)),
        Sfx.RefillDenied => new(0.16f, 1, (s, _) => RefillDenied(s)),
        Sfx.PickupPod => new(0.45f, 2, (s, _) => PickupPod(s)),
        Sfx.PickupAir => new(0.9f, 1, (s, _) => PickupAir(s)),

        Sfx.BreakStone => new(0.42f, n, (s, _) => Break(s, Surface.Stone)),
        Sfx.BreakMetal => new(0.6f, n, (s, _) => Break(s, Surface.Metal)),
        Sfx.BreakWood => new(0.42f, n, (s, _) => Break(s, Surface.Wood)),
        Sfx.BreakGlass => new(0.6f, n, (s, _) => Break(s, Surface.Glass)),
        Sfx.BreakGround => new(0.42f, n, (s, _) => Break(s, Surface.Ground)),
        Sfx.BreakGravel => new(0.42f, n, (s, _) => Break(s, Surface.Gravel)),
        Sfx.BreakTarp => new(0.42f, n, (s, _) => Break(s, Surface.Tarp)),
        Sfx.BreakInflatable => new(0.5f, n, (s, _) => Break(s, Surface.Inflatable)),
        Sfx.BreakRubber => new(0.42f, n, (s, _) => Break(s, Surface.Rubber)),
        Sfx.BreakPlayer => new(0.42f, n, (s, _) => Break(s, Surface.Player)),
        Sfx.BreakMask => new(0.42f, n, (s, _) => Break(s, Surface.Mask)),

        Sfx.BounceHard => new(0.12f, n, (s, _) => Bounce(s, Surface.Stone)),
        Sfx.BounceMetal => new(0.3f, n, (s, _) => Bounce(s, Surface.Metal)),
        Sfx.BounceWood => new(0.14f, n, (s, _) => Bounce(s, Surface.Wood)),
        Sfx.BounceGlass => new(0.3f, n, (s, _) => Bounce(s, Surface.Glass)),
        Sfx.BounceSoft => new(0.1f, n, (s, _) => Bounce(s, Surface.Ground)),

        Sfx.StepStone => new(0.26f, n + 2, (s, v) => Step(s, Surface.Stone, v)),
        Sfx.StepMetal => new(0.4f, n + 2, (s, v) => Step(s, Surface.Metal, v)),
        Sfx.StepWood => new(0.34f, n + 2, (s, v) => Step(s, Surface.Wood, v)),
        Sfx.StepGravel => new(0.3f, n + 2, (s, v) => Step(s, Surface.Gravel, v)),
        Sfx.StepGround => new(0.26f, n + 2, (s, v) => Step(s, Surface.Ground, v)),
        Sfx.StepGrass => new(0.3f, n + 2, (s, v) => Step(s, Surface.Grass, v)),
        Sfx.StepTarp => new(0.3f, n + 2, (s, v) => Step(s, Surface.Tarp, v)),
        Sfx.StepWater => new(0.4f, n + 2, (s, v) => Step(s, Surface.Water, v)),
        Sfx.StepRubber => new(0.26f, n + 2, (s, v) => Step(s, Surface.Rubber, v)),
        Sfx.SlideHard => new(0.6f, 2, (s, _) => Slide(s, Surface.Stone)),
        Sfx.SlideSoft => new(0.6f, 2, (s, _) => Slide(s, Surface.Ground)),
        Sfx.SlideMetal => new(0.6f, 2, (s, _) => Slide(s, Surface.Metal)),
        Sfx.SlideGravel => new(0.6f, 2, (s, _) => Slide(s, Surface.Gravel)),
        Sfx.Jump => new(0.3f, 3, (s, _) => Jump(s)),
        Sfx.LandHard => new(0.5f, 3, (s, _) => Land(s, Surface.Stone)),
        Sfx.LandSoft => new(0.5f, 3, (s, _) => Land(s, Surface.Ground)),
        Sfx.LandMetal => new(0.7f, 3, (s, _) => Land(s, Surface.Metal)),

        Sfx.DoorCreak => new(0.75f, 3, (s, _) => Creak(s, 230f, 110f, 1450f, 0.75f)),
        Sfx.DoorSlam => new(0.55f, 2, (s, _) => Slam(s)),
        Sfx.DoorSteelOpen => new(1.0f, 2, (s, _) => SteelOpen(s)),
        Sfx.DoorClank => new(0.9f, 2, (s, _) => Clank(s)),
        Sfx.SwingOpen => new(0.4f, 2, (s, _) => SwingOpen(s)),
        Sfx.SwingFlap => new(0.8f, 2, (s, _) => SwingFlap(s)),
        Sfx.ColdOpen => new(1.5f, 2, (s, _) => ColdOpen(s)),
        Sfx.ColdShut => new(1.0f, 2, (s, _) => ColdShut(s, latch: true)),
        Sfx.SlideRoll => new(1.3f, 2, (s, _) => SlideRoll(s)),
        Sfx.SlideBang => new(0.9f, 2, (s, _) => ColdShut(s, latch: false)),

        Sfx.HitTaken => new(0.9f, 2, (s, _) => HitTaken(s)),
        Sfx.HitMarker => new(0.09f, 1, (s, _) => HitMarker(s)),

        Sfx.CaseTake => new(0.35f, 1, (s, _) => CaseTake(s)),
        Sfx.CaseDrop => new(0.45f, 2, (s, _) => CaseDrop(s)),
        Sfx.CaseOut => new(1.4f, 1, (s, _) => Chime(s, [523.25f, 659.26f, 783.99f, 1046.5f], 0.11f, 0.45f)),
        Sfx.CaseBeep => new(0.12f, 1, (s, _) => Beep(s, 2150f, 0.07f), Peak: 0.5f),
        Sfx.CaseAlarm => new(1.0f, 1, (s, _) => Alarm(s, 520f, 690f, 4, 0.22f), Peak: 0.3f),
        Sfx.HoldOurs => new(0.9f, 1, (s, _) => Chime(s, [392f, 587.33f], 0.14f, 0.4f)),
        Sfx.HoldContested => new(0.75f, 1, (s, _) => Buzz(s, 180f, 2, 0.24f, 0.1f), Peak: 0.3f),
        Sfx.HoldTheirs => new(0.9f, 1, (s, _) => Chime(s, [587.33f, 349.23f], 0.16f, 0.4f)),

        Sfx.Horn => new(1.7f, 1, (s, _) => Horn(s), Peak: 0.5f),
        Sfx.WhistleLong => new(1.05f, 1, (s, _) => Whistle(s, [(0f, 0.95f)]), Peak: 0.6f),
        Sfx.WhistleTriple => new(1.4f, 1, (s, _) => Whistle(s, [(0f, 0.2f), (0.32f, 0.2f), (0.64f, 0.7f)]), Peak: 0.6f),
        Sfx.WhistlePips => new(0.5f, 1, (s, _) => Whistle(s, [(0f, 0.11f), (0.22f, 0.11f)]), Peak: 0.6f),

        Sfx.UiClick => new(0.05f, 2, (s, _) => UiClick(s, 1600f)),
        Sfx.UiHover => new(0.04f, 1, (s, _) => UiHover(s)),
        Sfx.UiToggle => new(0.07f, 1, (s, _) => UiClick(s, 1200f)),
        Sfx.UiBack => new(0.07f, 1, (s, _) => UiClick(s, 880f)),
        Sfx.UiReward => new(1.3f, 1, (s, _) => Chime(s, [659.26f, 783.99f, 987.77f, 1318.5f], 0.09f, 0.5f)),

        Sfx.Wind => new(10f, 1, (s, _) => Wind(s), Loop: true),
        Sfx.Traffic => new(10f, 1, (s, _) => Traffic(s), Loop: true),
        Sfx.ToneRoom => new(9f, 1, (s, _) => RoomTone(s, 240f, 0.5f), Loop: true),
        Sfx.ToneHall => new(10f, 1, (s, _) => Hall(s), Loop: true),
        Sfx.ToneDrip => new(10f, 1, (s, _) => Drips(s), Loop: true),
        Sfx.TonePigeons => new(12f, 1, (s, _) => Pigeons(s), Loop: true),
        Sfx.ToneDraught => new(10f, 1, (s, _) => Draught(s), Loop: true),
        Sfx.ToneCold => new(10f, 1, (s, _) => Cold(s), Loop: true),
        Sfx.ToneHum => new(6f, 1, (s, _) => Hum(s), Loop: true),
        Sfx.Caw => new(1.3f, 4, (s, v) => Caws(s, 1 + v % 3)),
        Sfx.WingFlap => new(0.9f, 2, (s, _) => WingFlap(s)),
        Sfx.Train => new(18f, 1, (s, _) => Train(s)),
        _ => throw new ArgumentOutOfRangeException(nameof(sfx), sfx, null),
    };

    private enum Surface
    {
        Stone,
        Metal,
        Wood,
        Glass,
        Ground,
        Gravel,
        Grass,
        Tarp,
        Inflatable,
        Rubber,
        Player,
        Mask,
        Water,
    }

    // ---- The marker -----------------------------------------------------------------------------------------------

    /// <summary>
    /// A paintball marker's report: the sear's click, a sharp crack of air out of the muzzle, the barrel ringing as a
    /// tube, a thump of body, and a hiss of air after. <paramref name="body"/> (1 at full pressure) weakens and lowers it.
    /// </summary>
    private static void Report(Synth s, float body)
    {
        float barrel = s.Range(255f, 300f) * (0.9f + 0.1f * body);
        s.NoiseBurst(0f, 3400f, 1.4f, 0.0002f, 0.0008f, 0.55f);
        var high = Biquad.HighPass(s.Rate, 650f, 0.7f);
        float crackDecay = 0.0035f + (1f - body) * 0.004f;
        for (int i = 0; i < s.Index(0.06f); i++)
        {
            s.Samples[i] += high.Process(s.Noise()) * MathF.Exp(-s.T(i) / crackDecay) * (0.8f + 0.5f * body);
        }

        s.Mode(0.0004f, barrel, 0.024f, 0.55f * body);
        s.Mode(0.0004f, barrel * 2.03f, 0.016f, 0.32f * body);
        s.Mode(0.0004f, barrel * 3.08f, 0.01f, 0.2f);
        s.NoiseBurst(0.0004f, barrel * 2f, 3f, 0.0004f, 0.012f, 0.25f);
        s.Sweep(0f, 155f, 62f, 0.0012f, 0.034f, 0.85f * body);
        var hiss = Biquad.HighPass(s.Rate, 2600f, 0.7f);
        for (int i = s.Index(0.004f); i < s.Length; i++)
        {
            float t = s.T(i) - 0.004f;
            s.Samples[i] += hiss.Process(s.Noise()) * Envelope.AttackDecay(t, 0.006f, 0.05f + (1f - body) * 0.04f) * 0.16f;
        }

        s.Saturate(1.7f);
    }

    /// <summary>The loader's motor spinning up to push the next ball in, and a little gear click.</summary>
    private static void LoaderFeed(Synth s)
    {
        var lowpass = Biquad.LowPass(s.Rate, 1300f, 0.8f);
        float phase = 0f;
        for (int i = 0; i < s.Length; i++)
        {
            float t = s.T(i);
            phase += (150f + 70f * MathF.Min(1f, t / 0.05f)) / s.Rate;
            float saw = 2f * (phase - MathF.Floor(phase + 0.5f));
            s.Samples[i] += lowpass.Process(saw) * Envelope.Swell(t, 0.02f, 0.06f, s.Seconds) * 0.5f;
        }

        s.NoiseBurst(0.09f, 2800f, 2f, 0.0002f, 0.001f, 0.35f);
    }

    private static void DryFire(Synth s)
    {
        s.NoiseBurst(0f, 2600f, 1.2f, 0.0001f, 0.0018f, 0.8f);
        s.Mode(0f, 2400f, 0.004f, 0.3f);
    }

    private static void FireMode(Synth s)
    {
        s.NoiseBurst(0f, 3600f, 2f, 0.0001f, 0.0008f, 0.9f);
        s.NoiseBurst(0.034f, 3000f, 2f, 0.0001f, 0.0008f, 0.6f);
    }

    /// <summary>The tank running dry: air sputtering out in fits.</summary>
    private static void AirLow(Synth s)
    {
        var band = Biquad.BandPass(s.Rate, 2400f, 0.8f);
        float gate = 0f;
        for (int i = 0; i < s.Length; i++)
        {
            float t = s.T(i);
            if (i % (s.Rate / 40) == 0)
            {
                gate = s.Random.NextFloat() < 0.55f ? s.Range(0.4f, 1f) : 0.05f;
            }

            s.Samples[i] += band.Process(s.Noise()) * gate * Envelope.Swell(t, 0.02f, 0.25f, s.Seconds) * 2f;
        }
    }

    /// <summary>A pod's lid popping open: a hollow plastic "pok".</summary>
    private static void RefillPop(Synth s)
    {
        s.DullBurst(0f, 1500f, 0.0005f, 0.005f, 0.8f);
        s.Mode(0f, s.Range(600f, 700f), 0.016f, 0.6f);
        s.Mode(0f, s.Range(1300f, 1450f), 0.008f, 0.25f);
    }

    /// <summary>Balls pouring out of a pod into the loader: a run of plastic clicks.</summary>
    private static void RefillPour(Synth s)
    {
        s.Grains(0.04f, 0.78f, 90, 2300f, 1.3f, 0.0011f, 0.8f);
        s.Grains(0.05f, 0.7f, 40, 1200f, 1.2f, 0.0016f, 0.4f);
        s.DullBurst(0.04f, 500f, 0.25f, 0.2f, 0.15f);
    }

    private static void RefillSnap(Synth s)
    {
        s.NoiseBurst(0f, 2800f, 1.5f, 0.0002f, 0.0018f, 0.9f);
        s.Struck(0f, s.Range(1200f, 1400f), PlasticRatios, 0.016f, 0.5f);
        s.Grains(0.02f, 0.08f, 8, 2200f, 1.4f, 0.001f, 0.3f);
    }

    private static void RefillDenied(Synth s)
    {
        s.DullBurst(0f, 900f, 0.0005f, 0.006f, 0.9f);
        s.DullBurst(0.07f, 800f, 0.0005f, 0.006f, 0.7f);
    }

    /// <summary>Grabbing a full pod: a handful of rattling balls and the rustle of a hand.</summary>
    private static void PickupPod(Synth s)
    {
        s.Grains(0f, 0.3f, 40, 2100f, 1.3f, 0.0012f, 0.7f);
        s.DullBurst(0f, 900f, 0.004f, 0.03f, 0.4f);
        s.NoiseBurst(0.01f, 3500f, 0.7f, 0.02f, 0.04f, 0.2f);
    }

    /// <summary>A tank screwed onto a fill valve and topped up: two clicks and air rushing in, rising.</summary>
    private static void PickupAir(Synth s)
    {
        s.NoiseBurst(0f, 2500f, 1.5f, 0.0002f, 0.0015f, 0.7f);
        s.NoiseBurst(0.06f, 2200f, 1.5f, 0.0002f, 0.0015f, 0.6f);
        var band = Biquad.BandPass(s.Rate, 1800f, 0.9f);
        float centre = 1800f;
        for (int i = s.Index(0.12f); i < s.Length; i++)
        {
            float t = s.T(i) - 0.12f;
            if ((i & 63) == 0)
            {
                centre = 1500f + 2600f * MathF.Min(1f, t / 0.65f);
                band.TuneBandPass(s.Rate, centre, 0.9f);
            }

            s.Samples[i] += band.Process(s.Noise()) * Envelope.Swell(t, 0.03f, 0.12f, s.Seconds - 0.12f) * 1.2f;
        }
    }

    // ---- Balls --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A ball breaking: its shell cracking, the fill splatting out and spattering, over the response of whatever it hit
    /// (a metal panel's ring, a plank's knock, a membrane's boom, a body's thwack...).
    /// </summary>
    private static void Break(Synth s, Surface on)
    {
        bool soft = on is Surface.Ground or Surface.Gravel or Surface.Tarp or Surface.Inflatable or Surface.Rubber or Surface.Player;
        if (!soft)
        {
            s.NoiseBurst(0f, s.Range(2300f, 3000f), 0.8f, 0.0002f, 0.0014f, 0.7f);
        }

        // The wet splat: low-passed noise squelching at a few tens of hertz, and droplets spattering.
        var wet = Biquad.LowPass(s.Rate, soft ? 1000f : 1500f, 0.7f);
        float squelch = s.Range(35f, 60f);
        for (int i = 0; i < s.Index(0.3f); i++)
        {
            float t = s.T(i);
            float am = 0.75f + 0.25f * MathF.Sin(MathF.Tau * squelch * t);
            s.Samples[i] += wet.Process(s.Noise()) * Envelope.AttackDecay(t, 0.0015f, 0.035f) * am * 3.2f;
        }

        s.Grains(0.004f, 0.1f, soft ? 12 : 26, 3600f, 1f, 0.0006f, soft ? 0.12f : 0.25f);

        switch (on)
        {
            case Surface.Stone:
                s.NoiseBurst(0f, s.Range(1600f, 2100f), 1.4f, 0.0002f, 0.003f, 0.7f);
                s.Mode(0f, s.Range(800f, 1000f), 0.006f, 0.3f);
                s.Grains(0.002f, 0.05f, 10, 5000f, 1f, 0.0004f, 0.2f);
                break;
            case Surface.Metal:
                s.Struck(0f, s.Range(850f, 1400f), MetalRatios, 0.2f, 0.45f);
                break;
            case Surface.Wood:
                s.Struck(0f, s.Range(180f, 320f), WoodRatios, 0.035f, 0.8f);
                s.NoiseBurst(0f, 1000f, 1.2f, 0.0003f, 0.004f, 0.5f);
                break;
            case Surface.Glass:
                s.Struck(0f, s.Range(1800f, 2600f), GlassRatios, 0.24f, 0.4f);
                s.Grains(0.01f, 0.2f, 6, 5200f, 3f, 0.002f, 0.1f);
                break;
            case Surface.Ground:
                s.DullBurst(0f, 700f, 0.003f, 0.03f, 1f);
                s.Sweep(0f, 110f, 60f, 0.002f, 0.03f, 0.6f);
                break;
            case Surface.Gravel:
                s.DullBurst(0f, 700f, 0.003f, 0.03f, 0.8f);
                s.Grains(0.003f, 0.13f, 32, 2500f, 0.9f, 0.0008f, 0.5f);
                break;
            case Surface.Tarp:
                Flap(s, 0f, 480f, 0.025f, 0.8f);
                s.Sweep(0f, 160f, 100f, 0.002f, 0.03f, 0.4f);
                break;
            case Surface.Inflatable:
                s.Sweep(0f, 140f, 82f, 0.003f, 0.09f, 1f);
                s.NoiseBurst(0f, 900f, 2f, 0.002f, 0.03f, 0.3f);
                break;
            case Surface.Rubber:
                s.Sweep(0f, 130f, 80f, 0.002f, 0.03f, 0.8f);
                s.DullBurst(0f, 600f, 0.002f, 0.02f, 0.5f);
                break;
            case Surface.Player:
                s.DullBurst(0f, 1200f, 0.001f, 0.02f, 1f);
                s.Sweep(0f, 120f, 70f, 0.002f, 0.04f, 0.7f);
                s.NoiseBurst(0.002f, 2000f, 0.8f, 0.002f, 0.012f, 0.3f);
                break;
            case Surface.Mask:
                s.Struck(0f, s.Range(1500f, 1900f), PlasticRatios, 0.02f, 0.6f);
                s.NoiseBurst(0f, 3000f, 1.2f, 0.0002f, 0.002f, 0.8f);
                break;
        }
    }

    /// <summary>A ball bouncing off without breaking: the shell's "pock" and a little of what it hit.</summary>
    private static void Bounce(Synth s, Surface on)
    {
        if (on == Surface.Ground)
        {
            s.DullBurst(0f, 600f, 0.001f, 0.008f, 1f);
            s.Mode(0f, s.Range(1700f, 2000f), 0.002f, 0.2f);
            return;
        }

        s.Mode(0f, s.Range(1900f, 2500f), 0.004f, 0.8f);
        s.NoiseBurst(0f, 3000f, 1f, 0.0001f, 0.001f, 0.5f);
        switch (on)
        {
            case Surface.Metal:
                s.Struck(0f, s.Range(1100f, 1500f), MetalRatios, 0.08f, 0.35f);
                break;
            case Surface.Wood:
                s.Struck(0f, s.Range(220f, 300f), WoodRatios, 0.02f, 0.5f);
                break;
            case Surface.Glass:
                s.Struck(0f, s.Range(2200f, 2800f), GlassRatios, 0.09f, 0.3f);
                break;
            default:
                s.Mode(0f, s.Range(1000f, 1200f), 0.004f, 0.3f);
                break;
        }
    }

    // ---- Feet ----------------------------------------------------------------------------------------------------------

    /// <summary>A footstep: the heel coming down and the toe rolling after, in whatever the floor answers with.</summary>
    private static void Step(Synth s, Surface on, int variation)
    {
        float toe = s.Range(0.035f, 0.06f);
        foreach ((float at, float g) in new[] { (0f, 1f), (toe, s.Range(0.45f, 0.65f)) })
        {
            switch (on)
            {
                case Surface.Stone:
                    s.DullBurst(at, 900f, 0.001f, 0.011f, g);
                    s.NoiseBurst(at, 2300f, 1f, 0.0005f, 0.006f, 0.35f * g);
                    s.Sweep(at, 100f, 60f, 0.002f, 0.02f, 0.4f * g);
                    s.Grains(at, 0.05f, 5, 5000f, 1f, 0.0004f, 0.15f * g);
                    break;
                case Surface.Metal:
                    s.DullBurst(at, 1000f, 0.001f, 0.01f, 0.8f * g);
                    s.Struck(at, s.Range(210f, 300f), DeckRatios, 0.075f, 0.55f * g);
                    break;
                case Surface.Wood:
                    s.DullBurst(at, 700f, 0.001f, 0.012f, g);
                    s.Struck(at, s.Range(120f, 180f), WoodRatios, 0.045f, 0.6f * g);
                    break;
                case Surface.Gravel:
                    s.Grains(at, 0.11f, 45, 2200f, 0.8f, 0.0009f, 0.8f * g);
                    s.DullBurst(at, 500f, 0.002f, 0.015f, 0.5f * g);
                    break;
                case Surface.Ground:
                    s.DullBurst(at, 450f, 0.002f, 0.018f, g);
                    s.Grains(at + 0.003f, 0.06f, 12, 3000f, 1f, 0.0005f, 0.25f * g);
                    break;
                case Surface.Grass:
                    s.NoiseBurst(at, 4000f, 0.7f, 0.02f, 0.05f, 0.45f * g);
                    s.DullBurst(at + 0.01f, 400f, 0.003f, 0.02f, 0.7f * g);
                    break;
                case Surface.Tarp:
                    s.Grains(at, 0.12f, 30, 3000f, 1.2f, 0.0006f, 0.6f * g);
                    s.DullBurst(at, 500f, 0.002f, 0.012f, 0.6f * g);
                    break;
                case Surface.Water:
                    s.DullBurst(at, 1600f, 0.004f, 0.05f, 0.8f * g);
                    for (int k = 0; k < 4; k++)
                    {
                        float f = s.Range(480f, 1100f);
                        s.Sweep(at + s.Range(0.005f, 0.07f), f, f * 1.7f, 0.002f, 0.012f, 0.22f * g);
                    }

                    s.Grains(at + 0.05f, 0.16f, 18, 2600f, 1f, 0.0007f, 0.3f * g);
                    break;
                case Surface.Rubber:
                    s.DullBurst(at, 350f, 0.002f, 0.02f, g);
                    break;
            }
        }

        // Now and then a board creaks underfoot.
        if (on == Surface.Wood && variation % 3 == 1)
        {
            Creak(s, 520f, 160f, 900f, 0.18f, start: 0.06f, gain: 0.35f);
        }
    }

    /// <summary>Sliding on the knees and hip: a scrape over the floor, gritty or soft, rising and dying away.</summary>
    private static void Slide(Synth s, Surface on)
    {
        float centre = on switch { Surface.Ground => 800f, Surface.Gravel => 1400f, Surface.Metal => 1800f, _ => 1500f };
        var band = Biquad.BandPass(s.Rate, centre, 0.6f);
        for (int i = 0; i < s.Length; i++)
        {
            float t = s.T(i);
            s.Samples[i] += band.Process(s.Noise()) * Envelope.Swell(t, 0.03f, 0.3f, s.Seconds) * 1.6f;
        }

        switch (on)
        {
            case Surface.Gravel:
                s.Grains(0f, 0.5f, 110, 2000f, 0.8f, 0.0009f, 0.8f);
                break;
            case Surface.Metal:
                s.Grains(0f, 0.45f, 50, 3200f, 1f, 0.0005f, 0.4f);
                s.Struck(0f, 310f, DeckRatios, 0.2f, 0.25f);
                break;
            case Surface.Stone:
                s.Grains(0f, 0.45f, 80, 3000f, 1f, 0.0005f, 0.4f);
                break;
        }

        s.DullBurst(0f, 400f, 0.002f, 0.03f, 0.5f);
    }

    /// <summary>Pushing off for a jump: a scuff, the rustle of a jersey and the pods knocking on the harness.</summary>
    private static void Jump(Synth s)
    {
        s.DullBurst(0f, 700f, 0.002f, 0.01f, 0.6f);
        s.NoiseBurst(0.02f, 3000f, 0.6f, 0.02f, 0.05f, 0.35f);
        s.Grains(0.03f, 0.1f, 6, 2500f, 2f, 0.0008f, 0.3f);
    }

    /// <summary>Landing: a heavy thump, the floor's answer and the gear rattling.</summary>
    private static void Land(Synth s, Surface on)
    {
        s.Sweep(0f, 110f, 50f, 0.002f, 0.05f, 1f);
        s.DullBurst(0f, on == Surface.Ground ? 600f : 900f, 0.001f, 0.02f, 0.8f);
        s.Grains(0.01f, 0.15f, 10, 2200f, 2.5f, 0.001f, 0.35f);
        if (on == Surface.Metal)
        {
            s.Struck(0f, s.Range(160f, 220f), DeckRatios, 0.12f, 0.6f);
        }
        else if (on == Surface.Stone)
        {
            s.NoiseBurst(0f, 2000f, 1f, 0.0005f, 0.006f, 0.35f);
        }
    }

    // ---- Doors --------------------------------------------------------------------------------------------------------

    /// <summary>A hinge creaking: stick-slip squeaks whose rate wanders as the leaf swings, swelling and fading.</summary>
    private static void Creak(Synth s, float rate, float wander, float ring, float length, float start = 0f, float gain = 1f)
    {
        float phase = 0f, level = 0f, ringPhase = 0f, air = 0f;
        float drift = s.Range(0.8f, 1.6f), offset = s.Range(0f, 6f);
        int from = s.Index(start), to = s.Index(start + length);
        for (int i = from; i < to; i++)
        {
            float t = s.T(i) - start;
            float r = rate + wander * MathF.Sin(MathF.Tau * drift * t + offset + 0.8f * MathF.Sin(MathF.Tau * 0.45f * t));
            phase += r / s.Rate;
            if (phase >= 1f)
            {
                phase -= 1f;
                level = 0.55f + 0.45f * s.Random.NextFloat();
            }

            level *= MathF.Exp(-1f / (s.Rate * 0.0035f));
            ringPhase += ring / s.Rate;
            float envelope = MathF.Sin(MathF.PI * MathF.Min(1f, t / length));
            air = air * 0.97f + s.Noise() * 0.03f;
            s.Samples[i] += (level * MathF.Sin(MathF.Tau * ringPhase) * 0.5f + air * 0.6f) * envelope * gain;
        }
    }

    /// <summary>A wooden door banging shut: a low thump, the latch's click and a rattle.</summary>
    private static void Slam(Synth s)
    {
        s.Sweep(0f, 85f, 55f, 0.001f, 0.07f, 0.9f);
        s.DullBurst(0f, 900f, 0.001f, 0.035f, 0.9f);
        s.Struck(0f, s.Range(150f, 210f), WoodRatios, 0.06f, 0.5f);
        s.NoiseBurst(0.012f, 2600f, 1.5f, 0.0002f, 0.003f, 0.5f);
        s.Grains(0.02f, 0.12f, 8, 1800f, 2f, 0.002f, 0.25f);
    }

    /// <summary>A steel door dragged open: a lower, heavier creak over a groan.</summary>
    private static void SteelOpen(Synth s)
    {
        Creak(s, 140f, 60f, 820f, 0.95f, gain: 0.9f);
        var lowpass = Biquad.LowPass(s.Rate, 300f, 1f);
        float phase = 0f;
        for (int i = 0; i < s.Length; i++)
        {
            float t = s.T(i);
            phase += (68f + 8f * MathF.Sin(MathF.Tau * 1.1f * t)) / s.Rate;
            float saw = 2f * (phase - MathF.Floor(phase + 0.5f));
            s.Samples[i] += lowpass.Process(saw) * MathF.Sin(MathF.PI * MathF.Min(1f, t / s.Seconds)) * 0.35f;
        }
    }

    /// <summary>A steel door clanging shut: a struck sheet's clashing partials over the thump.</summary>
    private static void Clank(Synth s)
    {
        s.Struck(0f, s.Range(200f, 230f), MetalRatios, 0.32f, 0.5f);
        s.Sweep(0f, 72f, 60f, 0.001f, 0.06f, 0.6f);
        s.DullBurst(0f, 1200f, 0.001f, 0.025f, 0.8f);
        s.NoiseBurst(0.01f, 2600f, 1.5f, 0.0002f, 0.003f, 0.4f);
    }

    /// <summary>A swing door pushed open: a whump of air and its rubber edge.</summary>
    private static void SwingOpen(Synth s)
    {
        s.DullBurst(0f, 320f, 0.03f, 0.07f, 1f);
        s.DullBurst(0.02f, 900f, 0.002f, 0.015f, 0.4f);
    }

    /// <summary>A swing door settling: knocks back and forth, each smaller.</summary>
    private static void SwingFlap(Synth s)
    {
        float at = 0f, g = 1f, gap = s.Range(0.17f, 0.2f);
        for (int k = 0; k < 4; k++)
        {
            s.DullBurst(at, 500f, 0.002f, 0.02f, g);
            s.Struck(at, s.Range(140f, 170f), WoodRatios, 0.03f, 0.5f * g);
            at += gap;
            gap *= 0.86f;
            g *= 0.55f;
        }
    }

    /// <summary>A cold-room door: the latch, the seal letting go with a hiss, and the heavy leaf groaning round.</summary>
    private static void ColdOpen(Synth s)
    {
        s.Struck(0f, s.Range(750f, 850f), MetalRatios, 0.04f, 0.6f);
        s.NoiseBurst(0.04f, 2500f, 0.6f, 0.01f, 0.06f, 0.5f);
        Creak(s, 110f, 40f, 700f, 1.35f, start: 0.12f, gain: 1.8f);
    }

    /// <summary>A heavy insulated door or sliding leaf shutting: a deep thunk, a ring, and the latch dropping.</summary>
    private static void ColdShut(Synth s, bool latch)
    {
        s.Sweep(0f, 90f, 45f, 0.003f, 0.09f, 1f);
        s.DullBurst(0f, 600f, 0.001f, 0.05f, 0.8f);
        s.Struck(0f, s.Range(170f, 200f), MetalRatios, 0.25f, 0.25f);
        if (latch)
        {
            s.Struck(0.06f, s.Range(900f, 1000f), PlasticRatios, 0.025f, 0.5f);
        }
    }

    /// <summary>A sliding door rolling on its track: a rumble with a bump at each rail joint.</summary>
    private static void SlideRoll(Synth s)
    {
        var band = Biquad.BandPass(s.Rate, 260f, 1f);
        for (int i = 0; i < s.Length; i++)
        {
            float t = s.T(i);
            s.Samples[i] += band.Process(s.Noise()) * Envelope.Swell(t, 0.1f, 0.25f, s.Seconds) * 2.5f;
        }

        for (float at = 0.15f; at < s.Seconds - 0.2f; at += s.Range(0.22f, 0.3f))
        {
            s.DullBurst(at, 400f, 0.002f, 0.02f, 0.5f);
        }
    }

    /// <summary>A flap of a membrane: band-passed noise fluttering and dying.</summary>
    private static void Flap(Synth s, float start, float frequency, float decay, float gain)
    {
        var band = Biquad.BandPass(s.Rate, frequency, 1f);
        float flutter = s.Range(22f, 32f);
        int to = s.Index(start + decay * 6f);
        for (int i = s.Index(start); i < to; i++)
        {
            float t = s.T(i) - start;
            s.Samples[i] += band.Process(s.Noise()) * MathF.Exp(-t / decay) * (0.6f + 0.4f * MathF.Sin(MathF.Tau * flutter * t)) * gain * 4f;
        }
    }

    // ---- You ----------------------------------------------------------------------------------------------------------

    /// <summary>Hit: a ball breaking on you, close, a thump felt more than heard, and a faint ring in the ears.</summary>
    private static void HitTaken(Synth s)
    {
        Break(s, Surface.Player);
        s.Sweep(0f, 90f, 40f, 0.002f, 0.08f, 1f);
        s.Mode(0.02f, 3600f, 0.35f, 0.05f);
    }

    /// <summary>The hit marker's tick: you put someone out.</summary>
    private static void HitMarker(Synth s)
    {
        s.Mode(0f, 1850f, 0.012f, 0.8f);
        s.Mode(0f, 3700f, 0.006f, 0.3f);
        s.NoiseBurst(0f, 5000f, 1f, 0.0001f, 0.0006f, 0.4f);
    }

    // ---- The objectives -----------------------------------------------------------------------------------------------

    /// <summary>The case picked up: its handle clacking and a latch.</summary>
    private static void CaseTake(Synth s)
    {
        s.Struck(0f, 700f, DeckRatios, 0.03f, 0.7f);
        s.Struck(0.07f, 1100f, PlasticRatios, 0.02f, 0.5f);
        s.NoiseBurst(0.01f, 3200f, 0.7f, 0.01f, 0.04f, 0.2f);
    }

    /// <summary>The case hitting the ground.</summary>
    private static void CaseDrop(Synth s)
    {
        s.Sweep(0f, 130f, 70f, 0.002f, 0.04f, 0.9f);
        s.Struck(0f, s.Range(360f, 400f), WoodRatios, 0.05f, 0.6f);
        s.Grains(0.01f, 0.12f, 8, 1800f, 2f, 0.0015f, 0.3f);
    }

    /// <summary>A rising (or falling) run of bell-like notes.</summary>
    private static void Chime(Synth s, float[] notes, float gap, float decay)
    {
        for (int k = 0; k < notes.Length; k++)
        {
            float at = k * gap;
            s.Mode(at, notes[k], decay, 0.5f);
            s.Mode(at, notes[k] * 2f, decay * 0.5f, 0.18f);
            s.Mode(at, notes[k] * 3.01f, decay * 0.3f, 0.07f);
        }
    }

    /// <summary>A soft electronic beep.</summary>
    private static void Beep(Synth s, float frequency, float length)
    {
        for (int i = 0; i < s.Length; i++)
        {
            float t = s.T(i);
            float tone = MathF.Sin(MathF.Tau * frequency * t) + 0.25f * MathF.Sin(MathF.Tau * frequency * 3f * t);
            s.Samples[i] += tone * Envelope.Swell(t, 0.004f, 0.02f, length);
        }
    }

    /// <summary>A two-tone alarm, each tone a filtered square.</summary>
    private static void Alarm(Synth s, float low, float high, int tones, float length)
    {
        var lowpass = Biquad.LowPass(s.Rate, 2400f, 0.7f);
        for (int i = 0; i < s.Length; i++)
        {
            float t = s.T(i);
            int k = (int)(t / length);
            if (k >= tones)
            {
                break;
            }

            float f = k % 2 == 0 ? low : high;
            float square = MathF.Sin(MathF.Tau * f * t) > 0f ? 1f : -1f;
            s.Samples[i] += lowpass.Process(square) * Envelope.Swell(t - k * length, 0.005f, 0.02f, length * 0.92f) * 0.6f;
        }
    }

    /// <summary>A buzzer: pulses of a low, filtered square.</summary>
    private static void Buzz(Synth s, float frequency, int pulses, float length, float gap)
    {
        var lowpass = Biquad.LowPass(s.Rate, 1800f, 0.7f);
        for (int i = 0; i < s.Length; i++)
        {
            float t = s.T(i);
            int k = (int)(t / (length + gap));
            float local = t - k * (length + gap);
            float square = MathF.Sin(MathF.Tau * frequency * t) > 0f ? 1f : -1f;
            float gate = k < pulses ? Envelope.Swell(local, 0.006f, 0.02f, length) : 0f;
            s.Samples[i] += lowpass.Process(square) * gate * 0.6f;
        }
    }

    // ---- The round ------------------------------------------------------------------------------------------------------

    /// <summary>The breakout horn: two reeds a minor third apart, blasting and drooping a little.</summary>
    private static void Horn(Synth s)
    {
        var lowpass = Biquad.LowPass(s.Rate, 2800f, 0.8f);
        float a = 0f, b = 0f;
        for (int i = 0; i < s.Length; i++)
        {
            float t = s.T(i);
            float droop = 1f - 0.025f * MathF.Min(1f, t / 1.4f);
            a += 370f * droop / s.Rate;
            b += 440f * droop / s.Rate;
            float reed = 2f * (a - MathF.Floor(a + 0.5f)) + 2f * (b - MathF.Floor(b + 0.5f));
            s.Samples[i] += lowpass.Process(reed) * Envelope.Swell(t, 0.02f, 0.15f, s.Seconds - 0.05f) * 0.5f;
        }

        s.Saturate(2f);
    }

    /// <summary>A pea whistle: a high tone warbled by the pea rattling round, with breath through it, in blasts.</summary>
    private static void Whistle(Synth s, (float Start, float Length)[] blasts)
    {
        var breath = Biquad.BandPass(s.Rate, 3100f, 2f);
        float phase = 0f;
        float trill = s.Range(26f, 32f);
        for (int i = 0; i < s.Length; i++)
        {
            float t = s.T(i);
            float gate = 0f;
            foreach ((float start, float length) in blasts)
            {
                gate = MathF.Max(gate, Envelope.Swell(t - start, 0.012f, 0.03f, length));
            }

            float warble = MathF.Sin(MathF.Tau * trill * t);
            phase += (2950f + 160f * warble) / s.Rate;
            float tone = MathF.Sin(MathF.Tau * phase) * (0.75f + 0.25f * warble);
            s.Samples[i] += (tone * 0.7f + breath.Process(s.Noise()) * 1.2f) * gate;
        }
    }

    // ---- Menus ----------------------------------------------------------------------------------------------------------

    private static void UiClick(Synth s, float pitch)
    {
        s.NoiseBurst(0f, 4000f, 1.5f, 0.0001f, 0.0012f, 0.6f);
        s.Mode(0f, pitch, 0.01f, 0.5f);
    }

    private static void UiHover(Synth s) => s.Mode(0f, 2600f, 0.006f, 0.4f);

    // ---- Ambience -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Wind: brown noise through a band-pass whose centre drifts slowly, its strength swelling, with a thin whistle
    /// over it. The game swells and dulls it further with the gusts and with where you are.
    /// </summary>
    private static void Wind(Synth s)
    {
        float brown = 0f, centre = 400f;
        var band = Biquad.BandPass(s.Rate, centre, 0.55f);
        var whistle = Biquad.BandPass(s.Rate, 1700f, 7f);
        float p1 = s.Range(0f, 6f), p2 = s.Range(0f, 6f);
        for (int i = 0; i < s.Length; i++)
        {
            float t = s.T(i);
            brown = brown * 0.995f + s.Noise() * 0.06f;
            if ((i & 127) == 0)
            {
                centre = 380f + 220f * MathF.Sin(MathF.Tau * 0.11f * t + p1) + 90f * MathF.Sin(MathF.Tau * 0.37f * t + p2);
                band.TuneBandPass(s.Rate, centre, 0.55f);
            }

            float swell = 0.65f + 0.35f * MathF.Sin(MathF.Tau * 0.07f * t + p2) * MathF.Sin(MathF.Tau * 0.23f * t + p1);
            float thin = whistle.Process(s.Noise()) * (0.4f + 0.6f * MathF.Max(0f, MathF.Sin(MathF.Tau * 0.13f * t + p1))) * 0.9f;
            s.Samples[i] = band.Process(brown) * swell * 3f + thin;
        }
    }

    /// <summary>Distant traffic: a low rumble with cars swelling past now and then.</summary>
    private static void Traffic(Synth s)
    {
        float brown = 0f;
        var rumble = Biquad.LowPass(s.Rate, 170f, 0.7f);
        for (int i = 0; i < s.Length; i++)
        {
            brown = brown * 0.996f + s.Noise() * 0.05f;
            s.Samples[i] = rumble.Process(brown) * 2.5f;
        }

        for (float at = s.Range(0f, 1.5f); at < s.Seconds; at += s.Range(1.6f, 3.2f))
        {
            float length = s.Range(1.6f, 3f);
            var band = Biquad.BandPass(s.Rate, s.Range(300f, 800f), 0.8f);
            int to = Math.Min(s.Length, s.Index(at + length));
            for (int i = s.Index(at); i < to; i++)
            {
                float t = s.T(i) - at;
                float e = MathF.Sin(MathF.PI * t / length);
                s.Samples[i] += band.Process(s.Noise()) * e * e * 0.25f;
            }
        }
    }

    /// <summary>A quiet room: low air, barely there.</summary>
    private static void RoomTone(Synth s, float cutoff, float gain)
    {
        float brown = 0f;
        var lowpass = Biquad.LowPass(s.Rate, cutoff, 0.7f);
        for (int i = 0; i < s.Length; i++)
        {
            float t = s.T(i);
            brown = brown * 0.99f + s.Noise() * 0.08f;
            s.Samples[i] += lowpass.Process(brown) * (0.85f + 0.15f * MathF.Sin(MathF.Tau * 0.09f * t)) * gain * 2f;
        }
    }

    /// <summary>A big empty hall: a low drone of wind in the roof, and the roof sheets ticking and creaking now and then.</summary>
    private static void Hall(Synth s)
    {
        RoomTone(s, 180f, 0.8f);
        var drone = Biquad.BandPass(s.Rate, 380f, 1.2f);
        for (int i = 0; i < s.Length; i++)
        {
            float t = s.T(i);
            s.Samples[i] += drone.Process(s.Noise()) * (0.5f + 0.5f * MathF.Sin(MathF.Tau * 0.15f * t)) * 0.35f;
        }

        for (float at = s.Range(0.5f, 2f); at < s.Seconds - 0.5f; at += s.Range(1.8f, 3.5f))
        {
            s.Struck(at, s.Range(380f, 900f), DeckRatios, 0.12f, 0.12f);
        }
    }

    /// <summary>Water dripping somewhere in a room: plinks into a puddle.</summary>
    private static void Drips(Synth s)
    {
        RoomTone(s, 220f, 0.45f);
        for (float at = s.Range(0.2f, 1f); at < s.Seconds - 0.2f; at += s.Range(0.9f, 2.2f))
        {
            float f = s.Range(850f, 1400f);
            float g = s.Range(0.25f, 0.5f);
            s.Sweep(at, f, f * 1.8f, 0.0005f, 0.012f, g);
            s.Grains(at + 0.002f, 0.03f, 4, 3200f, 1.5f, 0.0004f, 0.08f * g);
        }
    }

    /// <summary>Pigeons up in the rafters: cooing in twos and threes, and a flutter now and then.</summary>
    private static void Pigeons(Synth s)
    {
        RoomTone(s, 200f, 0.6f);
        for (float at = s.Range(0.3f, 1.5f); at < s.Seconds - 2f; at += s.Range(2.5f, 4.5f))
        {
            int coos = 2 + (int)(s.Random.NextFloat() * 2f);
            float base_ = s.Range(330f, 420f);
            float t0 = at;
            for (int k = 0; k < coos; k++)
            {
                float length = k == coos - 1 ? s.Range(0.45f, 0.6f) : s.Range(0.22f, 0.3f);
                Coo(s, t0, base_, length, 0.35f);
                t0 += length + s.Range(0.08f, 0.14f);
            }
        }

        WingBeats(s, s.Range(3f, 6f), 0.6f, 0.25f);
    }

    /// <summary>A pigeon's "hoo": a soft, breathy tone rising then falling, with a little of its octave.</summary>
    private static void Coo(Synth s, float start, float pitch, float length, float gain)
    {
        float phase = 0f;
        var breath = Biquad.BandPass(s.Rate, pitch * 2f, 3f);
        int to = Math.Min(s.Length, s.Index(start + length));
        for (int i = s.Index(start); i < to; i++)
        {
            float t = (s.T(i) - start) / length;
            float f = pitch * (1f + 0.12f * MathF.Sin(MathF.PI * t) - 0.05f * t);
            phase += f / s.Rate;
            float e = MathF.Sin(MathF.PI * t);
            float tone = MathF.Sin(MathF.Tau * phase) + 0.3f * MathF.Sin(MathF.Tau * phase * 2f);
            s.Samples[i] += (tone + breath.Process(s.Noise()) * 1.5f) * e * e * gain;
        }
    }

    /// <summary>Wings beating: quick puffs of air, slowing.</summary>
    private static void WingBeats(Synth s, float start, float length, float gain)
    {
        float at = start, gap = s.Range(0.08f, 0.1f);
        while (at < start + length && at < s.Seconds - 0.05f)
        {
            s.NoiseBurst(at, s.Range(700f, 1400f), 0.8f, 0.008f, 0.02f, gain);
            at += gap;
            gap *= 1.05f;
        }
    }

    /// <summary>A draught whistling through broken windows: narrow, wandering whistles over a low rush.</summary>
    private static void Draught(Synth s)
    {
        RoomTone(s, 250f, 0.6f);
        float centre = 950f;
        var band = Biquad.BandPass(s.Rate, centre, 9f);
        float p = s.Range(0f, 6f);
        for (int i = 0; i < s.Length; i++)
        {
            float t = s.T(i);
            if ((i & 127) == 0)
            {
                centre = 950f + 220f * MathF.Sin(MathF.Tau * 0.09f * t + p) + 60f * MathF.Sin(MathF.Tau * 0.31f * t);
                band.TuneBandPass(s.Rate, centre, 9f);
            }

            float swell = 0.3f + 0.7f * MathF.Max(0f, MathF.Sin(MathF.Tau * 0.12f * t + p));
            s.Samples[i] += band.Process(s.Noise()) * swell * 1.4f;
        }
    }

    /// <summary>A cold, dead room: almost nothing, and the odd tick of metal contracting.</summary>
    private static void Cold(Synth s)
    {
        RoomTone(s, 120f, 0.35f);
        for (float at = s.Range(0.5f, 2f); at < s.Seconds - 0.3f; at += s.Range(2.5f, 5f))
        {
            s.Struck(at, s.Range(2000f, 3800f), MetalRatios, 0.03f, 0.08f);
        }
    }

    /// <summary>Mains hum: 50 Hz and its harmonics, beating slowly.</summary>
    private static void Hum(Synth s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            float t = s.T(i);
            float hum = MathF.Sin(MathF.Tau * 50f * t) * 0.5f + MathF.Sin(MathF.Tau * 100.15f * t) * 0.3f +
                        MathF.Sin(MathF.Tau * 150f * t) * 0.15f + MathF.Sin(MathF.Tau * 200.3f * t) * 0.08f;
            s.Samples[i] += hum;
        }

        RoomTone(s, 200f, 0.2f);
    }

    /// <summary>A crow: harsh, nasal "kaah"s falling in pitch, one to three of them.</summary>
    private static void Caws(Synth s, int count)
    {
        float at = 0.02f;
        for (int k = 0; k < count; k++)
        {
            float length = s.Range(0.24f, 0.34f);
            float pitch = s.Range(520f, 680f);
            var f1 = Biquad.BandPass(s.Rate, s.Range(1050f, 1250f), 2.5f);
            var f2 = Biquad.BandPass(s.Rate, s.Range(2100f, 2500f), 3f);
            float phase = 0f;
            int to = Math.Min(s.Length, s.Index(at + length));
            for (int i = s.Index(at); i < to; i++)
            {
                float t = (s.T(i) - at) / length;
                phase += pitch * (1.08f - 0.22f * t) / s.Rate;
                float saw = 2f * (phase - MathF.Floor(phase + 0.5f));
                float rough = 1f + 0.5f * MathF.Sin(MathF.Tau * 70f * (s.T(i) - at)) + 0.3f * s.Noise();
                float source = saw * rough;
                float e = MathF.Min(1f, t / 0.12f) * MathF.Min(1f, (1f - t) / 0.35f);
                s.Samples[i] += (f1.Process(source) * 2f + f2.Process(source) * 1.2f) * e;
            }

            at += length + s.Range(0.12f, 0.22f);
        }
    }

    private static void WingFlap(Synth s) => WingBeats(s, 0f, 0.8f, 1f);

    /// <summary>
    /// A train a long way off: its rumble swelling and fading, the wheels clacking over the rail joints in pairs, and
    /// the horn, all already dulled by distance.
    /// </summary>
    private static void Train(Synth s)
    {
        float brown = 0f;
        var rumble = Biquad.LowPass(s.Rate, 160f, 0.7f);
        for (int i = 0; i < s.Length; i++)
        {
            float t = s.T(i);
            brown = brown * 0.996f + s.Noise() * 0.05f;
            s.Samples[i] += rumble.Process(brown) * Envelope.Swell(t, 6f, 6f, s.Seconds) * 3f;
        }

        for (float at = 3f; at < s.Seconds - 3f; at += s.Range(0.85f, 0.95f))
        {
            float g = Envelope.Swell(at, 6f, 6f, s.Seconds) * 0.35f;
            s.DullBurst(at, 600f, 0.002f, 0.015f, g);
            s.DullBurst(at + 0.11f, 600f, 0.002f, 0.015f, g * 0.8f);
        }

        var horn = Biquad.LowPass(s.Rate, 1200f, 0.7f);
        float a = 0f, b = 0f;
        float start = s.Range(2.5f, 4f);
        int to = s.Index(start + 1.4f);
        for (int i = s.Index(start); i < to; i++)
        {
            float t = s.T(i) - start;
            a += 311f / s.Rate;
            b += 370f / s.Rate;
            float reed = 2f * (a - MathF.Floor(a + 0.5f)) + 2f * (b - MathF.Floor(b + 0.5f));
            s.Samples[i] += horn.Process(reed) * Envelope.Swell(t, 0.08f, 0.3f, 1.4f) * 0.35f;
        }
    }
}
