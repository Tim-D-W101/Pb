using System.Numerics;
using Pb.Sim.Collision;

namespace Pb.Sim.Events;

public enum SimEventType : byte
{
    /// <summary>A ball left a muzzle. Position = origin, Velocity = launch velocity.</summary>
    ShotFired,
    /// <summary>A ball broke. Position = surface point, Normal = surface normal, Value = impact speed.</summary>
    BallBroke,
    /// <summary>A ball bounced. Position = contact point, Velocity = outgoing velocity, Value = impact speed.</summary>
    BallBounced,
    /// <summary>A ball was removed without breaking. Extra = <see cref="DespawnReason"/>.</summary>
    BallDespawned,
    /// <summary>A lethal (unbounced) break on a hit receiver. TargetId = receiver, Extra = hitbox part.</summary>
    TargetHit,
    /// <summary>Trigger pulled with an empty loader or empty tank.</summary>
    DryFire,
    FireModeChanged,
    RefillStarted,
    RefillCompleted,
    RefillCancelled,
    /// <summary>Refill requested but impossible (loader full or pods empty).</summary>
    RefillDenied,
    /// <summary>Tank pressure dropped below the regulator output; velocity now falls off. Value = pressure (Pa).</summary>
    AirLow,
    GearReset,
    /// <summary>Movement noise. Position = feet, Surface = underfoot, Value = hearing radius (m), Extra = <see cref="FootstepKind"/>.</summary>
    Footstep,
    /// <summary>A player is out. PlayerId = shooter, TargetId = eliminated player, Extra = hitbox part, Position = break point.</summary>
    PlayerEliminated,
    /// <summary>Paint sprayed a player's mask. TargetId = player, Team = paint, Position = break point, Value = strength 0..1.</summary>
    MaskSprayed,
    /// <summary>The round moved on (briefing → live). Extra = <see cref="Match.MatchPhase"/>.</summary>
    MatchPhaseChanged,
    /// <summary>The round is decided. Extra = <see cref="Match.RoundOutcome"/>.</summary>
    RoundEnded,
    /// <summary>A player took a pickup. TargetId = pickup index, Extra = <see cref="Data.PickupKind"/>, Position = where it was.</summary>
    PickupTaken,
}

public enum FootstepKind : byte
{
    Step,
    Slide,
    Jump,
    Land,
}

public enum DespawnReason : byte
{
    Lifetime,
    OutOfBounds,
    Rest,
    PoolFull,
}

/// <summary>
/// One flat struct for every event type so the queue is a single allocation-free list that can
/// later be serialised for replication. Unused fields keep their defaults.
/// </summary>
public struct SimEvent
{
    public SimEventType Type;
    public int Tick;
    /// <summary>Shooter / actor id. Stress cannons use negative ids.</summary>
    public int PlayerId;
    /// <summary>Hit receiver id (target or, later, player); -1 when none.</summary>
    public int TargetId;
    /// <summary>Static collider id for world hits; -1 when none.</summary>
    public int ColliderId;
    public uint ShotSequence;
    public byte Team;
    public SurfaceId Surface;
    public Vector3 Position;
    public Vector3 Normal;
    public Vector3 Velocity;
    public float Value;
    public int Extra;
    /// <summary>For BallBroke: true when the break counted as a hit on a receiver.</summary>
    public bool Lethal;
}
