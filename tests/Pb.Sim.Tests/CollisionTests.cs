using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Level;
using Pb.Sim.Core;

namespace Pb.Sim.Tests;

public class CollisionTests
{
    private const float BallRadius = 0.00865f;
    private const float Tolerance = 1e-4f;

    [Fact]
    public void Plane_hit_time_accounts_for_ball_radius()
    {
        var plane = new PlaneShape(Vector3.UnitY, 0f);
        Assert.True(plane.Sweep(new Vector3(0, 1, 0), new Vector3(0, -2, 0), 0.1f, out float t, out Vector3 n));
        Assert.Equal(0.45f, t, 4);
        AssertVector(Vector3.UnitY, n);
        Assert.False(plane.Sweep(new Vector3(0, 1, 0), new Vector3(0, 0.5f, 0), 0.1f, out _, out _));
    }

    [Fact]
    public void Sphere_front_hit_miss_and_start_inside()
    {
        var sphere = new SphereShape(Vector3.Zero, 1f);
        Assert.True(sphere.Sweep(new Vector3(0, 0, 5), new Vector3(0, 0, -10), 0.1f, out float t, out Vector3 n));
        Assert.Equal((5f - 1.1f) / 10f, t, 4);
        AssertVector(Vector3.UnitZ, n);

        Assert.False(sphere.Sweep(new Vector3(2, 0, 5), new Vector3(0, 0, -10), 0.1f, out _, out _));
        Assert.True(sphere.Sweep(new Vector3(0.5f, 0, 0), new Vector3(0, 0, -1), 0.1f, out t, out n));
        Assert.Equal(0f, t);
        AssertVector(Vector3.UnitX, n);
    }

    [Fact]
    public void Box_hit_reports_entry_face_even_when_rotated()
    {
        var box = new BoxShape(new Vector3(0, 1, 0), Quaternion.Identity, new Vector3(1, 1, 0.5f));
        Assert.True(box.Sweep(new Vector3(0, 1, 5), new Vector3(0, 0, -10), BallRadius, out float t, out Vector3 n));
        Assert.Equal((5f - 0.5f - BallRadius) / 10f, t, 4);
        AssertVector(Vector3.UnitZ, n);

        var rotated = new BoxShape(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4), new Vector3(1, 1, 1));
        Assert.True(rotated.Sweep(new Vector3(0, 0, 5), new Vector3(0, 0, -10), 0f, out t, out n));
        Vector3 expected = Vector3.Transform(Vector3.UnitZ, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4));
        // Hitting the corner-on box: either adjacent face is a valid entry normal; both face +Z-ish.
        Assert.True(n.Z > 0.7f);
        Assert.True(MathF.Abs(MathF.Abs(Vector3.Dot(n, expected)) - 1f) < 1e-3f || MathF.Abs(Vector3.Dot(n, expected)) < 1e-3f);
        Assert.Equal((5f - MathF.Sqrt(2f)) / 10f, t, 3);
    }

    [Fact]
    public void Box_start_inside_pushes_out_of_nearest_face()
    {
        var box = new BoxShape(Vector3.Zero, Quaternion.Identity, new Vector3(1, 1, 1));
        Assert.True(box.Sweep(new Vector3(0.9f, 0, 0), new Vector3(0, 0, -1), 0f, out float t, out Vector3 n));
        Assert.Equal(0f, t);
        AssertVector(Vector3.UnitX, n);
    }

    [Fact]
    public void Cylinder_side_and_cap_hits()
    {
        var can = new CylinderShape(new Vector3(0, 0.75f, 0), Vector3.UnitY, 0.75f, 0.6f);
        Assert.True(can.Sweep(new Vector3(0, 1, 5), new Vector3(0, 0, -10), BallRadius, out float t, out Vector3 n));
        Assert.Equal((5f - 0.6f - BallRadius) / 10f, t, 4);
        AssertVector(Vector3.UnitZ, n);

        Assert.True(can.Sweep(new Vector3(0.2f, 5, 0), new Vector3(0, -10, 0), BallRadius, out t, out n));
        Assert.Equal((5f - 1.5f - BallRadius) / 10f, t, 4);
        AssertVector(Vector3.UnitY, n);

        Assert.False(can.Sweep(new Vector3(0.7f, 5, 0), new Vector3(0, -10, 0), BallRadius, out _, out _));
    }

    [Fact]
    public void Capsule_body_and_cap_hits()
    {
        var capsule = new CapsuleShape(new Vector3(0, 0.3f, 0), new Vector3(0, 1.3f, 0), 0.21f);
        Assert.True(capsule.Sweep(new Vector3(0, 0.8f, 5), new Vector3(0, 0, -10), BallRadius, out float t, out Vector3 n));
        Assert.Equal((5f - 0.21f - BallRadius) / 10f, t, 4);
        AssertVector(Vector3.UnitZ, n);

        Assert.True(capsule.Sweep(new Vector3(0, 5, 0), new Vector3(0, -10, 0), BallRadius, out t, out n));
        Assert.Equal((5f - 1.51f - BallRadius) / 10f, t, 4);
        AssertVector(Vector3.UnitY, n);
    }

    [Fact]
    public void Wedge_slope_normal_faces_up_and_out()
    {
        var wedge = ConvexShape.Wedge(Vector3.Zero, 0f, 2f, 1f, 2f);
        // Straight down onto the right slope at x = 0.5 (surface height there is 0.5).
        Assert.True(wedge.Sweep(new Vector3(0.5f, 3, 0), new Vector3(0, -6, 0), 0f, out float t, out Vector3 n));
        Assert.Equal((3f - 0.5f) / 6f, t, 4);
        AssertVector(Vector3.Normalize(new Vector3(1, 1, 0)), n);

        Assert.False(wedge.Sweep(new Vector3(1.2f, 3, 0), new Vector3(0, -6, 0), 0f, out _, out _));
    }

    [Fact]
    public void A_levels_wedge_and_lying_tube_are_where_their_boxes_say()
    {
        // A wedge piece 2 m wide, 1.2 m tall and 3 m long, turned a quarter round: its ridge runs along X, 1.2 m up.
        Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        var wedge = new LevelPrimitive
        {
            Kind = PrimitiveKind.Wedge, Center = new Vector3(5f, 0.6f, 0f), Rotation = turn, HalfExtents = new Vector3(1f, 0.6f, 1.5f),
            Material = 0, Surface = new SurfaceId(0), Flags = PrimitiveFlags.Paint, Role = PrimitiveRole.Prop, Owner = 0,
        };
        Shape w = wedge.CreateShape();
        Assert.Equal(0f, w.Bounds.Min.Y, 4);
        Assert.Equal(1.2f, w.Bounds.Max.Y, 4);
        Assert.Equal(1.2f, wedge.Height, 4);
        // Down onto the ridge anywhere along it, and onto its slope (half way down at 0.5 m across: 0.6 m up).
        Assert.True(w.Sweep(new Vector3(6.2f, 3f, 0f), new Vector3(0f, -6f, 0f), 0f, out float t, out _));
        Assert.Equal((3f - 1.2f) / 6f, t, 3);
        Assert.True(w.Sweep(new Vector3(5f, 3f, 0.5f), new Vector3(0f, -6f, 0f), 0f, out t, out Vector3 n));
        Assert.Equal((3f - 0.6f) / 6f, t, 3);
        Assert.True(n.Y > 0.5f && n.Z > 0.5f, $"the slope faces up and out along +Z: {n}");
        Assert.False(w.Sweep(new Vector3(5f, 3f, 1.2f), new Vector3(0f, -6f, 0f), 0f, out _, out _));

        // A tube 1.2 m round and 4 m long lying along Z: its top 1.2 m up, its rounded ends 2 m either side.
        var tube = new LevelPrimitive
        {
            Kind = PrimitiveKind.Capsule, Center = new Vector3(0f, 0.6f, 0f), Rotation = Quaternion.Identity, HalfExtents = new Vector3(0.6f, 0.6f, 2f),
            Material = 0, Surface = new SurfaceId(0), Flags = PrimitiveFlags.Paint, Role = PrimitiveRole.Prop, Owner = 0,
        };
        Shape c = tube.CreateShape();
        Assert.Equal(1.2f, tube.Height, 4);
        AssertVector(new Vector3(-0.6f, 0f, -2f), c.Bounds.Min);
        AssertVector(new Vector3(0.6f, 1.2f, 2f), c.Bounds.Max);
        Assert.True(c.Sweep(new Vector3(0f, 3f, 1.3f), new Vector3(0f, -6f, 0f), BallRadius, out t, out n));
        Assert.Equal((3f - 1.2f - BallRadius) / 6f, t, 3);
        AssertVector(Vector3.UnitY, n);
        Assert.True(c.Sweep(new Vector3(0f, 0.6f, 6f), new Vector3(0f, 0f, -6f), BallRadius, out t, out n));
        Assert.Equal((6f - 2f - BallRadius) / 6f, t, 3);
        AssertVector(Vector3.UnitZ, n);
    }

    [Fact]
    public void Grid_broadphase_agrees_with_brute_force()
    {
        var rng = new Pcg32(42);
        var world = new CollisionWorld(cellSize: 2f);
        var surface = new SurfaceId(0);
        world.Add(new PlaneShape(Vector3.UnitY, 0f), surface, "ground");
        for (int i = 0; i < 80; i++)
        {
            var at = new Vector3(rng.Range(-20, 20), 0, rng.Range(-20, 20));
            float yaw = rng.Range(0, MathF.Tau);
            Shape shape = (i % 5) switch
            {
                0 => new BoxShape(at + new Vector3(0, 0.9f, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw), new Vector3(rng.Range(0.1f, 1.5f), 0.9f, rng.Range(0.005f, 1f))),
                1 => new CylinderShape(at + new Vector3(0, 0.75f, 0), Vector3.UnitY, 0.75f, rng.Range(0.2f, 1f)),
                2 => new CapsuleShape(at + new Vector3(0, 0.3f, 0), at + new Vector3(rng.Range(-1, 1), 1.3f, rng.Range(-1, 1)), rng.Range(0.1f, 0.4f)),
                3 => new SphereShape(at + new Vector3(0, rng.Range(0.2f, 2f), 0), rng.Range(0.1f, 0.8f)),
                _ => ConvexShape.Wedge(at, yaw, rng.Range(0.5f, 3f), rng.Range(0.5f, 1.5f), rng.Range(0.5f, 3f)),
            };
            world.Add(shape, surface, $"s{i}");
        }

        world.Build();
        int hits = 0;
        for (int i = 0; i < 20000; i++)
        {
            var from = new Vector3(rng.Range(-24, 24), rng.Range(0.02f, 3f), rng.Range(-24, 24));
            var to = from + new Vector3(rng.Symmetric(1.2f), rng.Symmetric(0.4f), rng.Symmetric(1.2f));
            bool fast = world.SweepSphere(from, to, BallRadius, out SweepHit a);
            bool slow = world.SweepSphereBruteForce(from, to, BallRadius, out SweepHit b);
            Assert.Equal(slow, fast);
            if (fast)
            {
                hits++;
                Assert.Equal(b.T, a.T, 5);
                Assert.Equal(b.ColliderId, a.ColliderId);
            }
        }

        Assert.True(hits > 500, $"test should exercise plenty of hits (got {hits})");
    }

    private static void AssertVector(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) < Tolerance * 10, $"expected {expected}, got {actual}");
    }
}
