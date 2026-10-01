using System.Numerics;

namespace Pb.Sim.Players;

[Flags]
public enum InputButtons : ushort
{
    None = 0,
    Fire = 1 << 0,
    Sprint = 1 << 1,
    Crouch = 1 << 2,
    Walk = 1 << 3,
    Refill = 1 << 4,
    ToggleFireMode = 1 << 5,
    Interact = 1 << 6,
    LeanLeft = 1 << 7,
    LeanRight = 1 << 8,
    SwapShoulder = 1 << 9,
    Slide = 1 << 10,
    Jump = 1 << 11,
}

/// <summary>
/// Everything a participant does in one tick. Humans, bots and (Phase 3) remote clients all
/// produce these; buttons are levels, and the sim detects edges itself.
/// </summary>
public struct InputCommand
{
    public int Tick;

    /// <summary>X = strafe right, Y = forward; length ≤ 1 (analog sticks give partial values).</summary>
    public Vector2 Move;

    public float Yaw;

    public float Pitch;

    public InputButtons Buttons;

    public readonly bool Has(InputButtons button) => (Buttons & button) != 0;
}
