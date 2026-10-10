using System.Numerics;
using Pb.Sim.AI;
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

/// <summary>Who decides what happens in a <see cref="SimWorld"/>.</summary>
public enum SimRole : byte
{
    /// <summary>Everything: offline, a host or a dedicated server.</summary>
    Authority,

    /// <summary>
    /// A copy joined to a server: it predicts its own player (<see cref="SimWorld.LocalPlayerId"/>) and flies the balls for
    /// show, while everyone else, the doors, pickups and the round come from the server.
    /// </summary>
    Client,
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
    private readonly SimEventQueue _replayEvents = new(64);
    private SimRole _role;

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

    /// <summary>The level's ladders (none on the range), climbed by the movement rules.</summary>
    public LadderSet Ladders { get; } = new();

    /// <summary>Players may move and fire: always without a match, and only while it's live with one.</summary>
    public bool IsLive => Match is null || Match.Phase == MatchPhase.Live;

    /// <summary>
    /// <see cref="SimRole.Authority"/> (the default) decides everything. On a <see cref="SimRole.Client"/> a step runs
    /// only <see cref="LocalPlayerId"/>'s marker and look, everyone's footsteps and the balls, which break on players
    /// without putting anyone out; the host poses everyone else, the doors and the round from the server.
    /// </summary>
    public SimRole Role
    {
        get => _role;
        set
        {
            _role = value;
            PlayerHits.Decides = value == SimRole.Authority;
        }
    }

    /// <summary>On a client, the player this copy predicts: its own (−1 for none).</summary>
    public int LocalPlayerId { get; set; } = -1;

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
        Ladders.Load(level.Ladders);
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
        Ladders.Load(Array.Empty<LadderSpec>());
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
    /// gear by the tier (full loaders and tanks, the tier's pods for people and for bots), pickups out or not, stats
    /// from zero. Every mode is won by the last team standing; an objective (its attackers the first person's side,
    /// unless the setup names them) is another way to win, and so is a speedball point's buzzer (on the level's field).
    /// The round waits in the briefing until <see cref="GoLive"/>.
    /// </summary>
    public MatchState StartMatch(MatchSetup setup)
    {
        ObjectiveKind kind = setup.Mode == MatchModeKind.FreeForAll ? ObjectiveKind.Eliminate : setup.Objective;
        ObjectiveState? objective = null;
        IMatchMode mode = LastTeamStandingMode.Instance;
        int heroTeam = FindPlayer(setup.HeroId)?.Team ?? -1;
        byte attackers = setup.Attackers ?? (byte)Math.Max(0, heroTeam);
        if (kind != ObjectiveKind.Eliminate)
        {
            LevelObjectives places = Level?.Objectives ?? LevelObjectives.None;
            if (!places.Offers(kind))
            {
                throw new InvalidOperationException($"{Level?.Id ?? "the range"} has no places for {kind.ToString().ToLowerInvariant()}");
            }

            objective = new ObjectiveState(kind, attackers, places, Config.Rules.Objectives, MatchSeed);
            mode = kind == ObjectiveKind.Retrieve ? RetrieveMode.Instance : HoldMode.Instance;
        }

        BuzzerSet? buzzers = null;
        if (setup.Format == MatchFormat.Speedball)
        {
            if (Level?.Field is not { } field)
            {
                throw new InvalidOperationException($"{Level?.Id ?? "the range"} has no field to play speedball on");
            }

            buzzers = new BuzzerSet(field.Buzzers, Config.Rules.Speedball);
            mode = SpeedballMode.Instance;
        }

        FlagSet? flags = null;
        if (setup.Format == MatchFormat.Flag)
        {
            // On a field the one flag stands on its centre bunker and scores at the other side's buzzer; elsewhere each
            // side's stands at its base, where its first player starts (the same on every copy: the roster's order).
            flags = Level?.Field is { } field && Level.FieldLayout is { } layout
                ? FlagSet.Centre(layout.FlagHome, field.Buzzers, Config.Rules.Flag)
                : FlagSet.Bases(BaseOf(0), BaseOf(1), Config.Rules.Flag);
            mode = CaptureMode.Instance;
        }

        Match = new MatchState(setup, Config.Rules, mode, attackers, heroTeam, objective, buzzers, flags);
        foreach (PlayerState p in _players)
        {
            p.SprintBlocked = false;
            p.Marker.ResetGear();
            p.Marker.Paint.FillWith(setup.IsPerson(p.Id) ? setup.StartPods : setup.BotPods);
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

    /// <summary>Side <paramref name="side"/>'s base in capture the flag: where its first player starts (the middle if it has none).</summary>
    private Vector3 BaseOf(int side)
    {
        foreach (PlayerState p in _players)
        {
            if (p.Team == side)
            {
                return p.Position;
            }
        }

        return Vector3.Zero;
    }

    /// <summary>
    /// Someone playing with others has left mid-round: at the next step they're off the field and count as out, put out by
    /// nobody (a <see cref="SimEventType.PlayerEliminated"/> with no shooter and <see cref="SimEvent.Extra"/> −1). Only the
    /// authority decides it; a joining copy hears it in the server's events.
    /// </summary>
    public void Withdraw(int playerId)
    {
        if (_role == SimRole.Authority && FindPlayer(playerId) is { } player)
        {
            player.Left = true;
        }
    }

    public void Step(ReadOnlySpan<InputCommand> commands)
    {
        double t0 = Time;
        float dt = Dt;
        int firstEvent = Events.Count;
        bool client = _role == SimRole.Client;
        Targets.Update(t0);
        PlayerHits.Record(Tick);
        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i].Left && _players[i].Present)
            {
                TakeOff(_players[i]);
            }
        }

        for (int i = 0; i < _players.Count; i++)
        {
            PlayerState player = _players[i];
            if (client && player.Id != LocalPlayerId)
            {
                // Someone else on a joining copy, posed from the server's snapshots: only the footsteps their movement
                // makes are worked out here (their shots come from the server).
                UpdateFootsteps(player);
                continue;
            }

            if (i < commands.Length)
            {
                InputCommand cmd = commands[i];
                bool climbing = Look(player, cmd, dt);
                UpdateFootsteps(player);
                int count = MarkerStep(player, cmd, t0, dt, climbing, Events);
                byte rewind = Math.Min(cmd.Rewind, (byte)(PlayerHitboxes.HistoryTicks - 1));
                for (int k = 0; k < count; k++)
                {
                    FireShot(player, _shots[k], rewind);
                }

                if (!client)
                {
                    bool interact = IsLive && player.Alive && !climbing && cmd.Has(InputButtons.Interact);
                    Doors.Interact(this, player, interact, dt);
                    Match?.Buzzers?.Hold(this, player, interact);
                    CallOut(player, cmd.Has(InputButtons.Callout), t0);
                }
            }
        }

        // A joining copy takes the doors, the pickups and the round from the server.
        if (!client)
        {
            Doors.Step(this, dt);
            if (IsLive)
            {
                Pickups.Update(this, Config.Rules);
            }
        }

        Stress?.Update(Ballistics, MatchSeed, Config.Shot.MuzzleVelocity, Config.Shot.VelocityVariance, Tick, dt, Events);
        Ballistics.Tick(Tick, dt, Events);
        SprayMasks(firstEvent);
        if (!client)
        {
            Match?.Update(this, firstEvent);
        }

        Tick++;
        Time += dt;
    }

    /// <summary>
    /// The callout key, on its press: "Contact!" about the opponent nearest the aim (within the rules' cone and range, in
    /// sight), or where the caller is looking when there's none, for the caller's side to take as a contact.
    /// </summary>
    private void CallOut(PlayerState player, bool held, double now)
    {
        bool pressed = held && !player.CalloutHeld;
        player.CalloutHeld = held;
        CalloutRules rules = Config.Rules.Callout;
        if (!pressed || !player.Alive || !IsLive || now - player.CalledOutAt < rules.Cooldown)
        {
            return;
        }

        player.CalledOutAt = now;
        Vector3 eye = player.EyePosition;
        Vector3 aim = ViewAngles.Forward(player.Yaw, player.Pitch);
        int best = -1;
        float bestCos = rules.ConeCos;
        Vector3 at = default;
        for (int i = 0; i < _players.Count; i++)
        {
            PlayerState other = _players[i];
            if (other == player || !other.Alive || !other.Present || other.Team == player.Team)
            {
                continue;
            }

            Vector3 to = other.EyePosition - eye;
            float distance = to.Length();
            if (distance < 0.5f || distance > rules.Range)
            {
                continue;
            }

            float cos = Vector3.Dot(to / distance, aim);
            if (cos < bestCos || Collision.SweepSphere(eye, other.EyePosition, 0.01f, out _))
            {
                continue;
            }

            best = other.Id;
            bestCos = cos;
            at = other.Position;
        }

        if (best < 0)
        {
            Vector3 end = eye + aim * rules.Range;
            at = Collision.SweepSphere(eye, end, 0.01f, out SweepHit hit) ? hit.Point : end;
        }

        Events.Add(new SimEvent
        {
            Type = SimEventType.CalledOut, Tick = Tick, PlayerId = player.Id, TargetId = best, Team = player.Team, Position = at, ColliderId = -1,
        });
    }

    /// <summary>Someone who left: out (if they were still in) and off the field at once.</summary>
    private void TakeOff(PlayerState player)
    {
        player.Present = false;
        player.Sprinting = false;
        if (!player.Alive)
        {
            return;
        }

        player.Alive = false;
        player.EliminatedBy = -1;
        player.EliminatedTick = Tick;
        Events.Add(new SimEvent
        {
            Type = SimEventType.PlayerEliminated, Tick = Tick, PlayerId = -1, TargetId = player.Id, Team = player.Team,
            Position = player.Position, Normal = Vector3.UnitY, Extra = -1, ColliderId = -1,
        });
    }

    /// <summary>
    /// A joining copy correcting its own player after the server's state was put back: runs <paramref name="cmd"/>'s look,
    /// tuck and marker for <paramref name="player"/> again as of <paramref name="t0"/>, the sim time it first ran at, firing
    /// nothing and telling nobody (that all happened the first time). The host replays the movement first, as before a step.
    /// </summary>
    public void ReplayLocal(PlayerState player, in InputCommand cmd, double t0)
    {
        bool climbing = Look(player, cmd, Dt);
        MarkerStep(player, cmd, t0, Dt, climbing, _replayEvents);
        _replayEvents.Clear();
    }

    /// <summary>The look a command gives (on a ladder the body faces it and the head looks round) and the marker's tuck.</summary>
    private bool Look(PlayerState player, in InputCommand cmd, float dt)
    {
        // On a ladder the body faces it and the head turns to look round (as far as a head turns); hands on the rungs,
        // the marker can neither fire nor refill.
        bool climbing = player.Ladder >= 0 && player.Ladder < Ladders.Count;
        float body = climbing ? Ladders[player.Ladder].Facing : cmd.Yaw;
        float head = climbing ? BotAim.Wrap(cmd.Yaw + cmd.HeadYaw - body) : cmd.HeadYaw;
        player.Yaw = body;
        player.Pitch = Math.Clamp(cmd.Pitch, -Config.Movement.MaxPitch, Config.Movement.MaxPitch);
        player.HeadYaw = player.Alive ? Math.Clamp(head, -Config.Movement.MaxHeadTurn, Config.Movement.MaxHeadTurn) : 0f;
        UpdateTuck(player, dt);
        return climbing;
    }

    /// <summary>The marker's tick: the shots it fires go in the step's buffer, its events in <paramref name="events"/>.</summary>
    private int MarkerStep(PlayerState player, in InputCommand cmd, double t0, float dt, bool climbing, SimEventQueue events)
    {
        bool live = IsLive;
        var input = new MarkerInput(
            live && !climbing && cmd.Has(InputButtons.Fire), live && !climbing && cmd.Has(InputButtons.Refill),
            cmd.Has(InputButtons.ToggleFireMode), player.Sprinting, player.Alive, player.MarkerReady);
        return player.Marker.Update(t0, dt, input, _shots, events, player.Id, player.Team, Tick);
    }

    /// <summary>
    /// A joining copy: flies, for show, a ball the server says someone else fired, as it is at the end of the shot's tick
    /// (its first step already flown: a copy applies the server's events after its own step). It hits nothing here: the
    /// server's events say where it bounced and ended. <paramref name="firstStep"/> and <paramref name="streamDraws"/> come
    /// with the shot's <see cref="SimEventType.ShotFired"/>, so it flies as the server's does and picks up its random stream.
    /// </summary>
    public bool SpawnRemoteShot(int owner, uint sequence, byte team, Vector3 origin, Vector3 velocity, float firstStep, int streamDraws)
    {
        var rng = new Pcg32(SeedHash.Shot(MatchSeed, owner, sequence));
        for (int k = 0; k < streamDraws; k++)
        {
            rng.NextUInt();
        }

        if (!Ballistics.Spawn(origin, velocity, owner, sequence, team, rng, firstStep, Tick, Events, remote: true, streamDraws: streamDraws))
        {
            return false;
        }

        Ballistics.Advance(Ballistics.Pool.Count - 1, firstStep);
        return true;
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
    /// matters while shots still converge on the aim point. Players under the crosshair are found as the
    /// shooter saw them, <paramref name="rewind"/> ticks back (lag compensation).
    /// </summary>
    public ShotSolution SolveShot(PlayerState player, int rewind = 0)
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

        if (Receivers.SweepSphere(eye, far, 0f, Tick - rewind, player.Id, out HitboxHit th) && th.T < nearest)
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

    private void FireShot(PlayerState player, in ShotRequest shot, byte rewind)
    {
        ShotParams p = Config.Shot;
        ShotSolution solution = SolveShot(player, rewind);
        var rng = new Pcg32(SeedHash.Shot(MatchSeed, player.Id, shot.Sequence));
        float cone = DispersionFor(player.HorizontalSpeed);
        Vector3 direction = Dispersion.SampleCone(solution.Direction, cone, ref rng);
        float speed = MathF.Max(0.5f, shot.MuzzleSpeed + rng.Symmetric(p.VelocityVariance));
        // The cone draws two numbers from the shot's stream and the speed one; the ball's break rolls take it on from there.
        int draws = cone > 0f ? 3 : 1;
        Vector3 velocity = direction * speed + player.Velocity * p.InheritShooterVelocity;
        if (solution.MuzzleBlocked && p.MuzzleBlockedBreaks)
        {
            Ballistics.BreakAtMuzzle(solution.Origin, velocity, solution.Block, player.Id, shot.Sequence, player.Team, Tick, Events);
            return;
        }

        float firstStep = MathF.Max(1e-5f, Dt - shot.TimeOffset);
        Ballistics.Spawn(solution.Origin, velocity, player.Id, shot.Sequence, player.Team, rng, firstStep, Tick, Events, rewind,
            streamDraws: draws);
    }

    /// <summary>
    /// Brings the marker up off whatever is in front of it (<see cref="PlayerState.Tuck"/>): the least pitch,
    /// in the rig's steps and then refined, at which the barrel from the shoulder to the muzzle clears the
    /// world, reached at the tuck rate (at once before the round goes live, so nobody starts with the
    /// barrel in a wall). Eliminated players' gear is up anyway.
    /// </summary>
    private void UpdateTuck(PlayerState player, float dt)
    {
        HitboxParams rig = Config.Hitboxes;
        float target = 0f;
        if (player.Alive && player.Present && !player.OnLadder && rig.TuckMax > 0f && !BarrelClear(player, 0f))
        {
            target = rig.TuckMax;
            float clear = -1f;
            for (float angle = rig.TuckStep; angle < rig.TuckMax + 1e-4f; angle += rig.TuckStep)
            {
                if (BarrelClear(player, MathF.Min(angle, rig.TuckMax)))
                {
                    clear = MathF.Min(angle, rig.TuckMax);
                    break;
                }
            }

            if (clear > 0f)
            {
                // Between the last blocked step and the first clear one.
                float low = clear - rig.TuckStep, high = clear;
                for (int k = 0; k < 3; k++)
                {
                    float mid = (low + high) * 0.5f;
                    if (BarrelClear(player, mid))
                    {
                        high = mid;
                    }
                    else
                    {
                        low = mid;
                    }
                }

                target = high;
            }
        }

        float step = IsLive ? rig.TuckRate * dt : float.MaxValue;
        player.Tuck = MathF.Abs(target - player.Tuck) <= step ? target : player.Tuck + MathF.CopySign(step, target - player.Tuck);
    }

    private bool BarrelClear(PlayerState player, float tuck)
    {
        (Vector3 back, Vector3 front) = HitboxRig.MarkerLine(player.EyePosition, player.Yaw, player.Pitch, player.LeanRoll, player.Shoulder, tuck,
            Config.Hitboxes);
        return !Collision.SweepSphere(back, front, Config.Hitboxes.TuckBarrelRadius, out _);
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

        if (player.Alive && player.Ladder >= 0 && player.Ladder < Ladders.Count)
        {
            // A foot on a rung every so often as they climb, heard like a step on what the rungs are made of.
            float climbed = MathF.Abs(position.Y - player.LastPosition.Y);
            player.ClimbDistance += climbed < 2f ? climbed : 0f;
            if (player.ClimbDistance >= f.ClimbStride)
            {
                player.ClimbDistance %= f.ClimbStride;
                player.GroundSurface = Ladders[player.Ladder].Surface;
                Footstep(player, FootstepKind.Step, f.ClimbRadius);
            }
        }
        else if (player.Alive)
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
