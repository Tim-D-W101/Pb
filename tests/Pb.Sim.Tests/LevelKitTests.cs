using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Xunit;

namespace Pb.Sim.Tests;

public class LevelKitTests
{
    private static readonly MaterialRef Concrete = new(0, TestData.Config.Surfaces.Get("concrete"));

    private static float BallRadius => TestData.Config.Projectile.Radius;

    private static LevelLayout Level => TestData.Data.Levels["oxbarrow_works"];

    private static CollisionWorld WorldOf(IEnumerable<LevelPrimitive> primitives)
    {
        var world = new CollisionWorld();
        foreach (LevelPrimitive p in primitives.Where(p => p.Has(PrimitiveFlags.Paint)))
        {
            world.Add(p.CreateShape(), p.Surface, "test");
        }

        world.Build();
        return world;
    }

    private static List<LevelPrimitive> SingleWall(params OpeningSpec[] openings)
    {
        var sink = new PrimitiveSink();
        KitGeometry.Wall(sink, PlanFrame.Identity, new[] { new Vector2(0, 0), new Vector2(6, 0) }, false, 0.3f, 0f, 3f, Concrete, openings, 0);
        return sink.Items;
    }

    [Fact]
    public void DoorLeavesTwoPiersAndALintel()
    {
        List<LevelPrimitive> pieces = SingleWall(new OpeningSpec(0, 3f, 1f, 0f, 2.1f, OpeningKind.Door));

        Assert.Equal(3, pieces.Count);
        Aabb[] boxes = pieces.Select(p => p.Bounds).OrderBy(b => b.Min.X).ThenBy(b => b.Min.Y).ToArray();
        // Left pier 0–2.5 m, lintel 2.5–3.5 m above 2.1 m, right pier 3.5–6 m.
        Assert.Equal(0f, boxes[0].Min.X, 3);
        Assert.Equal(2.5f, boxes[0].Max.X, 3);
        Assert.Equal(2.5f, boxes[1].Min.X, 3);
        Assert.Equal(3.5f, boxes[1].Max.X, 3);
        Assert.Equal(2.1f, boxes[1].Min.Y, 3);
        Assert.Equal(3f, boxes[1].Max.Y, 3);
        Assert.Equal(3.5f, boxes[2].Min.X, 3);
        Assert.Equal(6f, boxes[2].Max.X, 3);
        Assert.All(boxes, b => Assert.Equal(0.3f, b.Max.Z - b.Min.Z, 3));
    }

    [Fact]
    public void BallThroughAWindowPassesAndOneBesideItHits()
    {
        CollisionWorld world = WorldOf(SingleWall(new OpeningSpec(0, 3f, 1.4f, 0.9f, 1.2f, OpeningKind.Window)));

        // Through the middle of the window (x = 3, y = 1.5): clear.
        Assert.False(world.SweepSphere(new Vector3(3f, 1.5f, -2f), new Vector3(3f, 1.5f, 2f), BallRadius, out _));
        // Half a metre beside it, and just below the sill: both hit the wall's face.
        Assert.True(world.SweepSphere(new Vector3(1.8f, 1.5f, -2f), new Vector3(1.8f, 1.5f, 2f), BallRadius, out SweepHit beside));
        Assert.Equal(-0.15f - BallRadius, beside.Point.Z, 3);
        Assert.True(world.SweepSphere(new Vector3(3f, 0.8f, -2f), new Vector3(3f, 0.8f, 2f), BallRadius, out _));
        // Just inside the window's edge passes; just outside hits.
        Assert.False(world.SweepSphere(new Vector3(2.3f + BallRadius + 0.005f, 1.5f, -2f), new Vector3(2.3f + BallRadius + 0.005f, 1.5f, 2f), BallRadius, out _));
        Assert.True(world.SweepSphere(new Vector3(2.3f + BallRadius - 0.005f, 1.5f, -2f), new Vector3(2.3f + BallRadius - 0.005f, 1.5f, 2f), BallRadius, out _));
    }

    [Fact]
    public void RailingStopsBallsAtItsMembersAndLetsThemThroughBetween()
    {
        var sink = new PrimitiveSink();
        KitGeometry.Railing(sink, PlanFrame.Identity, new[] { new Vector2(0, 0), new Vector2(4, 0) }, false, 0.05f, 1.5f,
            0f, 1.1f, Concrete, new[] { new OpeningSpec(0, 3.3f, 0.6f, 0f, float.NaN, OpeningKind.Gap) }, 0);
        CollisionWorld world = WorldOf(sink.Items);
        bool Hits(float x, float y) => world.SweepSphere(new Vector3(x, y, -2f), new Vector3(x, y, 2f), BallRadius, out _);

        // Posts at 0, 1.5 and 2.975 (beside the gap from 3.0 to 3.6), then 3.625 and 4.0.
        Assert.True(Hits(1.5f, 0.8f));
        Assert.True(Hits(2.975f, 0.8f));
        Assert.True(Hits(3.625f, 0.8f));
        // Between posts: the top rail, mid rail and toe board stop paint; the bays between them don't.
        Assert.True(Hits(0.8f, 1.08f));
        Assert.True(Hits(0.8f, 0.55f));
        Assert.True(Hits(0.8f, 0.05f));
        Assert.False(Hits(0.8f, 0.8f));
        Assert.False(Hits(0.8f, 0.3f));
        // Nothing at all in the gap.
        Assert.False(Hits(3.3f, 0.55f));
        Assert.False(Hits(3.3f, 1.08f));
        // A railing blocks walking but is neither cover nor an occluder.
        Assert.All(sink.Items, p =>
        {
            Assert.True(p.Has(PrimitiveFlags.Walk));
            Assert.False(p.Has(PrimitiveFlags.Cover));
            Assert.False(p.Has(PrimitiveFlags.Occluder));
        });
    }

    [Fact]
    public void ClosedRectangleCornersAreSolid()
    {
        var sink = new PrimitiveSink();
        KitGeometry.Wall(sink, PlanFrame.Identity, new[] { new Vector2(0, 0), new Vector2(4, 0), new Vector2(4, 3), new Vector2(0, 3) },
            true, 0.3f, 0f, 2.5f, Concrete, Array.Empty<OpeningSpec>(), 0);
        CollisionWorld world = WorldOf(sink.Items);

        // Diagonal shots at every outer corner hit the wall rather than slipping between segments.
        foreach (Vector2 corner in new[] { new Vector2(0, 0), new Vector2(4, 0), new Vector2(4, 3), new Vector2(0, 3) })
        {
            var centre = new Vector3(2f, 1f, 1.5f);
            var c = new Vector3(corner.X, 1f, corner.Y);
            Vector3 outside = c + Vector3.Normalize(c - centre) * 2f;
            Assert.True(world.SweepSphere(outside, centre, BallRadius, out SweepHit hit));
            Assert.True(Vector3.Distance(hit.Point, c) < 0.3f, $"corner {corner}: hit at {hit.Point}");
        }
    }

    [Fact]
    public void BuildingYawRotatesItsGeometry()
    {
        var plain = new PrimitiveSink();
        var turned = new PrimitiveSink();
        Vector2[] points = { new(0, 0), new(5, 0) };
        KitGeometry.Wall(plain, new PlanFrame(new Vector3(10, 0, 0), 0f), points, false, 0.2f, 0f, 3f, Concrete, Array.Empty<OpeningSpec>(), 0);
        KitGeometry.Wall(turned, new PlanFrame(new Vector3(10, 0, 0), MathF.PI / 2f), points, false, 0.2f, 0f, 3f, Concrete, Array.Empty<OpeningSpec>(), 0);

        // A wall along local +X ends up along world −Z after a 90° (left) turn about the origin.
        Assert.Equal(new Vector3(12.5f, 1.5f, 0f), plain.Items[0].Center);
        Vector3 c = turned.Items[0].Center;
        Assert.Equal(10f, c.X, 4);
        Assert.Equal(-2.5f, c.Z, 4);
        Aabb b = turned.Items[0].Bounds;
        Assert.Equal(5f, b.Max.Z - b.Min.Z, 3);
        Assert.Equal(0.2f, b.Max.X - b.Min.X, 3);
    }

    [Fact]
    public void SlabHolesAreSubtractedExactly()
    {
        var rng = new Pcg32(SeedHash.Shot(7, 0, 0));
        for (int trial = 0; trial < 200; trial++)
        {
            var rect = new Vector4(0f, 0f, 10f, 8f);
            var holes = new List<Vector4>();
            int count = (int)(rng.NextUInt() % 4);
            for (int i = 0; i < count; i++)
            {
                float x0 = rng.NextFloat() * 9f, z0 = rng.NextFloat() * 7f;
                holes.Add(new Vector4(x0, z0, x0 + 0.3f + rng.NextFloat() * 4f, z0 + 0.3f + rng.NextFloat() * 4f));
            }

            List<Vector4> pieces = KitGeometry.SubtractHoles(rect, holes);

            // Sample a grid: every point is covered by exactly one piece iff it's outside every hole.
            for (float x = 0.05f; x < 10f; x += 0.1f)
            {
                for (float z = 0.05f; z < 8f; z += 0.1f)
                {
                    bool inHole = holes.Any(h => x > h.X && x < h.Z && z > h.Y && z < h.W);
                    int covering = pieces.Count(p => x > p.X && x < p.Z && z > p.Y && z < p.W);
                    Assert.Equal(inHole ? 0 : 1, covering);
                }
            }
        }
    }

    [Fact]
    public void StairRampRunsAlongTheStepNosings()
    {
        var sink = new PrimitiveSink();
        KitGeometry.Stairs(sink, PlanFrame.Identity, new Vector2(0f, 0f), 0f, 1.2f, 4.8f, 3.2f, 16, 0f, Concrete, 0);

        LevelPrimitive[] walkable = sink.Items.Where(p => p.Role == PrimitiveRole.Ramp).ToArray();
        Assert.All(walkable, p => Assert.Equal(PrimitiveFlags.Walk, p.Flags));
        LevelPrimitive ramp = walkable.OrderByDescending(p => p.HalfExtents.Z).First();
        Assert.Equal(16, sink.Items.Count(p => p.Role == PrimitiveRole.Stair && p.Has(PrimitiveFlags.Paint)));

        // The ramp's top surface passes through the middle of every tread (stairs climb toward −Z) and
        // meets the ground half a tread in front of the first step.
        Vector3 up = Vector3.Transform(Vector3.UnitY, ramp.Rotation);
        float Above(Vector3 point) => Vector3.Dot(point - ramp.Center, up) - ramp.HalfExtents.Y;
        for (int k = 1; k <= 16; k++)
        {
            float height = Above(new Vector3(0f, k * 0.2f, -(k - 0.5f) * 0.3f));
            Assert.True(MathF.Abs(height) < 0.01f, $"step {k}: {height:0.000} m off the ramp");
        }

        Assert.True(MathF.Abs(Above(new Vector3(0f, 0f, 0.15f))) < 0.01f);

        // The flat top piece is flush with the top step and reaches its far edge.
        LevelPrimitive top = walkable.OrderBy(p => p.HalfExtents.Z).First();
        Assert.Equal(3.2f, top.Bounds.Max.Y, 3);
        Assert.Equal(-4.8f, top.Bounds.Min.Z, 3);
    }

    [Fact]
    public void LevelOneBuildsWithEverySystemFed()
    {
        LevelLayout level = Level;
        Assert.Equal("Oxbarrow Works", level.DisplayName);
        Assert.True(level.Primitives.Count > 400, $"only {level.Primitives.Count} primitives");
        Assert.Contains(level.Primitives, p => p.Role == PrimitiveRole.Ramp);
        Assert.Contains(level.Primitives, p => p.Has(PrimitiveFlags.Occluder));
        Assert.Contains(level.Primitives, p => p.Has(PrimitiveFlags.Cover));
        Assert.Equal(4, level.Primitives.Count(p => p.Role == PrimitiveRole.Boundary));
        Assert.True(level.OpponentSpawns.Count >= 9, "need at least nine opponent spawns for Hard");
        Assert.All(level.Props, prop => Assert.True(prop.PrimitiveCount > 0));
        Assert.Equal("office, upstairs", level.AreaAt(new Vector3(-40f, 4f, 16f))?.Name);
        Assert.Equal("the yard", level.AreaAt(new Vector3(0f, 1f, 10f))?.Name);
    }

    /// <summary>Spawns, patrol points and pickups must be in open space and standing on something.</summary>
    [Fact]
    public void LevelOnePointsAreInOpenSpaceOnAFloor()
    {
        LevelLayout level = Level;
        var world = new CollisionWorld();
        level.BuildCollision(world);

        var points = new List<(string Name, Vector3 Position, float Clearance)>
        {
            ("player spawn", level.PlayerSpawn, 0.3f),
            ("dead zone", level.DeadZone, 0.3f),
        };
        points.AddRange(level.OpponentSpawns.Select(s => ($"spawn {s.Id}", s.Position, 0.3f)));
        points.AddRange(level.Patrols.SelectMany(p => p.Points.Select((pt, i) => ($"patrol {p.Id}[{i}]", pt, 0.3f))));
        points.AddRange(level.Pickups.Select(p => ($"pickup {p.Id}", p.Position, 0.25f)));

        foreach ((string name, Vector3 position, float clearance) in points)
        {
            foreach (float height in new[] { 0.4f, 1.0f, 1.5f })
            {
                Vector3 probe = position + new Vector3(0f, height, 0f);
                Assert.False(world.SweepSphere(probe, probe, clearance, out SweepHit hit),
                    $"{name} at {position} overlaps {world.Colliders[hit.ColliderId].Name} at {height} m");
            }

            Assert.True(world.SweepSphere(position + new Vector3(0f, 0.3f, 0f), position - new Vector3(0f, 0.3f, 0f), 0.01f, out SweepHit floor),
                $"{name} at {position} is not standing on anything");
            Assert.True(floor.Point.Y >= position.Y - 0.25f && floor.Point.Y <= position.Y + 0.25f, $"{name}: floor at {floor.Point.Y}");
        }
    }

    [Fact]
    public void LongSweepsMatchTheBruteForceReference()
    {
        var world = new CollisionWorld();
        Level.BuildCollision(world);
        var rng = new Pcg32(SeedHash.Shot(11, 1, 2));
        Aabb b = Level.Bounds;
        for (int i = 0; i < 5000; i++)
        {
            Vector3 Random() => new(
                b.Min.X + rng.NextFloat() * (b.Max.X - b.Min.X),
                rng.NextFloat() * 12f,
                b.Min.Z + rng.NextFloat() * (b.Max.Z - b.Min.Z));
            Vector3 from = Random(), to = Random();
            float radius = i % 3 == 0 ? 0f : BallRadius;

            bool fast = world.SweepSphere(from, to, radius, out SweepHit a);
            bool slow = world.SweepSphereBruteForce(from, to, radius, out SweepHit r);
            Assert.Equal(slow, fast);
            if (slow)
            {
                Assert.Equal(r.T, a.T, 5);
            }
        }
    }

    [Fact]
    public void CornersSharperThanNinetyDegreesAreRejected()
    {
        var source = new EditedDataSource(TestData.Source).Edit("kit/buildings/guardhouse.jsonc",
            s => s.Replace("[[0, 0], [6, 0], [6, 5], [0, 5]]", "[[0, 0], [6, 0], [1, 2], [0, 5]]"));

        DataException ex = Assert.Throws<DataException>(() => GameData.Load(source));
        Assert.Contains("kit/buildings/guardhouse.jsonc", ex.Message);
        Assert.Contains("walls[0].points_m", ex.Message);
        Assert.Contains("more than 90°", ex.Message);
    }

    [Fact]
    public void OpeningsThatRunPastTheirSegmentAreRejected()
    {
        var source = new EditedDataSource(TestData.Source).Edit("kit/buildings/guardhouse.jsonc",
            s => s.Replace("{ \"segment\": 1, \"at_m\": 2.5, \"width_m\": 0.9", "{ \"segment\": 1, \"at_m\": 4.8, \"width_m\": 0.9"));

        DataException ex = Assert.Throws<DataException>(() => GameData.Load(source));
        Assert.Contains("walls[0].openings[0].at_m", ex.Message);
    }

    [Fact]
    public void UnknownReferencesNameTheFileAndKey()
    {
        var badMaterial = new EditedDataSource(TestData.Source).Edit("kit/buildings/pump_house.jsonc",
            s => s.Replace("\"material\": \"corrugated_grey\"", "\"material\": \"corrugated_gray\""));
        DataException ex = Assert.Throws<DataException>(() => GameData.Load(badMaterial));
        Assert.Contains("kit/buildings/pump_house.jsonc: roof.material: unknown material 'corrugated_gray'", ex.Message);

        var badProp = new EditedDataSource(TestData.Source).Edit("levels/oxbarrow_works.jsonc",
            s => s.Replace("\"prop\": \"car_wreck\", \"position_m\": [21", "\"prop\": \"car_wreek\", \"position_m\": [21"));
        ex = Assert.Throws<DataException>(() => GameData.Load(badProp));
        Assert.Contains("levels/oxbarrow_works.jsonc", ex.Message);
        Assert.Contains("unknown prop 'car_wreek'", ex.Message);

        var badSurface = new EditedDataSource(TestData.Source).Edit("kit/materials.jsonc",
            s => s.Replace("\"surface\": \"rubber\"", "\"surface\": \"rubbr\""));
        ex = Assert.Throws<DataException>(() => GameData.Load(badSurface));
        Assert.Contains("kit/materials.jsonc", ex.Message);
        Assert.Contains("unknown surface 'rubbr'", ex.Message);
    }
}
