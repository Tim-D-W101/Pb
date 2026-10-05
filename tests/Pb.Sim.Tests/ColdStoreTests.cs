using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Level;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>The Cold Store (M3.6): its raised floor and the trailers up onto it, its dark chambers and heavy doors, its racking.</summary>
[Collection(BotArenaCollection.Name)]
public class ColdStoreTests
{
    private const float Ball = 0.0085f;

    private static readonly Lazy<CollisionWorld> Paint = new(() =>
    {
        var world = new CollisionWorld();
        Level.BuildCollision(world);
        return world;
    });

    private readonly ITestOutputHelper _out;

    public ColdStoreTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static LevelLayout Level => BotArena.SharedFor("cold_store").Level;

    private static NavGrid Grid => BotArena.SharedFor("cold_store").Grid;

    private static Vector3 Up(float y) => new(0f, y, 0f);

    private static float WalkLength(Vector3 from, Vector3 to)
    {
        var path = new List<Vector3>();
        Assert.True(Grid.FindPath(from, to, path), $"no path from {from} to {to}");
        Assert.True(Vector3.Distance(path[^1], to) < 1f, $"the path from {from} to {to} ends at {path[^1]}");
        float length = 0f;
        foreach (Vector3 p in path)
        {
            length += Vector3.Distance(from, p);
            from = p;
        }

        return length;
    }

    [Fact]
    public void A_trailer_is_a_tunnel_from_the_yard_up_onto_the_dock()
    {
        // The trailer backed onto dock 2 (x −18): from the yard at its nose, up its ramp, through it and into the dock hall.
        float length = WalkLength(new Vector3(-18f, 0f, 18f), new Vector3(-18f, 1.2f, -4f));
        _out.WriteLine($"through the trailer: {length:0.0} m");
        Assert.True(length < 26f, $"{length:0} m: the way in isn't through the trailer");
        int inside = Grid.SpanAt(new Vector3(-18f, 1.2f, 7f));
        Assert.NotEqual(-1, inside);
        Assert.Equal(1.2f, Grid.PositionOf(inside).Y, 0.05f);
    }

    [Fact]
    public void The_dock_is_a_lorrys_height_up_and_nobody_climbs_its_face()
    {
        // Dock 4 (x 6) has no trailer: from the yard below it to just inside it is the long way round.
        float length = WalkLength(new Vector3(6f, 0f, 3f), new Vector3(6f, 1.2f, -2f));
        _out.WriteLine($"round to the inside of dock 4: {length:0.0} m");
        Assert.True(length > 15f, $"only {length:0.0} m: something climbs the dock face");
        foreach (Vector3 p in new[] { new Vector3(-30f, 1.2f, -5f), new Vector3(0f, 1.2f, -22f), new Vector3(10f, 1.2f, -31f) })
        {
            int span = Grid.SpanAt(p);
            Assert.NotEqual(-1, span);
            Assert.Equal(1.2f, Grid.PositionOf(span).Y, 0.05f);
        }
    }

    [Fact]
    public void The_chambers_are_dark_and_shut_off_by_heavy_doors()
    {
        string[] rooms =
        {
            "the first chamber", "the second chamber", "the third chamber", "the fourth chamber", "the fifth chamber",
            "the sixth chamber", "the west freezer", "the east freezer",
        };
        foreach (string name in rooms)
        {
            AreaSpec area = Level.Areas.Single(a => a.Name == name);
            Assert.True(area.Indoor && area.Light <= 0.15f, $"{name} isn't dark");
            Assert.Contains(Level.Doors, d => d.Kind.Id == "cold_room" &&
                (area.Box.Contains(d.ShutCenter + d.Side * 0.6f + Up(0.2f)) || area.Box.Contains(d.ShutCenter - d.Side * 0.6f + Up(0.2f))));
        }

        int heavy = Level.Doors.Count(d => d.Kind.Id == "cold_room");
        _out.WriteLine($"{Level.Doors.Count} doors, {heavy} of them heavy, {Level.Doors.Count(d => d.Sliding)} sliding");
        Assert.True(heavy >= 11);
        Assert.Contains(Level.Doors, d => d.Kind.Id == "cold_room" && d.Sliding);
    }

    [Fact]
    public void Paint_flies_under_a_trailer_but_nobody_walks_under_it()
    {
        // Across the trailer at dock 3 (x −6), between its axles and its legs: under the floor a ball flies through,
        // above it the side stops it, and nobody can stand under it.
        Assert.False(Paint.Value.SweepSphere(new Vector3(-9f, 0.6f, 9f), new Vector3(-3f, 0.6f, 9f), Ball, out SweepHit under),
            $"under the trailer the ball hit {Paint.Value.Colliders[under.ColliderId].Name}");
        Assert.True(Paint.Value.SweepSphere(new Vector3(-9f, 2.0f, 9f), new Vector3(-3f, 2.0f, 9f), Ball, out _));
        Assert.Equal(-1, Grid.SpanAt(new Vector3(-6f, 0.1f, 9f)));
    }

    [Fact]
    public void Racking_hides_you_crouched_but_not_standing()
    {
        // An open-shelved bay in the first chamber (world (−36.8, −33.65)), looked through across its depth: at a standing
        // eye the empty middle shelf lets sight and paint through; at a crouched eye the bottom load is in the way.
        const float floor = 1.2f;
        var a = new Vector3(-39.5f, 0f, -33.65f);
        var b = new Vector3(-34.0f, 0f, -33.65f);
        float stand = TestData.Config.Movement.StandEyeHeight, crouch = TestData.Config.Movement.CrouchEyeHeight;
        Assert.False(Paint.Value.SweepSphere(a + Up(floor + stand), b + Up(floor + stand), Ball, out SweepHit seen),
            $"standing, the line hits {Paint.Value.Colliders[seen.ColliderId].Name} at {seen.Point}");
        Assert.True(Paint.Value.SweepSphere(a + Up(floor + crouch), b + Up(floor + crouch), Ball, out _));
    }

    [Fact]
    public void Flankers_work_the_store_and_a_marksman_watches_from_the_office()
    {
        Assert.True(Level.OpponentSpawns.Count(s => s.Roles.Contains("flanker")) >= 4);
        OpponentSpawn marksman = Level.OpponentSpawns.Single(s => s.Roles.Contains("marksman"));
        Assert.Equal("office_up", marksman.Id);
        Assert.True(marksman.Position.Y > 3f);
    }
}
