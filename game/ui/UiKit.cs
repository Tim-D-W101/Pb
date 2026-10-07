using System;
using Godot;

namespace Pb.Game.Ui;

/// <summary>
/// The shared look of menus and overlays, built in code: dark translucent panels, a paint-orange
/// accent, and large type that reads from across a room.
/// </summary>
public static class UiKit
{
    public static readonly Color Accent = new(1f, 0.52f, 0.16f);
    public static readonly Color Text = new(0.94f, 0.93f, 0.9f);
    public static readonly Color Dim = new(0.64f, 0.63f, 0.6f);
    public static readonly Color Good = new(0.45f, 0.85f, 0.45f);
    public static readonly Color Bad = new(0.95f, 0.38f, 0.32f);

    private static Theme? _theme;

    public static Theme Theme => _theme ??= BuildTheme();

    public static Label Title(string text, int size = 46, Color? color = null)
    {
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Left };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? Text);
        return label;
    }

    public static Label Body(string text, int size = 20, Color? color = null, bool wrap = false)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? Text);
        return label;
    }

    public static Button Button(string text, Action pressed, int minWidth = 280, bool enabled = true)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(minWidth, 46),
            Disabled = !enabled,
            FocusMode = Control.FocusModeEnum.All,
        };
        bool back = text.StartsWith("Back", StringComparison.Ordinal) || text.StartsWith("Cancel", StringComparison.Ordinal);
        button.Pressed += back ? Pb.Game.Audio.UiSounds.Back : Pb.Game.Audio.UiSounds.Click;
        button.Pressed += pressed;
        button.MouseEntered += Pb.Game.Audio.UiSounds.Hover;
        return button;
    }

    public static VBoxContainer Column(int separation = 12)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", separation);
        return column;
    }

    public static HBoxContainer Row(int separation = 12)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", separation);
        return row;
    }

    /// <summary>A padded panel around <paramref name="content"/>.</summary>
    public static PanelContainer Panel(Control content, float minWidth = 0f)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(minWidth, 0f) };
        panel.AddChild(content);
        return panel;
    }

    /// <summary>A full-screen layer that centres its child, over an optional dimmed backdrop.</summary>
    public static Control Overlay(Control child, float dim = 0.55f)
    {
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.Theme = Theme;
        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, dim), MouseFilter = Control.MouseFilterEnum.Ignore };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(backdrop);
        var centre = new CenterContainer();
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        centre.AddChild(child);
        root.AddChild(centre);
        return root;
    }

    /// <summary>"Label ……… value" with a slider; <paramref name="format"/> shows the value.</summary>
    public static HBoxContainer SliderRow(string name, double min, double max, double step, double value, Action<double> changed,
        Func<double, string> format)
    {
        HBoxContainer row = Row(16);
        Label label = Body(name);
        label.CustomMinimumSize = new Vector2(220, 0);
        var slider = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = value,
            CustomMinimumSize = new Vector2(280, 32), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        Label shown = Body(format(value), 20, Dim);
        shown.CustomMinimumSize = new Vector2(110, 0);
        slider.ValueChanged += v =>
        {
            shown.Text = format(v);
            changed(v);
            Pb.Game.Audio.UiSounds.Tick();
        };
        row.AddChild(label);
        row.AddChild(slider);
        row.AddChild(shown);
        return row;
    }

    public static HBoxContainer OptionRow(string name, string[] options, int selected, Action<int> changed)
    {
        HBoxContainer row = Row(16);
        Label label = Body(name);
        label.CustomMinimumSize = new Vector2(220, 0);
        var picker = new OptionButton { CustomMinimumSize = new Vector2(280, 40) };
        foreach (string option in options)
        {
            picker.AddItem(option);
        }

        picker.Selected = selected;
        picker.ItemSelected += i =>
        {
            Pb.Game.Audio.UiSounds.Click();
            changed((int)i);
        };
        row.AddChild(label);
        row.AddChild(picker);
        return row;
    }

    /// <summary>
    /// "Label  [A] [B] [C]": one choice from a few, as toggle buttons that stay lit (clearer than a drop-down
    /// from across a room). Each button is named <paramref name="namePrefix"/> + its index, for tests.
    /// </summary>
    public static HBoxContainer ChoiceRow(string name, string[] options, int selected, Action<int> changed, string namePrefix,
        int buttonWidth = 150)
    {
        HBoxContainer row = Row(10);
        Label label = Body(name);
        label.CustomMinimumSize = new Vector2(150, 0);
        row.AddChild(label);
        var group = new ButtonGroup();
        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            var button = new Button
            {
                Name = namePrefix + i,
                Text = options[i],
                ToggleMode = true,
                ButtonGroup = group,
                ButtonPressed = i == selected,
                CustomMinimumSize = new Vector2(buttonWidth, 44),
                FocusMode = Control.FocusModeEnum.All,
            };
            button.Toggled += on =>
            {
                if (on)
                {
                    Pb.Game.Audio.UiSounds.Click();
                    changed(index);
                }
            };
            button.MouseEntered += Pb.Game.Audio.UiSounds.Hover;
            row.AddChild(button);
        }

        return row;
    }

    /// <summary>"Label  [On]": a toggle button that reads On or Off (clearer than a small tick box from across a room).</summary>
    public static HBoxContainer CheckRow(string name, bool value, Action<bool> changed)
    {
        HBoxContainer row = Row(16);
        Label label = Body(name);
        label.CustomMinimumSize = new Vector2(220, 0);
        var toggle = new Button
        {
            ToggleMode = true, ButtonPressed = value, Text = value ? "On" : "Off",
            CustomMinimumSize = new Vector2(120, 40), FocusMode = Control.FocusModeEnum.All,
        };
        toggle.Toggled += on =>
        {
            toggle.Text = on ? "On" : "Off";
            Pb.Game.Audio.UiSounds.Toggle();
            changed(on);
        };
        row.AddChild(label);
        row.AddChild(toggle);
        return row;
    }

    private static Theme BuildTheme()
    {
        var theme = new Theme { DefaultFontSize = 20 };

        static StyleBoxFlat Box(Color background, Color border, int borderWidth, int radius, int margin) => new()
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = borderWidth, BorderWidthRight = borderWidth, BorderWidthTop = borderWidth, BorderWidthBottom = borderWidth,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = margin * 0.5f, ContentMarginBottom = margin * 0.5f,
        };

        theme.SetStylebox("normal", "Button", Box(new Color(0.12f, 0.125f, 0.13f, 0.95f), new Color(0.32f, 0.32f, 0.32f), 1, 4, 18));
        theme.SetStylebox("hover", "Button", Box(new Color(0.19f, 0.19f, 0.2f, 0.98f), Accent, 2, 4, 18));
        theme.SetStylebox("pressed", "Button", Box(Accent.Darkened(0.45f), Accent, 2, 4, 18));
        theme.SetStylebox("hover_pressed", "Button", Box(Accent.Darkened(0.35f), Accent, 2, 4, 18));
        theme.SetStylebox("focus", "Button", Box(new Color(0f, 0f, 0f, 0f), Accent, 2, 4, 18));
        theme.SetStylebox("disabled", "Button", Box(new Color(0.09f, 0.09f, 0.095f, 0.8f), new Color(0.2f, 0.2f, 0.2f), 1, 4, 18));
        theme.SetColor("font_color", "Button", Text);
        theme.SetColor("font_hover_color", "Button", Colors.White);
        theme.SetColor("font_pressed_color", "Button", Colors.White);
        theme.SetColor("font_focus_color", "Button", Colors.White);
        theme.SetColor("font_disabled_color", "Button", new Color(0.45f, 0.45f, 0.45f));
        theme.SetFontSize("font_size", "Button", 21);

        theme.SetStylebox("panel", "PanelContainer", Box(new Color(0.06f, 0.065f, 0.07f, 0.9f), new Color(1f, 1f, 1f, 0.08f), 1, 6, 34));
        theme.SetColor("font_color", "Label", Text);
        theme.SetStylebox("normal", "OptionButton", Box(new Color(0.12f, 0.125f, 0.13f, 0.95f), new Color(0.32f, 0.32f, 0.32f), 1, 4, 14));
        theme.SetStylebox("hover", "OptionButton", Box(new Color(0.19f, 0.19f, 0.2f, 0.98f), Accent, 2, 4, 14));
        return theme;
    }
}
