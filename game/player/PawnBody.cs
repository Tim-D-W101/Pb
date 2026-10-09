using Godot;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Players;

namespace Pb.Game.Player;

/// <summary>
/// One participant's body in the world. Each tick it runs the sim's movement rules on an
/// <see cref="InputCommand"/>, sizes its capsule for the stance, does Godot's collide-and-slide,
/// and writes position, velocity and whether it's on the ground back to the sim's player state.
/// The local player and bots both use it, so everyone moves by exactly the same rules.
/// </summary>
public partial class PawnBody : CharacterBody3D
{
    private CollisionShape3D _collider = null!;
    private CapsuleShape3D _capsule = null!;

    public PlayerState State { get; private set; } = null!;

    /// <summary>
    /// Whether the body is on the ground for its next move, instead of what its last collide-and-slide found: a joining
    /// copy puts the server's state back before replaying, and the body hasn't moved since.
    /// </summary>
    protected bool? GroundedOverride { get; set; }

    protected SimWorld Sim { get; private set; } = null!;

    protected MovementParams Move { get; private set; } = null!;

    public void ApplyMovementParams(MovementParams movement) => Move = movement;

    public virtual void Teleport(System.Numerics.Vector3 position, float yaw)
    {
        GlobalPosition = position.ToGodot();
        Velocity = Vector3.Zero;
        State.Position = position;
        State.Velocity = System.Numerics.Vector3.Zero;
        State.Yaw = yaw;
    }

    protected void InitializeBody(SimWorld sim, PlayerState state)
    {
        Sim = sim;
        State = state;
        Move = sim.Config.Movement;
        GlobalPosition = state.Position.ToGodot();
        FloorSnapLength = 0.2f;
        FloorMaxAngle = Mathf.DegToRad(50f);

        _capsule = new CapsuleShape3D { Radius = Move.CapsuleRadius, Height = Move.CapsuleHeightFor(state.Stance) };
        _collider = new CollisionShape3D { Shape = _capsule, Position = new Vector3(0, _capsule.Height * 0.5f, 0) };
        AddChild(_collider);
    }

    /// <summary>Puts the body where its state is (a joining copy: the server's pose for someone else, or a correction).</summary>
    protected void FollowState(bool? grounded = null)
    {
        GlobalPosition = State.Position.ToGodot();
        Velocity = State.Velocity.ToGodot();
        GroundedOverride = grounded;
        float height = Move.CapsuleHeightFor(State.Stance);
        if (!Mathf.IsEqualApprox(_capsule.Height, height))
        {
            _capsule.Height = height;
            _collider.Position = new Vector3(0, height * 0.5f, 0);
        }
    }

    /// <summary>Moves the body one tick by the sim's movement rules (with the sim's world for headroom and lean checks).</summary>
    protected MovementResult ApplyCommand(in InputCommand command, float dt)
    {
        // Outside a live round (the briefing, after the end) you can look around but not move or act;
        // eliminated players still walk off.
        InputCommand cmd = command;
        if (!Sim.IsLive && State.Alive)
        {
            cmd.Move = System.Numerics.Vector2.Zero;
            cmd.Buttons = InputButtons.None;
        }

        bool grounded = GroundedOverride ?? IsOnFloor();
        GroundedOverride = null;
        MovementResult result = MovementModel.Step(State, cmd, Move, dt, grounded, Sim.Collision, Sim.Ladders);

        // On a ladder the rules give the climb itself: no gravity.
        Vector3 velocity = Velocity;
        velocity.X = result.HorizontalVelocity.X;
        velocity.Z = result.HorizontalVelocity.Z;
        velocity.Y = result.Climbing ? result.ClimbVelocity
            : result.JumpVelocity > 0f ? result.JumpVelocity
            : grounded ? Mathf.Min(velocity.Y, 0f)
            : velocity.Y - Move.Gravity * dt;
        Velocity = velocity;

        if (!Mathf.IsEqualApprox(_capsule.Height, result.CapsuleHeight))
        {
            _capsule.Height = result.CapsuleHeight;
            _collider.Position = new Vector3(0, result.CapsuleHeight * 0.5f, 0);
        }

        MoveAndSlide();

        State.Position = GlobalPosition.ToSim();
        State.Velocity = Velocity.ToSim();
        State.Grounded = IsOnFloor();
        return result;
    }
}
