using System.Numerics;
using Pb.Sim.Ballistics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Gear;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;
using Pb.Sim.Range;

namespace Pb.Sim;

/// <summary>Where a shot leaves from and where it's aimed, before dispersion.</summary>
public struct ShotSolution
{
    public Vector3 Origin;
    public Vector3 Direction;
    public Vector3 AimPoint;
    /// <summary>The muzzle was on the far side of a wall from the eye; <see cref="Block"/> is where.</summary>
    public bool MuzzleBlocked;
    public SweepHit Block;
}

/// <summary>
/// The engine-free simulation for one match: the training range or a compound level. Hosts call <see cref="Step"/> once
/// per fixed tick with one <see cref="InputCommand"/> per player, then read and clear <see cref="Events"/>.
/// Player movement (collide-and-slide) is done by the host before the step; see <see cref="MovementModel"/>.
/// </summary>
public sealed class SimWorld
{
    private readonly List<PlayerState> _players = new();
    private readonly ShotRequest[] _shots = new ShotRequest[8];

    /// <param name="matchSeed">Seeds the round's randomness (shots, bots); the data's seed when null. A host deals
    /// a new one each round so rounds differ; tests and scripted runs keep the data's for repeatable results.</param>
    public SimWorld(SimConfig config, ulong? matchSeed = null)
    {
        Config = config;
        MatchSeed = matchSeed ?? config.MatchSeed;
        PlayerHits = new PlayerHitboxes(this);
        Receivers = new HitReceivers(Targets, PlayerHits);
        Ballistics = new BallisticsWorld(config.BallPoolCapacity, config.Projectile, config.BreakModel)
        {
            World = Collision,
            Hitboxes = Receivers,
        };
    }

    public SimConfig Config { get; private set; }

    /// <summary>Seeds every random choice in the round: shot dispersion and breaks, bots, random starts.</summary>
    public ulong MatchSeed { get; }

    public int Tick { get; private set; }

    /// <summary>Sim time at the start of the next tick (s). Accumulated, so tick-rate changes stay continuous.</summary>
    public double Time { get; private set; }

    public float Dt => Config.Dt;

    public CollisionWorld Collision { get; } = new();

    public TargetSet Targets { get; } = new();

    /// <summary>Players as hit receivers, with their hitbox history.</summary>
    public PlayerHitboxes PlayerHits { get; }

    /// <summary>Range targets and players together: what balls (and aim) are tested against.</summary>
    public HitReceivers Receivers { get; }

    /// <summary>The current round, once <see cref="StartMatch"/> has been called; null on the range.</summary>
    public MatchState? Match { get; private set; }

    public PickupSet Pickups { get; } = new();

    /// <summary>The level's door leaves (none on the range).</summary>
    public DoorSet Doors { get; } = new();

    /// <summary>Players may move and fire: always without a match, and only while it's live with one.</summary>
    public bool IsLive => Match is null || Match.Phase == MatchPhase.Live;

    public BallisticsWorld Ballistics { get; }

    public SimEventQueue Events { get; } = new();

    public StressCannons? Stress { get; private set; }

    public RangeLayout? Range { get; private set; }

    public LevelLayout? Level { get; private set; }

    public IReadOnlyList<PlayerState> Players => _players;

    public PlayerState? FindPlayer(int id)
    {
        foreach (PlayerState p in _players)
        {
            if (p.Id == id)
            {
                return p;
            }
        }

        return null;
    }

    public PlayerState AddPlayer(int id, byte team, Vector3 position, float yaw)
    {
        var marker = new Marker(Config.Fire, Config.Loader, Config.Air, Config.Shot.MuzzleVelocity);
        var player = new PlayerState(id, team, marker)
        {
            Position = position,
            LastPosition = position,
            Yaw = yaw,
            EyeHeight = Config.Movement.StandEyeHeight,
        };
        _players.Add(player);
        return player;
    }

    /// <summary>Loads a compound level: its paint collision and bounds. Range targets and stress cannons are cleared.</summary>
    public void LoadLevel(LevelLayout level)
    {
        Level = level;
        PlayerHits.Enabled = true;
        Pickups.Load(level.Pickups);
        Range = null;
        Stress = null;
        level.BuildCollision(Collision);
        Doors.Load(level.Doors, Collision, MatchSeed, Config.Rules.Doors);
        Targets.Load(Array.Empty<TargetSpec>());
        Ballistics.Bounds = level.Bounds;
    }

    public void LoadRange(RangeLayout range, StressSettings stress)
    {
        Range = range;
        Level = null;
        PlayerHits.Enabled = false;
        Pickups.Load(Array.Empty<PickupSpec>());
        range.BuildCollision(Collision);
        Doors.Load(Array.Empty<DoorSpec>(), Collision, MatchSeed, Config.Rules.Doors);
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

    /// <summary>
    /// Starts a round with everyone already added, each on their team (in free-for-all, a team each):
    /// gear by the tier (full loaders and tanks, the tier's pods), pickups out or not, stats from zero.
    /// Every mode is won by the last team standing; an objective (the hero's side attacking it) is another way to win.
    /// The round waits in the briefing until <see cref="GoLive"/>.
    /// </summary>
    public MatchState StartMatch(MatchSetup setup)
    {
        ObjectiveKind kind = setup.Mode == MatchModeKind.FreeForAll ? ObjectiveKind.Eliminate : setup.Objective;
        ObjectiveState? objective = null;
        IMatchMode mode = LastTeamStandingMode.Instance;
        if (kind != ObjectiveKind.Eliminate)
        {
            LevelObjectives places = Level?.Objectives ?? LevelObjectives.None;
            if (!places.Offers(kind))
            {
                throw new InvalidOperationException($"{Level?.Id ?? "the range"} has no places for {kind.ToString().ToLowerInvariant()}");
            }

            byte attackers = FindPlayer(setup.HeroId)?.Team ?? 0;
            objective = new ObjectiveState(kind, attackers, places, Config.Rules.Objectives, MatchSeed);
            mode = kind == ObjectiveKind.Retrieve ? RetrieveMode.Instance : HoldMode.Instance;
        }

        Match = new MatchState(setup, Config.Rules, mode, objective);
        foreach (PlayerState p in _players)
        {
            p.SprintBlocked = false;
            p.Marker.ResetGear();
            p.Marker.Paint.FillWith(p.Id == setup.HeroId ? setup.StartPods : setup.BotPods);
            Match.AddPlayer(p.Id);
        }

        Pickups.Reset();
        Pickups.Active = setup.Pickups && Pickups.Items.Count > 0;
        Events.Add(new SimEvent
        {
            Type = SimEventType.MatchPhaseChanged, Tick = Tick, PlayerId = -1, TargetId = -1, ColliderId = -1, Extra = (int)MatchPhase.Briefing,
        });
        return Match;
    }

    /// <summary>Ends the briefing: the clock starts and everyone may move and fire.</summary>
    public void GoLive() => Match?.GoLive(this);

    public void Step(ReadOnlySpan<InputCommand> commands)
    {
        double t0 = Time;
        float dt = Dt;
        int firstEvent = Events.Count;
        Targets.Update(t0);
        PlayerHits.Record(Tick);

        for (int i = 0; i < _players.Count; i++)
        {
            PlayerState player = _players[i];
            if (i < commands.Length)
            {
                InputCommand cmd = commands[i];
                player.Yaw = cmd.Yaw;
                player.Pitch = Math.Clamp(cmd.Pitch, -Config.Movement.MaxPitch, Config.Movement.MaxPitch);
                UpdateFootsteps(player);
                bool live = IsLive;
                var input = new MarkerInput(
                    live && cmd.Has(InputButtons.Fire), live && cmd.Has(InputButtons.Refill), cmd.Has(InputButtons.ToggleFireMode),
                    player.Sprinting, player.Alive, player.MarkerReady);
                int count = player.Marker.Update(t0, dt, input, _shots, Events, player.Id, player.Team, Tick);
                for (int k = 0; k < count; k++)
                {
                    FireShot(player, _shots[k]);
                }

                Doors.Interact(this, player, live && player.Alive && cmd.Has(InputButtons.Interact), dt);
            }
        }

        Doors.Step(this, dt);
        if (IsLive)
        {
            Pickups.Update(this, Config.Rules);
        }

        Stress?.Update(Ballistics, MatchSeed, Config.Shot.MuzzleVelocity, Config.Shot.VelocityVariance, Tick, dt, Events);
        Ballistics.Tick(Tick, dt, Events);
        SprayMasks(firstEvent);
        Match?.Update(this, firstEvent);

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
        Vector3 muzzle = eye + ViewAngles.ViewToWorld(PlayerPose.MuzzleOffset(p.MuzzleOffset, player.Shoulder, player.LeanRoll),
            player.Yaw, player.Pitch);

        Vector3 far = eye + forward * p.MaxAimDistance;
        float nearest = float.MaxValue;
        if (Collision.SweepSphere(eye, far, 0f, out SweepHit wh))
        {
            nearest = wh.T;
        }

        if (Receivers.SweepSphere(eye, far, 0f, Tick, player.Id, out HitboxHit th) && th.T < nearest)
        {
            nearest = th.T;
        }

        Vector3 aim = nearest <= 1f ? eye + (far - eye) * nearest : eye + forward * p.ConvergenceDistance;
        if (Vector3.Distance(eye, aim) < p.MinAimDistance)
        {
            aim = eye + forward * p.MinAimDistance;
        }

        var solution = new ShotSolution { Origin = muzzle, AimPoint = aim };

        // The barrel can't poke through a wall: if the eye→muzzle line meets one, the shot starts
        // there (and, with the muzzle-in-cover rule, breaks on it).
        if (Collision.SweepSphere(eye, muzzle, 0f, out SweepHit block) && block.T < 1f)
        {
            solution.Origin = Vector3.Lerp(eye, muzzle, MathF.Max(0f, block.T - 0.02f));
            solution.MuzzleBlocked = true;
            solution.Block = block;
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
        var rng = new Pcg32(SeedHash.Shot(MatchSeed, player.Id, shot.Sequence));
        Vector3 direction = Dispersion.SampleCone(solution.Direction, DispersionFor(player.HorizontalSpeed), ref rng);
        float speed = MathF.Max(0.5f, shot.MuzzleSpeed + rng.Symmetric(p.VelocityVariance));
        Vector3 velocity = direction * speed + player.Velocity * p.InheritShooterVelocity;
        if (solution.MuzzleBlocked && p.MuzzleBlockedBreaks)
        {
            Ballistics.BreakAtMuzzle(solution.Origin, velocity, solution.Block, player.Id, shot.Sequence, player.Team, Tick, Events);
            return;
        }

        float firstStep = MathF.Max(1e-5f, Dt - shot.TimeOffset);
        Ballistics.Spawn(solution.Origin, velocity, player.Id, shot.Sequence, player.Team, rng, firstStep, Tick, Events);
    }

    /// <summary>
    /// Tracks the surface underfoot and emits footstep noise (steps, slides, jumps, landings) from
    /// how the host moved the player since the last tick. Bots listen for these.
    /// </summary>
    private void UpdateFootsteps(PlayerState player)
    {
        MovementParams m = Config.Movement;
        FootstepParams f = m.Footsteps;
        Vector3 position = player.Position;
        Vector3 moved = position - player.LastPosition;
        float distance = MathF.Sqrt(moved.X * moved.X + moved.Z * moved.Z);
        if (distance > 2f)
        {
            distance = 0f; // teleported
            player.StrideDistance = 0f;
        }

        if (player.Grounded &&
            Collision.SweepSphere(position + new Vector3(0f, 0.2f, 0f), position - new Vector3(0f, 0.3f, 0f), 0f, out SweepHit ground))
        {
            player.GroundSurface = ground.Surface;
        }

        if (player.Alive)
        {
            if (player.Grounded && !player.LastGrounded && -player.LastVelocity.Y >= f.LandMinSpeed)
            {
                Footstep(player, FootstepKind.Land, f.LandRadius);
            }
            else if (!player.Grounded && player.LastGrounded && player.Velocity.Y > 0.5f)
            {
                Footstep(player, FootstepKind.Jump, f.JumpRadius);
            }

            if (player.Stance == Stance.Sliding && player.LastStance != Stance.Sliding)
            {
                Footstep(player, FootstepKind.Slide, f.SlideRadius);
            }

            if (player.Grounded && player.Stance != Stance.Sliding)
            {
                player.StrideDistance += distance;
                if (player.StrideDistance >= f.Stride)
                {
                    player.StrideDistance %= f.Stride;
                    float speed = player.HorizontalSpeed;
                    float radius = player.Stance == Stance.Crouching ? f.CrouchRadius
                        : player.Sprinting ? f.SprintRadius
                        : speed <= (m.WalkSpeed + m.RunSpeed) * 0.5f ? f.WalkRadius
                        : f.RunRadius;
                    Footstep(player, FootstepKind.Step, radius);
                }
            }
        }

        player.LastPosition = position;
        player.LastVelocity = player.Velocity;
        player.LastGrounded = player.Grounded;
        player.LastStance = player.Stance;
    }

    /// <summary>Breaks within the spray radius of someone's face paint their mask (spec §1.3), stronger the closer they are.</summary>
    private void SprayMasks(int firstEvent)
    {
        float radius = Config.Hitboxes.MaskSprayRadius;
        int count = Events.Count;
        for (int i = firstEvent; i < count && radius > 0f; i++)
        {
            SimEvent e = Events.Items[i];
            if (e.Type != SimEventType.BallBroke)
            {
                continue;
            }

            foreach (PlayerState p in _players)
            {
                float distance = Vector3.Distance(p.EyePosition, e.Position);
                if (p.Present && distance < radius)
                {
                    Events.Add(new SimEvent
                    {
                        Type = SimEventType.MaskSprayed, Tick = Tick, PlayerId = e.PlayerId, TargetId = p.Id, Team = e.Team,
                        Position = e.Position, Value = 1f - distance / radius, ColliderId = -1,
                    });
                }
            }
        }
    }

    private void Footstep(PlayerState player, FootstepKind kind, float radius) =>
        Events.Add(new SimEvent
        {
            Type = SimEventType.Footstep, Tick = Tick, PlayerId = player.Id, Team = player.Team, Position = player.Position,
            Surface = player.GroundSurface, Value = radius * Config.Movement.Footsteps.Loudness(player.GroundSurface),
            Extra = (int)kind, TargetId = -1, ColliderId = -1,
        });
}
