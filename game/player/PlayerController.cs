using System;
using Godot;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Core;
using Pb.Sim.Players;

namespace Pb.Game.Player;

/// <summary>
/// First-person controller for the local player: a <see cref="PawnBody"/> driven by devices (or an
/// <see cref="ICommandSource"/>), with the camera and viewmodel. The camera turns at render rate for
/// responsiveness, and its position and lean roll are interpolated between ticks, so 120 Hz physics
/// looks smooth at 144 Hz.
/// </summary>
public partial class PlayerController : PawnBody, IPlayerDriver
{
    private PresentationDef _view = null!;
    private GameSettings _settings = null!;
    private Node3D _head = null!;
    private float _yaw;
    private float _pitch;
    private Vector3 _previousEye;
    private Vector3 _currentEye;
    private float _previousRoll;
    private float _currentRoll;
    private float _bobPhase;

    public Camera3D Camera { get; private set; } = null!;

    public ViewModel ViewModel { get; private set; } = null!;

    /// <summary>When set, drives the player instead of devices.</summary>
    public ICommandSource? AutoPilot { get; set; }

    public float Yaw => _yaw;

    public float Pitch => _pitch;

    public void Initialize(SimWorld sim, PlayerState state, PresentationDef view, GameSettings settings, Color teamColor)
    {
        InitializeBody(sim, state);
        _view = view;
        _settings = settings;
        _yaw = state.Yaw;
        _pitch = state.Pitch;

        // The head is top-level so it can be interpolated independently of the body's tick steps.
        _head = new Node3D { Name = "Head", TopLevel = true };
        AddChild(_head);
        Camera = new Camera3D { Name = "Camera", Current = true, Near = view.Camera.NearClip_m, Far = view.Camera.FarClip_m };
        _head.AddChild(Camera);
        ViewModel = new ViewModel { Name = "ViewModel" };
        Camera.AddChild(ViewModel);
        ViewModel.Build(view.ViewModel, teamColor);

        _currentEye = _previousEye = state.EyePosition.ToGodot();
        _currentRoll = _previousRoll = state.LeanRoll;
        ApplyCamera(0f, 1f);
    }

    public override void Teleport(System.Numerics.Vector3 position, float yaw)
    {
        base.Teleport(position, yaw);
        _yaw = yaw;
        _pitch = 0f;
        _currentEye = _previousEye = State.EyePosition.ToGodot();
    }

    public InputCommand Step(int tick, float dt)
    {
        InputCommand cmd = AutoPilot is not null ? AutoPilot.Next(tick, State) : SampleDevices(tick);
        if (AutoPilot is not null)
        {
            _yaw = cmd.Yaw;
            _pitch = cmd.Pitch;
        }

        ApplyCommand(cmd, dt);

        _previousEye = _currentEye;
        _currentEye = State.EyePosition.ToGodot();
        _previousRoll = _currentRoll;
        _currentRoll = State.LeanRoll;
        return cmd;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (AutoPilot is null && e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            float sensitivity = _settings.MouseSensitivityDegPerCount * Units.DegreesToRadians;
            _yaw -= motion.Relative.X * sensitivity;
            _pitch -= motion.Relative.Y * sensitivity * (_settings.InvertY ? -1f : 1f);
            _pitch = Math.Clamp(_pitch, -Move.MaxPitch, Move.MaxPitch);
        }
    }

    public override void _Process(double delta)
    {
        if (State is null)
        {
            return;
        }

        if (AutoPilot is null)
        {
            Vector2 stick = Input.GetVector("look_left", "look_right", "look_up", "look_down");
            float magnitude = stick.Length();
            if (magnitude > 0f)
            {
                float curved = Mathf.Pow(Mathf.Min(magnitude, 1f), _view.Look.StickExponent);
                Vector2 look = stick / magnitude * curved * _view.Look.StickSpeed_degps * Units.DegreesToRadians * (float)delta;
                _yaw -= look.X;
                _pitch -= look.Y * (_settings.InvertY ? -1f : 1f);
                _pitch = Math.Clamp(_pitch, -Move.MaxPitch, Move.MaxPitch);
            }
        }

        float speed = State.HorizontalSpeed;
        if (_settings.HeadBob && IsOnFloor() && speed > 0.5f && State.Stance != Stance.Sliding)
        {
            _bobPhase += speed * (float)delta / _view.Camera.HeadBobStride_m * Mathf.Tau;
        }
        else
        {
            _bobPhase = Mathf.MoveToward(_bobPhase % Mathf.Tau, 0f, (float)delta * 4f);
        }

        ViewModel.Side = State.Shoulder;
        ViewModel.RefillProgress = State.Marker.Refill.Active ? State.Marker.Refill.Progress(State.Marker.Paint.Params) : -1f;
        int capacity = State.Marker.Paint.Params.Capacity;
        ViewModel.LoaderFill = capacity > 0 ? (float)State.Marker.Paint.Loader / capacity : 0f;
        ApplyCamera((float)Engine.GetPhysicsInterpolationFraction(), Mathf.Min(1f, speed / Mathf.Max(0.1f, Move.RunSpeed)));
    }

    /// <summary>World position where the drawn barrel tip appears (for the ball's visual blend).</summary>
    public Vector3 VisualMuzzlePosition() => ViewModel.ApparentMuzzle(Camera);

    private void ApplyCamera(float alpha, float bobWeight)
    {
        Vector3 eye = _previousEye.Lerp(_currentEye, alpha);
        float bob = _settings.HeadBob ? Mathf.Sin(_bobPhase) * _view.Camera.HeadBobAmplitude_m * bobWeight : 0f;
        _head.GlobalPosition = eye + new Vector3(0f, bob, 0f);
        // Leaning right rolls the view clockwise (negative about the view axis), by part of the body's roll.
        float roll = Mathf.Lerp(_previousRoll, _currentRoll, alpha) * _view.Camera.LeanRoll;
        _head.Rotation = new Vector3(_pitch, _yaw, -roll);

        // Settings store horizontal FOV at 16:9; Godot's camera FOV is vertical. Wider screens gain width (Hor+).
        float horizontal = Mathf.DegToRad(_settings.FovDeg);
        Camera.Fov = Mathf.RadToDeg(2f * Mathf.Atan(Mathf.Tan(horizontal * 0.5f) * 9f / 16f));
    }

    private InputCommand SampleDevices(int tick)
    {
        bool mouseCaptured = Input.MouseMode == Input.MouseModeEnum.Captured;
        bool padConnected = Input.GetConnectedJoypads().Count > 0;
        bool acceptInput = mouseCaptured || padConnected;

        Vector2 move = acceptInput ? Input.GetVector("move_left", "move_right", "move_back", "move_forward") : Vector2.Zero;
        InputButtons buttons = InputButtons.None;
        if (acceptInput)
        {
            foreach ((string action, InputButtons button) in Bindings)
            {
                if (Input.IsActionPressed(action))
                {
                    buttons |= button;
                }
            }
        }

        return new InputCommand
        {
            Tick = tick,
            Move = new System.Numerics.Vector2(move.X, move.Y),
            Yaw = _yaw,
            Pitch = _pitch,
            Buttons = buttons,
        };
    }

    /// <summary>Input actions (input.jsonc) and the command buttons they hold down.</summary>
    private static readonly (string Action, InputButtons Button)[] Bindings =
    {
        ("fire", InputButtons.Fire),
        ("sprint", InputButtons.Sprint),
        ("crouch", InputButtons.Crouch),
        ("walk", InputButtons.Walk),
        ("refill", InputButtons.Refill),
        ("fire_mode", InputButtons.ToggleFireMode),
        ("lean_left", InputButtons.LeanLeft),
        ("lean_right", InputButtons.LeanRight),
        ("swap_shoulder", InputButtons.SwapShoulder),
        ("slide", InputButtons.Slide),
        ("jump", InputButtons.Jump),
    };
}
