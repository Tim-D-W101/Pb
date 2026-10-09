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
/// Everything a participant does in one tick. Humans, bots and remote players all produce these;
/// buttons are levels, and the sim detects edges itself.
/// </summary>
public struct InputCommand
{
    public int Tick;

    /// <summary>X = strafe right, Y = forward; length ≤ 1 (analog sticks give partial values).</summary>
    public Vector2 Move;

    public float Yaw;

    public float Pitch;

    /// <summary>
    /// Where the head looks, turned from <see cref="Yaw"/> (rad, positive to the left, at most movement's
    /// maxHeadTurn): bots glance round and look ahead of a turn with it. A person's view is the aim, so theirs is 0.
    /// </summary>
    public float HeadYaw;

    public InputButtons Buttons;

    /// <summary>
    /// Lag compensation: how many ticks behind the server the shooter was seeing everyone else when they pressed (a server
    /// works it out from the player's view tick). Their shots are checked against players as they were that far back,
    /// at most <see cref="PlayerHitboxes.HistoryTicks"/> − 1; 0 for bots and anyone playing on the server's own copy.
    /// </summary>
    public byte Rewind;

    public readonly bool Has(InputButtons button) => (Buttons & button) != 0;
}
