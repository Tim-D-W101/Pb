using Godot;
using Pb.Game.Core;

namespace Pb.Game.Audio;

/// <summary>
/// The mixer's buses, made once when the game starts:
/// <list type="bullet">
/// <item><b>World</b> (a reverb whose size and mix follow where you are) takes <b>Effects</b> and <b>Voices</b>;</item>
/// <item><b>Ambience</b>, with <b>Outside</b> under it (a low-pass that closes when you go indoors) for the wind, traffic,
/// crows and trains;</item>
/// <item><b>Menus</b>, and <b>Referee</b> (the referee's voice, dry, at the voices' volume);</item>
/// <item><b>Stinger</b>: the moment you're hit and your hits' confirms, dry and never muffled, at the effects' volume.</item>
/// </list>
/// World and Ambience each end in a low-pass, the <b>muffle</b> (<see cref="Muffle"/>): wide open, it closes for a moment
/// when you're hit. Your settings set Master, Effects (and Stinger), Voices (and Referee), Ambience and Menus.
/// </summary>
public static class AudioBuses
{
    public static readonly StringName World = "World";
    public static readonly StringName Effects = "Effects";
    public static readonly StringName Voices = "Voices";
    public static readonly StringName Ambience = "Ambience";
    public static readonly StringName Outside = "Outside";
    public static readonly StringName Menus = "Menus";
    public static readonly StringName Referee = "Referee";
    public static readonly StringName Stinger = "Stinger";

    /// <summary>
    /// Whether nothing can be heard (Godot's dummy driver, as in headless runs). Sounds are still made and counted, but not
    /// started: the dummy driver never retires a finished playback, so they'd pile up and outlive the game.
    /// </summary>
    public static bool Silent { get; } = AudioServer.GetDriverName() == "Dummy";

    /// <summary>Makes the buses if they aren't there yet. A bus can only send to one before it, hence the order.</summary>
    public static void Ensure()
    {
        if (AudioServer.GetBusIndex(World) >= 0)
        {
            return;
        }

        StringName master = AudioServer.GetBusName(0);
        Add(World, master);
        AudioServer.AddBusEffect(AudioServer.GetBusIndex(World), new AudioEffectReverb { RoomSize = 0.2f, Damping = 0.7f, Wet = 0.04f, Dry = 1f, Spread = 0.8f });
        Add(Effects, World);
        Add(Voices, World);
        Add(Ambience, master);
        Add(Outside, Ambience);
        AudioServer.AddBusEffect(AudioServer.GetBusIndex(Outside), new AudioEffectLowPassFilter { CutoffHz = 20000f, Resonance = 0.5f });
        Add(Menus, master);
        Add(Referee, master);
        Add(Stinger, master);
        AudioServer.AddBusEffect(AudioServer.GetBusIndex(World), new AudioEffectLowPassFilter { CutoffHz = Open, Resonance = 0.5f });
        AudioServer.AddBusEffect(AudioServer.GetBusIndex(Ambience), new AudioEffectLowPassFilter { CutoffHz = Open, Resonance = 0.5f });
        SetMuffleEnabled(false);
    }

    /// <summary>The muffle's cutoff when it's open (Hz): it then changes nothing (and is switched off).</summary>
    public const float Open = 20000f;

    /// <summary>
    /// Muffles everything in the world and the ambience above <paramref name="cutoffHz"/> (<see cref="Open"/> or more: not
    /// at all, and the filters are switched off). The stinger, the menus and the referee aren't muffled.
    /// </summary>
    public static void Muffle(float cutoffHz)
    {
        bool on = cutoffHz < Open;
        SetMuffleEnabled(on);
        foreach (StringName bus in Muffled)
        {
            if (MuffleOf(bus) is { } filter)
            {
                filter.CutoffHz = on ? cutoffHz : Open;
            }
        }
    }

    private static readonly StringName[] Muffled = { World, Ambience };

    private static void SetMuffleEnabled(bool on)
    {
        foreach (StringName name in Muffled)
        {
            int bus = AudioServer.GetBusIndex(name);
            int last = bus >= 0 ? AudioServer.GetBusEffectCount(bus) - 1 : -1;
            if (last >= 0 && AudioServer.GetBusEffect(bus, last) is AudioEffectLowPassFilter && AudioServer.IsBusEffectEnabled(bus, last) != on)
            {
                AudioServer.SetBusEffectEnabled(bus, last, on);
            }
        }
    }

    /// <summary>The muffle: the last effect on <paramref name="bus"/>.</summary>
    private static AudioEffectLowPassFilter? MuffleOf(StringName bus)
    {
        int index = AudioServer.GetBusIndex(bus);
        int count = index >= 0 ? AudioServer.GetBusEffectCount(index) : 0;
        return count > 0 ? AudioServer.GetBusEffect(index, count - 1) as AudioEffectLowPassFilter : null;
    }

    /// <summary>The world's reverb, whose size and mix <see cref="Audio.Ambience"/> sets by where you are.</summary>
    public static AudioEffectReverb? Reverb => Effect<AudioEffectReverb>(World);

    /// <summary>The low-pass on everything outside, closed indoors.</summary>
    public static AudioEffectLowPassFilter? OutsideFilter => Effect<AudioEffectLowPassFilter>(Outside);

    /// <summary>Sets every bus's volume from the settings.</summary>
    public static void Apply(GameSettings settings)
    {
        Ensure();
        Set(0, settings.Volume);
        Set(AudioServer.GetBusIndex(Effects), settings.EffectsVolume);
        Set(AudioServer.GetBusIndex(Stinger), settings.EffectsVolume);
        Set(AudioServer.GetBusIndex(Voices), settings.VoicesVolume);
        Set(AudioServer.GetBusIndex(Referee), settings.VoicesVolume);
        Set(AudioServer.GetBusIndex(Ambience), settings.AmbienceVolume);
        Set(AudioServer.GetBusIndex(Menus), settings.MenusVolume);
    }

    private static void Set(int bus, float volume)
    {
        if (bus >= 0)
        {
            AudioServer.SetBusVolumeDb(bus, volume <= 0.001f ? -80f : Mathf.LinearToDb(volume));
        }
    }

    private static void Add(StringName name, StringName sendTo)
    {
        int index = AudioServer.BusCount;
        AudioServer.AddBus(index);
        AudioServer.SetBusName(index, name);
        AudioServer.SetBusSend(index, sendTo);
    }

    private static T? Effect<T>(StringName bus) where T : AudioEffect
    {
        int index = AudioServer.GetBusIndex(bus);
        return index >= 0 && AudioServer.GetBusEffectCount(index) > 0 ? AudioServer.GetBusEffect(index, 0) as T : null;
    }
}
