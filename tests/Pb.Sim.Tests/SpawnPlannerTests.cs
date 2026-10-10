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

    private static GameMode Mode(string id) => TestData.Config.Rules.FindMode(id)!;

    private static SpawnPlan Plan(GameMode mode, int size, ulong seed)
    {
        (LevelLayout level, CoverSet cover, CollisionWorld world) = Built.Value;
        return SpawnPlanner.Plan(level, cover, world, Rules, TestData.Data.Bots, RoundShape.Of(mode, size), TestData.Config.Movement.StandEyeHeight, seed);
    }

    /// <summary>Whether someone standing at <paramref name="a"/> can see the head or chest of someone standing at <paramref name="b"/>.</summary>
    private static bool Sees(Vector3 a, Vector3 b)
    {
        float eye = TestData.Config.Movement.StandEyeHeight;
        CollisionWorld world = Built.Value.World;
        Vector3 from = a + new Vector3(0f, eye, 0f);
        return !world.SweepSphere(from, b + new Vector3(0f, eye, 0f), 0.02f, out _) || !world.SweepSphere(from, b + new Vector3(0f, eye * 0.6f, 0f), 0.02f, out _);
    }

    private static float ClosestPair(IReadOnlyList<OpponentSpawn> starts) =>
        starts.SelectMany((a, i) => starts.Skip(i + 1).Select(b => Vector3.Distance(a.Position, b.Position))).DefaultIfEmpty(float.MaxValue).Min();

    [Fact]
    public void Every_mode_deals_starts_for_everyone_at_every_size()
    {
        foreach (GameMode mode in TestData.Config.Rules.Modes)
        {
            foreach (int size in mode.Sizes)
            {
                SpawnPlan plan = Plan(mode, size, (ulong)(size * 31 + mode.Id.Length));
                Assert.Equal(mode.TeammatesFor(size), plan.Teammates.Count);
                Assert.Equal(mode.OpponentsFor(size), plan.Opponents.Count);
                Assert.Equal(mode.PlayersFor(size), 1 + plan.Teammates.Count + plan.Opponents.Count);
                Assert.True(mode.PlayersFor(size) <= TestData.Config.Rules.MaxPlayers);
                var ids = plan.Teammates.Concat(plan.Opponents).Select(o => o.Id).ToList();
                Assert.Equal(ids.Count, ids.Distinct().Count());
                if (mode.Roles.Count > 0)
                {
                    Assert.All(plan.Teammates.Concat(plan.Opponents), o => Assert.Contains(o.Roles[0], mode.Roles.Select(r => r.Role)));
                }
            }
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(10)]
    public void Free_for_all_starts_everyone_apart_and_out_of_each_others_sight(int size)
    {
        int inSight = 0, rounds = 0;
        float closest = float.MaxValue;
        for (ulong seed = 200; seed < 210; seed++, rounds++)
        {
            SpawnPlan plan = Plan(Mode("ffa"), size, seed);
            foreach (OpponentSpawn o in plan.Opponents)
            {
                Assert.True(Vector3.Distance(o.Position, plan.You.Position) >= Rules.MinDistanceFromYou, $"seed {seed}: {o.Id} starts too close to you");
                Assert.False(Sees(plan.You.Position, o.Position), $"seed {seed}: you can see {o.Id}");
            }

            closest = MathF.Min(closest, ClosestPair(plan.Opponents));
            for (int i = 0; i < plan.Opponents.Count; i++)
            {
                for (int j = i + 1; j < plan.Opponents.Count; j++)
                {
                    inSight += Sees(plan.Opponents[i].Position, plan.Opponents[j].Position) ? 1 : 0;
                }
            }
        }

        _out.WriteLine($"free-for-all of {size}: closest pair {closest:0.0} m, {inSight} pairs in sight over {rounds} rounds");
        Assert.True(closest >= Rules.MinSpacing, $"two players start {closest:0.0} m apart");
        Assert.True(inSight <= rounds, $"{inSight} pairs in sight of each other over {rounds} rounds");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Teams_start_together_on_opposite_sides(int size)
    {
        float farthestMate = 0f, nearestEnemy = float.MaxValue, spread = 0f;
        for (ulong seed = 300; seed < 310; seed++)
        {
            SpawnPlan plan = Plan(Mode("teams"), size, seed);
            var ours = plan.Teammates.Select(m => m.Position).Append(plan.You.Position).ToList();
            foreach (OpponentSpawn mate in plan.Teammates)
            {
                float d = Vector3.Distance(mate.Position, plan.You.Position);
                farthestMate = MathF.Max(farthestMate, d);
                Assert.True(d >= Rules.TeammateSpacing, $"seed {seed}: {mate.Id} starts on top of you");
                Assert.Equal(plan.You.Yaw, mate.Yaw);
            }

            Assert.True(ClosestPair(plan.Teammates) >= Rules.TeammateSpacing, $"seed {seed}: two teammates start on top of each other");
            foreach (OpponentSpawn o in plan.Opponents)
            {
                nearestEnemy = MathF.Min(nearestEnemy, ours.Min(p => Vector3.Distance(p, o.Position)));
                Assert.True(Vector3.Distance(o.Position, plan.You.Position) >= Rules.MinDistanceFromYou, $"seed {seed}: {o.Id} starts too close to you");
                Assert.DoesNotContain(ours, p => Sees(p, o.Position));
            }

            Vector3 centre = plan.Opponents.Aggregate(Vector3.Zero, (a, o) => a + o.Position) / plan.Opponents.Count;
            spread = MathF.Max(spread, plan.Opponents.Max(o => Vector3.Distance(o.Position, centre)));
        }

        _out.WriteLine($"{size} v {size}: teammates within {farthestMate:0.0} m of you, nearest opponent {nearestEnemy:0.0} m from your team, " +
                       $"the other team within {spread:0.0} m of its centre");
        Assert.True(farthestMate <= Rules.TeammatesWithin * 1.5f * 1.5f, $"a teammate starts {farthestMate:0.0} m from you");
        Assert.True(spread <= Rules.TeamSpread * 1.5f, $"the other team is spread {spread:0.0} m round its centre");
    }

    [Fact]
    public void In_capture_the_flag_the_other_teams_base_is_on_the_ground_on_the_far_side_of_every_compound()
    {
        GameMode flag = Mode("flag");
        foreach (string id in TestData.Data.Areas.Areas.Select(a => a.Id).Where(id => TestData.Data.Levels[id].Field is null))
        {
            LevelLayout level = TestData.Data.Levels[id];
            NavGrid grid = NavGrid.Build(level, TestData.Data.Bots.Navigation);
            var world = new CollisionWorld();
            level.BuildCollision(world);
            CoverSet cover = CoverSet.Build(level, grid, TestData.Config.Movement.StandEyeHeight, TestData.Config.Movement.CrouchEyeHeight);
            float nearest = float.MaxValue;
            for (ulong seed = 500; seed < 508; seed++)
            {
                SpawnPlan plan = SpawnPlanner.Plan(level, cover, world, Rules, TestData.Data.Bots, RoundShape.Of(flag, 4), TestData.Config.Movement.StandEyeHeight, seed);
                Vector3 you = plan.You.Position, theirs = plan.Opponents[0].Position;
                Assert.True(MathF.Abs(theirs.Y - you.Y) <= Rules.BaseHeight, $"{id} seed {seed}: their base {theirs} isn't on the ground");
                Assert.True(grid.TrySnap(theirs, out Vector3 onGrid) && MathF.Abs(onGrid.Y - theirs.Y) < 0.3f, $"{id} seed {seed}: nobody could walk to {theirs}");
                Assert.Equal(4, plan.Opponents.Count);
                nearest = MathF.Min(nearest, Vector3.Distance(you, theirs));
            }

            _out.WriteLine($"{id}: the bases at least {nearest:0} m apart");
            Assert.True(nearest >= Rules.MinDistanceFromYou, $"{id}: the bases only {nearest:0} m apart");
        }
    }

    [Theory]
    [InlineData(2, 6)]
    [InlineData(4, 6)]
    [InlineData(5, 4)]
    public void Co_op_starts_everyone_who_joined_together_at_the_entries(int people, int opponents)
    {
        (LevelLayout level, CoverSet cover, CollisionWorld world) = Built.Value;
        for (ulong seed = 400; seed < 406; seed++)
        {
            SpawnPlan plan = SpawnPlanner.Plan(level, cover, world, Rules, TestData.Data.Bots, RoundShape.Of(Mode("solo"), opponents, people),
                TestData.Config.Movement.StandEyeHeight, seed);
            Assert.Equal(people - 1, plan.Teammates.Count);
            Assert.Equal(opponents, plan.Opponents.Count);
            var ours = plan.Teammates.Select(m => m.Position).Append(plan.You.Position).ToList();
            foreach (OpponentSpawn mate in plan.Teammates)
            {
                float d = Vector3.Distance(mate.Position, plan.You.Position);
                Assert.True(d >= Rules.TeammateSpacing && d <= Rules.TeammatesWithin * 1.5f * 1.5f, $"seed {seed}: {mate.Id} starts {d:0.0} m from the first");
            }

            foreach (OpponentSpawn o in plan.Opponents)
            {
                Assert.DoesNotContain(ours, p => Sees(p, o.Position));
            }
        }

        // One person is the solo round as before.
        Assert.Equal(RoundShape.Of(Mode("solo"), opponents), RoundShape.Of(Mode("solo"), opponents, 1));
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
