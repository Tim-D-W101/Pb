using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Game.Player;
using Pb.Sim;
using Pb.Sim.Players;

namespace Pb.Game.Ui;

/// <summary>
/// The touch screen's controls (Phase 6), on a layer of their own over the HUD: a move stick that appears where the left
/// thumb lands, looking by dragging anywhere on the right that isn't a button (or on FIRE as you hold it), and buttons by
/// the right thumb: on foot FIRE, AIM, JUMP, CROUCH, RELOAD and USE (which says what it'll do and shows only when there's
/// something to use); at the wheel GAS, BRAKE, HANDBRAKE, FIRE and GET OUT. Pause, the scoreboard and (playing with
/// others) the chat sit along the top. Up from the start on a phone or tablet, and on a computer from the first touch of
/// its screen; a mouse click, a key or a game pad puts them away again. The player reads the stick and the buttons from
/// here each tick, as the same commands a keyboard makes, and the look turns it as the fingers move. Sizes and places
/// (presentation.jsonc <c>touch</c>) are millimetres on the screen inside its safe area.
/// </summary>
public partial class TouchControls : CanvasLayer
{
    /// <summary>Over the HUD's layers and the cards over the round, under the pause menu (10).</summary>
    public const int ControlsLayer = 9;

    private enum Role
    {
        None,
        Stick,
        Look,
        Button,
    }

    /// <summary>One finger on the screen: what it's doing, where it went down (the stick's middle) and where it is.</summary>
    private sealed class Finger
    {
        public Role Role;
        public Spot? Button;
        public Vector2 Origin;
        public Vector2 At;
    }

    /// <summary>A button where it's drawn this frame.</summary>
    private sealed class Spot
    {
        public Spot(TouchButtonDef def) => Def = def;

        public TouchButtonDef Def { get; }

        public Vector2 Centre;
        public float Radius;
        public bool Shown;
        public string Label = "";
        public int FontSize = 16;
        public string SizedFor = "";
        public float SizedAt;
    }

    private TouchDef _def = null!;
    private GameSettings _settings = null!;
    private PlayerController _player = null!;
    private SimWorld _sim = null!;
    private Action _pause = null!;
    private Action? _chat;
    private Control _canvas = null!;
    private Font _font = null!;
    private Spot[] _foot = Array.Empty<Spot>();
    private Spot[] _drive = Array.Empty<Spot>();
    private Spot[] _top = Array.Empty<Spot>();
    private readonly Dictionary<int, Finger> _fingers = new();
    private Rect2 _safe;
    private float _mm = 4f;
    private bool _active;
    private bool _suspended;
    private bool _aimOn;
    private bool _crouchOn;
    private bool _driving;
    private bool _riding;
    private InputButtons _tapped;

    /// <summary>
    /// Up: on a phone from the start, on a computer since its screen was last touched (not clicked, typed on or played
    /// with a pad since).
    /// </summary>
    public bool Active => _active;

    /// <summary>
    /// Asked every frame (the pause menu stops the level, not this): whether something else has the screen, the pause
    /// menu, a card over the round, the chat being typed, a bot playing your slot. Then the controls hide and let go of
    /// every finger, and taps work the menus.
    /// </summary>
    public Func<bool>? Suspend { get; set; }

    /// <summary>Asked every frame: whether you're out (watching the rest of the round), when only the top row is left.</summary>
    public Func<bool>? Out { get; set; }

    /// <summary>Something else has the screen (<see cref="Suspend"/>).</summary>
    public bool Suspended => _suspended;

    /// <summary>You're out: only the top row is up.</summary>
    public bool OnlyTop { get; private set; }

    /// <summary>Your settings, as the controls read them (size, opacity, side, look speed).</summary>
    public GameSettings Settings => _settings;

    /// <summary>Playing by touch now: the player takes its moves, buttons and look from here.</summary>
    public bool InControl => _active && !_suspended && !OnlyTop;

    /// <summary>The stick (x right, y forward, at most 1 long); at the wheel x steers and y is GAS less BRAKE.</summary>
    public Vector2 Move
    {
        get
        {
            Vector2 stick = Stick(out _);
            if (!_driving)
            {
                return stick;
            }

            float throttle = (HeldDown("throttle") ? 1f : 0f) - (HeldDown("brake") ? 1f : 0f);
            return new Vector2(stick.X, throttle);
        }
    }

    /// <summary>AIM is on (on foot): the view narrows and the look slows.</summary>
    public bool Aiming => InControl && _aimOn && !_driving;

    /// <summary>SCORES is held: the scoreboard's up.</summary>
    public bool ScoresHeld => _active && !_suspended && HeldDown("scores");

    /// <summary>For the touch demo: where a button is up now and what it says (null: it isn't up).</summary>
    public (Vector2 At, string Label)? Button(string id)
    {
        Layout();
        foreach (Spot[] group in Groups())
        {
            foreach (Spot spot in group)
            {
                if (spot.Def.Id == id && spot.Shown)
                {
                    return (spot.Centre, spot.Label);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// For the touch demo: the buttons up now (and the stick's ring) that reach outside the safe area or the screen, or
    /// overlap each other; empty when every one is clear.
    /// </summary>
    public string Misplaced()
    {
        Layout();
        var wrong = new List<string>();
        var shown = new List<Spot>();
        foreach (Spot[] group in Groups())
        {
            foreach (Spot spot in group)
            {
                if (!spot.Shown)
                {
                    continue;
                }

                if (!Inside(spot.Centre, spot.Radius))
                {
                    wrong.Add($"{spot.Def.Id} outside the safe area");
                }

                foreach (Spot other in shown)
                {
                    if (other.Centre.DistanceTo(spot.Centre) < other.Radius + spot.Radius)
                    {
                        wrong.Add($"{spot.Def.Id} over {other.Def.Id}");
                    }
                }

                shown.Add(spot);
            }
        }

        if (!OnlyTop && !Inside(StickRest(), StickRadius))
        {
            wrong.Add("the stick outside the safe area");
        }

        return string.Join(", ", wrong);
    }

    private bool Inside(Vector2 centre, float radius) =>
        centre.X - radius >= _safe.Position.X - 0.5f && centre.X + radius <= _safe.End.X + 0.5f &&
        centre.Y - radius >= _safe.Position.Y - 0.5f && centre.Y + radius <= _safe.End.Y + 0.5f;

    /// <summary>For the touch demo: where the stick waits, and how far its ring reaches (interface pixels).</summary>
    public Vector2 StickPoint => StickRest();

    public float StickReach => StickRadius;

    /// <summary>For the touch demo: a place on the look side clear of every button.</summary>
    public Vector2 LookPoint
    {
        get
        {
            Layout();
            Vector2[] tries = { new(0.6f, 0.3f), new(0.55f, 0.5f), new(0.7f, 0.22f), new(0.62f, 0.62f), new(0.5f, 0.2f) };
            foreach (Vector2 t in tries)
            {
                Vector2 p = _safe.Position + _safe.Size * new Vector2(_settings.TouchLeftHanded ? 1f - t.X : t.X, t.Y);
                if (!InStickZone(p) && Clear(p))
                {
                    return p;
                }
            }

            return _safe.Position + _safe.Size * new Vector2(_settings.TouchLeftHanded ? 0.4f : 0.6f, 0.3f);
        }
    }

    private bool Clear(Vector2 p)
    {
        foreach (Spot[] group in Groups())
        {
            foreach (Spot spot in group)
            {
                if (spot.Shown && spot.Centre.DistanceTo(p) < spot.Radius * 1.6f)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>The bottom of the top row (interface pixels).</summary>
    public float TopRowBottom { get; private set; }

    /// <summary>Where the HUD's gear panel goes while the controls are up: under the top row, out of the buttons' way.</summary>
    public Vector2 HudCorner => new(_safe.Position.X + 3f * _mm, TopRowBottom + 2f * _mm);

    /// <param name="pause">Pause's button: opens the pause menu.</param>
    /// <param name="chat">Playing with others, the chat's button: starts typing (null: no chat, no button).</param>
    public void Build(TouchDef def, GameSettings settings, PlayerController player, SimWorld sim, Action pause, Action? chat)
    {
        Layer = ControlsLayer;
        // Paused, it still notices it's been suspended, so taps work the pause menu.
        ProcessMode = ProcessModeEnum.Always;
        _def = def;
        _settings = settings;
        _player = player;
        _sim = sim;
        _pause = pause;
        _chat = chat;
        _font = ThemeDB.FallbackFont;
        _foot = Spots(def.Buttons);
        _drive = Spots(def.Driving);
        _top = Spots(def.Top);
        _canvas = new Control { Name = "Canvas", MouseFilter = Control.MouseFilterEnum.Ignore };
        _canvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _canvas.Draw += DrawControls;
        AddChild(_canvas);
        _active = TouchUi.Phone;
        Visible = false;
    }

    private static Spot[] Spots(TouchButtonDef[] defs)
    {
        var spots = new Spot[defs.Length];
        for (int i = 0; i < defs.Length; i++)
        {
            spots[i] = new Spot(defs[i]) { Label = defs[i].Label };
        }

        return spots;
    }

    public override void _Ready() => Layout();

    public override void _ExitTree() => Input.EmulateMouseFromTouch = true;

    public override void _Notification(int what)
    {
        // The phone rang, or you switched away: nothing stays held.
        if (what == NotificationApplicationFocusOut || what == NotificationApplicationPaused)
        {
            ReleaseAll();
        }
    }

    /// <summary>Whether something else has the screen now, and whether you're out (letting go of every finger as it's suspended).</summary>
    private void Refresh()
    {
        bool suspended = Suspend?.Invoke() == true;
        if (suspended && !_suspended)
        {
            ReleaseAll();
        }

        _suspended = suspended;
        OnlyTop = Out?.Invoke() == true;
    }

    public override void _Input(InputEvent e)
    {
        Refresh();
        switch (e)
        {
            case InputEventScreenTouch touch:
                if (!_active)
                {
                    Activate(true);
                }

                if (_suspended)
                {
                    return; // the menus have it, as a click
                }

                if (touch.Pressed && !touch.Canceled)
                {
                    Down(touch.Index, touch.Position);
                }
                else
                {
                    Up(touch.Index);
                }

                GetViewport().SetInputAsHandled();
                break;
            case InputEventScreenDrag drag:
                if (_active && !_suspended)
                {
                    Dragged(drag.Index, drag.Position, drag.Relative);
                    GetViewport().SetInputAsHandled();
                }

                break;
            case InputEventMouseButton { Pressed: true } mouse:
                // A real mouse's click (not one made from a touch) puts the controls away.
                if (_active && mouse.Device != InputEvent.DeviceIdEmulation)
                {
                    Activate(false);
                }

                break;
            case InputEventKey { Pressed: true, Echo: false }:
                // On a phone its own keys (the volume) aren't a keyboard to play with.
                if (_active && !TouchUi.Phone)
                {
                    Activate(false);
                }

                break;
            case InputEventJoypadButton { Pressed: true }:
                if (_active)
                {
                    Activate(false);
                }

                break;
            case InputEventJoypadMotion motion:
                if (_active && Math.Abs(motion.AxisValue) > 0.5f)
                {
                    Activate(false);
                }

                break;
        }
    }

    public override void _Process(double delta)
    {
        Refresh();
        // Playing by touch, fingers are the controls' alone: made into mouse clicks too, the first one would fire (the
        // left button fires) and capture the mouse. Everywhere else a tap is a click, for the menus and cards.
        bool emulate = !InControl;
        if (Input.EmulateMouseFromTouch != emulate)
        {
            Input.EmulateMouseFromTouch = emulate;
        }

        Visible = _active && !_suspended;
        if (!Visible)
        {
            return;
        }

        Layout();
        _canvas.QueueRedraw();
    }

    /// <summary>
    /// The buttons the player holds this tick: those held down, CROUCH while it's on, sprint while the stick's pushed out
    /// past its ring, and any pressed since the last tick (a tap shorter than a tick still counts).
    /// </summary>
    public InputButtons TakeButtons()
    {
        InputButtons held = InputButtons.None;
        foreach (Finger f in _fingers.Values)
        {
            if (f.Role == Role.Button && f.Button is { } spot)
            {
                held |= ButtonFor(spot.Def.Id);
            }
        }

        Stick(out bool sprint);
        if (!_driving && !_riding)
        {
            if (sprint)
            {
                // Sprinting stands you up and lowers the marker, as it does from the keys.
                _crouchOn = false;
                _aimOn = false;
                held |= InputButtons.Sprint;
            }

            if (_crouchOn)
            {
                held |= InputButtons.Crouch;
            }
        }

        InputButtons buttons = held | _tapped;
        _tapped = InputButtons.None;
        return buttons;
    }

    /// <summary>What a button holds down in the command: at the wheel the handbrake is the jump button, as on the keys.</summary>
    private static InputButtons ButtonFor(string id) => id switch
    {
        "fire" => InputButtons.Fire,
        "jump" => InputButtons.Jump,
        "handbrake" => InputButtons.Jump,
        "reload" => InputButtons.Refill,
        "use" => InputButtons.Interact,
        _ => InputButtons.None,
    };

    private void Activate(bool on)
    {
        _active = on;
        ReleaseAll();
        if (on && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }

        Layout();
    }

    private void ReleaseAll()
    {
        _fingers.Clear();
        _tapped = InputButtons.None;
    }

    private void Down(int index, Vector2 at)
    {
        Layout();
        var finger = new Finger { At = at, Origin = at };
        if (Hit(at) is { } spot)
        {
            finger.Role = Role.Button;
            finger.Button = spot;
            Press(spot);
        }
        else if (OnlyTop)
        {
            finger.Role = Role.None;
        }
        else if (InStickZone(at))
        {
            if (!Has(Role.Stick))
            {
                finger.Role = Role.Stick;
                finger.Origin = StickOrigin(at);
            }
        }
        else if (!Has(Role.Look))
        {
            finger.Role = Role.Look;
        }

        _fingers[index] = finger;
    }

    private void Up(int index) => _fingers.Remove(index);

    private void Dragged(int index, Vector2 at, Vector2 relative)
    {
        if (!_fingers.TryGetValue(index, out Finger? finger))
        {
            return;
        }

        finger.At = at;
        if (finger.Role == Role.Look || finger.Role == Role.Button && finger.Button?.Def.Id == "fire")
        {
            Look(relative);
        }
    }

    private void Press(Spot spot)
    {
        switch (spot.Def.Id)
        {
            case "aim":
                _aimOn = !_aimOn;
                break;
            case "crouch":
                _crouchOn = !_crouchOn;
                break;
            case "pause":
                _pause();
                break;
            case "chat":
                _chat?.Invoke();
                break;
            default:
                _tapped |= ButtonFor(spot.Def.Id);
                break;
        }
    }

    /// <summary>A finger's move turns you: degrees for each centimetre, slower while aiming, at most a step's worth at once.</summary>
    private void Look(Vector2 relative)
    {
        if (!InControl || _mm <= 0f)
        {
            return;
        }

        float perCm = _settings.TouchLookDegPerCm * (Aiming ? _def.Aim.LookScale : 1f);
        Vector2 degrees = (relative / (_mm * 10f) * perCm).LimitLength(_def.MaxStep_deg);
        // Dragging right turns you right (yaw goes down), dragging up looks up.
        _player.Turn(-Mathf.DegToRad(degrees.X), -Mathf.DegToRad(degrees.Y));
    }

    /// <summary>The stick's push (x right, y forward), and whether it's out past its ring (sprinting).</summary>
    private Vector2 Stick(out bool sprint)
    {
        sprint = false;
        Finger? finger = With(Role.Stick);
        float radius = StickRadius;
        if (finger is null || radius <= 0f)
        {
            return Vector2.Zero;
        }

        Vector2 offset = (finger.At - finger.Origin) / radius;
        float length = offset.Length();
        sprint = length > _def.Stick.SprintBeyond;
        float dead = _def.Stick.Deadzone;
        if (length <= dead)
        {
            return Vector2.Zero;
        }

        float k = Math.Min(1f, (length - dead) / (1f - dead)) / length;
        return new Vector2(offset.X * k, -offset.Y * k);
    }

    private float StickRadius => _def.Stick.Radius_mm * _settings.TouchButtonScale * _mm;

    /// <summary>Where the stick goes for a thumb landing at <paramref name="at"/>: its ring kept inside the safe area.</summary>
    private Vector2 StickOrigin(Vector2 at)
    {
        float r = StickRadius;
        return new Vector2(Math.Clamp(at.X, _safe.Position.X + r, _safe.End.X - r), Math.Clamp(at.Y, _safe.Position.Y + r, _safe.End.Y - r));
    }

    /// <summary>Where the stick waits, drawn faintly, until a thumb lands.</summary>
    private Vector2 StickRest()
    {
        float x = _def.Stick.RestAt_mm[0] * _settings.TouchButtonScale * _mm;
        float y = _def.Stick.RestAt_mm[1] * _settings.TouchButtonScale * _mm;
        return new Vector2(_settings.TouchLeftHanded ? _safe.End.X - x : _safe.Position.X + x, _safe.End.Y - y);
    }

    private bool InStickZone(Vector2 at)
    {
        float edge = _def.Stick.Zone * _safe.Size.X;
        return _settings.TouchLeftHanded ? at.X > _safe.End.X - edge : at.X < _safe.Position.X + edge;
    }

    private bool Has(Role role) => With(role) is not null;

    private Finger? With(Role role)
    {
        foreach (Finger f in _fingers.Values)
        {
            if (f.Role == role)
            {
                return f;
            }
        }

        return null;
    }

    private bool HeldDown(string id)
    {
        foreach (Finger f in _fingers.Values)
        {
            if (f.Role == Role.Button && f.Button?.Def.Id == id)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The shown button nearest <paramref name="at"/> within a little more than its size (fingers are blunt).</summary>
    private Spot? Hit(Vector2 at)
    {
        Spot? best = null;
        float bestDistance = float.MaxValue;
        foreach (Spot[] group in Groups())
        {
            foreach (Spot spot in group)
            {
                float d = spot.Centre.DistanceTo(at);
                if (spot.Shown && d <= spot.Radius * 1.15f && d < bestDistance)
                {
                    best = spot;
                    bestDistance = d;
                }
            }
        }

        return best;
    }

    /// <summary>The groups up now: the top row, and on foot or at the wheel the buttons for it (none once you're out).</summary>
    private IEnumerable<Spot[]> Groups()
    {
        yield return _top;
        if (!OnlyTop)
        {
            yield return _driving ? _drive : _foot;
        }
    }

    /// <summary>Where everything is this frame (the safe area, the density, your size and side), and which buttons are up.</summary>
    private void Layout()
    {
        Window root = GetTree().Root;
        _safe = TouchUi.SafeArea(root);
        _mm = TouchUi.PxPerMm(root);
        float scale = _settings.TouchButtonScale;
        bool left = _settings.TouchLeftHanded;
        foreach (Spot spot in _foot)
        {
            PlaceBottom(spot, scale, left);
        }

        foreach (Spot spot in _drive)
        {
            PlaceBottom(spot, scale, left);
        }

        TopRowBottom = _safe.Position.Y;
        foreach (Spot spot in _top)
        {
            spot.Radius = spot.Def.Size_mm * 0.5f * scale * _mm;
            spot.Centre = new Vector2(_safe.Position.X + spot.Def.At_mm[0] * scale * _mm, _safe.Position.Y + spot.Def.At_mm[1] * scale * _mm);
            spot.Shown = spot.Def.Id != "chat" || _chat is not null;
            TopRowBottom = Math.Max(TopRowBottom, spot.Centre.Y + spot.Radius);
        }

        PlayerState you = _player.State;
        Pb.Sim.Vehicles.VehicleState? ride = _sim.Vehicles.Of(you);
        _driving = ride is not null && ride.Driver == you.Id;
        _riding = ride is not null && !_driving;
        bool slow = ride is not null && MathF.Abs(ride.Speed) <= ride.Params.ExitBelow;
        string? use = ride is not null ? (slow ? "GET OUT" : null) : UseVerb(you);
        foreach (Spot spot in _foot)
        {
            spot.Shown = spot.Def.Id switch
            {
                "use" => use is not null || HeldDown("use"),
                "jump" or "crouch" => ride is null,
                _ => true,
            };
            if (spot.Def.Id == "use" && use is not null)
            {
                spot.Label = use;
            }
        }

        foreach (Spot spot in _drive)
        {
            spot.Shown = spot.Def.Id != "use" || slow || HeldDown("use");
        }
    }

    private void PlaceBottom(Spot spot, float scale, bool left)
    {
        float x = spot.Def.At_mm[0] * scale * _mm;
        float y = spot.Def.At_mm[1] * scale * _mm;
        spot.Radius = spot.Def.Size_mm * 0.5f * scale * _mm;
        spot.Centre = new Vector2(left ? _safe.Position.X + x : _safe.End.X - x, _safe.End.Y - y);
    }

    /// <summary>What USE would do on foot, as its label (null: nothing in reach). The same reach the HUD's prompts use.</summary>
    private string? UseVerb(PlayerState you)
    {
        if (!you.Alive || !_sim.IsLive)
        {
            return null;
        }

        if (_sim.Vehicles.FreeSeatNear(you.Position).Vehicle >= 0)
        {
            return "GET IN";
        }

        if (you.OnLadder)
        {
            return null; // JUMP lets go
        }

        if (_sim.Ladders.FindGrab(you.Position, _player.Yaw, _sim.Config.Movement.Climbing, out bool fromTop) >= 0)
        {
            return fromTop ? "CLIMB DOWN" : "CLIMB";
        }

        int door = you.InteractDoor >= 0 ? you.InteractDoor : _sim.Doors.FindTarget(you.EyePosition, Pb.Sim.Core.ViewAngles.Forward(_player.Yaw, _player.Pitch));
        return door < 0 ? null : _sim.Doors.Target(door) < 0.5f ? "OPEN" : "SHUT";
    }

    private void DrawControls()
    {
        float alpha = _settings.TouchOpacity;
        if (!OnlyTop)
        {
            DrawStick(alpha);
            foreach (Spot spot in _driving ? _drive : _foot)
            {
                DrawSpot(spot, alpha);
            }
        }

        foreach (Spot spot in _top)
        {
            DrawSpot(spot, alpha);
        }
    }

    private void DrawStick(float alpha)
    {
        float r = StickRadius;
        Finger? finger = With(Role.Stick);
        Stick(out bool sprint);
        Vector2 middle = finger?.Origin ?? StickRest();
        Vector2 push = finger is null ? Vector2.Zero : (finger.At - finger.Origin).LimitLength(r);
        if (_driving)
        {
            push.Y = 0f; // at the wheel it only steers
        }

        float a = finger is null ? alpha * 0.5f : alpha;
        float width = Math.Max(1.5f, r * 0.05f);
        _canvas.DrawCircle(middle, r, new Color(0.06f, 0.06f, 0.07f, 0.3f * a));
        _canvas.DrawArc(middle, r, 0f, Mathf.Tau, 48, new Color(1f, 1f, 1f, 0.7f * a), width, antialiased: true);
        bool sprinting = sprint && !_driving && !_riding;
        if (sprinting)
        {
            _canvas.DrawArc(middle, r * _def.Stick.SprintBeyond, 0f, Mathf.Tau, 48, new Color(UiKit.Accent, 0.8f * a), width, antialiased: true);
        }

        Color knob = sprinting ? new Color(UiKit.Accent, 0.85f * a) : new Color(1f, 1f, 1f, 0.55f * a);
        _canvas.DrawCircle(middle + push, r * 0.42f, knob);
    }

    private void DrawSpot(Spot spot, float alpha)
    {
        if (!spot.Shown)
        {
            return;
        }

        bool lit = IsHeldSpot(spot) || spot.Def.Id == "aim" && _aimOn || spot.Def.Id == "crouch" && _crouchOn;
        Color fill = lit ? new Color(UiKit.Accent, 0.6f * alpha) : new Color(0.06f, 0.06f, 0.07f, 0.45f * alpha);
        _canvas.DrawCircle(spot.Centre, spot.Radius, fill);
        _canvas.DrawArc(spot.Centre, spot.Radius, 0f, Mathf.Tau, 48, new Color(1f, 1f, 1f, 0.8f * alpha), Math.Max(1.5f, spot.Radius * 0.06f), antialiased: true);
        FitLabel(spot);
        float width = spot.Radius * 1.8f;
        Vector2 size = _font.GetMultilineStringSize(spot.Label, HorizontalAlignment.Center, width, spot.FontSize);
        var at = new Vector2(spot.Centre.X - width * 0.5f, spot.Centre.Y - size.Y * 0.5f + _font.GetAscent(spot.FontSize));
        _canvas.DrawMultilineString(_font, at, spot.Label, HorizontalAlignment.Center, width, spot.FontSize, -1, new Color(1f, 1f, 1f, Math.Min(1f, 0.4f + alpha)));
    }

    /// <summary>Whether a finger holds this very spot (the foot's FIRE and the wheel's FIRE are different spots).</summary>
    private bool IsHeldSpot(Spot spot)
    {
        foreach (Finger f in _fingers.Values)
        {
            if (f.Role == Role.Button && f.Button == spot)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The label's size: as big as fits the button across (worked out again only when the label or size changes).</summary>
    private void FitLabel(Spot spot)
    {
        if (spot.SizedFor == spot.Label && Math.Abs(spot.SizedAt - spot.Radius) < 0.5f)
        {
            return;
        }

        int size = (int)Math.Clamp(spot.Radius * 0.42f, 8f, 48f);
        while (size > 8 && _font.GetMultilineStringSize(spot.Label, HorizontalAlignment.Center, -1, size).X > spot.Radius * 1.6f)
        {
            size--;
        }

        spot.FontSize = size;
        spot.SizedFor = spot.Label;
        spot.SizedAt = spot.Radius;
    }
}
