using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Level;

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

    /// <summary>Full lean (rad).</summary>
    public required float LeanAngle { get; init; }

    /// <summary>The lean pivots this far below the eye (≈ the hips).</summary>
    public required float LeanPivotBelowEye { get; init; }

    public required float LeanInTime { get; init; }

    public required float LeanReturnTime { get; init; }

    /// <summary>Radius of the head sphere for the lean's wall check.</summary>
    public required float HeadRadius { get; init; }

    /// <summary>The furthest the head turns either way from the aim (rad).</summary>
    public required float MaxHeadTurn { get; init; }

    public required float ShoulderSwapTime { get; init; }

    public required float SlideMinSpeed { get; init; }

    public required float SlideBoost { get; init; }

    public required float SlideMaxSpeed { get; init; }

    public required float SlideFriction { get; init; }

    public required float SlideEndSpeed { get; init; }

    public required float SlideMaxTime { get; init; }

    public required float SlideCooldown { get; init; }

    /// <summary>Share of the ground acceleration the stick can use to steer a slide.</summary>
    public required float SlideSteering { get; init; }

    public required float SlideEyeHeight { get; init; }

    public required float SlideCapsuleHeight { get; init; }

    public required float JumpSpeed { get; init; }

    public required float JumpCooldown { get; init; }

    public required float SprintRecoveryTime { get; init; }

    public required ClimbParams Climbing { get; init; }

    public required FootstepParams Footsteps { get; init; }

    public float CapsuleHeightFor(Stance stance) => stance switch
    {
        Stance.Crouching => CrouchCapsuleHeight,
        Stance.Sliding => SlideCapsuleHeight,
        _ => StandCapsuleHeight,
    };

    public float EyeHeightFor(Stance stance) => stance switch
    {
        Stance.Crouching => CrouchEyeHeight,
        Stance.Sliding => SlideEyeHeight,
        _ => StandEyeHeight,
    };
}

/// <summary>Climbing ladders (movement.jsonc "climbing").</summary>
public sealed class ClimbParams
{
    public required float Speed { get; init; }

    public required float Reach { get; init; }

    public required float Standoff { get; init; }

    /// <summary>Half the cone of facings you can get on in (rad).</summary>
    public required float GrabAngle { get; init; }

    public required float StepOffSpeed { get; init; }

    public required float LetGoSpeed { get; init; }
}

public sealed class FootstepParams
{
    /// <summary>One footstep per this much ground covered (m).</summary>
    public required float Stride { get; init; }

    public required float CrouchRadius { get; init; }

    public required float WalkRadius { get; init; }

    public required float RunRadius { get; init; }

    public required float SprintRadius { get; init; }

    public required float SlideRadius { get; init; }

    public required float JumpRadius { get; init; }

    public required float LandRadius { get; init; }

    /// <summary>Landing slower than this (m/s, downward) makes no landing noise.</summary>
    public required float LandMinSpeed { get; init; }

    /// <summary>A foot on a rung per this much climbed (m).</summary>
    public required float ClimbStride { get; init; }

    public required float ClimbRadius { get; init; }

    /// <summary>Loudness multiplier per surface, indexed by <see cref="Collision.SurfaceId"/>.</summary>
    public required float[] SurfaceLoudness { get; init; }

    public float Loudness(Collision.SurfaceId surface) =>
        surface.Value < SurfaceLoudness.Length ? SurfaceLoudness[surface.Value] : 1f;
}

public struct MovementResult
{
    /// <summary>New horizontal velocity; vertical motion is left to the character controller.</summary>
    public Vector3 HorizontalVelocity;

    /// <summary>Upward speed to give the body this tick to start a jump; 0 when not jumping.</summary>
    public float JumpVelocity;

    /// <summary>On a ladder: the body moves at <see cref="HorizontalVelocity"/> and this upwards, with no gravity.</summary>
    public bool Climbing;

    public float ClimbVelocity;

    /// <summary>Collision capsule height for the stance.</summary>
    public float CapsuleHeight;

    public Stance Stance;
    public float EyeHeight;
    public bool Sprinting;
}

/// <summary>
/// Movement rules: what the player wants to do this tick, turned into a target velocity, a posture
/// (stance, eye height, lean, shoulder) and maybe a jump. The posture is written to the player
/// state here; the host's character controller then collides and slides with the returned velocity
/// and writes position, velocity and <see cref="PlayerState.Grounded"/> back. Given a collision
/// world, a lean stops before the head meets a wall and standing up waits for headroom. Given the
/// level's ladders, interact gets you on one and you climb it (<see cref="Climb"/>). The rules
/// are deterministic for a given state and command, so bots, the server and client prediction all
/// replay them identically.
/// </summary>
public static class MovementModel
{
    /// <summary>Headroom and lean checks keep this far from geometry (m).</summary>
    private const float Clearance = 0.02f;

    /// <summary>Getting on or off at the top gives up after this long, when something's in the way (s).</summary>
    private const float TopTimeout = 1.5f;

    /// <summary>The fastest a climber is pulled onto the climbing line (m/s).</summary>
    private const float MaxPull = 3f;

    public static MovementResult Step(PlayerState state, in InputCommand cmd, MovementParams p, float dt, bool grounded,
        CollisionWorld? world = null, LadderSet? ladders = null)
    {
        InputButtons pressed = cmd.Buttons & ~state.PreviousButtons;
        state.PreviousButtons = cmd.Buttons;
        state.SlideCooldown = MathF.Max(0f, state.SlideCooldown - dt);
        state.JumpCooldown = MathF.Max(0f, state.JumpCooldown - dt);
        bool alive = state.Alive;

        if (state.Ladder >= 0)
        {
            if (ladders is not null && state.Ladder < ladders.Count)
            {
                return Climb(state, cmd, pressed, p, dt, grounded, ladders[state.Ladder]);
            }

            state.Ladder = -1; // its level is gone
        }

        // Interact facing a ladder within reach gets you on it (out over its top, to climb down).
        if (alive && state.Present && (pressed & InputButtons.Interact) != 0 && ladders is { Count: > 0 } && state.Stance != Stance.Sliding)
        {
            int ladder = ladders.FindGrab(state.Position, cmd.Yaw, p.Climbing, out bool fromTop);
            if (ladder >= 0)
            {
                state.Ladder = ladder;
                state.LadderPhase = fromTop ? LadderPhase.GettingOn : LadderPhase.Climbing;
                state.LadderTime = 0f;
                state.ClimbDistance = 0f;
                return Climb(state, cmd, InputButtons.None, p, dt, grounded, ladders[ladder]);
            }
        }

        Vector2 move = cmd.Move;
        float amount = move.Length();
        if (amount > 1f)
        {
            move /= amount;
            amount = 1f;
        }

        Vector3 wish = ViewAngles.Right(cmd.Yaw) * move.X + ViewAngles.FlatForward(cmd.Yaw) * move.Y;
        Vector3 wishDir = VectorMath.NormalizeOr(wish, Vector3.Zero);
        Vector3 current = VectorMath.Horizontal(state.Velocity);
        Stance stance = state.Stance;

        // Shoulder swap: the marker crosses to the other side over the swap time.
        if (alive && (pressed & InputButtons.SwapShoulder) != 0)
        {
            state.ShoulderTarget = -state.ShoulderTarget;
        }

        state.Shoulder = MoveTowards(state.Shoulder, state.ShoulderTarget, 2f / p.ShoulderSwapTime * dt);

        // A slide starts from a run: the slide key, or crouch while sprinting.
        float speed = current.Length();
        bool slidePressed = (pressed & InputButtons.Slide) != 0 || ((pressed & InputButtons.Crouch) != 0 && state.Sprinting);
        if (alive && slidePressed && stance != Stance.Sliding && grounded && state.SlideCooldown <= 0f && speed >= p.SlideMinSpeed)
        {
            stance = Stance.Sliding;
            state.SlideTime = 0f;
            current *= MathF.Min(speed + p.SlideBoost, MathF.Max(speed, p.SlideMaxSpeed)) / speed;
        }

        Vector3 velocity;
        bool sprinting = false;
        float jump = 0f;
        if (stance == Stance.Sliding)
        {
            // Steer a little, lose speed to friction, and end crouched once slow (or airborne).
            state.SlideTime += dt;
            Vector3 steered = current + wishDir * (p.SlideSteering * p.GroundAcceleration * dt);
            float s = current.Length();
            Vector3 dir = VectorMath.NormalizeOr(steered, wishDir);
            s = MathF.Max(0f, s - p.SlideFriction * dt);
            velocity = dir * s;
            bool done = !alive || !grounded || s <= p.SlideEndSpeed || state.SlideTime >= p.SlideMaxTime;
            if (done && HasRoom(state, p.SlideCapsuleHeight, p.CrouchCapsuleHeight, p, world))
            {
                stance = Stance.Crouching;
                state.SlideCooldown = p.SlideCooldown;
            }
        }
        else
        {
            // Crouch while held; stand up again only where there's headroom (eliminated players stand to walk off).
            if (alive && cmd.Has(InputButtons.Crouch))
            {
                stance = Stance.Crouching;
            }
            else if (stance == Stance.Crouching && HasRoom(state, p.CrouchCapsuleHeight, p.StandCapsuleHeight, p, world))
            {
                stance = Stance.Standing;
            }

            // Eliminated players can only walk (off the field, marker raised).
            sprinting = alive && cmd.Has(InputButtons.Sprint) && stance == Stance.Standing && move.Y >= p.SprintMinForwardInput &&
                        !state.SprintBlocked;
            float target = !alive ? p.WalkSpeed
                : stance == Stance.Crouching ? p.CrouchSpeed
                : sprinting ? p.SprintSpeed
                : cmd.Has(InputButtons.Walk) ? p.WalkSpeed
                : p.RunSpeed;
            Vector3 desired = state.Present ? wishDir * (target * amount) : Vector3.Zero;
            float rate = !grounded ? p.AirAcceleration
                : desired.LengthSquared() >= current.LengthSquared() ? p.GroundAcceleration
                : p.GroundDeceleration;
            velocity = VectorMath.MoveTowards(current, desired, rate * dt);

            if (alive && (pressed & InputButtons.Jump) != 0 && grounded && stance == Stance.Standing && state.JumpCooldown <= 0f)
            {
                jump = p.JumpSpeed;
                state.JumpCooldown = p.JumpCooldown;
            }
        }

        state.SprintRecovery = sprinting ? p.SprintRecoveryTime : MathF.Max(0f, state.SprintRecovery - dt);

        float targetEye = p.EyeHeightFor(stance);
        float eye = state.EyeHeight <= 0f ? targetEye : MoveTowards(state.EyeHeight, targetEye, p.StanceTransitionSpeed * dt);

        // Lean: out at the lean-in rate, back (or across, through upright) at the return rate.
        float leanTarget = 0f;
        if (alive && !sprinting && stance != Stance.Sliding)
        {
            leanTarget = (cmd.Has(InputButtons.LeanRight) ? 1f : 0f) - (cmd.Has(InputButtons.LeanLeft) ? 1f : 0f);
        }

        float lean = state.Lean;
        float goal = lean != 0f && MathF.Sign(leanTarget) != MathF.Sign(lean) ? 0f : leanTarget;
        bool outward = MathF.Abs(goal) > MathF.Abs(lean);
        lean = MoveTowards(lean, goal, (outward ? 1f / p.LeanInTime : 1f / p.LeanReturnTime) * dt);
        if (lean != 0f && world is not null)
        {
            lean *= LeanClearance(state.Position + new Vector3(0f, eye, 0f), lean, cmd.Yaw, p, world);
        }

        state.Stance = stance;
        state.EyeHeight = eye;
        state.Sprinting = sprinting;
        state.Lean = lean;
        state.LeanRoll = lean * p.LeanAngle;
        state.LeanOffset = PlayerPose.LeanOffset(state.LeanRoll, cmd.Yaw, p.LeanPivotBelowEye);

        return new MovementResult
        {
            HorizontalVelocity = velocity,
            JumpVelocity = jump,
            CapsuleHeight = p.CapsuleHeightFor(stance),
            Stance = stance,
            EyeHeight = eye,
            Sprinting = sprinting,
        };
    }

    /// <summary>
    /// A tick on a ladder. Forward climbs and back climbs down (the eliminated climb down, to walk off); at the top,
    /// climbing on steps you over it onto what it climbs to, and at the bottom climbing down puts you on the ground; jump
    /// lets go. The body hangs on the climbing line out in front of the rungs, upright, hands on the rungs.
    /// </summary>
    private static MovementResult Climb(PlayerState state, in InputCommand cmd, InputButtons pressed, MovementParams p, float dt,
        bool grounded, LadderSpec ladder)
    {
        ClimbParams c = p.Climbing;
        Vector3 feet = state.Position;
        Vector3 forward = ladder.Forward;
        Vector3 horizontal;
        float vertical;
        bool off = false;
        state.LadderTime += dt;
        switch (state.LadderPhase)
        {
            case LadderPhase.SteppingOff:
                // On over the top, rising until clear of its edge, until past the rungs by the ladder's exit.
                horizontal = forward * c.StepOffSpeed;
                vertical = feet.Y < ladder.TopY + 0.05f ? c.StepOffSpeed : 0f;
                off = -ladder.Ahead(feet) >= ladder.Exit || state.LadderTime >= TopTimeout;
                break;

            case LadderPhase.GettingOn:
            {
                // Back out over the top onto the climbing line, the feet held at the top's height until there.
                Vector3 to = ladder.ClimbPoint(ladder.TopY, c.Standoff) - feet;
                to.Y = 0f;
                float distance = to.Length();
                if (distance <= c.StepOffSpeed * dt || state.LadderTime >= TopTimeout)
                {
                    horizontal = to / dt;
                    state.LadderPhase = LadderPhase.Climbing;
                    state.LadderTime = 0f;
                }
                else
                {
                    horizontal = to * (c.StepOffSpeed / distance);
                }

                vertical = Math.Clamp((ladder.TopY - feet.Y) / dt, -c.StepOffSpeed, c.StepOffSpeed);
                break;
            }

            default:
            {
                if (state.Alive && (pressed & InputButtons.Jump) != 0)
                {
                    // Let go: pushed off backwards, to fall.
                    off = true;
                    horizontal = -forward * c.LetGoSpeed;
                    vertical = 0f;
                    break;
                }

                float input = state.Alive ? Math.Clamp(cmd.Move.Y, -1f, 1f) : -1f;
                vertical = input * c.Speed;
                if (input > 0f && feet.Y >= ladder.TopY - 0.02f)
                {
                    state.LadderPhase = LadderPhase.SteppingOff;
                    state.LadderTime = 0f;
                    vertical = c.StepOffSpeed;
                }
                else if (input < 0f && (grounded || feet.Y <= ladder.Foot.Y - 0.6f))
                {
                    off = true; // down: off onto the ground
                    vertical = 0f;
                }
                else if (feet.Y + vertical * dt > ladder.TopY)
                {
                    vertical = (ladder.TopY - feet.Y) / dt;
                }

                // Held on the climbing line: pulled onto it within a few ticks.
                Vector3 to = ladder.ClimbPoint(feet.Y, c.Standoff) - feet;
                to.Y = 0f;
                float pull = to.Length() / dt;
                horizontal = pull > MaxPull ? to * (MaxPull / to.Length()) : to / dt;
                break;
            }
        }

        if (off)
        {
            state.Ladder = -1;
            state.LadderPhase = LadderPhase.Climbing;
            state.LadderTime = 0f;
        }

        // Upright with both hands on the rungs: no crouch, lean or sprint; a lean eases back.
        float eye = state.EyeHeight <= 0f ? p.StandEyeHeight : MoveTowards(state.EyeHeight, p.StandEyeHeight, p.StanceTransitionSpeed * dt);
        state.Stance = Stance.Standing;
        state.EyeHeight = eye;
        state.Sprinting = false;
        state.SprintRecovery = MathF.Max(0f, state.SprintRecovery - dt);
        state.Shoulder = MoveTowards(state.Shoulder, state.ShoulderTarget, 2f / p.ShoulderSwapTime * dt);
        state.Lean = MoveTowards(state.Lean, 0f, dt / p.LeanReturnTime);
        state.LeanRoll = state.Lean * p.LeanAngle;
        state.LeanOffset = PlayerPose.LeanOffset(state.LeanRoll, ladder.Facing, p.LeanPivotBelowEye);
        return new MovementResult
        {
            HorizontalVelocity = new Vector3(horizontal.X, 0f, horizontal.Z),
            Climbing = !off,
            ClimbVelocity = vertical,
            CapsuleHeight = p.StandCapsuleHeight,
            Stance = Stance.Standing,
            EyeHeight = eye,
            Sprinting = false,
        };
    }

    /// <summary>
    /// True if the player's capsule can grow from <paramref name="fromHeight"/> to
    /// <paramref name="toHeight"/> without its top hitting anything (always true without a world).
    /// </summary>
    public static bool HasRoom(PlayerState state, float fromHeight, float toHeight, MovementParams p, CollisionWorld? world)
    {
        if (world is null || toHeight <= fromHeight)
        {
            return true;
        }

        float r = p.CapsuleRadius - Clearance;
        Vector3 from = state.Position + new Vector3(0f, fromHeight - p.CapsuleRadius, 0f);
        Vector3 to = state.Position + new Vector3(0f, toHeight - p.CapsuleRadius, 0f);
        return !world.SweepSphere(from, to, r, out _);
    }

    /// <summary>
    /// The share (0..1) of a lean the head can make before it meets a wall: the head sphere is swept
    /// from the upright eye toward the leaned one.
    /// </summary>
    private static float LeanClearance(Vector3 uprightEye, float lean, float yaw, MovementParams p, CollisionWorld world)
    {
        Vector3 leaned = uprightEye + PlayerPose.LeanOffset(lean * p.LeanAngle, yaw, p.LeanPivotBelowEye);
        if (!world.SweepSphere(uprightEye, leaned, p.HeadRadius, out SweepHit hit))
        {
            return 1f;
        }

        float travel = Vector3.Distance(uprightEye, leaned);
        return travel <= 1e-5f ? 0f : MathF.Max(0f, hit.T - Clearance / travel);
    }

    private static float MoveTowards(float current, float target, float maxDelta) =>
        MathF.Abs(target - current) <= maxDelta ? target : current + MathF.CopySign(maxDelta, target - current);
}

/// <summary>Body geometry from posture: where the lean puts the eye, and where the muzzle sits.</summary>
public static class PlayerPose
{
    /// <summary>World-space shift of the eye when the body rolls by <paramref name="roll"/> (rad, + = right) about a pivot below it.</summary>
    public static Vector3 LeanOffset(float roll, float yaw, float pivotBelowEye)
    {
        (float s, float c) = MathF.SinCos(roll);
        return ViewAngles.Right(yaw) * (s * pivotBelowEye) + new Vector3(0f, (c - 1f) * pivotBelowEye, 0f);
    }

    /// <summary>
    /// The muzzle offset in view space (x right, y up, z forward) with the marker on
    /// <paramref name="shoulder"/> (+1 right, −1 left, in between mid-swap) and rolled with the lean.
    /// </summary>
    public static Vector3 MuzzleOffset(Vector3 offset, float shoulder, float roll)
    {
        float x = offset.X * shoulder;
        (float s, float c) = MathF.SinCos(roll);
        return new Vector3(x * c + offset.Y * s, -x * s + offset.Y * c, offset.Z);
    }
}
