using Godot;

namespace Pb.Game.Audio;

/// <summary>
/// The menus' sounds on the Menus bus: a click for a button, a softer tick for hovering one or moving a slider, a
/// toggle, a lower click for going back, and a chime for something won. <see cref="Ui.UiKit"/>'s controls play them;
/// they wait for the shared <see cref="SoundBank"/>, which the first menu starts rendering.
/// </summary>
public static class UiSounds
{
    private static AudioStreamPlayer? _player;
    private static SoundSet? _set;
    private static ulong _lastTick;
    private static int _pick;

    /// <summary>How loud the menus play (dB): presentation's volume plus its menu mix, set by the first scene that loads it.</summary>
    public static float Volume_db { get; set; } = -20f;

    /// <summary>How many variations the shared bank renders, if the menus are the first to start it.</summary>
    public static int Variations { get; set; } = 4;

    public static void Click() => Play(Sfx.UiClick);

    public static void Back() => Play(Sfx.UiBack);

    public static void Toggle() => Play(Sfx.UiToggle);

    public static void Hover() => Play(Sfx.UiHover, -6f);

    public static void Reward() => Play(Sfx.UiReward, 4f);

    /// <summary>A slider's tick, at most one every 60 ms however fast it's dragged.</summary>
    public static void Tick()
    {
        ulong now = Time.GetTicksMsec();
        if (now - _lastTick >= 60)
        {
            _lastTick = now;
            Play(Sfx.UiHover, -3f);
        }
    }

    public static void Play(Sfx sfx, float db = 0f)
    {
        SoundBank.Warm(Variations);
        if (SoundBank.Ready is not { } bank || Player() is not { } player)
        {
            return;
        }

        _set ??= new SoundSet(bank);
        player.Stream = _set.Get(sfx, _pick++);
        player.VolumeDb = Volume_db + db;
        if (!AudioBuses.Silent)
        {
            player.Play();
        }
    }

    /// <summary>The menus' player, under the tree's root so it outlives scene changes (null until it's in the tree).</summary>
    private static AudioStreamPlayer? Player()
    {
        if (_player is not null && GodotObject.IsInstanceValid(_player))
        {
            return _player.IsInsideTree() ? _player : null;
        }

        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            return null;
        }

        AudioBuses.Ensure();
        // Always processing: the pause menu clicks too, with the game paused under it.
        _player = new AudioStreamPlayer { Name = "MenuSounds", Bus = AudioBuses.Menus, MaxPolyphony = 4, ProcessMode = Node.ProcessModeEnum.Always };
        // It goes when the game does: its streams go with it.
        _player.TreeExiting += () =>
        {
            _player.Stop();
            _player.Stream = null;
            _set?.Release();
            _set = null;
        };
        tree.Root.CallDeferred(Node.MethodName.AddChild, _player);
        return null;
    }
}
