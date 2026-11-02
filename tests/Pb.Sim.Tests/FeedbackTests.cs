using System.Numerics;
using Pb.Game.Ballistics;
using Pb.Game.Ui;
using Pb.Sim.Ballistics;
using Pb.Sim.Core;
using Pb.Sim.Events;

namespace Pb.Sim.Tests;

/// <summary>
/// Hit feedback's sums (Phase 5, M5.1): which way the incoming-fire arcs point, how they merge and expire, and which balls
/// count as whizzing past your head.
/// </summary>
public class FeedbackTests
{
    private static readonly float[] Lives = [1.0f, 1.4f, 1.8f, 3.0f];

    [Theory]
    // Facing −Z (yaw 0): paint travelling +Z came from straight ahead, −X from the right, +X from the left, −Z from behind.
    [InlineData(0f, 0f, 1f, 0f)]
    [InlineData(-1f, 0f, 0f, 90f)]
    [InlineData(1f, 0f, 0f, -90f)]
    [InlineData(0f, 0f, -1f, 180f)]
    [InlineData(-1f, 0f, 1f, 45f)]
    public void An_arc_points_back_along_the_balls_travel(float x, float y, float z, float expectedDeg)
    {
        var fire = new IncomingFire(8, 20f, Lives);
        Assert.True(fire.Add(new Vector3(x, y, z) * 70f, IncomingKind.NearBreak));
        float screen = IncomingFire.ScreenAngle(fire.Arcs[0].Bearing, viewYaw: 0f) * 180f / MathF.PI;
        Assert.Equal(expectedDeg, MathF.Abs(expectedDeg) == 180f ? MathF.Abs(screen) : screen, 3);
    }

    [Fact]
    public void An_arc_turns_with_you_and_sits_ahead_once_you_face_where_the_paint_came_from()
    {
        var fire = new IncomingFire(8, 20f, Lives);
        fire.Add(new Vector3(-1f, 0f, 0f), IncomingKind.Hit); // from the right of someone facing −Z
        float bearing = fire.Arcs[0].Bearing;

        // Turning right (negative yaw) by 90° faces it.
        Assert.Equal(0f, IncomingFire.ScreenAngle(bearing, -MathF.PI / 2f), 4);
        // The source is where ViewAngles says that yaw faces.
        Vector3 facing = ViewAngles.FlatForward(bearing);
        Assert.Equal(1f, facing.X, 4);
        // Turned the other way, it's behind.
        Assert.Equal(MathF.PI, MathF.Abs(IncomingFire.ScreenAngle(bearing, MathF.PI / 2f)), 4);
    }

    [Fact]
    public void Paint_from_much_the_same_way_refreshes_one_arc_and_takes_the_more_urgent_kind()
    {
        var fire = new IncomingFire(8, 20f, Lives);
        fire.Add(new Vector3(0f, 0f, 1f), IncomingKind.Flyby, 0.3f);
        fire.Update(0.5f);
        // 10° off: the same arc, now a near break, renewed.
        fire.Add(new Vector3(MathF.Sin(0.17f), 0f, MathF.Cos(0.17f)), IncomingKind.NearBreak, 0.6f);
        Assert.Equal(1, fire.Count);
        Assert.Equal(IncomingKind.NearBreak, fire.Arcs[0].Kind);
        Assert.Equal(0f, fire.Arcs[0].Age);
        Assert.Equal(0.6f, fire.Arcs[0].Strength, 4);

        // A flyby from there doesn't downgrade it.
        fire.Add(new Vector3(0f, 0f, 1f), IncomingKind.Flyby, 0.2f);
        Assert.Equal(IncomingKind.NearBreak, fire.Arcs[0].Kind);
        Assert.Equal(0.6f, fire.Arcs[0].Strength, 4);

        // 40° off: an arc of its own.
        fire.Add(new Vector3(MathF.Sin(0.7f), 0f, MathF.Cos(0.7f)), IncomingKind.Flyby);
        Assert.Equal(2, fire.Count);
    }

    [Fact]
    public void Arcs_last_as_long_as_their_kind_and_fade_before_they_go()
    {
        var fire = new IncomingFire(8, 20f, Lives);
        fire.Add(new Vector3(0f, 0f, 1f), IncomingKind.Flyby);
        fire.Add(new Vector3(0f, 0f, -1f), IncomingKind.Hit);
        Assert.Equal(1f, fire.Arcs[0].Fade);
        fire.Update(0.9f);
        Assert.Equal(2, fire.Count);
        Assert.True(fire.Arcs.ToArray().Single(a => a.Kind == IncomingKind.Flyby).Fade < 0.25f);
        fire.Update(0.2f);
        Assert.Equal(1, fire.Count);
        Assert.Equal(IncomingKind.Hit, fire.Arcs[0].Kind);
        fire.Update(2.0f);
        Assert.Equal(0, fire.Count);
    }

    [Fact]
    public void A_full_ring_drops_its_oldest_arc_and_paint_falling_straight_down_gives_none()
    {
        var fire = new IncomingFire(3, 10f, Lives);
        for (int i = 0; i < 3; i++)
        {
            fire.Add(new Vector3(MathF.Sin(i * 1.5f), 0f, MathF.Cos(i * 1.5f)), IncomingKind.NearBreak);
            fire.Update(0.1f);
        }

        float newest = IncomingFire.BearingOf(-new Vector3(MathF.Sin(4.5f), 0f, MathF.Cos(4.5f)));
        fire.Add(new Vector3(MathF.Sin(4.5f), 0f, MathF.Cos(4.5f)), IncomingKind.NearBreak);
        Assert.Equal(3, fire.Count);
        Assert.Contains(fire.Arcs.ToArray(), a => MathF.Abs(IncomingFire.Wrap(a.Bearing - newest)) < 1e-4f);
        Assert.DoesNotContain(fire.Arcs.ToArray(), a => a.Age >= 0.3f - 1e-4f);

        Assert.False(fire.Add(new Vector3(0f, -60f, 0f), IncomingKind.NearBreak));
    }

    [Fact]
    public void A_bounce_points_back_the_way_the_ball_came_in()
    {
        // In along −X at 45° down onto a floor: it leaves rising; mirrored back through the floor, it was going down.
        Vector3 outgoing = new(-20f, 12f, 0f);
        Vector3 before = IncomingFire.BeforeBounce(outgoing, Vector3.UnitY);
        Assert.Equal(-20f, before.X, 4);
        Assert.True(before.Y < 0f);
        // Already leaving along the surface or into it: unchanged.
        Assert.Equal(new Vector3(-5f, -1f, 0f), IncomingFire.BeforeBounce(new Vector3(-5f, -1f, 0f), Vector3.UnitY));
    }

    [Fact]
    public void A_ball_passing_close_by_is_found_once_on_the_tick_it_passes()
    {
        var pool = new BallPool(16);
        var scan = new FlybyScan(radius: 1.5f, minSpeed: 20f);
        Vector3 head = new(0f, 1.6f, 0f);
        // Flying along +X, 0.6 m to the side of the head, 70 m/s: about 0.58 m a tick.
        int i = pool.Add(new Vector3(-3f, 1.6f, 0.6f), new Vector3(70f, 0f, 0f), owner: 7, sequence: 3, team: 1, new Pcg32(1, 1), 0f);
        var found = new Flyby[4];
        int total = 0, ticks = 0;
        Flyby first = default;
        for (int t = 0; t < 12; t++)
        {
            pool.PrevPosition[i] = pool.Position[i];
            pool.Position[i] += pool.Velocity[i] / 120f;
            int n = scan.Scan(pool, head, skipOwner: 1, found);
            if (n > 0 && total == 0)
            {
                first = found[0];
                ticks = t;
            }

            total += n;
        }

        Assert.Equal(1, total);
        Assert.Equal(0.6f, first.Distance, 2);
        Assert.Equal(7, first.Owner);
        Assert.Equal(3u, first.Sequence);
        // It was found as it passed the head (x = 0), not before.
        Assert.InRange(first.Point.X, -0.6f, 0.6f);
        Assert.InRange(ticks, 4, 6);
    }

    [Fact]
    public void Your_own_balls_slow_balls_and_wide_misses_dont_whizz()
    {
        var pool = new BallPool(16);
        var scan = new FlybyScan(radius: 1.5f, minSpeed: 20f);
        Vector3 head = new(0f, 1.6f, 0f);
        int own = pool.Add(new Vector3(-0.3f, 1.6f, 0.2f), new Vector3(70f, 0f, 0f), owner: 1, sequence: 1, team: 0, new Pcg32(1, 1), 0f);
        int slow = pool.Add(new Vector3(-0.1f, 1.6f, 0.2f), new Vector3(8f, 0f, 0f), owner: 2, sequence: 1, team: 1, new Pcg32(1, 1), 0f);
        int wide = pool.Add(new Vector3(-0.3f, 1.6f, 2.5f), new Vector3(70f, 0f, 0f), owner: 3, sequence: 1, team: 1, new Pcg32(1, 1), 0f);
        foreach (int b in new[] { own, slow, wide })
        {
            pool.PrevPosition[b] = pool.Position[b];
            pool.Position[b] += pool.Velocity[b] / 120f * 2f;
        }

        Assert.Equal(0, scan.Scan(pool, head, skipOwner: 1, new Flyby[4]));
    }

    [Fact]
    public void Nearest_says_a_ball_still_closing_in_hasnt_passed_yet()
    {
        Vector3 head = Vector3.Zero;
        Assert.False(FlybyScan.Nearest(new Vector3(-5f, 0f, 1f), new Vector3(-4f, 0f, 1f), head, out _, out _));
        Assert.True(FlybyScan.Nearest(new Vector3(-0.5f, 0f, 1f), new Vector3(0.5f, 0f, 1f), head, out Vector3 point, out float d2));
        Assert.Equal(1f, d2, 4);
        Assert.Equal(0f, point.X, 4);
        // Already going away: nearest at the start of the move.
        Assert.True(FlybyScan.Nearest(new Vector3(1f, 0f, 1f), new Vector3(2f, 0f, 1f), head, out point, out _));
        Assert.Equal(1f, point.X, 4);
    }

    [Fact]
    public void A_real_shot_past_someones_head_whizzes_once()
    {
        (BallisticsWorld balls, _, SimEventQueue events) = TestData.GroundOnly();
        var scan = new FlybyScan(radius: 1.5f, minSpeed: 20f);
        // 0.8 m to the side of the ball's path where it has dropped 0.34 m, 20 m out (the spec's drop at 20 m).
        Vector3 head = new(20f, 1.5f - 0.343f, 0.8f);
        balls.Spawn(new Vector3(0f, 1.5f, 0f), new Vector3(88f, 0f, 0f), 4, 9, 1, new Pcg32(5), TestData.Dt, 0, events);
        int whizzes = 0;
        var found = new Flyby[4];
        for (int tick = 1; tick < 120 && balls.Pool.Count > 0; tick++)
        {
            balls.Tick(tick, TestData.Dt, events);
            whizzes += scan.Scan(balls.Pool, head, skipOwner: 0, found);
        }

        Assert.Equal(1, whizzes);
        Assert.Equal(0.8f, found[0].Distance, 1);
        Assert.True(found[0].Velocity.X > 50f);
    }
}
