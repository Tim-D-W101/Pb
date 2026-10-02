using System;
using Godot;

namespace Pb.Game.Player;

/// <summary>
/// Poses a rigged character to match the sim's hitbox rig, on top of its idle clip:
/// <list type="bullet">
/// <item>movement clips play over the idle as the <see cref="Gait"/> says, the legs turned towards the
/// travel and the chest turned back onto the aim;</item>
/// <item>the hips drop for a crouch;</item>
/// <item>the feet go where the clips put them, or stay planted, or step while the body moves (without
/// clips), by two-bone IK, so they stay on the ground however low the hips go;</item>
/// <item>the spine rolls with the lean, and the chest and head pitch with the aim;</item>
/// <item>both hands go to the marker by two-bone IK.</item>
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
    private readonly int[] _leftLeg = new int[3];
    private readonly int[] _rightLeg = new int[3];
    private readonly int[] _leftArm = new int[3];
    private readonly int[] _rightArm = new int[3];
    private int[] _spine = Array.Empty<int>();
    private Vector3 _leftAnkleRest;
    private Vector3 _rightAnkleRest;
    private bool _bound;

    /// <summary>The character's forward and right in world space (unit, horizontal).</summary>
    public Vector3 Forward { get; set; } = Vector3.Forward;

    public Vector3 Right { get; set; } = Vector3.Right;

    /// <summary>How far the hips sit below standing height (m).</summary>
    public float HipDrop { get; set; }

    /// <summary>The sim's lean roll (rad) and aim pitch (rad, up positive).</summary>
    public float LeanRoll { get; set; }

    public float Pitch { get; set; }

    /// <summary>Shares of the aim pitch taken by the chest and by the neck and head.</summary>
    public float ChestPitch { get; set; } = 0.3f;

    public float HeadPitch { get; set; } = 0.6f;

    /// <summary>Where the wrists go (world space), and which hand is on the trigger.</summary>
    public Vector3 TriggerHand { get; set; }

    public Vector3 SupportHand { get; set; }

    public bool RightHanded { get; set; } = true;

    public bool HandsOnMarker { get; set; } = true;

    /// <summary>The movement clips and how they play, or the steps without them.</summary>
    public Gait? Gait { get; set; }

    /// <summary>Steps without clips: stride and foot lift (m).</summary>
    public float Stride { get; set; } = 0.7f;

    public float StepLift { get; set; } = 0.12f;

    /// <summary>How far the hips bob down at each footfall of a full step (m).</summary>
    public float HipBob { get; set; } = 0.025f;

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
            _leftAnkleRest = skeleton.GetBoneGlobalRest(_leftLeg[2]).Origin;
            _rightAnkleRest = skeleton.GetBoneGlobalRest(_rightLeg[2]).Origin;
        }

        return _bound;
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

        // Movement clips over the idle, the legs turned towards the travel and the chest back onto the aim.
        float clips = 0f;
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

        // Where the clips put the feet, and which way they bend the knees, before the hips move.
        Vector3 leftFoot = skeleton.GetBoneGlobalPose(_leftLeg[2]).Origin;
        Vector3 rightFoot = skeleton.GetBoneGlobalPose(_rightLeg[2]).Origin;
        Vector3 leftKnee = skeleton.GetBoneGlobalPose(_leftLeg[1]).Origin - skeleton.GetBoneGlobalPose(_leftLeg[0]).Origin;
        Vector3 rightKnee = skeleton.GetBoneGlobalPose(_rightLeg[1]).Origin - skeleton.GetBoneGlobalPose(_rightLeg[0]).Origin;

        // Hips down for a crouch (less what the clips already lower them), with a little bob while stepping.
        float stepPhase = Gait?.StepPhase ?? 0f;
        float stepAmount = Gait?.StepAmount ?? 0f;
        Transform3D hips = skeleton.GetBoneGlobalPose(_hips);
        float bob = stepAmount * HipBob * (1f - Mathf.Cos(stepPhase * 2f)) * 0.5f;
        hips.Origin -= up * (Mathf.Max(0f, HipDrop - (Gait?.ClipHipsDrop ?? 0f)) + bob);
        skeleton.SetBoneGlobalPose(_hips, hips);

        // The lean rolls the spine about the body's long axis, a third per bone, like the hitboxes
        // roll about the hips; the aim tips the chest and head.
        Vector3 back = -forward.Normalized();
        foreach (int bone in _spine)
        {
            Rotate(skeleton, bone, back, -LeanRoll / 3f);
        }

        Vector3 pitchAxis = right.Normalized();
        Rotate(skeleton, _spineTop, pitchAxis, Pitch * ChestPitch);
        Rotate(skeleton, _neck, pitchAxis, Pitch * HeadPitch * 0.5f);
        Rotate(skeleton, _head, pitchAxis, Pitch * HeadPitch * 0.5f);

        // Feet: where the clips put them; without clips, planted where they stand in the rest pose, or
        // swinging through a step along the travel.
        float swing = Stride * 0.5f * stepAmount;
        float lift = StepLift * stepAmount;
        Vector3 along = toSkeleton.Basis * (Gait?.StepDirection ?? Forward);
        Vector3 Step(float phase) => along * (swing * Mathf.Sin(phase)) + up * (lift * Mathf.Max(0f, Mathf.Cos(phase)));
        Vector3 knees = forward.Normalized();
        SolveTwoBone(skeleton, _leftLeg, (_leftAnkleRest + Step(stepPhase)).Lerp(leftFoot, clips),
            knees.Lerp(leftKnee.Normalized(), clips), keepEnd: true);
        SolveTwoBone(skeleton, _rightLeg, (_rightAnkleRest + Step(stepPhase + Mathf.Pi)).Lerp(rightFoot, clips),
            knees.Lerp(rightKnee.Normalized(), clips), keepEnd: true);

        if (HandsOnMarker)
        {
            Vector3 trigger = toSkeleton * TriggerHand;
            Vector3 support = toSkeleton * SupportHand;
            // Elbows hang down and out.
            Vector3 down = -up;
            SolveTwoBone(skeleton, _rightArm, RightHanded ? trigger : support, down * 0.6f + right * 0.4f, keepEnd: false);
            SolveTwoBone(skeleton, _leftArm, RightHanded ? support : trigger, down * 0.6f - right * 0.4f, keepEnd: false);
        }
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
}
