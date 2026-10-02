using System.Numerics;
using Pb.Sim.Core;
using Pb.Sim.Events;

namespace Pb.Sim.Tests;

/// <summary>
/// Spec §1.1 validation targets, run through the real sim code with the shipped data:
/// fired level from 1.5 m at 88 m/s, drop at 20 m ≈ 0.35 m and speed ≈ 58 m/s, drop at 30 m ≈ 0.92 m,
/// max range ≈ 93 m at ~30°. Each is checked against the spec (±10%) and against an independent
/// high-precision reference (±1%), so a regression can't hide inside the loose spec band.
/// </summary>
public class BallisticsValidationTests
{
    private const float Height = 1.5f;
    private const float MuzzleSpeed = 88f;

    [Fact]
    public void Shipped_data_matches_spec_defaults()
    {
        SimConfig c = TestData.Config;
        Assert.Equal(0.00865f, c.Projectile.Radius, 5);
        Assert.Equal(0.0032f, c.Projectile.Mass, 6);
        Assert.Equal(0.47f, c.Projectile.DragCoefficient, 5);
        Assert.Equal(88f, c.Shot.MuzzleVelocity, 3);
        Assert.True(c.TickRate >= 120f, "spec requires a tick of at least 120 Hz");
        Assert.InRange(c.Projectile.DragFactor, 0.0210f, 0.0213f);
    }

    [Theory]
    [InlineData(20f, 0.35f, 58f)]
    [InlineData(30f, 0.92f, float.NaN)]
    public void Level_shot_drop_and_speed_match_spec(float distance, float specDrop, float specSpeed)
    {
        (float drop, float speed) = FireLevelAndProbe(distance);

        Assert.InRange(drop, specDrop * 0.9f, specDrop * 1.1f);
        if (!float.IsNaN(specSpeed))
        {
            Assert.InRange(speed, specSpeed * 0.9f, specSpeed * 1.1f);
        }

        (double refDrop, double refSpeed) = Reference.LevelProbe(TestData.Config.Projectile, MuzzleSpeed, Height, distance);
        Assert.InRange(drop, refDrop * 0.99, refDrop * 1.01);
        Assert.InRange(speed, refSpeed * 0.99, refSpeed * 1.01);
    }

    [Fact]
    public void Max_range_is_about_93_m_near_30_degrees()
    {
        float bestRange = 0f;
        float bestAngle = 0f;
        double bestReference = 0;
        for (float angle = 20f; angle <= 40f; angle += 0.5f)
        {
            float range = GroundRange(angle);
            if (range > bestRange)
            {
                bestRange = range;
                bestAngle = angle;
            }

            bestReference = Math.Max(bestReference,
                Reference.GroundRange(TestData.Config.Projectile, MuzzleSpeed, Height, angle, TestData.Config.Projectile.Radius));
        }

        Assert.InRange(bestRange, 93f * 0.9f, 93f * 1.1f);
        Assert.InRange(bestAngle, 25f, 35f);
        Assert.InRange(bestRange, bestReference * 0.99, bestReference * 1.01);
    }

    [Fact]
    public void Level_shot_from_eye_height_lands_before_40_m()
    {
        // Documents why the 40 m target needs holdover.
        float range = GroundRange(0f);
        Assert.InRange(range, 33f, 39f);
    }

    private static (float Drop, float Speed) FireLevelAndProbe(float distance)
    {
        var (balls, _, events) = TestData.GroundOnly();
        float dt = TestData.Dt;
        balls.Spawn(new Vector3(0f, Height, 0f), new Vector3(0f, 0f, -MuzzleSpeed), 1, 1, 0, new Pcg32(1), dt, 0, events);

        for (int tick = 0; tick < 2000 && balls.Pool.Count > 0; tick++)
        {
            Vector3 v0 = balls.Pool.Velocity[0];
            balls.Tick(tick, dt, events);
            Assert.Equal(1, balls.Pool.Count);
            Vector3 a = balls.Pool.PrevPosition[0];
            Vector3 b = balls.Pool.Position[0];
            if (-b.Z >= distance)
            {
                float f = (distance + a.Z) / (a.Z - b.Z);
                float y = a.Y + f * (b.Y - a.Y);
                Vector3 v = Vector3.Lerp(v0, balls.Pool.Velocity[0], f);
                return (Height - y, v.Length());
            }
        }

        throw new Xunit.Sdk.XunitException("ball never reached the probe distance");
    }

    private static float GroundRange(float elevationDeg)
    {
        var (balls, _, events) = TestData.GroundOnly();
        float dt = TestData.Dt;
        float a = elevationDeg * Units.DegreesToRadians;
        var velocity = new Vector3(0f, MathF.Sin(a), -MathF.Cos(a)) * MuzzleSpeed;
        balls.Spawn(new Vector3(0f, Height, 0f), velocity, 1, 1, 0, new Pcg32(7), dt, 0, events);

        for (int tick = 0; tick < 5000; tick++)
        {
            events.Clear();
            balls.Tick(tick, dt, events);
            foreach (SimEvent e in events.Items)
            {
                if (e.Type is SimEventType.BallBroke or SimEventType.BallBounced)
                {
                    return -e.Position.Z;
                }
            }
        }

        throw new Xunit.Sdk.XunitException("ball never landed");
    }
}

public class FlatFireTests
{
    [Theory]
    [InlineData(10f)]
    [InlineData(20f)]
    [InlineData(30f)]
    public void Flat_fire_drop_matches_the_reference_within_2_percent(float distance)
    {
        var p = TestData.Config.Projectile;
        (double refDrop, double refSpeed) = Reference.LevelProbe(p, 88, 1.5, distance);
        float drop = Pb.Sim.Ballistics.FlatFire.Drop(p.DragFactor, p.Gravity, 88f, distance);
        float speed = Pb.Sim.Ballistics.FlatFire.Speed(p.DragFactor, 88f, distance);
        Assert.InRange(drop, refDrop * 0.98, refDrop * 1.02);
        Assert.InRange(speed, refSpeed * 0.99, refSpeed * 1.01);
    }
}
