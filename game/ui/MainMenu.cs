using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Pb.Sim.Match;

namespace Pb.Game.Ui;

/// <summary>
/// The main scene: title, level select (each level with its mode, size and difficulty), the training
/// ground (the Phase 1 range), settings and quit. Start stores the choices in <see cref="GameSession"/>
/// and loads the level, where the briefing card takes over. <c>-- --smoke-test</c> builds every screen,
/// checks the ladder and the round choices are offered (and that picking a mode offers its sizes), and
/// quits; <c>-- --menu-tour</c> shows each screen in turn (for screenshots with <c>--write-movie</c>) and
/// quits. <c>-- --level=ID</c> (with <c>--mode</c>, <c>--size</c>, <c>--tier</c> and the level's other
/// options) skips the menu once and goes straight to the level: exported builds can't be told which
/// scene to run, so that's how a build starts a round, or a scripted run, from the command line.
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
    private TextureRect _backdrop = null!;
    private static bool _skippedToLevel;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        Theme = UiKit.Theme;
        _backdrop = Backdrop();
        AddChild(_backdrop);

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

        // The compound behind the menu, where there's a screen to show it on (not in CI's headless runs):
        // built a piece a frame behind the plain backdrop, which then fades to a shade over it.
        if (DisplayServer.GetName() != "headless" && !Args.Has("--smoke-test") && !(Args.Has("--level") && !_skippedToLevel))
        {
            var shade = new TextureRect
            {
                Name = "Shade", Texture = Shade(_view.MenuBackdrop.Shade), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore,
            };
            shade.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(shade);
            MoveChild(shade, _backdrop.GetIndex() + 1);
            var scene = new MenuBackdrop { Name = "Backdrop3D" };
            AddChild(scene);
            scene.Shown += () => CreateTween().TweenProperty(_backdrop, "modulate:a", 0f, 1.2);
            scene.Build(_data, _view, _settings);
        }

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
        else if (Args.Has("--level") && !_skippedToLevel)
        {
            // The level reads --level, --mode, --size and --tier itself. Only once, so the menu works after the round.
            _skippedToLevel = true;
            Callable.From(() => GetTree().ChangeSceneToFile(GameSession.LevelScene)).CallDeferred();
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
            case 65:
            case 85:
                // The first level's other modes, with their sizes.
                int mode = _tourFrame < 80 ? 2 : 1;
                if (_levels.FindChild($"Mode_*_{mode}", recursive: true, owned: false) is Button button)
                {
                    button.ButtonPressed = true;
                }

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
        var title = new TitleMark { Name = "Title" };
        title.Configure("PB", UiKit.Accent);
        column.AddChild(title);
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
        VBoxContainer card = UiKit.Column(10);
        card.AddChild(UiKit.Title(entry.DisplayName, 28, playable ? UiKit.Text : UiKit.Dim));
        string blurb = playable ? level!.Description : entry.Note ?? "Locked";
        Label description = UiKit.Body(blurb, 18, UiKit.Dim, wrap: true);
        description.CustomMinimumSize = new Vector2(760, 0);
        card.AddChild(description);
        if (playable && entry.Tiers is { Length: > 0 } tiers)
        {
            AddRoundChoices(card, entry, tiers);
        }

        return UiKit.Panel(card, 820f);
    }

    /// <summary>
    /// Mode, size and difficulty (last round's choices, else the first mode at its default size on Normal),
    /// what each means, and Start. Picking a mode offers its sizes.
    /// </summary>
    private void AddRoundChoices(VBoxContainer card, LadderLevelDef entry, LadderTierDef[] tiers)
    {
        IReadOnlyList<GameMode> modes = _data.Config.Rules.Modes;
        bool again = GameSession.LevelId == entry.Id;
        int modeIndex = Math.Max(0, again ? modes.ToList().FindIndex(m => m.Id == GameSession.ModeId) : 0);
        GameMode mode = modes[modeIndex];
        int size = again && GameSession.Size is { } last && mode.Sizes.Contains(last) ? last : mode.DefaultSize;
        int tierIndex = Array.FindIndex(tiers, t => again && t.Id == GameSession.TierId);
        if (tierIndex < 0)
        {
            tierIndex = Math.Max(0, Array.FindIndex(tiers, t => t.Id == "normal"));
        }

        LadderTierDef tier = tiers[tierIndex];

        card.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
        Label modeBlurb = UiKit.Body(mode.Description, 17, UiKit.Dim, wrap: true);
        modeBlurb.CustomMinimumSize = new Vector2(760, 0);
        var sizes = new VBoxContainer();
        Label details = UiKit.Body(TierDetails(tier), 17, UiKit.Dim, wrap: true);
        details.CustomMinimumSize = new Vector2(760, 0);

        void ShowSizes()
        {
            foreach (Node child in sizes.GetChildren())
            {
                sizes.RemoveChild(child);
                child.QueueFree();
            }

            string[] labels = mode.Sizes.Select(n => ModeText.Size(mode, n)).ToArray();
            int selected = Math.Max(0, mode.Sizes.ToList().IndexOf(size));
            sizes.AddChild(UiKit.ChoiceRow(mode.Kind == MatchModeKind.Solo ? "Opponents" : "Players", labels, selected,
                k => size = mode.Sizes[k], $"Size_{entry.Id}_", buttonWidth: 140));
        }

        card.AddChild(UiKit.ChoiceRow("Mode", modes.Select(m => m.DisplayName).ToArray(), modeIndex, k =>
        {
            mode = modes[k];
            size = mode.DefaultSize;
            modeBlurb.Text = mode.Description;
            ShowSizes();
        }, $"Mode_{entry.Id}_", buttonWidth: 190));
        card.AddChild(modeBlurb);
        ShowSizes();
        card.AddChild(sizes);
        card.AddChild(UiKit.ChoiceRow("Difficulty", tiers.Select(t => t.DisplayName).ToArray(), tierIndex, k =>
        {
            tier = tiers[k];
            details.Text = TierDetails(tier);
        }, $"Tier_{entry.Id}_", buttonWidth: 140));
        card.AddChild(details);
        Button start = UiKit.Button("Start", () => Play(entry, mode, size, tier), 260);
        start.Name = $"Start_{entry.Id}";
        start.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        card.AddChild(start);
    }

    private static string TierDetails(LadderTierDef tier) =>
        $"{tier.DisplayName}: {RoundScreens.Clock(tier.TimeLimit_s)} on the clock · you start with {tier.StartPods} spare " +
        $"pod{(tier.StartPods == 1 ? "" : "s")}, every bot carries {tier.BotPods} · {(tier.Pickups ? "pickups out" : "no pickups")}. " +
        "Difficulty sets how good the bots are, never how many there are.";

    private Control SettingsScreen()
    {
        VBoxContainer column = UiKit.Column(16);
        var panel = new SettingsPanel();
        panel.Build(_settings, _view);
        column.AddChild(panel);
        column.AddChild(UiKit.Button("Back", () => Open(_title), 200));
        return Screen(UiKit.Panel(column, 720f), left: false);
    }

    private void Play(LadderLevelDef entry, GameMode mode, int size, LadderTierDef tier)
    {
        GameSession.LevelId = entry.Id;
        GameSession.ModeId = mode.Id;
        GameSession.Size = size;
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

    /// <summary>A wash over the level behind the menu: darkest on the left, where the panels sit.</summary>
    private static GradientTexture2D Shade(float shade)
    {
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0.05f, 0.05f, 0.06f, shade));
        gradient.SetColor(1, new Color(0.05f, 0.05f, 0.06f, shade * 0.25f));
        return new GradientTexture2D
        {
            Gradient = gradient, FillFrom = new Vector2(0.15f, 0f), FillTo = new Vector2(0.85f, 0f), Width = 256, Height = 16,
        };
    }

    private static TextureRect Backdrop()
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
        var problems = new List<string>();
        Button[] buttons = _levels.FindChildren("*", nameof(Button), owned: false).OfType<Button>().ToArray();
        int Count(string prefix) => buttons.Count(b => b.Name.ToString().StartsWith(prefix, StringComparison.Ordinal));
        IReadOnlyList<GameMode> modes = _data.Config.Rules.Modes;
        LadderLevelDef[] playable = _data.Ladder.Levels.Where(l => _data.Levels.ContainsKey(l.Id)).ToArray();
        foreach (LadderLevelDef entry in playable)
        {
            if (Count($"Mode_{entry.Id}_") != modes.Count)
            {
                problems.Add($"{entry.Id}: {Count($"Mode_{entry.Id}_")} mode buttons, not {modes.Count}");
            }

            if (Count($"Tier_{entry.Id}_") != (entry.Tiers?.Length ?? 0))
            {
                problems.Add($"{entry.Id}: {Count($"Tier_{entry.Id}_")} difficulty buttons");
            }

            if (Count($"Start_{entry.Id}") != 1)
            {
                problems.Add($"{entry.Id}: no Start button");
            }

            // Picking each mode offers that mode's sizes.
            for (int m = 0; m < modes.Count; m++)
            {
                buttons.First(b => b.Name == $"Mode_{entry.Id}_{m}").ButtonPressed = true;
                int offered = _levels.FindChildren($"Size_{entry.Id}_*", nameof(Button), owned: false).Count(b => !b.IsQueuedForDeletion());
                if (offered != modes[m].Sizes.Count)
                {
                    problems.Add($"{entry.Id}: {modes[m].Id} offers {offered} sizes, not {modes[m].Sizes.Count}");
                }
            }
        }

        bool ok = playable.Length >= 1 && problems.Count == 0;
        GD.Print($"SMOKE {(ok ? "PASS" : "FAIL")}: menu shows {_data.Ladder.Levels.Length} ladder levels ({playable.Length} playable), " +
                 $"{modes.Count} modes with their sizes and the difficulty tiers{(problems.Count > 0 ? ": " + string.Join("; ", problems) : "")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
