using System.Numerics;
using Pb.Sim.Ballistics;
using Pb.Sim.Core;
using Pb.Sim.Events;
using Pb.Sim.Players;
using Pb.Sim.Range;

namespace Pb.Sim.Tests;

public class SimWorldTests
{
    [Fact]
    public void Same_seed_and_inputs_give_bit_identical_results()
    {
        (List<string> eventsA, Vector3[] ballsA) = RunScenario(seed: 1234);
        (List<string> eventsB, Vector3[] ballsB) = RunScenario(seed: 1234);
        Assert.Equal(eventsA, eventsB);
        Assert.Equal(ballsA, ballsB);

        (List<string> eventsC, _) = RunScenario(seed: 999);
        Assert.NotEqual(eventsA, eventsC);
    }

    [Fact]
    public void Steady_state_stepping_does_not_allocate()
    {
        SimWorld sim = TestData.RangeWorld();
        sim.Stress!.Enabled = true;
        PlayerState player = sim.AddPlayer(1, 0, sim.Range!.SpawnPosition, 0f);
        var commands = new InputCommand[1];

        void StepTicks(int from, int count)
        {
            for (int tick = from; tick < from + count; tick++)
            {
                commands[0] = ScriptedCommand(tick);
                if (player.Marker.Paint.Loader == 0)
                {
                    player.Marker.ResetGear();
                }

                sim.Step(commands);
                sim.Events.Clear();
            }
        }

        StepTicks(0, 600); // warm up: pool fills to the stress target, JIT settles
        Assert.True(sim.Ballistics.Pool.Count >= 900, $"stress mode should sustain ~1000 balls (got {sim.Ballistics.Pool.Count})");

        long before = GC.GetAllocatedBytesForCurrentThread();
        StepTicks(600, 600);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Aimed_shot_at_10_m_hits_the_dummy()
    {
        SimWorld sim = TestData.RangeWorld();
        PlayerState player = sim.AddPlayer(1, 0, sim.Range!.SpawnPosition, 0f);
        TargetSpec target = sim.Range.Targets.First(t => t.Id == "d10");
        Vector3 chest = target.BasePosition + new Vector3(0, 1.0f, 0);
        (float yaw, float pitch) = ViewAngles.FromDirection(chest - player.EyePosition);

        var commands = new[] { new InputCommand { Yaw = yaw, Pitch = pitch, Buttons = InputButtons.Fire } };
        int hits = 0;
        for (int tick = 0; tick < 120; tick++)
        {
            commands[0].Tick = tick;
            commands[0].Buttons = tick == 0 ? InputButtons.Fire : InputButtons.None;
            sim.Step(commands);
            hits += sim.Events.CountOf(SimEventType.TargetHit);
            sim.Events.Clear();
        }

        Assert.Equal(1, hits);
        Assert.Equal(1, sim.Targets.HitCount(0));
    }

    [Fact]
    public void Barrel_through_a_wall_starts_the_ball_at_the_wall()
    {
        SimWorld sim = TestData.RangeWorld();
        PropSpec panel = sim.Range!.Props.First(p => p.Id == "panel_ccd");
        // Stand 0.3 m in front of the 1 cm panel, facing it: the 0.55 m barrel would poke through.
        Vector3 feet = panel.BasePosition + new Vector3(0, 0, 0.3f);
        PlayerState player = sim.AddPlayer(1, 0, feet, 0f);
        player.EyeHeight = 1.0f;

        ShotSolution solution = sim.SolveShot(player);
        Assert.True(solution.MuzzleBlocked);
        Assert.True(solution.Origin.Z > panel.BasePosition.Z, "ball must start on the shooter's side of the panel");

        var commands = new[] { new InputCommand { Buttons = InputButtons.Fire } };
        sim.Step(commands);
        sim.Events.Clear();
        commands[0].Buttons = InputButtons.None;
        sim.Step(commands);
        bool passed = false;
        for (int i = 0; i < sim.Ballistics.Pool.Count; i++)
        {
            passed |= sim.Ballistics.Pool.Position[i].Z < panel.BasePosition.Z - 0.05f;
        }

        Assert.False(passed);
    }

    [Fact]
    public void Trajectory_prediction_matches_the_real_ball()
    {
        SimWorld sim = TestData.RangeWorld();
        var origin = new Vector3(0, 1.5f, 0);
        var velocity = Vector3.Normalize(new Vector3(0.02f, 0.05f, -1f)) * 88f;
        Span<Vector3> points = stackalloc Vector3[1024];
        TrajectoryResult predicted = TrajectoryPredictor.Predict(sim.Config.Projectile, sim.Collision, sim.Targets, 0, origin,
            velocity, sim.Dt, 5f, points);
        Assert.True(predicted.Impact);

        sim.Ballistics.Spawn(origin, velocity, 7, 1, 0, new Pcg32(1), sim.Dt, 0, sim.Events);
        sim.Events.Clear();
        Vector3? actual = null;
        for (int tick = 0; tick < 1000 && actual is null; tick++)
        {
            sim.Step(ReadOnlySpan<InputCommand>.Empty);
            foreach (SimEvent e in sim.Events.Items)
            {
                if (e.Type is SimEventType.BallBroke or SimEventType.BallBounced && e.PlayerId == 7)
                {
                    actual = e.Position;
                }
            }

            sim.Events.Clear();
        }

        Assert.NotNull(actual);
        Assert.True(Vector3.Distance(predicted.ImpactPoint, actual!.Value) < 1e-3f,
            $"predicted {predicted.ImpactPoint}, actual {actual}");
    }

    [Fact]
    public void Moving_target_follows_a_constant_speed_triangle_wave()
    {
        var motion = new TargetMotion { Axis = Vector3.UnitX, Amplitude = 3f, Speed = 2.5f };
        Assert.Equal(-3f, motion.OffsetAt(0).X, 4);
        Assert.Equal(3f, motion.OffsetAt(6.0 / 2.5).X, 4);
        Assert.Equal(-3f, motion.OffsetAt(12.0 / 2.5).X, 4);
        Assert.Equal(0f, motion.OffsetAt(3.0 / 2.5).X, 4);
        float speed = (motion.OffsetAt(1.1).X - motion.OffsetAt(1.0).X) / 0.1f;
        Assert.Equal(2.5f, MathF.Abs(speed), 3);
    }

    [Fact]
    public void Hits_on_a_moving_target_use_its_current_position()
    {
        SimWorld sim = TestData.RangeWorld();
        int runner = Enumerable.Range(0, sim.Targets.Count).First(i => sim.Targets[i].Id == "runner25");
        TargetSpec spec = sim.Targets[runner];
        double time = 1.0;
        sim.Targets.Update(time);
        Vector3 at = spec.PositionAt(time) + new Vector3(0, 1f, 0);
        Vector3 elsewhere = spec.PositionAt(time + 1.2) + new Vector3(0, 1f, 0);

        Assert.True(sim.Targets.SweepSphere(at + new Vector3(0, 0, 2), at - new Vector3(0, 0, 2), 0.00865f, 0, 0, out var hit));
        Assert.Equal(runner, hit.ReceiverId);
        Assert.False(sim.Targets.SweepSphere(elsewhere + new Vector3(0, 0, 2), elsewhere - new Vector3(0, 0, 2), 0.00865f, 0, 0, out _));
    }

    [Fact]
    public void Movement_rules_follow_the_data()
    {
        MovementParams p = TestData.Config.Movement;
        var state = new PlayerState(1, 0, null!) { EyeHeight = p.StandEyeHeight };

        MovementResult Settle(InputCommand cmd)
        {
            MovementResult r = default;
            for (int i = 0; i < 240; i++)
            {
                r = MovementModel.Step(state, cmd, p, TestData.Dt, grounded: true);
                state.Velocity = r.HorizontalVelocity;
                state.EyeHeight = r.EyeHeight;
            }

            return r;
        }

        var forward = new InputCommand { Move = new Vector2(0, 1) };
        Assert.Equal(p.RunSpeed, Settle(forward).HorizontalVelocity.Length(), 3);

        forward.Buttons = InputButtons.Sprint;
        MovementResult sprint = Settle(forward);
        Assert.True(sprint.Sprinting);
        Assert.Equal(p.SprintSpeed, sprint.HorizontalVelocity.Length(), 3);

        var sideways = new InputCommand { Move = new Vector2(1, 0), Buttons = InputButtons.Sprint };
        MovementResult noSprint = Settle(sideways);
        Assert.False(noSprint.Sprinting);
        Assert.Equal(p.RunSpeed, noSprint.HorizontalVelocity.Length(), 3);

        var crouch = new InputCommand { Move = new Vector2(0, 1), Buttons = InputButtons.Crouch | InputButtons.Sprint };
        MovementResult crouched = Settle(crouch);
        Assert.False(crouched.Sprinting);
        Assert.Equal(p.CrouchSpeed, crouched.HorizontalVelocity.Length(), 3);
        Assert.Equal(p.CrouchEyeHeight, crouched.EyeHeight, 3);

        var walk = new InputCommand { Move = new Vector2(0, 1), Buttons = InputButtons.Walk };
        Assert.Equal(p.WalkSpeed, Settle(walk).HorizontalVelocity.Length(), 3);
    }

    internal static InputCommand ScriptedCommand(int tick) => new()
    {
        Tick = tick,
        Yaw = 0.2f * MathF.Sin(tick * 0.01f),
        Pitch = 0.03f + 0.02f * MathF.Cos(tick * 0.013f),
        Buttons = (tick % 15 < 7 ? InputButtons.Fire : InputButtons.None) |
                  (tick == 300 ? InputButtons.ToggleFireMode : InputButtons.None),
    };

    private static (List<string> Events, Vector3[] Balls) RunScenario(ulong seed)
    {
        SimConfig baseConfig = TestData.Config;
        var config = new SimConfig
        {
            TickRate = baseConfig.TickRate, MatchSeed = seed, BallPoolCapacity = baseConfig.BallPoolCapacity,
            Surfaces = baseConfig.Surfaces, Projectile = baseConfig.Projectile, BreakModel = baseConfig.BreakModel,
            Shot = baseConfig.Shot, Fire = baseConfig.Fire, Loader = baseConfig.Loader, Air = baseConfig.Air,
            Movement = baseConfig.Movement, Hitboxes = baseConfig.Hitboxes, Rules = baseConfig.Rules,
        };
        var sim = new SimWorld(config);
        sim.LoadRange(TestData.Data.Range, TestData.Data.Stress);
        sim.Stress!.Enabled = true;
        sim.Stress.TargetLiveBalls = 300;
        sim.AddPlayer(1, 0, sim.Range!.SpawnPosition, 0f);
        var commands = new InputCommand[1];
        var log = new List<string>();
        for (int tick = 0; tick < 900; tick++)
        {
            commands[0] = ScriptedCommand(tick);
            sim.Step(commands);
            foreach (SimEvent e in sim.Events.Items)
            {
                log.Add($"{e.Tick}:{e.Type}:{e.PlayerId}:{e.ShotSequence}:{e.Position.X:R},{e.Position.Y:R},{e.Position.Z:R}:{e.Velocity.X:R},{e.Velocity.Z:R}");
            }

            sim.Events.Clear();
        }

        return (log, sim.Ballistics.Pool.Position[..sim.Ballistics.Pool.Count]);
    }
}
