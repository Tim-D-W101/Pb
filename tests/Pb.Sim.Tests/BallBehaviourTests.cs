using System.Numerics;
using Pb.Sim.Ballistics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Events;
using Pb.Sim.Range;

namespace Pb.Sim.Tests;

public class BallBehaviourTests
{
    [Fact]
    public void No_ball_tunnels_through_a_1_cm_panel()
    {
        // A ball covers ~0.73 m per tick at 88 m/s, so only the swept test can catch a 1 cm panel.
        SimConfig c = TestData.Config;
        var world = new CollisionWorld();
        Collider panel = world.Add(new BoxShape(new Vector3(0, 1.5f, -10), Quaternion.Identity, new Vector3(0.75f, 0.75f, 0.005f)),
            c.Surfaces.Get("panel"), "panel");
        world.Build();
        const int shots = 10000;
        var balls = new BallisticsWorld(shots, c.Projectile, c.BreakModel)
        {
            World = world,
            Bounds = new Aabb(new Vector3(-50, -50, -10.5f), new Vector3(50, 50, 50)),
        };
        var events = new SimEventQueue(shots * 4);
        var rng = new Pcg32(99);
        for (uint i = 0; i < shots; i++)
        {
            // Random start distance and sub-tick phase so the panel is crossed at every possible offset.
            var origin = new Vector3(rng.Symmetric(0.6f), 1.5f + rng.Symmetric(0.6f), -rng.Range(0f, 0.8f));
            var velocity = new Vector3(0, 0, -(88f + rng.Symmetric(1.5f)));
            balls.Spawn(origin, velocity, 1, i, 0, new Pcg32(i), rng.Range(0.0001f, TestData.Dt), 0, events);
        }

        for (int tick = 0; tick < 240 && balls.Pool.Count > 0; tick++)
        {
            balls.Tick(tick, TestData.Dt, events);
        }

        var firstImpacts = new HashSet<uint>();
        int passedThrough = 0;
        foreach (SimEvent e in events.Items)
        {
            if (e.Type is SimEventType.BallBroke or SimEventType.BallBounced && e.ColliderId == panel.Id)
            {
                firstImpacts.Add(e.ShotSequence);
            }

            if (e.Type == SimEventType.BallDespawned && e.Extra == (int)DespawnReason.OutOfBounds && e.Position.Z < -10.4f)
            {
                passedThrough++;
            }
        }

        Assert.Equal(0, passedThrough);
        Assert.Equal(shots, firstImpacts.Count);
    }

    [Fact]
    public void Break_probability_follows_the_data_curve()
    {
        BreakModel model = TestData.Config.BreakModel;
        SurfaceId inflatable = TestData.Config.Surfaces.Get("inflatable");
        Assert.Equal(0.5f, model.BreakProbability(inflatable, 22f), 3);
        Assert.True(model.BreakProbability(inflatable, 58f) > 0.999f);
        Assert.InRange(model.BreakProbability(inflatable, 15f), 0.15f, 0.25f);
        Assert.True(model.BreakProbability(inflatable, 5f) < 0.05f);
    }

    [Fact]
    public void Head_on_fast_hits_almost_always_break()
    {
        float fraction = BreakFraction(new Vector3(0, 0, -58f), start: new Vector3(0, 1f, -0.5f));
        Assert.True(fraction >= 0.99f, $"break fraction {fraction:P1}");
    }

    [Fact]
    public void Glancing_hits_mostly_bounce()
    {
        float angle = 15f * Units.DegreesToRadians;
        // Starts over the floor block, beyond the wall, so the first contact is the floor's top face.
        float fraction = BreakFraction(new Vector3(0, -MathF.Sin(angle), -MathF.Cos(angle)) * 58f, start: new Vector3(0, 0.25f, -3f));
        Assert.InRange(fraction, 0.10f, 0.30f);
    }

    [Fact]
    public void Bounced_balls_never_eliminate()
    {
        // A wall that never breaks paint deflects every ball into a dummy target.
        var surfaces = new SurfaceRegistry(new[] { "wall", "player" });
        var model = new BreakModel(surfaces, new[]
        {
            new SurfaceResponse(Break50: 500f, BreakWidth: 1f, Restitution: 0.45f, TangentRetain: 0.8f),
            new SurfaceResponse(Break50: 5f, BreakWidth: 1f, Restitution: 0.3f, TangentRetain: 0.7f),
        }, restSpeed: 0.5f, maxBounces: 4);

        var world = new CollisionWorld();
        world.Add(new BoxShape(new Vector3(0, 1.2f, -5), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4), new Vector3(2f, 1.2f, 0.2f)),
            surfaces.Get("wall"), "deflector");
        world.Build();

        var targets = new TargetSet();
        var dummy = new TargetKindSpec
        {
            Id = "dummy",
            Surface = surfaces.Get("player"),
            Parts = new[] { new TargetPartSpec { Part = HitboxPart.Body, Kind = PartShapeKind.Capsule, From = new Vector3(0, 0.2f, 0), To = new Vector3(0, 2.2f, 0), Radius = 1.2f } },
        };
        targets.Load(new[] { new TargetSpec { Id = "t", Label = "t", Kind = dummy, BasePosition = new Vector3(4.5f, 0, -6.1f), Yaw = 0f } });

        var balls = new BallisticsWorld(512, TestData.Config.Projectile, model) { World = world, Hitboxes = targets };
        var events = new SimEventQueue();
        for (uint i = 0; i < 200; i++)
        {
            balls.Spawn(new Vector3(0, 1.2f, 0), new Vector3(0, 0.4f, -30f), 1, i, 0, new Pcg32(i), TestData.Dt, 0, events);
        }

        for (int tick = 0; tick < 480 && balls.Pool.Count > 0; tick++)
        {
            balls.Tick(tick, TestData.Dt, events);
        }

        int breaksOnTarget = 0;
        foreach (SimEvent e in events.Items)
        {
            Assert.NotEqual(SimEventType.TargetHit, e.Type);
            if (e.Type == SimEventType.BallBroke && e.TargetId >= 0)
            {
                Assert.False(e.Lethal);
                breaksOnTarget++;
            }
        }

        Assert.True(breaksOnTarget > 50, $"scenario should put bounced paint on the target (got {breaksOnTarget})");
        Assert.Equal(0, targets.HitCount(0));
    }

    [Fact]
    public void Dispersion_stays_inside_the_cone_and_fills_it_uniformly()
    {
        float half = TestData.Config.Shot.DispersionHalfAngle;
        Vector3 axis = Vector3.Normalize(new Vector3(0.3f, 0.1f, -1f));
        var rng = new Pcg32(5);
        int inner = 0;
        const int samples = 100000;
        for (int i = 0; i < samples; i++)
        {
            Vector3 d = Dispersion.SampleCone(axis, half, ref rng);
            float angle = Dispersion.AngleBetween(axis, d);
            Assert.True(angle <= half * 1.0005f, $"sample outside cone: {angle * Units.RadiansToDegrees}°");
            if (angle <= half * 0.5f)
            {
                inner++;
            }
        }

        // Uniform over solid angle: share inside half the angle = (1 − cos(θ/2)) / (1 − cos θ) ≈ 25 %.
        double expected = (1 - Math.Cos(half * 0.5)) / (1 - Math.Cos(half));
        Assert.InRange(inner / (double)samples, expected - 0.01, expected + 0.01);
    }

    [Fact]
    public void Moving_widens_dispersion_up_to_the_cap()
    {
        SimWorld sim = TestData.RangeWorld();
        ShotParams p = TestData.Config.Shot;
        Assert.Equal(p.DispersionHalfAngle, sim.DispersionFor(0f), 6);
        Assert.Equal(p.DispersionHalfAngle + p.MovingDispersionPerSpeed * 5.5f, sim.DispersionFor(5.5f), 6);
        Assert.Equal(p.MaxDispersion, sim.DispersionFor(1000f), 6);
    }

    [Fact]
    public void Shot_velocity_varies_within_the_configured_band()
    {
        SimWorld sim = TestData.RangeWorld();
        var player = sim.AddPlayer(1, 0, sim.Range!.SpawnPosition, 0f);
        player.Pitch = 0.05f;
        var commands = new Players.InputCommand[1];
        float min = float.MaxValue, max = float.MinValue;
        for (int tick = 0; tick < 12000; tick++)
        {
            commands[0] = new Players.InputCommand
            {
                Tick = tick, Yaw = 0f, Pitch = 0.05f,
                Buttons = tick % 12 < 6 ? Players.InputButtons.Fire : Players.InputButtons.None,
            };
            if (player.Marker.Paint.Loader == 0 || !player.Marker.Air.CanFire)
            {
                player.Marker.ResetGear();
            }

            sim.Step(commands);
            foreach (SimEvent e in sim.Events.Items)
            {
                if (e.Type == SimEventType.ShotFired && e.PlayerId == 1)
                {
                    float speed = e.Velocity.Length();
                    min = MathF.Min(min, speed);
                    max = MathF.Max(max, speed);
                }
            }

            sim.Events.Clear();
        }

        float v0 = TestData.Config.Shot.MuzzleVelocity;
        float variance = TestData.Config.Shot.VelocityVariance;
        Assert.InRange(min, v0 - variance - 1e-3f, v0 - variance * 0.9f);
        Assert.InRange(max, v0 + variance * 0.9f, v0 + variance + 1e-3f);
    }

    private static float BreakFraction(Vector3 velocity, Vector3 start)
    {
        SimConfig c = TestData.Config;
        var world = new CollisionWorld();
        // Big inflatable block: top face at y = 0, front face at z = -1.
        world.Add(new BoxShape(new Vector3(0, -1, -51), Quaternion.Identity, new Vector3(50, 1, 50)), c.Surfaces.Get("inflatable"), "floor");
        world.Add(new BoxShape(new Vector3(0, 1, -1.5f), Quaternion.Identity, new Vector3(50, 1, 0.5f)), c.Surfaces.Get("inflatable"), "wall");
        world.Build();
        var balls = new BallisticsWorld(8192, c.Projectile, c.BreakModel) { World = world };
        var events = new SimEventQueue(32768);
        const int shots = 4000;
        for (uint i = 0; i < shots; i++)
        {
            balls.Spawn(start, velocity, 1, i, 0, new Pcg32(SeedHash.Shot(3, 1, i)), TestData.Dt, 0, events);
        }

        var first = new Dictionary<uint, bool>();
        for (int tick = 0; tick < 60 && balls.Pool.Count > 0; tick++)
        {
            events.Clear();
            balls.Tick(tick, TestData.Dt, events);
            foreach (SimEvent e in events.Items)
            {
                if (e.Type is SimEventType.BallBroke or SimEventType.BallBounced)
                {
                    first.TryAdd(e.ShotSequence, e.Type == SimEventType.BallBroke);
                }
            }
        }

        Assert.Equal(shots, first.Count);
        return first.Values.Count(b => b) / (float)shots;
    }
}
