using System;
using System.Linq;
using System.Text;
using Godot;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Gear;
using Pb.Sim.Players;

namespace Pb.Game.Ui;

/// <summary>
/// The HUD: crosshair, gear panel (<see cref="GearPanel"/>: loader, pods, air in bar, fire mode, refill), perf overlay, help and
/// toasts; on the range the target hit tally, and in a round the match HUD (spec §6): the top bar with
/// the clock and everyone's in/out icon, the kill feed, subtitles for callouts, pickup prompts and the
/// hit marker. It reads sim state for display only.
/// </summary>
public partial class Hud : CanvasLayer, ISimEventListener
{
    private const string HelpText =
        "CONTROLS\n" +
        "WASD move · Mouse look · LMB fire · Shift sprint · Ctrl/C crouch · Alt walk · Space jump\n" +
        "Q/E lean · X or MMB swap shoulder · V slide (or crouch while sprinting)\n" +
        "R refill from pod · F open/shut a door (hold to ease it open) · B semi/ramping · Esc pause menu (settings, restart, quit)\n" +
        "Gamepad: sticks · RT fire · L3 sprint · B crouch · A jump · LB/RB lean · R3 swap · X refill (a door, facing one) · Y fire mode\n\n" +
        "DEBUG\n" +
        "F1 help · F2 arc preview · F3 stress mode (range) / bot debug (compound) · F4 perf overlay\n" +
        "F5 head-bob · F6 reset gear (range) · F7 crosshair · F8 vsync\n" +
        "F9 reload data files · F10 invert Y · F11 fullscreen · F12 graphics preset\n" +
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
    private GearPanel _gear = null!;
    private Label _warning = null!;
    private Label _help = null!;
    private Label _toast = null!;
    private Label _hits = null!;
    private Label _error = null!;
    private TopBar? _topBar;
    private KillFeed? _feed;
    private Subtitles? _subtitles;
    private Label _prompt = null!;
    private HudDef? _hudDef;
    private Color[] _teams = Array.Empty<Color>();
    private double _uiTimer;
    private double _toastTimer;
    private double _helpTimer = 12;

    private Label? _connection;

    public bool ShowPerf { get; set; } = true;

    /// <summary>A line more for the perf overlay (the training ground's: what the paint costs), or null.</summary>
    public Func<string>? PerfExtra { get; set; }

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
        _gear = new GearPanel { Name = "Gear" };
        root.AddChild(_gear);
        Place(_gear, Control.LayoutPreset.BottomRight, new Vector2(-GearPanel.PanelSize.X - 20f, -GearPanel.PanelSize.Y - 20f), GearPanel.PanelSize);

        _prompt = MakeLabel(root, 20, Control.LayoutPreset.Center, new Vector2(0, 46), HorizontalAlignment.Center);
        _prompt.Modulate = new Color(0.95f, 0.92f, 0.8f);
        _prompt.Visible = false;

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
        _gear.Initialize(player.Marker, Color.FromHtml(view.TeamColors[player.Team % view.TeamColors.Length]), view.Hud.LowPaint);
    }

    /// <summary>Switches to the match HUD: top bar (with <paramref name="clock"/>), kill feed, subtitles and pickup prompts.</summary>
    public void InitializeMatch(PresentationDef view, Func<string> clock)
    {
        _hudDef = view.Hud;
        _teams = view.TeamColors.Select(c => Color.FromHtml(c)).ToArray();
        _hits.Visible = false;
        Control root = GetNode<Control>("Root");
        _topBar = new TopBar { Name = "TopBar" };
        root.AddChild(_topBar);
        _topBar.Initialize(_sim, _player, _teams, _hudDef.IconSize_px, clock);

        _feed = new KillFeed { Name = "KillFeed" };
        root.AddChild(_feed);
        _feed.Configure(_hudDef.KillFeedLines, _hudDef.KillFeedTime_s);
        Place(_feed, Control.LayoutPreset.TopRight, new Vector2(-24, 64), Vector2.Zero);
        _feed.GrowHorizontal = Control.GrowDirection.Begin;

        _subtitles = new Subtitles { Name = "Subtitles" };
        root.AddChild(_subtitles);
        _subtitles.Configure(_hudDef.SubtitleTime_s);
        Place(_subtitles, Control.LayoutPreset.CenterBottom, new Vector2(-450, -330), new Vector2(900, 80));
        _subtitles.GrowVertical = Control.GrowDirection.Begin;
    }

    /// <summary>The objective's marker and status line, once the round (with an objective) has started.</summary>
    public void InitializeObjective(ObjectivesViewDef view)
    {
        if (_sim.Match?.Objective is not { } objective)
        {
            return;
        }

        var hud = new ObjectiveHud { Name = "Objective" };
        GetNode<Control>("Root").AddChild(hud);
        hud.Initialize(_sim, _player, objective, view);
    }

    /// <summary>Speedball: the countdown, the match's score (<paramref name="score"/>) and the hang under way.</summary>
    public void InitializeSpeedball(Func<(int Ours, int Theirs, int RaceTo, int Point)?> score)
    {
        if (_sim.Match?.Buzzers is null || _hudDef is null)
        {
            return;
        }

        var hud = new SpeedballHud { Name = "Speedball" };
        GetNode<Control>("Root").AddChild(hud);
        hud.Initialize(_sim, _player, _teams[_player.Team % _teams.Length], _hudDef.HornShow_s, score);
    }

    /// <summary>Playing with others: a warning that the connection's poor (null takes it away).</summary>
    public void Connection(string? warning)
    {
        if (_connection is null)
        {
            if (warning is null)
            {
                return;
            }

            _connection = MakeLabel(GetNode<Control>("Root"), 20, Control.LayoutPreset.CenterTop, new Vector2(0, 58), HorizontalAlignment.Center);
            _connection.Modulate = new Color(1f, 0.62f, 0.3f);
        }

        _connection.Visible = warning is not null;
        if (warning is not null && _connection.Text != warning)
        {
            _connection.Text = warning;
        }
    }

    /// <summary>A subtitle for something a player shouted (unless subtitles are off).</summary>
    public void Subtitle(string speaker, int team, string line)
    {
        if (_settings?.Subtitles != false)
        {
            _subtitles?.Show(speaker, _teams.Length > 0 ? _teams[team % _teams.Length] : Colors.White, line, _settings?.SubtitleSize ?? 1f);
        }
    }

    /// <summary>A subtitle for what the referee called (unless subtitles are off).</summary>
    public void RefereeSubtitle(string line)
    {
        if (_settings?.Subtitles != false)
        {
            _subtitles?.Show("Referee", Colors.White, line, _settings?.SubtitleSize ?? 1f);
        }
    }

    public void ApplyView(PresentationDef view)
    {
        _crosshairDef = view.Crosshair;
        ApplySettings();
    }

    /// <summary>
    /// What the settings change in the HUD: the crosshair's style, colour and size, and the HUD's own size (it's laid
    /// out at that scale across the whole screen, so the corners stay in the corners).
    /// </summary>
    public void ApplySettings()
    {
        if (_crosshairDef is { } c && _settings is { } s)
        {
            Color colour = Color.FromHtml(s.CrosshairColor.Length > 0 ? s.CrosshairColor : c.Color);
            _crosshair.Configure(c.Size_px * s.CrosshairSize, c.Gap_px * s.CrosshairSize, c.Thickness_px * Mathf.Sqrt(s.CrosshairSize), colour, s.CrosshairStyle);
        }

        _scaled = -1f;
    }

    private CrosshairDef? _crosshairDef;
    private float _scaled = -1f;
    private Vector2 _scaledFor;

    /// <summary>Lays the HUD out at the settings' scale whenever it or the window's size changes.</summary>
    private void ApplyScale()
    {
        float scale = _settings?.HudScale ?? 1f;
        Vector2 screen = GetViewport().GetVisibleRect().Size;
        if (Mathf.IsEqualApprox(scale, _scaled) && screen == _scaledFor)
        {
            return;
        }

        _scaled = scale;
        _scaledFor = screen;
        var root = GetNode<Control>("Root");
        root.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        root.Position = Vector2.Zero;
        root.Scale = new Vector2(scale, scale);
        root.Size = screen / scale;
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
        if (e.Type == SimEventType.PlayerEliminated && _feed is not null)
        {
            Eliminated(e);
        }

        if (e.Type is SimEventType.CaseTaken or SimEventType.CaseDropped or SimEventType.CaseExtracted or SimEventType.HoldChanged)
        {
            ObjectiveToast(e);
            return;
        }

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
            case SimEventType.PickupTaken:
                Toast((PickupKind)e.Extra == PickupKind.Air ? "Air tank refilled" : "Picked up a full pod");
                break;
        }
    }

    /// <summary>What just happened to the objective, from your side's point of view.</summary>
    private void ObjectiveToast(in SimEvent e)
    {
        string who = _sim.FindPlayer(e.PlayerId)?.Name ?? "Someone";
        string place = _sim.Match?.Objective?.Room?.Name ?? "room";
        string room = ModeText.The(place);
        string? line = e.Type switch
        {
            SimEventType.CaseTaken => e.PlayerId == _player.Id ? "You have the case: get it to a way out (you can't sprint with it)" : $"{who} has the case",
            SimEventType.CaseDropped => e.PlayerId == _player.Id ? "You're out: the case is down" : $"{who} is out: the case is down",
            SimEventType.CaseExtracted => e.PlayerId == _player.Id ? "You got the case out!" : $"{who} got the case out!",
            SimEventType.HoldChanged => (Pb.Sim.Match.HoldStatus)e.Extra switch
            {
                Pb.Sim.Match.HoldStatus.Ours => $"You hold {room}",
                Pb.Sim.Match.HoldStatus.Contested => $"{ModeText.TheCapital(place)} is contested",
                Pb.Sim.Match.HoldStatus.Theirs => $"They're in {room}",
                _ => null,
            },
            _ => null,
        };
        if (line is not null)
        {
            Toast(line, 3.0);
        }
    }

    /// <summary>A kill feed line, and the hit marker if it was your ball.</summary>
    private void Eliminated(in SimEvent e)
    {
        PlayerState? victim = _sim.FindPlayer(e.TargetId);
        PlayerState? shooter = _sim.FindPlayer(e.PlayerId);
        if (victim is null)
        {
            return;
        }

        Color Of(PlayerState p) => _teams[p.Team % _teams.Length];
        if (e.Extra < 0)
        {
            // Playing with others: someone who left mid-round is out.
            _feed!.Add("", Colors.White, victim.Name, Of(victim), "left the game");
            return;
        }

        string part = SpectatorView.PartName((Pb.Sim.Collision.HitboxPart)e.Extra);
        string distance = shooter is null ? "" : $" · {System.Numerics.Vector3.Distance(shooter.EyePosition, victim.EyePosition):0} m";
        _feed!.Add(shooter?.Name ?? "Stray ball", shooter is null ? Colors.White : Of(shooter), victim.Name, Of(victim), part + distance);
        if (shooter == _player && _settings.HitMarker)
        {
            _crosshair.Flash(_hudDef!.HitMarkerTime_s, Color.FromHtml(_hudDef.HitMarkerColor));
        }
    }

    public override void _Process(double delta)
    {
        if (_sim is null)
        {
            return;
        }

        _crosshair.Visible = _settings.Crosshair;
        ApplyScale();
        UpdatePrompt();
        if (_toastTimer > 0 && (_toastTimer -= delta) <= 0)
        {
            _toast.Visible = false;
        }

        if (_helpTimer > 0 && (_helpTimer -= delta) <= 0)
        {
            _help.Visible = false;
        }

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

    /// <summary>The warning over the gear panel: an empty tank or loader, low air, or why you can't fire.</summary>
    private void UpdateGearText()
    {
        Marker m = _player.Marker;

        // In a round there's no gear reset: you find pickups instead.
        bool round = _hudDef is not null;
        _warning.Text = !m.Air.CanFire ? (round ? "TANK EMPTY - find an air tank" : "TANK EMPTY - F6 to reset gear")
            : m.Paint.Loader == 0 ? (m.Paint.PodsRemaining > 0 ? "LOADER EMPTY - press R to refill"
                : round ? "OUT OF PAINT - find a pod" : "OUT OF PAINT - F6 to reset gear")
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

        if (PerfExtra?.Invoke() is { Length: > 0 } extra)
        {
            _text.Append('\n').Append(extra);
        }

        _perf.Text = _text.ToString();
    }

    /// <summary>
    /// The door you're facing within reach (what interact would do to it), else the nearest pickup within reach: what
    /// it is, how far, and whether you've room for it.
    /// </summary>
    private void UpdatePrompt()
    {
        Pb.Sim.Match.PickupSet pickups = _sim.Pickups;
        if (_hudDef is null || !_player.Alive || !_sim.IsLive)
        {
            _prompt.Visible = false;
            return;
        }

        // On a ladder, how to climb it and let go; in reach of one, how to get on.
        if (_player.OnLadder)
        {
            _prompt.Visible = true;
            _prompt.Text = $"{InputSetup.KeyName("move_forward")} / {InputSetup.KeyName("move_back")} · climb up or down · {InputSetup.KeyName("jump")} · let go";
            _prompt.Modulate = new Color(0.95f, 0.92f, 0.8f);
            return;
        }

        int ladder = _sim.Ladders.FindGrab(_player.Position, _player.Yaw, _sim.Config.Movement.Climbing, out bool fromTop);
        if (ladder >= 0)
        {
            _prompt.Visible = true;
            _prompt.Text = $"{InputSetup.KeyName("interact")} · {(fromTop ? "climb down the ladder" : "climb the ladder")}";
            _prompt.Modulate = new Color(0.95f, 0.92f, 0.8f);
            return;
        }

        int door = _player.InteractDoor >= 0 ? _player.InteractDoor
            : _sim.Doors.FindTarget(_player.EyePosition, Pb.Sim.Core.ViewAngles.Forward(_player.Yaw, _player.Pitch));
        if (door >= 0)
        {
            bool shut = _sim.Doors.Target(door) < 0.5f;
            _prompt.Visible = true;
            _prompt.Text = $"{InputSetup.KeyName("interact")} · {(shut ? "open the door (hold to ease it open)" : "shut the door")}";
            _prompt.Modulate = new Color(0.95f, 0.92f, 0.8f);
            return;
        }

        // At the other side's buzzer: how to hang it.
        if (_sim.Match?.Buzzers is { HungSide: < 0 } buzzers)
        {
            for (int side = 0; side < buzzers.Count; side++)
            {
                if (buzzers.InReach(_player, side))
                {
                    int hanger = buzzers.Hanger(side);
                    _prompt.Visible = true;
                    _prompt.Text = hanger == _player.Id ? $"Keep holding {InputSetup.KeyName("interact")} · hanging the buzzer"
                        : hanger >= 0 ? "Someone's already hanging the buzzer"
                        : $"Hold {InputSetup.KeyName("interact")} · hang their buzzer";
                    _prompt.Modulate = new Color(0.95f, 0.92f, 0.8f);
                    return;
                }
            }
        }

        if (!pickups.Active)
        {
            _prompt.Visible = false;
            return;
        }

        int best = -1;
        float bestDistance = _hudDef.PickupPromptRange_m;
        for (int i = 0; i < pickups.Items.Count; i++)
        {
            System.Numerics.Vector3 d = pickups.Items[i].Position - _player.Position;
            float flat = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
            if (!pickups.IsTaken(i) && MathF.Abs(d.Y) < 1.5f && flat <= bestDistance)
            {
                best = i;
                bestDistance = flat;
            }
        }

        _prompt.Visible = best >= 0;
        if (best < 0)
        {
            return;
        }

        bool air = pickups.Items[best].Kind == PickupKind.Air;
        bool room = air ? _player.Marker.Air.FillFraction < _sim.Config.Rules.AirPickupBelow : HasEmptyPod();
        string what = air ? "Air tank" : "Paint pod";
        _prompt.Text = room ? $"{what} · {bestDistance:0.0} m · walk over it" : $"{what} · {(air ? "your tank's still full" : "no empty pod slot")}";
        _prompt.Modulate = room ? new Color(0.95f, 0.92f, 0.8f) : new Color(0.7f, 0.7f, 0.68f);
    }

    private bool HasEmptyPod()
    {
        foreach (int pod in _player.Marker.Paint.Pods)
        {
            if (pod == 0)
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateHitsText()
    {
        if (_hudDef is not null)
        {
            return;
        }

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
}
