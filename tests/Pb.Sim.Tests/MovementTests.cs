using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Events;
using Pb.Sim.Players;

namespace Pb.Sim.Tests;

/// <summary>Phase 2 movement: lean, shoulder swap, the muzzle-in-cover rule, slide, jump, headroom and footsteps.</summary>
public class MovementTests
{
    private static MovementParams P => TestData.Config.Movement;

    private static float Dt => TestData.Dt;

    private static PlayerState NewState() => new(1, 0, null!) { EyeHeight = P.StandEyeHeight };

    /// <summary>One tick as the host runs it: the movement rules, then the body moves at the returned velocity.</summary>
    private static MovementResult Tick(PlayerState state, InputCommand cmd, CollisionWorld? world = null, bool grounded = true)
    {
        MovementResult r = MovementModel.Step(state, cmd, P, Dt, grounded, world);
        state.Velocity = r.HorizontalVelocity;
        return r;
    }

    private static CollisionWorld World(params Shape[] shapes)
    {
        var world = new CollisionWorld();
        foreach (Shape s in shapes)
        {
            world.Add(s, TestData.Config.Surfaces.Get("concrete"), "test");
        }

        world.Build();
        return world;
    }

    private static int TicksFor(float seconds) => (int)MathF.Ceiling(seconds / Dt - 1e-3f);

    [Fact]
    public void Lean_moves_the_eye_out_fast_and_back_faster()
    {
        PlayerState state = NewState();
        var lean = new InputCommand { Buttons = InputButtons.LeanRight };

        for (int i = 0; i < TicksFor(P.LeanInTime); i++)
        {
            Tick(state, lean);
        }

        Assert.Equal(1f, state.Lean, 4);
        // Yaw 0 faces −Z, so right is +X: the eye moves sin(20°) × 0.8 m sideways and dips a little.
        Assert.Equal(MathF.Sin(P.LeanAngle) * P.LeanPivotBelowEye, state.LeanOffset.X, 4);
        Assert.True(state.LeanOffset.Y < 0f);
        Assert.Equal(state.Position.Y + P.StandEyeHeight + state.LeanOffset.Y, state.EyePosition.Y, 4);

        for (int i = 0; i < TicksFor(P.LeanReturnTime); i++)
        {
            Tick(state, default);
        }

        Assert.Equal(0f, state.Lean);
        Assert.Equal(Vector3.Zero, state.LeanOffset);

        // Switching sides goes back through upright first.
        for (int i = 0; i < TicksFor(P.LeanInTime); i++)
        {
            Tick(state, new InputCommand { Buttons = InputButtons.LeanLeft });
        }

        Assert.Equal(-1f, state.Lean, 4);
        Tick(state, lean);
        Assert.True(state.Lean < -0.5f, "the first tick of a side switch is still leaning left");
    }

    [Fact]
    public void Lean_stops_before_the_head_meets_a_wall()
    {
        // A wall whose face is 0.3 m to the right of the eye.
        CollisionWorld world = World(new BoxShape(new Vector3(0.8f, 1.5f, 0f), Quaternion.Identity, new Vector3(0.5f, 1.5f, 2f)));
        PlayerState state = NewState();
        for (int i = 0; i < 60; i++)
        {
            Tick(state, new InputCommand { Buttons = InputButtons.LeanRight }, world);
        }

        Assert.InRange(state.Lean, 0.4f, 0.75f);
        Assert.True(state.EyePosition.X + P.HeadRadius <= 0.3f, $"head at {state.EyePosition.X + P.HeadRadius:0.000} m");

        // Leaning away from it is unhindered.
        for (int i = 0; i < 60; i++)
        {
            Tick(state, new InputCommand { Buttons = InputButtons.LeanLeft }, world);
        }

        Assert.Equal(-1f, state.Lean, 4);
    }

    [Fact]
    public void Shoulder_swap_mirrors_the_muzzle_and_holds_fire_while_it_moves()
    {
        SimWorld sim = Ground("turf");
        PlayerState player = sim.AddPlayer(1, 0, Vector3.Zero, 0f);
        Assert.Equal(TestData.Config.Shot.MuzzleOffset.X, sim.SolveShot(player).Origin.X, 4);

        var commands = new InputCommand[1];
        int shotsDuringSwap = 0;
        for (int tick = 0; tick < TicksFor(P.ShoulderSwapTime) - 1; tick++)
        {
            // Swap on the first tick, then pull the trigger repeatedly while the marker crosses over.
            commands[0] = new InputCommand
            {
                Tick = tick,
                Buttons = InputButtons.SwapShoulder | (tick % 4 < 2 ? InputButtons.Fire : InputButtons.None),
            };
            MovementModel.Step(player, commands[0], P, Dt, true);
            Assert.False(player.MarkerReady);
            sim.Step(commands);
            shotsDuringSwap += sim.Events.CountOf(SimEventType.ShotFired);
            sim.Events.Clear();
        }

        Assert.Equal(0, shotsDuringSwap);
        commands[0] = new InputCommand { Tick = 100 };
        MovementModel.Step(player, commands[0], P, Dt, true);
        Assert.Equal(-1f, player.Shoulder);
        Assert.True(player.MarkerReady);
        Assert.Equal(-TestData.Config.Shot.MuzzleOffset.X, sim.SolveShot(player).Origin.X, 4);
    }

    [Fact]
    public void A_barrel_behind_a_wall_edge_breaks_the_ball_on_that_wall()
    {
        // A wall in front, starting 5 cm right of the eye: you can see past its left edge, but the
        // marker on your right shoulder is behind it.
        var wall = new BoxShape(new Vector3(1.05f, 1.5f, -0.3f), Quaternion.Identity, new Vector3(1f, 1.5f, 0.05f));

        SimWorld sim = Ground("turf");
        int wallId = sim.Collision.Add(wall, TestData.Config.Surfaces.Get("concrete"), "wall").Id;
        PlayerState player = sim.AddPlayer(1, 0, Vector3.Zero, 0f);
        Assert.True(sim.SolveShot(player).MuzzleBlocked);

        Assert.True(TestData.Config.Shot.MuzzleBlockedBreaks, "the rule is on in the shipped data");
        List<SimEvent> events = FireOnce(sim);
        SimEvent broke = Assert.Single(events, e => e.Type == SimEventType.BallBroke);
        Assert.Equal(wallId, broke.ColliderId);
        Assert.Equal(-0.25f, broke.Position.Z, 3);
        ShotParams shot = TestData.Config.Shot;
        Assert.InRange(broke.Value, shot.MuzzleVelocity - shot.VelocityVariance, shot.MuzzleVelocity + shot.VelocityVariance); // point blank
        Assert.Single(events, e => e.Type == SimEventType.ShotFired);
        Assert.Equal(0, sim.Ballistics.Pool.Count);

        // Swap to the left shoulder: the muzzle clears the edge and the ball flies.
        player.ShoulderTarget = player.Shoulder = -1f;
        Assert.False(sim.SolveShot(player).MuzzleBlocked);
        events = FireOnce(sim);
        Assert.Single(events, e => e.Type == SimEventType.ShotFired);
        Assert.DoesNotContain(events, e => e.Type == SimEventType.BallBroke);
        Assert.Equal(1, sim.Ballistics.Pool.Count);
    }

    [Fact]
    public void Slide_from_a_sprint_boosts_then_ends_crouched_and_cools_down()
    {
        PlayerState state = NewState();
        state.Velocity = new Vector3(0f, 0f, -P.SprintSpeed);
        var sprint = new InputCommand { Move = new Vector2(0, 1), Buttons = InputButtons.Sprint };
        Tick(state, sprint);
        Assert.True(state.Sprinting);

        // Crouch while sprinting starts the slide.
        var dive = new InputCommand { Move = new Vector2(0, 1), Buttons = InputButtons.Sprint | InputButtons.Crouch };
        MovementResult r = Tick(state, dive);
        Assert.Equal(Stance.Sliding, state.Stance);
        Assert.Equal(P.SlideCapsuleHeight, r.CapsuleHeight);
        Assert.True(r.HorizontalVelocity.Length() > P.SprintSpeed, "the slide adds a burst of speed");

        int ticks = 1;
        while (state.Stance == Stance.Sliding && ticks < 400)
        {
            // The marker comes up from the sprint partway into the slide, so you can shoot while sliding.
            Assert.Equal(ticks >= TicksFor(P.SprintRecoveryTime), state.MarkerReady);
            r = Tick(state, dive);
            ticks++;
        }

        Assert.Equal(Stance.Crouching, state.Stance);
        Assert.True(ticks * Dt <= P.SlideMaxTime + 2 * Dt, $"slide lasted {ticks * Dt:0.00} s");
        Assert.True(state.SlideCooldown > 0f);

        // Straight back up to speed: the cooldown stops another slide for a moment.
        state.Velocity = new Vector3(0f, 0f, -P.SprintSpeed);
        Tick(state, new InputCommand { Move = new Vector2(0, 1), Buttons = InputButtons.Slide });
        Assert.NotEqual(Stance.Sliding, state.Stance);
    }

    [Fact]
    public void Jump_needs_a_fresh_press_the_ground_and_a_standing_player()
    {
        PlayerState state = NewState();
        var jump = new InputCommand { Buttons = InputButtons.Jump };
        Assert.Equal(P.JumpSpeed, Tick(state, jump).JumpVelocity);
        Assert.Equal(0f, Tick(state, jump).JumpVelocity); // held, not pressed again

        Tick(state, default);
        Assert.Equal(0f, Tick(state, jump).JumpVelocity); // cooling down
        for (int i = 0; i < TicksFor(P.JumpCooldown); i++)
        {
            Tick(state, default);
        }

        Assert.Equal(0f, Tick(state, jump, grounded: false).JumpVelocity);
        Tick(state, default);
        Assert.Equal(P.JumpSpeed, Tick(state, jump).JumpVelocity);

        PlayerState crouched = NewState();
        Assert.Equal(0f, Tick(crouched, new InputCommand { Buttons = InputButtons.Crouch | InputButtons.Jump }).JumpVelocity);
    }

    [Fact]
    public void Standing_up_waits_for_headroom()
    {
        // A low ceiling at 1.5 m: room to crouch under, not to stand.
        CollisionWorld ceiling = World(new BoxShape(new Vector3(0f, 1.6f, 0f), Quaternion.Identity, new Vector3(2f, 0.1f, 2f)));
        PlayerState state = NewState();
        for (int i = 0; i < 30; i++)
        {
            Tick(state, new InputCommand { Buttons = InputButtons.Crouch }, ceiling);
        }

        for (int i = 0; i < 30; i++)
        {
            MovementResult r = Tick(state, default, ceiling);
            Assert.Equal(Stance.Crouching, r.Stance);
            Assert.Equal(P.CrouchCapsuleHeight, r.CapsuleHeight);
        }

        // Out from under it, you stand.
        state.Position = new Vector3(5f, 0f, 0f);
        Assert.Equal(Stance.Standing, Tick(state, default, ceiling).Stance);
    }

    [Fact]
    public void Footsteps_carry_by_pace_and_surface_and_landings_thump()
    {
        SimWorld sim = Ground("metal");
        PlayerState player = sim.AddPlayer(1, 0, Vector3.Zero, 0f);
        float metal = P.Footsteps.Loudness(TestData.Config.Surfaces.Get("metal"));
        Assert.True(metal > 1f);

        List<SimEvent> Walk(float speed, Stance stance, float seconds)
        {
            var steps = new List<SimEvent>();
            var commands = new InputCommand[1];
            var velocity = new Vector3(0f, 0f, -speed);
            for (int i = 0; i < TicksFor(seconds); i++)
            {
                player.Position += velocity * Dt;
                player.Velocity = velocity;
                player.Stance = stance;
                sim.Step(commands);
                steps.AddRange(sim.Events.Items.ToArray().Where(e => e.Type == SimEventType.Footstep));
                sim.Events.Clear();
            }

            return steps;
        }

        // Running 11 m: one step per 1.2 m stride, each heard 14 m × the metal's loudness away.
        List<SimEvent> run = Walk(P.RunSpeed, Stance.Standing, 11f / P.RunSpeed);
        Assert.Equal((int)(11f / P.Footsteps.Stride), run.Count);
        Assert.All(run, e =>
        {
            Assert.Equal((int)FootstepKind.Step, e.Extra);
            Assert.Equal(TestData.Config.Surfaces.Get("metal"), e.Surface);
            Assert.Equal(P.Footsteps.RunRadius * metal, e.Value, 3);
        });

        // Crouch-walking is much quieter.
        List<SimEvent> sneak = Walk(P.CrouchSpeed, Stance.Crouching, 3f);
        Assert.NotEmpty(sneak);
        Assert.All(sneak, e => Assert.Equal(P.Footsteps.CrouchRadius * metal, e.Value, 3));

        // A fall that ends at 4 m/s makes a landing thump.
        var commands = new InputCommand[1];
        player.Grounded = false;
        player.Velocity = new Vector3(0f, -4f, 0f);
        sim.Step(commands);
        sim.Events.Clear();
        player.Grounded = true;
        player.Velocity = Vector3.Zero;
        sim.Step(commands);
        SimEvent land = Assert.Single(sim.Events.Items.ToArray(), e => e.Type == SimEventType.Footstep);
        Assert.Equal((int)FootstepKind.Land, land.Extra);
        Assert.Equal(P.Footsteps.LandRadius * metal, land.Value, 3);
    }

    private static SimWorld Ground(string surface)
    {
        var sim = new SimWorld(TestData.Config);
        sim.Collision.Add(new PlaneShape(Vector3.UnitY, 0f), TestData.Config.Surfaces.Get(surface), "ground");
        return sim;
    }

    /// <summary>
    /// Waits a quarter of a second with the trigger released (well past the rate cap), then pulls it
    /// for one tick and returns that tick's events.
    /// </summary>
    private static List<SimEvent> FireOnce(SimWorld sim)
    {
        var commands = new InputCommand[1];
        for (int i = 0; i < TicksFor(0.25f); i++)
        {
            sim.Step(commands);
            sim.Events.Clear();
        }

        commands[0] = new InputCommand { Buttons = InputButtons.Fire };
        sim.Step(commands);
        List<SimEvent> events = sim.Events.Items.ToArray().ToList();
        sim.Events.Clear();
        return events;
    }
}
