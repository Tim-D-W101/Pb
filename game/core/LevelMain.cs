using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Pb.Game.Ai;
using Pb.Game.Audio;
using Pb.Game.Ballistics;
using Pb.Game.Player;
using Pb.Game.Ui;
using Pb.Game.World;
using Pb.Sim;
using Pb.Sim.AI;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;

namespace Pb.Game.Core;

/// <summary>
/// Composition root of a compound level: loads data, builds the sim and the level, wires every
/// presentation system to them, and runs the round (briefing card → live → summary). The level and
/// difficulty come from the menus (<see cref="GameSession"/>) or, run directly, from user args after "--":
///   --level=ID            which level to load (default: the first playable one in the ladder)
///   --tier=ID             which difficulty tier (default: "normal", or the level's first)
///   --smoke-test[=ticks]  headless CI check: walk in through the gate firing, exit code 0/1
///   --shots               camera tour of the level's viewpoints (screenshots with --write-movie)
///   --posture-demo        scripted lean / shoulder swap / muzzle-in-cover sequence at a wall corner
///   --duel-demo           scripted elimination of an opponent, then of you (mask spray, spectator view)
///   --round-tour          the round's screens in order: briefing, pause menu, a duel, spectator view, summary
///   --bot-demo            bots fighting you from cover, seen from above with the F3 overlay, then through your eyes
///   --bot-match           CI: a bot plays your slot (it hunts round the opponent spawns) until the round ends
///   --time-limit=SECONDS  overrides the tier's time limit (keeps the bot match short in CI)
/// Scripted runs skip the briefing and the summary, and keep the bots passive until a script wakes
/// them. Bots stand at the spawns the tier lists, with the behaviour their spawn's roles name and the
/// tier's difficulty; F3 shows what they're thinking.
/// </summary>
public partial class LevelMain : Node3D, ISimEventListener
{
    private GameData _data = null!;
    private PresentationDef _view = null!;
    private GameSettings _settings = null!;
    private LevelLayout _level = null!;
    private LadderLevelDef _entry = null!;
    private LadderTierDef _tier = null!;
    private MatchState _match = null!;
    private PauseMenu _pause = null!;
    private CanvasLayer _overlays = null!;
    private Control? _overlay;
    private SimWorld _sim = null!;
    private SimDriver _driver = null!;
    private LevelBuilder _world = null!;
    private PlayerController _player = null!;
    private BallRenderer _balls = null!;
    private SplatSystem _splats = null!;
    private ArcPreview _arc = null!;
    private Hud _hud = null!;
    private WorldEnvironment _environment = null!;
    private DirectionalLight3D _sun = null!;
    private readonly List<OpponentPawn> _opponents = new();
    private readonly List<BotBrain> _bots = new();
    private int[] _calloutsSeen = Array.Empty<int>();
    private BotSquad _squad = null!;
    private BotDebugOverlay _botDebug = null!;
    private LevelSmokeTest? _smoke;
    private SpectatorView? _spectator;
    private PickupVisuals _pickups = null!;
    private string? _hitBy;
    private bool _scripted;
    private bool _botMatch;
    private bool _summaryShown;
    private bool _ready;

    /// <summary>True once you've been eliminated and the spectator view is showing.</summary>
    public bool Spectating => _spectator is not null;

    public override void _Ready()
    {
        _hud = GetNode<Hud>("Hud");
        _driver = GetNode<SimDriver>("SimDriver");
        _world = GetNode<LevelBuilder>("World");
        _player = GetNode<PlayerController>("Player");
        _balls = GetNode<BallRenderer>("Balls");
        _splats = GetNode<SplatSystem>("Splats");
        _arc = GetNode<ArcPreview>("ArcPreview");
        _environment = GetNode<WorldEnvironment>("WorldEnvironment");
        _sun = GetNode<DirectionalLight3D>("Sun");

        try
        {
            var source = new GodotDataSource();
            _data = GameData.Load(source);
            _view = Jsonc.Load<PresentationDef>(source, PresentationDef.File);
            InputSetup.Apply(Jsonc.Load<InputDef>(source, InputDef.File));
            (_entry, _tier) = PickLevelAndTier(_data);
            _level = _data.Levels[_entry.Id];
        }
        catch (Exception ex) when (ex is DataException or InvalidOperationException)
        {
            GD.PushError(ex.Message);
            _hud.ShowFatal(ex.Message);
            if (Args.Has("--smoke-test"))
            {
                GetTree().Quit(1);
            }

            return;
        }

        _settings = GameSettings.Load(_view);
        _settings.ApplyVolume();
        DisplayServer.WindowSetVsyncMode(_settings.Vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);

        _sim = new SimWorld(_data.Config);
        _sim.LoadLevel(_level);
        var navWatch = Stopwatch.StartNew();
        _squad = BotSquad.ForLevel(_sim, _data.Bots, _level);
        double navMs = navWatch.Elapsed.TotalMilliseconds;
        PlayerState state = _sim.AddPlayer(0, 0, _level.PlayerSpawn, _level.PlayerSpawnYaw);
        state.Name = "You";
        Color teamColor = Color.FromHtml(_view.TeamColors[state.Team % _view.TeamColors.Length]);

        GraphicsPresetDef preset = _view.Graphics.Find(_settings.GraphicsPreset);
        _world.Build(_level, new MaterialLibrary(_level.Materials), preset.AmbientProbes, _view.Horizon);
        Atmosphere.ApplyLighting(_environment, _sun, _view.Lighting);
        Atmosphere.ApplyPreset(_environment, _sun, GetViewport(), preset);

        _player.Initialize(_sim, state, _view, _settings, teamColor);
        // Quiet opponents for the screenshot tours; the smoke test turns them hostile when it's ready.
        bool botDemo = Args.Has("--bot-demo");
        _botMatch = Args.Has("--bot-match");
        _scripted = Args.Has("--shots") || Args.Has("--posture-demo") || Args.Has("--duel-demo") || Args.Has("--smoke-test") || botDemo || _botMatch;
        bool roundTour = Args.Has("--round-tour");
        SpawnOpponents(hostile: botDemo || _botMatch || (!_scripted && !roundTour));
        _botDebug = new BotDebugOverlay { Name = "BotDebug" };
        AddChild(_botDebug);
        _botDebug.Initialize(_squad);
        _balls.Initialize(_sim.Ballistics, _view, state.Id, _player.VisualMuzzlePosition, RenderBounds());
        _splats.Initialize(_view, SplatParent);
        var fx = GetNode<ImpactFx>("ImpactFx");
        fx.Initialize(_view);
        _arc.Initialize(_sim, state, _view);
        _arc.Enabled = _view.ArcPreview.EnabledOnStart;

        bool headless = DisplayServer.GetName() == "headless";
        var audio = GetNode<AudioDirector>("Audio");
        if (!headless)
        {
            audio.Initialize(state, _view);
        }

        _hud.Initialize(_sim, state, _driver, _settings, _view, () => (_splats.ActiveCount, _splats.Capacity), () => _arc.Summary);
        _hud.InitializeMatch(_view, MatchClock);
        _hud.ShowHelp = false;
        MatchSetup setup = MatchSetup.From(_tier, state.Id);
        if (float.TryParse(Args.Value("--time-limit"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float limit))
        {
            setup = new MatchSetup
            {
                HeroId = setup.HeroId, TimeLimit = limit, StartPods = setup.StartPods, OpponentPods = setup.OpponentPods, Pickups = setup.Pickups,
            };
        }

        _match = _sim.StartMatch(setup);
        _pickups = new PickupVisuals { Name = "Pickups" };
        AddChild(_pickups);
        _pickups.Build(_sim.Pickups);
        _overlays = new CanvasLayer { Name = "Overlays", Layer = 8 };
        AddChild(_overlays);
        _pause = new PauseMenu { Name = "Pause" };
        AddChild(_pause);
        _pause.Build(_settings, _view, s => Atmosphere.ApplyPreset(_environment, _sun, GetViewport(), _view.Graphics.Find(s.GraphicsPreset)),
            restart: () => GetTree().ReloadCurrentScene());

        var maskSpray = new MaskSprayOverlay { Name = "MaskSpray" };
        AddChild(maskSpray);
        maskSpray.Initialize(_view, state.Id);

        _driver.Initialize(_sim);
        _driver.AddDriver(_player);
        foreach (OpponentPawn opponent in _opponents)
        {
            _driver.AddDriver(opponent);
        }

        _driver.AddListener(maskSpray);
        _driver.AddListener(_pickups);
        _driver.AddListener(_balls);
        _driver.AddListener(_splats);
        _driver.AddListener(fx);
        if (!headless)
        {
            _driver.AddListener(audio);
        }

        _driver.AddListener(_hud);
        _driver.AddListener(this);

        if (_scripted)
        {
            _sim.GoLive();
        }

        if (Args.Ticks("--smoke-test", 1800) is { } ticks)
        {
            _smoke = new LevelSmokeTest(this, _sim, _driver, _player, _world, ticks, _opponents, _bots);
            _player.AutoPilot = _smoke.Pilot;
        }
        else if (Args.Has("--posture-demo"))
        {
            _player.Teleport(PostureDemo.Position, PostureDemo.Yaw);
            _player.AutoPilot = new PostureDemo(this, _sim);
            _hud.ShowHelp = false;
            _hud.ShowPerf = false;
        }
        else if (Args.Has("--duel-demo"))
        {
            var demo = new DuelDemo(this, _sim, _opponents);
            demo.Setup(_player);
            _player.AutoPilot = demo;
            _hud.ShowHelp = false;
            _hud.ShowPerf = false;
        }
        else if (Args.Has("--shots"))
        {
            var tour = new ViewpointTour { Name = "ViewpointTour" };
            AddChild(tour);
            tour.Start(_level, _hud, _player.ViewModel, _view.Camera.FarClip_m);
        }
        else if (_botMatch)
        {
            var you = new OpponentSpawn { Id = "you", Position = _level.PlayerSpawn, Yaw = _level.PlayerSpawnYaw, Roles = new[] { "hunter" } };
            BotBrain brain = _squad.Add(state, _data.Bots.Archetypes["hunter"], _data.Bots.Difficulty[_tier.Bots], you);
            _player.AutoPilot = new BotPilot(brain);
            _hud.ShowPerf = false;
            GD.Print($"BOT MATCH a hunter bot plays your slot against {_bots.Count} {_tier.Bots} bots, {setup.TimeLimit:0} s on the clock");
        }
        else if (botDemo)
        {
            var demo = new BotDemo { Name = "BotDemo" };
            AddChild(demo);
            demo.Start(_sim, _player, _opponents, _botDebug, _hud, _view.Camera.FarClip_m);
        }
        else if (roundTour)
        {
            ShowOverlay(RoundScreens.Briefing(_level, _tier, BeginRound, BackToLevelSelect));
            _hud.ShowPerf = false;
            var tour = new RoundTour { Name = "RoundTour" };
            AddChild(tour);
            tour.Start(() =>
            {
                var demo = new DuelDemo(this, _sim, _opponents);
                demo.Setup(_player);
                _player.AutoPilot = demo;
                BeginRound();
            }, () => _summaryShown, _pause);
        }
        else
        {
            ShowOverlay(RoundScreens.Briefing(_level, _tier, BeginRound, BackToLevelSelect));
        }

        GD.Print($"Level {_level.Id} ({_tier.Id}, {_opponents.Count} {_tier.Bots} bots): {_level.Primitives.Count} primitives, " +
                 $"{_world.MeshCount} meshes, {_world.ColliderCount} walking colliders, {_sim.Collision.Colliders.Count} paint colliders, " +
                 $"{_squad.Grid.SpanCount} nav spans and {_squad.Cover.Points.Count} cover points in {navMs:0} ms, preset {preset.Name}");
        _ready = true;
    }

    public void OnSimEvent(in SimEvent e)
    {
        _squad.Hear(e);
        if (e.Type == SimEventType.ShotFired && e.PlayerId == _player.State.Id)
        {
            _player.ViewModel.Kick();
        }
        else if (e.Type == SimEventType.PlayerEliminated)
        {
            OnEliminated(e);
        }
        else if (e.Type == SimEventType.RoundEnded)
        {
            OnRoundEnded();
        }

        _smoke?.OnSimEvent(e);
    }

    /// <summary>Bots (team 1) at the spawns the tier lists, each with its spawn's behaviour at the tier's difficulty.</summary>
    private void SpawnOpponents(bool hostile)
    {
        var parent = new Node3D { Name = "Opponents" };
        AddChild(parent);
        string[] callsigns = DealCallsigns(_view.Hud.Callsigns, _data.Config.MatchSeed);
        Color jersey = Color.FromHtml(_view.TeamColors[1 % _view.TeamColors.Length]);
        for (int i = 0; i < _tier.Opponents.Length; i++)
        {
            OpponentSpawn spawn = _level.OpponentSpawns.First(s => s.Id == _tier.Opponents[i]);
            PlayerState state = _sim.AddPlayer(i + 1, 1, spawn.Position, spawn.Yaw);
            state.Name = callsigns[i % callsigns.Length];
            BotBrain brain = _squad.Add(state, _data.Bots.ArchetypeFor(spawn.Roles)!, _data.Bots.Difficulty[_tier.Bots], spawn);
            brain.Passive = !hostile;
            var pawn = new OpponentPawn { Name = $"Opponent_{spawn.Id}" };
            parent.AddChild(pawn);
            pawn.Initialize(_sim, state, jersey, new BotPilot(brain));
            _opponents.Add(pawn);
            _bots.Add(brain);
        }

        _calloutsSeen = Enumerable.Repeat(-1, _bots.Count).ToArray();
    }

    /// <summary>The callsigns in a shuffled order for this round (the same order for the same match seed).</summary>
    private static string[] DealCallsigns(string[] callsigns, ulong seed)
    {
        string[] dealt = (string[])callsigns.Clone();
        var rng = new Pb.Sim.Core.Pcg32(seed ^ 0xCA115165);
        for (int i = dealt.Length - 1; i > 0; i--)
        {
            int j = (int)(rng.NextUInt() % (uint)(i + 1));
            (dealt[i], dealt[j]) = (dealt[j], dealt[i]);
        }

        return dealt;
    }

    private Node3D? SplatParent(int receiverId, int part)
    {
        if (!PlayerHitboxes.IsPlayer(receiverId))
        {
            return null;
        }

        int id = PlayerHitboxes.PlayerIdOf(receiverId);
        OpponentPawn? pawn = _opponents.FirstOrDefault(o => o.State.Id == id);
        return pawn?.Visual.PartNode((Pb.Sim.Collision.HitboxPart)part);
    }

    private void OnEliminated(in SimEvent e)
    {
        PlayerState? victim = _sim.FindPlayer(e.TargetId);
        PlayerState? shooter = _sim.FindPlayer(e.PlayerId);
        if (victim is null)
        {
            return;
        }

        string part = SpectatorView.PartName((Pb.Sim.Collision.HitboxPart)e.Extra);
        if (victim == _player.State)
        {
            StartSpectating(victim, shooter);
            return;
        }

        _opponents.FirstOrDefault(o => o.State == victim)?.CallHit();
        if (shooter == _player.State)
        {
            float distance = System.Numerics.Vector3.Distance(shooter.EyePosition, victim.EyePosition);
            int left = _opponents.Count(o => o.State.Alive);
            _hud.Toast($"You eliminated {victim.Name} · {part} · {distance:0} m   ({left} left)", 3.0);
        }
    }

    private void StartSpectating(PlayerState victim, PlayerState? shooter)
    {
        if (_spectator is not null)
        {
            return;
        }

        if (shooter is not null)
        {
            float distance = System.Numerics.Vector3.Distance(shooter.EyePosition, victim.EyePosition);
            _hitBy = $"{shooter.Name} got you: {SpectatorView.PartName(victim.EliminatedPart)}, {distance:0} m.";
        }

        _player.AutoPilot = new IdlePilot();
        _player.ViewModel.Visible = false;
        _hud.Visible = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _spectator = new SpectatorView { Name = "Spectator" };
        AddChild(_spectator);
        // Once you've seen who got you, the summary (scripted runs just end; the smoke test ends itself).
        _spectator.Start(victim, shooter, _view.Spectator, _view.Camera.FarClip_m, () =>
        {
            if (!_scripted)
            {
                ShowSummary();
            }
            else if (_smoke is null)
            {
                GetTree().Quit();
            }
        });
    }

    /// <summary>The briefing card's Start: the round goes live and the mouse is yours.</summary>
    private void BeginRound()
    {
        CloseOverlay();
        _sim.GoLive();
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void OnRoundEnded()
    {
        if (_botMatch)
        {
            FinishBotMatch();
            return;
        }

        if (_scripted)
        {
            return;
        }

        // Out: the spectator view shows who got you first. Otherwise a moment to take it in.
        if (_spectator is null)
        {
            GetTree().CreateTimer(1.5).Timeout += ShowSummary;
        }
    }

    /// <summary>The bot match is over: report how it went, pass if nothing went wrong on the way.</summary>
    private void FinishBotMatch()
    {
        PlayerStats you = _match.StatsFor(_player.State.Id)!;
        int errors = _driver.ErrorCount;
        bool ok = errors == 0 && _match.Outcome != RoundOutcome.None;
        GD.Print($"SMOKE {(ok ? "PASS" : "FAIL")}: bot match {_match.Outcome} after {_match.Elapsed:0} s: you put out {you.Eliminations} of " +
                 $"{_opponents.Count}, {you.Shots} shots, {you.Hits} hits, {you.Pickups} pickups; simErrors={errors} avgStepMs={_driver.AverageStepMs:0.000}");
        GetTree().Quit(ok ? 0 : 1);
    }

    private void ShowSummary()
    {
        if (_match.Phase != MatchPhase.Ended || _overlay is not null)
        {
            return;
        }

        _hud.Visible = false;
        _summaryShown = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        PlayerStats you = _match.StatsFor(_player.State.Id)!;
        ShowOverlay(RoundScreens.Summary(_level, _tier, _match, you, _opponents.Count, _hitBy,
            retry: () => GetTree().ReloadCurrentScene(),
            levelSelect: BackToLevelSelect,
            mainMenu: () => GetTree().ChangeSceneToFile(GameSession.MainScene)));
    }

    private void BackToLevelSelect()
    {
        GameSession.ReturnTo = "levels";
        GetTree().ChangeSceneToFile(GameSession.MainScene);
    }

    private void ShowOverlay(Control overlay)
    {
        CloseOverlay();
        _overlay = overlay;
        _overlays.AddChild(overlay);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void CloseOverlay()
    {
        _overlay?.QueueFree();
        _overlay = null;
    }

    /// <summary>The top bar's clock: the full time limit during the briefing, then the time left.</summary>
    private string MatchClock() =>
        RoundScreens.Clock(_match.Phase == MatchPhase.Briefing ? _match.Setup.TimeLimit : _match.TimeLeft);

    /// <summary>Shows what the bots shout as subtitles, when you're close enough to hear it.</summary>
    public override void _Process(double delta)
    {
        if (!_ready)
        {
            return;
        }

        HudDef hud = _view.Hud;
        for (int i = 0; i < _bots.Count; i++)
        {
            BotBrain bot = _bots[i];
            if (bot.CalloutTick <= _calloutsSeen[i])
            {
                continue;
            }

            _calloutsSeen[i] = bot.CalloutTick;
            string[] lines = hud.Callouts.For(bot.Callout);
            float distance = System.Numerics.Vector3.Distance(bot.Self.Position, _player.State.Position);
            if (lines.Length > 0 && distance <= hud.SubtitleRange_m)
            {
                _hud.Subtitle(bot.Self.Name, bot.Self.Team, lines[(bot.Self.Id * 31 + bot.CalloutTick) % lines.Length]);
            }
        }
    }

    /// <summary>No input: what an eliminated local player sends while spectating.</summary>
    private sealed class IdlePilot : ICommandSource
    {
        public InputCommand Next(int tick, PlayerState state) => new() { Tick = tick, Yaw = state.Yaw, Pitch = state.Pitch };
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!_ready)
        {
            return;
        }

        bool playing = _overlay is null && _spectator is null && _player.AutoPilot is null;
        if (playing && e is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (e.IsActionPressed("pause"))
        {
            if (playing && !_pause.Open)
            {
                _pause.Toggle();
            }
        }
        else if (e.IsActionPressed("debug_help"))
        {
            _hud.ShowHelp = !_hud.ShowHelp;
        }
        else if (e.IsActionPressed("debug_stress"))
        {
            _botDebug.Visible = !_botDebug.Visible;
            _hud.Toast(_botDebug.Visible ? "Bot debug overlay on" : "Bot debug overlay off");
        }
        else if (e.IsActionPressed("debug_arc"))
        {
            _arc.Enabled = !_arc.Enabled;
            _hud.Toast(_arc.Enabled ? "Arc preview on" : "Arc preview off");
        }
        else if (e.IsActionPressed("debug_perf"))
        {
            _hud.ShowPerf = !_hud.ShowPerf;
        }
        else if (e.IsActionPressed("toggle_crosshair"))
        {
            _settings.Crosshair = !_settings.Crosshair;
            SaveAndToast(_settings.Crosshair ? "Crosshair on" : "Crosshair off");
        }
        else if (e.IsActionPressed("toggle_head_bob"))
        {
            _settings.HeadBob = !_settings.HeadBob;
            SaveAndToast(_settings.HeadBob ? "Head-bob on" : "Head-bob off");
        }
        else if (e.IsActionPressed("toggle_vsync"))
        {
            _settings.Vsync = !_settings.Vsync;
            DisplayServer.WindowSetVsyncMode(_settings.Vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
            SaveAndToast(_settings.Vsync ? "V-sync on" : "V-sync off");
        }
        else if (e.IsActionPressed("cycle_graphics"))
        {
            GraphicsPresetDef[] presets = _view.Graphics.Presets;
            int index = Array.FindIndex(presets, p => p.Name == _settings.GraphicsPreset);
            GraphicsPresetDef next = presets[(index + 1) % presets.Length];
            _settings.GraphicsPreset = next.Name;
            Atmosphere.ApplyPreset(_environment, _sun, GetViewport(), next);
            SaveAndToast($"Graphics: {next.Name}");
        }
        else if (e.IsActionPressed("toggle_invert_y"))
        {
            _settings.InvertY = !_settings.InvertY;
            SaveAndToast(_settings.InvertY ? "Invert Y on" : "Invert Y off");
        }
        else if (e.IsActionPressed("toggle_fullscreen"))
        {
            bool full = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen;
            DisplayServer.WindowSetMode(full ? DisplayServer.WindowMode.Windowed : DisplayServer.WindowMode.Fullscreen);
        }
        else if (e.IsActionPressed("fov_down") || e.IsActionPressed("fov_up"))
        {
            float step = e.IsActionPressed("fov_up") ? _view.Camera.FovStep_deg : -_view.Camera.FovStep_deg;
            _settings.FovDeg = Mathf.Clamp(_settings.FovDeg + step, _view.Camera.FovMin_deg, _view.Camera.FovMax_deg);
            SaveAndToast($"Field of view {_settings.FovDeg:0}° (horizontal)");
        }
        else if (e.IsActionPressed("sensitivity_down") || e.IsActionPressed("sensitivity_up"))
        {
            float step = e.IsActionPressed("sensitivity_up") ? _view.Look.SensitivityStep : -_view.Look.SensitivityStep;
            _settings.MouseSensitivityDegPerCount = Mathf.Clamp(_settings.MouseSensitivityDegPerCount + step, 0.005f, 1f);
            SaveAndToast($"Mouse sensitivity {_settings.MouseSensitivityDegPerCount:0.000}°/count");
        }
        else if (e.IsActionPressed("reload_data"))
        {
            GetTree().ReloadCurrentScene();
        }
    }

    private void SaveAndToast(string message)
    {
        _settings.Save();
        _hud.Toast(message);
    }

    private Aabb RenderBounds()
    {
        Pb.Sim.Collision.Aabb b = _level.Bounds;
        Vector3 min = b.Min.ToGodot() - new Vector3(5, 5, 5);
        Vector3 max = b.Max.ToGodot() + new Vector3(5, 5, 5);
        return new Aabb(min, max - min);
    }

    /// <summary>The menus' choice, else --level/--tier, else the first playable level on "normal" (or its first tier).</summary>
    private static (LadderLevelDef Entry, LadderTierDef Tier) PickLevelAndTier(GameData data)
    {
        string? levelId = GameSession.LevelId ?? Args.Value("--level");
        LadderLevelDef? entry = levelId is not null
            ? data.Ladder.Levels.FirstOrDefault(l => l.Id == levelId && data.Levels.ContainsKey(l.Id))
            : data.Ladder.Levels.FirstOrDefault(l => data.Levels.ContainsKey(l.Id));
        if (entry is null)
        {
            throw new InvalidOperationException(levelId is null
                ? "The ladder has no playable level."
                : $"No playable level '{levelId}' (known: {string.Join(", ", data.Levels.Keys)})");
        }

        LadderTierDef[] tiers = entry.Tiers!;
        string tierId = GameSession.TierId ?? Args.Value("--tier") ?? "normal";
        return (entry, tiers.FirstOrDefault(t => t.Id == tierId) ?? tiers[0]);
    }
}
