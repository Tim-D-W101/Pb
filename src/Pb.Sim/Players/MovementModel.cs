using System.Numerics;
using Pb.Sim.Core;

namespace Pb.Sim.Players;

public sealed class MovementParams
{
    public required float WalkSpeed { get; init; }

    public required float RunSpeed { get; init; }

    public required float SprintSpeed { get; init; }

    public required float CrouchSpeed { get; init; }

    public required float GroundAcceleration { get; init; }

    public required float GroundDeceleration { get; init; }

    public required float AirAcceleration { get; init; }

    public required float StandEyeHeight { get; init; }

    public required float CrouchEyeHeight { get; init; }

    /// <summary>Eye height change rate between stances (m/s).</summary>
    public required float StanceTransitionSpeed { get; init; }

    public required float StandCapsuleHeight { get; init; }

    public required float CrouchCapsuleHeight { get; init; }

    public required float CapsuleRadius { get; init; }

    /// <summary>Sprint needs at least this much forward stick/keys (0..1).</summary>
    public required float SprintMinForwardInput { get; init; }

    public required float MaxPitch { get; init; }

    public required float Gravity { get; init; }
}

public struct MovementResult
{
    /// <summary>New horizontal velocity; vertical motion is left to the character controller.</summary>
    public Vector3 HorizontalVelocity;
    public Stance Stance;
    public float EyeHeight;
    public bool Sprinting;
}

/// <summary>
/// Pure movement rules: what the player wants to do this tick, turned into a target velocity,
/// stance and eye height. The Godot character controller does the collide-and-slide. Keeping the
/// rules here means the server and client prediction can replay them (Phase 3).
/// </summary>
public static class MovementModel
{
    public static MovementResult Step(PlayerState state, in InputCommand cmd, MovementParams p, float dt, bool grounded)
    {
        Vector2 move = cmd.Move;
        float amount = move.Length();
        if (amount > 1f)
        {
            move /= amount;
            amount = 1f;
        }

        Stance stance = cmd.Has(InputButtons.Crouch) ? Stance.Crouching : Stance.Standing;
        bool sprinting = state.Alive && cmd.Has(InputButtons.Sprint) && stance == Stance.Standing &&
                         move.Y >= p.SprintMinForwardInput;

        float speed = stance == Stance.Crouching ? p.CrouchSpeed
            : sprinting ? p.SprintSpeed
            : cmd.Has(InputButtons.Walk) ? p.WalkSpeed
            : p.RunSpeed;

        Vector3 wish = ViewAngles.Right(cmd.Yaw) * move.X + ViewAngles.FlatForward(cmd.Yaw) * move.Y;
        Vector3 desired = state.Alive ? VectorMath.NormalizeOr(wish, Vector3.Zero) * (speed * amount) : Vector3.Zero;
        Vector3 current = VectorMath.Horizontal(state.Velocity);

        float rate = !grounded ? p.AirAcceleration
            : desired.LengthSquared() >= current.LengthSquared() ? p.GroundAcceleration
            : p.GroundDeceleration;

        float targetEye = stance == Stance.Crouching ? p.CrouchEyeHeight : p.StandEyeHeight;
        float eye = state.EyeHeight <= 0f
            ? targetEye
            : MoveTowards(state.EyeHeight, targetEye, p.StanceTransitionSpeed * dt);

        return new MovementResult
        {
            HorizontalVelocity = VectorMath.MoveTowards(current, desired, rate * dt),
            Stance = stance,
            EyeHeight = eye,
            Sprinting = sprinting,
        };
    }

    private static float MoveTowards(float current, float target, float maxDelta) =>
        MathF.Abs(target - current) <= maxDelta ? target : current + MathF.CopySign(maxDelta, target - current);
}
