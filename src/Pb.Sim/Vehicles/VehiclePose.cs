using System.Numerics;

namespace Pb.Sim.Vehicles;

/// <summary>
/// A vehicle's turn as three angles, in the order a body turns (lean, then pitch, then heading: yaw about the world's
/// up, pitch about its right, lean about its length), to send it compactly and back again.
/// </summary>
public static class VehiclePose
{
    /// <summary>
    /// <paramref name="yaw"/> as the sim's (0 faces −Z, positive left), <paramref name="pitch"/> nose up positive (−π/2 …
    /// π/2), <paramref name="roll"/> its top to the left positive (−π … π).
    /// </summary>
    public static void Angles(Quaternion q, out float yaw, out float pitch, out float roll)
    {
        Vector3 f = Vector3.Transform(-Vector3.UnitZ, q);
        yaw = MathF.Atan2(-f.X, -f.Z);
        pitch = MathF.Asin(Math.Clamp(f.Y, -1f, 1f));
        Vector3 up = Vector3.Transform(Vector3.UnitY, q);
        Vector3 local = Vector3.Transform(up, Quaternion.Conjugate(Heading(yaw, pitch)));
        roll = MathF.Atan2(-local.X, local.Y);
    }

    public static Quaternion FromAngles(float yaw, float pitch, float roll) =>
        Heading(yaw, pitch) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, roll);

    private static Quaternion Heading(float yaw, float pitch) =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch);
}
