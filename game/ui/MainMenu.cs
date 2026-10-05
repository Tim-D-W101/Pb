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
    private LadderProgress _progress = null!;
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
        InputSetup.Apply(_settings.Bindings);
        _settings.ApplyWindow();
        Pb.Game.Audio.UiSounds.Volume_db = _view.Audio.Volume_db + _view.Audio.Mix.Menu;
        Pb.Game.Audio.UiSounds.Variations = _view.Audio.Variations;
        Pb.Game.Audio.SoundBank.Warm(_view.Audio.Variations);
        _settings.ApplyVolume();
        _progress = Profile.Load(_data.Ladder, _settings);
        bool skipping = Args.Has("--level") && !_skippedToLevel;
        if (GameSession.LevelId is null && !skipping && _progress.Data.Last is { } last && _data.Levels.ContainsKey(last.Level))
        {
            // The choices you made last time the game ran (not when skipping to the level the command line names).
            GameSession.LevelId = last.Level;
            GameSession.ModeId = last.Mode;
            GameSession.Size = last.Size;
            GameSession.TierId = last.Tier;
            GameSession.ObjectiveId = last.Objective.Length > 0 ? last.Objective : null;
        }

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
            scene.Build(_data, _view, _settings, _progress.Newest(_data.Levels.ContainsKey));
        }

        _title = TitleScreen();
        _levels = LevelSelect();
        _settingsScreen = SettingsScreen();
        foreach (Control screen in new[] { _title, _levels, _settingsScreen })
        {
            AddChild(screen);
        }

        if (GameSession.ReturnTo == "levels")
        {
            ShowLevels();
        }
        else
        {
            Open(_title);
        }

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
                ShowLevels();
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
            case 125:
            case 145:
            case 165:
            case 185:
                // Each of the settings' tabs in turn.
                if (_settingsScreen.FindChild("SettingsMenu", recursive: true, owned: false) is SettingsMenu menu)
                {
                    menu.Tabs.CurrentTab = (_tourFrame - 106) / 20;
                }

                break;
            case 210:
                GetTree().Quit();
                break;
        }
    }

    private void Open(Control screen, Button? focus = null)
    {
        foreach (Control s in new[] { _title, _levels, _settingsScreen })
        {
            s.Visible = s == screen;
        }

        // Keyboard and pad navigation start on the first button (or the one asked for).
        focus ??= screen.FindChildren("*", nameof(Button), owned: false).OfType<Button>().FirstOrDefault(b => !b.Disabled);
        focus?.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>
    /// Level select, built afresh (what's open may have changed), on the level you last played or else the newest
    /// one you've opened.
    /// </summary>
    private void ShowLevels()
    {
        int index = _levels.GetIndex();
        RemoveChild(_levels);
        _levels.QueueFree();
        _levels = LevelSelect();
        AddChild(_levels);
        MoveChild(_levels, index);
        string? chosen = GameSession.LevelId is { } id && _data.Levels.ContainsKey(id) && _progress.IsOpen(id) ? id
            : _progress.Newest(_data.Levels.ContainsKey);
        Open(_levels, _levels.FindChild($"Start_{chosen}", recursive: true, owned: false) as Button);
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
        buttons.AddChild(UiKit.Button("Play", ShowLevels));
        buttons.AddChild(UiKit.Button("Training ground", () => Load(GameSession.RangeScene, "Training ground", "Setting out the targets…")));
        buttons.AddChild(UiKit.Button("Settings", () => Open(_settingsScreen)));
        buttons.AddChild(UiKit.Button("Quit", () => GetTree().Quit()));
        column.AddChild(buttons);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });
        column.AddChild(UiKit.Body("Working title · Phase 3 build", 16, UiKit.Dim));
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
        bool built = _data.Levels.TryGetValue(entry.Id, out LevelLayout? level);
        bool open = built && _progress.IsOpen(entry.Id);
        VBoxContainer card = UiKit.Column(10);
        card.AddChild(UiKit.Title(entry.DisplayName, 28, open ? UiKit.Text : UiKit.Dim));
        string blurb = built ? level!.Description : entry.Note ?? "Coming soon";
        Label description = UiKit.Body(blurb, 18, UiKit.Dim, wrap: true);
        description.CustomMinimumSize = new Vector2(760, 0);
        card.AddChild(description);
        if (built && !open)
        {
            Label locked = UiKit.Body(LockedLine(entry), 18, UiKit.Accent, wrap: true);
            locked.Name = $"Locked_{entry.Id}";
            locked.CustomMinimumSize = new Vector2(760, 0);
            card.AddChild(locked);
        }
        else if (open && entry.Tiers is { Length: > 0 } tiers)
        {
            AddRoundChoices(card, entry, tiers);
        }

        return UiKit.Panel(card, 820f);
    }

    /// <summary>"Locked: win a round at Oxbarrow Works to open it (on Normal or harder)."</summary>
    private string LockedLine(LadderLevelDef entry)
    {
        LadderLevelDef? before = _progress.OpenedBy(entry.Id);
        if (before is null)
        {
            return "Locked.";
        }

        string tier = "";
        if (_progress.MinTier is { } min && before.Tiers is { Length: > 0 } tiers && Array.FindIndex(tiers, t => t.Id == min) > 0)
        {
            tier = $" on {tiers.First(t => t.Id == min).DisplayName} or harder";
        }

        return $"Locked: win a round at {before.DisplayName}{tier} to open it.";
    }

    /// <summary>Your record on this level in this mode, objective and difficulty, and the difficulties you've won on.</summary>
    private string RecordLine(LadderLevelDef entry, GameMode mode, ObjectiveChoice objective, LadderTierDef tier)
    {
        string[] won = (entry.Tiers ?? Array.Empty<LadderTierDef>()).Where(t => _progress.WonOn(entry.Id, t.Id)).Select(t => t.DisplayName).ToArray();
        string wins = won.Length > 0 ? $" Won on {string.Join(", ", won)}." : "";
        ObjectiveKind kind = mode.Kind == MatchModeKind.FreeForAll ? ObjectiveKind.Eliminate : objective.Kind;
        LevelRecord? r = _progress.Record(entry.Id, mode.Id, tier.Id, LadderProgress.IdOf(kind));
        string what = kind == ObjectiveKind.Eliminate ? $"{mode.DisplayName}, {tier.DisplayName}" : $"{mode.DisplayName}, {objective.DisplayName}, {tier.DisplayName}";
        if (r is null)
        {
            return $"Your record ({what}): no rounds yet.{wins}";
        }

        var parts = new List<string> { $"won {r.Wins} of {r.Rounds}" };
        if (r.BestClear_s > 0f)
        {
            parts.Add($"fastest win {RoundScreens.Clock(r.BestClear_s)}");
        }

        if (r.BestAccuracy > 0f)
        {
            parts.Add($"best accuracy {r.BestAccuracy * 100:0}%");
        }

        parts.Add($"most eliminations {r.MostEliminations}");
        return $"Your record ({what}): {string.Join(" · ", parts)}.{wins}";
    }

    /// <summary>
    /// Mode, size, objective and difficulty (last round's choices, else the first mode at its default size, eliminate,
    /// on Normal), what each means, and Start. Picking a mode offers its sizes, and the objectives the level has places
    /// for (none in free-for-all, which is always last one standing).
    /// </summary>
    private void AddRoundChoices(VBoxContainer card, LadderLevelDef entry, LadderTierDef[] tiers)
    {
        IReadOnlyList<GameMode> modes = _data.Config.Rules.Modes;
        bool again = GameSession.LevelId == entry.Id;
        int modeIndex = Math.Max(0, again ? modes.ToList().FindIndex(m => m.Id == GameSession.ModeId) : 0);
        GameMode mode = modes[modeIndex];
        int size = again && GameSession.Size is { } last && mode.Sizes.Contains(last) ? last : mode.DefaultSize;
        ObjectiveChoice[] objectives = _data.Config.Rules.Objectives.Kinds.Where(k => _data.Levels[entry.Id].Objectives.Offers(k.Kind)).ToArray();
        int objectiveIndex = Math.Max(0, again ? Array.FindIndex(objectives, o => LadderProgress.IdOf(o.Kind) == GameSession.ObjectiveId) : 0);
        ObjectiveChoice objective = objectives[objectiveIndex];
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
        Label record = UiKit.Body(RecordLine(entry, mode, objective, tier), 17, UiKit.Text, wrap: true);
        record.Name = $"Record_{entry.Id}";
        record.CustomMinimumSize = new Vector2(760, 0);
        Label objectiveBlurb = UiKit.Body(objective.Description, 17, UiKit.Dim, wrap: true);
        objectiveBlurb.CustomMinimumSize = new Vector2(760, 0);
        HBoxContainer objectiveRow = UiKit.ChoiceRow("Objective", objectives.Select(o => o.DisplayName).ToArray(), objectiveIndex, k =>
        {
            objective = objectives[k];
            objectiveBlurb.Text = objective.Description;
            record.Text = RecordLine(entry, mode, objective, tier);
        }, $"Objective_{entry.Id}_", buttonWidth: 160);

        void ShowObjectives()
        {
            bool offered = mode.Kind != MatchModeKind.FreeForAll && objectives.Length > 1;
            objectiveRow.Visible = offered;
            objectiveBlurb.Visible = offered;
        }

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
            record.Text = RecordLine(entry, mode, objective, tier);
            ShowSizes();
            ShowObjectives();
        }, $"Mode_{entry.Id}_", buttonWidth: 190));
        card.AddChild(modeBlurb);
        ShowSizes();
        card.AddChild(sizes);
        card.AddChild(objectiveRow);
        card.AddChild(objectiveBlurb);
        ShowObjectives();
        card.AddChild(UiKit.ChoiceRow("Difficulty", tiers.Select(t => t.DisplayName).ToArray(), tierIndex, k =>
        {
            tier = tiers[k];
            details.Text = TierDetails(tier);
            record.Text = RecordLine(entry, mode, objective, tier);
        }, $"Tier_{entry.Id}_", buttonWidth: 140));
        card.AddChild(details);
        card.AddChild(record);
        Button start = UiKit.Button("Start", () => Play(entry, mode, size, mode.Kind == MatchModeKind.FreeForAll ? objectives[0] : objective, tier), 260);
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
        var menu = new SettingsMenu { Name = "SettingsMenu" };
        // Graphics changes show from the next scene (the backdrop keeps the look it was built with).
        menu.Build(_settings, _view, extra: page => page.AddChild(UiKit.CheckRow("Open every level", _settings.OpenAllLevels, on =>
        {
            // For testing: shows the whole ladder open without counting as wins.
            _settings.OpenAllLevels = on;
            _settings.Save();
            _progress.OpenAll = on || Args.Has("--unlock-all");
        })));
        column.AddChild(menu);
        column.AddChild(UiKit.Button("Back", () => Open(_title), 200));
        return Screen(UiKit.Panel(column, 940f), left: false);
    }

    private void Play(LadderLevelDef entry, GameMode mode, int size, ObjectiveChoice objective, LadderTierDef tier)
    {
        string objectiveId = LadderProgress.IdOf(mode.Kind == MatchModeKind.FreeForAll ? ObjectiveKind.Eliminate : objective.Kind);
        GameSession.LevelId = entry.Id;
        GameSession.ModeId = mode.Id;
        GameSession.Size = size;
        GameSession.TierId = tier.Id;
        GameSession.ObjectiveId = objectiveId;
        _progress.Remember(entry.Id, mode.Id, size, tier.Id, objectiveId);
        Profile.Save(_progress);
        string what = objective.Kind == ObjectiveKind.Eliminate || mode.Kind == MatchModeKind.FreeForAll ? "" : $" · {objective.DisplayName}";
        Load(GameSession.LevelScene, entry.DisplayName, $"{mode.DisplayName} · {ModeText.Size(mode, size)}{what} · {tier.DisplayName}");
    }

    /// <summary>
    /// A card saying what's loading over the menu, drawn for a frame or two before the scene changes, so
    /// the screen isn't left frozen on the menu while the place builds.
    /// </summary>
    private async void Load(string scene, string name, string line)
    {
        // The card stands alone over the backdrop, and nothing under it takes a second click.
        foreach (Control s in new[] { _title, _levels, _settingsScreen })
        {
            s.Visible = false;
        }

        VBoxContainer column = UiKit.Column(10);
        column.AddChild(UiKit.Body("LOADING", 18, UiKit.Accent));
        column.AddChild(UiKit.Title(name, 40));
        column.AddChild(UiKit.Body(line, 20, UiKit.Dim));
        AddChild(UiKit.Overlay(UiKit.Panel(column, 520f), dim: 0.6f));
        for (int i = 0; i < 2; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        GetTree().ChangeSceneToFile(scene);
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
        foreach (LadderLevelDef entry in playable.Where(l => _progress.IsOpen(l.Id)))
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

            // Picking each mode offers that mode's sizes, and the level's objectives except in free-for-all.
            int objectives = _data.Config.Rules.Objectives.Kinds.Count(k => _data.Levels[entry.Id].Objectives.Offers(k.Kind));
            for (int m = 0; m < modes.Count; m++)
            {
                buttons.First(b => b.Name == $"Mode_{entry.Id}_{m}").ButtonPressed = true;
                int offered = _levels.FindChildren($"Size_{entry.Id}_*", nameof(Button), owned: false).Count(b => !b.IsQueuedForDeletion());
                if (offered != modes[m].Sizes.Count)
                {
                    problems.Add($"{entry.Id}: {modes[m].Id} offers {offered} sizes, not {modes[m].Sizes.Count}");
                }

                bool shown = buttons.FirstOrDefault(b => b.Name == $"Objective_{entry.Id}_0")?.GetParent<Control>().Visible ?? false;
                bool wanted = modes[m].Kind != MatchModeKind.FreeForAll && objectives > 1;
                if (shown != wanted)
                {
                    problems.Add($"{entry.Id}: {modes[m].Id} {(wanted ? "doesn't offer" : "offers")} objectives");
                }
            }
        }

        // Levels still locked say what opens them (CI runs with --unlock-all, which opens them all).
        foreach (LadderLevelDef entry in playable.Where(l => !_progress.IsOpen(l.Id)))
        {
            if (_levels.FindChild($"Locked_{entry.Id}", recursive: true, owned: false) is null)
            {
                problems.Add($"{entry.Id}: locked but doesn't say so");
            }
        }

        if (!_progress.IsOpen(_data.Ladder.Levels[0].Id))
        {
            problems.Add("the first level isn't open");
        }

        // The settings: five tabs, and three slots to rebind for every action.
        string settingsNote = "no settings menu";
        if (_settingsScreen.FindChild("SettingsMenu", recursive: true, owned: false) is SettingsMenu menu)
        {
            int slots = menu.FindChildren("*", nameof(Button), owned: false)
                .Count(b => b.GetParent()?.GetParent() is BindingsList && b.Name.ToString() is var n &&
                            (n.EndsWith("_First", StringComparison.Ordinal) || n.EndsWith("_Second", StringComparison.Ordinal) || n.EndsWith("_Pad", StringComparison.Ordinal)));
            int expected = 3 * (InputSetup.Current?.Actions.Length ?? 0);
            if (menu.Tabs.GetTabCount() != 5)
            {
                problems.Add($"the settings have {menu.Tabs.GetTabCount()} tabs, not 5");
            }

            if (slots != expected)
            {
                problems.Add($"the settings offer {slots} binding slots, not {expected}");
            }

            settingsNote = $"settings in {menu.Tabs.GetTabCount()} tabs with {slots} binding slots";
        }
        else
        {
            problems.Add(settingsNote);
        }

        bool ok = playable.Length >= 1 && problems.Count == 0;
        GD.Print($"SMOKE {(ok ? "PASS" : "FAIL")}: menu shows {_data.Ladder.Levels.Length} ladder levels ({playable.Length} playable, " +
                 $"{playable.Count(l => _progress.IsOpen(l.Id))} open{(_progress.OpenAll ? " with every level open" : "")}), " +
                 $"{modes.Count} modes with their sizes, objectives and the difficulty tiers, {settingsNote}" +
                 $"{(problems.Count > 0 ? ": " + string.Join("; ", problems) : "")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
