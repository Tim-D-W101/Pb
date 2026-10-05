using System;
using System.Linq;
using Godot;
using Pb.Game.Core;

namespace Pb.Game.Ui;

/// <summary>
/// The basic settings, shared by the main menu and the pause menu: field of view, mouse sensitivity,
/// invert Y, graphics preset, render scale, v-sync and the volumes (master, effects, voices, ambience, menus). Every change is saved at once; the owner of the
/// panel applies what it needs (the camera reads FOV and sensitivity itself).
/// </summary>
public partial class SettingsPanel : VBoxContainer
{
    public void Build(GameSettings settings, PresentationDef view, Action<GameSettings>? graphicsChanged = null)
    {
        AddThemeConstantOverride("separation", 14);
        AddChild(UiKit.Title("Settings", 34));

        void Changed()
        {
            settings.Save();
        }

        AddChild(UiKit.SliderRow("Field of view", view.Camera.FovMin_deg, view.Camera.FovMax_deg, view.Camera.FovStep_deg,
            settings.FovDeg, v =>
            {
                settings.FovDeg = (float)v;
                Changed();
            }, v => $"{v:0}°"));
        AddChild(UiKit.SliderRow("Mouse sensitivity", 0.01, 0.4, 0.005, settings.MouseSensitivityDegPerCount, v =>
        {
            settings.MouseSensitivityDegPerCount = (float)v;
            Changed();
        }, v => $"{v:0.000}°"));
        AddChild(UiKit.CheckRow("Invert Y", settings.InvertY, on =>
        {
            settings.InvertY = on;
            Changed();
        }));

        string[] presets = view.Graphics.Presets.Select(p => p.Name).ToArray();
        int current = Math.Max(0, Array.IndexOf(presets, settings.GraphicsPreset));
        AddChild(UiKit.OptionRow("Graphics", presets.Select(Capitalise).ToArray(), current, i =>
        {
            settings.GraphicsPreset = presets[i];
            Changed();
            graphicsChanged?.Invoke(settings);
        }));
        float[] scales = view.Graphics.RenderScales;
        int scale = Math.Max(0, Array.FindIndex(scales, s => Math.Abs(s - settings.RenderScale) < 0.001f));
        AddChild(UiKit.OptionRow("Render scale", scales.Select(s => s >= 0.999f ? "100% (sharpest)" : $"{s * 100:0}% (faster)").ToArray(), scale, i =>
        {
            settings.RenderScale = scales[i];
            Changed();
            graphicsChanged?.Invoke(settings);
        }));
        AddChild(UiKit.CheckRow("V-sync", settings.Vsync, on =>
        {
            settings.Vsync = on;
            DisplayServer.WindowSetVsyncMode(on ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
            Changed();
        }));
        void Volume(string name, float value, Action<float> set) => AddChild(UiKit.SliderRow(name, 0, 1, 0.05, value, v =>
        {
            set((float)v);
            settings.ApplyVolume();
            Changed();
        }, v => $"{v * 100:0}%"));

        Volume("Volume", settings.Volume, v => settings.Volume = v);
        Volume("Effects", settings.EffectsVolume, v => settings.EffectsVolume = v);
        Volume("Voices", settings.VoicesVolume, v => settings.VoicesVolume = v);
        Volume("Ambience", settings.AmbienceVolume, v => settings.AmbienceVolume = v);
        Volume("Menus", settings.MenusVolume, v => settings.MenusVolume = v);
    }

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
