using System.Diagnostics;
using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Core;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Data;
using Pb.Sim.Match;
using Pb.Sim.Players;
using Pb.Sim.Vehicles;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>Thornwick Woods (M5.4): the big woodsball site loads, grows its woods, and bots can find their way over it.</summary>
[Collection(BotArenaCollection.Name)]
public class ThornwickWoodsTests
{
    private readonly ITestOutputHelper _out;

    public ThornwickWoodsTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static LevelLayout Level => TestData.Data.Levels["thornwick_woods"];

    [Fact]
    public void It_is_several_times_the_works_with_woods_tracks_and_buggies()
    {
        LevelLayout level = Level, works = TestData.Data.Levels["oxbarrow_works"];
        float Area(LevelLayout l) => (l.Bounds.Max.X - l.Bounds.Min.X) * (l.Bounds.Max.Z - l.Bounds.Min.Z);
        _out.WriteLine($"{Area(level) / Area(works):0.0}× the works; {level.Trees.Count} trees, {level.Roads.Count} tracks, " +
                       $"{level.VehicleSpots.Count} buggies, {level.Primitives.Count} primitives");
        Assert.True(Area(level) >= 5f * Area(works), "it isn't several times the works");
        Assert.InRange(level.Trees.Count, 300, 4000);
        Assert.True(level.Roads.Count >= 5);
        Assert.True(level.VehicleSpots.Count >= 4);
    }

    [Fact]
    public void A_bot_going_a_long_way_drives_a_buggy_up_the_lane_and_walks_the_rest()
    {
        BotArena arena = BotArena.Create("normal", level: "thornwick_woods");
        BotBrain bot = arena.AddBotAt(arena.Level.OpponentSpawns.First(s => s.Id == "farmyard"), team: 0, archetype: "sentry");
        arena.AddBotAt(arena.Level.OpponentSpawns.First(s => s.Id == "scrapyard"), team: 1).Passive = true;
        arena.Hero.Position = new Vector3(-150f, 0f, 110f); // you, out of the way in the far corner
        arena.Start(mode: MatchModeKind.Teams);
        var goal = new Vector3(6f, 0f, -104f); // by the north lane, up by the north gate
        bot.Guard(goal, 0f, shout: false);
        float fastest = 0f, drove = 0f;
        int satAt = -1, outAt = -1;
        Vector3 last = bot.Self.Position;
        arena.Run(120 * 120, () =>
        {
            if (arena.Sim.Vehicles.Of(bot.Self) is { } v)
            {
                satAt = satAt < 0 ? arena.Sim.Tick : satAt;
                fastest = MathF.Max(fastest, v.Speed);
                drove += Flat(bot.Self.Position - last);
            }
            else if (satAt >= 0 && outAt < 0)
            {
                outAt = arena.Sim.Tick;
            }

            last = bot.Self.Position;
            if (arena.Sim.Tick % 240 == 0)
            {
                _out.WriteLine($"  {arena.Sim.Tick / 120f:0} s: at {bot.Self.Position}, {bot.Ride}, in {bot.Self.Vehicle}, " +
                               $"{(arena.Sim.Vehicles.Of(bot.Self) is { } car ? $"{car.Speed:0.0} m/s yaw {car.Yaw * 57.3f:0}" : "on foot")}, {bot.Label}");
            }

            return outAt >= 0 && Flat(bot.Self.Position - goal) < 3f;
        });
        float seconds = arena.Sim.Tick / 120f;
        _out.WriteLine($"in at {satAt / 120f:0.0} s, out at {outAt / 120f:0.0} s after driving {drove:0} m (fastest {fastest:0.0} m/s); " +
                       $"{Flat(bot.Self.Position - goal):0.0} m from the goal after {seconds:0} s ({bot.Ride}, {bot.Label})");
        Assert.True(satAt >= 0, "it never got in a buggy");
        Assert.True(drove > 120f, $"it drove only {drove:0} m");
        Assert.True(outAt > satAt, "it never got out");
        Assert.True(fastest > 5f, $"it never got going ({fastest:0.0} m/s)");
        Assert.True(Flat(bot.Self.Position - goal) < 3f, "it didn't get where it was going");
        Assert.True(seconds < 70f, $"it took {seconds:0} s: walking would've been as quick");
    }

    [Fact]
    public void A_driving_bot_shoots_from_the_wheel_and_drives_on()
    {
        BotArena arena = BotArena.Create("normal", level: "thornwick_woods");
        BotBrain bot = arena.AddBotAt(arena.Level.OpponentSpawns.First(s => s.Id == "farmyard"), team: 0, archetype: "sentry");
        // An enemy standing out in the farm, well off the lane: in sight as the buggy goes by, too far to get out for.
        BotBrain enemy = arena.AddBotAt(arena.Level.OpponentSpawns.First(s => s.Id == "scrapyard"), team: 1);
        enemy.Passive = true;
        enemy.Self.Position = new Vector3(24f, 0f, -24f);
        arena.AddBotAt(arena.Level.OpponentSpawns.First(s => s.Id == "hazel_copse"), team: 1).Passive = true; // keeps the round going
        arena.Hero.Position = new Vector3(-150f, 0f, 110f);
        arena.Start(mode: MatchModeKind.Teams);
        var goal = new Vector3(6f, 0f, -104f);
        bot.Guard(goal, 0f, shout: false);
        int fromTheWheel = 0, seen = 0;
        float drove = 0f;
        Vector3 last = bot.Self.Position;
        arena.Run(90 * 120, () =>
        {
            bool driving = arena.Sim.Vehicles.Of(bot.Self) is { } v && v.Driver == bot.Self.Id;
            if (driving)
            {
                drove += Flat(bot.Self.Position - last);
            }

            for (; seen < arena.Log.Count; seen++)
            {
                fromTheWheel += driving && arena.Log[seen].Type == SimEventType.ShotFired && arena.Log[seen].PlayerId == bot.Self.Id ? 1 : 0;
            }

            last = bot.Self.Position;
            return bot.Self.Vehicle < 0 && drove > 150f;
        });

        _out.WriteLine($"drove {drove:0} m, {fromTheWheel} shots from the wheel; the enemy is {(enemy.Self.Alive ? "still in" : "out")}");
        Assert.True(fromTheWheel > 0, "it never fired from the wheel");
        Assert.True(drove > 150f, $"it stopped driving after {drove:0} m");
    }

    [Fact]
    public void The_tracks_join_up()
    {
        RoadGraph roads = RoadGraph.Build(Level.Roads);
        var route = new List<Vector3>();
        _out.WriteLine($"{roads.NodeCount} junctions and bends, {roads.SegmentCount} stretches");
        foreach (Vector3 to in new[] { new Vector3(-160f, 0f, 60f), new Vector3(118f, 0f, 20f), new Vector3(0f, 0f, -118f), new Vector3(-10f, 0f, 68f) })
        {
            Assert.True(roads.Route(new Vector3(0f, 0f, 118f), to, route, out float length), $"no way by the tracks to {to}");
            _out.WriteLine($"  to {to}: {length:0} m by {route.Count} points");
        }
    }

    private static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

    [Fact]
    public void Its_grid_builds_quickly()
    {
        var watch = Stopwatch.StartNew();
        NavGrid grid = NavGrid.Build(Level, TestData.Data.Bots.Navigation);
        double built = watch.Elapsed.TotalSeconds;
        watch.Restart();
        CoverSet cover = CoverSet.Build(Level, grid, TestData.Config.Movement.StandEyeHeight, TestData.Config.Movement.CrouchEyeHeight);
        _out.WriteLine($"grid: {grid.SpanCount} places in {built:0.00} s; cover: {cover.Points.Count} points in {watch.Elapsed.TotalSeconds:0.00} s");
    }

    [Fact]
    public void Bots_find_their_way_across_it_quickly()
    {
        var watch = Stopwatch.StartNew();
        (LevelLayout level, NavGrid grid, _) = BotArena.SharedFor("thornwick_woods");
        double built = watch.Elapsed.TotalSeconds;
        var search = new NavSearch(grid);
        var path = new List<Vector3>();
        var from = new Vector3(0f, 0f, 118f);
        (Vector3 To, string Name)[] goals =
        {
            (new Vector3(-104f, 0f, -72f), "Blackthorn glade"), (new Vector3(30f, 0f, -80f), "the fort"),
            (new Vector3(130f, 0f, 64f), "the scrapyard"), (new Vector3(-136f, 0f, 48f), "the camp"),
        };
        foreach ((Vector3 to, string name) in goals)
        {
            watch.Restart();
            Assert.True(search.FindPath(from, to, path), $"no way from the south gate to {name}");
            float length = Vector3.Distance(from, path[0]);
            _out.WriteLine($"  {path.Count} waypoints, {path[0]} … {path[^1]}");
            for (int i = 1; i < path.Count; i++)
            {
                length += Vector3.Distance(path[i - 1], path[i]);
            }

            _out.WriteLine($"to {name}: {length:0} m ({Vector3.Distance(from, to):0} m straight), found in {watch.Elapsed.TotalMilliseconds:0.0} ms");
            Assert.True(length < Vector3.Distance(from, to) * 1.6f, $"the way to {name} goes a long way round");
        }

        _out.WriteLine($"grid: {grid.SpanCount} places, built (with the cover points) in {built:0.0} s");
    }
}
