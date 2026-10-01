using System.Collections.Generic;
using Godot;
using Pb.Game.Player;
using Pb.Game.World;
using Pb.Sim;
using Pb.Sim.Core;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Players;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// Headless end-to-end check of a compound level (<c>-- --smoke-test</c> on Level.tscn), through
/// the real scene, walking collision and presentation code:
/// <list type="number">
/// <item>the autopilot walks in through the main gate, sweeping its aim and firing;</item>
/// <item>it climbs every flight of stairs in the level, starting at the foot of each;</item>
/// <item>back at the spawn, it sprints, slides into a crouch, stands and jumps.</item>
/// </list>
/// It passes only if the player walked well into the compound without falling through the world,
/// paint flew and broke on level geometry, every flight was climbed to its landing, the slide and
/// the jump worked, and nothing threw.
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

    private readonly Node _host;
    private readonly SimWorld _sim;
    private readonly SimDriver _driver;
    private readonly PlayerController _player;
    private readonly LevelBuilder _world;
    private readonly int _walkTicks;
    private readonly SVector3 _start;
    private readonly List<Climb> _climbs;
    private readonly ScriptedPilot _pilot;
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
    private bool _moves;
    private bool _slid;
    private bool _slideEndedCrouched;
    private float _jumpBaseY;
    private float _jumpHeight;

    public LevelSmokeTest(Node host, SimWorld sim, SimDriver driver, PlayerController player, LevelBuilder world, int ticks)
    {
        _host = host;
        _sim = sim;
        _driver = driver;
        _player = player;
        _world = world;
        _walkTicks = ticks;
        _start = player.State.Position;
        _climbs = sim.Level is { } level ? FindClimbs(level) : new List<Climb>();
        _pilot = new ScriptedPilot(sim);
        driver.Ticked += _ => AfterTick();
        GD.Print($"SMOKE start: level {sim.Level?.Id}, {ticks} ticks walking in at {sim.Config.TickRate} Hz, " +
                 $"then {_climbs.Count} flights of stairs, then a slide and a jump");
    }

    public ICommandSource Pilot => _pilot;

    public void OnSimEvent(in SimEvent e)
    {
        switch (e.Type)
        {
            case SimEventType.ShotFired when e.PlayerId >= 0:
                _shots++;
                break;
            case SimEventType.BallBroke:
                _breaks++;
                break;
            case SimEventType.BallBounced:
                _bounces++;
                break;
        }
    }

    private void AfterTick()
    {
        PlayerState state = _player.State;
        SVector3 p = state.Position;
        _elapsed++;
        int t = _elapsed - _phaseStart;
        if (_moves)
        {
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
                Finish(state);
            }
        }
        else if (_climb < 0)
        {
            _travelled = System.MathF.Max(_travelled, SVector3.Distance(new SVector3(p.X, 0f, p.Z), new SVector3(_start.X, 0f, _start.Z)));
            _lowestY = System.MathF.Min(_lowestY, p.Y);
            if (t >= _walkTicks)
            {
                NextClimb();
            }
        }
        else
        {
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
        }
    }

    private void NextClimb()
    {
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
        _moves = true;
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

    private void Finish(PlayerState state)
    {
        bool slideOk = _slid && _slideEndedCrouched;
        bool jumpOk = _jumpHeight >= 0.35f && state.Grounded && state.Stance == Stance.Standing;
        GD.Print($"SMOKE moves: slide {(slideOk ? "ok" : "FAILED")} (slid={_slid}, ended crouched={_slideEndedCrouched}), " +
                 $"jump {(jumpOk ? "ok" : "FAILED")} (height {_jumpHeight:0.00} m, landed={state.Grounded})");

        bool ok = _travelled > 15f && _lowestY > -0.5f && _shots > 0 && _breaks > 0 && _climbsFailed == 0 && slideOk && jumpOk &&
                  _driver.ErrorCount == 0 && _world.MeshCount > 0 && _world.ColliderCount > 0;
        GD.Print($"SMOKE {(ok ? "PASS" : "FAIL")}: ticks={_elapsed} travelled={_travelled:0.0}m lowestY={_lowestY:0.00} " +
                 $"shots={_shots} breaks={_breaks} bounces={_bounces} climbs={_climbs.Count - _climbsFailed}/{_climbs.Count} " +
                 $"meshes={_world.MeshCount} walkColliders={_world.ColliderCount} simErrors={_driver.ErrorCount} " +
                 $"avgStepMs={_driver.AverageStepMs:0.000}");
        _host.GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// Every sloped stair ramp (the walking surface the kit lays over the steps) becomes a climb:
    /// start half a metre before the bottom step, facing up the flight.
    /// </summary>
    private static List<Climb> FindClimbs(LevelLayout level)
    {
        var climbs = new List<Climb>();
        foreach (LevelPrimitive p in level.Primitives)
        {
            if (p.Role != PrimitiveRole.Ramp || p.HalfExtents.Z < 0.5f)
            {
                continue; // the flat landing pieces
            }

            SVector3 uphill = SVector3.Transform(-SVector3.UnitZ, p.Rotation);
            SVector3 normal = SVector3.Transform(SVector3.UnitY, p.Rotation);
            SVector3 surface = p.Center + normal * p.HalfExtents.Y;
            SVector3 bottom = surface - uphill * p.HalfExtents.Z;
            SVector3 top = surface + uphill * p.HalfExtents.Z;
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
