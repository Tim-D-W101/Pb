using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Players;
using Xunit;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>
/// Climbing ladders, on the Rail Yard's water tower (at x 64, z −33): a ladder up the middle of its west side to a gap
/// in its catwalk's railing 12.2 m up, and one from the catwalk up the tank's east side to its roof at 15.6 m. You move
/// over the navigation grid as bots do in headless runs; climbing is the movement rules', the same in the game.
/// </summary>
[Collection(BotArenaCollection.Name)]
public class LadderTests
{
    private const int Second = 120;
    private readonly ITestOutputHelper _out;

    public LadderTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static ClimbParams Climbing => TestData.Config.Movement.Climbing;

    [Fact]
    public void Each_ladder_has_ground_at_its_foot_a_floor_at_its_top_and_room_to_climb()
    {
        Assert.Equal(2, TestData.Data.Levels["oxbarrow_works"].Ladders.Count);
        Assert.Equal(5, TestData.Data.Levels["rail_yard"].Ladders.Count);
        Assert.Empty(TestData.Data.Levels["cold_store"].Ladders);
        Assert.Empty(TestData.Data.Levels["hospital_wing"].Ladders);

        MovementParams m = TestData.Config.Movement;
        float r = m.CapsuleRadius - 0.02f;
        foreach ((string id, LevelLayout level) in TestData.Data.Levels)
        {
            // What bodies stand on and bump into (railings included, which paint passes).
            var walk = new CollisionWorld();
            walk.Add(new PlaneShape(Vector3.UnitY, 0f), default, "ground");
            foreach (LevelPrimitive p in level.Primitives.Where(p => p.Has(PrimitiveFlags.Walk)))
            {
                walk.Add(p.CreateShape(), p.Surface, level.Owners[p.Owner]);
            }

            walk.Build();
            foreach (LadderSpec l in level.Ladders)
            {
                string name = $"{id} {level.Owners[l.Owner]} ladder {l.Height:0.0} m";
                Vector3 foot = l.ClimbPoint(l.Foot.Y, Climbing.Standoff);
                Assert.True(walk.SweepSphere(foot + Vector3.UnitY * 0.5f, foot - Vector3.UnitY, 0.01f, out SweepHit ground), $"{name}: no ground");
                Assert.InRange(ground.Point.Y, l.Foot.Y - 0.4f, l.Foot.Y + 0.05f);

                Vector3 top = l.TopPoint;
                Assert.True(walk.SweepSphere(top + Vector3.UnitY * 0.5f, top - Vector3.UnitY * 0.5f, 0.01f, out SweepHit floor), $"{name}: nothing to step off onto");
                Assert.InRange(floor.Point.Y, l.TopY - 0.05f, l.TopY + 0.05f);

                // The body's capsule up the climbing line, and over the top onto the floor there.
                Vector3 low = foot with { Y = MathF.Max(ground.Point.Y, l.Foot.Y - 0.3f) + m.CapsuleRadius + 0.05f };
                Vector3 high = l.ClimbPoint(l.TopY + m.StandCapsuleHeight - m.CapsuleRadius, Climbing.Standoff);
                Assert.False(walk.SweepSphere(low, high, r, out SweepHit hit), $"{name}: the climb meets {walk.Colliders[Math.Max(hit.ColliderId, 0)].Name} at {hit.Point}");
                foreach (float up in new[] { m.CapsuleRadius + 0.06f, m.StandCapsuleHeight - m.CapsuleRadius })
                {
                    Vector3 from = l.ClimbPoint(l.TopY + up, Climbing.Standoff), to = top + Vector3.UnitY * up;
                    Assert.False(walk.SweepSphere(from, to, r, out hit), $"{name}: stepping off meets {walk.Colliders[Math.Max(hit.ColliderId, 0)].Name} at {hit.Point}");
                }
            }
        }
    }

    [Fact]
    public void You_climb_the_water_tower_and_step_off_onto_its_catwalk()
    {
        var c = new Climber("rail_yard");
        (LadderSpec legs, _, _, _) = c.Tower();
        c.Put(legs.ClimbPoint(0f, Climbing.Standoff + 0.3f), legs.Facing);
        int ticks = c.Run(12 * Second, _ => c.Command(legs.Facing, forward: 1f, InputButtons.Interact),
            () => !c.Hero.OnLadder && c.Hero.Position.Y > 12f);
        _out.WriteLine($"up {c.Hero.Position.Y:0.00} m in {ticks / (float)Second:0.0} s, {-legs.Ahead(c.Hero.Position):0.00} m past the rungs");
        Assert.InRange(c.Hero.Position.Y, legs.TopY - 0.05f, legs.TopY + 0.1f);
        Assert.InRange(-legs.Ahead(c.Hero.Position), legs.Exit - 0.1f, legs.Exit + 0.4f);
        Assert.InRange(ticks / (float)Second, legs.Height / Climbing.Speed, legs.Height / Climbing.Speed + 1.5f);

        // On the catwalk, walking on: the grid holds you on it.
        c.Run(Second, _ => c.Command(legs.Facing + MathF.PI * 0.5f, forward: 1f));
        Assert.InRange(c.Hero.Position.Y, legs.TopY - 0.05f, legs.TopY + 0.05f);
    }

    [Fact]
    public void From_the_catwalk_you_climb_to_the_roof_and_back_down()
    {
        var c = new Climber("rail_yard");
        (_, _, LadderSpec tank, _) = c.Tower();
        c.Put(tank.ClimbPoint(tank.Foot.Y, Climbing.Standoff + 0.1f), tank.Facing);
        c.Run(5 * Second, _ => c.Command(tank.Facing, forward: 1f, InputButtons.Interact), () => !c.Hero.OnLadder && c.Hero.Position.Y > 15f);
        _out.WriteLine($"on the roof at {c.Hero.Position}");
        Assert.InRange(c.Hero.Position.Y, tank.TopY - 0.05f, tank.TopY + 0.1f);

        // Facing out over the top, interact gets you back on, and back takes you down to the catwalk.
        float outward = tank.Facing + MathF.PI;
        c.Run(2, _ => c.Command(outward));
        c.Run(6 * Second, t => c.Command(outward, forward: -1f, t == 0 ? InputButtons.Interact : InputButtons.None),
            () => c.Hero.OnLadder && c.Hero.LadderPhase == LadderPhase.Climbing && c.Hero.Position.Y < tank.TopY - 0.5f);
        Assert.True(c.Hero.OnLadder, "didn't get back on the ladder from the roof");
        Assert.InRange(tank.Ahead(c.Hero.Position), Climbing.Standoff - 0.05f, Climbing.Standoff + 0.05f);
        c.Run(6 * Second, _ => c.Command(outward, forward: -1f), () => !c.Hero.OnLadder);
        Assert.False(c.Hero.OnLadder);
        Assert.InRange(c.Hero.Position.Y, tank.Foot.Y - 0.05f, tank.Foot.Y + 0.05f);
    }

    [Fact]
    public void From_the_catwalk_the_ladder_takes_you_down_to_the_ground()
    {
        var c = new Climber("rail_yard");
        (LadderSpec legs, _, _, _) = c.Tower();
        float outward = legs.Facing + MathF.PI;
        c.Put(legs.TopPoint, outward);
        int ticks = c.Run(12 * Second, t => c.Command(outward, forward: -1f, t == 0 ? InputButtons.Interact : InputButtons.None),
            () => !c.Hero.OnLadder && c.Hero.Position.Y < 1f);
        _out.WriteLine($"down to {c.Hero.Position} in {ticks / (float)Second:0.0} s");
        Assert.InRange(c.Hero.Position.Y, -0.05f, 0.05f);
        Assert.InRange(legs.Ahead(c.Hero.Position), Climbing.Standoff - 0.1f, Climbing.Standoff + 0.1f);
        Assert.InRange(ticks / (float)Second, legs.Height / Climbing.Speed, legs.Height / Climbing.Speed + 1.5f);
    }

    [Fact]
    public void You_get_on_facing_the_ladder_within_reach_or_facing_out_over_its_top()
    {
        var c = new Climber("rail_yard");
        (LadderSpec legs, int index, _, _) = c.Tower();
        LadderSet ladders = c.Sim.Ladders;
        float away = legs.Facing + MathF.PI;
        Vector3 Front(float ahead, float aside = 0f, float y = 0f) => legs.ClimbPoint(y, ahead) + legs.Across * aside;

        Assert.Equal(index, ladders.FindGrab(Front(0.6f), legs.Facing, Climbing, out bool fromTop));
        Assert.False(fromTop);
        Assert.Equal(index, ladders.FindGrab(Front(0.6f), legs.Facing + 0.9f, Climbing, out _)); // turned 52°: still facing it
        Assert.Equal(-1, ladders.FindGrab(Front(0.6f), legs.Facing + 1.2f, Climbing, out _)); // 69°
        Assert.Equal(-1, ladders.FindGrab(Front(0.6f), away, Climbing, out _));
        Assert.Equal(-1, ladders.FindGrab(Front(Climbing.Reach + 0.2f), legs.Facing, Climbing, out _));
        Assert.Equal(-1, ladders.FindGrab(Front(0.6f, legs.Width * 0.5f + 0.4f), legs.Facing, Climbing, out _));
        Assert.Equal(index, ladders.FindGrab(Front(0.6f, y: 6f), legs.Facing, Climbing, out _)); // halfway up, after a fall onto it
        Assert.Equal(-1, ladders.FindGrab(Front(0.6f, y: legs.TopY - 0.5f), legs.Facing, Climbing, out _)); // that close to the top, step off

        Assert.Equal(index, ladders.FindGrab(legs.TopPoint, away, Climbing, out fromTop));
        Assert.True(fromTop);
        Assert.Equal(-1, ladders.FindGrab(legs.TopPoint, legs.Facing, Climbing, out _)); // facing in, not out over it
        Assert.Equal(-1, ladders.FindGrab(legs.TopPoint with { Y = 0f }, away, Climbing, out _)); // under the catwalk, behind it
    }

    [Fact]
    public void On_a_ladder_you_can_neither_fire_nor_refill_and_your_body_faces_it()
    {
        var c = new Climber("rail_yard");
        (LadderSpec legs, _, _, _) = c.Tower();
        c.Put(legs.ClimbPoint(0f, Climbing.Standoff + 0.2f), legs.Facing);
        c.Hero.Marker.Paint.FillWith(2);
        c.Run(Second, _ => c.Command(legs.Facing, forward: 1f, InputButtons.Interact));
        Assert.True(c.Hero.OnLadder);

        // Looking round (and pulling the trigger, and trying to refill) on the way up.
        float look = legs.Facing + 1.0f;
        c.Run(2 * Second, t => c.Command(look, forward: 1f, (t / 30) % 2 == 0 ? InputButtons.Fire : InputButtons.Refill));
        Assert.True(c.Hero.OnLadder);
        Assert.DoesNotContain(c.Events, e => e.Type is SimEventType.ShotFired or SimEventType.RefillStarted);
        Assert.Equal(legs.Facing, c.Hero.Yaw, 4);
        Assert.Equal(1.0f, c.Hero.HeadYaw, 3);

        // Off at the top, the marker works again.
        c.Run(10 * Second, _ => c.Command(legs.Facing, forward: 1f), () => !c.Hero.OnLadder);
        c.Run(Second, t => c.Command(legs.Facing + MathF.PI * 0.5f, buttons: t % 12 == 0 ? InputButtons.Fire : InputButtons.None));
        Assert.Contains(c.Events, e => e.Type == SimEventType.ShotFired);
    }

    [Fact]
    public void Jump_lets_go_and_pushes_you_off_the_ladder()
    {
        var c = new Climber("rail_yard");
        (LadderSpec legs, _, _, _) = c.Tower();
        c.Put(legs.ClimbPoint(0f, Climbing.Standoff + 0.2f), legs.Facing);
        c.Run(2 * Second, _ => c.Command(legs.Facing, forward: 1f, InputButtons.Interact));
        Assert.True(c.Hero.OnLadder);

        // The rules' own result: falling is the host's (headless, nothing falls).
        MovementResult r = MovementModel.Step(c.Hero, c.Command(legs.Facing, buttons: InputButtons.Jump), TestData.Config.Movement, c.Sim.Dt,
            grounded: false, c.Sim.Collision, c.Sim.Ladders);
        Assert.False(c.Hero.OnLadder);
        Assert.False(r.Climbing);
        Assert.True(Vector3.Dot(r.HorizontalVelocity, legs.Forward) < -1f, $"let go at {r.HorizontalVelocity}");
    }

    [Fact]
    public void Out_on_a_ladder_you_climb_down_to_walk_off()
    {
        var c = new Climber("rail_yard");
        (LadderSpec legs, _, _, _) = c.Tower();
        c.Put(legs.ClimbPoint(0f, Climbing.Standoff + 0.2f), legs.Facing);
        c.Run(3 * Second, _ => c.Command(legs.Facing, forward: 1f, InputButtons.Interact));
        float high = c.Hero.Position.Y;
        Assert.True(high > 4f, $"only {high:0.0} m up");
        c.Hero.Alive = false;
        c.Run(6 * Second, _ => c.Command(legs.Facing), () => !c.Hero.OnLadder);
        Assert.False(c.Hero.OnLadder);
        Assert.InRange(c.Hero.Position.Y, -0.05f, 0.05f);
    }

    [Fact]
    public void A_foot_on_a_rung_is_heard_every_other_rung()
    {
        var c = new Climber("rail_yard");
        (LadderSpec legs, _, _, _) = c.Tower();
        c.Put(legs.ClimbPoint(0f, Climbing.Standoff + 0.2f), legs.Facing);
        c.Run(5 * Second, _ => c.Command(legs.Facing, forward: 1f, InputButtons.Interact));
        float climbed = c.Hero.Position.Y;
        SimEvent[] steps = c.Events.Where(e => e.Type == SimEventType.Footstep).ToArray();
        _out.WriteLine($"{steps.Length} steps over {climbed:0.0} m");
        Assert.InRange(steps.Length, (int)(climbed / TestData.Config.Movement.Footsteps.ClimbStride) - 1, (int)(climbed / TestData.Config.Movement.Footsteps.ClimbStride) + 1);
        Assert.All(steps, e => Assert.Equal(legs.Surface, e.Surface));
    }

    [Fact]
    public void Climbing_steps_without_allocating()
    {
        var c = new Climber("rail_yard", keepEvents: false);
        (LadderSpec legs, _, _, _) = c.Tower();
        c.Put(legs.ClimbPoint(0f, Climbing.Standoff + 0.2f), legs.Facing);
        c.Run(Second, _ => c.Command(legs.Facing, forward: 1f, InputButtons.Interact));
        Assert.True(c.Hero.OnLadder);
        Func<int, InputCommand> up = _ => c.Command(legs.Facing, forward: 1f);
        Assert.Equal(0, Allocations.During(() => c.Run(2 * Second, up)));
        Assert.True(c.Hero.OnLadder);
    }

    [Fact]
    public void A_ladder_too_short_to_climb_fails_to_load_with_its_key()
    {
        var source = new EditedDataSource(TestData.Source)
            .Edit("kit/props.jsonc", text => text.Replace("\"height_m\": 12.2, \"facing_deg\": -90", "\"height_m\": 0.1, \"facing_deg\": -90"));
        DataException ex = Assert.Throws<DataException>(() => GameData.Load(source));
        Assert.Contains("ladders[0].height_m", ex.Message);
    }

    /// <summary>You on your own in a level, moving over its grid as a headless bot does, with the tick's events kept.</summary>
    private sealed class Climber
    {
        private readonly NavGridMover _mover;
        private readonly InputCommand[] _commands = new InputCommand[1];
        private readonly bool _keepEvents;

        public Climber(string levelId, bool keepEvents = true)
        {
            (LevelLayout level, NavGrid grid, _) = BotArena.SharedFor(levelId);
            Sim = new SimWorld(TestData.Config);
            Sim.LoadLevel(level);
            Hero = Sim.AddPlayer(0, 0, level.PlayerSpawn, level.PlayerSpawnYaw);
            _mover = new NavGridMover(Sim, grid);
            _keepEvents = keepEvents;
        }

        public SimWorld Sim { get; }

        public PlayerState Hero { get; }

        public List<SimEvent> Events { get; } = new(4096);

        /// <summary>The water tower's two ladders: up its legs, and up its tank.</summary>
        public (LadderSpec Legs, int LegsIndex, LadderSpec Tank, int TankIndex) Tower()
        {
            int legs = -1, tank = -1;
            for (int i = 0; i < Sim.Ladders.Count; i++)
            {
                if (Sim.Level!.Owners[Sim.Ladders[i].Owner].Contains("water_tower", StringComparison.Ordinal))
                {
                    if (Sim.Ladders[i].Foot.Y < 1f)
                    {
                        legs = i;
                    }
                    else
                    {
                        tank = i;
                    }
                }
            }

            Assert.True(legs >= 0 && tank >= 0, "the water tower's ladders");
            return (Sim.Ladders[legs], legs, Sim.Ladders[tank], tank);
        }

        public void Put(Vector3 feet, float yaw)
        {
            Hero.Position = feet;
            Hero.LastPosition = feet;
            Hero.Yaw = yaw;
        }

        public InputCommand Command(float yaw, float forward = 0f, InputButtons buttons = InputButtons.None) =>
            new() { Tick = Sim.Tick, Yaw = yaw, Move = new Vector2(0f, forward), Buttons = buttons };

        /// <summary>Runs <paramref name="script"/> (given the tick since the start) until <paramref name="until"/> holds; returns the ticks run.</summary>
        public int Run(int ticks, Func<int, InputCommand> script, Func<bool>? until = null)
        {
            for (int t = 0; t < ticks; t++)
            {
                InputCommand cmd = script(t);
                cmd.Tick = Sim.Tick;
                _mover.Step(Hero, cmd, Sim.Dt);
                _commands[0] = cmd;
                Sim.Step(_commands);
                if (_keepEvents)
                {
                    foreach (SimEvent e in Sim.Events.Items)
                    {
                        Events.Add(e);
                    }
                }

                Sim.Events.Clear();
                if (until?.Invoke() == true)
                {
                    return t + 1;
                }
            }

            return ticks;
        }
    }
}
