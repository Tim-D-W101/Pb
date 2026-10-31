using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Core;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Xunit;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>
/// The bots' path searches after the play-test's hitches (bot-brain ticks of 4–7 ms in free-for-all): a long search is
/// carried on over several ticks within a budget of places a tick, and only places a door can swing over ask whether a
/// door stands open there. Neither changes a path.
/// </summary>
[Collection(BotArenaCollection.Name)]
public class PathBudgetTests
{
    private const int Second = 120;

    private readonly ITestOutputHelper _out;

    public PathBudgetTests(ITestOutputHelper output)
    {
        _out = output;
    }

    [Fact]
    public void A_long_search_carried_on_over_ticks_keeps_to_its_budget_and_finds_the_same_path()
    {
        (LevelLayout level, NavGrid grid, _) = BotArena.SharedFor("rail_yard");
        var whole = new NavSearch(grid);
        var sliced = new NavSearch(grid);
        var rng = new Pcg32(5);
        int budget = TestData.Data.Bots.Navigation.SearchNodesPerTick;
        int longest = 0;
        for (int pair = 0; pair < 40; pair++)
        {
            Vector3 from = grid.PositionOf((int)(rng.NextUInt() % (uint)grid.SpanCount));
            Vector3 to = grid.PositionOf((int)(rng.NextUInt() % (uint)grid.SpanCount));
            var expected = new List<Vector3>();
            bool found = whole.FindPath(from, to, expected);

            var path = new List<Vector3>();
            PathStatus status = sliced.Begin(from, to, path);
            int ticks = 0;
            while (status == PathStatus.Running)
            {
                int left = budget;
                status = sliced.Continue(ref left, path);
                Assert.InRange(left, 0, budget);
                ticks++;
            }

            longest = Math.Max(longest, ticks);
            Assert.Equal(found, status == PathStatus.Found);
            Assert.Equal(expected, path);
        }

        _out.WriteLine($"the longest of 40 searches took {longest} ticks at {budget} places a tick");
        Assert.True(longest > 1, "no search needed more than one tick: the test proves nothing");
    }

    [Fact]
    public void Only_places_a_door_can_swing_over_ask_about_doors_and_no_path_changes()
    {
        // A grid of its own: the test turns the door mask off and on, which mustn't touch the grid other tests share.
        LevelLayout level = TestData.Data.Levels["hospital_wing"];
        NavGrid grid = NavGrid.Build(level, TestData.Data.Bots.Navigation);
        var sim = new SimWorld(TestData.Config);
        sim.LoadLevel(level);
        CoverSet cover = CoverSet.Build(level, grid, TestData.Config.Movement.StandEyeHeight, TestData.Config.Movement.CrouchEyeHeight);
        var squad = new BotSquad(sim, TestData.Data.Bots, grid, cover);
        for (int leaf = 0; leaf < sim.Doors.Count; leaf++)
        {
            sim.Doors.SetOpen(leaf, 1f); // every door standing wide open, its leaf out across the way
        }

        int asked = 0;
        Func<Vector3, bool> doors = squad.Paths.Blocked!;
        var counting = new NavSearch(grid) { Blocked = feet => { asked++; return doors(feet); } };
        int masked = grid.MayBlockCount;
        _out.WriteLine($"{sim.Doors.Count} door leaves; {masked} of {grid.SpanCount} places can be blocked");
        Assert.InRange(masked, 1, grid.SpanCount / 10);

        var rng = new Pcg32(9);
        var withMask = new List<List<Vector3>>();
        var pairs = new List<(Vector3, Vector3)>();
        for (int i = 0; i < 30; i++)
        {
            pairs.Add((grid.PositionOf((int)(rng.NextUInt() % (uint)grid.SpanCount)), grid.PositionOf((int)(rng.NextUInt() % (uint)grid.SpanCount))));
        }

        foreach ((Vector3 from, Vector3 to) in pairs)
        {
            var path = new List<Vector3>();
            counting.FindPath(from, to, path);
            withMask.Add(path);
        }

        int askedWithMask = asked;
        asked = 0;
        grid.LimitBlockedTo(null);
        for (int i = 0; i < pairs.Count; i++)
        {
            var path = new List<Vector3>();
            counting.FindPath(pairs[i].Item1, pairs[i].Item2, path);
            Assert.Equal(withMask[i], path);
        }

        _out.WriteLine($"doors asked about {askedWithMask} times with the mask, {asked} without");
        Assert.True(askedWithMask * 20 < asked, $"the mask only saved asking {asked - askedWithMask} of {asked} times");
    }

    [Fact]
    public void In_a_free_for_all_the_bots_never_search_more_than_the_budget_in_a_tick()
    {
        BotArena arena = BotArena.Create("hard", seed: 4, level: "hospital_wing");
        GameMode mode = TestData.Config.Rules.FindMode("ffa")!;
        SpawnPlan plan = SpawnPlanner.Plan(arena.Level, arena.Squad.Cover, arena.Sim.Collision, TestData.Config.Rules.Spawning, TestData.Data.Bots,
            RoundShape.Of(mode, 10), TestData.Config.Movement.StandEyeHeight, 4);
        arena.PlaceHero(plan.You.Position, plan.You.Position + ViewAngles.FlatForward(plan.You.Yaw));
        arena.HeroBot().RestlessAfter = mode.RestlessAfter;
        byte team = 1;
        foreach (OpponentSpawn o in plan.Opponents)
        {
            arena.AddBotAt(o, team++).RestlessAfter = mode.RestlessAfter;
        }

        arena.Start(timeLimit: 120f, mode: mode.Kind);
        int budget = TestData.Data.Bots.Navigation.SearchNodesPerTick;
        long most = 0, last = 0;
        arena.Run(60 * Second, () =>
        {
            most = Math.Max(most, arena.Squad.NodesSearched - last);
            last = arena.Squad.NodesSearched;
            return arena.Sim.Match!.Phase == MatchPhase.Ended;
        });
        _out.WriteLine($"{arena.Squad.SearchesDone} searches, {arena.Squad.NodesSearched} places in all, at most {most} in a tick");
        Assert.True(arena.Squad.SearchesDone > 10, "hardly any searching: the test proves nothing");
        Assert.InRange(most, 1, budget);
    }
}
