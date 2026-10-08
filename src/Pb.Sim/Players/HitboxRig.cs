using System.Numerics;
using Pb.Sim.Collision;

namespace Pb.Sim.Players;

/// <summary>A hitbox defined from the eye: centre in view space (x right, y up, z forward) and half extents.</summary>
public readonly record struct PartBox(Vector3 Centre, Vector3 HalfExtents);

/// <summary>Player hitbox rig and elimination rules (SI), from hitboxes.jsonc.</summary>
public sealed class HitboxParams
{
    public required SurfaceId Surface { get; init; }

    public required float LegsWidth { get; init; }

    public required float LegsDepth { get; init; }

    public required float TorsoWidth { get; init; }

    public required float TorsoDepth { get; init; }

    public required float TorsoTopBelowEye { get; init; }

    public required PartBox Head { get; init; }

    public required PartBox Mask { get; init; }

    public required PartBox Arms { get; init; }

    public required PartBox Marker { get; init; }

    public required PartBox Loader { get; init; }

    public required PartBox Tank { get; init; }

    public required float EliminatedRaise { get; init; }

    /// <summary>Pitch of the raised marker of an eliminated player (rad).</summary>
    public required float EliminatedPitch { get; init; }

    /// <summary>
    /// Near a wall the gear comes up off it instead of poking through: the most it pitches up about the
    /// back of the marker (rad), how fast (rad/s), the steps the clearance check tries (rad) and the
    /// barrel's radius for that check (m).
    /// </summary>
    public float TuckMax { get; init; }

    public float TuckRate { get; init; }

    public float TuckStep { get; init; } = 0.17f;

    public float TuckBarrelRadius { get; init; }

    /// <summary>
    /// On a ladder: how far the arms reach up (rad), and where the gear hangs on the back: the marker's back end below
    /// and behind the eye (m), its barrel pitched down and turned across the back (rad).
    /// </summary>
    public float ClimbArmsPitch { get; init; }

    public float SlungBelowEye { get; init; }

    public float SlungBehind { get; init; }

    public float SlungPitch { get; init; }

    public float SlungRoll { get; init; }

    /// <summary>Indexed by <see cref="HitboxPart"/>.</summary>
    public required bool[] LethalParts { get; init; }

    public required bool BallsInFlightCount { get; init; }

    public required float MaskSprayRadius { get; init; }

    public bool IsLethal(HitboxPart part) => (int)part < LethalParts.Length && LethalParts[(int)part];
}

/// <summary>
/// Everything the rig needs from a player, recorded every tick for the hitbox history. <see cref="Tuck"/>
/// is how far the gear is pitched up off a wall in front (rad, see <see cref="PlayerState.Tuck"/>), and
/// <see cref="HeadYaw"/> how far the head is turned from the aim (rad, see <see cref="PlayerState.HeadYaw"/>).
/// <see cref="Climbing"/>: on a ladder, hands on the rungs and the gear slung.
/// </summary>
public readonly record struct HitboxPose(
    Vector3 Position, float Yaw, float Pitch, float EyeHeight, float LeanRoll, float Shoulder, bool Alive, bool Present, float Tuck = 0f,
    float HeadYaw = 0f, bool Climbing = false)
{
    public static HitboxPose Of(PlayerState p) =>
        new(p.Position, p.Yaw, p.Pitch, p.EyeHeight, p.LeanRoll, p.Shoulder, p.Alive, p.Present, p.Tuck, p.HeadYaw, p.OnLadder);
}

/// <summary>One posed hitbox: an oriented box in world space.</summary>
public struct PosedBox
{
    public HitboxPart Part;
    public Vector3 Center;
    public Vector3 AxisX;
    public Vector3 AxisY;
    public Vector3 AxisZ;
    public Vector3 HalfExtents;

    public readonly Quaternion Rotation => Quaternion.CreateFromRotationMatrix(new Matrix4x4(
        AxisX.X, AxisX.Y, AxisX.Z, 0f, AxisY.X, AxisY.Y, AxisY.Z, 0f, AxisZ.X, AxisZ.Y, AxisZ.Z, 0f, 0f, 0f, 0f, 1f));
}

/// <summary>
/// Poses a player's hitboxes (spec §1.2) from their state: the legs stand on the feet and turn with
/// the yaw; the torso and head roll about the hips with the lean; the mask, arms and gear follow the
/// aim, and the arms and gear sit on the shoulder side. The head and mask turn with the head, about
/// the middle of the head. Near a wall the arms and gear pitch up about the back of the marker (the
/// tuck), so the barrel never pokes through. Eliminated players hold the marker up. On a ladder the arms reach
/// up to the rungs and the gear hangs on the back. Characters are drawn from the same boxes, so what you see is
/// what you can hit.
/// </summary>
public static class HitboxRig
{
    public const int PartCount = 8;

    public static void Pose(in HitboxPose pose, HitboxParams rig, float leanPivotBelowEye, Span<PosedBox> parts)
    {
        // Frames: yaw only (legs); yaw and lean roll (torso, head); yaw, pitch and lean roll (aim).
        // Rolling right is negative about the view axis (see PlayerPose).
        Quaternion body = Quaternion.CreateFromYawPitchRoll(pose.Yaw, 0f, 0f);
        Quaternion upper = Quaternion.CreateFromYawPitchRoll(pose.Yaw, 0f, -pose.LeanRoll);
        Quaternion aim = Quaternion.CreateFromYawPitchRoll(pose.Yaw, pose.Alive ? pose.Pitch : rig.EliminatedPitch, -pose.LeanRoll);

        float hips = MathF.Max(0.1f, pose.EyeHeight - leanPivotBelowEye);
        Vector3 pivot = pose.Position + new Vector3(0f, hips, 0f);
        Vector3 eye = pose.Position + new Vector3(0f, pose.EyeHeight, 0f) + PlayerPose.LeanOffset(pose.LeanRoll, pose.Yaw, leanPivotBelowEye);
        Vector3 hands = pose.Alive ? eye : eye + new Vector3(0f, rig.EliminatedRaise, 0f);

        parts[0] = Box(HitboxPart.Legs, pose.Position + new Vector3(0f, hips * 0.5f, 0f), body,
            new Vector3(rig.LegsWidth * 0.5f, hips * 0.5f, rig.LegsDepth * 0.5f));
        float torso = MathF.Max(0.1f, leanPivotBelowEye - rig.TorsoTopBelowEye);
        parts[1] = Box(HitboxPart.Torso, pivot + Vector3.Transform(new Vector3(0f, torso * 0.5f, 0f), upper), upper,
            new Vector3(rig.TorsoWidth * 0.5f, torso * 0.5f, rig.TorsoDepth * 0.5f));
        parts[2] = FromEye(HitboxPart.Head, rig.Head, eye, upper, 1f);
        parts[3] = FromEye(HitboxPart.Mask, rig.Mask, eye, aim, 1f);
        if (pose.HeadYaw != 0f && pose.Alive)
        {
            // Turned about the vertical through the middle of the head: the head turns in place, the mask goes round it.
            Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, pose.HeadYaw);
            Vector3 middle = parts[2].Center;
            parts[2] = Box(HitboxPart.Head, middle, Quaternion.Concatenate(upper, turn), rig.Head.HalfExtents);
            parts[3] = Box(HitboxPart.Mask, middle + Vector3.Transform(parts[3].Center - middle, turn), Quaternion.Concatenate(aim, turn),
                rig.Mask.HalfExtents);
        }
        if (pose.Climbing && pose.Alive)
        {
            // Both hands on the rungs: the arms up in front, the gear on the back, the marker's back end at the sling
            // and its barrel hanging down, the loader and tank where they sit on it.
            Quaternion reach = Quaternion.CreateFromYawPitchRoll(pose.Yaw, rig.ClimbArmsPitch, 0f);
            Quaternion slung = Quaternion.CreateFromYawPitchRoll(pose.Yaw, rig.SlungPitch, rig.SlungRoll);
            Vector3 sling = eye + Vector3.Transform(new Vector3(0f, -rig.SlungBelowEye, rig.SlungBehind), body);
            Vector3 marker = sling + Vector3.Transform(new Vector3(0f, 0f, -rig.Marker.HalfExtents.Z), slung);
            parts[4] = FromEye(HitboxPart.Arms, rig.Arms, eye, reach, 1f);
            parts[5] = Box(HitboxPart.Marker, marker, slung, rig.Marker.HalfExtents);
            parts[6] = OnMarker(HitboxPart.Loader, rig.Loader, rig.Marker, marker, slung);
            parts[7] = OnMarker(HitboxPart.Tank, rig.Tank, rig.Marker, marker, slung);
            return;
        }

        float tuck = pose.Alive ? pose.Tuck : 0f;
        Quaternion tilt = Quaternion.CreateFromAxisAngle(Vector3.UnitX, tuck);
        Quaternion gear = tuck > 0f ? Quaternion.Concatenate(tilt, aim) : aim;
        Vector3 back = MarkerBack(rig, pose.Shoulder);
        parts[4] = Gear(HitboxPart.Arms, rig.Arms, hands, aim, gear, tilt, back, pose.Shoulder);
        parts[5] = Gear(HitboxPart.Marker, rig.Marker, hands, aim, gear, tilt, back, pose.Shoulder);
        parts[6] = Gear(HitboxPart.Loader, rig.Loader, hands, aim, gear, tilt, back, pose.Shoulder);
        parts[7] = Gear(HitboxPart.Tank, rig.Tank, hands, aim, gear, tilt, back, pose.Shoulder);
    }

    /// <summary>
    /// The marker's back (where it rests at the shoulder) and its front, the muzzle, in world space for a
    /// player whose eye is at <paramref name="eye"/>, with the gear pitched up by <paramref name="tuck"/>.
    /// </summary>
    public static (Vector3 Back, Vector3 Front) MarkerLine(Vector3 eye, float yaw, float pitch, float leanRoll, float shoulder, float tuck, HitboxParams rig)
    {
        Quaternion aim = Quaternion.CreateFromYawPitchRoll(yaw, pitch, -leanRoll);
        Vector3 back = MarkerBack(rig, shoulder);
        Vector3 front = back + Vector3.Transform(new Vector3(0f, 0f, -2f * rig.Marker.HalfExtents.Z), Quaternion.CreateFromAxisAngle(Vector3.UnitX, tuck));
        return (eye + Vector3.Transform(back, aim), eye + Vector3.Transform(front, aim));
    }

    /// <summary>The back of the marker in the aim frame's own space (z back): what the gear pitches up about.</summary>
    private static Vector3 MarkerBack(HitboxParams rig, float side) =>
        new(rig.Marker.Centre.X * side, rig.Marker.Centre.Y, -(rig.Marker.Centre.Z - rig.Marker.HalfExtents.Z));

    /// <summary>A gear box placed from the eye like <see cref="FromEye"/>, then turned up about <paramref name="pivot"/> by <paramref name="tilt"/>.</summary>
    private static PosedBox Gear(HitboxPart part, PartBox box, Vector3 hands, Quaternion aim, Quaternion gear, Quaternion tilt, Vector3 pivot, float side)
    {
        var local = new Vector3(box.Centre.X * side, box.Centre.Y, -box.Centre.Z);
        Vector3 tilted = pivot + Vector3.Transform(local - pivot, tilt);
        return Box(part, hands + Vector3.Transform(tilted, aim), gear, box.HalfExtents);
    }

    /// <summary>A gear box where it sits on the marker (its offset from the marker's middle), the marker's middle at <paramref name="marker"/>.</summary>
    private static PosedBox OnMarker(HitboxPart part, PartBox box, PartBox markerBox, Vector3 marker, Quaternion frame)
    {
        Vector3 offset = box.Centre - markerBox.Centre;
        return Box(part, marker + Vector3.Transform(new Vector3(offset.X, offset.Y, -offset.Z), frame), frame, box.HalfExtents);
    }

    private static PosedBox FromEye(HitboxPart part, PartBox box, Vector3 eye, Quaternion frame, float side)
    {
        // View space (z forward) to the frame's local space (z back).
        var local = new Vector3(box.Centre.X * side, box.Centre.Y, -box.Centre.Z);
        return Box(part, eye + Vector3.Transform(local, frame), frame, box.HalfExtents);
    }

    private static PosedBox Box(HitboxPart part, Vector3 center, Quaternion rotation, Vector3 halfExtents) => new()
    {
        Part = part,
        Center = center,
        AxisX = Vector3.Transform(Vector3.UnitX, rotation),
        AxisY = Vector3.Transform(Vector3.UnitY, rotation),
        AxisZ = Vector3.Transform(Vector3.UnitZ, rotation),
        HalfExtents = halfExtents,
    };
}
