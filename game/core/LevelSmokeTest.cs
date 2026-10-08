using System.Collections.Generic;
using Godot;
using Pb.Game.Player;
using Pb.Game.World;
using Pb.Sim;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// Headless end-to-end check of a compound level (<c>-- --smoke-test</c> on Level.tscn), through
/// the real scene, walking collision and presentation code:
/// <list type="number">
/// <item>the autopilot walks in through the main gate, sweeping its aim and firing;</item>
/// <item>it climbs every ladder to the top and steps off, then gets back on facing out over the top and climbs down;</item>
/// <item>it climbs every flight of stairs and every ramp in the level, starting at the foot of each;</item>
/// <item>back at the spawn, it sprints, slides into a crouch, stands and jumps;</item>
/// <item>in front of a shut door it shoots the door (the ball must break on it), opens it with interact and walks through;</item>
/// <item>it shoots a passive bot, who must go out and walk off;</item>
/// <item>it stands in front of a sentry, now hostile, until it's eliminated and spectating, and the round ends as eliminated.</item>
/// </list>
/// It passes only if every step worked and nothing threw.
/// </summary>
public sealed class LevelSmokeTest
{
    /// <summary>Ticks allowed per flight of stairs (4 s at 120 Hz).</summary>
    private const int ClimbTicks = 480;

    // The moves script, in ticks from its start: sprint, then crouch while sprinting (a slide)
    // and hold the crouch, then stand, then jump.
    private const int SlideAt = 96;
    private const int StandAt = 276;
    private const int JumpAt = 330;
    private const int MovesTicks = 480;

    /// <summary>Time to put an opponent out (3 s), then to see them walk off (6 s more), and to get shot (12 s).</summary>
    private const int ShootTicks = 360;
    private const int WalkOffTicks = 720;
    private const int GetShotTicks = 1440;
    private const int RoundEndTicks = 360;

    // The door: fire at it, tap interact, then walk through once it's open.
    private const int DoorFireAt = 20;
    private const int DoorTapAt = 90;
    private const int DoorWalkAt = 260;
    private const int DoorTicks = 600;

    private enum Phase
    {
        Walk,
        Ladder,
        Climb,
        Moves,
        Door,
        Shoot,
        GetShot,
    }

    private readonly LevelMain _host;
    private readonly SimWorld _sim;
    private readonly SimDriver _driver;
    private readonly PlayerController _player;
    private readonly LevelBuilder _world;
    private readonly IReadOnlyList<OpponentPawn> _opponents;
    private readonly IReadOnlyList<BotBrain> _bots;
    private readonly int _walkTicks;
    private readonly SVector3 _start;
    private readonly List<Climb> _climbs;
    private readonly ScriptedPilot _pilot;
    private Phase _phase = Phase.Walk;
    private int _elapsed;
    private int _phaseStart;
    private int _shots;
    private int _breaks;
    private int _bounces;
    private float _travelled;
    private float _lowestY = float.MaxValue;
    private int _climb = -1;
    private float _climbHighest;
    private int _climbsFailed;
    private int _ladder = -1;
    private int _laddersFailed;
    private bool _ladderUp;
    private int _ladderUpAt = -1;
    private int _ladderDownFrom = -1;
    private bool _ladderOn;
    private bool _slid;
    private bool _slideEndedCrouched;
    private float _jumpBaseY;
    private float _jumpHeight;
    private PlayerState? _victim;
    private SVector3 _victimStart;
    private int _victimOutAt = -1;
    private bool _victimWalkedOff;
    private string _shootNote = "no target found";
    private string _shotNote = "no sentry found";
    private bool _gotShot;
    private int _gotShotAt = -1;
    private RoundOutcome _outcome;
    private int _door = -1;
    private bool _doorStopped;
    private bool _doorOpened;
    private bool _doorWalked;
    private string _doorNote = "no door found";

    public LevelSmokeTest(LevelMain host, SimWorld sim, SimDriver driver, PlayerController player, LevelBuilder world, int ticks,
        IReadOnlyList<OpponentPawn> opponents, IReadOnlyList<BotBrain> bots)
    {
        _host = host;
        _sim = sim;
        _driver = driver;
        _player = player;
        _world = world;
        _opponents = opponents;
        _bots = bots;
        _walkTicks = ticks;
        _start = player.State.Position;
        _climbs = sim.Level is { } level ? FindClimbs(level) : new List<Climb>();
        _pilot = new ScriptedPilot(sim);
        driver.Ticked += _ => AfterTick();
        GD.Print($"SMOKE start: level {sim.Level?.Id}, {ticks} ticks walking in at {sim.Config.TickRate} Hz, " +
                 $"then {sim.Ladders.Count} ladders up and down, {_climbs.Count} flights of stairs, a slide and a jump, and a duel with {opponents.Count} bots");
    }

    public ICommandSource Pilot => _pilot;

    public void OnSimEvent(in SimEvent e)
    {
        switch (e.Type)
        {
            case SimEventType.ShotFired when e.PlayerId == _player.State.Id:
                _shots++;
                break;
            case SimEventType.BallBroke:
                _breaks++;
                _doorStopped |= _door >= 0 && e.ColliderId == _sim.Doors.ColliderOf(_door);
                break;
            case SimEventType.BallBounced:
                _bounces++;
                break;
            case SimEventType.PlayerEliminated when _victim is not null && e.TargetId == _victim.Id && e.PlayerId == _player.State.Id:
                _victimOutAt = _elapsed;
                break;
        }
    }

    private void AfterTick()
    {
        PlayerState state = _player.State;
        SVector3 p = state.Position;
        _elapsed++;
        int t = _elapsed - _phaseStart;
        switch (_phase)
        {
            case Phase.Walk:
                _travelled = System.MathF.Max(_travelled, SVector3.Distance(new SVector3(p.X, 0f, p.Z), new SVector3(_start.X, 0f, _start.Z)));
                _lowestY = System.MathF.Min(_lowestY, p.Y);
                if (t >= _walkTicks)
                {
                    NextLadder();
                }

                break;

            case Phase.Ladder:
                StepLadder(state, t);
                break;

            case Phase.Climb:
                _climbHighest = System.MathF.Max(_climbHighest, p.Y);
                if (t >= ClimbTicks)
                {
                    Climb c = _climbs[_climb];
                    bool reached = _climbHighest >= c.TopY - 0.2f;
                    _climbsFailed += reached ? 0 : 1;
                    GD.Print($"SMOKE climb {c.Name}: from y={c.Start.Y:0.00} to the landing at {c.TopY:0.00}, reached {_climbHighest:0.00} " +
                             (reached ? "ok" : "FAILED"));
                    NextClimb();
                }

                break;

            case Phase.Moves:
                _slid |= state.Stance == Stance.Sliding;
                _slideEndedCrouched |= _slid && state.Stance == Stance.Crouching && t < StandAt;
                if (t == JumpAt)
                {
                    _jumpBaseY = p.Y;
                }
                else if (t > JumpAt)
                {
                    _jumpHeight = System.MathF.Max(_jumpHeight, p.Y - _jumpBaseY);
                }

                if (t >= MovesTicks)
                {
                    StartDoor();
                }

                break;

            case Phase.Door:
                if (_door >= 0)
                {
                    DoorSpec d = _sim.Doors[_door];
                    _doorOpened |= t >= DoorWalkAt - 20 && _sim.Doors.Open(_door) >= 0.95f;
                    _doorWalked |= SVector3.Dot(p - d.ShutCenter, d.Side) > 0.8f;
                }

                if (_door < 0 || t >= DoorTicks)
                {
                    if (_door >= 0)
                    {
                        _doorNote = $"{_sim.Level!.Owners[_sim.Doors[_door].Owner]} door: stopped a ball={_doorStopped}, opened={_doorOpened}, walked through={_doorWalked}";
                    }

                    StartShoot();
                }

                break;

            case Phase.Shoot:
                if (_victim is not null && _victimOutAt >= 0)
                {
                    _victimWalkedOff |= !_victim.Present || SVector3.Distance(_victim.Position, _victimStart) > 0.75f;
                }

                if (_victim is null || (_victimOutAt < 0 && t >= ShootTicks) || (_victimOutAt >= 0 && (_victimWalkedOff || _elapsed - _victimOutAt >= WalkOffTicks)))
                {
                    _shootNote = _victim is null ? _shootNote
                        : _victimOutAt < 0 ? $"{_victim.Name} still in after {ShootTicks} ticks"
                        : $"{_victim.Name} out after {_victimOutAt - _phaseStart} ticks, walked off={_victimWalkedOff}";
                    StartGetShot();
                }

                break;

            case Phase.GetShot:
                if (!_gotShot && _host.Spectating)
                {
                    _gotShot = true;
                    _gotShotAt = t;
                    _shotNote += $", eliminated by {_sim.FindPlayer(state.EliminatedBy)?.Name} after {t} ticks and spectating";
                }

                // Out, the round must end as eliminated once the balls still in the air have landed.
                if (_gotShot && (_sim.Match?.Phase == MatchPhase.Ended || t >= _gotShotAt + RoundEndTicks))
                {
                    _outcome = _sim.Match?.Outcome ?? RoundOutcome.None;
                    _shotNote += $"; round ended {_outcome} after {t - _gotShotAt} more ticks";
                    Finish(state);
                }
                else if (!_gotShot && t >= GetShotTicks)
                {
                    _shotNote += ", never hit";
                    Finish(state);
                }

                break;
        }
    }

    /// <summary>Up the next ladder from in front of its foot, facing it, until stepped off at the top; or on to the stairs.</summary>
    private void NextLadder()
    {
        _phase = Phase.Ladder;
        _phaseStart = _elapsed;
        _ladder++;
        if (_ladder >= _sim.Ladders.Count)
        {
            NextClimb();
            return;
        }

        LadderSpec l = _sim.Ladders[_ladder];
        _player.Teleport(l.ClimbPoint(l.Foot.Y, _sim.Config.Movement.Climbing.Standoff + 0.25f), l.Facing);
        _ladderUp = false;
        _ladderUpAt = -1;
        _ladderDownFrom = -1;
        _ladderOn = false;
        _pilot.Script = (_, _) =>
        {
            int t = _elapsed - _phaseStart;
            if (!_ladderUp)
            {
                return new InputCommand { Move = new System.Numerics.Vector2(0f, 1f), Yaw = l.Facing, Buttons = t < 2 ? InputButtons.Interact : InputButtons.None };
            }

            // At the top: turn round, then interact facing out over it, and back down.
            int down = t - _ladderUpAt;
            float outward = l.Facing + System.MathF.PI;
            return down < 30
                ? new InputCommand { Yaw = outward }
                : new InputCommand { Move = new System.Numerics.Vector2(0f, -1f), Yaw = outward, Buttons = down < 32 ? InputButtons.Interact : InputButtons.None };
        };
    }

    /// <summary>
    /// A ladder's climb: up, the body must step off at the top and stand on the floor there; then back on from the top and
    /// down, off at the bottom on the ground. Each way gets twice the time the climb takes, and a few seconds.
    /// </summary>
    private void StepLadder(PlayerState state, int t)
    {
        LadderSpec l = _sim.Ladders[_ladder];
        int budget = (int)((l.Height / _sim.Config.Movement.Climbing.Speed * 2f + 4f) * _sim.Config.TickRate);
        string name = $"{_sim.Level!.Owners[l.Owner]} ({l.Height:0.0} m)";
        _ladderOn |= state.OnLadder;
        if (!_ladderUp)
        {
            if (_ladderOn && !state.OnLadder && state.Grounded && state.Position.Y >= l.TopY - 0.1f)
            {
                _ladderUp = true;
                _ladderUpAt = t;
                _ladderOn = false;
            }
            else if (t >= budget)
            {
                _laddersFailed++;
                GD.Print($"SMOKE ladder {name}: FAILED up (on it={_ladderOn}, at y={state.Position.Y:0.00}, top {l.TopY:0.00})");
                NextLadder();
            }

            return;
        }

        if (_ladderOn && !state.OnLadder && _ladderDownFrom < 0)
        {
            _ladderDownFrom = t;
        }

        bool down = _ladderDownFrom >= 0 && state.Grounded && state.Position.Y <= l.Foot.Y + 0.1f;
        if (down || t - _ladderUpAt >= budget)
        {
            _laddersFailed += down ? 0 : 1;
            GD.Print($"SMOKE ladder {name}: up to {l.TopY:0.00} in {_ladderUpAt / _sim.Config.TickRate:0.0} s ok, " +
                     (down ? $"down to {state.Position.Y:0.00} in {(t - _ladderUpAt) / _sim.Config.TickRate:0.0} s ok"
                         : $"FAILED down (on it={_ladderOn}, at y={state.Position.Y:0.00})"));
            NextLadder();
        }
    }

    private void NextClimb()
    {
        _phase = Phase.Climb;
        _phaseStart = _elapsed;
        _climb++;
        if (_climb < _climbs.Count)
        {
            Climb c = _climbs[_climb];
            _player.Teleport(c.Start, c.Yaw);
            _pilot.Script = (_, _) => new InputCommand { Move = new System.Numerics.Vector2(0f, 1f), Yaw = c.Yaw };
            _climbHighest = float.MinValue;
            return;
        }

        // Back to the spawn, facing into the compound, for the moves.
        LevelLayout level = _sim.Level!;
        _phase = Phase.Moves;
        _player.Teleport(level.PlayerSpawn, level.PlayerSpawnYaw);
        float yaw = level.PlayerSpawnYaw;
        _pilot.Script = (_, _) =>
        {
            int t = _elapsed - _phaseStart;
            var forward = new System.Numerics.Vector2(0f, 1f);
            return t switch
            {
                < SlideAt => new InputCommand { Move = forward, Yaw = yaw, Buttons = InputButtons.Sprint },
                < StandAt => new InputCommand { Move = forward, Yaw = yaw, Buttons = InputButtons.Sprint | InputButtons.Crouch },
                >= JumpAt and < JumpAt + 4 => new InputCommand { Yaw = yaw, Buttons = InputButtons.Jump },
                _ => new InputCommand { Yaw = yaw },
            };
        };
    }

    /// <summary>
    /// In front of a ground-floor door, shut, on the side it doesn't open into: a shot at it, a tap on interact, and once
    /// it's open a walk straight through.
    /// </summary>
    private void StartDoor()
    {
        _phase = Phase.Door;
        _phaseStart = _elapsed;
        SVector3 up = new(0f, 1.2f, 0f);
        for (int i = 0; i < _sim.Doors.Count && _door < 0; i++)
        {
            DoorSpec d = _sim.Doors[i];
            SVector3 stand = d.ShutCenter - d.Side * 1.3f;
            SVector3 beyond = d.ShutCenter + d.Side * 1.6f;
            if (d.Hinge.Y > 0.2f || d.Partner >= 0 ||
                _sim.Collision.SweepSphere(stand + up, d.ShutCenter + up - d.Side * 0.1f, 0.3f, out _, includeDynamic: false) ||
                _sim.Collision.SweepSphere(d.ShutCenter + up + d.Side * 0.1f, beyond + up, 0.3f, out _, includeDynamic: false))
            {
                continue;
            }

            _door = i;
            _sim.Doors.SetOpen(i, 0f);
            _player.Teleport(stand, ScenePositions.Facing(stand, d.ShutCenter));
        }

        _pilot.Script = (_, me) =>
        {
            if (_door < 0)
            {
                return default;
            }

            int t = _elapsed - _phaseStart;
            DoorSpec d = _sim.Doors[_door];
            (float yaw, float pitch) = ViewAngles.FromDirection(d.ShutCenter + new SVector3(0f, 1.1f, 0f) - me.EyePosition);
            InputButtons buttons = t == DoorFireAt ? InputButtons.Fire : t == DoorTapAt ? InputButtons.Interact : InputButtons.None;
            var move = t >= DoorWalkAt ? new System.Numerics.Vector2(0f, 1f) : System.Numerics.Vector2.Zero;
            return new InputCommand { Yaw = t >= DoorWalkAt ? me.Yaw : yaw, Pitch = t >= DoorWalkAt ? 0f : pitch, Buttons = buttons, Move = move };
        };
    }

    /// <summary>Stands a few metres in front of a passive bot and shoots them in the chest.</summary>
    private void StartShoot()
    {
        _phase = Phase.Shoot;
        _phaseStart = _elapsed;
        // Anyone will do while nobody shoots back; leave the sentries for the next phase.
        foreach (bool sentries in new[] { false, true })
        {
            for (int i = 0; i < _opponents.Count && _victim is null; i++)
            {
                PlayerState o = _opponents[i].State;
                if (IsSentry(_bots[i]) == sentries && o.Alive && ScenePositions.FindSpot(_sim, o, 7f, out SVector3 spot))
                {
                    _victim = o;
                    _victimStart = o.Position;
                    _player.Teleport(spot, ScenePositions.Facing(spot, o.Position));
                }
            }
        }

        _pilot.Script = (tick, me) =>
        {
            if (_victim is null)
            {
                return default;
            }

            (float yaw, float pitch) = ScenePositions.AimAt(_sim, me, _victim);
            int t = _elapsed - _phaseStart;
            return new InputCommand { Yaw = yaw, Pitch = pitch, Buttons = t > 30 && t % 24 < 2 && _victim.Alive ? InputButtons.Fire : InputButtons.None };
        };
    }

    /// <summary>Wakes the bots and stands in front of a sentry until it (or another bot) gets us.</summary>
    private void StartGetShot()
    {
        _phase = Phase.GetShot;
        _phaseStart = _elapsed;
        foreach (BotBrain bot in _bots)
        {
            bot.Passive = false;
        }

        // In front of them, where they're looking: a sentry keeps watch the way it faces. Indoors a sentry may face a wall
        // close by, so failing that, anyone with room in front of them will do.
        foreach (bool sentriesOnly in new[] { true, false })
        {
            for (int i = 0; i < _opponents.Count && _shotNote == "no sentry found"; i++)
            {
                PlayerState o = _opponents[i].State;
                if ((IsSentry(_bots[i]) || !sentriesOnly) && o.Alive && ScenePositions.FindSpot(_sim, o, 9f, out SVector3 spot, maxTurn: 0.9f))
                {
                    _shotNote = $"facing {o.Name} ({_bots[i].Archetype.Id}) at {o.Position} from {spot}";
                    _player.Teleport(spot, ScenePositions.Facing(spot, o.Position));
                }
            }
        }

        _pilot.Script = (_, me) => new InputCommand { Yaw = me.Yaw, Pitch = 0f };
    }

    private static bool IsSentry(BotBrain bot) => bot.Archetype.Id == "sentry";

    private void Finish(PlayerState state)
    {
        bool slideOk = _slid && _slideEndedCrouched;
        bool jumpOk = _jumpHeight >= 0.35f;
        bool shootOk = _victimOutAt >= 0 && _victimWalkedOff;
        GD.Print($"SMOKE moves: slide {(slideOk ? "ok" : "FAILED")} (slid={_slid}, ended crouched={_slideEndedCrouched}), " +
                 $"jump {(jumpOk ? "ok" : "FAILED")} (height {_jumpHeight:0.00} m)");
        bool shotOk = _gotShot && _outcome == RoundOutcome.Eliminated;
        GD.Print($"SMOKE duel: shoot {(shootOk ? "ok" : "FAILED")} ({_shootNote}); get shot {(shotOk ? "ok" : "FAILED")} ({_shotNote})");
        // A level without doors has nothing to check here.
        bool doorOk = _sim.Doors.Count == 0 || (_doorStopped && _doorOpened && _doorWalked);
        GD.Print($"SMOKE door: {(doorOk ? "ok" : "FAILED")} ({_doorNote})");

        // Sound: the bank rendered whole, every kind of event made its sound (headless, through the dummy driver), and the
        // referee called the round.
        Pb.Game.Audio.AudioDirector audio = _host.Audio;
        Pb.Game.Audio.SoundBank? bank = audio.Bank;
        bool audioOk = bank is { Problems.Count: 0 } && audio.Shots > 0 && audio.Breaks > 0 && audio.Steps > 0 &&
                       (_sim.Doors.Count == 0 || audio.Doors > 0) && _host.Referee.Called.Count > 0;
        GD.Print($"SMOKE audio: {(audioOk ? "ok" : "FAILED")} (" +
                 (bank is null ? "no sound bank" : $"{bank.SoundCount} sounds in {bank.VariationCount} variations, rendered in {bank.RenderMilliseconds:0} ms") +
                 $"; shots {audio.Shots}, breaks {audio.Breaks}, bounces {audio.Bounces}, steps {audio.Steps}, doors {audio.Doors}, cues {audio.Cues}: " +
                 $"{audio.Played} played from where they happened ({audio.Muffled} through walls), {audio.Skipped} out of earshot or over the budget; " +
                 $"callouts {audio.Spoken} voiced, {audio.Unvoiced} subtitles only; ambience {audio.Ambience?.Where} (tone '{audio.Ambience?.Tone}', " +
                 $"{audio.Ambience?.Crows} crows); referee: {string.Join(" / ", _host.Referee.Called)})");

        bool ok = _travelled > 15f && _lowestY > -0.5f && _shots > 0 && _breaks > 0 && _climbsFailed == 0 && _laddersFailed == 0 && slideOk && jumpOk &&
                  doorOk && shootOk && shotOk && audioOk && _driver.ErrorCount == 0 && _world.MeshCount > 0 && _world.ColliderCount > 0;
        GD.Print($"SMOKE {(ok ? "PASS" : "FAIL")}: ticks={_elapsed} travelled={_travelled:0.0}m lowestY={_lowestY:0.00} " +
                 $"shots={_shots} breaks={_breaks} bounces={_bounces} climbs={_climbs.Count - _climbsFailed}/{_climbs.Count} " +
                 $"ladders={_sim.Ladders.Count - _laddersFailed}/{_sim.Ladders.Count} " +
                 $"meshes={_world.MeshCount} walkColliders={_world.ColliderCount} simErrors={_driver.ErrorCount} " +
                 $"avgStepMs={_driver.AverageStepMs:0.000}");
        _host.GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// Every sloped stair ramp (the walking surface the kit lays over the steps) and every sloped walking surface of a
    /// prop (a trailer's loading ramp) becomes a climb: start half a metre before the bottom, facing up the slope.
    /// </summary>
    private static List<Climb> FindClimbs(LevelLayout level)
    {
        var climbs = new List<Climb>();
        foreach (LevelPrimitive p in level.Primitives)
        {
            bool stairs = p.Role == PrimitiveRole.Ramp && p.HalfExtents.Z >= 0.5f; // not the flat landing pieces
            float tilt = System.MathF.Acos(System.Math.Clamp(SVector3.Transform(SVector3.UnitY, p.Rotation).Y, -1f, 1f));
            bool propRamp = p.Role == PrimitiveRole.Prop && p.Kind == PrimitiveKind.Box && p.Has(PrimitiveFlags.Walk) && tilt is > 0.15f and < 0.8f;
            if (!stairs && !propRamp)
            {
                continue;
            }

            SVector3 uphill = SVector3.Transform(-SVector3.UnitZ, p.Rotation);
            SVector3 normal = SVector3.Transform(SVector3.UnitY, p.Rotation);
            SVector3 surface = p.Center + normal * p.HalfExtents.Y;
            SVector3 bottom = surface - uphill * p.HalfExtents.Z;
            SVector3 top = surface + uphill * p.HalfExtents.Z;
            if (propRamp && top.Y - bottom.Y < 0.5f)
            {
                continue; // a tilted piece of a heap, not a way up
            }

            SVector3 flat = SVector3.Normalize(new SVector3(uphill.X, 0f, uphill.Z));
            SVector3 start = new SVector3(bottom.X, bottom.Y + 0.05f, bottom.Z) - flat * 0.5f;
            float yaw = System.MathF.Atan2(-flat.X, -flat.Z);
            climbs.Add(new Climb($"{level.Owners[p.Owner]}@({bottom.X:0.0}, {bottom.Z:0.0})", start, yaw, top.Y));
        }

        return climbs;
    }

    private readonly record struct Climb(string Name, SVector3 Start, float Yaw, float TopY);

    /// <summary>
    /// Walks north from the spawn through the gate road, sweeping its aim and pulling the trigger,
    /// until a <see cref="Script"/> takes over.
    /// </summary>
    private sealed class ScriptedPilot : ICommandSource
    {
        private readonly SimWorld _sim;

        public ScriptedPilot(SimWorld sim)
        {
            _sim = sim;
        }

        public System.Func<int, PlayerState, InputCommand>? Script { get; set; }

        public InputCommand Next(int tick, PlayerState state)
        {
            if (Script is not null)
            {
                InputCommand scripted = Script(tick, state);
                scripted.Tick = tick;
                return scripted;
            }

            float t = tick / _sim.Config.TickRate;
            float yaw = (_sim.Level?.PlayerSpawnYaw ?? 0f) + 0.6f * System.MathF.Sin(t * 0.8f);
            InputButtons buttons = tick % 30 < 4 ? InputButtons.Fire : InputButtons.None;
            if (state.Marker.Paint.Loader == 0 && !state.Marker.Refill.Active)
            {
                buttons = InputButtons.Refill;
            }

            // Move north in world terms regardless of where we're looking.
            SVector3 north = new(0f, 0f, -1f);
            SVector3 right = ViewAngles.Right(yaw), forward = ViewAngles.FlatForward(yaw);
            var move = new System.Numerics.Vector2(SVector3.Dot(north, right), SVector3.Dot(north, forward));
            return new InputCommand { Tick = tick, Move = move, Yaw = yaw, Pitch = -0.05f, Buttons = buttons };
        }
    }
}
