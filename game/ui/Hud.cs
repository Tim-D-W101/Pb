using System;
using System.Text;
using Godot;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Core;
using Pb.Sim.Events;
using Pb.Sim.Gear;
using Pb.Sim.Players;

namespace Pb.Game.Ui;

/// <summary>
/// Phase 1 HUD: crosshair, gear panel (loader, pods, air in bar, fire mode, refill), perf overlay,
/// help, target hit tally and toasts. It reads sim state for display only.
/// </summary>
public partial class Hud : CanvasLayer, ISimEventListener
{
    private const string HelpText =
        "CONTROLS\n" +
        "WASD move · Mouse look · LMB fire · Shift sprint · Ctrl/C crouch · Alt walk\n" +
        "R refill from pod · B semi/ramping · Esc release mouse (click to capture)\n" +
        "Gamepad: sticks · RT fire · L3 sprint · B crouch · X refill · Y fire mode\n\n" +
        "SANDBOX\n" +
        "F1 help · F2 arc preview · F3 stress mode (1,000 balls) · F4 perf overlay\n" +
        "F5 head-bob · F6 reset gear + hit counters · F7 crosshair · F8 vsync\n" +
        "F9 reload data files · F10 invert Y · F11 fullscreen\n" +
        "[ ] field of view · - = mouse sensitivity";

    private readonly StringBuilder _text = new();
    private SimWorld _sim = null!;
    private PlayerState _player = null!;
    private SimDriver _driver = null!;
    private Func<(int Count, int Capacity)> _splats = () => (0, 0);
    private Func<string> _arcSummary = () => string.Empty;
    private Label _arc = null!;
    private GameSettings _settings = null!;
    private CrosshairControl _crosshair = null!;
    private Label _perf = null!;
    private Label _gear = null!;
    private Label _warning = null!;
    private Label _help = null!;
    private Label _toast = null!;
    private Label _hits = null!;
    private ProgressBar _air = null!;
    private ProgressBar _refill = null!;
    private Label _error = null!;
    private double _uiTimer;
    private double _toastTimer;
    private double _helpTimer = 12;

    public bool ShowPerf { get; set; } = true;

    public bool ShowHelp
    {
        get => _help.Visible;
        set
        {
            _help.Visible = value;
            _helpTimer = 0;
        }
    }

    public override void _Ready()
    {
        var root = new Control { Name = "Root", MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        _crosshair = new CrosshairControl { MouseFilter = Control.MouseFilterEnum.Ignore };
        _crosshair.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_crosshair);

        _perf = MakeLabel(root, 18, Control.LayoutPreset.TopLeft, new Vector2(16, 12));
        _hits = MakeLabel(root, 18, Control.LayoutPreset.TopRight, new Vector2(-16, 12), HorizontalAlignment.Right);
        _arc = MakeLabel(root, 20, Control.LayoutPreset.CenterTop, new Vector2(0, 12), HorizontalAlignment.Center);
        _arc.Modulate = new Color(1f, 0.95f, 0.55f);
        _help = MakeLabel(root, 18, Control.LayoutPreset.CenterLeft, new Vector2(24, -140));
        _help.Text = HelpText;
        _toast = MakeLabel(root, 24, Control.LayoutPreset.CenterBottom, new Vector2(0, -170), HorizontalAlignment.Center);
        _warning = MakeLabel(root, 22, Control.LayoutPreset.CenterBottom, new Vector2(0, -210), HorizontalAlignment.Center);
        _warning.Modulate = new Color(1f, 0.55f, 0.35f);
        _gear = MakeLabel(root, 22, Control.LayoutPreset.BottomRight, new Vector2(-24, -124), HorizontalAlignment.Right);

        _air = MakeBar(root, new Vector2(-284, -46), new Color(0.35f, 0.8f, 0.45f));
        _refill = MakeBar(root, new Vector2(-284, -24), new Color(0.95f, 0.8f, 0.2f));
        _refill.Visible = false;

        _error = MakeLabel(root, 20, Control.LayoutPreset.Center, new Vector2(-560, -200));
        _error.Modulate = new Color(1f, 0.5f, 0.45f);
        _error.CustomMinimumSize = new Vector2(1120, 0);
        _error.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _error.Visible = false;
    }

    public void Initialize(SimWorld sim, PlayerState player, SimDriver driver, GameSettings settings, PresentationDef view,
        Func<(int Count, int Capacity)> splats, Func<string> arcSummary)
    {
        _arcSummary = arcSummary;
        _sim = sim;
        _player = player;
        _driver = driver;
        _settings = settings;
        _splats = splats;
        ApplyView(view);
    }

    public void ApplyView(PresentationDef view)
    {
        _crosshair.Configure(view.Crosshair.Size_px, view.Crosshair.Gap_px, view.Crosshair.Thickness_px, Color.FromHtml(view.Crosshair.Color));
    }

    /// <summary>Full-screen message shown instead of the game when data can't be loaded.</summary>
    public void ShowFatal(string message)
    {
        _error.Text = "Could not start: data problem\n\n" + message + "\n\nFix the file and restart (or press F9 once the game is running).";
        _error.Visible = true;
        _help.Visible = false;
    }

    public void Toast(string message, double seconds = 2.0)
    {
        _toast.Text = message;
        _toastTimer = seconds;
        _toast.Visible = true;
    }

    public void OnSimEvent(in SimEvent e)
    {
        if (e.PlayerId != _player?.Id)
        {
            return;
        }

        switch (e.Type)
        {
            case SimEventType.FireModeChanged:
                Toast((FireMode)e.Extra == FireMode.Ramping ? "Fire mode: RAMPING" : "Fire mode: SEMI");
                break;
            case SimEventType.RefillDenied:
                Toast(_player.Marker.Paint.LoaderFull ? "Loader already full" : "No paint left in pods");
                break;
            case SimEventType.RefillCancelled:
                Toast("Refill cancelled");
                break;
            case SimEventType.GearReset:
                Toast("Gear reset: full loader, pods and tank");
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (_sim is null)
        {
            return;
        }

        _crosshair.Visible = _settings.Crosshair;
        if (_toastTimer > 0 && (_toastTimer -= delta) <= 0)
        {
            _toast.Visible = false;
        }

        if (_helpTimer > 0 && (_helpTimer -= delta) <= 0)
        {
            _help.Visible = false;
        }

        UpdateBars();
        _arc.Text = _arcSummary();
        _uiTimer -= delta;
        if (_uiTimer > 0)
        {
            return;
        }

        _uiTimer = 0.1;
        UpdateGearText();
        UpdatePerfText();
        UpdateHitsText();
    }

    private void UpdateBars()
    {
        AirTank air = _player.Marker.Air;
        _air.MaxValue = air.Params.FillPressure / Units.BarToPascals;
        _air.Value = air.Pressure / Units.BarToPascals;
        Color fill = air.BelowRegulator ? new Color(0.95f, 0.3f, 0.25f)
            : air.Pressure < air.Params.LowWarningPressure ? new Color(0.95f, 0.7f, 0.2f)
            : new Color(0.35f, 0.8f, 0.45f);
        ((StyleBoxFlat)_air.GetThemeStylebox("fill")).BgColor = fill;

        Refill refill = _player.Marker.Refill;
        _refill.Visible = refill.Active;
        _refill.Value = refill.Progress(_player.Marker.Paint.Params) * 100.0;
    }

    private void UpdateGearText()
    {
        Marker m = _player.Marker;
        ReadOnlySpan<int> pods = m.Paint.Pods;
        _text.Clear();
        _text.Append(m.Fire.Mode == FireMode.Ramping ? (m.Fire.IsRamping ? "RAMPING >>" : "RAMPING") : "SEMI").Append('\n');
        _text.Append("LOADER ").Append(m.Paint.Loader).Append(" / ").Append(m.Paint.Params.Capacity).Append('\n');
        _text.Append("PODS ");
        for (int i = 0; i < pods.Length; i++)
        {
            _text.Append(i == 0 ? "" : " · ").Append(pods[i]);
        }

        _text.Append('\n');
        _text.Append("AIR ").Append((m.Air.Pressure / Units.BarToPascals).ToString("0")).Append(" bar");
        if (m.Refill.Active)
        {
            _text.Append("\nREFILLING...");
        }

        _gear.Text = _text.ToString();

        _warning.Text = !m.Air.CanFire ? "TANK EMPTY - F6 to reset gear"
            : m.Paint.Loader == 0 ? (m.Paint.PodsRemaining > 0 ? "LOADER EMPTY - press R to refill" : "OUT OF PAINT - F6 to reset gear")
            : m.Air.BelowRegulator ? "LOW AIR - velocity dropping"
            : _player.Sprinting ? "sprinting (can't fire)"
            : string.Empty;
    }

    private void UpdatePerfText()
    {
        _perf.Visible = ShowPerf;
        if (!ShowPerf)
        {
            return;
        }

        (int splats, int splatCap) = _splats();
        double fps = Performance.GetMonitor(Performance.Monitor.TimeFps);
        double frameMs = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        _text.Clear();
        _text.Append("FPS ").Append(fps.ToString("0")).Append("  (process ").Append(frameMs.ToString("0.0")).Append(" ms)\n");
        _text.Append("sim ").Append(_driver.AverageStepMs.ToString("0.000")).Append(" ms/tick @ ")
            .Append(_sim.Config.TickRate.ToString("0")).Append(" Hz\n");
        _text.Append("balls ").Append(_sim.Ballistics.Pool.Count).Append(" / ").Append(_sim.Ballistics.Pool.Capacity).Append('\n');
        _text.Append("splats ").Append(splats).Append(" / ").Append(splatCap).Append('\n');
        _text.Append("draw calls ").Append(Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame).ToString("0")).Append('\n');
        _text.Append("FOV ").Append(_settings.FovDeg.ToString("0")).Append("°  vsync ").Append(_settings.Vsync ? "on" : "off");
        if (_sim.Stress?.Enabled == true)
        {
            _text.Append("\nSTRESS MODE: ").Append(_sim.Stress.TargetLiveBalls).Append(" balls");
        }

        _perf.Text = _text.ToString();
    }

    private void UpdateHitsText()
    {
        _text.Clear();
        _text.Append("HITS\n");
        for (int i = 0; i < _sim.Targets.Count; i++)
        {
            _text.Append(_sim.Targets[i].Label).Append("  ").Append(_sim.Targets.HitCount(i)).Append('\n');
        }

        _hits.Text = _text.ToString();
    }

    private static Label MakeLabel(Control parent, int size, Control.LayoutPreset anchor, Vector2 offset,
        HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = align,
            LabelSettings = new LabelSettings { FontSize = size, OutlineSize = 6, OutlineColor = new Color(0, 0, 0, 0.85f) },
        };
        parent.AddChild(label);
        Place(label, anchor, offset, Vector2.Zero);
        label.GrowHorizontal = align switch
        {
            HorizontalAlignment.Right => Control.GrowDirection.Begin,
            HorizontalAlignment.Center => Control.GrowDirection.Both,
            _ => Control.GrowDirection.End,
        };
        label.GrowVertical = anchor is Control.LayoutPreset.BottomRight or Control.LayoutPreset.CenterBottom or Control.LayoutPreset.BottomLeft
            ? Control.GrowDirection.Begin
            : Control.GrowDirection.End;
        return label;
    }

    /// <summary>
    /// Anchors a control to a screen point and offsets it from there. Anchors only (keepOffsets) —
    /// the default preset call would preserve the control's current top-left position instead.
    /// </summary>
    private static void Place(Control control, Control.LayoutPreset anchor, Vector2 offset, Vector2 size)
    {
        control.SetAnchorsPreset(anchor, keepOffsets: true);
        control.OffsetLeft = offset.X;
        control.OffsetTop = offset.Y;
        control.OffsetRight = offset.X + size.X;
        control.OffsetBottom = offset.Y + size.Y;
    }

    private static ProgressBar MakeBar(Control parent, Vector2 offset, Color fill)
    {
        var bar = new ProgressBar
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(260, 14),
        };
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = fill });
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.45f) });
        parent.AddChild(bar);
        Place(bar, Control.LayoutPreset.BottomRight, offset, bar.CustomMinimumSize);
        return bar;
    }
}
