using System.Linq;
using Godot;

namespace Pb.Game.Core;

/// <summary>The settings' engine side: where they're kept (user://settings.json), their defaults from the data, and applying them to the window.</summary>
public sealed partial class GameSettings
{
    private const string Path = "user://settings.json";

    /// <summary>The input actions the settings' bindings can name (set by <see cref="InputSetup"/> when it loads input.jsonc).</summary>
    public static string[] KnownActions { get; set; } = System.Array.Empty<string>();

    /// <summary>Your settings, or a new player's: from user://settings.json, put right against the data's defaults.</summary>
    public static GameSettings Load(PresentationDef defaults)
    {
        string? json = FileAccess.FileExists(Path) ? FileAccess.GetFileAsString(Path) : null;
        GameSettings settings = FromJson(json, DefaultsFrom(defaults), out string? problem);
        if (problem is not null)
        {
            GD.PushWarning($"Ignoring unreadable {Path}: {problem}");
        }

        // A new profile id is kept from the start, so every screen reads the same one. An unreadable file is left as it
        // is, for you to put right, and so is everything before the input actions are known (bindings for actions it
        // didn't know yet would be lost).
        if (settings.ProfileIdMade && problem is null && KnownActions.Length > 0)
        {
            settings.Save();
        }

        return settings;
    }

    /// <summary>The defaults and choices presentation.jsonc gives.</summary>
    public static SettingsDefaults DefaultsFrom(PresentationDef view) => new()
    {
        MouseSensitivity = view.Look.MouseSensitivity_degPerCount,
        InvertY = view.Look.InvertY,
        StickSpeed = view.Look.StickSpeed_degps,
        StickCurve = view.Look.StickExponent,
        Vsync = view.Graphics.Vsync,
        GraphicsPresets = new[] { view.Graphics.DefaultPreset }.Concat(view.Graphics.Presets.Select(p => p.Name)).Distinct().ToArray(),
        RenderScales = view.Graphics.RenderScales,
        Fov = view.Camera.Fov_deg,
        FovMin = view.Camera.FovMin_deg,
        FovMax = view.Camera.FovMax_deg,
        Master = view.Audio.Buses.Master,
        Effects = view.Audio.Buses.Effects,
        Voices = view.Audio.Buses.Voices,
        Ambience = view.Audio.Buses.Ambience,
        Menus = view.Audio.Buses.Menus,
        Crosshair = view.Crosshair.Enabled,
        HeadBob = view.Camera.HeadBob,
        TeamColorSets = new[] { "standard" }.Concat(view.TeamColorSets.Select(s => s.Id)).ToArray(),
        Actions = KnownActions,
    };

    public void Save()
    {
        using FileAccess? file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        file?.StoreString(ToJson());
    }

    /// <summary>Applies the volumes to the audio buses.</summary>
    public void ApplyVolume() => Pb.Game.Audio.AudioBuses.Apply(this);

    /// <summary>Applies the window mode, v-sync and the frame cap (not in headless runs, which have no window).</summary>
    public void ApplyWindow()
    {
        Engine.MaxFps = FrameCap;
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }

        DisplayServer.WindowSetVsyncMode(Vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        DisplayServer.WindowMode mode = WindowMode switch
        {
            "fullscreen" => DisplayServer.WindowMode.ExclusiveFullscreen,
            "borderless" => DisplayServer.WindowMode.Fullscreen,
            _ => DisplayServer.WindowMode.Windowed,
        };
        if (DisplayServer.WindowGetMode() != mode)
        {
            DisplayServer.WindowSetMode(mode);
        }
    }
}
