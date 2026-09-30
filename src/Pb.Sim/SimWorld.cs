using System.Numerics;
using Pb.Sim.Ballistics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Events;
using Pb.Sim.Gear;
using Pb.Sim.Players;
using Pb.Sim.Range;

namespace Pb.Sim;

/// <summary>Where a shot leaves from and where it's aimed, before dispersion.</summary>
public struct ShotSolution
{
    public Vector3 Origin;
    public Vector3 Direction;
    public Vector3 AimPoint;
    /// <summary>The muzzle was on the far side of a wall; the ball starts at the wall.</summary>
    public bool MuzzleBlocked;
}

/// <summary>
/// The engine-free simulation for one match (Phase 1: one range). Hosts call <see cref="Step"/> once
/// per fixed tick with one <see cref="InputCommand"/> per player, then read and clear <see cref="Events"/>.
/// Player movement (collide-and-slide) is done by the host before the step; see <see cref="MovementModel"/>.
/// </summary>
public sealed class SimWorld
{
    private readonly List<PlayerState> _players = new();
    private readonly ShotRequest[] _shots = new ShotRequest[8];

    public SimWorld(SimConfig config)
    {
        Config = config;
        Ballistics = new BallisticsWorld(config.BallPoolCapacity, config.Projectile, config.BreakModel)
        {
            World = Collision,
            Hitboxes = Targets,
        };
    }

    public SimConfig Config { get; private set; }

    public int Tick { get; private set; }

    /// <summary>Sim time at the start of the next tick (s). Accumulated, so tick-rate changes stay continuous.</summary>
    public double Time { get; private set; }

    public float Dt => Config.Dt;

    public CollisionWorld Collision { get; } = new();

    public TargetSet Targets { get; } = new();

    public BallisticsWorld Ballistics { get; }

    public SimEventQueue Events { get; } = new();

    public StressCannons? Stress { get; private set; }

    public RangeLayout? Range { get; private set; }

    public IReadOnlyList<PlayerState> Players => _players;

    public PlayerState AddPlayer(int id, byte team, Vector3 position, float yaw)
    {
        var marker = new Marker(Config.Fire, Config.Loader, Config.Air, Config.Shot.MuzzleVelocity);
        var player = new PlayerState(id, team, marker)
        {
            Position = position,
            Yaw = yaw,
            EyeHeight = Config.Movement.StandEyeHeight,
        };
        _players.Add(player);
        return player;
    }

    public void LoadRange(RangeLayout range, StressSettings stress)
    {
        Range = range;
        range.BuildCollision(Collision);
        Targets.Load(range.Targets);
        Targets.Update(Time);
        Ballistics.Bounds = range.Bounds;
        bool enabled = Stress?.Enabled ?? false;
        Stress = new StressCannons(stress) { Enabled = enabled };
    }

    /// <summary>Applies re-loaded data (hot reload). Live balls keep flying with the new physics.</summary>
    public void ApplyConfig(SimConfig config)
    {
        Config = config;
        Ballistics.Projectile = config.Projectile;
        Ballistics.BreakModel = config.BreakModel;
        foreach (PlayerState p in _players)
        {
            p.Marker.Fire.Params = config.Fire;
            p.Marker.Paint.Reconfigure(config.Loader);
            p.Marker.Air.Reconfigure(config.Air);
            p.Marker.MuzzleVelocity = config.Shot.MuzzleVelocity;
        }
    }

    public void Step(ReadOnlySpan<InputCommand> commands)
    {
        double t0 = Time;
        float dt = Dt;
        Targets.Update(t0);

        for (int i = 0; i < _players.Count; i++)
        {
            PlayerState player = _players[i];
            if (i < commands.Length)
            {
                InputCommand cmd = commands[i];
                player.Yaw = cmd.Yaw;
                player.Pitch = Math.Clamp(cmd.Pitch, -Config.Movement.MaxPitch, Config.Movement.MaxPitch);
                var input = new MarkerInput(
                    cmd.Has(InputButtons.Fire), cmd.Has(InputButtons.Refill), cmd.Has(InputButtons.ToggleFireMode),
                    player.Sprinting, player.Alive);
                int count = player.Marker.Update(t0, dt, input, _shots, Events, player.Id, player.Team, Tick);
                for (int k = 0; k < count; k++)
                {
                    FireShot(player, _shots[k]);
                }
            }
        }

        Stress?.Update(Ballistics, Config.MatchSeed, Config.Shot.MuzzleVelocity, Config.Shot.VelocityVariance, Tick, dt, Events);
        Ballistics.Tick(Tick, dt, Events);

        Tick++;
        Time += dt;
    }

    public void ResetGear(PlayerState player, bool resetTargets)
    {
        player.Marker.ResetGear();
        if (resetTargets)
        {
            Targets.ResetHits();
        }

        Events.Add(new SimEvent { Type = SimEventType.GearReset, Tick = Tick, PlayerId = player.Id, TargetId = -1, ColliderId = -1 });
    }

    /// <summary>
    /// Muzzle position and launch direction for <paramref name="player"/>'s current view. The ball
    /// flies from the marker's muzzle toward whatever is under the crosshair, so shoulder position
    /// matters while shots still converge on the aim point.
    /// </summary>
    public ShotSolution SolveShot(PlayerState player)
    {
        ShotParams p = Config.Shot;
        Vector3 eye = player.EyePosition;
        Vector3 forward = ViewAngles.Forward(player.Yaw, player.Pitch);
        Vector3 muzzle = eye + ViewAngles.ViewToWorld(p.MuzzleOffset, player.Yaw, player.Pitch);

        Vector3 far = eye + forward * p.MaxAimDistance;
        float nearest = float.MaxValue;
        if (Collision.SweepSphere(eye, far, 0f, out SweepHit wh))
        {
            nearest = wh.T;
        }

        if (Targets.SweepSphere(eye, far, 0f, Tick, player.Id, out HitboxHit th) && th.T < nearest)
        {
            nearest = th.T;
        }

        Vector3 aim = nearest <= 1f ? eye + (far - eye) * nearest : eye + forward * p.ConvergenceDistance;
        if (Vector3.Distance(eye, aim) < p.MinAimDistance)
        {
            aim = eye + forward * p.MinAimDistance;
        }

        var solution = new ShotSolution { Origin = muzzle, AimPoint = aim };

        // Don't let the barrel poke through a wall: start the ball where the eye→muzzle line meets it.
        if (Collision.SweepSphere(eye, muzzle, 0f, out SweepHit block) && block.T < 1f)
        {
            solution.Origin = Vector3.Lerp(eye, muzzle, MathF.Max(0f, block.T - 0.02f));
            solution.MuzzleBlocked = true;
        }

        solution.Direction = VectorMath.NormalizeOr(aim - solution.Origin, forward);
        return solution;
    }

    /// <summary>Effective dispersion half-angle (rad) for a shooter moving at <paramref name="speed"/>.</summary>
    public float DispersionFor(float speed)
    {
        ShotParams p = Config.Shot;
        return MathF.Min(p.MaxDispersion, p.DispersionHalfAngle + p.MovingDispersionPerSpeed * speed);
    }

    private void FireShot(PlayerState player, in ShotRequest shot)
    {
        ShotParams p = Config.Shot;
        ShotSolution solution = SolveShot(player);
        var rng = new Pcg32(SeedHash.Shot(Config.MatchSeed, player.Id, shot.Sequence));
        Vector3 direction = Dispersion.SampleCone(solution.Direction, DispersionFor(player.HorizontalSpeed), ref rng);
        float speed = MathF.Max(0.5f, shot.MuzzleSpeed + rng.Symmetric(p.VelocityVariance));
        Vector3 velocity = direction * speed + player.Velocity * p.InheritShooterVelocity;
        float firstStep = MathF.Max(1e-5f, Dt - shot.TimeOffset);
        Ballistics.Spawn(solution.Origin, velocity, player.Id, shot.Sequence, player.Team, rng, firstStep, Tick, Events);
    }
}
