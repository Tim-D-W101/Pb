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

    /// <summary>Indexed by <see cref="HitboxPart"/>.</summary>
    public required bool[] LethalParts { get; init; }

    public required bool BallsInFlightCount { get; init; }

    public required float MaskSprayRadius { get; init; }

    public bool IsLethal(HitboxPart part) => (int)part < LethalParts.Length && LethalParts[(int)part];
}

/// <summary>Everything the rig needs from a player, recorded every tick for the hitbox history.</summary>
public readonly record struct HitboxPose(
    Vector3 Position, float Yaw, float Pitch, float EyeHeight, float LeanRoll, float Shoulder, bool Alive, bool Present)
{
    public static HitboxPose Of(PlayerState p) =>
        new(p.Position, p.Yaw, p.Pitch, p.EyeHeight, p.LeanRoll, p.Shoulder, p.Alive, p.Present);
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
/// aim, and the arms and gear sit on the shoulder side. Eliminated players hold the marker up.
/// Characters are drawn from the same boxes, so what you see is what you can hit.
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
        parts[4] = FromEye(HitboxPart.Arms, rig.Arms, hands, aim, pose.Shoulder);
        parts[5] = FromEye(HitboxPart.Marker, rig.Marker, hands, aim, pose.Shoulder);
        parts[6] = FromEye(HitboxPart.Loader, rig.Loader, hands, aim, pose.Shoulder);
        parts[7] = FromEye(HitboxPart.Tank, rig.Tank, hands, aim, pose.Shoulder);
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
