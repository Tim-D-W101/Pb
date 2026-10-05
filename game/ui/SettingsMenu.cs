using System;
using System.Linq;
using Godot;
using Pb.Game.Core;

namespace Pb.Game.Ui;

/// <summary>
/// The settings, in tabs, the same in the main menu and the pause menu: <b>Controls</b> (sensitivity, the stick, crouch
/// and walk as hold or toggle, and every binding), <b>Video</b> (window, v-sync, frame cap, the graphics preset and its
/// parts, render scale, field of view), <b>Audio</b> (the volumes, subtitles), <b>Gameplay</b> (crosshair, hit
/// marker, head-bob, camera jolt, mask spray) and <b>Accessibility</b> (team colours, HUD size). Every change is saved
/// at once; the owner applies what's its to apply (graphics, the HUD).
/// </summary>
public partial class SettingsMenu : VBoxContainer
{
    private static readonly (string Name, string Hex)[] CrosshairColours =
    {
        ("As the game has it", ""), ("White", "#ffffff"), ("Yellow", "#ffe14d"), ("Green", "#5cff6e"), ("Cyan", "#4de8ff"),
        ("Magenta", "#ff5cf0"), ("Red", "#ff4d4d"),
    };

    private static readonly (string Name, float? Value)[] WeedChoices = { ("As the preset", null), ("None", 0f), ("Sparse", 0.5f), ("Thick", 1f) };

    private GameSettings _settings = null!;
    private PresentationDef _view = null!;
    private Action<GameSettings>? _graphicsChanged;
    private Action<GameSettings>? _hudChanged;
    private BindingsList? _bindings;

    /// <summary>The tabs, for tests and the menu tour.</summary>
    public TabContainer Tabs { get; private set; } = null!;

    /// <summary>Whether a binding is waiting for a key (Esc then cancels that, not the menu).</summary>
    public bool Capturing => _bindings?.Capturing == true;

    /// <param name="graphicsChanged">Applies a change of preset, its parts or the render scale.</param>
    /// <param name="hudChanged">Applies a change to the crosshair or the HUD's size.</param>
    /// <param name="extra">Adds anything more to the Accessibility tab (the main menu's "open every level").</param>
    public void Build(GameSettings settings, PresentationDef view, Action<GameSettings>? graphicsChanged = null, Action<GameSettings>? hudChanged = null,
        Action<VBoxContainer>? extra = null)
    {
        _settings = settings;
        _view = view;
        _graphicsChanged = graphicsChanged;
        _hudChanged = hudChanged;
        AddThemeConstantOverride("separation", 12);
        AddChild(UiKit.Title("Settings", 34));
        Tabs = new TabContainer { CustomMinimumSize = new Vector2(900, 560), Name = "Tabs" };
        AddChild(Tabs);
        Tabs.AddChild(Page("Controls", Controls));
        Tabs.AddChild(Page("Video", Video));
        Tabs.AddChild(Page("Audio", Audio));
        Tabs.AddChild(Page("Gameplay", Gameplay));
        Tabs.AddChild(Page("Accessibility", page =>
        {
            Accessibility(page);
            extra?.Invoke(page);
        }));
    }

    private void Controls(VBoxContainer page)
    {
        page.AddChild(UiKit.SliderRow("Mouse sensitivity", 0.01, 0.4, 0.005, _settings.MouseSensitivityDegPerCount, v => Set(() => _settings.MouseSensitivityDegPerCount = (float)v),
            v => $"{v:0.000}°"));
        page.AddChild(UiKit.CheckRow("Invert Y", _settings.InvertY, on => Set(() => _settings.InvertY = on)));
        page.AddChild(UiKit.SliderRow("Stick speed", 60, 600, 10, _settings.StickSpeedDegps, v => Set(() => _settings.StickSpeedDegps = (float)v), v => $"{v:0}°/s"));
        page.AddChild(UiKit.SliderRow("Stick response", 1, 4, 0.1, _settings.StickCurve, v => Set(() => _settings.StickCurve = (float)v),
            v => v < 1.3 ? "straight" : v < 2.4 ? "curved" : "fine"));
        page.AddChild(UiKit.CheckRow("Crouch toggles", _settings.CrouchToggle, on => Set(() => _settings.CrouchToggle = on)));
        page.AddChild(UiKit.CheckRow("Walk toggles", _settings.WalkToggle, on => Set(() => _settings.WalkToggle = on)));
        if (InputSetup.Current is { } input)
        {
            page.AddChild(UiKit.Body("BINDINGS", 18, UiKit.Accent));
            _bindings = new BindingsList { Name = "Bindings" };
            page.AddChild(_bindings);
            _bindings.Build(_settings, input);
        }
    }

    private void Video(VBoxContainer page)
    {
        string[] modes = { "Windowed", "Borderless full screen", "Full screen" };
        page.AddChild(UiKit.OptionRow("Window", modes, Math.Max(0, Array.IndexOf(GameSettings.WindowModes, _settings.WindowMode)), i => Set(() =>
        {
            _settings.WindowMode = GameSettings.WindowModes[i];
            _settings.ApplyWindow();
        })));
        page.AddChild(UiKit.CheckRow("V-sync", _settings.Vsync, on => Set(() =>
        {
            _settings.Vsync = on;
            _settings.ApplyWindow();
        })));
        page.AddChild(UiKit.OptionRow("Frame cap", GameSettings.FrameCaps.Select(c => c == 0 ? "None" : $"{c} fps").ToArray(),
            Math.Max(0, Array.IndexOf(GameSettings.FrameCaps, _settings.FrameCap)), i => Set(() =>
            {
                _settings.FrameCap = GameSettings.FrameCaps[i];
                _settings.ApplyWindow();
            })));

        string[] presets = _view.Graphics.Presets.Select(p => p.Name).ToArray();
        page.AddChild(UiKit.OptionRow("Graphics", presets.Select(Capitalise).ToArray(), Math.Max(0, Array.IndexOf(presets, _settings.GraphicsPreset)),
            i => Graphics(() => _settings.GraphicsPreset = presets[i])));
        GraphicsParts parts = _settings.Graphics;
        string[] shadows = new[] { "As the preset" }.Concat(GameSettings.ShadowQualities.Select(Capitalise)).ToArray();
        page.AddChild(UiKit.OptionRow("  Shadows", shadows, parts.Shadows is null ? 0 : Array.IndexOf(GameSettings.ShadowQualities, parts.Shadows) + 1,
            i => Graphics(() => parts.Shadows = i == 0 ? null : GameSettings.ShadowQualities[i - 1])));
        page.AddChild(Tristate("  Ambient occlusion", parts.AmbientOcclusion, v => Graphics(() => parts.AmbientOcclusion = v)));
        page.AddChild(Tristate("  Glow", parts.Glow, v => Graphics(() => parts.Glow = v)));
        page.AddChild(Tristate("  Sunbeams", parts.Sunbeams, v => Graphics(() => parts.Sunbeams = v)));
        page.AddChild(Tristate("  Ground detail", parts.GroundDetail, v => Graphics(() => parts.GroundDetail = v)));
        page.AddChild(UiKit.OptionRow("  Weeds", WeedChoices.Select(w => w.Name).ToArray(), Math.Max(0, Array.FindIndex(WeedChoices, w => w.Value == parts.Weeds)),
            i => Graphics(() => parts.Weeds = WeedChoices[i].Value)));

        float[] scales = _view.Graphics.RenderScales;
        int scale = Math.Max(0, Array.FindIndex(scales, s => Math.Abs(s - _settings.RenderScale) < 0.001f));
        page.AddChild(UiKit.OptionRow("Render scale", scales.Select(s => s >= 0.999f ? "100% (sharpest)" : $"{s * 100:0}% (faster)").ToArray(), scale,
            i => Graphics(() => _settings.RenderScale = scales[i])));
        page.AddChild(UiKit.SliderRow("Field of view", _view.Camera.FovMin_deg, _view.Camera.FovMax_deg, _view.Camera.FovStep_deg, _settings.FovDeg,
            v => Set(() => _settings.FovDeg = (float)v), v => $"{v:0}°"));
    }

    private void Audio(VBoxContainer page)
    {
        void Volume(string name, float value, Action<float> set) => page.AddChild(UiKit.SliderRow(name, 0, 1, 0.05, value, v => Set(() =>
        {
            set((float)v);
            _settings.ApplyVolume();
        }), v => $"{v * 100:0}%"));

        Volume("Volume", _settings.Volume, v => _settings.Volume = v);
        Volume("Effects", _settings.EffectsVolume, v => _settings.EffectsVolume = v);
        Volume("Voices", _settings.VoicesVolume, v => _settings.VoicesVolume = v);
        Volume("Ambience", _settings.AmbienceVolume, v => _settings.AmbienceVolume = v);
        Volume("Menus", _settings.MenusVolume, v => _settings.MenusVolume = v);
        page.AddChild(UiKit.CheckRow("Subtitles", _settings.Subtitles, on => Set(() => _settings.Subtitles = on)));
        page.AddChild(UiKit.SliderRow("Subtitle size", 0.75, 2, 0.05, _settings.SubtitleSize, v => Set(() => _settings.SubtitleSize = (float)v), v => $"{v * 100:0}%"));
    }

    private void Gameplay(VBoxContainer page)
    {
        page.AddChild(UiKit.CheckRow("Crosshair", _settings.Crosshair, on => Hud(() => _settings.Crosshair = on)));
        string[] styles = { "Ticks and a dot", "Ticks", "Dot", "Ring and dot" };
        page.AddChild(UiKit.OptionRow("  Style", styles, Math.Max(0, Array.IndexOf(GameSettings.CrosshairStyles, _settings.CrosshairStyle)),
            i => Hud(() => _settings.CrosshairStyle = GameSettings.CrosshairStyles[i])));
        page.AddChild(UiKit.OptionRow("  Colour", CrosshairColours.Select(c => c.Name).ToArray(),
            Math.Max(0, Array.FindIndex(CrosshairColours, c => c.Hex == _settings.CrosshairColor)), i => Hud(() => _settings.CrosshairColor = CrosshairColours[i].Hex)));
        page.AddChild(UiKit.SliderRow("  Size", 0.5, 2.5, 0.05, _settings.CrosshairSize, v => Hud(() => _settings.CrosshairSize = (float)v), v => $"{v * 100:0}%"));
        page.AddChild(UiKit.CheckRow("Hit marker", _settings.HitMarker, on => Set(() => _settings.HitMarker = on)));
        page.AddChild(UiKit.CheckRow("Head-bob", _settings.HeadBob, on => Set(() => _settings.HeadBob = on)));
        page.AddChild(UiKit.SliderRow("Camera jolt", 0, 1, 0.05, _settings.CameraJolt, v => Set(() => _settings.CameraJolt = (float)v), v => $"{v * 100:0}%"));
        page.AddChild(UiKit.SliderRow("Paint on your mask", 0, 1, 0.05, _settings.MaskSpray, v => Set(() => _settings.MaskSpray = (float)v), v => $"{v * 100:0}%"));
    }

    private void Accessibility(VBoxContainer page)
    {
        string[] ids = new[] { "standard" }.Concat(_view.TeamColorSets.Select(s => s.Id)).ToArray();
        string[] names = new[] { "Standard" }.Concat(_view.TeamColorSets.Select(s => s.Name)).ToArray();
        HBoxContainer swatches = UiKit.Row(6);
        void ShowSwatches(string id)
        {
            foreach (Node child in swatches.GetChildren())
            {
                child.QueueFree();
            }

            swatches.AddChild(UiKit.Body("You, them, then the rest:", 18, UiKit.Dim));
            string[] colours = _view.TeamColorSets.FirstOrDefault(s => s.Id == id)?.Colors ?? _view.StandardTeamColors;
            foreach (string c in colours.Take(6))
            {
                swatches.AddChild(new ColorRect { Color = Color.FromHtml(c), CustomMinimumSize = new Vector2(30, 22) });
            }
        }

        page.AddChild(UiKit.OptionRow("Team colours", names, Math.Max(0, Array.IndexOf(ids, _settings.TeamColors)), i => Set(() =>
        {
            _settings.TeamColors = ids[i];
            ShowSwatches(ids[i]);
        })));
        ShowSwatches(_settings.TeamColors);
        page.AddChild(swatches);
        page.AddChild(UiKit.Body("Team colours change from the next round.", 18, UiKit.Dim));
        page.AddChild(UiKit.SliderRow("HUD size", 0.75, 1.5, 0.05, _settings.HudScale, v => Hud(() => _settings.HudScale = (float)v), v => $"{v * 100:0}%"));
    }

    /// <summary>"As the preset", "On" or "Off" for one of the preset's parts.</summary>
    private static HBoxContainer Tristate(string name, bool? value, Action<bool?> changed) =>
        UiKit.OptionRow(name, new[] { "As the preset", "On", "Off" }, value is null ? 0 : value.Value ? 1 : 2, i => changed(i switch { 1 => true, 2 => false, _ => null }));

    private void Set(Action change)
    {
        change();
        _settings.Save();
    }

    private void Graphics(Action change)
    {
        Set(change);
        _graphicsChanged?.Invoke(_settings);
    }

    private void Hud(Action change)
    {
        Set(change);
        _hudChanged?.Invoke(_settings);
    }

    /// <summary>One tab: its rows in a scrolling column.</summary>
    private static ScrollContainer Page(string name, Action<VBoxContainer> fill)
    {
        var scroll = new ScrollContainer { Name = name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        margin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        VBoxContainer page = UiKit.Column(12);
        margin.AddChild(page);
        scroll.AddChild(margin);
        fill(page);
        return scroll;
    }

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
