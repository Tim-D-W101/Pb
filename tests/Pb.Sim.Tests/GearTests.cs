using Pb.Sim.Events;
using Pb.Sim.Gear;

namespace Pb.Sim.Tests;

public class FireControlTests
{
    private static readonly FireControlParams Params = TestData.Config.Fire;

    [Fact]
    public void Shipped_rate_cap_is_10_5_bps()
    {
        Assert.Equal(10.5f, Params.RateCap, 3);
    }

    [Fact]
    public void Semi_fires_one_shot_per_pull()
    {
        var fc = new FireControl(Params) { Mode = FireMode.Semi };
        List<double> shots = Run(fc, seconds: 10, pullHz: 2);
        Assert.Equal(20, shots.Count);
    }

    [Fact]
    public void Semi_never_exceeds_the_cap_even_with_frantic_pulls()
    {
        var fc = new FireControl(Params) { Mode = FireMode.Semi };
        List<double> shots = Run(fc, seconds: 10, pullHz: 20);
        Assert.InRange(shots.Count, 100, (int)(Params.RateCap * 10) + 1);
        AssertMinInterval(shots);
    }

    [Fact]
    public void Ramping_at_8_pulls_per_second_fires_at_the_cap()
    {
        var fc = new FireControl(Params) { Mode = FireMode.Ramping };
        List<double> shots = Run(fc, seconds: 60, pullHz: 8);
        Assert.InRange(shots.Count, 628, 632);
        AssertMinInterval(shots);

        // Once ramping, shots are exactly evenly spaced despite 120 Hz ticks.
        double interval = 1.0 / Params.RateCap;
        for (int i = 10; i < shots.Count; i++)
        {
            Assert.Equal(interval, shots[i] - shots[i - 1], 5);
        }
    }

    [Fact]
    public void Ramping_needs_fast_pulls_and_stops_when_they_stop()
    {
        var slow = new FireControl(Params) { Mode = FireMode.Ramping };
        Assert.Equal(30, Run(slow, seconds: 10, pullHz: 3).Count); // below 5 Hz: plain semi

        var fc = new FireControl(Params) { Mode = FireMode.Ramping };
        List<double> shots = Run(fc, seconds: 2, pullHz: 8, stopPullingAfter: 1.0);
        double lastPull = 7 / 8.0; // pulls at 0, 0.125 … 0.875 s
        double sustain = 1.0 / Params.RampMinPullRate;
        Assert.All(shots, t => Assert.True(t <= lastPull + sustain + 1e-6, $"shot at {t:F3}s after ramp should have ended"));
    }

    [Fact]
    public void Blocked_fires_nothing_and_resets_ramp()
    {
        var fc = new FireControl(Params) { Mode = FireMode.Ramping };
        Span<float> buffer = stackalloc float[8];
        double dt = 1.0 / 120;
        for (int tick = 0; tick < 600; tick++)
        {
            bool trigger = tick % 15 < 7;
            Assert.Equal(0, fc.Update(tick * dt, dt, trigger, blocked: true, buffer));
        }

        Assert.False(fc.IsRamping);
    }

    /// <summary>Simulates pulling the trigger at <paramref name="pullHz"/> on 120 Hz ticks; returns absolute shot times.</summary>
    internal static List<double> Run(FireControl fc, double seconds, double pullHz, double stopPullingAfter = double.MaxValue)
    {
        const double dt = 1.0 / 120;
        var shots = new List<double>();
        Span<float> buffer = stackalloc float[8];
        int ticks = (int)Math.Round(seconds / dt);
        int periodTicks = (int)Math.Round(1.0 / (pullHz * dt));
        for (int tick = 0; tick < ticks; tick++)
        {
            double t0 = tick * dt;
            bool trigger = t0 < stopPullingAfter && tick % periodTicks < periodTicks / 2;
            int n = fc.Update(t0, dt, trigger, blocked: false, buffer);
            for (int i = 0; i < n; i++)
            {
                shots.Add(t0 + buffer[i]);
            }
        }

        return shots;
    }

    private static void AssertMinInterval(List<double> shots)
    {
        double interval = 1.0 / Params.RateCap;
        for (int i = 1; i < shots.Count; i++)
        {
            Assert.True(shots[i] - shots[i - 1] >= interval - 1e-6, $"shots {i - 1}→{i} only {shots[i] - shots[i - 1]:F4}s apart");
        }
    }
}

public class MarkerTests
{
    private static Marker NewMarker() =>
        new(TestData.Config.Fire, TestData.Config.Loader, TestData.Config.Air, TestData.Config.Shot.MuzzleVelocity);

    [Fact]
    public void Shipped_loader_and_pods_match_spec()
    {
        LoaderParams p = TestData.Config.Loader;
        Assert.Equal(200, p.Capacity);
        Assert.Equal(3, p.PodCount);
        Assert.Equal(140, p.PodCapacity);
        Assert.Equal(2.5f, p.RefillTime, 4);
    }

    [Fact]
    public void Loader_empties_after_200_shots_then_dry_fires()
    {
        var harness = new Harness(NewMarker());
        int shots = harness.PullRepeatedly(pulls: 230, ticksPerPull: 12);
        Assert.Equal(200, shots);
        Assert.Equal(0, harness.Marker.Paint.Loader);
        Assert.True(harness.Count(SimEventType.DryFire) >= 29);
    }

    [Fact]
    public void Refill_takes_exactly_its_duration_and_moves_one_pod()
    {
        var harness = new Harness(NewMarker());
        harness.PullRepeatedly(pulls: 200, ticksPerPull: 12);
        harness.Events.Clear();

        harness.Step(refill: true);
        Assert.Equal(1, harness.Count(SimEventType.RefillStarted));
        int ticks = 1;
        while (harness.Count(SimEventType.RefillCompleted) == 0)
        {
            harness.Step();
            ticks++;
            Assert.True(ticks < 1000);
        }

        float expected = TestData.Config.Loader.RefillTime * TestData.Config.TickRate;
        Assert.InRange(ticks, expected - 1, expected + 1);
        Assert.Equal(140, harness.Marker.Paint.Loader);
        Assert.Equal(new[] { 0, 140, 140 }, harness.Marker.Paint.Pods.ToArray());
    }

    [Fact]
    public void Refill_tops_up_only_what_fits_and_keeps_the_rest_in_the_pod()
    {
        var harness = new Harness(NewMarker());
        harness.PullRepeatedly(pulls: 50, ticksPerPull: 12); // loader 150
        harness.Step(refill: true);
        harness.StepFor(TestData.Config.Loader.RefillTime + 0.1f);
        Assert.Equal(200, harness.Marker.Paint.Loader);
        Assert.Equal(new[] { 90, 140, 140 }, harness.Marker.Paint.Pods.ToArray());
    }

    [Fact]
    public void Pressing_fire_cancels_refill_without_moving_paint()
    {
        var harness = new Harness(NewMarker());
        harness.PullRepeatedly(pulls: 100, ticksPerPull: 12); // loader 100
        harness.Events.Clear();
        harness.Step(refill: true);
        harness.StepFor(1.0f);
        harness.Step(trigger: true); // pull: cancels, then fires
        Assert.Equal(1, harness.Count(SimEventType.RefillCancelled));
        Assert.Equal(0, harness.Count(SimEventType.RefillCompleted));
        Assert.Equal(new[] { 140, 140, 140 }, harness.Marker.Paint.Pods.ToArray());
        Assert.InRange(harness.Marker.Paint.Loader, 99, 100);
    }

    [Fact]
    public void Holding_the_trigger_through_a_refill_fires_nothing()
    {
        var harness = new Harness(NewMarker());
        harness.PullRepeatedly(pulls: 100, ticksPerPull: 12);
        harness.Step(trigger: true); // one shot, then keep holding
        int before = harness.Shots;
        harness.Step(trigger: true, refill: true);
        harness.StepFor(TestData.Config.Loader.RefillTime + 0.1f, trigger: true);
        Assert.Equal(before, harness.Shots);
        Assert.True(harness.Count(SimEventType.RefillCompleted) == 1);
    }

    [Fact]
    public void Refill_is_denied_when_the_loader_is_full()
    {
        var harness = new Harness(NewMarker());
        harness.Step(refill: true);
        Assert.Equal(1, harness.Count(SimEventType.RefillDenied));
        Assert.False(harness.Marker.Refill.Active);
    }

    [Fact]
    public void Sprinting_blocks_firing()
    {
        var harness = new Harness(NewMarker());
        for (int i = 0; i < 20; i++)
        {
            harness.Step(trigger: i % 2 == 0, sprinting: true);
        }

        Assert.Equal(0, harness.Shots);
    }

    private sealed class Harness
    {
        private readonly ShotRequest[] _buffer = new ShotRequest[8];
        private int _tick;

        public Harness(Marker marker)
        {
            Marker = marker;
        }

        public Marker Marker { get; }

        public SimEventQueue Events { get; } = new();

        public int Shots { get; private set; }

        public int Count(SimEventType type) => Events.CountOf(type);

        public void Step(bool trigger = false, bool refill = false, bool sprinting = false)
        {
            float dt = TestData.Dt;
            Shots += Marker.Update(_tick * (double)dt, dt, new MarkerInput(trigger, refill, false, sprinting, true), _buffer, Events, 1, 0, _tick);
            _tick++;
        }

        public void StepFor(float seconds, bool trigger = false)
        {
            int ticks = (int)MathF.Round(seconds * TestData.Config.TickRate);
            for (int i = 0; i < ticks; i++)
            {
                Step(trigger);
            }
        }

        public int PullRepeatedly(int pulls, int ticksPerPull)
        {
            int before = Shots;
            for (int p = 0; p < pulls; p++)
            {
                for (int t = 0; t < ticksPerPull; t++)
                {
                    Step(trigger: t < ticksPerPull / 2);
                }
            }

            return Shots - before;
        }
    }
}

public class AirTankTests
{
    [Fact]
    public void Default_fill_gives_about_1000_full_velocity_shots()
    {
        var tank = new AirTank(TestData.Config.Air);
        int shots = 0;
        while (!tank.BelowRegulator)
        {
            Assert.Equal(1f, tank.VelocityFactor);
            tank.ConsumeShot();
            shots++;
        }

        Assert.InRange(shots, 950, 1050);
        Assert.InRange(TestData.Config.Air.FullVelocityShots, 950f, 1050f);
    }

    [Fact]
    public void Velocity_falls_off_below_the_regulator_and_stops_at_cutoff()
    {
        var tank = new AirTank(TestData.Config.Air);
        while (!tank.BelowRegulator)
        {
            tank.ConsumeShot();
        }

        float previous = 1f;
        while (tank.CanFire)
        {
            float factor = tank.VelocityFactor;
            Assert.True(factor <= previous + 1e-6f, "velocity must not rise as the tank empties");
            Assert.True(factor < 1f);
            previous = factor;
            tank.ConsumeShot();
        }

        Assert.False(tank.CanFire);
        Assert.Equal(0f, tank.VelocityFactor);
        tank.Fill();
        Assert.True(tank.CanFire);
        Assert.Equal(1f, tank.VelocityFactor);
    }

    [Fact]
    public void Marker_shots_slow_down_when_the_tank_is_low()
    {
        var marker = new Marker(TestData.Config.Fire, TestData.Config.Loader, TestData.Config.Air, 88f);
        var events = new SimEventQueue();
        var buffer = new ShotRequest[8];
        float lastSpeed = 88f;
        int tick = 0;
        while (marker.Air.CanFire && tick < 400000)
        {
            if (marker.Paint.Loader == 0)
            {
                marker.Paint.Fill();
            }

            bool trigger = tick % 12 < 6;
            int n = marker.Update(tick / 120.0, 1f / 120, new MarkerInput(trigger, false, false, false, true), buffer, events, 1, 0, tick);
            for (int i = 0; i < n; i++)
            {
                Assert.True(buffer[i].MuzzleSpeed <= 88f + 1e-3f);
                lastSpeed = buffer[i].MuzzleSpeed;
            }

            events.Clear();
            tick++;
        }

        Assert.True(lastSpeed < 40f, $"last shots before cutoff should be slow (got {lastSpeed:F1} m/s)");
    }
}
