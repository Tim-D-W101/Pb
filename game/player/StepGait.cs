using Godot;
using Pb.Game.Core;

namespace Pb.Game.Player;

/// <summary>
/// Where a character's feet go, in the world. Each foot stays planted where it landed until its next
/// step, so feet never skate, whichever way the body goes and however fast: walking, running, strafing,
/// backing off or turning on the spot. Steps follow a gait cycle (two steps) whose rate, the share of it
/// each foot spends in the air, the lift and where the foot lands all come from the speed
/// (<see cref="StepsDef.Gaits"/>): a walk with both feet down between steps, then a jog and a run with
/// both off the ground. Each landing is aimed ahead along the body's travel so the foot passes under its
/// hip partway through its time down, and set on the real ground there (stair treads, kerbs). Standing,
/// the feet keep their places in a shooter's stance while the upper body turns with the aim, and step
/// round, one then the other, when the twist or the drift gets too much. Pulling up from a run (the body
/// halts in a few hundredths of a second), the foot in the air comes down short into the stance and the
/// other follows at once, while the hips dip and the upper body tips on and settles back. The hips bob,
/// sway, turn with the stride and drop as far as both legs need to reach their feet.
/// <see cref="CharacterPoser"/> bends the legs to it.
/// </summary>
public sealed class StepGait
{
    /// <summary>Below this speed (m/s) the body is standing.</summary>
    private const float Moving = 0.3f;

    /// <summary>Further than this (m) in one frame is a teleport: the feet are put back under the body.</summary>
    private const float Teleport = 1.5f;

    private readonly StepsDef _def;
    private readonly FootState _left = new(-1f, 0f);
    private readonly FootState _right = new(1f, 0.5f);
    private readonly FootState[] _feet;
    private Vector3 _root;
    private Vector3 _velocity;
    private bool _started;
    private bool _moving;
    private float _phase;
    private float _cadence = 1f;
    private float _swing = 0.4f;
    private float _reachDrop;
    private float _run;
    private float _legYaw;
    private float _facing;
    private float _side = 1f;
    private float _crouch;
    private float _hipDrop;
    private float _soft;
    private float _lean;
    private float _recentSpeed;
    private float _stopFrom;
    private float _stopAge = 10f;
    private System.Random _random = new(1);
    private float _weight;
    private float _weightTarget;
    private float _weightIn;

    public StepGait(StepsDef def)
    {
        _def = def;
        _feet = new[] { _left, _right };
    }

    /// <summary>Gives this character its own timing for the idle shifts of weight (so two side by side don't move as one).</summary>
    public void Vary(int seed)
    {
        _random = new System.Random(seed);
        _weightIn = Between(0f, _def.WeightEveryMax_s);
    }

    /// <summary>The legs' build (m): set once the model's rest pose has been measured.</summary>
    public LegBuild Build { get; set; }

    /// <summary>
    /// How far the knees go out to the sides instead of forward (0–1): crouched with something just ahead at knee height.
    /// Standing still the feet also come back from it, up to kneeBack_m.
    /// </summary>
    public float KneesOut { get; set; }

    public bool HasBuild => Build.Length > 0f;

    /// <summary>Where the ankles go this frame (world) and which way the feet point (world, unit, level).</summary>
    public Vector3 LeftAnkle => _left.Ankle;

    public Vector3 RightAnkle => _right.Ankle;

    public Vector3 LeftForward => Forward(_left.Yaw);

    public Vector3 RightForward => Forward(_right.Yaw);

    /// <summary>Whether each foot is down where it landed (not in the air, not sliding with the body).</summary>
    public bool LeftPlanted => !_left.Swinging && !_left.Airborne;

    public bool RightPlanted => !_right.Swinging && !_right.Airborne;

    /// <summary>
    /// How far each foot is pitched from flat (rad): positive toe-down with the heel up about the ball of
    /// the foot (late in its time down, and at lift-off), negative toes-up about the heel (landing).
    /// </summary>
    public float LeftHeel => _left.Heel;

    public float RightHeel => _right.Heel;

    /// <summary>The hips' offset (world) from where they'd stand at this crouch: the stride's bob and the drop the legs need (down), the sway (sideways).</summary>
    public Vector3 HipsOffset { get; private set; }

    /// <summary>How far the hips turn from the facing (rad, positive to the left): towards the travel, the stance's blade, the stride's swing.</summary>
    public float HipsTwist { get; private set; }

    /// <summary>The hips' roll with the stride (rad, positive drops the right hip).</summary>
    public float HipsRoll { get; private set; }

    /// <summary>The upper body's lean (rad, forward): into a run, and on over the feet as it pulls up.</summary>
    public float Lean => _lean + Mathf.DegToRad(_def.StopLean_deg) * Settling();

    /// <summary>
    /// Moves on a frame. <paramref name="root"/> is where the body stands (world, its feet),
    /// <paramref name="facing"/> the yaw it aims along, <paramref name="crouch"/> how far it's crouched (0–1)
    /// and <paramref name="hipDrop"/> how far that lowers the hips (m), <paramref name="shoulder"/> the side the
    /// marker is on (+1 right). <paramref name="ground"/> gives the ground's height near a point.
    /// </summary>
    public void Update(Vector3 root, float facing, float crouch, float hipDrop, float shoulder, bool sliding, bool grounded, float delta,
        GroundQuery ground)
    {
        if (!HasBuild || delta <= 0f)
        {
            return;
        }

        _facing = facing;
        _crouch = Mathf.Clamp(crouch, 0f, 1f);
        _hipDrop = hipDrop;
        _side = shoulder >= 0f ? 1f : -1f;
        if (!_started || root.DistanceTo(_root) > Teleport)
        {
            Reset(root, ground);
        }

        // The body's velocity from where it's drawn, smoothed (the sim moves it in ticks).
        Vector3 moved = root - _root;
        _root = root;
        var flat = new Vector3(moved.X / delta, 0f, moved.Z / delta);
        _velocity = _velocity.Lerp(flat, 1f - Mathf.Exp(-delta / _def.VelocitySmoothing_s));
        float speed = _velocity.Length();
        _stopAge += delta;

        if (!grounded || sliding)
        {
            Unplanted(sliding, ground, delta);
            Feet(delta);
            return;
        }

        foreach (FootState foot in _feet)
        {
            if (foot.Airborne)
            {
                // Landed: the feet go down where they are.
                foot.Plant(Grounded(foot.Position, ground), foot.Yaw);
            }
        }

        Row gait = GaitAt(speed);
        float ease = 1f - Mathf.Exp(-delta / 0.15f);
        _cadence = Mathf.Lerp(_cadence, gait.Cadence, ease);
        _swing = Mathf.Lerp(_swing, gait.Swing, ease);
        _run = Mathf.Lerp(_run, Mathf.Clamp((_swing - 0.47f) / 0.13f, 0f, 1f), ease);
        bool moving = speed > Moving;
        if (moving && !_moving)
        {
            Start();
        }
        else if (!moving && _moving)
        {
            // Pulling up: how fast the body was going just before decides how hard the stop is.
            _stopFrom = _recentSpeed;
            _stopAge = 0f;
        }

        // The fastest the body has gone lately (it falls away over a quarter of a second).
        _recentSpeed = Mathf.Max(speed, Mathf.Lerp(_recentSpeed, speed, 1f - Mathf.Exp(-delta / 0.25f)));
        _moving = moving;
        float turn = Mathf.DegToRad(_def.LegTurnRate_degps) * delta;
        if (moving)
        {
            _legYaw = ApproachAngle(_legYaw, _facing + TravelTwist(), turn);
            float before = _phase;
            _phase = Mathf.PosMod(_phase + _cadence * delta, 1f);
            foreach (FootState foot in _feet)
            {
                StepPhased(foot, before, gait, ground);
            }
        }
        else
        {
            StandStill(gait, ground, delta, turn);
        }

        KeepTwistInLimits();
        Feet(delta);
        ShiftWeight(delta);
        Hips(gait.Bob, delta);
    }

    /// <summary>
    /// Standing still, the weight goes from foot to foot every few seconds (or back onto both), as a person's does
    /// while they wait; moving, it's back on both at once.
    /// </summary>
    private void ShiftWeight(float delta)
    {
        bool settled = !_moving && !_left.Swinging && !_right.Swinging;
        if (!settled)
        {
            _weightTarget = 0f;
            _weightIn = Mathf.Max(_weightIn, _def.WeightEveryMin_s);
        }
        else if ((_weightIn -= delta) <= 0f)
        {
            // From both feet onto either; from one, onto the other, or now and then back onto both.
            _weightTarget = _weightTarget == 0f ? (_random.NextDouble() < 0.5 ? -1f : 1f)
                : _random.NextDouble() < 0.7 ? -_weightTarget : 0f;
            _weightIn = Between(_def.WeightEveryMin_s, _def.WeightEveryMax_s);
        }

        float time = settled ? _def.WeightShift_s : 0.15f;
        _weight = Mathf.Lerp(_weight, _weightTarget, 1f - Mathf.Exp(-delta / (time * 0.35f)));
    }

    private float Between(float min, float max) => min + (max - min) * (float)_random.NextDouble();

    /// <summary>Puts both feet straight down under the body, in the standing stance.</summary>
    private void Reset(Vector3 root, GroundQuery ground)
    {
        _root = root;
        _velocity = Vector3.Zero;
        _legYaw = _facing + StandTwist();
        _started = true;
        _moving = false;
        _reachDrop = 0f;
        _stopFrom = 0f;
        foreach (FootState foot in _feet)
        {
            foot.Plant(Grounded(Neutral(foot, _legYaw, true), ground), FootYaw(foot, _legYaw));
            foot.Heel = 0f;
        }
    }

    /// <summary>Moving, the legs face the travel (backing off, away from it), as far as the hips turn from the aim.</summary>
    private float TravelTwist()
    {
        float travel = Mathf.Atan2(-_velocity.X, -_velocity.Z);
        float rel = Mathf.AngleDifference(_facing, travel);
        if (Mathf.Abs(rel) > Mathf.DegToRad(_def.BackOff_deg))
        {
            rel = Mathf.AngleDifference(0f, rel - Mathf.Pi * Mathf.Sign(rel));
        }

        float limit = Mathf.DegToRad(_def.MaxTwist_deg);
        return Mathf.Clamp(rel * _def.TravelTwist, -limit, limit);
    }

    /// <summary>Standing, the hips turn off the aim towards the trigger side: a shooter's stance, support-side foot forward.</summary>
    private float StandTwist() => -_side * Mathf.DegToRad(_def.Blade_deg) * (1f - _crouch * 0.5f);

    /// <summary>
    /// Starting off: the phase is set so the foot with further to go lifts first, or, with one already in
    /// the air, so its swing carries on.
    /// </summary>
    private void Start()
    {
        _stopFrom = 0f;
        FootState? swinging = _left.Swinging ? _left : _right.Swinging ? _right : null;
        if (swinging is not null)
        {
            _phase = Mathf.PosMod(swinging.Offset + swinging.Progress * _swing, 1f);
            return;
        }

        Vector3 ahead = _velocity.Normalized();
        float leftBehind = -(_left.Planted - _root).Dot(ahead);
        float rightBehind = -(_right.Planted - _root).Dot(ahead);
        // Just before that foot's lift-off, so it lifts this frame.
        _phase = Mathf.PosMod((leftBehind >= rightBehind ? _left.Offset : _right.Offset) - 1e-4f, 1f);
    }

    /// <summary>
    /// Walking or running: a foot lifts when its share of the cycle comes round, flies to where it should
    /// land (re-aimed every frame as the body's pace and heading change) and stays there while it's down.
    /// </summary>
    private void StepPhased(FootState foot, float before, Row gait, GroundQuery ground)
    {
        float swing = Mathf.Max(_swing, 0.05f);
        float p = Mathf.PosMod(_phase - foot.Offset, 1f);
        float was = Mathf.PosMod(before - foot.Offset, 1f);
        if (!foot.Swinging && p < swing && (was >= swing || was > p))
        {
            foot.Lift(0f, _root);
        }

        if (!foot.Swinging)
        {
            return;
        }

        float s = p < swing ? p / swing : 1f;
        s = Mathf.Max(s, foot.Progress);
        // The foot swings through relative to the body, to land ahead of its hip by a share of the ground
        // the body will cover while it's down.
        float down = (1f - swing) / Mathf.Max(_cadence, 0.05f);
        Vector3 land = Limit(foot, Neutral(foot, _legYaw, false) + _velocity * (down * gait.LandAhead)) - _root;
        foot.Swing(s, _root, land, Grounded(_root + land, ground).Y, gait.Lift, FootYaw(foot, _legYaw));
        if (s >= 1f)
        {
            foot.Land();
        }
    }

    /// <summary>
    /// Standing: feet stay where they are. One in the air finishes its step into the stance, and a foot
    /// that's drifted, or that the aim has turned away from, steps back into it, one foot at a time.
    /// </summary>
    private void StandStill(Row gait, GroundQuery ground, float delta, float turn)
    {
        float stand = _facing + StandTwist();
        // The hips follow the feet, turning towards the aim as far as they give.
        float feet = MeanAngle(_left.Yaw - FootOut(_left), _right.Yaw - FootOut(_right));
        float give = Mathf.DegToRad(_def.HipGive_deg);
        _legYaw = ApproachAngle(_legYaw, feet + Mathf.Clamp(Mathf.AngleDifference(feet, stand), -give, give), turn);

        // Just pulled up from a run, the steps into the stance are quicker and lower, and closer counts as out of place.
        bool stopping = _stopFrom > _def.StopFrom_mps && _stopAge < _def.StopWindow_s;
        float stepTime = stopping ? _def.StopStep_s : _def.SettleStep_s;
        float tolerance = stopping ? _def.StopTolerance_m : _def.SettleDistance_m;
        float lift = Mathf.Min(gait.Lift, stopping ? _def.StopLift_m : _def.SettleLift_m);
        foreach (FootState foot in _feet)
        {
            if (foot.Swinging)
            {
                Vector3 land = Neutral(foot, stand, true) - _root;
                foot.Swing(Mathf.Min(1f, foot.Progress + delta / stepTime), _root, land, Grounded(_root + land, ground).Y, lift, FootYaw(foot, stand));
                if (foot.Progress >= 1f)
                {
                    foot.Land();
                }
            }
        }

        if (_left.Swinging || _right.Swinging)
        {
            return;
        }

        // The foot furthest out of place steps, if either is out far enough.
        float twist = Mathf.Abs(Mathf.AngleDifference(feet, stand)) / Mathf.DegToRad(_def.SettleTwist_deg);
        FootState? step = null;
        float worst = 1f;
        foreach (FootState foot in _feet)
        {
            Vector3 want = Neutral(foot, stand, true);
            float off = new Vector2(foot.Planted.X - want.X, foot.Planted.Z - want.Z).Length() / tolerance;
            float yaw = Mathf.Abs(Mathf.AngleDifference(foot.Yaw, FootYaw(foot, stand))) / Mathf.DegToRad(_def.SettleTwist_deg);
            float score = Mathf.Max(off, Mathf.Max(yaw, twist) * (0.5f + 0.5f * Mathf.Min(yaw, 1f)));
            if (score > worst)
            {
                worst = score;
                step = foot;
            }
        }

        step?.Lift(0f, _root);
    }

    /// <summary>
    /// Turned further than the hips can twist from the aim (a fast turn on the spot), the legs come round
    /// with it, and the feet hurry their steps to catch up.
    /// </summary>
    private void KeepTwistInLimits()
    {
        float limit = Mathf.DegToRad(_def.MaxTwist_deg);
        float twist = Mathf.AngleDifference(_facing, _legYaw);
        if (Mathf.Abs(twist) > limit)
        {
            _legYaw = _facing + Mathf.Sign(twist) * limit;
        }
    }

    /// <summary>In the air or sliding, the feet go with the body: tucked up off the ground, or the lead leg out along the slide.</summary>
    private void Unplanted(bool sliding, GroundQuery ground, float delta)
    {
        float yaw = sliding && _velocity.LengthSquared() > 0.5f ? Mathf.Atan2(-_velocity.X, -_velocity.Z) : _legYaw;
        _legYaw = ApproachAngle(_legYaw, yaw, Mathf.DegToRad(_def.LegTurnRate_degps) * delta);
        KeepTwistInLimits();
        foreach (FootState foot in _feet)
        {
            Vector3 at = Neutral(foot, _legYaw, false);
            Vector3 forward = Forward(_legYaw);
            bool lead = foot.Side == -_side;
            if (sliding)
            {
                // The lead (support-side) leg out in front, the other folded under.
                at = Grounded(at + forward * (lead ? _def.SlideReach_m : -0.1f), ground);
            }
            else
            {
                at += Vector3.Up * _def.TuckLift_m + forward * (lead ? 0.12f : -0.1f);
            }

            foot.Float(at, FootYaw(foot, _legYaw), delta);
        }

        _moving = false;
        HipsOffset = HipsOffset.Lerp(Vector3.Zero, 1f - Mathf.Exp(-delta / 0.1f));
        HipsTwist = Mathf.AngleDifference(_facing, _legYaw);
        HipsRoll = 0f;
        _lean = Mathf.Lerp(_lean, 0f, 1f - Mathf.Exp(-delta / 0.2f));
    }

    /// <summary>
    /// The ankles for this frame: over the sole, swung up about the ball of the foot as the heel lifts late
    /// in its time down and at toe-off, and toes up a little to land heel first.
    /// </summary>
    private void Feet(float delta)
    {
        Vector3 ahead = _velocity.LengthSquared() > 1e-4f ? _velocity.Normalized() : Forward(_legYaw);
        float heelOff = Mathf.DegToRad(_def.HeelOff_deg) * Mathf.Lerp(0.75f, 1f, _run);
        float toeUp = Mathf.DegToRad(_def.ToeUp_deg) * (1f - 0.6f * _run);
        foreach (FootState foot in _feet)
        {
            float heel = 0f;
            if (foot.Swinging)
            {
                float s = foot.Progress;
                heel = s < 0.35f ? Mathf.Lerp(foot.LiftHeel, 0f, Mathf.SmoothStep(0f, 0.35f, s)) : -toeUp * Mathf.SmoothStep(0.45f, 0.9f, s);
            }
            else if (!foot.Airborne && _moving)
            {
                float behind = -(foot.Planted - _root).Dot(ahead);
                heel = heelOff * Mathf.SmoothStep(_def.HeelOffFrom_m, _def.HeelOffFrom_m + _def.HeelOffOver_m, behind);
            }

            foot.Heel = Mathf.Lerp(foot.Heel, heel, 1f - Mathf.Exp(-delta / 0.035f));
            if (!foot.Swinging)
            {
                foot.LiftHeel = Mathf.Max(foot.Heel, 0f);
            }

            Vector3 forward = Forward(foot.Yaw);
            Vector3 across = Vector3.Up.Cross(forward).Normalized();
            Vector3 ankle = foot.Position + Vector3.Up * Build.AnkleHeight;
            if (foot.Heel > 1e-3f)
            {
                Vector3 ball = foot.Position + forward * Build.BallAhead;
                ankle = ball + (ankle - ball).Rotated(across, foot.Heel);
            }
            else if (foot.Heel < -1e-3f)
            {
                Vector3 back = foot.Position - forward * Build.HeelBehind;
                ankle = back + (ankle - back).Rotated(across, foot.Heel);
            }

            foot.Ankle = ankle;
        }
    }

    /// <summary>
    /// The hips: the stride's bob (up over a walking foot, down onto a running one), a sway over the foot
    /// that's down, a turn and roll with the stride, as low as both legs need to reach, and a lean into a run.
    /// </summary>
    private void Hips(float bob, float delta)
    {
        float up = 0f, sway = 0f;
        foreach (FootState foot in _feet)
        {
            if (_moving && !foot.Swinging && !foot.Airborne)
            {
                float p = Mathf.PosMod(_phase - foot.Offset, 1f);
                float u = Mathf.Clamp((p - _swing) / Mathf.Max(1f - _swing, 0.05f), 0f, 1f);
                float over = Mathf.Sin(Mathf.Pi * u);
                up = Mathf.Max(up, over);
                sway += foot.Side * over;
            }
        }

        // Walking vaults over the foot (highest mid-stance); running sinks onto it (lowest mid-stance).
        float vertical = _moving ? Mathf.Lerp((up - 0.5f) * bob, (0.35f - up) * bob, _run) : 0f;
        // Knees a little soft, never locked straight; and down into them for a moment when pulling up.
        _soft = Mathf.Lerp(_soft, _def.SoftKnees_m + _def.RunSink_m * _run, 1f - Mathf.Exp(-delta / 0.3f));
        vertical -= _soft + _def.StopDip_m * Settling();
        Vector3 right = Right(_legYaw);
        // Over the foot that's down; standing, over the foot the weight is on.
        Vector3 offset = Vector3.Up * vertical + right * (sway * _def.Sway_m * (1f - 0.6f * _run) + _weight * _def.WeightSway_m);

        // As low as each leg needs to reach its ankle (straight legs give out a touch short of their
        // length), to a point: a foot still out of reach behind is pushing off, and comes along with the
        // body until it lifts.
        float need = 0f;
        float reach = Build.Length * _def.MaxStretch;
        foreach (FootState foot in _feet)
        {
            need = Mathf.Max(need, Need(foot, right, vertical, reach));
        }

        // Down at once (a leg mustn't fall short), back up eased.
        float rate = need > _reachDrop ? 0.004f : 0.12f;
        _reachDrop = Mathf.Lerp(_reachDrop, Mathf.Clamp(need, 0f, _def.MaxReachDrop_m), 1f - Mathf.Exp(-delta / rate));
        // The poser lowers the hips exactly as far as the legs need (up to the same most); this is for the push-off below.
        HipsOffset = offset;
        foreach (FootState foot in _feet)
        {
            if (!foot.Swinging && !foot.Airborne)
            {
                Vector3 hip = Hip(foot, right, vertical - _reachDrop);
                Vector3 to = foot.Ankle - hip;
                float height = Mathf.Abs(to.Y);
                var across = new Vector2(to.X, to.Z);
                float most = height < reach ? Mathf.Sqrt(reach * reach - height * height) : 0f;
                if (across.Length() > most + 1e-3f)
                {
                    Vector2 pull = across.Normalized() * (across.Length() - most);
                    foot.Drag(new Vector3(-pull.X, 0f, -pull.Y));
                    foot.Ankle -= new Vector3(pull.X, 0f, pull.Y);
                }
            }
        }

        float stride = _moving ? 1f : 0f;
        float swingTurn = Mathf.Sin(Mathf.Tau * _phase) * Mathf.DegToRad(_def.HipTurn_deg) * stride;
        HipsTwist = Mathf.AngleDifference(_facing, _legYaw) + swingTurn;
        // The hip over the weighted foot up, the other dropped (that knee eases).
        HipsRoll = -sway * Mathf.DegToRad(_def.HipRoll_deg) * (1f - 0.5f * _run) * stride - _weight * Mathf.DegToRad(_def.WeightRoll_deg);
        float lean = Mathf.Min(_velocity.Length() * Mathf.DegToRad(_def.LeanPerSpeed_deg), Mathf.DegToRad(_def.MaxLean_deg));
        _lean = Mathf.Lerp(_lean, _moving ? lean : 0f, 1f - Mathf.Exp(-delta / 0.2f));
    }

    /// <summary>
    /// How far into its settle a stop is (0–1): in quickly as the body pulls up, then easing out, and the
    /// more the faster it was going (nothing for a stop from a stroll).
    /// </summary>
    private float Settling()
    {
        if (_stopFrom <= _def.StopFrom_mps)
        {
            return 0f;
        }

        float hard = Mathf.Clamp((_stopFrom - _def.StopFrom_mps) / Mathf.Max(_def.StopFull_mps - _def.StopFrom_mps, 0.1f), 0f, 1f);
        float t = _stopAge;
        float shape = t < _def.StopDipIn_s ? Mathf.SmoothStep(0f, _def.StopDipIn_s, t) : Mathf.Exp(-(t - _def.StopDipIn_s) / _def.StopSettle_s);
        return hard * shape;
    }

    /// <summary>Where a hip joint is (world) with the hips <paramref name="vertical"/> off their standing height for the crouch.</summary>
    private Vector3 Hip(FootState foot, Vector3 right, float vertical) =>
        _root + Vector3.Up * (Build.HipHeight - _hipDrop + vertical) + right * (foot.Side * Build.HipHalfWidth);

    /// <summary>How far the hips must come down for this leg to reach its ankle (m, 0 if it reaches).</summary>
    private float Need(FootState foot, Vector3 right, float vertical, float reach)
    {
        Vector3 to = foot.Ankle - Hip(foot, right, vertical);
        float across = new Vector2(to.X, to.Z).Length();
        float height = across < reach ? Mathf.Sqrt(reach * reach - across * across) : 0f;
        return Mathf.Max(0f, -to.Y - height);
    }

    /// <summary>The gait at <paramref name="speed"/>, from the table (crouched, slower and lower).</summary>
    private Row GaitAt(float speed)
    {
        GaitRowDef[] rows = _def.Gaits;
        GaitRowDef a = rows[^1], b = rows[^1];
        float t = 0f;
        for (int i = 0; i + 1 < rows.Length; i++)
        {
            if (speed <= rows[i + 1].Speed_mps)
            {
                a = rows[i];
                b = rows[i + 1];
                t = Mathf.Clamp((speed - a.Speed_mps) / Mathf.Max(b.Speed_mps - a.Speed_mps, 1e-3f), 0f, 1f);
                break;
            }
        }

        return new Row(
            Mathf.Lerp(a.Cadence_hz, b.Cadence_hz, t) * Mathf.Lerp(1f, _def.CrouchCadence, _crouch),
            Mathf.Lerp(a.Swing, b.Swing, t),
            Mathf.Lerp(a.Lift_m, b.Lift_m, t) * Mathf.Lerp(1f, _def.CrouchLift, _crouch),
            Mathf.Lerp(a.LandAhead, b.LandAhead, t),
            Mathf.Lerp(a.Bob_m, b.Bob_m, t) * (1f - 0.5f * _crouch));
    }

    /// <summary>
    /// Where a foot goes (world, on the body's floor). Moving, either side of the legs' heading by the
    /// stance width (narrower running). Standing still, a shooter's stance square to the aim: the feet
    /// apart by the stance width, the support-side foot forward by the stagger.
    /// </summary>
    private Vector3 Neutral(FootState foot, float yaw, bool standing)
    {
        if (!standing)
        {
            float stride = _def.StanceWidth_m * (1f - 0.45f * _run) * (1f - _crouch) + _def.CrouchStanceWidth_m * _crouch * 0.8f;
            return _root + Right(yaw) * (foot.Side * stride * 0.5f);
        }

        float width = Mathf.Lerp(_def.StanceWidth_m, _def.CrouchStanceWidth_m, _crouch);
        float stagger = Mathf.Lerp(_def.Stagger_m, _def.CrouchStagger_m, _crouch);
        // Tucked in behind something, the front foot comes back level and both a little back, the knees out.
        float forward = (foot.Side == -_side ? stagger * 0.5f : -stagger * 0.5f) * (1f - KneesOut) - _def.KneeBack_m * KneesOut;
        return _root + Right(_facing) * (foot.Side * width * 0.5f * (1f + 0.3f * KneesOut)) + Forward(_facing) * forward;
    }

    /// <summary>A landing no further from its hip than a step can reach (for a sudden change of pace).</summary>
    private Vector3 Limit(FootState foot, Vector3 target)
    {
        Vector3 hip = _root + Right(_legYaw) * (foot.Side * Build.HipHalfWidth);
        var off = new Vector3(target.X - hip.X, 0f, target.Z - hip.Z);
        float most = Build.Length * _def.MaxStep;
        return off.Length() > most ? new Vector3(hip.X, target.Y, hip.Z) + off.Normalized() * most : target;
    }

    private float FootYaw(FootState foot, float legYaw) => legYaw + FootOut(foot);

    /// <summary>Toes turned out a little, each to its own side.</summary>
    private float FootOut(FootState foot) => -foot.Side * Mathf.DegToRad(_def.ToeOut_deg);

    /// <summary>The ground under <paramref name="at"/>, near the body's own floor (a step up or down at most); the body's floor where there's nothing near.</summary>
    private Vector3 Grounded(Vector3 at, GroundQuery ground)
    {
        var floor = new Vector3(at.X, _root.Y, at.Z);
        if (ground(floor, out float height) && height - _root.Y < _def.StepUp_m && _root.Y - height < _def.StepDown_m)
        {
            floor.Y = height;
        }

        return floor;
    }

    private static Vector3 Forward(float yaw) => new(-Mathf.Sin(yaw), 0f, -Mathf.Cos(yaw));

    private static Vector3 Right(float yaw) => new(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));

    private static float ApproachAngle(float from, float to, float step)
    {
        float d = Mathf.AngleDifference(from, to);
        return Mathf.Abs(d) <= step ? to : from + Mathf.Sign(d) * step;
    }

    private static float MeanAngle(float a, float b) => a + Mathf.AngleDifference(a, b) * 0.5f;

    /// <summary>The ground's height near <paramref name="at"/> (world, searched a little above and below it); false where there's none.</summary>
    public delegate bool GroundQuery(Vector3 at, out float height);

    /// <summary>
    /// A model's legs, measured from its rest pose (m): hip to ankle, the hips' height and half their
    /// width, the ankle's height over the sole, and how far the ball of the foot is ahead of the ankle and
    /// the heel behind it.
    /// </summary>
    public readonly record struct LegBuild(float Length, float HipHeight, float HipHalfWidth, float AnkleHeight, float BallAhead, float HeelBehind);

    private readonly record struct Row(float Cadence, float Swing, float Lift, float LandAhead, float Bob);

    /// <summary>One foot: planted where it landed, or in the air on its way to its next spot.</summary>
    private sealed class FootState
    {
        private Vector3 _from;
        private float _fromY;
        private float _fromYaw;
        private Vector3 _target;
        private float _targetYaw;

        public FootState(float side, float offset)
        {
            Side = side;
            Offset = offset;
        }

        /// <summary>−1 left, +1 right.</summary>
        public float Side { get; }

        /// <summary>Where in the gait cycle this foot lifts off.</summary>
        public float Offset { get; }

        /// <summary>Where it last landed (world, on the ground).</summary>
        public Vector3 Planted { get; private set; }

        /// <summary>Where the sole is this frame (world).</summary>
        public Vector3 Position { get; private set; }

        public float Yaw { get; private set; }

        public Vector3 Ankle { get; set; }

        public float Heel { get; set; }

        /// <summary>The heel's pitch when the foot lifted off.</summary>
        public float LiftHeel { get; set; }

        public bool Swinging { get; private set; }

        public bool Airborne { get; private set; }

        /// <summary>How far through its swing (0–1).</summary>
        public float Progress { get; private set; }

        public void Plant(Vector3 at, float yaw)
        {
            Planted = Position = _target = at;
            Yaw = _targetYaw = yaw;
            Swinging = Airborne = false;
            Progress = 0f;
        }

        /// <summary>Lifts off: the swing starts from where the foot is, relative to the body at <paramref name="root"/>.</summary>
        public void Lift(float progress, Vector3 root)
        {
            _from = Position - root;
            _fromY = Position.Y;
            _fromYaw = Yaw;
            Swinging = true;
            Airborne = false;
            Progress = progress;
        }

        /// <summary>
        /// Through the swing to <paramref name="s"/>, relative to the body (at <paramref name="root"/> now), towards
        /// <paramref name="land"/> off it on ground at <paramref name="groundY"/>: eased in and out, up in an arc that
        /// peaks a little early, turning to <paramref name="yaw"/>.
        /// </summary>
        public void Swing(float s, Vector3 root, Vector3 land, float groundY, float lift, float yaw)
        {
            Progress = s;
            float along = s * s * s * (s * (s * 6f - 15f) + 10f);
            Vector3 rel = _from.Lerp(land, along);
            _target = new Vector3(root.X + land.X, groundY, root.Z + land.Z);
            _targetYaw = yaw;
            Position = new Vector3(root.X + rel.X, Mathf.Lerp(_fromY, groundY, along) + Mathf.Sin(Mathf.Pi * Mathf.Pow(s, 0.8f)) * lift, root.Z + rel.Z);
            Yaw = _fromYaw + Mathf.AngleDifference(_fromYaw, _targetYaw) * Mathf.SmoothStep(0f, 0.8f, s);
        }

        public void Land() => Plant(_target, _targetYaw);

        /// <summary>A planted foot pulled along the ground by <paramref name="by"/> (pushing off, out of reach).</summary>
        public void Drag(Vector3 by)
        {
            Planted += by;
            Position += by;
        }

        /// <summary>Off the ground with the body (in the air, sliding): eases towards <paramref name="at"/>.</summary>
        public void Float(Vector3 at, float yaw, float delta)
        {
            Airborne = true;
            Swinging = false;
            float k = 1f - Mathf.Exp(-delta / 0.08f);
            Position = Position.Lerp(at, k);
            Planted = Position;
            Yaw += Mathf.AngleDifference(Yaw, yaw) * k;
        }
    }
}
