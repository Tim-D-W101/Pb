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
using Pb.Sim.Players;

namespace Pb.Game.Core;

/// <summary>
/// Composition root of the Phase 1 range scene: loads data, builds the sim and wires every
/// presentation system to it. Also hosts the sandbox hotkeys, data hot reload (F9) and two
/// scripted modes, selected by user args after "--":
///   --smoke-test[=ticks]  headless CI check: autopilot + stress mode, exit code 0/1
///   --demo                autopilot tour used for screenshots (with Godot's --write-movie)
///   --shots               camera tour of the range's viewpoints (screenshots with --write-movie); --views=…
/// </summary>
public partial class RangeMain : Node3D, ISimEventListener
{
    private GameData _data = null!;
    private PresentationDef _view = null!;
    private GameSettings _settings = null!;
    private SimWorld _sim = null!;
    private SimDriver _driver = null!;
    private RangeBuilder _world = null!;
    private PlayerController _player = null!;
    private BallRenderer _balls = null!;
    private SplatSystem _splats = null!;
    private PaintDrips _drips = null!;
    private ArcPreview _arc = null!;
    private Hud _hud = null!;
    private SmokeTest? _smoke;
    private DemoTour? _demo;
    private PauseMenu _pause = null!;
    private bool _ready;

    public override void _Ready()
    {
        _hud = GetNode<Hud>("Hud");
        _driver = GetNode<SimDriver>("SimDriver");
        _world = GetNode<RangeBuilder>("World");
        _player = GetNode<PlayerController>("Player");
        _balls = GetNode<BallRenderer>("Balls");
        _splats = GetNode<SplatSystem>("Splats");
        _arc = GetNode<ArcPreview>("ArcPreview");

        InputDef input;
        try
        {
            var source = new GodotDataSource();
            _data = GameData.Load(source);
            _view = Jsonc.Load<PresentationDef>(source, PresentationDef.File);
            input = Jsonc.Load<InputDef>(source, InputDef.File);
            InputSetup.Apply(input);
        }
        catch (DataException ex)
        {
            GD.PushError(ex.Message);
            _hud.ShowFatal(ex.Message);
            if (IsSmokeTest(out _))
            {
                GetTree().Quit(1);
            }

            return;
        }

        _settings = GameSettings.Load(_view);
        ApplyVsync();

        _sim = new SimWorld(_data.Config);
        _sim.LoadRange(_data.Range, _data.Stress);
        PlayerState state = _sim.AddPlayer(0, 0, _data.Range.SpawnPosition, _data.Range.SpawnYaw);
        Color teamColor = Color.FromHtml(_view.TeamColors[state.Team % _view.TeamColors.Length]);

        _world.Build(_data.Range, _data.Kit.Materials, _view);
        Atmosphere.ApplyLighting(GetNode<WorldEnvironment>("WorldEnvironment"), GetNode<DirectionalLight3D>("Sun"), _view.Lighting);
        ApplyGraphics(_view.Graphics.Find(_settings.GraphicsPreset));
        _player.Initialize(_sim, state, _view, _settings, teamColor);
        _balls.Initialize(_sim.Ballistics, _view, state.Id, _player.VisualMuzzlePosition, RenderBounds());
        _splats.Initialize(_view, (i, _, _) => _world.TargetNode(i) is { } target ? new SplatAnchor(target) : null);
        GetNode<ImpactFx>("ImpactFx").Initialize(_view);
        var dust = new FootDust { Name = "FootDust" };
        AddChild(dust);
        dust.Initialize(_sim, _view.FootDust, _view.GroundWind);
        _drips = new PaintDrips { Name = "PaintDrips" };
        AddChild(_drips);
        _drips.Initialize(_view);
        _arc.Initialize(_sim, state, _view);
        _arc.Enabled = _view.ArcPreview.EnabledOnStart;
        // Headless runs (CI) use Godot's dummy audio driver, which never retires finished
        // playbacks, so sound is only wired up when there is a real display.
        var audio = GetNode<AudioDirector>("Audio");
        bool headless = DisplayServer.GetName() == "headless";
        if (!headless)
        {
            audio.Initialize(state, _view);
        }
        _hud.Initialize(_sim, state, _driver, _settings, _view, () => (_splats.ActiveCount, _splats.Capacity), () => _arc.Summary);
        _settings.ApplyVolume();
        _pause = new PauseMenu { Name = "Pause" };
        AddChild(_pause);
        _pause.Build(_settings, _view, s => ApplyGraphics(_view.Graphics.Find(s.GraphicsPreset)), restart: null);

        _driver.Initialize(_sim);
        _driver.AddDriver(_player);
        _driver.AddListener(_balls);
        _driver.AddListener(_splats);
        _driver.AddListener(GetNode<ImpactFx>("ImpactFx"));
        _driver.AddListener(dust);
        _driver.AddListener(_drips);
        if (!headless)
        {
            _driver.AddListener(audio);
        }
        _driver.AddListener(_hud);
        _driver.AddListener(this);

        if (IsSmokeTest(out int ticks))
        {
            _smoke = new SmokeTest(this, _sim, _driver, _splats, ticks, ReloadData);
            _player.AutoPilot = new AutoPilot(_sim);
            _sim.Stress!.Enabled = true;
        }
        else if (OS.GetCmdlineUserArgs().Contains("--demo"))
        {
            _demo = new DemoTour(_sim, _player, _arc, _hud);
            _player.AutoPilot = _demo.Pilot;
        }
        else if (Args.Has("--shots"))
        {
            var tour = new ViewpointTour { Name = "ViewpointTour" };
            AddChild(tour);
            tour.Start(_data.Range.Viewpoints, _hud, _player.ViewModel, _view.Camera.FarClip_m);
        }
        else if (!headless)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }

        _ready = true;
    }

    public override void _Process(double delta)
    {
        if (!_ready)
        {
            return;
        }

        // Targets move as a pure function of time; draw them at the interpolated render time.
        double renderTime = _sim.Time - _sim.Dt + Engine.GetPhysicsInterpolationFraction() * _sim.Dt;
        _world.UpdateTargets(renderTime);
    }

    public void OnSimEvent(in SimEvent e)
    {
        if (e.Type == SimEventType.TargetHit && _world.Targets.Count > e.TargetId && e.TargetId >= 0)
        {
            _world.Targets[e.TargetId].Flash();
        }

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
            if (e.IsActionPressed("reload_data"))
            {
                GetTree().ReloadCurrentScene();
            }

            return;
        }

        if (e is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured && _player.AutoPilot is null)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (e.IsActionPressed("pause") && !_pause.Open)
        {
            _pause.Toggle();
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
        else if (e.IsActionPressed("debug_stress"))
        {
            _sim.Stress!.Enabled = !_sim.Stress.Enabled;
            _hud.Toast(_sim.Stress.Enabled ? $"Stress mode: {_sim.Stress.TargetLiveBalls} live balls" : "Stress mode off");
        }
        else if (e.IsActionPressed("debug_perf"))
        {
            _hud.ShowPerf = !_hud.ShowPerf;
        }
        else if (e.IsActionPressed("toggle_head_bob"))
        {
            _settings.HeadBob = !_settings.HeadBob;
            SaveAndToast(_settings.HeadBob ? "Head-bob on" : "Head-bob off");
        }
        else if (e.IsActionPressed("debug_reset_gear"))
        {
            _sim.ResetGear(_player.State, resetTargets: true);
            foreach (TargetView view in _world.Targets)
            {
                view.SetHits(0);
            }
        }
        else if (e.IsActionPressed("toggle_crosshair"))
        {
            _settings.Crosshair = !_settings.Crosshair;
            SaveAndToast(_settings.Crosshair ? "Crosshair on" : "Crosshair off");
        }
        else if (e.IsActionPressed("toggle_vsync"))
        {
            _settings.Vsync = !_settings.Vsync;
            ApplyVsync();
            SaveAndToast(_settings.Vsync ? "V-sync on" : "V-sync off");
        }
        else if (e.IsActionPressed("reload_data"))
        {
            ReloadData();
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
    }

    /// <summary>F9: re-read every data file and apply it without restarting.</summary>
    private void ReloadData()
    {
        try
        {
            var source = new GodotDataSource();
            GameData data = GameData.Load(source);
            PresentationDef view = Jsonc.Load<PresentationDef>(source, PresentationDef.File);
            InputSetup.Apply(Jsonc.Load<InputDef>(source, InputDef.File));

            bool needsRestart = data.Config.TickRate != _sim.Config.TickRate ||
                                data.Config.BallPoolCapacity != _sim.Ballistics.Pool.Capacity;
            _data = data;
            _view = view;
            _sim.ApplyConfig(data.Config);
            _sim.LoadRange(data.Range, data.Stress);
            _world.Build(data.Range, data.Kit.Materials, view);
            Atmosphere.ApplyLighting(GetNode<WorldEnvironment>("WorldEnvironment"), GetNode<DirectionalLight3D>("Sun"), view.Lighting);
            ApplyGraphics(view.Graphics.Find(_settings.GraphicsPreset));
            _splats.ClearAll();
            _drips.ClearAll();
            _player.ApplyMovementParams(data.Config.Movement);
            _balls.ApplyView(view);
            _splats.ApplyView(view);
            _arc.ApplyView(view);
            _hud.ApplyView(view);
            _hud.Toast(needsRestart ? "Data reloaded (tick rate / pool size need a restart)" : "Data reloaded", 3);
        }
        catch (DataException ex)
        {
            GD.PushWarning(ex.Message);
            _hud.Toast("Reload failed:\n" + ex.Message, 8);
        }
    }

    /// <summary>A graphics preset: environment, shadows, anti-aliasing and render scale, whether the old paint shows, and the weeds.</summary>
    private void ApplyGraphics(GraphicsPresetDef preset)
    {
        Atmosphere.ApplyPreset(GetNode<WorldEnvironment>("WorldEnvironment"), GetNode<DirectionalLight3D>("Sun"), GetViewport(), preset);
        Atmosphere.ApplyRenderScale(GetViewport(), _settings.RenderScale, _view.Graphics);
        foreach (OldPaint paint in _world.OldPaint)
        {
            paint.Visible = preset.OldPaint;
        }

        _world.Weeds?.ApplyPreset(preset);
    }

    private void SaveAndToast(string message)
    {
        _settings.Save();
        _hud.Toast(message);
    }

    private void ApplyVsync() =>
        DisplayServer.WindowSetVsyncMode(_settings.Vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);

    private Aabb RenderBounds()
    {
        Pb.Sim.Collision.Aabb b = _data.Range.Bounds;
        Vector3 min = b.Min.ToGodot() - new Vector3(5, 5, 5);
        Vector3 max = b.Max.ToGodot() + new Vector3(5, 5, 5);
        return new Aabb(min, max - min);
    }

    private static bool IsSmokeTest(out int ticks)
    {
        ticks = 900;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--smoke-test")
            {
                return true;
            }

            if (arg.StartsWith("--smoke-test=", StringComparison.Ordinal) && int.TryParse(arg["--smoke-test=".Length..], out int n))
            {
                ticks = n;
                return true;
            }
        }

        return false;
    }
}
