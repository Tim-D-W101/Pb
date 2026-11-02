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

/// <summary>
/// Vehicles (M5.4): getting in and out, driving, what stops them, and the field's rules for them; and (M5.7) how they
/// handle: sitting on their springs, the weight moving as they pull away, brake and corner, the grip each surface gives,
/// the handbrake, slopes, turning over, and stepping the same way every time without allocating.
/// </summary>
public class VehicleTests
{
    private const int Second = 120;
    private readonly ITestOutputHelper _out;

    public VehicleTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static VehicleParams Buggy => TestData.Config.Vehicles["buggy"];

    /// <summary>
    /// Open ground with a wall <paramref name="wall"/> m north (none at 0), a buggy at the origin facing north (−Z), you
    /// beside its driver's door and two others.
    /// </summary>
    private static (SimWorld Sim, PlayerState You, PlayerState Mate, PlayerState Them) Field(bool live = true, string ground = "turf", float wall = 30f)
    {
        var sim = new SimWorld(TestData.Config, 3);
        sim.Collision.Add(new PlaneShape(Vector3.UnitY, 0f), TestData.Config.Surfaces.Get(ground), "ground");
        if (wall > 0f)
        {
            sim.Collision.Add(new BoxShape(new Vector3(0f, 2f, -wall - 0.5f), Quaternion.Identity, new Vector3(40f, 2f, 0.5f)),
                TestData.Config.Surfaces.Get("concrete"), "north wall");
        }

        sim.Vehicles.Load(new[] { new VehicleSpot("buggy", Vector3.Zero, 0f) }, TestData.Config.Vehicles, sim.Collision,
            new Aabb(new Vector3(-400f, -2f, -400f), new Vector3(400f, 30f, 400f)), TestData.Config.Surfaces.Get("metal"));
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
        (SimWorld sim, PlayerState you, _, _) = Field(wall: 0f);
        VehicleState v = sim.Vehicles[0];
        List<SimEvent> inside = Press(sim, you);
        Assert.Contains(inside, e => e.Type == SimEventType.VehicleEntered && e.PlayerId == you.Id && e.Extra == v.Params.DriverSeat);
        Assert.Equal((v.Id, v.Params.DriverSeat), (you.Vehicle, you.Seat));
        Assert.Equal(you.Id, v.Driver);
        Assert.Equal(v.Seat(v.Params.DriverSeat), you.Position);
        Assert.Equal(Buggy.SeatedEyeHeight, you.EyeHeight);

        // Full throttle for eight seconds: up through the gears to near the field's limit, never past it, and you go with it.
        float fastest = 0f;
        int topGear = 0;
        for (int t = 0; t < 8 * Second; t++)
        {
            Step(sim, 1, p => new InputCommand { Yaw = p.Yaw, Move = p == you ? new Vector2(0f, 1f) : Vector2.Zero });
            fastest = MathF.Max(fastest, v.Speed);
            topGear = Math.Max(topGear, v.Gear);
        }

        _out.WriteLine($"after 8 s: {v.Speed:0.0} m/s in gear {v.Gear} at {v.Rpm:0} rpm, at {v.Position}");
        Assert.InRange(v.Speed, Buggy.MaxSpeed * 0.9f, Buggy.MaxSpeed);
        Assert.True(fastest <= Buggy.MaxSpeed, $"it went {fastest:0.00} m/s, past the limit");
        Assert.Equal(Buggy.GearSpeeds.Count, topGear);
        Assert.True(v.Position.Z < -10f, "it didn't go forward (north)");
        Assert.Equal(v.Seat(v.Params.DriverSeat), you.Position);

        // Too fast to get out ...
        List<SimEvent> tooFast = Press(sim, you);
        Assert.DoesNotContain(tooFast, e => e.Type == SimEventType.VehicleLeft);
        Assert.Equal(v.Id, you.Vehicle);

        // ... so brake (holding back brakes it to a stop, then backs it up), then out: beside your seat, on the ground.
        for (int t = 0; t < 4 * Second && v.Speed > 0.3f; t++)
        {
            Step(sim, 1, p => new InputCommand { Yaw = p.Yaw, Move = p == you ? new Vector2(0f, -1f) : Vector2.Zero });
        }

        Step(sim, Second / 2);
        Assert.True(MathF.Abs(v.Speed) <= Buggy.ExitBelow, $"still at {v.Speed:0.0} m/s");
        List<SimEvent> outside = Press(sim, you);
        Assert.Contains(outside, e => e.Type == SimEventType.VehicleLeft && e.PlayerId == you.Id);
        Assert.Equal(-1, you.Vehicle);
        Assert.Equal(-1, v.Driver);
        Assert.InRange(you.Position.Y, -0.02f, 0.02f);
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
        float hardest = 0f;
        for (int t = 0; t < 8 * Second; t++)
        {
            Step(sim, 1, p => new InputCommand { Yaw = p.Yaw, Move = p == you ? new Vector2(0f, 1f) : Vector2.Zero });
            hardest = MathF.Max(hardest, v.Impact);
        }

        _out.WriteLine($"into the wall: at {v.Position}, {v.Speed:0.00} m/s, hit it at {hardest:0.0} m/s");
        Assert.InRange(v.Speed, -0.3f, 0.3f);
        Assert.True(hardest > 5f, "it never hit the wall");
        Assert.True(v.Position.Z > -30f + Buggy.Length * 0.5f - 0.1f && v.Position.Z < -24f, $"stopped at {v.Position.Z:0.0}, not short of the wall");
        Assert.True(v.Up.Y > 0.95f, "it didn't stay on its wheels");

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
    public void The_driver_fires_one_handed_and_wider_a_passenger_as_usual()
    {
        (SimWorld sim, PlayerState you, PlayerState mate, _) = Field();
        Press(sim, you);
        Press(sim, mate);
        Assert.Equal(1 - Buggy.DriverSeat, mate.Seat);
        Assert.True(sim.Vehicles.MayFire(you));
        Assert.True(sim.Vehicles.MayFire(mate));
        Assert.Equal(Buggy.Seats[Buggy.DriverSeat].SpreadScale, sim.Vehicles.SpreadScale(you));
        Assert.Equal(1f, sim.Vehicles.SpreadScale(mate));

        // Both pull the trigger for eight seconds, sitting still, aiming the same way: the driver's shots spread wider.
        List<SimEvent> events = Step(sim, 8 * Second, p => new InputCommand { Yaw = 0f, Buttons = sim.Tick % 20 < 10 ? InputButtons.Fire : InputButtons.None });
        float Spread(PlayerState who)
        {
            Vector3[] shots = events.Where(e => e.Type == SimEventType.ShotFired && e.PlayerId == who.Id).Select(e => Vector3.Normalize(e.Velocity)).ToArray();
            Assert.True(shots.Length > 20, $"{who.Name} fired only {shots.Length} shots");
            Vector3 mean = Vector3.Normalize(shots.Aggregate(Vector3.Zero, (a, b) => a + b));
            return shots.Average(d => MathF.Acos(Math.Clamp(Vector3.Dot(d, mean), -1f, 1f))) * 180f / MathF.PI;
        }

        float driver = Spread(you), passenger = Spread(mate);
        _out.WriteLine($"mean spread: driver {driver:0.00}°, passenger {passenger:0.00}°");
        Assert.True(driver > passenger * 1.5f, "the driver's one-handed shots aren't wider");
    }

    [Fact]
    public void Driving_the_marker_hangs_on_its_sling_and_comes_up_to_fire()
    {
        (SimWorld sim, PlayerState you, PlayerState mate, _) = Field();
        Press(sim, you);
        Press(sim, mate);
        HitboxParams rig = TestData.Config.Hitboxes;
        Span<PosedBox> boxes = stackalloc PosedBox[HitboxRig.PartCount];
        Vector3 MarkerAt(PlayerState who, Span<PosedBox> into)
        {
            HitboxRig.Pose(HitboxPose.Of(who), rig, TestData.Config.Movement.LeanPivotBelowEye, into);
            return into[5].Center;
        }

        // At the wheel, not firing: the marker goes onto its sling (its hitbox with it); the passenger's stays up.
        Step(sim, Second);
        Vector3 aimedAt = Vector3.Zero;
        Assert.Equal(1f, you.Sling);
        Assert.Equal(0f, mate.Sling);
        Vector3 slung = MarkerAt(you, boxes);
        you.Sling = 0f;
        aimedAt = MarkerAt(you, boxes);
        you.Sling = 1f;
        Assert.True(Vector3.Distance(slung, aimedAt) > 0.1f, "the slung marker's hitbox didn't move");

        // The trigger held: it comes up over the sling's time and fires once it's up.
        int firedAt = -1;
        for (int t = 0; t < Second && firedAt < 0; t++)
        {
            List<SimEvent> events = Step(sim, 1, p => new InputCommand { Yaw = p.Yaw, Buttons = p == you ? InputButtons.Fire : InputButtons.None });
            if (events.Any(e => e.Type == SimEventType.ShotFired && e.PlayerId == you.Id))
            {
                firedAt = t;
            }
        }

        _out.WriteLine($"the marker came up off its sling and fired after {firedAt / (float)Second * 1000f:0} ms");
        Assert.InRange(firedAt / (float)Second, rig.WheelSling * 0.8f, rig.WheelSling + 0.1f);
        Assert.Equal(0f, you.Sling);

        // Left alone, back on the sling after a while; out of the buggy, up at once.
        Step(sim, (int)((rig.WheelUpFor + rig.WheelSling) * Second) + 2);
        Assert.Equal(1f, you.Sling);
        Press(sim, you);
        Assert.Equal(-1, you.Vehicle);
        Assert.Equal(0f, you.Sling);
    }

    [Fact]
    public void Someone_hit_in_it_gets_out_and_without_a_driver_it_rolls_to_a_stop()
    {
        (SimWorld sim, PlayerState you, PlayerState mate, _) = Field(wall: 0f);
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
        Step(sim, 8 * Second);
        Assert.Equal(0f, v.Speed);
        Assert.True(v.Asleep, "it never came to rest");
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

    [Fact]
    public void It_sits_still_on_its_springs_and_sleeps()
    {
        (SimWorld sim, _, _, _) = Field();
        VehicleState v = sim.Vehicles[0];
        Step(sim, 2 * Second);
        // Each spring pressed by a quarter of its weight: the body where it was put, level, and at rest.
        for (int w = 0; w < VehicleState.WheelCount; w++)
        {
            Assert.InRange(v.Compression[w], Buggy.StaticCompression - 0.002f, Buggy.StaticCompression + 0.002f);
            Assert.InRange(v.Load[w], Buggy.Mass * VehicleParams.Gravity / 4f * 0.98f, Buggy.Mass * VehicleParams.Gravity / 4f * 1.02f);
        }

        Assert.InRange(v.Position.Y, -0.003f, 0.003f);
        Assert.True(v.Up.Y > 0.9999f);
        Assert.True(v.Asleep);
        Assert.Equal(Vector3.Zero, v.Velocity);
    }

    [Fact]
    public void The_weight_moves_as_it_pulls_away_brakes_and_corners()
    {
        (SimWorld sim, PlayerState you, _, _) = Field(ground: "asphalt", wall: 0f);
        VehicleState v = sim.Vehicles[0];
        Press(sim, you);
        float Pitch() => MathF.Asin(Math.Clamp(v.BodyForward.Y, -1f, 1f)) * 180f / MathF.PI; // nose up positive
        float Roll() => MathF.Asin(Math.Clamp(Vector3.Dot(v.Up, v.Right), -1f, 1f)) * 180f / MathF.PI; // top to the right positive

        // Pulling away: it squats, the load onto the back wheels.
        Step(sim, Second / 2, p => new InputCommand { Yaw = p.Yaw, Move = p == you ? new Vector2(0f, 1f) : Vector2.Zero });
        float squat = Pitch(), backShare = (v.Load[2] + v.Load[3]) / (v.Load[0] + v.Load[1] + v.Load[2] + v.Load[3]);
        Step(sim, 5 * Second, p => new InputCommand { Yaw = p.Yaw, Move = p == you ? new Vector2(0f, 1f) : Vector2.Zero });

        // Braking hard: it dives, the load onto the front.
        Step(sim, Second / 3, p => new InputCommand { Yaw = p.Yaw, Move = p == you ? new Vector2(0f, -1f) : Vector2.Zero });
        float dive = Pitch(), frontShare = (v.Load[0] + v.Load[1]) / (v.Load[0] + v.Load[1] + v.Load[2] + v.Load[3]);

        // A hard right-hander: it leans out (to the left), the load onto the left wheels.
        Step(sim, 3 * Second, p => new InputCommand { Yaw = p.Yaw, Move = p == you ? new Vector2(0f, 1f) : Vector2.Zero });
        Step(sim, 2 * Second, p => new InputCommand { Yaw = p.Yaw, Move = p == you ? new Vector2(1f, 0.5f) : Vector2.Zero });
        float lean = Roll(), leftShare = (v.Load[0] + v.Load[2]) / (v.Load[0] + v.Load[1] + v.Load[2] + v.Load[3]);
        float sideways = MathF.Abs(v.AngularVelocity.Y) * v.Velocity.Length() / VehicleParams.Gravity;
        _out.WriteLine($"pulling away: nose up {squat:0.00}°, {backShare:P0} on the back; braking: nose {dive:0.00}°, {frontShare:P0} on the " +
                       $"front; cornering at {sideways:0.00} g: leaning {lean:0.00}°, {leftShare:P0} on the outside");
        Assert.True(squat > 0.5f && backShare > 0.53f, "it didn't squat pulling away");
        Assert.True(dive < -1.5f && frontShare > 0.6f, "it didn't dive braking");
        Assert.True(sideways > 0.4f, "it didn't corner hard");
        Assert.True(lean < -1.5f && leftShare > 0.65f, "it didn't lean out of the corner");
    }

    [Fact]
    public void Each_surface_grips_as_much_as_it_does()
    {
        // From standstill, three seconds' full throttle then braking to a stop, on each: mud gets it going slowest, spins
        // the back wheels, and stops it longest; asphalt the other way.
        (float Stopping, float Spin) Run(string ground)
        {
            (SimWorld sim, PlayerState you, _, _) = Field(ground: ground, wall: 0f);
            VehicleState v = sim.Vehicles[0];
            Press(sim, you);
            float spin = 0f;
            for (int t = 0; t < 2 * Second; t++)
            {
                Step(sim, 1, p => new InputCommand { Yaw = p.Yaw, Move = p == you ? new Vector2(0f, 1f) : Vector2.Zero });
                spin = MathF.Max(spin, v.Slide[2]);
            }

            float speed = v.Speed, from = v.Position.Z;
            for (int t = 0; t < 4 * Second && v.Speed > 0.3f; t++)
            {
                Step(sim, 1, p => new InputCommand { Yaw = p.Yaw, Move = p == you ? new Vector2(0f, -1f) : Vector2.Zero });
            }

            float stopping = from - v.Position.Z;
            _out.WriteLine($"{ground}: {speed:0.0} m/s after 2 s, the back wheels spinning up to {spin:0.0} m/s; stopped in {stopping:0.0} m");
            // Stopping distance over the square of the speed: one over twice the deceleration.
            return (stopping / (speed * speed), spin);
        }

        (float mudStop, float mudSpin) = Run("mud");
        (float turfStop, float turfSpin) = Run("turf");
        (float asphaltStop, float asphaltSpin) = Run("asphalt");
        Assert.True(mudStop > turfStop * 1.4f && turfStop > asphaltStop * 1.2f, "the stopping distances don't follow the grip");
        Assert.True(mudSpin > 2f && mudSpin > turfSpin && turfSpin >= asphaltSpin, "mud didn't spin the wheels");
    }

    [Fact]
    public void The_handbrake_swings_the_tail_round()
    {
        float Turn(bool handbrake, out float slide)
        {
            (SimWorld sim, PlayerState you, _, _) = Field(wall: 0f);
            VehicleState v = sim.Vehicles[0];
            Press(sim, you);
            Step(sim, 5 * Second, p => new InputCommand { Yaw = p.Yaw, Move = p == you ? new Vector2(0f, 1f) : Vector2.Zero });
            float before = v.Yaw;
            slide = 0f;
            for (int t = 0; t < Second; t++)
            {
                Step(sim, 1, p => new InputCommand
                {
                    Yaw = p.Yaw, Move = p == you ? new Vector2(-1f, 0f) : Vector2.Zero, Buttons = handbrake && p == you ? InputButtons.Jump : InputButtons.None,
                });
                slide = MathF.Max(slide, v.Slide[3]);
            }

            return Wrap(v.Yaw - before) * 180f / MathF.PI;
        }

        float steered = Turn(false, out float steeredSlide), pulled = Turn(true, out float pulledSlide);
        _out.WriteLine($"a second of full left lock: {steered:0}° (the back sliding {steeredSlide:0.0} m/s); with the handbrake: {pulled:0}° ({pulledSlide:0.0} m/s)");
        Assert.True(steered > 10f, "it didn't turn left");
        Assert.True(pulled > steered * 1.5f, "the handbrake didn't swing it round");
        Assert.True(pulledSlide > 4f, "the back wheels didn't slide");
    }

    [Fact]
    public void Parked_on_a_slope_it_stays_put()
    {
        // A 12° slope rising to the north: an empty buggy settles onto it and stays there.
        var sim = new SimWorld(TestData.Config, 3);
        float slope = 12f * MathF.PI / 180f;
        sim.Collision.Add(new PlaneShape(new Vector3(0f, MathF.Cos(slope), MathF.Sin(slope)), 0f), TestData.Config.Surfaces.Get("turf"), "hill");
        sim.Vehicles.Load(new[] { new VehicleSpot("buggy", new Vector3(0f, 0.05f, 0f), 0.4f) }, TestData.Config.Vehicles, sim.Collision,
            new Aabb(new Vector3(-100f, -50f, -100f), new Vector3(100f, 50f, 100f)), TestData.Config.Surfaces.Get("metal"));
        VehicleState v = sim.Vehicles[0];
        Step(sim, 3 * Second);
        Vector3 settled = v.Position;
        Step(sim, 10 * Second);
        _out.WriteLine($"settled at {settled}, ten seconds later at {v.Position}, its top {v.Up}, asleep {v.Asleep}");
        Assert.True(Vector3.Distance(settled, v.Position) < 0.05f, "it crept down the slope");
        Assert.True(Vector3.Dot(v.Up, new Vector3(0f, MathF.Cos(slope), MathF.Sin(slope))) > 0.995f, "it isn't lying on the slope");
        Assert.True(v.Asleep);
    }

    [Fact]
    public void Turned_over_it_is_righted()
    {
        (SimWorld sim, _, _, _) = Field();
        VehicleState v = sim.Vehicles[0];
        // Dropped on its roof.
        v.Position = new Vector3(0f, 1.5f, 0f);
        v.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI);
        v.Asleep = false;
        Step(sim, Second);
        Assert.True(v.Up.Y < 0f, "it didn't land on its roof");
        Step(sim, (int)(Buggy.RightAfter * Second) + 2 * Second);
        Assert.True(v.Up.Y > 0.99f, "it wasn't righted");
        Assert.InRange(v.Position.Y, -0.05f, 0.15f);
    }

    [Fact]
    public void It_drives_the_same_every_time_and_never_allocates()
    {
        string Drive(out long allocated)
        {
            (SimWorld sim, PlayerState you, _, _) = Field(ground: "gravel", wall: 0f);
            VehicleState v = sim.Vehicles[0];
            Press(sim, you);
            var commands = new InputCommand[sim.Players.Count];
            void Run(int ticks)
            {
                for (int t = 0; t < ticks; t++)
                {
                    for (int i = 0; i < commands.Length; i++)
                    {
                        commands[i] = new InputCommand { Yaw = sim.Players[i].Yaw };
                    }

                    int k = sim.Tick;
                    commands[you.Id].Move = new Vector2(MathF.Sin(k * 0.013f), k % 600 < 450 ? 1f : -0.6f);
                    commands[you.Id].Buttons = k % 700 > 650 ? InputButtons.Jump : InputButtons.None;
                    sim.Step(commands);
                    sim.Events.Clear();
                }
            }

            Run(Second); // warmed up
            allocated = Allocations.During(() => Run(10 * Second));
            return $"{v.Position.X:R} {v.Position.Y:R} {v.Position.Z:R} {v.Orientation} {v.Velocity} {v.AngularVelocity} {v.Gear} {v.Rpm:R}";
        }

        string first = Drive(out long allocated), second = Drive(out _);
        _out.WriteLine(first);
        Assert.Equal(first, second);
        Assert.Equal(0, allocated);
    }

    private static float Wrap(float a)
    {
        a %= MathF.Tau;
        return a > MathF.PI ? a - MathF.Tau : a <= -MathF.PI ? a + MathF.Tau : a;
    }

    private static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);
}
