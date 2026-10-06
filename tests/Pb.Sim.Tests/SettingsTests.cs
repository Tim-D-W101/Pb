using Pb.Game.Core;

namespace Pb.Sim.Tests;

/// <summary>
/// The settings file and the binding overrides (M3.9): old files load with the new settings at their defaults, bad
/// values are put right, and your bindings apply over the defaults, clash and swap, and survive a reload.
/// </summary>
public class SettingsTests
{
    private static readonly SettingsDefaults Defaults = new()
    {
        MouseSensitivity = 0.07f,
        InvertY = false,
        StickSpeed = 220f,
        StickCurve = 2f,
        Vsync = true,
        GraphicsPresets = new[] { "medium", "low", "high", "ultra" },
        RenderScales = new[] { 1f, 0.85f, 0.75f, 0.67f, 0.5f },
        Fov = 90f,
        FovMin = 70f,
        FovMax = 110f,
        Master = 0.8f,
        Effects = 1f,
        Voices = 1f,
        Ambience = 0.8f,
        Menus = 0.7f,
        Crosshair = true,
        HeadBob = true,
        TeamColorSets = new[] { "standard", "redGreen", "blueYellow" },
        Actions = new[] { "move_forward", "jump", "crouch", "interact", "refill", "lean_left", "fire" },
    };

    /// <summary>A trimmed copy of input.jsonc's defaults: two keyboard-and-mouse slots and a pad slot each.</summary>
    private static readonly (string, IReadOnlyList<string>)[] Input =
    {
        ("move_forward", new[] { "key:W", "key:Up", "axis:LeftY-" }),
        ("jump", new[] { "key:Space", "pad:A" }),
        ("crouch", new[] { "key:Ctrl", "key:C", "pad:B" }),
        ("interact", new[] { "key:F", "pad:X" }),
        ("refill", new[] { "key:R", "pad:X" }),
        ("lean_left", new[] { "key:Q", "pad:LeftShoulder" }),
        ("fire", new[] { "mouse:Left", "axis:TriggerRight+" }),
    };

    [Fact]
    public void A_settings_file_from_before_the_settings_menu_loads_with_the_new_settings_at_their_defaults()
    {
        // What Phase 2's game saved (version 1): no bindings, window, crosshair style, subtitles, team colours...
        const string old = """
            {
              "fovDeg": 100,
              "mouseSensitivityDegPerCount": 0.12,
              "invertY": true,
              "crosshair": false,
              "headBob": false,
              "vsync": false,
              "graphicsPreset": "high",
              "renderScale": 0.75,
              "volume": 0.5,
              "openAllLevels": true,
              "version": 1
            }
            """;
        GameSettings s = GameSettings.FromJson(old, Defaults, out string? problem);
        Assert.Null(problem);
        // What was saved is kept...
        Assert.Equal(100f, s.FovDeg);
        Assert.Equal(0.12f, s.MouseSensitivityDegPerCount);
        Assert.True(s.InvertY);
        Assert.False(s.Crosshair);
        Assert.False(s.Vsync);
        Assert.Equal("high", s.GraphicsPreset);
        Assert.Equal(0.75f, s.RenderScale);
        Assert.Equal(0.5f, s.Volume);
        // ...a setting since dropped ("openAllLevels": every area is open now) is ignored...
        // ...and what wasn't saved starts at its default.
        Assert.Equal(220f, s.StickSpeedDegps);
        Assert.Equal(2f, s.StickCurve);
        Assert.Empty(s.Bindings);
        Assert.Equal("windowed", s.WindowMode);
        Assert.Equal(0, s.FrameCap);
        Assert.Equal("cross-dot", s.CrosshairStyle);
        Assert.True(s.Subtitles);
        Assert.True(s.HitMarker);
        Assert.Equal(1f, s.EffectsVolume);
        Assert.Equal("standard", s.TeamColors);
        Assert.Equal(1f, s.HudScale);
        Assert.Equal(GameSettings.CurrentVersion, s.Version);
    }

    [Fact]
    public void Bad_values_are_put_right()
    {
        const string bad = """
            {
              "fovDeg": 500, "mouseSensitivityDegPerCount": -3, "stickSpeedDegps": 99999, "stickCurve": 0,
              "windowMode": "huge", "frameCap": 59, "graphicsPreset": "cinematic", "renderScale": 0.8,
              "graphics": { "shadows": "extreme", "weeds": 7 },
              "volume": 1.5, "effectsVolume": -1, "subtitleSize": 9, "crosshairStyle": "skull", "crosshairColor": "#12345",
              "crosshairSize": 0.1, "cameraJolt": 3, "maskSpray": "NaN", "teamColors": "rainbow", "hudScale": 0,
              "bindings": { "jump": { "first": "key:J" }, "teleport": { "first": "key:T" } },
              "version": 2
            }
            """;
        GameSettings s = GameSettings.FromJson(bad.Replace("\"NaN\"", "-1"), Defaults, out string? problem);
        Assert.Null(problem);
        Assert.Equal(110f, s.FovDeg);
        Assert.Equal(0.07f, s.MouseSensitivityDegPerCount);
        Assert.Equal(1000f, s.StickSpeedDegps);
        Assert.Equal(2f, s.StickCurve);
        Assert.Equal("windowed", s.WindowMode);
        Assert.Equal(60, s.FrameCap);
        Assert.Equal("medium", s.GraphicsPreset);
        Assert.Equal(0.85f, s.RenderScale);
        Assert.Null(s.Graphics.Shadows);
        Assert.Equal(1f, s.Graphics.Weeds);
        Assert.Equal(1f, s.Volume);
        Assert.Equal(0f, s.EffectsVolume);
        Assert.Equal(2f, s.SubtitleSize);
        Assert.Equal("cross-dot", s.CrosshairStyle);
        Assert.Equal("", s.CrosshairColor);
        Assert.Equal(0.5f, s.CrosshairSize);
        Assert.Equal(1f, s.CameraJolt);
        Assert.Equal(0f, s.MaskSpray);
        Assert.Equal("standard", s.TeamColors);
        Assert.Equal(1f, s.HudScale);
        // Bindings for an action that's gone are dropped; the rest stay.
        Assert.Equal(new[] { "jump" }, s.Bindings.Keys);
    }

    [Fact]
    public void A_file_that_isnt_settings_gives_the_defaults_and_says_why()
    {
        GameSettings s = GameSettings.FromJson("{ this is not json", Defaults, out string? problem);
        Assert.NotNull(problem);
        Assert.Equal(90f, s.FovDeg);
        Assert.True(s.Vsync);
        Assert.Equal("medium", s.GraphicsPreset);
        GameSettings none = GameSettings.FromJson(null, Defaults, out problem);
        Assert.Null(problem);
        Assert.Equal(0.8f, none.Volume);
    }

    [Fact]
    public void Settings_survive_a_save_and_a_reload()
    {
        GameSettings s = GameSettings.Defaults(Defaults);
        s.WindowMode = "borderless";
        s.FrameCap = 144;
        s.Graphics.Shadows = "off";
        s.Graphics.Glow = false;
        s.CrosshairStyle = "circle";
        s.CrosshairColor = "#4de8ff";
        s.TeamColors = "redGreen";
        s.CrouchToggle = true;
        s.Bindings["jump"] = new BindingOverride { First = "key:J", Pad = "" };
        GameSettings back = GameSettings.FromJson(s.ToJson(), Defaults, out string? problem);
        Assert.Null(problem);
        Assert.Equal(s.ToJson(), back.ToJson());
        Assert.Equal("off", back.Graphics.Shadows);
        Assert.Equal("key:J", back.Bindings["jump"].First);
        Assert.Equal("", back.Bindings["jump"].Pad);
        Assert.Null(back.Bindings["jump"].Second);
    }

    [Fact]
    public void Overrides_apply_over_the_defaults()
    {
        var overrides = new Dictionary<string, BindingOverride>
        {
            ["jump"] = new() { First = "key:J" },
            ["crouch"] = new() { Second = "" },
            ["fire"] = new() { Pad = "axis:TriggerLeft+" },
        };
        var set = new BindingSet(Input, overrides);
        Assert.Equal(new[] { "key:J", "pad:A" }, set.Effective("jump"));
        Assert.Equal(new[] { "key:Ctrl", "pad:B" }, set.Effective("crouch"));
        Assert.Equal(new[] { "mouse:Left", "axis:TriggerLeft+" }, set.Effective("fire"));
        Assert.Equal(new[] { "key:W", "key:Up", "axis:LeftY-" }, set.Effective("move_forward"));
        // Saved back, only the changes are kept.
        Dictionary<string, BindingOverride> saved = set.Overrides();
        Assert.Equal(new[] { "crouch", "fire", "jump" }, saved.Keys.Order());
        Assert.Equal("key:J", saved["jump"].First);
        Assert.Null(saved["jump"].Pad);
        Assert.Equal("", saved["crouch"].Second);
    }

    [Fact]
    public void Bindings_survive_a_reload()
    {
        var set = new BindingSet(Input);
        set.Assign("lean_left", BindingSet.Slot.First, "key:Z");
        set.Assign("jump", BindingSet.Slot.Pad, "pad:Y");
        GameSettings s = GameSettings.Defaults(Defaults);
        s.Bindings = set.Overrides();
        GameSettings back = GameSettings.FromJson(s.ToJson(), Defaults, out _);
        var reloaded = new BindingSet(Input, back.Bindings);
        foreach ((string action, _) in Input)
        {
            Assert.Equal(set.Effective(action), reloaded.Effective(action));
        }
    }

    [Fact]
    public void A_taken_binding_clashes_and_can_be_swapped()
    {
        var set = new BindingSet(Input);
        // The pad's X is shared by interact and refill in the defaults: that's no clash.
        Assert.Empty(set.Clashes());
        // Space for lean left: jump has it.
        (string Action, BindingSet.Slot Slot)? holder = set.Assign("lean_left", BindingSet.Slot.First, "key:Space");
        Assert.Equal(("jump", BindingSet.Slot.First), holder);
        Assert.Single(set.Clashes());
        // Swapped: jump gets lean left's old Q.
        set.Swap("lean_left", BindingSet.Slot.First, "jump", BindingSet.Slot.First, "key:Q");
        Assert.Equal("key:Space", set.Get("lean_left", BindingSet.Slot.First));
        Assert.Equal("key:Q", set.Get("jump", BindingSet.Slot.First));
        Assert.Empty(set.Clashes());
        // The pad's X for jump, though, clashes with both the actions sharing it.
        Assert.NotNull(set.Assign("jump", BindingSet.Slot.Pad, "pad:X"));
        Assert.Equal(2, set.Clashes().Count);
    }

    [Fact]
    public void Resetting_puts_the_defaults_back()
    {
        var set = new BindingSet(Input);
        set.Assign("jump", BindingSet.Slot.First, "key:J");
        set.Assign("fire", BindingSet.Slot.Pad, "");
        set.Reset("jump");
        Assert.Equal("key:Space", set.Get("jump", BindingSet.Slot.First));
        Assert.Equal("", set.Get("fire", BindingSet.Slot.Pad));
        set.ResetAll();
        Assert.Equal("axis:TriggerRight+", set.Get("fire", BindingSet.Slot.Pad));
        Assert.Empty(set.Overrides());
    }

    [Fact]
    public void A_binding_in_the_wrong_slot_or_badly_formed_is_refused_or_dropped()
    {
        var set = new BindingSet(Input);
        Assert.Throws<ArgumentException>(() => set.Assign("jump", BindingSet.Slot.First, "pad:A"));
        Assert.Throws<ArgumentException>(() => set.Assign("jump", BindingSet.Slot.Pad, "key:J"));
        Assert.Throws<ArgumentException>(() => set.Assign("jump", BindingSet.Slot.Pad, "axis:LeftX"));
        int dropped = set.Apply(new Dictionary<string, BindingOverride>
        {
            ["jump"] = new() { First = "pad:A", Pad = "wobble" },
            ["teleport"] = new() { First = "key:T" },
        });
        Assert.Equal(3, dropped);
        Assert.Equal(new[] { "key:Space", "pad:A" }, set.Effective("jump"));
    }
}
