using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Level;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>
/// The Hospital Wing (M3.7): three floors in two wings and the stairs between them, the lift shaft, the fallen end, the
/// curtains round the beds, and the Marksmen's windows over the courtyard.
/// </summary>
[Collection(BotArenaCollection.Name)]
public class HospitalTests
{
    // The wings' local frames: the north wing's origin is at world (−48, 0, −36), the west wing's at (−48, 0, −22).
    private static readonly Vector3 North = new(-48f, 0f, -36f);
    private static readonly Vector3 West = new(-48f, 0f, -22f);
    private const float Storey = 3.4f;

    private static readonly Lazy<CollisionWorld> Paint = new(() =>
    {
        var world = new CollisionWorld();
        Level.BuildCollision(world);
        return world;
    });

    private readonly ITestOutputHelper _out;

    public HospitalTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static LevelLayout Level => BotArena.SharedFor("hospital_wing").Level;

    private static NavGrid Grid => BotArena.SharedFor("hospital_wing").Grid;

    private static Vector3 Gate => Level.PlayerSpawn;

    private static (float Length, Vector3 End) Walk(Vector3 from, Vector3 to)
    {
        var path = new List<Vector3>();
        Assert.True(Grid.FindPath(from, to, path), $"no path from {from} to {to}");
        float length = 0f;
        Vector3 at = from;
        foreach (Vector3 p in path)
        {
            length += Vector3.Distance(at, p);
            at = p;
        }

        return (length, path[^1]);
    }

    /// <summary>Whether there's a span within 0.3 m of height <paramref name="y"/> under (x, z).</summary>
    private static bool StandsAt(float x, float y, float z)
    {
        int span = Grid.SpanAt(new Vector3(x, y, z));
        return span != -1 && MathF.Abs(Grid.PositionOf(span).Y - y) < 0.3f;
    }

    [Fact]
    public void Every_floor_of_both_wings_is_reached_up_its_stairs()
    {
        var targets = new (string Name, Vector3 At)[]
        {
            ("the north corridor, first floor", North + new Vector3(30f, Storey, 7f)),
            ("the north corridor, top floor", North + new Vector3(10f, 2f * Storey, 7f)),
            ("the west corridor, first floor", West + new Vector3(7f, Storey, 30f)),
            ("the west corridor, top floor", West + new Vector3(7f, 2f * Storey, 20f)),
        };
        foreach ((string name, Vector3 at) in targets)
        {
            (float length, Vector3 end) = Walk(Gate, at);
            _out.WriteLine($"{name}: {length:0} m from the gate");
            Assert.True(Vector3.Distance(end, at) < 1f, $"the path to {name} ends at {end}");
        }

        // The east stairs serve the east end: from the ground floor corridor there up to the top floor is a short climb.
        (float east, _) = Walk(North + new Vector3(40f, 0f, 7f), North + new Vector3(39.5f, 2f * Storey, 7f));
        _out.WriteLine($"up the east stairs: {east:0.0} m");
        Assert.True(east < 25f, $"{east:0} m: the east stairs don't reach the top floor");
    }

    [Fact]
    public void The_lift_shaft_drops_through_every_floor()
    {
        Vector3 shaft = North + new Vector3(21.25f, 0f, 4.55f);
        Assert.False(StandsAt(shaft.X, Storey, shaft.Z), "something to stand on in the shaft on the first floor");
        Assert.False(StandsAt(shaft.X, 2f * Storey, shaft.Z), "something to stand on in the shaft on the top floor");
        Assert.False(Paint.Value.SweepSphere(shaft + new Vector3(0f, 9.5f, 0f), shaft + new Vector3(0f, 0.3f, 0f), 0.0085f, out SweepHit hit),
            $"a ball dropped down the shaft hits {Paint.Value.Colliders[hit.ColliderId].Name} at {hit.Point}");
    }

    [Fact]
    public void The_fallen_end_is_open_to_the_sky()
    {
        Vector3 ruin = North + new Vector3(50f, 0f, 7f);
        Assert.False(Paint.Value.SweepSphere(ruin + new Vector3(0f, 25f, 0f), ruin + new Vector3(0f, 1.0f, 0f), 0.0085f, out SweepHit hit),
            $"something over the fallen end: {Paint.Value.Colliders[hit.ColliderId].Name} at {hit.Point}");
        Assert.True(StandsAt(ruin.X, 0f, ruin.Z));
        // The first floor breaks off at x 46 (some of the roof lies on it), the top floor at x 42.5.
        Assert.True(StandsAt(North.X + 44.8f, Storey, North.Z + 12.8f), "the first floor's broken edge can't be stood on");
        Assert.False(StandsAt(North.X + 48f, Storey, North.Z + 7f));
        Assert.False(StandsAt(North.X + 44.5f, 2f * Storey, North.Z + 7f));
    }

    [Fact]
    public void Curtains_hide_you_and_stop_a_ball_but_you_walk_through_them()
    {
        PropInstance[] curtains = Level.Props.Where(p => p.Type.Id == "curtain_screen").ToArray();
        Assert.True(curtains.Length >= 20, $"only {curtains.Length} curtains");
        int blocked = 0, walkable = 0;
        foreach (PropInstance curtain in curtains)
        {
            LevelPrimitive cloth = Level.Primitives[curtain.FirstPrimitive];
            Vector3 across = Vector3.Transform(Vector3.UnitZ, cloth.Rotation);
            Vector3 chest = cloth.Center with { Y = curtain.Position.Y + 1.2f };
            blocked += Paint.Value.SweepSphere(chest - across * 0.5f, chest + across * 0.5f, 0.0085f, out _) ? 1 : 0;
            walkable += StandsAt(cloth.Center.X, curtain.Position.Y, cloth.Center.Z) ? 1 : 0;
        }

        _out.WriteLine($"{curtains.Length} curtains: {blocked} stop a ball across them, {walkable} stand on walkable floor");
        Assert.Equal(curtains.Length, blocked);
        Assert.Equal(curtains.Length, walkable);
    }

    [Fact]
    public void Marksmen_watch_the_courtyard_from_the_ward_windows()
    {
        OpponentSpawn[] marksmen = Level.OpponentSpawns.Where(s => s.Roles.Contains("marksman")).ToArray();
        Assert.Equal(new[] { "womens_ward", "sun_ward" }, marksmen.Select(s => s.Id));
        var courtyard = new List<Vector3>();
        for (float x = -30f; x <= 4f; x += 4f)
        {
            for (float z = -18f; z <= 14f; z += 4f)
            {
                courtyard.Add(new Vector3(x, 1.2f, z));
            }
        }

        foreach (OpponentSpawn spawn in marksmen)
        {
            Vector3 eye = spawn.Position + new Vector3(0f, TestData.Config.Movement.StandEyeHeight, 0f);
            int seen = courtyard.Count(p => !Paint.Value.SweepSphere(eye, p, 0f, out _));
            _out.WriteLine($"{spawn.Id} at {spawn.Position} sees {seen} of {courtyard.Count} points in the courtyard");
            Assert.True(spawn.Position.Y >= 2f * Storey - 0.1f, $"{spawn.Id} isn't on the top floor");
            Assert.True(seen >= courtyard.Count / 5, $"{spawn.Id} sees only {seen} of the courtyard");
        }
    }

    [Fact]
    public void Flankers_hold_the_stairwells()
    {
        foreach (string id in new[] { "east_stairs", "corner_stairs", "south_stairs" })
        {
            Assert.Contains("flanker", Level.OpponentSpawns.Single(s => s.Id == id).Roles);
        }
    }
}
