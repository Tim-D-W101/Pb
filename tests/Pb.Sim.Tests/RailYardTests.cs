using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Level;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>The Rail Yard (M3.5): its tracks and wagons, its high places and the ways up to them.</summary>
[Collection(BotArenaCollection.Name)]
public class RailYardTests
{
    private static readonly Lazy<CollisionWorld> Paint = new(() =>
    {
        var world = new CollisionWorld();
        Level.BuildCollision(world);
        return world;
    });

    private readonly ITestOutputHelper _out;

    public RailYardTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static LevelLayout Level => BotArena.SharedFor("rail_yard").Level;

    private static NavGrid Grid => BotArena.SharedFor("rail_yard").Grid;

    private static float PathLength(Vector3 from, Vector3 to)
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
    public void Tracks_are_rails_paint_hits_and_feet_step_over()
    {
        Assert.Equal(5, Level.Tracks.Count);
        LevelPrimitive[] rails = Level.Primitives.Where(p => p.Role == PrimitiveRole.Rail).ToArray();
        Assert.NotEmpty(rails);
        Assert.All(rails, r => Assert.True(r.Has(PrimitiveFlags.Paint) && !r.Has(PrimitiveFlags.Walk)));

        // A low shot across a bare stretch of track 3 (z −8) hits a rail; a shot a little higher clears both.
        Assert.True(Paint.Value.SweepSphere(new Vector3(-3f, 0.18f, -5f), new Vector3(-3f, 0.18f, -11f), 0.0085f, out SweepHit low));
        Assert.Equal(-8f + 0.7525f, low.Point.Z, 0.05f);
        Assert.False(Paint.Value.SweepSphere(new Vector3(-3f, 0.4f, -5f), new Vector3(-3f, 0.4f, -11f), 0.0085f, out _));
    }

    [Fact]
    public void Paint_flies_under_a_wagon_between_its_wheels_but_nobody_walks_under_it()
    {
        // The covered van on track 1 at x 7: its body and frame stop a shot at chest height; under the frame, between
        // its wheelsets, a ball flies through to the far side. Nobody can stand under it.
        var across = (float y) => (new Vector3(7f, y, -20.5f), new Vector3(7f, y, -27.5f));
        (Vector3 a, Vector3 b) = across(1.5f);
        Assert.True(Paint.Value.SweepSphere(a, b, 0.0085f, out SweepHit body));
        _out.WriteLine($"at 1.5 m the ball stops on {Paint.Value.Colliders[body.ColliderId].Name} at {body.Point}");
        (a, b) = across(0.5f);
        Assert.False(Paint.Value.SweepSphere(a, b, 0.0085f, out SweepHit under), $"at 0.5 m the ball hit {Paint.Value.Colliders[under.ColliderId].Name}");
        Assert.Equal(-1, Grid.SpanAt(new Vector3(7f, 0.1f, -24f)));
        Assert.Equal(-1, Grid.SpanAt(new Vector3(9f, 0.1f, -24f)));
    }

    [Fact]
    public void Marksmen_start_high_up_and_the_stairs_reach_them()
    {
        OpponentSpawn[] high = Level.OpponentSpawns.Where(s => s.Roles.Contains("marksman")).ToArray();
        Assert.Equal(new[] { "signal_box_up", "footbridge_deck", "shed_gantry" }, high.Select(s => s.Id));
        var path = new List<Vector3>();
        foreach (OpponentSpawn spawn in high)
        {
            Assert.True(spawn.Position.Y >= 3f, $"{spawn.Id} is only {spawn.Position.Y} m up");
            Assert.True(Grid.FindPath(Level.PlayerSpawn, spawn.Position, path), $"no path up to {spawn.Id}");
            Assert.InRange(path[^1].Y, spawn.Position.Y - 0.3f, spawn.Position.Y + 0.3f);
            _out.WriteLine($"{spawn.Id}: {Grid.LastSearchExpanded} spans searched");
        }
    }

    [Fact]
    public void The_gantry_has_stairs_at_both_ends()
    {
        // The engine shed's corner is at (−66, −30): its floor by the east doors and the west wall, and the gantry above.
        float east = PathLength(new Vector3(-26f, 0f, -21f), new Vector3(-25f, 4.5f, -29f));
        float west = PathLength(new Vector3(-62f, 0f, -18f), new Vector3(-62f, 4.5f, -29f));
        _out.WriteLine($"up the east stairs {east:0.0} m, up the west stairs {west:0.0} m");
        Assert.True(east < 25f, $"{east:0} m from the east doors up to the east end of the gantry");
        Assert.True(west < 20f, $"{west:0} m from the west end of the shed up to the gantry");
    }

    [Fact]
    public void The_footbridge_crosses_every_track()
    {
        // From the foot of its north stairs to the foot of its south stairs, without setting foot on the tracks.
        var path = new List<Vector3>();
        Assert.True(Grid.FindPath(new Vector3(1.2f, 0f, -38f), new Vector3(1.2f, 6f, -12f), path));
        Assert.True(Grid.FindPath(new Vector3(1.2f, 6f, -12f), new Vector3(1.2f, 0f, 14f), path));
        float deck = PathLength(new Vector3(1.2f, 6f, -26f), new Vector3(1.2f, 6f, 2f));
        Assert.InRange(deck, 27.5f, 29f);
    }
}
