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
/// <item>then it climbs every flight of stairs in the level, starting at the foot of each.</item>
/// </list>
/// It passes only if the player walked well into the compound without falling through the world,
/// paint flew and broke on level geometry, every flight was climbed to its landing, and nothing
/// threw.
/// </summary>
public sealed class LevelSmokeTest
{
    /// <summary>Ticks allowed per flight of stairs (4 s at 120 Hz).</summary>
    private const int ClimbTicks = 480;

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
    private int _shots;
    private int _breaks;
    private int _bounces;
    private float _travelled;
    private float _lowestY = float.MaxValue;
    private int _climb = -1;
    private float _climbHighest;
    private int _climbsFailed;

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
        GD.Print($"SMOKE start: level {sim.Level?.Id}, {ticks} ticks walking in at {sim.Config.TickRate} Hz, then {_climbs.Count} flights of stairs");
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
        SVector3 p = _player.State.Position;
        _elapsed++;
        if (_climb < 0)
        {
            _travelled = System.MathF.Max(_travelled, SVector3.Distance(new SVector3(p.X, 0f, p.Z), new SVector3(_start.X, 0f, _start.Z)));
            _lowestY = System.MathF.Min(_lowestY, p.Y);
            if (_elapsed >= _walkTicks)
            {
                NextClimb();
            }

            return;
        }

        _climbHighest = System.MathF.Max(_climbHighest, p.Y);
        if (_elapsed >= _walkTicks + (_climb + 1) * ClimbTicks)
        {
            Climb c = _climbs[_climb];
            bool reached = _climbHighest >= c.TopY - 0.2f;
            _climbsFailed += reached ? 0 : 1;
            GD.Print($"SMOKE climb {c.Name}: from y={c.Start.Y:0.00} to the landing at {c.TopY:0.00}, reached {_climbHighest:0.00} " +
                     (reached ? "ok" : "FAILED"));
            NextClimb();
        }
    }

    private void NextClimb()
    {
        _climb++;
        if (_climb < _climbs.Count)
        {
            Climb c = _climbs[_climb];
            _player.Teleport(c.Start, c.Yaw);
            _pilot.ClimbYaw = c.Yaw;
            _climbHighest = float.MinValue;
            return;
        }

        bool ok = _travelled > 15f && _lowestY > -0.5f && _shots > 0 && _breaks > 0 && _climbsFailed == 0 &&
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
    /// Walks north from the spawn through the gate road, sweeping its aim and pulling the trigger;
    /// once <see cref="ClimbYaw"/> is set, walks straight ahead along it without firing.
    /// </summary>
    private sealed class ScriptedPilot : ICommandSource
    {
        private readonly SimWorld _sim;

        public ScriptedPilot(SimWorld sim)
        {
            _sim = sim;
        }

        public float? ClimbYaw { get; set; }

        public InputCommand Next(int tick, PlayerState state)
        {
            if (ClimbYaw is { } climbYaw)
            {
                return new InputCommand { Tick = tick, Move = new System.Numerics.Vector2(0f, 1f), Yaw = climbYaw, Pitch = 0f };
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
