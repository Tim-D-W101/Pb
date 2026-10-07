using Godot;
using Pb.Game.Core;

namespace Pb.Game.Audio;

/// <summary>
/// The mixer's buses, made once when the game starts:
/// <list type="bullet">
/// <item><b>World</b> (a reverb whose size and mix follow where you are) takes <b>Effects</b> and <b>Voices</b>;</item>
/// <item><b>Ambience</b>, with <b>Outside</b> under it (a low-pass that closes when you go indoors) for the wind, traffic,
/// crows and trains;</item>
/// <item><b>Menus</b>, and <b>Referee</b> (the referee's voice, dry, at the voices' volume).</item>
/// </list>
/// Your settings set Master, Effects, Voices (and Referee), Ambience and Menus.
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
