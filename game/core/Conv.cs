using Godot;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>Conversions between the sim's System.Numerics types and Godot's.</summary>
public static class Conv
{
    public static Vector3 ToGodot(this SVector3 v) => new(v.X, v.Y, v.Z);

    public static SVector3 ToSim(this Vector3 v) => new(v.X, v.Y, v.Z);

    public static Color ParseColor(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return fallback;
        }

        return Color.HtmlIsValid(hex) ? Color.FromHtml(hex) : fallback;
    }

    /// <summary>Basis whose +Y axis is <paramref name="up"/>, spun by <paramref name="spin"/> radians around it.</summary>
    public static Basis BasisFromUp(Vector3 up, float spin)
    {
        up = up.Normalized();
        Vector3 reference = Mathf.Abs(up.Dot(Vector3.Forward)) < 0.95f ? Vector3.Forward : Vector3.Right;
        Vector3 x = reference.Cross(up).Normalized();
        Vector3 z = x.Cross(up).Normalized();
        return new Basis(x, up, z).Rotated(up, spin).Orthonormalized();
    }
}
