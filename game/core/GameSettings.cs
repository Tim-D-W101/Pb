#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pb.Game.Core;

/// <summary>
/// Your preferences: controls and key bindings, video, audio, gameplay and accessibility, saved to
/// user://settings.json (<c>GameSettings.Godot.cs</c> reads, writes and applies them). This part is plain C#, so
/// <c>dotnet test</c> checks that old files load, bad values are put right and bindings survive a reload. Defaults come
/// from presentation.jsonc, through <see cref="SettingsDefaults"/>.
/// </summary>
public sealed partial class GameSettings
{
    /// <summary>
    /// Bumped when a default changes for players who already saved settings (1: v-sync on; 2: the full settings menu,
    /// whose new choices start at their defaults).
    /// </summary>
    public const int CurrentVersion = 2;

    public static readonly string[] WindowModes = { "windowed", "borderless", "fullscreen" };

    /// <summary>Ticks and a dot (the usual), ticks only, a dot only, or a ring round a dot.</summary>
    public static readonly string[] CrosshairStyles = { "cross-dot", "cross", "dot", "circle" };

    public static readonly string[] ShadowQualities = { "off", "low", "medium", "high" };

    /// <summary>Frame caps on offer (0: none).</summary>
    public static readonly int[] FrameCaps = { 0, 30, 60, 90, 120, 144, 165, 240 };

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    // ---- Controls

    public float MouseSensitivityDegPerCount { get; set; }

    public bool InvertY { get; set; }

    /// <summary>How fast the right stick turns you at full tilt (°/s).</summary>
    public float StickSpeedDegps { get; set; }

    /// <summary>The stick's response curve: the tilt is raised to this power (1: straight; higher: finer near the middle).</summary>
    public float StickCurve { get; set; }

    /// <summary>Crouch on a press and stand on the next, instead of holding.</summary>
    public bool CrouchToggle { get; set; }

    /// <summary>Walk on a press and stop walking on the next, instead of holding.</summary>
    public bool WalkToggle { get; set; }

    /// <summary>Your changes to input.jsonc's bindings, by action.</summary>
    public Dictionary<string, BindingOverride> Bindings { get; set; } = new();

    // ---- Video

    public string WindowMode { get; set; } = "windowed";

    public bool Vsync { get; set; }

    /// <summary>The most frames a second (0: no cap).</summary>
    public int FrameCap { get; set; }

    /// <summary>Graphics preset name (see presentation.jsonc "graphics").</summary>
    public string GraphicsPreset { get; set; } = "";

    /// <summary>The preset's parts you've changed (each null: as the preset has it).</summary>
    public GraphicsParts Graphics { get; set; } = new();

    /// <summary>Share of the screen's resolution the 3D view is drawn at (upscaled with FSR below 1).</summary>
    public float RenderScale { get; set; }

    public float FovDeg { get; set; }

    // ---- Audio

    /// <summary>Master volume, 0..1.</summary>
    public float Volume { get; set; } = 0.8f;

    /// <summary>The effects bus (shots, breaks, footsteps, doors), 0..1.</summary>
    public float EffectsVolume { get; set; } = 1f;

    /// <summary>The voices bus (callouts and the referee), 0..1.</summary>
    public float VoicesVolume { get; set; } = 1f;

    /// <summary>The ambience bus (wind, traffic, the rooms' tones, crows), 0..1.</summary>
    public float AmbienceVolume { get; set; } = 0.8f;

    /// <summary>The menus' clicks, 0..1.</summary>
    public float MenusVolume { get; set; } = 0.7f;

    /// <summary>Whether callouts and the referee are shown as subtitles.</summary>
    public bool Subtitles { get; set; } = true;

    /// <summary>The subtitles' size, as a share of the usual.</summary>
    public float SubtitleSize { get; set; } = 1f;

    // ---- Gameplay

    public bool Crosshair { get; set; }

    /// <summary>One of <see cref="CrosshairStyles"/>.</summary>
    public string CrosshairStyle { get; set; } = "cross-dot";

    /// <summary>The crosshair's colour, #rrggbb ("" for presentation.jsonc's).</summary>
    public string CrosshairColor { get; set; } = "";

    /// <summary>The crosshair's size, as a share of presentation.jsonc's.</summary>
    public float CrosshairSize { get; set; } = 1f;

    /// <summary>Whether the crosshair flashes when your ball puts someone out.</summary>
    public bool HitMarker { get; set; } = true;

    public bool HeadBob { get; set; }

    /// <summary>How hard a ball hitting you jolts the view, as a share of presentation.jsonc's (0: not at all).</summary>
    public float CameraJolt { get; set; } = 1f;

    /// <summary>How much paint on your mask covers the view, as a share of presentation.jsonc's.</summary>
    public float MaskSpray { get; set; } = 1f;

    // ---- Accessibility

    /// <summary>Which team colour set (presentation.jsonc "teamColorSets"; "standard" is "teamColors").</summary>
    public string TeamColors { get; set; } = "standard";

    /// <summary>The HUD's size, as a share of the usual.</summary>
    public float HudScale { get; set; } = 1f;

    // ---- Playing with others

    /// <summary>The longest name you can play under, and how many addresses you joined are remembered.</summary>
    public const int NameLength = 24;

    public const int Recent = 5;

    /// <summary>The name you play under with others (empty until you type one).</summary>
    public string PlayerName { get; set; } = "";

    /// <summary>Which of the characters you play.</summary>
    public int PlayerLook { get; set; }

    /// <summary>The addresses you joined last, the latest first.</summary>
    public List<string> RecentAddresses { get; set; } = new();

    /// <summary>A pretend round trip added to your connection when you host or join (ms), to feel what lag does.</summary>
    public float PretendLag_ms { get; set; }

    /// <summary>
    /// Who you are when you play with others, offline: made once and kept, so a host knows you again when you rejoin
    /// (32 hexadecimal digits; anything else is replaced).
    /// </summary>
    public string ProfileId { get; set; } = "";

    /// <summary>The id was made as these settings were read (the file had none), so they want saving to keep it.</summary>
    [JsonIgnore]
    public bool ProfileIdMade { get; private set; }

    /// <summary>Which defaults these settings have caught up with (<see cref="CurrentVersion"/>).</summary>
    public int Version { get; set; }

    /// <summary>An address you joined, remembered first (once).</summary>
    public void RememberAddress(string address)
    {
        string a = address.Trim();
        if (a.Length == 0)
        {
            return;
        }

        RecentAddresses.RemoveAll(r => string.Equals(r, a, StringComparison.OrdinalIgnoreCase));
        RecentAddresses.Insert(0, a);
        if (RecentAddresses.Count > Recent)
        {
            RecentAddresses.RemoveRange(Recent, RecentAddresses.Count - Recent);
        }
    }

    /// <summary>A name as typed, made fit to play under: no control characters, trimmed, at most <see cref="NameLength"/> long.</summary>
    public static string CleanName(string? name)
    {
        string clean = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length > NameLength ? clean[..NameLength].Trim() : clean;
    }

    /// <summary>A new player's settings.</summary>
    public static GameSettings Defaults(SettingsDefaults d) => new()
    {
        MouseSensitivityDegPerCount = d.MouseSensitivity,
        InvertY = d.InvertY,
        StickSpeedDegps = d.StickSpeed,
        StickCurve = d.StickCurve,
        Vsync = d.Vsync,
        GraphicsPreset = d.GraphicsPresets[0],
        RenderScale = d.RenderScales[0],
        FovDeg = d.Fov,
        Volume = d.Master,
        EffectsVolume = d.Effects,
        VoicesVolume = d.Voices,
        AmbienceVolume = d.Ambience,
        MenusVolume = d.Menus,
        Crosshair = d.Crosshair,
        HeadBob = d.HeadBob,
        Version = CurrentVersion,
        ProfileId = Guid.NewGuid().ToString("N"),
        ProfileIdMade = true,
    };

    /// <summary>
    /// Settings from a saved file's text, brought up to date and put right (null or unreadable text gives the defaults,
    /// with <paramref name="problem"/> saying why).
    /// </summary>
    public static GameSettings FromJson(string? json, SettingsDefaults d, out string? problem)
    {
        problem = null;
        GameSettings settings = Defaults(d);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                GameSettings? saved = JsonSerializer.Deserialize<GameSettings>(json, Json);
                if (saved is not null)
                {
                    settings = saved;
                    settings.CatchUp(d);
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
            {
                problem = ex.Message;
            }
        }

        settings.Clamp(d);
        return settings;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Puts every value in its range and every choice among the choices; anything nonsensical goes back to its default.</summary>
    public void Clamp(SettingsDefaults d)
    {
        MouseSensitivityDegPerCount = Range(MouseSensitivityDegPerCount, 0.001f, 2f, d.MouseSensitivity);
        StickSpeedDegps = Range(StickSpeedDegps, 30f, 1000f, d.StickSpeed);
        StickCurve = Range(StickCurve, 1f, 4f, d.StickCurve);
        Bindings = Bindings?.Where(b => d.Actions.Contains(b.Key) && b.Value is not null)
            .ToDictionary(b => b.Key, b => b.Value, StringComparer.Ordinal) ?? new Dictionary<string, BindingOverride>(StringComparer.Ordinal);

        WindowMode = OneOf(WindowMode, WindowModes, WindowModes[0]);
        FrameCap = FrameCaps.Contains(FrameCap) ? FrameCap : FrameCaps.OrderBy(c => Math.Abs(c - FrameCap)).First();
        GraphicsPreset = OneOf(GraphicsPreset, d.GraphicsPresets, d.GraphicsPresets[0]);
        Graphics ??= new GraphicsParts();
        Graphics.Clamp();
        RenderScale = d.RenderScales.Contains(RenderScale) ? RenderScale : d.RenderScales.OrderBy(s => Math.Abs(s - RenderScale)).First();
        FovDeg = Range(FovDeg, d.FovMin, d.FovMax, d.Fov);

        Volume = Range(Volume, 0f, 1f, d.Master);
        EffectsVolume = Range(EffectsVolume, 0f, 1f, d.Effects);
        VoicesVolume = Range(VoicesVolume, 0f, 1f, d.Voices);
        AmbienceVolume = Range(AmbienceVolume, 0f, 1f, d.Ambience);
        MenusVolume = Range(MenusVolume, 0f, 1f, d.Menus);
        SubtitleSize = Range(SubtitleSize, 0.75f, 2f, 1f);

        CrosshairStyle = OneOf(CrosshairStyle, CrosshairStyles, CrosshairStyles[0]);
        CrosshairColor = IsColour(CrosshairColor) ? CrosshairColor.ToLowerInvariant() : "";
        CrosshairSize = Range(CrosshairSize, 0.5f, 2.5f, 1f);
        CameraJolt = Range(CameraJolt, 0f, 1f, 1f);
        MaskSpray = Range(MaskSpray, 0f, 1f, 1f);

        TeamColors = OneOf(TeamColors, d.TeamColorSets, "standard");
        HudScale = Range(HudScale, 0.75f, 1.5f, 1f);

        PlayerName = CleanName(PlayerName);
        PlayerLook = Math.Clamp(PlayerLook, 0, 15);
        RecentAddresses = (RecentAddresses ?? new List<string>()).Where(a => !string.IsNullOrWhiteSpace(a) && a.Length <= 80).Select(a => a.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(Recent).ToList();
        PretendLag_ms = float.IsFinite(PretendLag_ms) ? Math.Clamp(PretendLag_ms, 0f, 500f) : 0f;
        if (!IsProfileId(ProfileId))
        {
            ProfileId = Guid.NewGuid().ToString("N");
            ProfileIdMade = true;
        }
    }

    private static bool IsProfileId(string? id) => id is { Length: 32 } && id.All(Uri.IsHexDigit);

    /// <summary>Settings saved before a default changed take the new default.</summary>
    private void CatchUp(SettingsDefaults d)
    {
        // Saved before v-sync defaulted on: it was off without anyone choosing that.
        if (Version < 1)
        {
            Vsync = d.Vsync;
        }

        // Saved before the full settings menu: the stick's settings weren't saved at all.
        if (Version < 2)
        {
            StickSpeedDegps = d.StickSpeed;
            StickCurve = d.StickCurve;
        }

        Version = CurrentVersion;
    }

    /// <summary>
    /// Clamped into [min, max]; not a number, or nothing where something is needed (0 below a positive minimum, as an
    /// old file leaves a setting it didn't have), gives the fallback.
    /// </summary>
    private static float Range(float value, float min, float max, float fallback)
    {
        if (!float.IsFinite(value) || (value <= 0f && min > 0f))
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }

    private static string OneOf(string? value, IReadOnlyCollection<string> choices, string fallback) =>
        value is not null && choices.Contains(value) ? value : fallback;

    private static bool IsColour(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
}

/// <summary>The graphics preset's parts you can change on their own: each null keeps the preset's choice.</summary>
public sealed class GraphicsParts
{
    /// <summary>Off, low, medium or high (see <see cref="GameSettings.ShadowQualities"/>).</summary>
    public string? Shadows { get; set; }

    public bool? AmbientOcclusion { get; set; }

    public bool? Glow { get; set; }

    /// <summary>How thick the weeds grow, 0..1 of the preset's.</summary>
    public float? Weeds { get; set; }

    /// <summary>Shafts of light through the windows and holes in the roofs.</summary>
    public bool? Sunbeams { get; set; }

    /// <summary>Puddles, litter, drips and things lying on the floors.</summary>
    public bool? GroundDetail { get; set; }

    public void Clamp()
    {
        if (Shadows is not null && !GameSettings.ShadowQualities.Contains(Shadows))
        {
            Shadows = null;
        }

        if (Weeds is { } weeds)
        {
            Weeds = float.IsFinite(weeds) ? Math.Clamp(weeds, 0f, 1f) : null;
        }
    }
}

/// <summary>The settings' defaults and choices, from presentation.jsonc and input.jsonc.</summary>
public sealed class SettingsDefaults
{
    public required float MouseSensitivity { get; init; }

    public required bool InvertY { get; init; }

    public required float StickSpeed { get; init; }

    public required float StickCurve { get; init; }

    public required bool Vsync { get; init; }

    /// <summary>The graphics presets' names, the default first.</summary>
    public required IReadOnlyList<string> GraphicsPresets { get; init; }

    /// <summary>The render scales on offer, the default first.</summary>
    public required IReadOnlyList<float> RenderScales { get; init; }

    public required float Fov { get; init; }

    public required float FovMin { get; init; }

    public required float FovMax { get; init; }

    public required float Master { get; init; }

    public required float Effects { get; init; }

    public required float Voices { get; init; }

    public required float Ambience { get; init; }

    public required float Menus { get; init; }

    public required bool Crosshair { get; init; }

    public required bool HeadBob { get; init; }

    /// <summary>The team colour sets' ids ("standard" and those in presentation.jsonc "teamColorSets").</summary>
    public required IReadOnlyList<string> TeamColorSets { get; init; }

    /// <summary>The input actions (input.jsonc), so bindings for actions that are gone can be dropped.</summary>
    public required IReadOnlyCollection<string> Actions { get; init; }
}
