using System.Diagnostics;
using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Level;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>The bots' navigation grid, built over the shipped compound (Oxbarrow Works).</summary>
public class NavGridTests
{
    private static readonly Lazy<(LevelLayout Level, NavGrid Grid, double BuildMs)> Built = new(() =>
    {
        LevelLayout level = TestData.Data.Levels["oxbarrow_works"];
        var watch = Stopwatch.StartNew();
        NavGrid grid = NavGrid.Build(level, TestData.Data.Bots.Navigation);
        return (level, grid, watch.Elapsed.TotalMilliseconds);
    });

    private readonly ITestOutputHelper _out;

    public NavGridTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static LevelLayout Level => Built.Value.Level;

    private static NavGrid Grid => Built.Value.Grid;

    [Fact]
    public void The_compound_builds_into_a_connected_grid()
    {
        _out.WriteLine($"{Grid.Columns}×{Grid.Rows} columns, {Grid.SpanCount} spans ({Grid.LinkedSpanCount} linked), built in {Built.Value.BuildMs:0} ms");
        Assert.True(Grid.SpanCount > 50_000);
        Assert.True(Grid.LinkedSpanCount > Grid.SpanCount * 0.9);
        Assert.True(Grid.TrySnap(Level.PlayerSpawn, out Vector3 snapped));
        Assert.True(Vector3.Distance(snapped, Level.PlayerSpawn) < 1f);
    }

    [Fact]
    public void Every_opponent_spawn_patrol_point_and_pickup_can_be_reached_from_your_spawn()
    {
        var path = new List<Vector3>();
        var goals = new List<(string Name, Vector3 At)>();
        goals.AddRange(Level.OpponentSpawns.Select(s => ($"spawn {s.Id}", s.Position)));
        goals.AddRange(Level.Patrols.SelectMany(r => r.Points.Select((p, i) => ($"patrol {r.Id}[{i}]", p))));
        goals.AddRange(Level.Pickups.Select(p => ($"pickup {p.Id}", p.Position)));
        foreach ((string name, Vector3 at) in goals)
        {
            Assert.True(Grid.FindPath(Level.PlayerSpawn, at, path), $"no path to {name} at {at}");
            Vector3 end = path[^1];
            Assert.True(Vector3.Distance(end, at) < 1.5f, $"path to {name} ends {Vector3.Distance(end, at):0.00} m away, at {end}");
            float length = PathLength(Level.PlayerSpawn, path);
            float straight = Vector3.Distance(Level.PlayerSpawn, at);
            Assert.True(length < straight * 3f + 20f, $"path to {name} is {length:0} m for {straight:0} m straight");
        }
    }

    [Fact]
    public void Paths_climb_to_the_office_upstairs_and_the_mezzanine()
    {
        var path = new List<Vector3>();
        foreach (string id in new[] { "office_up_south", "office_up_east", "warehouse_mezz_north", "warehouse_mezz_west" })
        {
            OpponentSpawn spawn = Level.OpponentSpawns.First(s => s.Id == id);
            var watch = Stopwatch.StartNew();
            Assert.True(Grid.FindPath(Level.PlayerSpawn, spawn.Position, path), $"no path up to {id}");
            double ms = watch.Elapsed.TotalMilliseconds;
            Assert.InRange(path[^1].Y, spawn.Position.Y - 0.3f, spawn.Position.Y + 0.3f);
            _out.WriteLine($"{id}: {path.Count} waypoints, {PathLength(Level.PlayerSpawn, path):0} m, {Grid.LastSearchExpanded} spans searched in {ms:0.0} ms");
        }
    }

    [Fact]
    public void Paths_never_go_through_walls()
    {
        // Chest-high lines between consecutive waypoints, thinner than a body, must be clear of paint geometry.
        var world = new CollisionWorld();
        Level.BuildCollision(world);
        var path = new List<Vector3>();
        foreach (OpponentSpawn spawn in Level.OpponentSpawns)
        {
            Assert.True(Grid.FindPath(Level.PlayerSpawn, spawn.Position, path));
            Vector3 from = Level.PlayerSpawn;
            foreach (Vector3 to in path)
            {
                var lift = new Vector3(0f, 1.0f, 0f);
                bool blocked = world.SweepSphere(from + lift, to + lift, 0.12f, out SweepHit hit);
                Assert.False(blocked, $"path to {spawn.Id}: {from} → {to} crosses {(blocked ? world.Colliders[hit.ColliderId].Name : "")}");
                from = to;
            }
        }
    }

    [Fact]
    public void The_narrowest_door_is_open_to_bots()
    {
        // The guardhouse is closed except for its 0.9 m door, and a spawn stands inside it.
        OpponentSpawn inside = Level.OpponentSpawns.First(s => s.Id == "guardhouse");
        var path = new List<Vector3>();
        Assert.True(Grid.FindPath(Level.PlayerSpawn, inside.Position, path));
        Assert.True(Vector3.Distance(path[^1], inside.Position) < 1f);
    }

    [Fact]
    public void Building_and_searching_are_deterministic()
    {
        NavGrid again = NavGrid.Build(Level, TestData.Data.Bots.Navigation);
        Assert.Equal(Grid.SpanCount, again.SpanCount);
        var a = new List<Vector3>();
        var b = new List<Vector3>();
        Vector3 goal = Level.OpponentSpawns.First(s => s.Id == "warehouse_mezz_north").Position;
        Assert.True(Grid.FindPath(Level.PlayerSpawn, goal, a));
        Assert.True(again.FindPath(Level.PlayerSpawn, goal, b));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Searches_do_not_allocate_once_warm()
    {
        var path = new List<Vector3>(256);
        Vector3 goal = Level.OpponentSpawns.First(s => s.Id == "pump_house").Position;
        Grid.FindPath(Level.PlayerSpawn, goal, path); // warm up: lists reach their size
        Assert.Equal(0, Allocations.During(() =>
        {
            for (int i = 0; i < 5; i++)
            {
                Grid.FindPath(Level.PlayerSpawn, goal, path);
            }
        }));
    }

    private static float PathLength(Vector3 start, List<Vector3> path)
    {
        float length = 0f;
        Vector3 from = start;
        foreach (Vector3 to in path)
        {
            length += Vector3.Distance(from, to);
            from = to;
        }

        return length;
    }
}
