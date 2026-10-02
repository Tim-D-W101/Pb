using System.Numerics;

namespace Pb.Sim.Level;

public enum ApertureKind : byte
{
    Door,
    Window,
    Gap,

    /// <summary>A patch of roof that has fallen in.</summary>
    RoofHole,
}

/// <summary>
/// A hole through a wall or roof (window, door, gap, fallen-in roof) as a rectangle in world space:
/// its centre, two unit axes in its plane and the half sizes along them. No geometry crosses it, so
/// light and paint pass straight through. The game draws light shafts and window light from these.
/// </summary>
public readonly record struct Aperture(ApertureKind Kind, Vector3 Center, Vector3 U, Vector3 V, float HalfWidth, float HalfHeight, int Owner)
{
    /// <summary>U × V: horizontal for wall openings (either side may be outdoors), up for roof holes.</summary>
    public Vector3 Normal => Vector3.Normalize(Vector3.Cross(U, V));

    public float Area => 4f * HalfWidth * HalfHeight;

    /// <summary>The point at (<paramref name="u"/>, <paramref name="v"/>) in −1…1 across the rectangle.</summary>
    public Vector3 At(float u, float v) => Center + U * (u * HalfWidth) + V * (v * HalfHeight);
}
