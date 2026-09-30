using System.Numerics;

namespace Pb.Sim.Core;

/// <summary>
/// View-angle maths shared by the sim and the Godot layer. Coordinate system matches Godot:
/// Y up, right-handed, yaw 0 faces −Z, positive yaw turns left, positive pitch looks up.
/// </summary>
public static class ViewAngles
{
    public static Vector3 Forward(float yaw, float pitch)
    {
        (float sy, float cy) = MathF.SinCos(yaw);
        (float sp, float cp) = MathF.SinCos(pitch);
        return new Vector3(-sy * cp, sp, -cy * cp);
    }

    public static Vector3 FlatForward(float yaw)
    {
        (float sy, float cy) = MathF.SinCos(yaw);
        return new Vector3(-sy, 0f, -cy);
    }

    public static Vector3 Right(float yaw)
    {
        (float sy, float cy) = MathF.SinCos(yaw);
        return new Vector3(cy, 0f, -sy);
    }

    public static Vector3 Up(float yaw, float pitch) => Vector3.Cross(Right(yaw), Forward(yaw, pitch));

    /// <summary>Converts a view-space offset (x right, y up, z forward) to a world-space offset.</summary>
    public static Vector3 ViewToWorld(Vector3 local, float yaw, float pitch) =>
        Right(yaw) * local.X + Up(yaw, pitch) * local.Y + Forward(yaw, pitch) * local.Z;

    /// <summary>Yaw and pitch that face along <paramref name="direction"/>.</summary>
    public static (float Yaw, float Pitch) FromDirection(Vector3 direction)
    {
        Vector3 d = Vector3.Normalize(direction);
        float yaw = MathF.Atan2(-d.X, -d.Z);
        float pitch = MathF.Asin(Math.Clamp(d.Y, -1f, 1f));
        return (yaw, pitch);
    }
}
