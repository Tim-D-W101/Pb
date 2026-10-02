using Godot;
using Pb.Game.Core;

namespace Pb.Game.Player;

/// <summary>
/// How a character's legs move: which movement clips play, how much, where in their cycle and which
/// way the legs face; or, without a walk clip, the procedural steps. It's fed the ground covered each
/// frame, so clips play at the pace that keeps their feet planted. Walk blends into run between the
/// two clips' own speeds and into the crouched walk as the body crouches, all on one phase so the feet
/// stay in step; the legs turn towards where the body is going (the chest stays on the aim), and
/// backing off plays the cycle backwards. <see cref="CharacterPoser"/> applies it.
/// </summary>
public sealed class Gait
{
    /// <summary>Below this speed (m/s) the body counts as standing still.</summary>
    private const float Standing = 0.25f;

    /// <summary>Moving more than this far round from the facing (rad) counts as backing off, and less than the other to stop.</summary>
    private const float BackOff = 1.92f, BackOn = 1.22f;

    private readonly CharactersDef _look;
    private bool _backward;

    public Gait(CharactersDef look, MoveClip? walk, MoveClip? run, MoveClip? crouch)
    {
        _look = look;
        Walk = walk;
        Run = walk is null ? null : run;
        Crouch = walk is null ? null : crouch;
    }

    public MoveClip? Walk { get; }

    public MoveClip? Run { get; }

    public MoveClip? Crouch { get; }

    /// <summary>Where the clips are in their cycle (cycles; 0 with the left foot under the body).</summary>
    public float Phase { get; private set; }

    /// <summary>How much the clips drive the body over its idle (0–1), and how much of that is the run and the crouched walk.</summary>
    public float MoveWeight { get; private set; }

    public float RunWeight { get; private set; }

    public float CrouchWeight { get; private set; }

    /// <summary>How far the legs turn from the facing towards the travel (rad, positive to the left).</summary>
    public float LegYaw { get; private set; }

    /// <summary>Procedural steps, without clips: phase (rad), how much of a full step (0–1) and which way (world, unit).</summary>
    public float StepPhase { get; private set; }

    public float StepAmount { get; private set; }

    public Vector3 StepDirection { get; private set; } = Vector3.Forward;

    /// <summary>How far below their rest height the playing clips already carry the hips, on average (m).</summary>
    public float ClipHipsDrop
    {
        get
        {
            if (Walk is null)
            {
                return 0f;
            }

            float drop = Mathf.Lerp(Walk.HipsDrop_m, Run?.HipsDrop_m ?? Walk.HipsDrop_m, RunWeight);
            drop = Mathf.Lerp(drop, Crouch?.HipsDrop_m ?? drop, CrouchWeight);
            return drop * MoveWeight;
        }
    }

    /// <summary>
    /// Moves the gait on by a frame: <paramref name="moved"/> is the ground the feet covered (world),
    /// <paramref name="velocity"/> the body's, <paramref name="forward"/> its facing (world, horizontal,
    /// unit), <paramref name="crouch"/> how far it's crouched (0 standing, 1 fully). A sliding body's legs
    /// hold still, and one in the air keeps its step.
    /// </summary>
    public void Update(Vector3 moved, Vector3 velocity, Vector3 forward, float crouch, bool sliding, bool grounded, float delta)
    {
        moved.Y = 0f;
        velocity.Y = 0f;
        float speed = sliding ? 0f : velocity.Length();
        float distance = sliding || !grounded ? 0f : moved.Length();
        if (Walk is null)
        {
            Steps(speed, distance, velocity, forward, delta);
            return;
        }

        float ease = delta / _look.ClipBlend_s;
        if (grounded)
        {
            MoveWeight = Mathf.MoveToward(MoveWeight, speed > Standing ? 1f : 0f, ease);
        }

        if (Run is not null)
        {
            // Walk below the walk clip's own speed, run above the run clip's, a blend between.
            float low = Walk.Speed_mps, high = Run.Speed_mps;
            float run = high > low + 0.1f ? Mathf.Clamp((speed - low) / (high - low), 0f, 1f) : speed > (low + high) * 0.5f ? 1f : 0f;
            if (speed > Standing)
            {
                RunWeight = Mathf.MoveToward(RunWeight, Mathf.SmoothStep(0f, 1f, run), ease);
            }
        }

        if (Crouch is not null)
        {
            CrouchWeight = Mathf.MoveToward(CrouchWeight, Mathf.Clamp(crouch, 0f, 1f), ease);
        }

        float yaw = 0f;
        if (speed > Standing)
        {
            Vector3 left = new(forward.Z, 0f, -forward.X);
            float travel = Mathf.Atan2(velocity.Dot(left), velocity.Dot(forward));
            if (_backward ? Mathf.Abs(travel) < BackOn : Mathf.Abs(travel) > BackOff)
            {
                _backward = !_backward;
            }

            // Legs face the travel, or away from it when backing off.
            yaw = _backward ? travel - Mathf.Pi * Mathf.Sign(travel) : travel;
            float limit = Mathf.DegToRad(_look.MaxLegYaw_deg);
            yaw = Mathf.Clamp(yaw, -limit, limit);
        }

        LegYaw = Mathf.Lerp(LegYaw, yaw, 1f - Mathf.Exp(-delta / _look.ClipBlend_s));

        float cycle = Mathf.Lerp(Walk.CycleDistance_m, Run?.CycleDistance_m ?? Walk.CycleDistance_m, RunWeight);
        cycle = Mathf.Lerp(cycle, Crouch?.CycleDistance_m ?? cycle, CrouchWeight);
        Phase += (_backward ? -1f : 1f) * distance / Mathf.Max(cycle, 0.1f);
        Phase -= Mathf.Floor(Phase);
    }

    /// <summary>Without clips: one stride per half cycle, as far as the feet have moved, along the travel; they settle when it stops.</summary>
    private void Steps(float speed, float distance, Vector3 velocity, Vector3 forward, float delta)
    {
        float target = Mathf.Clamp(speed / _look.FullStrideSpeed_mps, 0f, 1f);
        StepAmount = Mathf.MoveToward(StepAmount, target, delta / _look.StrideEase_s);
        StepPhase = Mathf.Wrap(StepPhase + distance / _look.Stride_m * Mathf.Pi, 0f, Mathf.Tau);
        if (speed > Standing)
        {
            StepDirection = velocity.Normalized();
        }
        else if (StepAmount <= 0f)
        {
            StepDirection = forward;
        }
    }
}
