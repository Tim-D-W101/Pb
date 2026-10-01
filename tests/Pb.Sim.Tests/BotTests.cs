using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Ballistics;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Players;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>Bots in the compound, run headless: senses, aim, behaviours, difficulty, determinism.</summary>
public class BotTests
{
    private const int Second = 120;

    private readonly ITestOutputHelper _out;

    public BotTests(ITestOutputHelper output)
    {
        _out = output;
    }

    [Fact]
    public void The_aim_solution_leads_a_moving_target_and_holds_over_for_drop()
    {
        SimConfig config = TestData.Config;
        var eye = new Vector3(0f, 1.6f, 0f);
        var target = new Vector3(0f, 1.2f, -25f);
        var velocity = new Vector3(3f, 0f, 0f); // running across at 3 m/s
        (float yaw, float pitch) = BotAim.Solve(config, eye, target, velocity);
        Vector3 launch = ViewAngles.Forward(yaw, pitch) * config.Shot.MuzzleVelocity;

        // Fly the ball with the real integrator and find its closest approach to where the target is then.
        Span<Vector3> points = stackalloc Vector3[512];
        TrajectoryResult flight = TrajectoryPredictor.Predict(config.Projectile, null, null, 0, eye, launch, 1f / 240f, 1.5f, points);
        float closest = float.MaxValue;
        for (int i = 0; i < flight.PointCount; i++)
        {
            Vector3 there = target + velocity * (i / 240f);
            closest = MathF.Min(closest, Vector3.Distance(points[i], there));
        }

        Assert.True(closest < 0.1f, $"missed the moving target by {closest:0.00} m");

        // Without the lead it would miss by about the distance the target covers in flight.
        (float yaw0, float pitch0) = BotAim.Solve(config, eye, target, Vector3.Zero);
        Assert.True(MathF.Abs(BotAim.Wrap(yaw - yaw0)) > 0.02f);
        Assert.True(pitch0 > ViewAngles.FromDirection(target - eye).Pitch, "no holdover for the drop");
    }

    [Fact]
    public void Without_spread_or_error_the_solver_hits_a_still_target_at_every_range()
    {
        SimConfig config = TestData.Config;
        var eye = new Vector3(0f, 1.6f, 0f);
        Span<Vector3> points = stackalloc Vector3[2048];
        foreach (float range in new[] { 10f, 20f, 30f, 40f })
        {
            var target = new Vector3(0f, 1.2f, -range);
            (float yaw, float pitch) = BotAim.Solve(config, eye, target, Vector3.Zero);
            Vector3 launch = ViewAngles.Forward(yaw, pitch) * config.Shot.MuzzleVelocity;
            TrajectoryResult flight = TrajectoryPredictor.Predict(config.Projectile, null, null, 0, eye, launch, 1f / 1000f, 1.5f, points);
            float closest = float.MaxValue;
            for (int i = 0; i < flight.PointCount; i++)
            {
                closest = MathF.Min(closest, Vector3.Distance(points[i], target));
            }

            Assert.True(closest < 0.05f, $"missed a still target at {range} m by {closest * 100f:0.0} cm");
        }
    }

    [Fact]
    public void Detection_is_slower_at_range_in_the_dark_crouched_still_and_out_of_the_corner_of_the_eye()
    {
        SenseParams p = TestData.Data.Bots.Senses;
        DifficultyParams tier = TestData.Data.Bots.Difficulty["normal"];
        float Rate(float distance = 10f, float angle = 0f, float light = 1f, bool crouch = false, float speed = 0f, int parts = 3) =>
            BotSenses.DetectionRate(p, tier, distance, angle, light, crouch, speed, parts);

        Assert.True(Rate(distance: 30f) < Rate(distance: 10f));
        Assert.True(Rate(light: 0f) < Rate(light: 1f));
        Assert.True(Rate(crouch: true) < Rate());
        Assert.True(Rate() < Rate(speed: 5f));
        Assert.True(Rate(angle: p.HalfFieldOfView * 0.9f) < Rate());
        Assert.True(Rate(parts: 1) < Rate(parts: 3));
        Assert.Equal(0f, Rate(parts: 0));
        Assert.Equal(0f, Rate(distance: tier.SightRange + 1f));
        Assert.Equal(0f, Rate(angle: p.HalfFieldOfView + 0.01f));
    }

    [Fact]
    public void Nobody_is_seen_through_a_wall()
    {
        BotArena arena = BotArena.Create("hard");
        BotBrain bot = arena.AddBot("yard_east");
        arena.Start();
        // Behind something solid, close and standing: never spotted, never even a glimpse.
        Vector3 hidden = HiddenSpot(arena, bot, 6f, 12f);
        arena.PlaceHero(hidden, bot.Self.Position);
        float meter = 0f;
        arena.Run(6 * Second, () =>
        {
            meter = MathF.Max(meter, bot.Senses.For(0)?.Meter ?? 0f);
            return false;
        });
        Assert.Equal(0f, meter);
        Assert.False(bot.Senses.For(0)?.Visible ?? false);
    }

    [Fact]
    public void Shots_carry_less_far_through_walls()
    {
        bool Hears(bool behindWall)
        {
            BotArena arena = BotArena.Create("normal");
            BotBrain bot = arena.AddBot("yard_east");
            arena.Start();
            // 30 m: inside the open-air range of a shot, beyond the muffled one.
            Vector3 spot = behindWall ? HiddenSpot(arena, bot, 28f, 32f) : arena.SpotInFront(bot, 30f);
            arena.PlaceHero(spot, spot + new Vector3(1f, 0f, 0f)); // facing away: not seen
            arena.HeroScript = (tick, me) => new InputCommand { Tick = tick, Yaw = me.Yaw, Pitch = -0.6f, Buttons = tick == 10 ? InputButtons.Fire : InputButtons.None };
            bool heard = false;
            arena.Run(Second, () =>
            {
                Awareness? a = bot.Senses.For(0);
                heard |= a is { HasLead: true, LastKnownSeen: false };
                return heard;
            });
            return heard;
        }

        Assert.True(Hears(behindWall: false), "didn't hear a shot 30 m away in the open");
        Assert.False(Hears(behindWall: true), "heard a shot 30 m away through a wall");
    }

    [Fact]
    public void Each_difficulty_tier_is_at_least_as_good_as_the_one_below()
    {
        DifficultyParams easy = TestData.Data.Bots.Difficulty["easy"];
        DifficultyParams normal = TestData.Data.Bots.Difficulty["normal"];
        DifficultyParams hard = TestData.Data.Bots.Difficulty["hard"];
        foreach ((DifficultyParams lower, DifficultyParams higher) in new[] { (easy, normal), (normal, hard) })
        {
            Assert.True(higher.ReactionTime <= lower.ReactionTime, $"{higher.Id} reacts slower than {lower.Id}");
            Assert.True(higher.AimError <= lower.AimError, $"{higher.Id} aims worse than {lower.Id}");
            Assert.True(higher.TrackingLag <= lower.TrackingLag, $"{higher.Id} tracks worse than {lower.Id}");
            Assert.True(higher.DecisionInterval <= lower.DecisionInterval, $"{higher.Id} thinks slower than {lower.Id}");
            Assert.True(higher.SightRange >= lower.SightRange, $"{higher.Id} sees less far than {lower.Id}");
            Assert.True(higher.DetectionScale >= lower.DetectionScale, $"{higher.Id} notices slower than {lower.Id}");
            Assert.True(higher.HearingScale >= lower.HearingScale, $"{higher.Id} hears less than {lower.Id}");
            Assert.True(higher.TurnSpeed >= lower.TurnSpeed, $"{higher.Id} turns slower than {lower.Id}");
            Assert.True(higher.PullInterval <= lower.PullInterval, $"{higher.Id} shoots slower than {lower.Id}");
        }
    }

    [Fact]
    public void Standing_close_in_daylight_is_spotted_sooner_than_crouching_far_off()
    {
        float Spot(float distance, bool crouch)
        {
            BotArena arena = BotArena.Create("normal");
            BotBrain bot = arena.AddBot("yard_east");
            arena.Start();
            Vector3 spot = arena.SpotInFront(bot, distance);
            arena.PlaceHero(spot, bot.Self.Position);
            arena.HeroScript = (tick, me) => new InputCommand { Tick = tick, Yaw = me.Yaw, Buttons = crouch ? InputButtons.Crouch : InputButtons.None };
            bot.Passive = false;
            int ticks = 0;
            arena.Run(12 * Second, () =>
            {
                ticks++;
                return bot.Senses.For(0) is { Spotted: true };
            });
            return ticks / (float)Second;
        }

        float near = Spot(8f, crouch: false);
        float far = Spot(25f, crouch: true);
        _out.WriteLine($"spotted standing at 8 m after {near:0.00} s, crouching at 25 m after {far:0.00} s");
        Assert.True(near < 1.5f, $"took {near:0.00} s to spot someone standing 8 m away");
        Assert.True(far > near * 2f, $"crouching at 25 m ({far:0.00} s) should take much longer than standing at 8 m ({near:0.00} s)");
    }

    [Fact]
    public void A_sentry_eliminates_someone_standing_in_the_open()
    {
        BotArena arena = BotArena.Create("hard");
        BotBrain bot = arena.AddBot("yard_east");
        arena.Start();
        arena.PlaceHero(arena.SpotInFront(bot, 14f), bot.Self.Position + new Vector3(0f, 0f, 30f));
        arena.Run(12 * Second, () => !arena.Hero.Alive);
        int shots = arena.ShotsBy(bot.Self.Id);
        _out.WriteLine($"you out={!arena.Hero.Alive} after {arena.Sim.Tick / (float)Second:0.00} s, {shots} shots, mode {bot.Label}");
        Assert.False(arena.Hero.Alive);
        Assert.Equal(bot.Self.Id, arena.Hero.EliminatedBy);
        SimEvent first = arena.Log.First(e => e.Type == SimEventType.ShotFired && e.PlayerId == bot.Self.Id);
        Assert.True(first.Tick / (float)Second >= arena.Tier.ReactionTime, "fired before it could have reacted");
    }

    [Fact]
    public void A_shot_out_of_sight_makes_an_idle_bot_come_and_look()
    {
        BotArena arena = BotArena.Create("normal");
        BotBrain bot = arena.AddBot("yard_east");
        arena.Start();
        // You stand round the back of something solid, 20 m off, and fire once into the ground.
        Vector3 hidden = HiddenSpot(arena, bot, 16f, 26f);
        arena.PlaceHero(hidden, hidden + new Vector3(1f, 0f, 0f));
        arena.HeroScript = (tick, me) => new InputCommand { Tick = tick, Yaw = me.Yaw, Pitch = -0.6f, Buttons = tick == 30 ? InputButtons.Fire : InputButtons.None };
        float before = Vector3.Distance(bot.Self.Position, hidden);
        var modes = new HashSet<BotMode>();
        arena.Run(10 * Second, () =>
        {
            modes.Add(bot.Mode);
            return false;
        });
        float after = Vector3.Distance(bot.Self.Position, hidden);
        _out.WriteLine($"modes {string.Join(", ", modes)}; distance {before:0.0} → {after:0.0} m");
        Assert.Contains(BotMode.Suspicious, modes);
        Assert.Contains(BotMode.Investigate, modes);
        Assert.True(after < before - 5f, $"didn't come closer ({before:0.0} → {after:0.0} m)");
    }

    [Fact]
    public void A_patroller_walks_its_route()
    {
        BotArena arena = BotArena.Create("normal");
        BotBrain bot = arena.AddBot("warehouse_floor");
        arena.Start();
        arena.PlaceHero(new Vector3(60f, 0f, 45f), new Vector3(60f, 0f, 40f)); // far out of the way
        IReadOnlyList<Vector3> route = bot.Route!.Points;
        var reached = new bool[route.Count];
        arena.Run(40 * Second, () =>
        {
            for (int i = 0; i < route.Count; i++)
            {
                reached[i] |= Vector3.Distance(bot.Self.Position with { Y = 0f }, route[i] with { Y = 0f }) < 1.2f;
            }

            return false;
        });
        _out.WriteLine($"reached {reached.Count(r => r)} of {route.Count} patrol points");
        Assert.True(reached.Count(r => r) >= 2, "didn't get round its patrol");
        Assert.Equal(BotMode.Idle, bot.Mode);
    }

    [Fact]
    public void Under_fire_a_bot_takes_cover_and_shoots_back_from_it()
    {
        BotArena arena = BotArena.Create("normal");
        BotBrain bot = arena.AddBot("yard_containers", "sentry");
        arena.Start();
        arena.Sim.PlayerHits.Enabled = false; // nobody goes out: this is about what it does under fire
        Vector3 spot = arena.SpotInFront(bot, 20f);
        arena.PlaceHero(spot, bot.Self.Position);
        // You keep firing just wide of it: under fire, it should get into cover and shoot back from there.
        arena.HeroScript = arena.ShootAt(bot.Self, interval: 40, startTick: 20, miss: 1.6f);
        var phases = new HashSet<CoverPhase>();
        bool usedCover = false;
        arena.Run(15 * Second, () =>
        {
            phases.Add(bot.Phase);
            usedCover |= bot.CoverIndex >= 0;
            return !arena.Hero.Alive || !bot.Self.Alive;
        });
        _out.WriteLine($"phases {string.Join(", ", phases)}; bot shots {arena.ShotsBy(bot.Self.Id)}; you out={!arena.Hero.Alive}, bot out={!bot.Self.Alive}");
        Assert.True(usedCover, "never picked a cover point");
        Assert.Contains(CoverPhase.Hiding, phases);
        Assert.Contains(CoverPhase.Peeking, phases);
        Assert.True(arena.ShotsBy(bot.Self.Id) > 3, "hardly shot back");
    }

    [Fact]
    public void A_bot_refills_from_a_pod_when_its_loader_runs_low()
    {
        BotArena arena = BotArena.Create("normal");
        BotBrain bot = arena.AddBot("yard_east");
        arena.Start();
        arena.PlaceHero(new Vector3(60f, 0f, 45f), new Vector3(60f, 0f, 40f));
        bot.Self.Marker.Paint.Loader = 20; // what a long fight leaves; the pods are still full
        arena.Run(2 * Second);
        Assert.Contains(arena.Log, e => e.Type == SimEventType.RefillStarted && e.PlayerId == bot.Self.Id);
    }

    [Fact]
    public void A_rusher_pushes_toward_where_it_last_saw_you()
    {
        BotArena arena = BotArena.Create("normal");
        BotBrain bot = arena.AddBot("east_scrap", "rusher");
        arena.Start();
        Vector3 spot = arena.SpotInFront(bot, 25f);
        arena.PlaceHero(spot, bot.Self.Position);
        // Seen, then gone: you vanish (walk off the field) once it has spotted you.
        arena.Run(6 * Second, () => bot.Senses.For(0) is { Spotted: true });
        Assert.True(bot.Senses.For(0) is { Spotted: true }, "never spotted you");
        arena.Hero.Present = false;
        arena.Hero.Position = new Vector3(60f, 0f, 45f);
        float before = Vector3.Distance(bot.Self.Position, spot);
        arena.Run(6 * Second);
        float after = Vector3.Distance(bot.Self.Position, spot);
        _out.WriteLine($"rusher {before:0.0} → {after:0.0} m from where it saw you, now {bot.Label}");
        Assert.True(after < before - 8f, $"didn't push ({before:0.0} → {after:0.0} m)");
    }

    [Fact]
    public void Hard_bots_win_the_same_duel_faster_than_easy_ones()
    {
        float Duel(string tier)
        {
            BotArena arena = BotArena.Create(tier);
            BotBrain bot = arena.AddBot("yard_east");
            arena.Start();
            arena.PlaceHero(arena.SpotInFront(bot, 22f), bot.Self.Position + new Vector3(0f, 0f, 30f));
            arena.Run(40 * Second, () => !arena.Hero.Alive);
            return arena.Hero.Alive ? float.MaxValue : arena.Sim.Tick / (float)Second;
        }

        float hard = Duel("hard");
        float easy = Duel("easy");
        _out.WriteLine($"hard: {hard:0.00} s, easy: {easy:0.00} s");
        Assert.True(easy < 40f, "an easy bot never hit someone standing still in the open");
        Assert.True(hard < easy, $"hard took {hard:0.00} s, easy {easy:0.00} s");
    }

    [Fact]
    public void An_eliminated_bot_walks_off_the_field()
    {
        BotArena arena = BotArena.Create("normal");
        BotBrain bot = arena.AddBot("yard_east");
        arena.Start();
        bot.Passive = true;
        arena.PlaceHero(arena.SpotInFront(bot, 6f), bot.Self.Position);
        arena.HeroScript = arena.ShootAt(bot.Self, interval: 30);
        arena.Run(5 * Second, () => !bot.Self.Alive);
        Assert.False(bot.Self.Alive);
        Vector3 at = bot.Self.Position;
        arena.HeroScript = null;
        arena.Run(30 * Second, () => !bot.Self.Present);
        _out.WriteLine($"walked {Vector3.Distance(at, bot.Self.Position):0.0} m, present={bot.Self.Present}");
        Assert.False(bot.Self.Present);
    }

    [Fact]
    public void The_same_round_plays_out_the_same_way_twice()
    {
        List<(SimEventType, int, int)> Play()
        {
            BotArena arena = BotArena.Create("normal");
            BotBrain a = arena.AddBot("yard_east");
            arena.AddBot("yard_containers");
            arena.Start();
            arena.PlaceHero(arena.SpotInFront(a, 18f), a.Self.Position);
            arena.HeroScript = arena.ShootAt(a.Self, interval: 50, startTick: 60);
            arena.Run(10 * Second);
            return arena.Log.Select(e => (e.Type, e.Tick, e.PlayerId)).ToList();
        }

        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void A_full_squad_thinks_without_allocating_once_warm()
    {
        BotArena arena = BotArena.Create("hard");
        foreach (string spawn in new[] { "guardhouse", "yard_containers", "yard_east", "office_corridor", "office_up_south", "warehouse_bays", "warehouse_mezz_north", "pump_house", "east_scrap" })
        {
            arena.AddBot(spawn);
        }

        arena.Start();
        arena.PlaceHero(new Vector3(60f, 0f, 45f), new Vector3(60f, 0f, 40f));
        arena.Run(8 * Second); // warm up: paths planned, lists at size, JIT done
        long before = GC.GetAllocatedBytesForCurrentThread();
        arena.Run(4 * Second);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated == 0, $"{allocated} bytes allocated by 9 bots over 4 s");
    }

    [Fact]
    public void Tiers_must_name_a_bot_difficulty_and_spawns_a_behaviour()
    {
        var badTier = new EditedDataSource(TestData.Source).Edit("levels/ladder.jsonc", t => t.Replace("\"bots\": \"easy\"", "\"bots\": \"brutal\""));
        DataException tier = Assert.Throws<DataException>(() => GameData.Load(badTier));
        Assert.Contains("brutal", tier.Message);

        var badRole = new EditedDataSource(TestData.Source).Edit("levels/oxbarrow_works.jsonc",
            t => t.Replace("{ \"id\": \"guardhouse\", \"position_m\": [1.0, 0, 34.5], \"roles\": [\"sentry\"] }",
                "{ \"id\": \"guardhouse\", \"position_m\": [1.0, 0, 34.5], \"roles\": [\"lurker\"] }"));
        DataException role = Assert.Throws<DataException>(() => GameData.Load(badRole));
        Assert.Contains("lurker", role.Message);
    }

    /// <summary>A walkable spot <paramref name="min"/>–<paramref name="max"/> m from the bot with no line of sight to its eyes.</summary>
    private static Vector3 HiddenSpot(BotArena arena, BotBrain bot, float min, float max)
    {
        Vector3 eye = bot.Self.EyePosition;
        for (int ring = 0; ring < 6; ring++)
        {
            float d = min + (max - min) * ring / 5f;
            for (int k = 0; k < 24; k++)
            {
                float angle = k * MathF.Tau / 24f;
                Vector3 at = bot.Self.Position + new Vector3(MathF.Sin(angle), 0f, MathF.Cos(angle)) * d;
                if (arena.Squad.Grid.TrySnap(at, out Vector3 spot) && MathF.Abs(spot.Y - bot.Self.Position.Y) < 0.3f &&
                    arena.Sim.Collision.SweepSphere(spot + new Vector3(0f, 1.6f, 0f), eye, 0f, out _) &&
                    arena.Squad.Grid.FindPath(bot.Self.Position, spot, new List<Vector3>()))
                {
                    return spot;
                }
            }
        }

        throw new InvalidOperationException("no hidden spot near the bot");
    }
}
