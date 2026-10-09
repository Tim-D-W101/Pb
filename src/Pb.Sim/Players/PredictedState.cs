using System.Numerics;
using Pb.Sim.Gear;

namespace Pb.Sim.Players;

/// <summary>
/// Everything that decides how a player moves and fires next: what a joining copy predicts for its own player, and what
/// the server sends back so the copy can check its prediction, put the server's state back and replay its later presses
/// from it. The marker's times are relative to the sim's clock (<see cref="MarkerState"/>), since the two copies' clocks
/// count from different starts; footsteps and the like, which change nothing that follows, are left out.
/// </summary>
public struct PredictedState
{
    public Vector3 Position;

    public Vector3 Velocity;

    public Vector3 LeanOffset;

    public float Yaw;

    public float Pitch;

    public float HeadYaw;

    public float EyeHeight;

    public float Lean;

    public float LeanRoll;

    public float Shoulder;

    public float ShoulderTarget;

    public float Tuck;

    public float SlideTime;

    public float SlideCooldown;

    public float JumpCooldown;

    public float SprintRecovery;

    public float LadderTime;

    public Stance Stance;

    public InputButtons PreviousButtons;

    public short Ladder;

    public LadderPhase LadderPhase;

    public bool Sprinting;

    public bool Grounded;

    public bool Alive;

    public bool Present;

    public bool SprintBlocked;

    public MarkerState Marker;

    /// <summary><paramref name="player"/>'s state now, its marker's times relative to <paramref name="now"/> (sim time).</summary>
    public static PredictedState Capture(PlayerState player, double now) => new()
    {
        Position = player.Position,
        Velocity = player.Velocity,
        LeanOffset = player.LeanOffset,
        Yaw = player.Yaw,
        Pitch = player.Pitch,
        HeadYaw = player.HeadYaw,
        EyeHeight = player.EyeHeight,
        Lean = player.Lean,
        LeanRoll = player.LeanRoll,
        Shoulder = player.Shoulder,
        ShoulderTarget = player.ShoulderTarget,
        Tuck = player.Tuck,
        SlideTime = player.SlideTime,
        SlideCooldown = player.SlideCooldown,
        JumpCooldown = player.JumpCooldown,
        SprintRecovery = player.SprintRecovery,
        LadderTime = player.LadderTime,
        Stance = player.Stance,
        PreviousButtons = player.PreviousButtons,
        Ladder = (short)player.Ladder,
        LadderPhase = player.LadderPhase,
        Sprinting = player.Sprinting,
        Grounded = player.Grounded,
        Alive = player.Alive,
        Present = player.Present,
        SprintBlocked = player.SprintBlocked,
        Marker = player.Marker.Capture(now),
    };

    /// <summary>Puts this state back on <paramref name="player"/> at <paramref name="now"/> on this copy's clock.</summary>
    public readonly void Restore(PlayerState player, double now)
    {
        player.Position = Position;
        player.Velocity = Velocity;
        player.LeanOffset = LeanOffset;
        player.Yaw = Yaw;
        player.Pitch = Pitch;
        player.HeadYaw = HeadYaw;
        player.EyeHeight = EyeHeight;
        player.Lean = Lean;
        player.LeanRoll = LeanRoll;
        player.Shoulder = Shoulder;
        player.ShoulderTarget = ShoulderTarget;
        player.Tuck = Tuck;
        player.SlideTime = SlideTime;
        player.SlideCooldown = SlideCooldown;
        player.JumpCooldown = JumpCooldown;
        player.SprintRecovery = SprintRecovery;
        player.LadderTime = LadderTime;
        player.Stance = Stance;
        player.PreviousButtons = PreviousButtons;
        player.Ladder = Ladder;
        player.LadderPhase = LadderPhase;
        player.Sprinting = Sprinting;
        player.Grounded = Grounded;
        player.Alive = Alive;
        player.Present = Present;
        player.SprintBlocked = SprintBlocked;
        player.Marker.Restore(Marker, now);
    }

    /// <summary>
    /// Whether a prediction matches the server's state: the feet within <paramref name="tolerance"/> (m), and everything
    /// else as good as equal.
    /// </summary>
    public readonly bool Matches(in PredictedState server, float tolerance)
    {
        const float Small = 1e-4f;
        return Vector3.DistanceSquared(Position, server.Position) <= tolerance * tolerance &&
               Vector3.DistanceSquared(Velocity, server.Velocity) <= tolerance * tolerance * 3600f &&
               Stance == server.Stance && Ladder == server.Ladder && LadderPhase == server.LadderPhase && Sprinting == server.Sprinting &&
               Grounded == server.Grounded && Alive == server.Alive && Present == server.Present && SprintBlocked == server.SprintBlocked &&
               PreviousButtons == server.PreviousButtons &&
               MathF.Abs(EyeHeight - server.EyeHeight) < Small && MathF.Abs(Lean - server.Lean) < Small &&
               MathF.Abs(Shoulder - server.Shoulder) < Small && ShoulderTarget == server.ShoulderTarget &&
               MathF.Abs(Tuck - server.Tuck) < Small && MathF.Abs(SlideTime - server.SlideTime) < Small &&
               MathF.Abs(SlideCooldown - server.SlideCooldown) < Small && MathF.Abs(JumpCooldown - server.JumpCooldown) < Small &&
               MathF.Abs(SprintRecovery - server.SprintRecovery) < Small && MathF.Abs(LadderTime - server.LadderTime) < Small &&
               Marker.SameAs(server.Marker);
    }
}
