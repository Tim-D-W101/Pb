using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>Random starts on Oxbarrow Works (owner: "where the guys are placed mustn't be fixed").</summary>
public class SpawnPlannerTests
{
    private static readonly Lazy<(LevelLayout Level, CoverSet Cover, CollisionWorld World)> Built = new(() =>
    {
        LevelLayout level = TestData.Data.Levels["oxbarrow_works"];
        NavGrid grid = NavGrid.Build(level, TestData.Data.Bots.Navigation);
        var world = new CollisionWorld();
        level.BuildCollision(world);
        CoverSet cover = CoverSet.Build(level, grid, TestData.Config.Movement.StandEyeHeight, TestData.Config.Movement.CrouchEyeHeight);
        return (level, cover, world);
    });

    private readonly ITestOutputHelper _out;

    public SpawnPlannerTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static SpawnRules Rules => TestData.Config.Rules.Spawning;

    private static SpawnPlan Plan(int count, ulong seed)
    {
        (LevelLayout level, CoverSet cover, CollisionWorld world) = Built.Value;
        return SpawnPlanner.Plan(level, cover, world, Rules, TestData.Data.Bots, count, TestData.Config.Movement.StandEyeHeight, seed);
    }

    [Fact]
    public void The_same_seed_deals_the_same_starts()
    {
        SpawnPlan a = Plan(6, 42), b = Plan(6, 42);
        Assert.Equal(a.You, b.You);
        Assert.Equal(a.Opponents.Select(o => (o.Id, o.Position, o.Roles[0])), b.Opponents.Select(o => (o.Id, o.Position, o.Roles[0])));
    }

    [Fact]
    public void Every_round_starts_differently()
    {
        var layouts = new HashSet<string>();
        var entries = new HashSet<Vector3>();
        var places = new HashSet<string>();
        for (ulong seed = 1; seed <= 12; seed++)
        {
            SpawnPlan plan = Plan(6, seed);
            entries.Add(plan.You.Position);
            layouts.Add(string.Join(",", plan.Opponents.Select(o => o.Id).OrderBy(id => id)));
            places.UnionWith(plan.Opponents.Select(o => o.Id));
        }

        _out.WriteLine($"12 rounds: {layouts.Count} different line-ups, {places.Count} different start places, {entries.Count} entry points");
        Assert.Equal(12, layouts.Count);
        Assert.True(places.Count >= 40, $"only {places.Count} start places in 12 rounds");
        Assert.Equal(Built.Value.Level.PlayerSpawns.Count, entries.Count);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(9)]
    public void Opponents_start_away_from_you_out_of_sight_and_spread_out(int count)
    {
        (LevelLayout level, _, CollisionWorld world) = Built.Value;
        float eyeHeight = TestData.Config.Movement.StandEyeHeight;
        for (ulong seed = 100; seed < 110; seed++)
        {
            SpawnPlan plan = Plan(count, seed);
            Assert.Equal(count, plan.Opponents.Count);
            Vector3 eye = plan.You.Position + new Vector3(0f, eyeHeight, 0f);
            foreach (OpponentSpawn o in plan.Opponents)
            {
                Assert.True(Vector3.Distance(o.Position, plan.You.Position) >= Rules.MinDistanceFromYou, $"seed {seed}: {o.Id} starts too close");
                Assert.True(world.SweepSphere(eye, o.Position + new Vector3(0f, eyeHeight, 0f), 0.02f, out _), $"seed {seed}: you can see {o.Id}'s head");
                Assert.True(o.Position.X >= level.SpawnArea.Min.X && o.Position.X <= level.SpawnArea.Max.X &&
                            o.Position.Z >= level.SpawnArea.Min.Z && o.Position.Z <= level.SpawnArea.Max.Z, $"seed {seed}: {o.Id} is outside the compound");
            }

            float closest = plan.Opponents.SelectMany(a => plan.Opponents.Where(b => b != a).Select(b => Vector3.Distance(a.Position, b.Position))).DefaultIfEmpty(float.MaxValue).Min();
            Assert.True(closest >= Rules.MinSpacing, $"seed {seed}: two opponents start {closest:0.0} m apart");
        }
    }

    [Fact]
    public void Opponents_play_a_mix_of_roles_and_patrollers_have_routes()
    {
        var roles = new Dictionary<string, int>();
        int patrollers = 0, routed = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            foreach (OpponentSpawn o in Plan(6, seed).Opponents)
            {
                Assert.Single(o.Roles);
                roles[o.Roles[0]] = roles.GetValueOrDefault(o.Roles[0]) + 1;
                Assert.NotNull(TestData.Data.Bots.ArchetypeFor(o.Roles));
                if (o.Roles[0] == "patroller")
                {
                    patrollers++;
                    routed += o.Patrol is null ? 0 : 1;
                }
            }
        }

        _out.WriteLine($"roles over 20 rounds: {string.Join(", ", roles.Select(r => $"{r.Key} {r.Value}"))}; {routed} of {patrollers} patrollers have a route");
        Assert.True(roles.Count >= 3, "fewer than three different roles in 20 rounds");
        Assert.True(routed > patrollers / 2, $"only {routed} of {patrollers} patrollers have a route");
    }
}
