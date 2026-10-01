using System;
using System.Linq;
using Godot;
using Pb.Game.Audio;
using Pb.Game.Ballistics;
using Pb.Game.Player;
using Pb.Game.Ui;
using Pb.Game.World;
using Pb.Sim;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Players;

namespace Pb.Game.Core;

/// <summary>
/// Composition root of a compound level: loads data, builds the sim and the level, and wires every
/// presentation system to them. User args after "--":
///   --level=ID            which level to load (default: the first playable one in the ladder)
///   --smoke-test[=ticks]  headless CI check: walk in through the gate firing, exit code 0/1
///   --shots               camera tour of the level's viewpoints (screenshots with --write-movie)
///   --posture-demo        scripted lean / shoulder swap / muzzle-in-cover sequence at a wall corner
/// </summary>
public partial class LevelMain : Node3D, ISimEventListener
{
    private GameData _data = null!;
    private PresentationDef _view = null!;
    private GameSettings _settings = null!;
    private LevelLayout _level = null!;
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
    private LevelSmokeTest? _smoke;
    private bool _ready;

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
            _level = PickLevel(_data);
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
        DisplayServer.WindowSetVsyncMode(_settings.Vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);

        _sim = new SimWorld(_data.Config);
        _sim.LoadLevel(_level);
        PlayerState state = _sim.AddPlayer(0, 0, _level.PlayerSpawn, _level.PlayerSpawnYaw);
        Color teamColor = Color.FromHtml(_view.TeamColors[state.Team % _view.TeamColors.Length]);

        GraphicsPresetDef preset = _view.Graphics.Find(_settings.GraphicsPreset);
        _world.Build(_level, new MaterialLibrary(_level.Materials), preset.AmbientProbes, _view.Horizon);
        Atmosphere.ApplyLighting(_environment, _sun, _view.Lighting);
        Atmosphere.ApplyPreset(_environment, _sun, GetViewport(), preset);

        _player.Initialize(_sim, state, _view, _settings, teamColor);
        _balls.Initialize(_sim.Ballistics, _view, state.Id, _player.VisualMuzzlePosition, RenderBounds());
        _splats.Initialize(_view, _ => null);
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

        _driver.Initialize(_sim);
        _driver.AddDriver(_player);
        _driver.AddListener(_balls);
        _driver.AddListener(_splats);
        _driver.AddListener(fx);
        if (!headless)
        {
            _driver.AddListener(audio);
        }

        _driver.AddListener(_hud);
        _driver.AddListener(this);

        if (Args.Ticks("--smoke-test", 1800) is { } ticks)
        {
            _smoke = new LevelSmokeTest(this, _sim, _driver, _player, _world, ticks);
            _player.AutoPilot = _smoke.Pilot;
        }
        else if (Args.Has("--posture-demo"))
        {
            _player.Teleport(PostureDemo.Position, PostureDemo.Yaw);
            _player.AutoPilot = new PostureDemo(this, _sim);
            _hud.ShowHelp = false;
            _hud.ShowPerf = false;
        }
        else if (Args.Has("--shots"))
        {
            var tour = new ViewpointTour { Name = "ViewpointTour" };
            AddChild(tour);
            tour.Start(_level, _hud, _player.ViewModel, _view.Camera.FarClip_m);
        }
        else if (!headless)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }

        GD.Print($"Level {_level.Id}: {_level.Primitives.Count} primitives, {_world.MeshCount} meshes, " +
                 $"{_world.ColliderCount} walking colliders, {_sim.Collision.Colliders.Count} paint colliders, preset {preset.Name}");
        _ready = true;
    }

    public void OnSimEvent(in SimEvent e)
    {
        if (e.Type == SimEventType.ShotFired && e.PlayerId == _player.State.Id)
        {
            _player.ViewModel.Kick();
        }

        _smoke?.OnSimEvent(e);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!_ready)
        {
            return;
        }

        if (e is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured && _player.AutoPilot is null)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (e.IsActionPressed("release_mouse"))
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (e.IsActionPressed("debug_help"))
        {
            _hud.ShowHelp = !_hud.ShowHelp;
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
        else if (e.IsActionPressed("debug_reset_gear"))
        {
            _sim.ResetGear(_player.State, resetTargets: false);
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

    private static LevelLayout PickLevel(GameData data)
    {
        string? requested = Args.Value("--level");
        if (requested is not null)
        {
            return data.Levels.TryGetValue(requested, out LevelLayout? level)
                ? level
                : throw new InvalidOperationException($"No playable level '{requested}' (known: {string.Join(", ", data.Levels.Keys)})");
        }

        LadderLevelDef? first = data.Ladder.Levels.FirstOrDefault(l => data.Levels.ContainsKey(l.Id));
        return first is not null ? data.Levels[first.Id] : throw new InvalidOperationException("The ladder has no playable level.");
    }
}
