using System;
using Godot;

namespace Pb.Game.Player;

/// <summary>
/// Poses a rigged character to match the sim's hitbox rig, on top of its idle clip:
/// <list type="bullet">
/// <item>the legs: with <see cref="Steps"/>, each foot goes where the steps plant it in the world (two-bone
/// IK, the knee over the foot, the foot flat on the ground or rolling heel to toe), the hips bob, sway
/// and turn with the stride and drop as far as the legs need, and the chest turns back onto the aim;
/// with movement clips (<see cref="Gait"/>), the clips play over the idle with the legs turned towards
/// the travel and the feet IK'd onto the clips' footfalls;</item>
/// <item>the hips drop for a crouch;</item>
/// <item>the spine rolls with the lean, the chest and head pitch with the aim, the chest breathes and the
/// head turns as the sim's head does;</item>
/// <item>both hands go to the marker by two-bone IK;</item>
/// <item>on a ladder (<see cref="Ladder"/>), the body square to it with the hips in towards it, the balls of the feet
/// and the hands on the rungs.</item>
/// </list>
/// <see cref="CharacterModel"/> sets the targets in world space every frame; the hitboxes stay the sim's.
/// </summary>
public partial class CharacterPoser : SkeletonModifier3D
{
    private int _hips;
    private int _spineLow;
    private int _spineMid;
    private int _spineTop;
    private int _neck;
    private int _head;
    private int _leftToe;
    private int _rightToe;
    private readonly int[] _leftLeg = new int[3];
    private readonly int[] _rightLeg = new int[3];
    private readonly int[] _leftArm = new int[3];
    private readonly int[] _rightArm = new int[3];
    private int[] _spine = Array.Empty<int>();
    private FootRest _leftRest;
    private FootRest _rightRest;
    private bool _bound;

    /// <summary>The character's forward and right in world space (unit, horizontal).</summary>
    public Vector3 Forward { get; set; } = Vector3.Forward;

    public Vector3 Right { get; set; } = Vector3.Right;

    /// <summary>How far the hips sit below standing height (m).</summary>
    public float HipDrop { get; set; }

    /// <summary>The sim's lean roll (rad) and aim pitch (rad, up positive).</summary>
    public float LeanRoll { get; set; }

    public float Pitch { get; set; }

    /// <summary>A flinch: the upper body turned about this axis (world space) by its length (rad), more towards the top.</summary>
    public Vector3 Flinch { get; set; }

    /// <summary>How far the head is turned from the aim (rad, positive to the left), the sim's: the neck takes some, the head the rest.</summary>
    public float HeadYaw { get; set; }

    /// <summary>The breath this frame (rad): the chest lifts and opens by it, the neck keeping the head where it was.</summary>
    public float Breath { get; set; }

    /// <summary>Shares of the aim pitch taken by the chest and by the neck and head.</summary>
    public float ChestPitch { get; set; } = 0.3f;

    public float HeadPitch { get; set; } = 0.6f;

    /// <summary>Where the wrists go (world space), and which hand is on the trigger.</summary>
    public Vector3 TriggerHand { get; set; }

    public Vector3 SupportHand { get; set; }

    public bool RightHanded { get; set; } = true;

    /// <summary>How far the support hand is off the marker and up over the head (0–1): out of the round, walking off.</summary>
    public float SupportRaise { get; set; }

    /// <summary>How far the elbows go out to the sides, level, instead of hanging (0–1): something just ahead at elbow height.</summary>
    public float ElbowsOut { get; set; }

    public bool HandsOnMarker { get; set; } = true;

    /// <summary>The planted steps: where the feet go in the world. Null with movement clips.</summary>
    public StepGait? Steps { get; set; }

    /// <summary>With steps, the most the hips come down so the legs reach their feet (m).</summary>
    public float MaxReachDrop { get; set; } = 0.08f;

    private float _reachDrop;

    /// <summary>The movement clips and how they play (when the legs follow clips rather than steps).</summary>
    public Gait? Gait { get; set; }

    /// <summary>On a ladder: where the balls of the feet and the hands go on the rungs (world); null off one.</summary>
    public LadderGrip? Ladder { get; set; }

    /// <summary>On a ladder, the hips this far in towards the rungs and down (m).</summary>
    public float LadderHipsIn { get; set; } = 0.12f;

    public float LadderHipsDown { get; set; } = 0.06f;

    /// <summary>Finds the generator rig's bones; false when the skeleton isn't one.</summary>
    public bool Bind(Skeleton3D skeleton)
    {
        int Find(string name) => skeleton.FindBone(name);
        _hips = Find("Hips");
        _spineLow = Find("Spine02");
        _spineMid = Find("Spine01");
        _spineTop = Find("Spine");
        _neck = Find("neck");
        _head = Find("Head");
        _leftToe = Find("LeftToeBase");
        _rightToe = Find("RightToeBase");
        Fill(_leftLeg, Find("LeftUpLeg"), Find("LeftLeg"), Find("LeftFoot"));
        Fill(_rightLeg, Find("RightUpLeg"), Find("RightLeg"), Find("RightFoot"));
        Fill(_leftArm, Find("LeftArm"), Find("LeftForeArm"), Find("LeftHand"));
        Fill(_rightArm, Find("RightArm"), Find("RightForeArm"), Find("RightHand"));
        _bound = _hips >= 0 && _spineLow >= 0 && _spineMid >= 0 && _spineTop >= 0 && _neck >= 0 && _head >= 0 &&
                 Array.TrueForAll(_leftLeg, b => b >= 0) && Array.TrueForAll(_rightLeg, b => b >= 0) &&
                 Array.TrueForAll(_leftArm, b => b >= 0) && Array.TrueForAll(_rightArm, b => b >= 0);
        if (_bound)
        {
            _spine = new[] { _spineLow, _spineMid, _spineTop };
            _leftRest = FootRest.Of(skeleton, _leftLeg[2], _leftToe);
            _rightRest = FootRest.Of(skeleton, _rightLeg[2], _rightToe);
        }

        return _bound;
    }

    /// <summary>
    /// The legs' build in metres, from the rest pose, given the skeleton's transform in the model's space
    /// (Y up, facing −Z): hip to ankle, the hips' height and half their width, the ankle over the sole,
    /// and the ball of the foot ahead of the ankle and the heel behind it.
    /// </summary>
    public StepGait.LegBuild Measure(Skeleton3D skeleton, Transform3D skeletonInModel)
    {
        Vector3 At(int bone) => skeletonInModel * skeleton.GetBoneGlobalRest(bone).Origin;
        Vector3 hip = At(_leftLeg[0]), knee = At(_leftLeg[1]), ankle = At(_leftLeg[2]);
        Vector3 otherHip = At(_rightLeg[0]), otherAnkle = At(_rightLeg[2]);
        float length = (hip.DistanceTo(knee) + knee.DistanceTo(ankle) + otherHip.DistanceTo(At(_rightLeg[1])) + At(_rightLeg[1]).DistanceTo(otherAnkle)) * 0.5f;
        float ankleHeight = Mathf.Max(0.03f, (ankle.Y + otherAnkle.Y) * 0.5f);
        float ball = _leftToe >= 0 ? Mathf.Max(0.1f, -(At(_leftToe).Z - ankle.Z)) : 0.13f;
        return new StepGait.LegBuild(length, (hip.Y + otherHip.Y) * 0.5f, Mathf.Abs(hip.X - otherHip.X) * 0.5f, ankleHeight, ball,
            Mathf.Max(0.03f, ball * 0.4f));
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        Skeleton3D skeleton = GetSkeleton();
        if (!_bound || skeleton is null)
        {
            return;
        }

        // Skeleton space has its own scale (the rig is in centimetres): convert world vectors once.
        Transform3D toSkeleton = skeleton.GlobalTransform.AffineInverse();
        Vector3 up = toSkeleton.Basis * Vector3.Up;
        Vector3 forward = toSkeleton.Basis * Forward;
        Vector3 right = toSkeleton.Basis * Right;

        if (Ladder is { } grip)
        {
            LadderBody(skeleton, toSkeleton, grip, up, forward, right);
            return;
        }

        if (Steps is { HasBuild: true } steps)
        {
            StepHips(skeleton, toSkeleton, steps, up, forward, right);
        }
        else
        {
            ClipLegs(skeleton, up, forward, out Vector3 leftFoot, out Vector3 rightFoot, out Vector3 leftKnee, out Vector3 rightKnee, out float clips);
            UpperBody(skeleton, up, forward, right, toSkeleton);
            SolveTwoBone(skeleton, _leftLeg, leftFoot, forward.Normalized().Lerp(leftKnee.Normalized(), clips), keepEnd: true);
            SolveTwoBone(skeleton, _rightLeg, rightFoot, forward.Normalized().Lerp(rightKnee.Normalized(), clips), keepEnd: true);
            Hands(skeleton, toSkeleton, up, right);
            return;
        }

        UpperBody(skeleton, up, forward, right, toSkeleton);
        StepLeg(skeleton, toSkeleton, _leftLeg, _leftToe, _leftRest, steps.LeftAnkle, steps.LeftForward, steps.LeftHeel, -1f, up, steps.KneesOut);
        StepLeg(skeleton, toSkeleton, _rightLeg, _rightToe, _rightRest, steps.RightAnkle, steps.RightForward, steps.RightHeel, 1f, up, steps.KneesOut);
        Hands(skeleton, toSkeleton, up, right);
        Check(skeleton, 0, _leftLeg[2], _leftToe, steps.LeftAnkle, steps.LeftPlanted, (float)delta);
        Check(skeleton, 1, _rightLeg[2], _rightToe, steps.RightAnkle, steps.RightPlanted, (float)delta);
    }

    // Checks on the steps as posed (for the gait demo): how far a planted foot slid while it was down (by
    // whichever of its ankle and its ball moved less: the foot rolls over one or the other), over how long,
    // and the furthest an ankle ended up from where the steps put it.
    private readonly Vector3[] _lastAnkle = new Vector3[2];
    private readonly Vector3[] _lastBall = new Vector3[2];
    private readonly bool[] _wasPlanted = new bool[2];

    public float CheckSlide { get; set; }

    public float CheckDown { get; set; }

    public float CheckMiss { get; set; }

    private void Check(Skeleton3D skeleton, int i, int bone, int toe, Vector3 target, bool planted, float delta)
    {
        Vector3 ankle = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(bone).Origin;
        Vector3 ball = toe >= 0 ? skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(toe).Origin : ankle;
        CheckMiss = Mathf.Max(CheckMiss, ankle.DistanceTo(target));
        if (planted && _wasPlanted[i])
        {
            float byAnkle = new Vector2(ankle.X - _lastAnkle[i].X, ankle.Z - _lastAnkle[i].Z).Length();
            float byBall = new Vector2(ball.X - _lastBall[i].X, ball.Z - _lastBall[i].Z).Length();
            CheckSlide += Mathf.Min(byAnkle, byBall);
            CheckDown += delta;
        }

        _wasPlanted[i] = planted;
        _lastAnkle[i] = ankle;
        _lastBall[i] = ball;
    }

    /// <summary>
    /// The hips with planted steps: where they stand (the idle clip's own shift and turn of them replaced),
    /// down for the crouch, the stride's bob and sway, turned with the legs and rolled with the stride; the
    /// spine turned back so the chest faces the aim, and leaning into a run.
    /// </summary>
    private void StepHips(Skeleton3D skeleton, Transform3D toSkeleton, StepGait steps, Vector3 up, Vector3 forward, Vector3 right)
    {
        Vector3 axis = up.Normalized();
        // How far the idle clip has turned the hips and the chest about the vertical (some idles stand bladed).
        float hipsIdle = YawFromRest(skeleton, _hips, axis);
        float chestIdle = YawFromRest(skeleton, _spineTop, axis);

        // The hips from their rest pose: the steps' offset, the crouch, the legs' heading and the stride's roll.
        Transform3D rest = skeleton.GetBoneGlobalRest(_hips);
        Vector3 along = forward.Normalized();
        var hips = new Transform3D(new Basis(along, steps.HipsRoll) * new Basis(axis, steps.HipsTwist) * rest.Basis,
            rest.Origin + toSkeleton.Basis * steps.HipsOffset - up * HipDrop);
        skeleton.SetBoneGlobalPose(_hips, hips);

        // Then down as far as both legs need to reach their ankles, at most so far: at once (the need grows
        // smoothly as a foot falls behind), eased back up so it doesn't bounce.
        float need = Mathf.Max(Need(skeleton, toSkeleton, _leftLeg, steps.LeftAnkle, axis), Need(skeleton, toSkeleton, _rightLeg, steps.RightAnkle, axis));
        float unit = up.Length();
        need = Mathf.Min(need / unit, MaxReachDrop);
        _reachDrop = Mathf.Lerp(_reachDrop, need, 1f - Mathf.Exp(-(float)GetProcessDeltaTimeSafe() / (need > _reachDrop ? 0.004f : 0.1f)));
        if (_reachDrop > 1e-4f)
        {
            hips.Origin -= up * _reachDrop;
            skeleton.SetBoneGlobalPose(_hips, hips);
        }

        // The idle's hips were turned by hipsIdle and its chest by chestIdle; replacing the hips' turn with the
        // legs' leaves the chest that far round, so the spine turns it back onto the aim.
        float turn = steps.HipsTwist - hipsIdle;
        float back = -(chestIdle + turn);
        Rotate(skeleton, _spineLow, axis, back * 0.4f);
        Rotate(skeleton, _spineMid, axis, back * 0.3f);
        Rotate(skeleton, _spineTop, axis, back * 0.3f);

        // The stride's roll of the hips, taken back by the spine.
        if (!Mathf.IsZeroApprox(steps.HipsRoll))
        {
            Rotate(skeleton, _spineLow, along, -steps.HipsRoll);
        }

        // Into a run the body leans forward from the waist; the neck keeps the head up.
        if (steps.Lean > 1e-3f)
        {
            Vector3 pitchAxis = right.Normalized();
            Rotate(skeleton, _spineLow, pitchAxis, -steps.Lean);
            Rotate(skeleton, _neck, pitchAxis, steps.Lean * 0.6f);
        }
    }

    /// <summary>
    /// On a ladder: the hips in towards the rungs and down, square to them (the idle's turn of the hips and the chest
    /// taken out), the head looking where the sim's does, the balls of the feet on their rungs with the knees forward
    /// and a little out, and the hands on theirs with the elbows down and out.
    /// </summary>
    private void LadderBody(Skeleton3D skeleton, Transform3D toSkeleton, LadderGrip grip, Vector3 up, Vector3 forward, Vector3 right)
    {
        Vector3 axis = up.Normalized();
        float hipsIdle = YawFromRest(skeleton, _hips, axis);
        float chestIdle = YawFromRest(skeleton, _spineTop, axis);
        Transform3D rest = skeleton.GetBoneGlobalRest(_hips);
        skeleton.SetBoneGlobalPose(_hips, new Transform3D(rest.Basis, rest.Origin + toSkeleton.Basis * (grip.Into * LadderHipsIn) - up * LadderHipsDown));
        float back = -(chestIdle - hipsIdle);
        Rotate(skeleton, _spineLow, axis, back * 0.4f);
        Rotate(skeleton, _spineMid, axis, back * 0.3f);
        Rotate(skeleton, _spineTop, axis, back * 0.3f);
        UpperBody(skeleton, up, forward, right, toSkeleton);

        float ballAhead = Steps is { HasBuild: true } s ? s.Build.BallAhead : 0.13f;
        float ankleHeight = Steps is { HasBuild: true } t ? t.Build.AnkleHeight : 0.08f;
        Vector3 lift = Vector3.Up * ankleHeight;
        StepLeg(skeleton, toSkeleton, _leftLeg, _leftToe, _leftRest, grip.LeftFoot - grip.Into * ballAhead + lift, grip.Into, 0f, -1f, up, 0.3f);
        StepLeg(skeleton, toSkeleton, _rightLeg, _rightToe, _rightRest, grip.RightFoot - grip.Into * ballAhead + lift, grip.Into, 0f, 1f, up, 0.3f);

        Vector3 down = -up;
        Vector3 side = right.Normalized() * up.Length();
        SolveTwoBone(skeleton, _leftArm, toSkeleton * grip.LeftHand, down * 0.6f - side * 0.5f, keepEnd: false);
        SolveTwoBone(skeleton, _rightArm, toSkeleton * grip.RightHand, down * 0.6f + side * 0.5f, keepEnd: false);
    }

    /// <summary>How far the hips must come down (skeleton units) for <paramref name="chain"/> to reach <paramref name="ankle"/> (world).</summary>
    private static float Need(Skeleton3D skeleton, Transform3D toSkeleton, int[] chain, Vector3 ankle, Vector3 axis)
    {
        Vector3 hip = skeleton.GetBoneGlobalPose(chain[0]).Origin;
        float reach = (skeleton.GetBoneRest(chain[1]).Origin.Length() + skeleton.GetBoneRest(chain[2]).Origin.Length()) * 0.998f;
        Vector3 to = toSkeleton * ankle - hip;
        float below = -to.Dot(axis);
        float across = (to + axis * below).Length();
        return across < reach ? Mathf.Max(0f, below - Mathf.Sqrt(reach * reach - across * across)) : below;
    }

    private float GetProcessDeltaTimeSafe() => Mathf.Max((float)GetProcessDeltaTime(), 1e-3f);

    /// <summary>How far <paramref name="bone"/> is turned from its rest pose about <paramref name="axis"/> (rad, skeleton space).</summary>
    private static float YawFromRest(Skeleton3D skeleton, int bone, Vector3 axis)
    {
        Basis turn = skeleton.GetBoneGlobalPose(bone).Basis * skeleton.GetBoneGlobalRest(bone).Basis.Inverse();
        Vector3 reference = Mathf.Abs(axis.Dot(Vector3.Back)) < 0.9f ? Vector3.Back : Vector3.Right;
        reference = (reference - axis * reference.Dot(axis)).Normalized();
        Vector3 turned = turn * reference;
        turned -= axis * turned.Dot(axis);
        return turned.LengthSquared() < 1e-8f ? 0f : reference.SignedAngleTo(turned.Normalized(), axis);
    }

    /// <summary>
    /// One leg to its planted-step target: the ankle where the steps put it, the knee bent over the foot
    /// and a little out (out to the side, <paramref name="kneesOut"/>, tucked in behind something), the foot
    /// along its heading and pitched for the heel or toe, the toes staying on the ground while the heel is up.
    /// </summary>
    private void StepLeg(Skeleton3D skeleton, Transform3D toSkeleton, int[] chain, int toe, FootRest rest, Vector3 ankle, Vector3 heading,
        float heel, float side, Vector3 up, float kneesOut)
    {
        Vector3 target = toSkeleton * ankle;
        Vector3 ahead = (toSkeleton.Basis * heading).Normalized();
        Vector3 upright = up.Normalized();
        Vector3 outward = upright.Cross(ahead).Normalized() * -side;
        SolveTwoBone(skeleton, chain, target, ahead * (1f - 0.75f * kneesOut) + outward * (0.12f + 0.9f * kneesOut), keepEnd: false);

        // The foot flat on the ground along its heading, then pitched about its own across axis.
        Basis flat = LookFrame(ahead, upright) * rest.Frame.Inverse();
        Vector3 across = upright.Cross(ahead).Normalized();
        Basis pitched = new Basis(across, heel) * flat;
        Transform3D foot = skeleton.GetBoneGlobalPose(chain[2]);
        foot.Basis = pitched * rest.Basis;
        skeleton.SetBoneGlobalPose(chain[2], foot);
        if (toe >= 0)
        {
            // With the heel up the toes bend to stay flat on the ground; otherwise they follow the foot.
            Transform3D toes = skeleton.GetBoneGlobalPose(toe);
            Basis turn = heel > 0f ? flat : pitched;
            toes.Basis = turn * rest.ToeBasis;
            skeleton.SetBoneGlobalPose(toe, toes);
        }
    }

    /// <summary>
    /// With movement clips: the clips over the idle, the legs turned towards the travel and the chest back
    /// onto the aim; then where the clips put the feet and which way they bend the knees, and the hips down
    /// for a crouch (less what the clips already lower them).
    /// </summary>
    private void ClipLegs(Skeleton3D skeleton, Vector3 up, Vector3 forward, out Vector3 leftFoot, out Vector3 rightFoot, out Vector3 leftKnee,
        out Vector3 rightKnee, out float clips)
    {
        clips = 0f;
        if (Gait is { Walk: not null, MoveWeight: > 0f } gait)
        {
            clips = gait.MoveWeight;
            PlayClips(skeleton, gait);
            if (!Mathf.IsZeroApprox(gait.LegYaw))
            {
                Vector3 axis = up.Normalized();
                Rotate(skeleton, _hips, axis, gait.LegYaw);
                Rotate(skeleton, _spineLow, axis, -gait.LegYaw * 0.4f);
                Rotate(skeleton, _spineMid, axis, -gait.LegYaw * 0.3f);
                Rotate(skeleton, _spineTop, axis, -gait.LegYaw * 0.3f);
            }
        }

        leftFoot = skeleton.GetBoneGlobalPose(_leftLeg[2]).Origin;
        rightFoot = skeleton.GetBoneGlobalPose(_rightLeg[2]).Origin;
        leftKnee = skeleton.GetBoneGlobalPose(_leftLeg[1]).Origin - skeleton.GetBoneGlobalPose(_leftLeg[0]).Origin;
        rightKnee = skeleton.GetBoneGlobalPose(_rightLeg[1]).Origin - skeleton.GetBoneGlobalPose(_rightLeg[0]).Origin;
        if (clips <= 0f)
        {
            // Standing on the idle: the feet stay where the rest pose puts them.
            leftFoot = skeleton.GetBoneGlobalRest(_leftLeg[2]).Origin;
            rightFoot = skeleton.GetBoneGlobalRest(_rightLeg[2]).Origin;
        }

        Transform3D hips = skeleton.GetBoneGlobalPose(_hips);
        hips.Origin -= up * Mathf.Max(0f, HipDrop - (Gait?.ClipHipsDrop ?? 0f));
        skeleton.SetBoneGlobalPose(_hips, hips);
    }

    /// <summary>The lean rolls the spine about the body's long axis, a third per bone, like the hitboxes roll about the hips; the aim tips the chest and head; a flinch snaps the upper body away.</summary>
    private void UpperBody(Skeleton3D skeleton, Vector3 up, Vector3 forward, Vector3 right, Transform3D toSkeleton)
    {
        Vector3 back = -forward.Normalized();
        foreach (int bone in _spine)
        {
            Rotate(skeleton, bone, back, -LeanRoll / 3f);
        }

        Vector3 pitchAxis = right.Normalized();
        Rotate(skeleton, _spineTop, pitchAxis, Pitch * ChestPitch + Breath);
        Rotate(skeleton, _neck, pitchAxis, Pitch * HeadPitch * 0.5f - Breath);
        Rotate(skeleton, _head, pitchAxis, Pitch * HeadPitch * 0.5f);
        if (!Mathf.IsZeroApprox(HeadYaw))
        {
            Vector3 axis = up.Normalized();
            Rotate(skeleton, _neck, axis, HeadYaw * 0.4f);
            Rotate(skeleton, _head, axis, HeadYaw * 0.6f);
        }
        if (Flinch.LengthSquared() > 1e-6f)
        {
            Vector3 flinchAxis = (toSkeleton.Basis * Flinch).Normalized();
            float angle = Flinch.Length();
            Rotate(skeleton, _spineMid, flinchAxis, angle * 0.35f);
            Rotate(skeleton, _spineTop, flinchAxis, angle * 0.4f);
            Rotate(skeleton, _head, flinchAxis, angle * 0.6f);
        }
    }

    private void Hands(Skeleton3D skeleton, Transform3D toSkeleton, Vector3 up, Vector3 right)
    {
        if (!HandsOnMarker)
        {
            return;
        }

        Vector3 trigger = toSkeleton * TriggerHand;
        Vector3 support = toSkeleton * SupportHand;
        // Elbows hang down and out; an arm raised over the head bends its elbow out to the side and a little forward.
        Vector3 down = -up;
        Vector3 ahead = (toSkeleton.Basis * Forward).Normalized() * right.Length();
        Vector3 rightBend = down * 0.6f + right * 0.4f, leftBend = down * 0.6f - right * 0.4f;
        if (ElbowsOut > 0f)
        {
            // Something just ahead at elbow height: the elbows come in and back along the body, out to the sides, clear of it.
            Vector3 back = -ahead;
            rightBend = rightBend.Lerp(right * 0.6f + back * 0.75f + down * 0.25f, ElbowsOut);
            leftBend = leftBend.Lerp(-right * 0.6f + back * 0.75f + down * 0.25f, ElbowsOut);
        }
        if (SupportRaise > 0f)
        {
            Vector3 raised = (RightHanded ? -right : right) * 0.9f + ahead * 0.3f + down * 0.15f;
            if (RightHanded)
            {
                leftBend = leftBend.Lerp(raised, SupportRaise);
            }
            else
            {
                rightBend = rightBend.Lerp(raised, SupportRaise);
            }
        }

        SolveTwoBone(skeleton, _rightArm, RightHanded ? trigger : support, rightBend, keepEnd: false);
        SolveTwoBone(skeleton, _leftArm, RightHanded ? support : trigger, leftBend, keepEnd: false);
    }

    /// <summary>Sets every bone to the gait's blend of its clips, over the idle as far as the gait moves.</summary>
    private void PlayClips(Skeleton3D skeleton, Gait gait)
    {
        MoveClip walk = gait.Walk!;
        MoveClip? run = gait.RunWeight > 0f ? gait.Run : null;
        MoveClip? crouch = gait.CrouchWeight > 0f ? gait.Crouch : null;
        MoveClip.Cursor w = walk.At(gait.Phase);
        MoveClip.Cursor r = run?.At(gait.Phase) ?? default;
        MoveClip.Cursor c = crouch?.At(gait.Phase) ?? default;
        int bones = Mathf.Min(skeleton.GetBoneCount(), walk.Bones);
        for (int b = 0; b < bones; b++)
        {
            Quaternion q = walk.Rotation(w, b);
            if (run is not null)
            {
                q = q.Slerp(run.Rotation(r, b), gait.RunWeight);
            }

            if (crouch is not null)
            {
                q = q.Slerp(crouch.Rotation(c, b), gait.CrouchWeight);
            }

            skeleton.SetBonePoseRotation(b, skeleton.GetBonePoseRotation(b).Slerp(q, gait.MoveWeight));
        }

        Vector3 hips = walk.Hips(w);
        if (run is not null)
        {
            hips = hips.Lerp(run.Hips(r), gait.RunWeight);
        }

        if (crouch is not null)
        {
            hips = hips.Lerp(crouch.Hips(c), gait.CrouchWeight);
        }

        skeleton.SetBonePosePosition(_hips, skeleton.GetBonePosePosition(_hips).Lerp(hips, gait.MoveWeight));
    }

    private static void Fill(int[] chain, int a, int b, int c)
    {
        chain[0] = a;
        chain[1] = b;
        chain[2] = c;
    }

    private static void Rotate(Skeleton3D skeleton, int bone, Vector3 axis, float angle)
    {
        if (Mathf.IsZeroApprox(angle))
        {
            return;
        }

        Transform3D pose = skeleton.GetBoneGlobalPose(bone);
        pose.Basis = new Basis(axis, angle) * pose.Basis;
        skeleton.SetBoneGlobalPose(bone, pose);
    }

    /// <summary>A frame looking along <paramref name="forward"/> with <paramref name="up"/> up (−Z forward, as Godot's).</summary>
    private static Basis LookFrame(Vector3 forward, Vector3 up) => Basis.LookingAt(forward, up);

    /// <summary>
    /// Two-bone IK in skeleton space: bends the chain so its end reaches <paramref name="target"/> (or
    /// as near as the bones allow), with the middle joint pushed towards <paramref name="bend"/>.
    /// </summary>
    private static void SolveTwoBone(Skeleton3D skeleton, int[] chain, Vector3 target, Vector3 bend, bool keepEnd)
    {
        Transform3D upper = skeleton.GetBoneGlobalPose(chain[0]);
        Transform3D middle = skeleton.GetBoneGlobalPose(chain[1]);
        Transform3D end = skeleton.GetBoneGlobalPose(chain[2]);
        Vector3 a = upper.Origin, b = middle.Origin, c = end.Origin;
        float l1 = a.DistanceTo(b), l2 = b.DistanceTo(c);
        Vector3 toTarget = target - a;
        if (l1 < 1e-4f || l2 < 1e-4f || toTarget.LengthSquared() < 1e-8f)
        {
            return;
        }

        float reach = Mathf.Clamp(toTarget.Length(), Mathf.Abs(l1 - l2) + 1e-3f, (l1 + l2) * 0.999f);
        Vector3 dir = toTarget.Normalized();
        Vector3 side = bend - dir * bend.Dot(dir);
        if (side.LengthSquared() < 1e-8f)
        {
            side = (b - a) - dir * (b - a).Dot(dir);
        }

        side = side.Normalized();
        float cos = Mathf.Clamp((l1 * l1 + reach * reach - l2 * l2) / (2f * l1 * reach), -1f, 1f);
        Vector3 joint = a + dir * (l1 * cos) + side * (l1 * Mathf.Sqrt(1f - cos * cos));
        Vector3 tip = a + dir * reach;

        Basis endBasis = end.Basis;
        upper.Basis = new Basis(Arc((b - a).Normalized(), (joint - a).Normalized())) * upper.Basis;
        skeleton.SetBoneGlobalPose(chain[0], upper);

        middle = skeleton.GetBoneGlobalPose(chain[1]);
        end = skeleton.GetBoneGlobalPose(chain[2]);
        middle.Basis = new Basis(Arc((end.Origin - middle.Origin).Normalized(), (tip - middle.Origin).Normalized())) * middle.Basis;
        skeleton.SetBoneGlobalPose(chain[1], middle);

        if (keepEnd)
        {
            end = skeleton.GetBoneGlobalPose(chain[2]);
            end.Basis = endBasis;
            skeleton.SetBoneGlobalPose(chain[2], end);
        }
    }

    /// <summary>The shortest rotation taking unit vector <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static Quaternion Arc(Vector3 from, Vector3 to)
    {
        float dot = Mathf.Clamp(from.Dot(to), -1f, 1f);
        if (dot > 0.99999f)
        {
            return Quaternion.Identity;
        }

        Vector3 axis = from.Cross(to);
        if (axis.LengthSquared() < 1e-10f)
        {
            axis = Mathf.Abs(from.X) < 0.9f ? from.Cross(Vector3.Right) : from.Cross(Vector3.Up);
        }

        return new Quaternion(axis.Normalized(), Mathf.Acos(dot));
    }

    /// <summary>On a ladder, where the balls of the feet and the hands go (world), and which way is into the ladder.</summary>
    public readonly record struct LadderGrip(Vector3 LeftFoot, Vector3 RightFoot, Vector3 LeftHand, Vector3 RightHand, Vector3 Into);

    /// <summary>
    /// A foot at rest, in skeleton space: its bone's and its toes' rest orientations, and the frame of the
    /// foot itself (along the ground from the ankle towards the toes, skeleton up), so a foot can be turned
    /// to any heading and pitch and keep its shape.
    /// </summary>
    private readonly record struct FootRest(Basis Basis, Basis ToeBasis, Basis Frame)
    {
        public static FootRest Of(Skeleton3D skeleton, int foot, int toe)
        {
            Transform3D rest = skeleton.GetBoneGlobalRest(foot);
            Vector3 up = Vector3.Up;
            Vector3 along = toe >= 0 ? skeleton.GetBoneGlobalRest(toe).Origin - rest.Origin : Vector3.Back;
            along -= up * along.Dot(up);
            if (along.LengthSquared() < 1e-8f)
            {
                along = Vector3.Back;
            }

            return new FootRest(rest.Basis, toe >= 0 ? skeleton.GetBoneGlobalRest(toe).Basis : Basis.Identity, LookFrame(along.Normalized(), up));
        }
    }
}
