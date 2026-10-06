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
/// The main scene: title, where to play (an area, the place in it, the mode, size, objective and difficulty: every
/// area and place open), the training ground (the Phase 1 range), settings and quit. Start stores the choices in
/// <see cref="GameSession"/> and loads the level, where the briefing card takes over. <c>-- --smoke-test</c> builds
/// every screen, checks every area offers all its places and the round choices (and that picking a mode offers its
/// sizes, and a place its objectives), and quits; <c>-- --menu-tour</c> shows each screen in turn (for screenshots
/// with <c>--write-movie</c>) and quits. <c>-- --level=ID</c> (with <c>--place</c>, <c>--mode</c>, <c>--size</c>,
/// <c>--tier</c> and the level's other options) skips the menu once and goes straight to the level: exported builds
/// can't be told which scene to run, so that's how a build starts a round, or a scripted run, from the command line.
/// </summary>
public partial class MainMenu : Control
{
    private GameData _data = null!;
    private PresentationDef _view = null!;
    private GameSettings _settings = null!;
    private RecordBook _records = null!;
    private Control _title = null!;
    private Control _levels = null!;
    private Control _settingsScreen = null!;
    private int _tourFrame = -1;
    private TextureRect _backdrop = null!;
    private static bool _skippedToLevel;
    private static bool _runtimeLogged;

    public override void _Ready()
    {
        if (OS.HasFeature("template") && !_runtimeLogged)
        {
            // In the log a player sends: the runtime, and whether the export kept game/Pb.csproj's JIT switches.
            _runtimeLogged = true;
            GD.Print($".NET {System.Environment.Version}: TieredPGO={AppContext.GetData("System.Runtime.TieredPGO") ?? "default"}, "
                + $"QuickJitForLoops={AppContext.GetData("System.Runtime.TieredCompilation.QuickJitForLoops") ?? "default"}");
        }

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
        _records = Profile.Load(_data.Areas);
        bool skipping = Args.Has("--level") && !_skippedToLevel;
        if (GameSession.LevelId is null && !skipping && _records.Data.Last is { } last && _data.Levels.ContainsKey(last.Level))
        {
            // The choices you made last time the game ran (not when skipping to the level the command line names).
            GameSession.LevelId = last.Level;
            GameSession.PlaceId = last.Place.Length > 0 ? last.Place : null;
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
            // The area you played last (else the first) behind the menu.
            scene.Build(_data, _view, _settings, ShownArea().Id);
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
            case 55:
                // A part of the first area, then teams there (with its objectives).
                Press("Place_*_1");
                break;
            case 65:
                Press("Mode_*_2");
                break;
            case 75:
            case 85:
            case 95:
                // Each of the other areas in turn, every one open.
                Press($"Area_{(_tourFrame - 66) / 10}");
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

    /// <summary>Presses the first button on the place picker named like <paramref name="pattern"/> (the tour).</summary>
    private void Press(string pattern)
    {
        if (_levels.FindChild(pattern, recursive: true, owned: false) is Button button)
        {
            button.ButtonPressed = true;
        }
    }

    private void Open(Control screen, Button? focus = null)
    {
        foreach (Control s in new[] { _title, _levels, _settingsScreen })
        {
            s.Visible = s == screen;
        }

        // Keyboard and pad navigation start on the first button, not a Back up in the corner (or the one asked for).
        Button[] buttons = screen.FindChildren("*", nameof(Button), owned: false).OfType<Button>().Where(b => !b.Disabled).ToArray();
        focus ??= buttons.FirstOrDefault(b => b.Name != "Back") ?? buttons.FirstOrDefault();
        focus?.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>Where to play, built afresh (your records may have changed), on the area you played last.</summary>
    private void ShowLevels()
    {
        int index = _levels.GetIndex();
        RemoveChild(_levels);
        _levels.QueueFree();
        _levels = LevelSelect();
        AddChild(_levels);
        MoveChild(_levels, index);
        Open(_levels, _levels.FindChild($"Start_{ShownArea().Id}", recursive: true, owned: false) as Button);
    }

    /// <summary>The area last chosen (this session or the last), else the first.</summary>
    private AreaEntryDef ShownArea() =>
        _data.Areas.Areas.FirstOrDefault(a => a.Id == GameSession.LevelId && _data.Levels.ContainsKey(a.Id)) ?? _data.Areas.Areas[0];

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

    /// <summary>
    /// Where to play: Back up by the title, the areas in a row (every one open), and the chosen area's card below,
    /// rebuilt when you pick another.
    /// </summary>
    private Control LevelSelect()
    {
        VBoxContainer column = UiKit.Column(16);
        HBoxContainer header = UiKit.Row(12);
        Label title = UiKit.Title("Where to play", 40);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        Button back = UiKit.Button("Back", () => Open(_title), 200);
        back.Name = "Back";
        back.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        header.AddChild(back);
        column.AddChild(header);
        Label lead = UiKit.Body("Every area is open, and so is every place in it: pick one, then how you want to play. " +
                                "Difficulty sets how good the bots are, never how many there are.", 18, UiKit.Dim, wrap: true);
        lead.CustomMinimumSize = new Vector2(CardWidth, 0);
        column.AddChild(lead);

        AreaEntryDef[] areas = _data.Areas.Areas.Where(a => _data.Levels.ContainsKey(a.Id)).ToArray();
        var holder = new VBoxContainer();
        int shown = Math.Max(0, Array.IndexOf(areas, ShownArea()));
        column.AddChild(UiKit.ChoiceRow("Area", areas.Select(a => a.DisplayName).ToArray(), shown, k =>
        {
            foreach (Node child in holder.GetChildren())
            {
                holder.RemoveChild(child);
                child.QueueFree();
            }

            holder.AddChild(AreaCard(areas[k]));
        }, "Area_", buttonWidth: 230, wrap: true));
        holder.AddChild(AreaCard(areas[shown]));
        column.AddChild(holder);
        return Screen(column, left: true);
    }

    /// <summary>The card's width; its two columns are where in the area (left) and how to play it (right).</summary>
    private const float CardWidth = 1300f;

    private Control AreaCard(AreaEntryDef entry)
    {
        VBoxContainer card = UiKit.Column(12);
        card.AddChild(UiKit.Title(entry.DisplayName, 28));
        AddRoundChoices(card, entry, _data.Levels[entry.Id]);
        return UiKit.Panel(card, CardWidth);
    }

    /// <summary>Your record in this place of the area, in this mode, objective and difficulty, and the difficulties you've won on there.</summary>
    private string RecordLine(AreaEntryDef entry, PlaceSpec place, GameMode mode, ObjectiveChoice objective, TierDef tier)
    {
        string[] won = entry.Tiers.Where(t => _records.WonOn(entry.Id, place.Id, t.Id)).Select(t => t.DisplayName).ToArray();
        string wins = won.Length > 0 ? $" Won here on {string.Join(", ", won)}." : "";
        ObjectiveKind kind = mode.Kind == MatchModeKind.FreeForAll ? ObjectiveKind.Eliminate : objective.Kind;
        LevelRecord? r = _records.Record(entry.Id, place.Id, mode.Id, tier.Id, RecordBook.IdOf(kind));
        string what = kind == ObjectiveKind.Eliminate ? $"{mode.DisplayName}, {tier.DisplayName}" : $"{mode.DisplayName}, {objective.DisplayName}, {tier.DisplayName}";
        if (r is null)
        {
            return $"Your record here ({what}): no rounds yet.{wins}";
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
        return $"Your record here ({what}): {string.Join(" · ", parts)}.{wins}";
    }

    /// <summary>
    /// Where in the area (left), then the mode, size, objective and difficulty (right), with what each means, your
    /// record and Start: last round's choices, else the whole area, the first mode at its default size, eliminate, on
    /// Normal. Picking a mode offers its sizes; picking a place offers the objectives it has room for (none in
    /// free-for-all, which is always last one standing).
    /// </summary>
    private void AddRoundChoices(VBoxContainer card, AreaEntryDef entry, LevelLayout level)
    {
        TierDef[] tiers = entry.Tiers;
        IReadOnlyList<GameMode> modes = _data.Config.Rules.Modes;
        bool again = GameSession.LevelId == entry.Id;
        IReadOnlyList<PlaceSpec> places = level.Places;
        int placeIndex = Math.Max(0, again ? places.ToList().FindIndex(p => p.Id == GameSession.PlaceId) : 0);
        PlaceSpec place = places[placeIndex];
        int modeIndex = Math.Max(0, again ? modes.ToList().FindIndex(m => m.Id == GameSession.ModeId) : 0);
        GameMode mode = modes[modeIndex];
        int size = again && GameSession.Size is { } last && mode.Sizes.Contains(last) ? last : mode.DefaultSize;
        ObjectiveChoice[] Offered(PlaceSpec p) =>
            _data.Config.Rules.Objectives.Kinds.Where(k => level.ForPlace(p).Objectives.Offers(k.Kind)).ToArray();
        ObjectiveChoice[] objectives = Offered(place);
        ObjectiveChoice objective = objectives.FirstOrDefault(o => again && RecordBook.IdOf(o.Kind) == GameSession.ObjectiveId) ?? objectives[0];
        int tierIndex = Array.FindIndex(tiers, t => again && t.Id == GameSession.TierId);
        if (tierIndex < 0)
        {
            tierIndex = Math.Max(0, Array.FindIndex(tiers, t => t.Id == "normal"));
        }

        TierDef tier = tiers[tierIndex];

        HBoxContainer columns = UiKit.Row(36);
        VBoxContainer where = UiKit.Column(12);
        where.CustomMinimumSize = new Vector2(360, 0);
        VBoxContainer how = UiKit.Column(10);
        how.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        columns.AddChild(where);
        columns.AddChild(how);
        card.AddChild(columns);

        Label placeBlurb = UiKit.Body(place.Description, 17, UiKit.Dim, wrap: true);
        placeBlurb.CustomMinimumSize = new Vector2(360, 0);
        // The place as you'd see it there: a picture from one of its viewpoints (none until it's been taken).
        var still = new TextureRect
        {
            Name = $"Still_{entry.Id}",
            CustomMinimumSize = new Vector2(360, 160),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            Texture = PlaceStills.For(entry.Id, place.Id),
        };
        Label modeBlurb = UiKit.Body(mode.Description, 17, UiKit.Dim, wrap: true);
        var sizes = new VBoxContainer();
        var objectiveBox = new VBoxContainer();
        objectiveBox.AddThemeConstantOverride("separation", 8);
        Label details = UiKit.Body(TierDetails(tier), 17, UiKit.Dim, wrap: true);
        Label record = UiKit.Body("", 17, UiKit.Text, wrap: true);
        record.Name = $"Record_{entry.Id}";
        void ShowRecord() => record.Text = RecordLine(entry, place, mode, objective, tier);

        void ShowObjectives()
        {
            foreach (Node child in objectiveBox.GetChildren())
            {
                objectiveBox.RemoveChild(child);
                child.QueueFree();
            }

            objectives = Offered(place);
            objective = objectives.FirstOrDefault(o => o.Kind == objective.Kind) ?? objectives[0];
            if (mode.Kind == MatchModeKind.FreeForAll || objectives.Length < 2)
            {
                return;
            }

            Label blurb = UiKit.Body(objective.Description, 17, UiKit.Dim, wrap: true);
            objectiveBox.AddChild(UiKit.ChoiceRow("Objective", objectives.Select(o => o.DisplayName).ToArray(), Array.IndexOf(objectives, objective), k =>
            {
                objective = objectives[k];
                blurb.Text = objective.Description;
                ShowRecord();
            }, $"Objective_{entry.Id}_", buttonWidth: 160));
            objectiveBox.AddChild(blurb);
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

        where.AddChild(UiKit.ChoiceColumn("WHERE", places.Select(p => p.DisplayName).ToArray(), placeIndex, k =>
        {
            place = places[k];
            placeBlurb.Text = place.Description;
            still.Texture = PlaceStills.For(entry.Id, place.Id);
            ShowObjectives();
            ShowRecord();
        }, $"Place_{entry.Id}_", buttonWidth: 360));
        where.AddChild(still);
        where.AddChild(placeBlurb);

        how.AddChild(UiKit.ChoiceRow("Mode", modes.Select(m => m.DisplayName).ToArray(), modeIndex, k =>
        {
            mode = modes[k];
            size = mode.DefaultSize;
            modeBlurb.Text = mode.Description;
            ShowSizes();
            ShowObjectives();
            ShowRecord();
        }, $"Mode_{entry.Id}_", buttonWidth: 190));
        how.AddChild(modeBlurb);
        ShowSizes();
        how.AddChild(sizes);
        ShowObjectives();
        how.AddChild(objectiveBox);
        how.AddChild(UiKit.ChoiceRow("Difficulty", tiers.Select(t => t.DisplayName).ToArray(), tierIndex, k =>
        {
            tier = tiers[k];
            details.Text = TierDetails(tier);
            ShowRecord();
        }, $"Tier_{entry.Id}_", buttonWidth: 140));
        how.AddChild(details);
        ShowRecord();
        how.AddChild(record);
        Button start = UiKit.Button("Start", () => Play(entry, place, mode, size, mode.Kind == MatchModeKind.FreeForAll ? objectives[0] : objective, tier), 260);
        start.Name = $"Start_{entry.Id}";
        start.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        how.AddChild(start);
    }

    private static string TierDetails(TierDef tier) =>
        $"{tier.DisplayName}: {RoundScreens.Clock(tier.TimeLimit_s)} on the clock · you start with {tier.StartPods} spare " +
        $"pod{(tier.StartPods == 1 ? "" : "s")}, every bot carries {tier.BotPods} · {(tier.Pickups ? "pickups out" : "no pickups")}.";

    private Control SettingsScreen()
    {
        VBoxContainer column = UiKit.Column(16);
        var menu = new SettingsMenu { Name = "SettingsMenu" };
        // Graphics changes show from the next scene (the backdrop keeps the look it was built with).
        menu.Build(_settings, _view);
        column.AddChild(menu);
        column.AddChild(UiKit.Button("Back", () => Open(_title), 200));
        return Screen(UiKit.Panel(column, 940f), left: false);
    }

    private void Play(AreaEntryDef entry, PlaceSpec place, GameMode mode, int size, ObjectiveChoice objective, TierDef tier)
    {
        string objectiveId = RecordBook.IdOf(mode.Kind == MatchModeKind.FreeForAll ? ObjectiveKind.Eliminate : objective.Kind);
        GameSession.LevelId = entry.Id;
        GameSession.PlaceId = place.Id;
        GameSession.ModeId = mode.Id;
        GameSession.Size = size;
        GameSession.TierId = tier.Id;
        GameSession.ObjectiveId = objectiveId;
        _records.Remember(entry.Id, place.Id, mode.Id, size, tier.Id, objectiveId);
        Profile.Save(_records);
        string what = objective.Kind == ObjectiveKind.Eliminate || mode.Kind == MatchModeKind.FreeForAll ? "" : $" · {objective.DisplayName}";
        string part = place.Whole ? "" : $"{place.DisplayName} · ";
        Load(GameSession.LevelScene, entry.DisplayName, $"{part}{mode.DisplayName} · {ModeText.Size(mode, size)}{what} · {tier.DisplayName}");
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
        IReadOnlyList<GameMode> modes = _data.Config.Rules.Modes;
        AreaEntryDef[] areas = _data.Areas.Areas.Where(a => _data.Levels.ContainsKey(a.Id)).ToArray();
        if (areas.Length != _data.Areas.Areas.Length)
        {
            problems.Add($"{_data.Areas.Areas.Length - areas.Length} areas without a level");
        }

        int places = 0;
        for (int a = 0; a < areas.Length; a++)
        {
            // Every area is open: picking it shows its card, with all its places and the round choices.
            AreaEntryDef entry = areas[a];
            LevelLayout level = _data.Levels[entry.Id];
            if (_levels.FindChild($"Area_{a}", recursive: true, owned: false) is not Button areaButton)
            {
                problems.Add($"{entry.Id}: no button to pick it");
                continue;
            }

            areaButton.ButtonPressed = true;
            Button[] buttons = _levels.FindChildren("*", nameof(Button), owned: false).OfType<Button>().Where(b => !b.IsQueuedForDeletion()).ToArray();
            int Count(string prefix) => buttons.Count(b => b.Name.ToString().StartsWith(prefix, StringComparison.Ordinal));
            places += Count($"Place_{entry.Id}_");
            if (Count($"Place_{entry.Id}_") != level.Places.Count || level.Places.Count < 2)
            {
                problems.Add($"{entry.Id}: {Count($"Place_{entry.Id}_")} places offered, not {level.Places.Count}");
            }

            if (Count($"Mode_{entry.Id}_") != modes.Count)
            {
                problems.Add($"{entry.Id}: {Count($"Mode_{entry.Id}_")} mode buttons, not {modes.Count}");
            }

            if (Count($"Tier_{entry.Id}_") != entry.Tiers.Length)
            {
                problems.Add($"{entry.Id}: {Count($"Tier_{entry.Id}_")} difficulty buttons");
            }

            if (Count($"Start_{entry.Id}") != 1)
            {
                problems.Add($"{entry.Id}: no Start button");
            }

            // In each place, picking each mode offers that mode's sizes, and the place's objectives except in free-for-all.
            for (int p = 0; p < level.Places.Count; p++)
            {
                buttons.First(b => b.Name == $"Place_{entry.Id}_{p}").ButtonPressed = true;
                int objectives = _data.Config.Rules.Objectives.Kinds.Count(k => level.ForPlace(level.Places[p]).Objectives.Offers(k.Kind));
                for (int m = 0; m < modes.Count; m++)
                {
                    buttons.First(b => b.Name == $"Mode_{entry.Id}_{m}").ButtonPressed = true;
                    int offered = _levels.FindChildren($"Size_{entry.Id}_*", nameof(Button), owned: false).Count(b => !b.IsQueuedForDeletion());
                    if (offered != modes[m].Sizes.Count)
                    {
                        problems.Add($"{entry.Id}: {modes[m].Id} offers {offered} sizes, not {modes[m].Sizes.Count}");
                    }

                    int shown = _levels.FindChildren($"Objective_{entry.Id}_*", nameof(Button), owned: false).Count(b => !b.IsQueuedForDeletion());
                    int wanted = modes[m].Kind != MatchModeKind.FreeForAll && objectives > 1 ? objectives : 0;
                    if (shown != wanted)
                    {
                        problems.Add($"{entry.Id}, {level.Places[p].Id}: {modes[m].Id} offers {shown} objectives, not {wanted}");
                    }
                }
            }
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

        bool ok = areas.Length >= 1 && problems.Count == 0;
        GD.Print($"SMOKE {(ok ? "PASS" : "FAIL")}: menu shows {areas.Length} area{(areas.Length == 1 ? "" : "s")} with {places} places to play, all open, " +
                 $"{modes.Count} modes with their sizes, objectives and the difficulty tiers, {settingsNote}" +
                 $"{(problems.Count > 0 ? ": " + string.Join("; ", problems) : "")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
