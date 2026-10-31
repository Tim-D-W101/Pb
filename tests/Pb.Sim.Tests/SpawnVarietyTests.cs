using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Xunit;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>
/// After the play-test (the bots started in the same places every round and parked at one post): every round's starts
/// differ from the last's, roles aren't tied to places, the opponents can start anywhere in an area (inside the hospital
/// and the cold store too), and bots with nothing to do move between posts instead of standing at one all round.
/// </summary>
public class SpawnVarietyTests
{
    private const int Second = 120;

    private readonly ITestOutputHelper _out;

    public SpawnVarietyTests(ITestOutputHelper output)
    {
        _out = output;
    }

    public static IEnumerable<object[]> AreasAndModes()
    {
        foreach (string level in new[] { "oxbarrow_works", "rail_yard", "cold_store", "hospital_wing" })
        {
            yield return new object[] { level, "solo", 6 };
            yield return new object[] { level, "solo", 9 };
            yield return new object[] { level, "teams", 5 };
            yield return new object[] { level, "ffa", 10 };
        }
    }

    [Theory]
    [MemberData(nameof(AreasAndModes))]
    public void Each_round_starts_everyone_somewhere_else_than_the_last(string levelId, string modeId, int size)
    {
        (LevelLayout level, _, CoverSet cover) = BotArena.SharedFor(levelId);
        var sim = new SimWorld(TestData.Config);
        sim.LoadLevel(level);
        SpawnRules rules = TestData.Config.Rules.Spawning;
        GameMode mode = TestData.Config.Rules.FindMode(modeId)!;
        SpawnMemory? last = null;
        var spots = new HashSet<string>();
        for (int round = 0; round < 10; round++)
        {
            SpawnPlan plan = SpawnPlanner.Plan(level, cover, sim.Collision, rules, TestData.Data.Bots, RoundShape.Of(mode, size),
                TestData.Config.Movement.StandEyeHeight, (ulong)(1000 + round * 7919), last);
            foreach (OpponentSpawn o in plan.Opponents.Concat(plan.Teammates))
            {
                spots.Add(o.Id);
                if (last is not null)
                {
                    Vector3 nearest = last.Starts.MinBy(s => Flat(s - o.Position) + (MathF.Abs(s.Y - o.Position.Y) < 2.5f ? 0f : 1000f));
                    Assert.False(Flat(nearest - o.Position) < rules.RepeatRadius && MathF.Abs(nearest.Y - o.Position.Y) < 2.5f,
                        $"round {round}: {o.Id} at {o.Position} is {Flat(nearest - o.Position):0.0} m from a start of the round before");
                }
            }

            if (last?.YourEntry is { } entry && level.PlayerSpawns.Count > 1)
            {
                Assert.True(Vector3.Distance(entry, plan.You.Position) > 1f, $"round {round}: you came in where you did the round before");
            }

            last = SpawnMemory.Of(plan);
        }

        _out.WriteLine($"{levelId} {modeId} {size}: {spots.Count} different starts over 10 rounds");
    }

    [Theory]
    [InlineData("hospital_wing", -40f, 18f)]
    [InlineData("cold_store", -40f, 0f)]
    public void Opponents_can_start_inside_the_building_not_only_out_in_the_yard(string levelId, float minZ, float maxZ)
    {
        (LevelLayout level, _, CoverSet cover) = BotArena.SharedFor(levelId);
        var sim = new SimWorld(TestData.Config);
        sim.LoadLevel(level);
        int inside = 0, starts = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            SpawnPlan plan = SpawnPlanner.Plan(level, cover, sim.Collision, TestData.Config.Rules.Spawning, TestData.Data.Bots, 6,
                TestData.Config.Movement.StandEyeHeight, seed);
            starts += plan.Opponents.Count;
            inside += plan.Opponents.Count(o => o.Position.Z >= minZ && o.Position.Z <= maxZ);
        }

        _out.WriteLine($"{levelId}: {inside} of {starts} opponents started inside over 20 rounds");
        Assert.True(inside >= starts / 5, $"only {inside} of {starts} started inside");
    }

    [Fact]
    public void A_named_spawn_doesnt_always_play_the_same_role()
    {
        (LevelLayout level, _, CoverSet cover) = BotArena.SharedFor("oxbarrow_works");
        var sim = new SimWorld(TestData.Config);
        sim.LoadLevel(level);
        var roles = new Dictionary<string, HashSet<string>>();
        for (ulong seed = 1; seed <= 60; seed++)
        {
            SpawnPlan plan = SpawnPlanner.Plan(level, cover, sim.Collision, TestData.Config.Rules.Spawning, TestData.Data.Bots, 9,
                TestData.Config.Movement.StandEyeHeight, seed);
            foreach (OpponentSpawn o in plan.Opponents.Where(o => level.OpponentSpawns.Any(s => s.Id == o.Id)))
            {
                (roles.TryGetValue(o.Id, out HashSet<string>? seen) ? seen : roles[o.Id] = new HashSet<string>()).Add(o.Roles[0]);
            }
        }

        // The named spawns with a single role of their own: dealt others as well now.
        var single = level.OpponentSpawns.Where(s => s.Roles.Count == 1 && roles.ContainsKey(s.Id)).ToList();
        _out.WriteLine(string.Join("; ", single.Select(s => $"{s.Id} ({s.Roles[0]}): {string.Join(", ", roles[s.Id])}")));
        Assert.NotEmpty(single);
        Assert.True(single.Count(s => roles[s.Id].Count > 1) >= single.Count / 2, "most single-role spawns still always play their own role");
    }

    [Fact]
    public void A_sentry_with_nothing_going_on_moves_between_posts_near_its_start()
    {
        BotArena arena = BotArena.Create("normal", seed: 3);
        BotBrain bot = arena.AddBot("yard_east", "sentry");
        arena.Start();
        arena.PlaceHero(new Vector3(60f, 0f, 45f), new Vector3(60f, 0f, 40f)); // well out of the way
        ArchetypeParams sentry = arena.BotConfig.Archetypes["sentry"];
        var posts = new List<Vector3> { bot.Post };
        float furthest = 0f;
        arena.Run((int)(sentry.ShiftAfter * 4f) * Second, () =>
        {
            if (Flat(bot.Post - posts[^1]) > 0.5f)
            {
                posts.Add(bot.Post);
            }

            furthest = MathF.Max(furthest, Flat(bot.Self.Position - bot.Home));
            return false;
        });
        _out.WriteLine($"posts: {string.Join(" → ", posts.Select(p => $"({p.X:0.0},{p.Z:0.0})"))}; furthest from its start {furthest:0.0} m; mode {bot.Mode}");
        Assert.True(posts.Count >= 3, $"it held only {posts.Count} posts in {sentry.ShiftAfter * 4f:0} s");
        Assert.All(posts, p => Assert.True(Flat(p - bot.Home) <= sentry.ShiftWithin + 0.5f, $"a post {Flat(p - bot.Home):0.0} m from its start"));
        Assert.True(furthest > 3f, "it never walked anywhere");

        // It gets to the post it's making for (and stands it).
        Vector3 post = bot.Post;
        arena.Run(20 * Second, () => Flat(bot.Self.Position - post) < 1.2f);
        Assert.True(Flat(bot.Self.Position - post) < 1.2f, $"{Flat(bot.Self.Position - post):0.0} m short of its post after 20 s");
    }

    [Fact]
    public void A_marksman_moves_to_another_vantage_after_a_while()
    {
        BotArena arena = BotArena.Create("normal", seed: 5);
        BotBrain bot = arena.AddBot("office_up_south", "marksman");
        arena.Start();
        arena.PlaceHero(new Vector3(60f, 0f, 45f), new Vector3(60f, 0f, 40f));
        arena.Run(15 * Second); // settle at the first vantage
        Vector3 first = bot.Self.Position;
        ArchetypeParams marksman = arena.BotConfig.Archetypes["marksman"];
        float moved = 0f;
        arena.Run((int)(marksman.ShiftAfter * 2.2f) * Second, () =>
        {
            moved = MathF.Max(moved, Flat(bot.Self.Position - first));
            return false;
        });
        _out.WriteLine($"first vantage ({first.X:0.0},{first.Y:0.0},{first.Z:0.0}); moved up to {moved:0.0} m; now {bot.Label}");
        Assert.True(moved >= TestData.Data.Bots.Brain.RelocateDistance - 1f, $"it only moved {moved:0.0} m");
    }

    [Fact]
    public void Patrols_set_off_either_way_round()
    {
        // Oxbarrow Works' yard loop: from the containers the patrol makes for its south-west corner, then round one way
        // (east, along the south side) or the other (north-west). Fifteen seconds in, rounds differ by which.
        var where = new List<Vector3>();
        for (ulong seed = 1; seed <= 10; seed++)
        {
            BotArena arena = BotArena.Create("normal", seed);
            OpponentSpawn spawn = arena.Level.OpponentSpawns.First(s => s.Patrol is { Points.Count: > 2 } && s.Roles.Contains("patroller"));
            BotBrain bot = arena.AddBot(spawn.Id, "patroller");
            arena.Start();
            arena.PlaceHero(new Vector3(60f, 0f, 45f), new Vector3(60f, 0f, 40f));
            arena.Run(15 * Second);
            where.Add(bot.Self.Position);
        }

        _out.WriteLine($"after 15 s: {string.Join(" ", where.Select(p => $"({p.X:0},{p.Z:0})"))}");
        Assert.Contains(where, p => p.X > 4f);
        Assert.Contains(where, p => p.X < -2f);
    }

    [Theory]
    [InlineData("oxbarrow_works")]
    [InlineData("rail_yard")]
    [InlineData("cold_store")]
    [InlineData("hospital_wing")]
    public void Nobody_has_a_shot_at_you_the_moment_you_step_in(string levelId)
    {
        (LevelLayout level, _, CoverSet cover) = BotArena.SharedFor(levelId);
        var sim = new SimWorld(TestData.Config);
        sim.LoadLevel(level);
        SpawnRules rules = TestData.Config.Rules.Spawning;
        float eye = TestData.Config.Movement.StandEyeHeight;
        for (ulong seed = 1; seed <= 12; seed++)
        {
            SpawnPlan plan = SpawnPlanner.Plan(level, cover, sim.Collision, rules, TestData.Data.Bots, 9, eye, seed);
            Vector3 inFrom = plan.You.Position + Core.ViewAngles.FlatForward(plan.You.Yaw) * rules.EntryLookAhead;
            foreach (OpponentSpawn o in plan.Opponents)
            {
                bool seen = !sim.Collision.SweepSphere(inFrom + new Vector3(0f, eye, 0f), o.Position + new Vector3(0f, eye, 0f), 0.02f, out _);
                Assert.False(seen, $"seed {seed}: {o.Id} at {o.Position} sees {rules.EntryLookAhead} m in from your entry at {plan.You.Position}");
            }
        }
    }

    private static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);
}
