using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;
using Pb.Sim.Vehicles;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>Vehicles (M5.4): getting in and out, driving, what stops them, and the field's rules for them.</summary>
public class VehicleTests
{
    private const int Second = 120;
    private readonly ITestOutputHelper _out;

    public VehicleTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static VehicleParams Buggy => TestData.Config.Vehicles["buggy"];

    /// <summary>Open ground with a wall 30 m north, a buggy at the origin facing north (−Z), you beside its driver's door and two others.</summary>
    private static (SimWorld Sim, PlayerState You, PlayerState Mate, PlayerState Them) Field(bool live = true)
    {
        var sim = new SimWorld(TestData.Config, 3);
        sim.Collision.Add(new PlaneShape(Vector3.UnitY, 0f), TestData.Config.Surfaces.Get("turf"), "ground");
        sim.Collision.Add(new BoxShape(new Vector3(0f, 2f, -30.5f), Quaternion.Identity, new Vector3(40f, 2f, 0.5f)),
            TestData.Config.Surfaces.Get("concrete"), "north wall");
        sim.Vehicles.Load(new[] { new VehicleSpot("buggy", Vector3.Zero, 0f) }, TestData.Config.Vehicles, sim.Collision,
            new Aabb(new Vector3(-100f, -2f, -100f), new Vector3(100f, 30f, 100f)), TestData.Config.Surfaces.Get("metal"));
        VehicleState v = sim.Vehicles[0];
        PlayerState you = sim.AddPlayer(0, 0, VehicleSet.Door(v, v.Params.DriverSeat), 0f);
        PlayerState mate = sim.AddPlayer(1, 0, VehicleSet.Door(v, 1 - v.Params.DriverSeat), 0f);
        PlayerState them = sim.AddPlayer(2, 1, new Vector3(20f, 0f, 20f), 0f);
        sim.StartMatch(new MatchSetup { HeroId = 0, Mode = MatchModeKind.Teams, TimeLimit = 900f, StartPods = 2, BotPods = 2, Pickups = false });
        if (live)
        {
            sim.GoLive();
        }

        return (sim, you, mate, them);
    }

    private static List<SimEvent> Step(SimWorld sim, int ticks, Func<PlayerState, InputCommand>? command = null)
    {
        var events = new List<SimEvent>();
        var commands = new InputCommand[sim.Players.Count];
        for (int t = 0; t < ticks; t++)
        {
            for (int i = 0; i < commands.Length; i++)
            {
                commands[i] = command?.Invoke(sim.Players[i]) ?? new InputCommand { Yaw = sim.Players[i].Yaw };
            }

            sim.Step(commands);
            events.AddRange(sim.Events.Items.ToArray());
            sim.Events.Clear();
        }

        return events;
    }

    /// <summary>One press of interact by <paramref name="who"/> (down a tick, then up).</summary>
    private static List<SimEvent> Press(SimWorld sim, PlayerState who)
    {
        List<SimEvent> events = Step(sim, 1, p => new InputCommand { Yaw = p.Yaw, Buttons = p == who ? InputButtons.Interact : InputButtons.None });
        events.AddRange(Step(sim, 1));
        return events;
    }

    [Fact]
    public void You_get_in_at_its_door_drive_it_and_get_out_beside_it()
    {
        (SimWorld sim, PlayerState you, _, _) = Field();
        VehicleState v = sim.Vehicles[0];
        List<SimEvent> inside = Press(sim, you);
        Assert.Contains(inside, e => e.Type == SimEventType.VehicleEntered && e.PlayerId == you.Id && e.Extra == v.Params.DriverSeat);
        Assert.Equal((v.Id, v.Params.DriverSeat), (you.Vehicle, you.Seat));
        Assert.Equal(you.Id, v.Driver);
        Assert.Equal(v.Seat(v.Params.DriverSeat), you.Position);
        Assert.Equal(Buggy.SeatedEyeHeight, you.EyeHeight);

        // Full throttle for three seconds: it speeds up to the field's limit, never past it, and you go with it.
        float fastest = 0f;
        Step(sim, 3 * Second, p => new InputCommand { Yaw = p.Yaw, Move = p == you ? new Vector2(0f, 1f) : Vector2.Zero });
        fastest = MathF.Max(fastest, v.Speed);
        _out.WriteLine($"after 3 s: {v.Speed:0.0} m/s, at {v.Position}");
        Assert.InRange(v.Speed, Buggy.MaxSpeed * 0.95f, Buggy.MaxSpeed);
        Assert.True(v.Position.Z < -10f, "it didn't go forward (north)");
        Assert.Equal(v.Seat(v.Params.DriverSeat), you.Position);

        // Too fast to get out ...
        List<SimEvent> tooFast = Press(sim, you);
        Assert.DoesNotContain(tooFast, e => e.Type == SimEventType.VehicleLeft);
        Assert.Equal(v.Id, you.Vehicle);

        // ... so brake (holding back brakes it to a stop, then backs it up), then out: beside your seat, on the ground.
        for (int t = 0; t < 3 * Second && v.Speed > 0f; t++)
        {
            Step(sim, 1, p => new InputCommand { Yaw = p.Yaw, Move = p == you ? new Vector2(0f, -1f) : Vector2.Zero });
        }

        Step(sim, Second / 2);
        Assert.True(MathF.Abs(v.Speed) <= Buggy.ExitBelow, $"still at {v.Speed:0.0} m/s");
        List<SimEvent> outside = Press(sim, you);
        Assert.Contains(outside, e => e.Type == SimEventType.VehicleLeft && e.PlayerId == you.Id);
        Assert.Equal(-1, you.Vehicle);
        Assert.Equal(-1, v.Driver);
        Assert.Equal(0f, you.Position.Y);
        Assert.InRange(Flat(you.Position - v.Position), Buggy.Width * 0.5f, Buggy.Width * 0.5f + 1.5f);
    }

    [Fact]
    public void It_turns_the_way_you_steer_and_tighter_the_more_you_do()
    {
        // Left on the stick turns it left (yaw grows) and off to the west; more of it, more turn in the same time.
        float Turned(float stick, out Vector3 at)
        {
            (SimWorld sim, PlayerState you, _, _) = Field();
            VehicleState v = sim.Vehicles[0];
            Press(sim, you);
            Step(sim, 3 * Second / 2, p => new InputCommand { Yaw = p.Yaw, Move = p == you ? new Vector2(-stick, 0.6f) : Vector2.Zero });
            at = v.Position;
            return v.Yaw;
        }

        float half = Turned(0.5f, out Vector3 halfAt), full = Turned(1f, out Vector3 fullAt);
        _out.WriteLine($"1.5 s steering left: half the wheel {half * 180f / MathF.PI:0}° to {halfAt}, all of it {full * 180f / MathF.PI:0}° to {fullAt}");
        Assert.True(half > 0.2f, "it didn't turn left");
        Assert.True(halfAt.X < 0f && fullAt.X < 0f, "it didn't go off to the left (west)");
        Assert.True(full > half * 1.5f, "more steer didn't turn it tighter");
        // The tightest circle (the back axle's), the wheelbase over the tangent of the most steer, is a buggy's.
        Assert.InRange(Buggy.Wheelbase / MathF.Tan(Buggy.MaxSteer), 2f, 8f);
    }

    [Fact]
    public void A_wall_stops_it_and_so_does_anyone_in_its_way()
    {
        (SimWorld sim, PlayerState you, _, PlayerState them) = Field();
        VehicleState v = sim.Vehicles[0];
        Press(sim, you);
        Step(sim, 8 * Second, p => new InputCommand { Yaw = p.Yaw, Move = p == you ? new Vector2(0f, 1f) : Vector2.Zero });
        _out.WriteLine($"into the wall: at {v.Position}, {v.Speed:0.0} m/s");
        Assert.Equal(0f, v.Speed);
        Assert.True(v.Position.Z > -30f + Buggy.Length * 0.5f - 0.1f && v.Position.Z < -24f, $"stopped at {v.Position.Z:0.0}, not short of the wall");

        // Back up, then drive at someone standing in the way: it stops short of them, and they're unharmed.
        Step(sim, 3 * Second, p => new InputCommand { Yaw = p.Yaw, Move = p == you ? new Vector2(0f, -1f) : Vector2.Zero });
        them.Position = v.Position + v.Forward * 6f;
        Step(sim, 4 * Second, p => new InputCommand { Yaw = p.Yaw, Move = p == you ? new Vector2(0f, 1f) : Vector2.Zero });
        float gap = Flat(them.Position - v.Position) - Buggy.Length * 0.5f;
        _out.WriteLine($"someone in the way: stopped {gap:0.00} m short");
        Assert.InRange(gap, 0f, 1.5f);
        Assert.True(them.Alive);
    }

    [Fact]
    public void The_driver_cant_fire_a_passenger_can()
    {
        (SimWorld sim, PlayerState you, PlayerState mate, _) = Field();
        Press(sim, you);
        Press(sim, mate);
        Assert.Equal(1 - Buggy.DriverSeat, mate.Seat);
        Assert.False(sim.Vehicles.MayFire(you));
        Assert.True(sim.Vehicles.MayFire(mate));
        List<SimEvent> events = Step(sim, Second, p => new InputCommand { Yaw = p.Yaw, Buttons = sim.Tick % 20 < 10 ? InputButtons.Fire : InputButtons.None });
        Assert.DoesNotContain(events, e => e.Type == SimEventType.ShotFired && e.PlayerId == you.Id);
        Assert.Contains(events, e => e.Type == SimEventType.ShotFired && e.PlayerId == mate.Id);
    }

    [Fact]
    public void Someone_hit_in_it_gets_out_and_without_a_driver_it_rolls_to_a_stop()
    {
        (SimWorld sim, PlayerState you, PlayerState mate, _) = Field();
        VehicleState v = sim.Vehicles[0];
        Press(sim, you);
        Press(sim, mate);
        Step(sim, 2 * Second, p => new InputCommand { Yaw = p.Yaw, Move = p == you ? new Vector2(0f, 1f) : Vector2.Zero });
        Assert.True(v.Speed > 4f);

        you.Alive = false; // hit
        List<SimEvent> events = Step(sim, 1);
        Assert.Contains(events, e => e.Type == SimEventType.VehicleLeft && e.PlayerId == you.Id);
        Assert.Equal(-1, you.Vehicle);
        Assert.Equal(-1, v.Driver);
        Assert.Equal(v.Id, mate.Vehicle); // the passenger stays
        Step(sim, 6 * Second);
        Assert.Equal(0f, v.Speed);
    }

    [Fact]
    public void Paint_breaks_on_its_body_and_never_counts()
    {
        (SimWorld sim, _, _, _) = Field();
        VehicleState v = sim.Vehicles[0];
        // A ball from the east at its side, at waist height: it meets the body first.
        var from = new Vector3(10f, 0.7f, 0f);
        Assert.True(sim.Collision.SweepSphere(from, v.Position + new Vector3(-10f, 0.7f, 0f), 0.0086f, out SweepHit hit, includeDynamic: true));
        Assert.StartsWith("vehicle#", sim.Collision.Colliders[hit.ColliderId].Name);
        Assert.InRange(hit.Point.X, Buggy.Width * 0.5f - 0.05f, Buggy.Width * 0.5f + 0.05f);
    }

    [Fact]
    public void A_new_round_puts_it_back_empty()
    {
        (SimWorld sim, PlayerState you, _, _) = Field();
        VehicleState v = sim.Vehicles[0];
        Press(sim, you);
        Step(sim, 2 * Second, p => new InputCommand { Yaw = p.Yaw, Move = p == you ? new Vector2(0f, 1f) : Vector2.Zero });
        sim.StartMatch(new MatchSetup { HeroId = 0, Mode = MatchModeKind.Teams, TimeLimit = 900f, StartPods = 2, BotPods = 2, Pickups = false });
        Assert.Equal((Vector3.Zero, 0f, 0f, -1), (v.Position, v.Yaw, v.Speed, v.Driver));
        Assert.Equal(-1, you.Vehicle);
    }

    private static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);
}
