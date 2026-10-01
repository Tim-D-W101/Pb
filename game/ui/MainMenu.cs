using System;
using System.Linq;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Data;
using Pb.Sim.Level;

namespace Pb.Game.Ui;

/// <summary>
/// The main scene: title, level select with difficulty, the training ground (the Phase 1 range),
/// settings and quit. Picking a level and tier stores them in <see cref="GameSession"/> and loads
/// the level, where the briefing card takes over. <c>-- --smoke-test</c> builds every screen, checks
/// the ladder is shown, and quits; <c>-- --menu-tour</c> shows each screen in turn (for screenshots
/// with <c>--write-movie</c>) and quits.
/// </summary>
public partial class MainMenu : Control
{
    private GameData _data = null!;
    private PresentationDef _view = null!;
    private GameSettings _settings = null!;
    private Control _title = null!;
    private Control _levels = null!;
    private Control _settingsScreen = null!;
    private int _tourFrame = -1;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        Theme = UiKit.Theme;
        AddChild(Backdrop());

        try
        {
            var source = new GodotDataSource();
            _data = GameData.Load(source);
            _view = Jsonc.Load<PresentationDef>(source, PresentationDef.File);
            InputSetup.Apply(Jsonc.Load<InputDef>(source, InputDef.File));
        }
        catch (DataException ex)
        {
            GD.PushError(ex.Message);
            AddChild(UiKit.Overlay(UiKit.Panel(UiKit.Body(ex.Message, 20, UiKit.Bad, wrap: true), 900f)));
            if (Args.Has("--smoke-test"))
            {
                GetTree().Quit(1);
            }

            return;
        }

        _settings = GameSettings.Load(_view);
        _settings.ApplyVolume();
        Input.MouseMode = Input.MouseModeEnum.Visible;

        _title = TitleScreen();
        _levels = LevelSelect();
        _settingsScreen = SettingsScreen();
        foreach (Control screen in new[] { _title, _levels, _settingsScreen })
        {
            AddChild(screen);
        }

        Open(GameSession.ReturnTo == "levels" ? _levels : _title);
        GameSession.ReturnTo = null;

        if (Args.Has("--smoke-test"))
        {
            SmokeTest();
        }
        else if (Args.Has("--menu-tour"))
        {
            _tourFrame = 0;
        }
    }

    public override void _Process(double delta)
    {
        if (_tourFrame < 0)
        {
            return;
        }

        // Frames, not seconds, so captures come out the same at any rendering speed.
        switch (_tourFrame++)
        {
            case 45:
                Open(_levels);
                break;
            case 105:
                Open(_settingsScreen);
                break;
            case 150:
                GetTree().Quit();
                break;
        }
    }

    private void Open(Control screen)
    {
        foreach (Control s in new[] { _title, _levels, _settingsScreen })
        {
            s.Visible = s == screen;
        }

        // Keyboard and pad navigation start on the first button.
        Button? first = screen.FindChildren("*", nameof(Button), owned: false).OfType<Button>().FirstOrDefault(b => !b.Disabled);
        first?.CallDeferred(Control.MethodName.GrabFocus);
    }

    private Control TitleScreen()
    {
        VBoxContainer column = UiKit.Column(14);
        column.AddChild(UiKit.Title("PB", 96, UiKit.Accent));
        column.AddChild(UiKit.Body("First-person paintball in an abandoned compound", 22, UiKit.Dim));
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 26) });
        VBoxContainer buttons = UiKit.Column(14);
        buttons.CustomMinimumSize = new Vector2(380, 0);
        buttons.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        buttons.AddChild(UiKit.Button("Play", () => Open(_levels)));
        buttons.AddChild(UiKit.Button("Training ground", () => GetTree().ChangeSceneToFile(GameSession.RangeScene)));
        buttons.AddChild(UiKit.Button("Settings", () => Open(_settingsScreen)));
        buttons.AddChild(UiKit.Button("Quit", () => GetTree().Quit()));
        column.AddChild(buttons);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });
        column.AddChild(UiKit.Body("Working title · Phase 2 build", 16, UiKit.Dim));
        return Screen(column, left: true);
    }

    private Control LevelSelect()
    {
        VBoxContainer column = UiKit.Column(16);
        column.AddChild(UiKit.Title("Choose a level", 40));
        foreach (LadderLevelDef entry in _data.Ladder.Levels)
        {
            column.AddChild(LevelCard(entry));
        }

        Button back = UiKit.Button("Back", () => Open(_title), 200);
        back.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        column.AddChild(back);
        return Screen(column, left: true);
    }

    private Control LevelCard(LadderLevelDef entry)
    {
        bool playable = _data.Levels.TryGetValue(entry.Id, out LevelLayout? level);
        VBoxContainer card = UiKit.Column(8);
        card.AddChild(UiKit.Title(entry.DisplayName, 28, playable ? UiKit.Text : UiKit.Dim));
        string blurb = playable ? level!.Description : entry.Note ?? "Locked";
        Label description = UiKit.Body(blurb, 18, UiKit.Dim, wrap: true);
        description.CustomMinimumSize = new Vector2(760, 0);
        card.AddChild(description);
        if (playable && entry.Tiers is { Length: > 0 } tiers)
        {
            HBoxContainer row = UiKit.Row(12);
            // What the highlighted tier means: clock, pods and pickups.
            Label details = UiKit.Body(TierDetails(tiers[0]), 17, UiKit.Dim);
            foreach (LadderTierDef tier in tiers)
            {
                string label = $"{tier.DisplayName} · {tier.Opponents.Length} opponents";
                Button button = UiKit.Button(label, () => Play(entry, tier), 240);
                button.FocusEntered += () => details.Text = TierDetails(tier);
                button.MouseEntered += () => details.Text = TierDetails(tier);
                row.AddChild(button);
            }

            card.AddChild(row);
            card.AddChild(details);
        }

        return UiKit.Panel(card, 820f);
    }

    private static string TierDetails(LadderTierDef tier) =>
        $"{tier.DisplayName}: {RoundScreens.Clock(tier.TimeLimit_s)} on the clock · you start with {tier.StartPods} spare " +
        $"pod{(tier.StartPods == 1 ? "" : "s")}, they carry {tier.OpponentPods} · {(tier.Pickups ? "pickups out" : "no pickups")}";

    private Control SettingsScreen()
    {
        VBoxContainer column = UiKit.Column(16);
        var panel = new SettingsPanel();
        panel.Build(_settings, _view);
        column.AddChild(panel);
        column.AddChild(UiKit.Button("Back", () => Open(_title), 200));
        return Screen(UiKit.Panel(column, 720f), left: false);
    }

    private void Play(LadderLevelDef entry, LadderTierDef tier)
    {
        GameSession.LevelId = entry.Id;
        GameSession.TierId = tier.Id;
        GetTree().ChangeSceneToFile(GameSession.LevelScene);
    }

    /// <summary>A screen laid out at the left third (title, lists) or centred (dialogs).</summary>
    private static Control Screen(Control content, bool left)
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", left ? 120 : 0);
        margin.AddThemeConstantOverride("margin_top", 80);
        margin.AddThemeConstantOverride("margin_bottom", 60);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var holder = left ? (Container)new VBoxContainer() : new CenterContainer();
        holder.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        // Left-aligned screens keep their own width instead of stretching to the right edge.
        content.SizeFlagsHorizontal = left ? SizeFlags.ShrinkBegin : SizeFlags.ShrinkCenter;
        holder.AddChild(content);
        scroll.AddChild(holder);
        margin.AddChild(scroll);
        return margin;
    }

    private static Control Backdrop()
    {
        // A warm-to-cold overcast wash with a darker floor, behind everything.
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0.16f, 0.15f, 0.14f));
        gradient.SetColor(1, new Color(0.05f, 0.055f, 0.06f));
        var texture = new GradientTexture2D
        {
            Gradient = gradient, FillFrom = new Vector2(0.2f, 0f), FillTo = new Vector2(0.8f, 1f), Width = 256, Height = 256,
        };
        var rect = new TextureRect { Texture = texture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale };
        rect.SetAnchorsPreset(LayoutPreset.FullRect);
        return rect;
    }

    private void SmokeTest()
    {
        int playable = _data.Ladder.Levels.Count(l => _data.Levels.ContainsKey(l.Id));
        int tierButtons = _levels.FindChildren("*", nameof(Button), owned: false).Count(b => ((Button)b).Text.Contains("opponents"));
        int expected = _data.Ladder.Levels.Where(l => _data.Levels.ContainsKey(l.Id)).Sum(l => l.Tiers?.Length ?? 0);
        bool ok = playable >= 1 && tierButtons == expected && expected > 0;
        GD.Print($"SMOKE {(ok ? "PASS" : "FAIL")}: menu shows {_data.Ladder.Levels.Length} ladder levels ({playable} playable) and {tierButtons} difficulty buttons");
        GetTree().Quit(ok ? 0 : 1);
    }
}
