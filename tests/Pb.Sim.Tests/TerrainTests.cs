using System.Diagnostics;
using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;
using Pb.Sim.Vehicles;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>
/// The rolling ground (M5.8): Thornwick Woods' hills, banks, hollow and brook; everything placed standing on them; paint,
/// sight and wheels meeting the same ground its heights describe; bots walking over it and buggies driving it.
/// </summary>
[Collection(BotArenaCollection.Name)]
public class TerrainTests
{
    private const int Second = 120;
    private readonly ITestOutputHelper _out;

    public TerrainTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static LevelLayout Level => TestData.Data.Levels["thornwick_woods"];

    private static Terrain Ground => Level.Terrain!;

    private static Vector3 On(float x, float z, float above = 0f) => new(x, Ground.HeightAt(x, z) + above, z);

    [Fact]
    public void Thornwick_rolls_with_hills_a_hollow_banks_and_a_brook()
    {
        Terrain g = Ground;
        _out.WriteLine($"{g.Cols} × {g.Rows} points every {g.Cell} m, from {g.MinHeight:0.00} m to {g.MaxHeight:0.00} m");
        Assert.True(g.MaxHeight - g.MinHeight > 5f, "it hardly rolls");
        Assert.True(g.HeightAt(140f, -92f) > 3.5f, "Rookery Wood's hill isn't there");
        Assert.True(g.HeightAt(88f, -40f) < -1f, "Rookery hollow isn't there");
        Assert.True(g.HeightAt(6f, -38f) - g.HeightAt(0f, -38f) > 0.8f, "the north lane isn't sunk between banks");

        // The brook: its bed lower than the field beside it, and of mud, where it crosses the farm.
        SurfaceId mud = TestData.Config.Surfaces.Get("mud");
        foreach (float x in new[] { -30f, 30f })
        {
            float bed = g.HeightAt(x, 40.7f), field = g.HeightAt(x, 52f);
            _out.WriteLine($"the brook at x {x}: bed {bed:0.00} m, the field beside it {field:0.00} m");
            Assert.True(field - bed > 0.6f, $"the brook at x {x} is hardly lower than the field");
            Assert.Equal(mud, g.SurfaceAt(x, 40.7f));
        }

        // Where the lane fords it, the lane dips through it.
        float ford = g.HeightAt(0f, 40.6f), lane = g.HeightAt(0f, 56f);
        _out.WriteLine($"the ford: {ford:0.00} m, the lane 15 m on {lane:0.00} m");
        Assert.True(lane - ford > 0.6f);
    }

    [Fact]
    public void It_is_level_under_the_buildings_and_the_fort_stands_on_its_knoll()
    {
        foreach (PlacedBuilding b in Level.Buildings)
        {
            Vector3 at = b.Frame.Origin;
            float here = Ground.HeightAt(at.X, at.Z);
            Assert.Equal(here, at.Y, 3);
            Assert.InRange(MathF.Abs(here - (b.Template.Id == "signal_box" ? 1.6f : 0f)), 0f, 1e-3f);
        }

        // The fort's knoll is level all over the stockade.
        (float low, float high) = Ground.RangeIn(16f, -102f, 44f, -74f);
        Assert.InRange(low, 1.6f - 1e-3f, 1.6f + 1e-3f);
        Assert.InRange(high, 1.6f - 1e-3f, 1.6f + 1e-3f);
    }

    [Fact]
    public void Everything_out_on_it_stands_on_it()
    {
        foreach (OpponentSpawn s in Level.OpponentSpawns.Where(s => s.Id != "fort_tower"))
        {
            Assert.True(MathF.Abs(s.Position.Y - Ground.HeightAt(s.Position.X, s.Position.Z)) < 1e-3f, $"{s.Id} isn't on the ground");
        }

        foreach (VehicleSpot v in Level.VehicleSpots)
        {
            Assert.Equal(Ground.HeightAt(v.Position.X, v.Position.Z), v.Position.Y, 3);
        }

        Assert.All(Level.Pickups.Where(p => p.Id != "pod_tower"),
            p => Assert.True(MathF.Abs(p.Position.Y - Ground.HeightAt(p.Position.X, p.Position.Z)) < 1e-3f, $"{p.Id} floats"));

        // A prop out in the open stands on the ground, a little into it on a slope, never floating over it.
        int props = 0;
        foreach (PropInstance prop in Level.Props.Where(p => p.PrimitiveCount > 0 && Level.Owners[Level.Primitives[p.FirstPrimitive].Owner].StartsWith("prop:")))
        {
            // Its foot (its frame's origin, the floor of its model) at or a little below the ground under its middle.
            float under = Ground.HeightAt(prop.Position.X, prop.Position.Z);
            Assert.True(prop.Position.Y <= under + 1e-3f, $"{prop.Type.Id} at {prop.Position} floats {prop.Position.Y - under:0.00} m over the ground");
            Assert.True(prop.Position.Y > under - 0.6f, $"{prop.Type.Id} at {prop.Position} is sunk {under - prop.Position.Y:0.00} m into the ground");
            props++;
        }

        _out.WriteLine($"{props} props in the open");
        Assert.True(props > 100);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.0085f)]
    [InlineData(0.3f)]
    public void A_sphere_meets_the_ground_where_its_heights_say(float radius)
    {
        var shape = new HeightFieldShape(Ground);
        var random = new Random(41);
        float worst = 0f;
        for (int k = 0; k < 400; k++)
        {
            // From above a point of the site, down and across at up to 60° from straight down.
            float x = -170f + random.NextSingle() * 340f, z = -120f + random.NextSingle() * 240f;
            Vector3 from = On(x, z, 0.5f + random.NextSingle() * 6f);
            float across = random.NextSingle() * 1.7f, heading = random.NextSingle() * MathF.Tau;
            Vector3 d = new Vector3(MathF.Sin(heading) * across, -1f, MathF.Cos(heading) * across) * 12f;
            if (from.Y - Ground.HeightAt(from.X, from.Z) < radius + 0.01f)
            {
                continue;
            }

            bool hit = shape.Sweep(from, d, radius, out float t, out Vector3 normal);
            float reference = Reference(from, d, radius);
            if (float.IsNaN(reference))
            {
                Assert.False(hit && t < 0.98f, $"a sweep from {from} along {d} met nothing, yet the shape says it did at {t:0.000}");
                continue;
            }

            Assert.True(hit, $"a sweep from {from} along {d} (r {radius}) missed the ground it meets at {reference:0.000}");
            float error = MathF.Abs(t - reference) * d.Length();
            worst = MathF.Max(worst, error);
            Assert.True(error < (radius > 0.1f ? 0.06f : 0.01f), $"from {from} along {d} (r {radius}): met at {t:0.0000}, should be {reference:0.0000}");
            Assert.True(normal.Y > 0.5f);
        }

        _out.WriteLine($"radius {radius}: worst {worst * 100f:0.0} cm off");
    }

    /// <summary>When a sphere along the path first comes within its radius of the ground, by marching and halving.</summary>
    private static float Reference(Vector3 from, Vector3 d, float radius)
    {
        float Gap(float t)
        {
            Vector3 c = from + d * t;
            // The nearest the ground comes to the centre: its height at points round it, and under it.
            float best = c.Y - Ground.HeightAt(c.X, c.Z);
            if (radius > 0f)
            {
                for (int ring = 1; ring <= 4; ring++)
                {
                    float rr = radius * ring / 4f;
                    for (int a = 0; a < 16; a++)
                    {
                        float ax = c.X + MathF.Cos(a * MathF.Tau / 16f) * rr, az = c.Z + MathF.Sin(a * MathF.Tau / 16f) * rr;
                        float dy = c.Y - Ground.HeightAt(ax, az);
                        best = MathF.Min(best, MathF.Sign(dy) * MathF.Sqrt(dy * dy + rr * rr));
                    }
                }
            }

            return best - radius;
        }

        const int Steps = 2400;
        float prev = 0f;
        for (int i = 1; i <= Steps; i++)
        {
            float t = i / (float)Steps;
            if (Gap(t) <= 0f)
            {
                float lo = prev, hi = t;
                for (int k = 0; k < 30; k++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (Gap(mid) <= 0f)
                    {
                        hi = mid;
                    }
                    else
                    {
                        lo = mid;
                    }
                }

                return hi;
            }

            prev = t;
        }

        return float.NaN;
    }

    [Fact]
    public void Paint_and_sight_meet_it_with_its_surfaces()
    {
        var sim = new SimWorld(TestData.Config);
        sim.LoadLevel(Level);
        SurfaceAt(sim, On(-30f, 40.7f), "mud");    // the brook's bed
        SurfaceAt(sim, On(30f, -20f), "turf");     // the middle field
        SurfaceAt(sim, On(-110f, -100f), "dirt");  // leaf litter in Blackthorn Wood
        Vector3 lane = On(0f, 60f);
        Assert.True(sim.Collision.SweepSphere(lane + new Vector3(0f, 3f, 0f), lane - new Vector3(0f, 3f, 0f), 0f, out SweepHit onLane));
        Assert.Equal(TestData.Config.Surfaces.Get("gravel"), onLane.Surface);
        Assert.InRange(onLane.Point.Y - lane.Y, -0.01f, 0.03f);

        // Over the north field's rise: crouched either side, the rise hides one from the other; standing, they see over it.
        Vector3 west = On(-60f, -98f), east = On(-16f, -98f);
        var shape = new HeightFieldShape(Ground);
        Assert.True(shape.Sweep(west + new Vector3(0f, 1.1f, 0f), east - west, 0f, out _, out _), "the rise doesn't hide a crouching player");
        Assert.True(Ground.HeightAt(-38f, -98f) > 2.5f);
    }

    private void SurfaceAt(SimWorld sim, Vector3 at, string surface)
    {
        Assert.True(sim.Collision.SweepSphere(at + new Vector3(0f, 2f, 0f), at - new Vector3(0f, 2f, 0f), 0.0085f, out SweepHit hit), $"nothing under {at}");
        _out.WriteLine($"at {at}: {TestData.Config.Surfaces.NameOf(hit.Surface)} at {hit.Point.Y:0.000} m");
        Assert.Equal(TestData.Config.Surfaces.Get(surface), hit.Surface);
        Assert.InRange(hit.Point.Y - at.Y, 0.0085f - 0.002f, 0.0085f + 0.002f); // the ball's middle, its radius over the ground
    }

    [Fact]
    public void It_builds_quickly_and_the_same_every_time()
    {
        LevelDef def = Jsonc.Load<LevelDef>(TestData.Source, "levels/thornwick_woods.jsonc");
        var watch = Stopwatch.StartNew();
        LevelLayout again = LevelFactory.Build(def, "levels/thornwick_woods.jsonc", TestData.Data.Kit);
        _out.WriteLine($"the level, its ground included, in {watch.ElapsedMilliseconds} ms");
        Terrain a = Ground, b = again.Terrain!;
        Assert.Equal((a.Cols, a.Rows), (b.Cols, b.Rows));
        for (int r = 0; r < a.Rows; r++)
        {
            for (int c = 0; c < a.Cols; c++)
            {
                Assert.Equal(a.PointHeight(c, r), b.PointHeight(c, r));
                Assert.Equal(a.KindOf(c, r), b.KindOf(c, r));
            }
        }
    }

    [Fact]
    public void A_bot_walks_down_through_the_brook_and_over_a_rise()
    {
        BotArena arena = BotArena.Create("normal", level: "thornwick_woods");
        BotBrain bot = arena.AddBotAt(arena.Level.OpponentSpawns.First(s => s.Id == "farmyard"), team: 0, archetype: "sentry");
        arena.AddBotAt(arena.Level.OpponentSpawns.First(s => s.Id == "ring_north"), team: 1).Passive = true;
        arena.Hero.Position = On(-150f, 110f);
        bot.Self.Position = On(-12f, 33.5f);
        arena.Start(mode: MatchModeKind.Teams);

        float Walk(Vector3 goal, out float lowest, out float highest)
        {
            bot.Guard(goal, 0f, shout: false);
            float low = float.MaxValue, high = float.MinValue;
            int start = arena.Sim.Tick;
            arena.Run(60 * Second, () =>
            {
                low = MathF.Min(low, bot.Self.Position.Y);
                high = MathF.Max(high, bot.Self.Position.Y);
                return Flat(bot.Self.Position - goal) < 1.5f;
            });
            lowest = low;
            highest = high;
            return (arena.Sim.Tick - start) / (float)Second;
        }

        // Across the brook into the farmyard.
        Vector3 yard = On(-12f, 56f);
        float took = Walk(yard, out float lowest, out _);
        _out.WriteLine($"across the brook in {took:0.0} s, down to {lowest:0.00} m (the yard is at {yard.Y:0.00} m)");
        Assert.True(Flat(bot.Self.Position - yard) < 1.5f, "it didn't get across the brook");
        Assert.True(lowest < yard.Y - 0.5f, "it didn't go down through the bed");
        float off = bot.Self.Position.Y - Ground.HeightAt(bot.Self.Position.X, bot.Self.Position.Z);
        Assert.True(MathF.Abs(off) < 0.1f, $"it isn't on the ground at {bot.Self.Position}: {off:0.00} m off");

        // Over the middle field's east rise.
        bot.Self.Position = On(14f, -26f);
        Vector3 beyond = On(58f, -26f);
        took = Walk(beyond, out _, out float highest);
        _out.WriteLine($"over the rise in {took:0.0} s, up to {highest:0.00} m");
        Assert.True(Flat(bot.Self.Position - beyond) < 1.5f, "it didn't get over the rise");
        Assert.True(highest > 1.4f, "it went round the rise, not over it");
    }

    /// <summary>A world on Thornwick Woods with its first buggy at <paramref name="at"/> facing <paramref name="yaw"/> and you at its wheel.</summary>
    private static (SimWorld Sim, PlayerState You, VehicleState Car) AtTheWheel(Vector3 at, float yaw)
    {
        var sim = new SimWorld(TestData.Config, 7);
        sim.LoadLevel(Level);
        VehicleState car = sim.Vehicles[0];
        PlayerState you = sim.AddPlayer(0, 0, at, yaw);
        sim.AddPlayer(1, 1, On(-150f, 110f), 0f);
        sim.StartMatch(new MatchSetup { HeroId = 0, Mode = MatchModeKind.Teams, TimeLimit = 900f, StartPods = 2, BotPods = 2, Pickups = false });
        sim.GoLive();
        // The round puts every buggy back where it stood: this one goes where the test wants it after.
        car.PutAt(at, yaw);
        sim.Vehicles.Restore(0, car.Capture());
        you.Position = VehicleSet.Door(car, car.Params.DriverSeat);
        var commands = new InputCommand[2];
        commands[0] = new InputCommand { Yaw = yaw, Buttons = InputButtons.Interact };
        sim.Step(commands);
        commands[0] = new InputCommand { Yaw = yaw };
        sim.Step(commands);
        sim.Events.Clear();
        Assert.Equal(car.Id, you.Vehicle);
        return (sim, you, car);
    }

    /// <summary>Full throttle toward <paramref name="to"/>, steering for a point <paramref name="ahead"/> m on along the line there.</summary>
    private static void DriveTo(SimWorld sim, PlayerState you, VehicleState car, Vector3 to, float ahead, Action each, float throttle = 1f)
    {
        var commands = new InputCommand[2];
        for (int t = 0; t < 40 * Second && Flat(car.Position - to) > 4f; t++)
        {
            Vector3 aim = to - car.Position;
            float yaw = MathF.Atan2(-aim.X, -aim.Z);
            float error = MathF.IEEERemainder(yaw - car.Yaw, MathF.Tau);
            commands[0] = new InputCommand { Yaw = you.Yaw, Move = new Vector2(Math.Clamp(-2.5f * error, -1f, 1f), throttle) };
            sim.Step(commands);
            sim.Events.Clear();
            each();
        }
    }

    [Fact]
    public void A_buggy_dips_through_the_ford_and_climbs_out()
    {
        (SimWorld sim, PlayerState you, VehicleState car) = AtTheWheel(On(0f, 78f), 0f);
        float lowest = float.MaxValue, fastest = 0f, leastUp = 1f;
        int airborne = 0, ticks = 0;
        DriveTo(sim, you, car, On(0f, 2f), 15f, () =>
        {
            lowest = MathF.Min(lowest, car.Position.Y);
            fastest = MathF.Max(fastest, car.Speed);
            leastUp = MathF.Min(leastUp, car.Up.Y);
            airborne += car.Contacts == 0 ? 1 : 0;
            ticks++;
        });

        _out.WriteLine($"through the ford in {ticks / (float)Second:0.0} s: down to {lowest:0.00} m (the ford {Ground.HeightAt(0f, 40.6f):0.00} m), " +
                       $"fastest {fastest:0.0} m/s, least upright {MathF.Acos(leastUp) * 57.3f:0} °, {airborne} ticks with no wheel down, now at {car.Position}");
        Assert.True(Flat(car.Position - On(0f, 2f)) <= 4f, "it didn't get through the ford");
        Assert.True(lowest < Ground.HeightAt(0f, 40.6f) + 0.3f, "it didn't go down into the ford");
        Assert.True(fastest > 8f, $"it never got going ({fastest:0.0} m/s)");
        Assert.True(leastUp > MathF.Cos(25f * MathF.PI / 180f), "it nearly turned over");
        Assert.True(airborne < Second / 2, "it was in the air too long");
        Assert.Equal(car.Id, you.Vehicle);
    }

    [Fact]
    public void A_buggy_climbs_over_Blackthorn_hill_on_the_ring()
    {
        (SimWorld sim, PlayerState you, VehicleState car) = AtTheWheel(On(-160f, -6f), 0f);
        float highest = float.MinValue, leastUp = 1f, slowest = float.MaxValue;
        int ticks = 0;
        DriveTo(sim, you, car, On(-160f, -100f), 15f, () =>
        {
            highest = MathF.Max(highest, car.Position.Y);
            leastUp = MathF.Min(leastUp, car.Up.Y);
            ticks++;
            if (ticks > 4 * Second)
            {
                slowest = MathF.Min(slowest, car.Speed);
            }
        });

        _out.WriteLine($"over the hill in {ticks / (float)Second:0.0} s: up to {highest:0.00} m, least upright {MathF.Acos(leastUp) * 57.3f:0} °, " +
                       $"slowest once going {slowest:0.0} m/s");
        Assert.True(Flat(car.Position - On(-160f, -100f)) <= 4f, "it didn't get over the hill");
        Assert.True(highest > 1f, "the ring doesn't go over the hill");
        Assert.True(slowest > 4f, "it struggled up the hill");
        Assert.True(leastUp > MathF.Cos(25f * MathF.PI / 180f), "it nearly turned over");
    }

    [Fact]
    public void A_parked_buggy_rests_on_the_slope_where_it_was_left()
    {
        var sim = new SimWorld(TestData.Config, 7);
        sim.LoadLevel(Level);
        VehicleState car = sim.Vehicles[0];
        // Across the side of Rookery Wood's hill, facing along the slope's contour, then down it.
        foreach ((Vector3 at, float yaw) in new[] { (On(140f, -70f), MathF.PI * 0.5f), (On(140f, -70f), 0f) })
        {
            car.PutAt(at, yaw);
            sim.Vehicles.Restore(0, car.Capture());
            var commands = Array.Empty<InputCommand>();
            for (int t = 0; t < 4 * Second; t++)
            {
                sim.Step(commands);
                sim.Events.Clear();
            }

            _out.WriteLine($"left at {at} facing {yaw * 57.3f:0} °: {Flat(car.Position - at):0.000} m off, " +
                           $"{MathF.Acos(car.Up.Y) * 57.3f:0.0} ° off upright, asleep {car.Asleep}");
            Assert.True(Flat(car.Position - at) < 0.15f, "it rolled away");
            Assert.True(car.Contacts == 15, "it isn't on all four wheels");
        }
    }

    private static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);
}
