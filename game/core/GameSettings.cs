using System;
using System.Text.Json;
using Godot;

namespace Pb.Game.Core;

/// <summary>
/// Player preferences (FOV, sensitivity, crosshair, graphics, volume…), saved to
/// user://settings.json. Defaults come from presentation.jsonc. The pause menu edits the main ones;
/// the full settings menu with key rebinding arrives in Phase 3.
/// </summary>
public sealed class GameSettings
{
    private const string Path = "user://settings.json";

    /// <summary>Bumped when a default changes for players who already saved settings (1: v-sync on).</summary>
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public float FovDeg { get; set; }

    public float MouseSensitivityDegPerCount { get; set; }

    public bool InvertY { get; set; }

    public bool Crosshair { get; set; }

    public bool HeadBob { get; set; }

    public bool Vsync { get; set; }

    /// <summary>Graphics preset name (see presentation.jsonc "graphics").</summary>
    public string GraphicsPreset { get; set; } = "";

    /// <summary>Share of the screen's resolution the 3D view is drawn at (upscaled with FSR below 1).</summary>
    public float RenderScale { get; set; }

    /// <summary>Master volume, 0..1.</summary>
    public float Volume { get; set; } = 0.8f;

    /// <summary>Which defaults these settings have caught up with (<see cref="CurrentVersion"/>).</summary>
    public int Version { get; set; }

    /// <summary>Applies the master volume to the audio bus.</summary>
    public void ApplyVolume() =>
        AudioServer.SetBusVolumeDb(0, Volume <= 0.001f ? -80f : Mathf.LinearToDb(Volume));

    public static GameSettings Load(PresentationDef defaults)
    {
        var settings = new GameSettings
        {
            FovDeg = defaults.Camera.Fov_deg,
            MouseSensitivityDegPerCount = defaults.Look.MouseSensitivity_degPerCount,
            InvertY = defaults.Look.InvertY,
            Crosshair = defaults.Crosshair.Enabled,
            HeadBob = defaults.Camera.HeadBob,
            Vsync = defaults.Graphics.Vsync,
            GraphicsPreset = defaults.Graphics.DefaultPreset,
            RenderScale = defaults.Graphics.RenderScales[0],
            Version = CurrentVersion,
        };

        if (!FileAccess.FileExists(Path))
        {
            return settings;
        }

        try
        {
            using FileAccess? file = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
            GameSettings? saved = file is null ? null : JsonSerializer.Deserialize<GameSettings>(file.GetAsText(), Json);
            if (saved is not null)
            {
                settings = saved;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            GD.PushWarning($"Ignoring unreadable {Path}: {ex.Message}");
        }

        if (string.IsNullOrEmpty(settings.GraphicsPreset))
        {
            settings.GraphicsPreset = defaults.Graphics.DefaultPreset;
        }

        // Settings saved before v-sync defaulted on had it off without anyone choosing that.
        if (settings.Version < 1)
        {
            settings.Vsync = defaults.Graphics.Vsync;
        }

        settings.Version = CurrentVersion;

        if (settings.RenderScale is <= 0f or > 1f)
        {
            settings.RenderScale = defaults.Graphics.RenderScales[0];
        }

        settings.FovDeg = Math.Clamp(settings.FovDeg, defaults.Camera.FovMin_deg, defaults.Camera.FovMax_deg);
        settings.MouseSensitivityDegPerCount = Math.Clamp(settings.MouseSensitivityDegPerCount, 0.001f, 2f);
        settings.Volume = Math.Clamp(settings.Volume, 0f, 1f);
        return settings;
    }

    public void Save()
    {
        using FileAccess? file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        file?.StoreString(JsonSerializer.Serialize(this, Json));
    }
}
