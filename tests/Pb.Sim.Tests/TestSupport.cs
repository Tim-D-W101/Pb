using System.Numerics;
using Pb.Sim.Ballistics;
using Pb.Sim.Collision;
using Pb.Sim.Data;
using Pb.Sim.Events;

namespace Pb.Sim.Tests;

/// <summary>Shared fixtures: the shipped data files and small worlds built around them.</summary>
internal static class TestData
{
    public static FileSystemDataSource Source { get; } = FileSystemDataSource.FindRepoData();

    private static readonly Lazy<GameData> Loaded = new(() => GameData.Load(Source));

    public static GameData Data => Loaded.Value;

    public static SimConfig Config => Data.Config;

    public static float Dt => Config.Dt;

    /// <summary>Ballistics over an infinite flat ground (y = 0) with no other geometry.</summary>
    public static (BallisticsWorld Balls, CollisionWorld World, SimEventQueue Events) GroundOnly(int capacity = 64)
    {
        var world = new CollisionWorld();
        world.Add(new PlaneShape(Vector3.UnitY, 0f), Config.Surfaces.Get("turf"), "ground");
        world.Build();
        var balls = new BallisticsWorld(capacity, Config.Projectile, Config.BreakModel) { World = world };
        return (balls, world, new SimEventQueue());
    }

    public static SimWorld RangeWorld()
    {
        var sim = new SimWorld(Config);
        sim.LoadRange(Data.Range, Data.Stress);
        return sim;
    }
}

/// <summary>
/// What the calling thread allocates while some code runs, for the tests that hot paths don't allocate. A garbage
/// collection running alongside the measured code (another test's, or a background one still going) can move a thread's
/// allocation count by the unused rest of its allocation context, a few bytes to a few KB it never allocated. So
/// collections in progress are finished first and held off while the code runs, and a run a collection broke into anyway
/// is measured again.
/// </summary>
internal static class Allocations
{
    /// <summary>What every test thread together may allocate while the code runs before a collection is let through.</summary>
    private const long RegionBytes = 256L * 1024 * 1024;

    private const int Attempts = 5;

    /// <summary>One measurement at a time: the process has only one no-collection region.</summary>
    private static readonly object Gate = new();

    /// <summary>Bytes allocated on this thread by <paramref name="run"/> (called again if a collection disturbed it).</summary>
    public static long During(Action run)
    {
        lock (Gate)
        {
            for (int attempt = 1; ; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                bool held = GC.TryStartNoGCRegion(RegionBytes);
                bool undisturbed;
                long allocated;
                try
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    run();
                    allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                }
                finally
                {
                    undisturbed = held && EndRegion();
                }

                if (undisturbed || allocated == 0 || attempt == Attempts)
                {
                    return allocated;
                }
            }
        }
    }

    /// <returns>false if a collection ended the region early (the others allocated more than it holds).</returns>
    private static bool EndRegion()
    {
        try
        {
            GC.EndNoGCRegion();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

/// <summary>A data source that serves edited copies of real files, for validation tests.</summary>
internal sealed class EditedDataSource : IDataSource
{
    private readonly IDataSource _inner;
    private readonly Dictionary<string, Func<string, string>> _edits = new(StringComparer.Ordinal);

    public EditedDataSource(IDataSource inner)
    {
        _inner = inner;
    }

    public EditedDataSource Edit(string path, Func<string, string> edit)
    {
        _edits[path] = edit;
        return this;
    }

    public string ReadAllText(string path)
    {
        string text = _inner.ReadAllText(path);
        return _edits.TryGetValue(path, out Func<string, string>? edit) ? edit(text) : text;
    }
}

/// <summary>
/// Independent high-precision reference: classic RK4 in double precision at 10 µs steps,
/// same physics as the spec (quadratic drag + gravity). Used to catch regressions far tighter
/// than the spec's ±10% tolerance.
/// </summary>
internal static class Reference
{
    private const double Step = 1e-5;

    public static (double Drop, double Speed) LevelProbe(ProjectileParams p, double v0, double height, double distance)
    {
        double k = p.DragFactor;
        double g = p.Gravity;
        double x = 0, y = height, vx = v0, vy = 0;
        while (true)
        {
            double px = x, py = y, pvx = vx, pvy = vy;
            Rk4(k, g, ref x, ref y, ref vx, ref vy);
            if (x >= distance)
            {
                double f = (distance - px) / (x - px);
                double yy = py + f * (y - py);
                double speed = Math.Sqrt(Sq(pvx + f * (vx - pvx)) + Sq(pvy + f * (vy - pvy)));
                return (height - yy, speed);
            }
        }
    }

    /// <summary>Horizontal distance at which a ball centre reaches <paramref name="contactHeight"/>.</summary>
    public static double GroundRange(ProjectileParams p, double v0, double height, double elevationDeg, double contactHeight)
    {
        double k = p.DragFactor;
        double g = p.Gravity;
        double a = elevationDeg * Math.PI / 180.0;
        double x = 0, y = height, vx = v0 * Math.Cos(a), vy = v0 * Math.Sin(a);
        while (true)
        {
            double px = x, py = y;
            Rk4(k, g, ref x, ref y, ref vx, ref vy);
            if (y <= contactHeight)
            {
                double f = (py - contactHeight) / (py - y);
                return px + f * (x - px);
            }
        }
    }

    private static void Rk4(double k, double g, ref double x, ref double y, ref double vx, ref double vy)
    {
        (double ax1, double ay1) = Accel(k, g, vx, vy);
        double vx2 = vx + ax1 * Step / 2, vy2 = vy + ay1 * Step / 2;
        (double ax2, double ay2) = Accel(k, g, vx2, vy2);
        double vx3 = vx + ax2 * Step / 2, vy3 = vy + ay2 * Step / 2;
        (double ax3, double ay3) = Accel(k, g, vx3, vy3);
        double vx4 = vx + ax3 * Step, vy4 = vy + ay3 * Step;
        (double ax4, double ay4) = Accel(k, g, vx4, vy4);
        x += Step / 6 * (vx + 2 * vx2 + 2 * vx3 + vx4);
        y += Step / 6 * (vy + 2 * vy2 + 2 * vy3 + vy4);
        vx += Step / 6 * (ax1 + 2 * ax2 + 2 * ax3 + ax4);
        vy += Step / 6 * (ay1 + 2 * ay2 + 2 * ay3 + ay4);
    }

    private static (double, double) Accel(double k, double g, double vx, double vy)
    {
        double s = Math.Sqrt(vx * vx + vy * vy);
        return (-k * s * vx, -k * s * vy - g);
    }

    private static double Sq(double v) => v * v;
}
