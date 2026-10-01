using System;
using Godot;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Core;
using Pb.Sim.Players;

namespace Pb.Game.Player;

/// <summary>
/// First-person controller for one sim player. Every physics tick it samples devices (or an
/// <see cref="ICommandSource"/>) into an <see cref="InputCommand"/>, applies the sim's movement
/// rules and does Godot's collide-and-slide. The camera turns at render rate for responsiveness
/// and its position is interpolated between ticks, so 120 Hz physics looks smooth at 144 Hz.
/// </summary>
public partial class PlayerController : CharacterBody3D, IPlayerDriver
{
    private SimWorld _sim = null!;
    private MovementParams _move = null!;
    private PresentationDef _view = null!;
    private GameSettings _settings = null!;
    private CollisionShape3D _collider = null!;
    private CapsuleShape3D _capsule = null!;
    private Node3D _head = null!;
    private float _yaw;
    private float _pitch;
    private Vector3 _previousEye;
    private Vector3 _currentEye;
    private float _bobPhase;
    private bool _wasCrouching;

    public PlayerState State { get; private set; } = null!;

    public Camera3D Camera { get; private set; } = null!;

    public ViewModel ViewModel { get; private set; } = null!;

    /// <summary>When set, drives the player instead of devices.</summary>
    public ICommandSource? AutoPilot { get; set; }

    public float Yaw => _yaw;

    public float Pitch => _pitch;

    public void Initialize(SimWorld sim, PlayerState state, PresentationDef view, GameSettings settings, Color teamColor)
    {
        _sim = sim;
        State = state;
        _move = sim.Config.Movement;
        _view = view;
        _settings = settings;
        _yaw = state.Yaw;
        _pitch = state.Pitch;

        GlobalPosition = state.Position.ToGodot();
        FloorSnapLength = 0.2f;
        FloorMaxAngle = Mathf.DegToRad(50f);

        _capsule = new CapsuleShape3D { Radius = _move.CapsuleRadius, Height = _move.StandCapsuleHeight };
        _collider = new CollisionShape3D { Shape = _capsule, Position = new Vector3(0, _move.StandCapsuleHeight * 0.5f, 0) };
        AddChild(_collider);

        // The head is top-level so it can be interpolated independently of the body's tick steps.
        _head = new Node3D { Name = "Head", TopLevel = true };
        AddChild(_head);
        Camera = new Camera3D { Name = "Camera", Current = true, Near = view.Camera.NearClip_m, Far = view.Camera.FarClip_m };
        _head.AddChild(Camera);
        ViewModel = new ViewModel { Name = "ViewModel" };
        Camera.AddChild(ViewModel);
        ViewModel.Build(view.ViewModel, teamColor);

        _currentEye = _previousEye = state.EyePosition.ToGodot();
        ApplyCamera(0f, 1f);
    }

    public void ApplyMovementParams(MovementParams movement) => _move = movement;

    public void Teleport(System.Numerics.Vector3 position, float yaw)
    {
        GlobalPosition = position.ToGodot();
        Velocity = Vector3.Zero;
        State.Position = position;
        State.Velocity = System.Numerics.Vector3.Zero;
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

        bool grounded = IsOnFloor();
        MovementResult result = MovementModel.Step(State, cmd, _move, dt, grounded);

        Vector3 velocity = Velocity;
        velocity.X = result.HorizontalVelocity.X;
        velocity.Z = result.HorizontalVelocity.Z;
        velocity.Y = grounded ? Mathf.Min(velocity.Y, 0f) : velocity.Y - _move.Gravity * dt;
        Velocity = velocity;

        bool crouching = result.Stance == Stance.Crouching;
        if (crouching != _wasCrouching)
        {
            float height = crouching ? _move.CrouchCapsuleHeight : _move.StandCapsuleHeight;
            _capsule.Height = height;
            _collider.Position = new Vector3(0, height * 0.5f, 0);
            _wasCrouching = crouching;
        }

        MoveAndSlide();

        State.Position = GlobalPosition.ToSim();
        State.Velocity = Velocity.ToSim();
        State.Stance = result.Stance;
        State.EyeHeight = result.EyeHeight;
        State.Sprinting = result.Sprinting;

        _previousEye = _currentEye;
        _currentEye = State.EyePosition.ToGodot();
        return cmd;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (AutoPilot is null && e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            float sensitivity = _settings.MouseSensitivityDegPerCount * Units.DegreesToRadians;
            _yaw -= motion.Relative.X * sensitivity;
            _pitch -= motion.Relative.Y * sensitivity * (_settings.InvertY ? -1f : 1f);
            _pitch = Math.Clamp(_pitch, -_move.MaxPitch, _move.MaxPitch);
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
                _pitch = Math.Clamp(_pitch, -_move.MaxPitch, _move.MaxPitch);
            }
        }

        float speed = State.HorizontalSpeed;
        if (_settings.HeadBob && IsOnFloor() && speed > 0.5f)
        {
            _bobPhase += speed * (float)delta / _view.Camera.HeadBobStride_m * Mathf.Tau;
        }
        else
        {
            _bobPhase = Mathf.MoveToward(_bobPhase % Mathf.Tau, 0f, (float)delta * 4f);
        }

        ApplyCamera((float)Engine.GetPhysicsInterpolationFraction(), Mathf.Min(1f, speed / Mathf.Max(0.1f, _move.RunSpeed)));
    }

    /// <summary>World position where the drawn barrel tip appears (for the ball's visual blend).</summary>
    public Vector3 VisualMuzzlePosition() => ViewModel.ApparentMuzzle(Camera);

    private void ApplyCamera(float alpha, float bobWeight)
    {
        Vector3 eye = _previousEye.Lerp(_currentEye, alpha);
        float bob = _settings.HeadBob ? Mathf.Sin(_bobPhase) * _view.Camera.HeadBobAmplitude_m * bobWeight : 0f;
        _head.GlobalPosition = eye + new Vector3(0f, bob, 0f);
        _head.Rotation = new Vector3(_pitch, _yaw, 0f);

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
            if (Input.IsActionPressed("fire"))
            {
                buttons |= InputButtons.Fire;
            }

            if (Input.IsActionPressed("sprint"))
            {
                buttons |= InputButtons.Sprint;
            }

            if (Input.IsActionPressed("crouch"))
            {
                buttons |= InputButtons.Crouch;
            }

            if (Input.IsActionPressed("walk"))
            {
                buttons |= InputButtons.Walk;
            }

            if (Input.IsActionPressed("refill"))
            {
                buttons |= InputButtons.Refill;
            }

            if (Input.IsActionPressed("fire_mode"))
            {
                buttons |= InputButtons.ToggleFireMode;
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
}
