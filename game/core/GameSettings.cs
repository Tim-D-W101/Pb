using System;
using System.Text.Json;
using Godot;

namespace Pb.Game.Core;

/// <summary>
/// Player preferences (FOV, sensitivity, crosshair…), saved to user://settings.json. Defaults
/// come from presentation.jsonc. The full settings menu arrives in Phase 5; Phase 1 changes
/// these with hotkeys.
/// </summary>
public sealed class GameSettings
{
    private const string Path = "user://settings.json";

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

    public static GameSettings Load(PresentationDef defaults)
    {
        var settings = new GameSettings
        {
            FovDeg = defaults.Camera.Fov_deg,
            MouseSensitivityDegPerCount = defaults.Look.MouseSensitivity_degPerCount,
            InvertY = defaults.Look.InvertY,
            Crosshair = defaults.Crosshair.Enabled,
            HeadBob = defaults.Camera.HeadBob,
            Vsync = false,
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

        settings.FovDeg = Math.Clamp(settings.FovDeg, defaults.Camera.FovMin_deg, defaults.Camera.FovMax_deg);
        settings.MouseSensitivityDegPerCount = Math.Clamp(settings.MouseSensitivityDegPerCount, 0.001f, 2f);
        return settings;
    }

    public void Save()
    {
        using FileAccess? file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        file?.StoreString(JsonSerializer.Serialize(this, Json));
    }
}
