using System.Numerics;
using Pb.Sim.Ballistics;
using Pb.Sim.Collision;
using Pb.Sim.Gear;
using Pb.Sim.Players;

namespace Pb.Sim;

/// <summary>How a marker turns a trigger pull into a launch (SI units, angles in radians).</summary>
public sealed class ShotParams
{
    public required float MuzzleVelocity { get; init; }

    public required float VelocityVariance { get; init; }

    public required float DispersionHalfAngle { get; init; }

    /// <summary>Extra cone half-angle per m/s of shooter speed (rad·s/m).</summary>
    public required float MovingDispersionPerSpeed { get; init; }

    public required float MaxDispersion { get; init; }

    /// <summary>0 = ball ignores the shooter's velocity, 1 = physically inherits it.</summary>
    public required float InheritShooterVelocity { get; init; }

    /// <summary>Muzzle position relative to the eye, view space (x right, y up, z forward), right-shoulder hold.</summary>
    public required Vector3 MuzzleOffset { get; init; }

    /// <summary>A shot whose barrel is behind a wall the eye sees past breaks on that wall.</summary>
    public required bool MuzzleBlockedBreaks { get; init; }

    /// <summary>Aim point distance when nothing is under the crosshair.</summary>
    public required float ConvergenceDistance { get; init; }

    public required float MinAimDistance { get; init; }

    public required float MaxAimDistance { get; init; }
}

/// <summary>Every gameplay parameter the sim needs, converted to SI. Built by <see cref="Data.GameData"/>.</summary>
public sealed class SimConfig
{
    public required float TickRate { get; init; }

    public required ulong MatchSeed { get; init; }

    public required int BallPoolCapacity { get; init; }

    public required SurfaceRegistry Surfaces { get; init; }

    public required ProjectileParams Projectile { get; init; }

    public required BreakModel BreakModel { get; init; }

    public required ShotParams Shot { get; init; }

    public required FireControlParams Fire { get; init; }

    public required LoaderParams Loader { get; init; }

    public required AirParams Air { get; init; }

    public required MovementParams Movement { get; init; }

    public float Dt => 1f / TickRate;
}
