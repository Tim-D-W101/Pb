using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Pb.Sim;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Players;
using Pb.Sim.Range;

// Headless benchmark + ballistics report. Prints Markdown (CI appends it to the job summary).
//   dotnet run -c Release --project tools/Pb.Bench              full run
//   dotnet run -c Release --project tools/Pb.Bench -- --quick   shorter run for CI

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
bool quick = args.Contains("--quick");
int[] ballCounts = { 1000, 2000, 5000 };
int measureTicks = quick ? 600 : 2400;

GameData data = GameData.Load(FileSystemDataSource.FindRepoData());
SimConfig config = data.Config;

Console.WriteLine("## Phase 1 ballistics report");
Console.WriteLine();
Console.WriteLine($"Tick {config.TickRate:0} Hz · drag factor k = {config.Projectile.DragFactor:0.00000} /m · terminal velocity {config.Projectile.TerminalVelocity:0.0} m/s");
Console.WriteLine();
Console.WriteLine("| Check (level shot, 1.5 m, 88 m/s) | Spec | Sim |");
Console.WriteLine("|---|---|---|");
(float drop20, float speed20) = LevelProbe(config, 20f);
(float drop30, _) = LevelProbe(config, 30f);
(float range, float angle) = MaxRange(config);
Console.WriteLine($"| Drop at 20 m | ≈ 0.35 m | {drop20:0.000} m |");
Console.WriteLine($"| Speed at 20 m | ≈ 58 m/s | {speed20:0.0} m/s |");
Console.WriteLine($"| Drop at 30 m | ≈ 0.92 m | {drop30:0.000} m |");
Console.WriteLine($"| Max range | ≈ 93 m at ~30° | {range:0.0} m at {angle:0.0}° |");
Console.WriteLine($"| Full-velocity shots per air fill | ≈ 1000 | {config.Air.FullVelocityShots:0} |");
Console.WriteLine();

Console.WriteLine($"## Sim tick cost ({measureTicks} ticks per row, {RuntimeLabel()})");
Console.WriteLine();
Measure(data, 1000, 600); // untimed warm-up so JIT tiering doesn't skew the first row
Console.WriteLine("| Live balls | Mean ms/tick | p95 ms/tick | Max ms/tick | ms per 60 fps frame (2 ticks) |");
Console.WriteLine("|---|---|---|---|---|");
foreach (int balls in ballCounts)
{
    (double mean, double p95, double max, int live) = Measure(data, balls, measureTicks);
    Console.WriteLine($"| {live} | {mean:0.000} | {p95:0.000} | {max:0.000} | {mean * 2:0.000} |");
}

Console.WriteLine();
foreach ((string id, LevelLayout level) in data.Levels)
{
    Console.WriteLine($"Compound level **{level.DisplayName}** ({level.Primitives.Count} primitives, balls fired from its opponent spawns in all directions):");
    Console.WriteLine();
    Console.WriteLine("| Live balls | Mean ms/tick | p95 ms/tick | Max ms/tick | ms per 60 fps frame (2 ticks) |");
    Console.WriteLine("|---|---|---|---|---|");
    MeasureLevel(data, level, 1000, 300); // warm-up
    foreach (int balls in ballCounts)
    {
        (double mean, double p95, double max, int live) = MeasureLevel(data, level, balls, measureTicks);
        Console.WriteLine($"| {live} | {mean:0.000} | {p95:0.000} | {max:0.000} | {mean * 2:0.000} |");
    }

    Console.WriteLine();
}

Console.WriteLine("_Sim only (no rendering). The 60 fps check itself runs in the game's stress mode on real hardware._");

static (float Drop, float Speed) LevelProbe(SimConfig config, float distance)
{
    var sim = new SimWorld(config);
    var balls = sim.Ballistics;
    float dt = config.Dt;
    balls.Spawn(new Vector3(0, 1.5f, 0), new Vector3(0, 0, -88f), 1, 1, 0, new Pcg32(1), dt, 0, sim.Events);
    for (int tick = 0; tick < 2000; tick++)
    {
        Vector3 v0 = balls.Pool.Velocity[0];
        balls.Tick(tick, dt, sim.Events);
        Vector3 a = balls.Pool.PrevPosition[0], b = balls.Pool.Position[0];
        if (-b.Z >= distance)
        {
            float f = (distance + a.Z) / (a.Z - b.Z);
            return (1.5f - (a.Y + f * (b.Y - a.Y)), Vector3.Lerp(v0, balls.Pool.Velocity[0], f).Length());
        }
    }

    return (float.NaN, float.NaN);
}

static (float Range, float Angle) MaxRange(SimConfig config)
{
    float best = 0f, bestAngle = 0f;
    for (float angle = 20f; angle <= 40f; angle += 0.5f)
    {
        var sim = new SimWorld(config);
        sim.Collision.Add(new Pb.Sim.Collision.PlaneShape(Vector3.UnitY, 0f), config.Surfaces.Get("turf"), "ground");
        float a = angle * Units.DegreesToRadians;
        sim.Ballistics.Spawn(new Vector3(0, 1.5f, 0), new Vector3(0, MathF.Sin(a), -MathF.Cos(a)) * 88f, 1, 1, 0, new Pcg32(1), config.Dt, 0, sim.Events);
        for (int tick = 0; tick < 5000; tick++)
        {
            sim.Events.Clear();
            sim.Ballistics.Tick(tick, config.Dt, sim.Events);
            float landed = float.NaN;
            foreach (SimEvent e in sim.Events.Items)
            {
                if (e.Type is SimEventType.BallBroke or SimEventType.BallBounced)
                {
                    landed = -e.Position.Z;
                    break;
                }
            }

            if (!float.IsNaN(landed))
            {
                if (landed > best)
                {
                    best = landed;
                    bestAngle = angle;
                }

                break;
            }
        }
    }

    return (best, bestAngle);
}

static (double Mean, double P95, double Max, int Live) Measure(GameData data, int balls, int ticks)
{
    SimConfig c = data.Config;
    var config = new SimConfig
    {
        TickRate = c.TickRate, MatchSeed = c.MatchSeed, BallPoolCapacity = Math.Max(c.BallPoolCapacity, balls * 2),
        Surfaces = c.Surfaces, Projectile = c.Projectile, BreakModel = c.BreakModel, Shot = c.Shot, Fire = c.Fire,
        Loader = c.Loader, Air = c.Air, Movement = c.Movement, Hitboxes = c.Hitboxes,
    };
    var stress = new StressSettings { TargetLiveBalls = balls, MaxSpawnPerTick = Math.Max(16, balls / 40), Cannons = data.Stress.Cannons };
    var sim = new SimWorld(config);
    sim.LoadRange(data.Range, stress);
    sim.Stress!.Enabled = true;
    PlayerState player = sim.AddPlayer(1, 0, data.Range.SpawnPosition, 0f);
    var commands = new InputCommand[1];

    void Step(int tick)
    {
        commands[0] = new InputCommand { Tick = tick, Pitch = 0.04f, Buttons = tick % 15 < 7 ? InputButtons.Fire : InputButtons.None };
        if (player.Marker.Paint.Loader == 0)
        {
            player.Marker.ResetGear();
        }

        sim.Step(commands);
        sim.Events.Clear();
    }

    int t = 0;
    while (t < 4000 && (sim.Ballistics.Pool.Count < balls * 0.97 || t < 480))
    {
        Step(t++);
    }

    var samples = new double[ticks];
    int liveSum = 0;
    for (int i = 0; i < ticks; i++)
    {
        long start = Stopwatch.GetTimestamp();
        Step(t++);
        samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        liveSum += sim.Ballistics.Pool.Count;
    }

    Array.Sort(samples);
    return (samples.Average(), samples[(int)(ticks * 0.95)], samples[^1], liveSum / ticks);
}

// Keeps about `balls` paintballs in flight across a compound level, fired from its opponent spawns
// at random headings and elevations, and times SimWorld.Step (topping up happens between ticks).
static (double Mean, double P95, double Max, int Live) MeasureLevel(GameData data, LevelLayout level, int balls, int ticks)
{
    SimConfig c = data.Config;
    var config = new SimConfig
    {
        TickRate = c.TickRate, MatchSeed = c.MatchSeed, BallPoolCapacity = Math.Max(c.BallPoolCapacity, balls * 2),
        Surfaces = c.Surfaces, Projectile = c.Projectile, BreakModel = c.BreakModel, Shot = c.Shot, Fire = c.Fire,
        Loader = c.Loader, Air = c.Air, Movement = c.Movement, Hitboxes = c.Hitboxes,
    };
    var sim = new SimWorld(config);
    sim.LoadLevel(level);
    sim.AddPlayer(1, 0, level.PlayerSpawn, level.PlayerSpawnYaw);
    var commands = new InputCommand[1];
    var rng = new Pcg32(7);
    uint sequence = 0;
    float speed = c.Shot.MuzzleVelocity;

    void TopUp(int tick)
    {
        int spawns = 0;
        while (sim.Ballistics.Pool.Count < balls && spawns++ < Math.Max(16, balls / 40))
        {
            OpponentSpawn from = level.OpponentSpawns[(int)(rng.NextUInt() % (uint)level.OpponentSpawns.Count)];
            float heading = rng.NextFloat() * MathF.Tau;
            float elevation = (rng.NextFloat() - 0.3f) * 0.6f;
            var direction = new Vector3(MathF.Sin(heading) * MathF.Cos(elevation), MathF.Sin(elevation), MathF.Cos(heading) * MathF.Cos(elevation));
            sim.Ballistics.Spawn(from.Position + new Vector3(0f, 1.5f, 0f), direction * speed, 1, sequence++, 0, rng, config.Dt, tick, sim.Events);
        }
    }

    void Step(int tick)
    {
        commands[0] = new InputCommand { Tick = tick };
        sim.Step(commands);
        sim.Events.Clear();
    }

    int t = 0;
    while (t < 4000 && (sim.Ballistics.Pool.Count < balls * 0.97 || t < 480))
    {
        TopUp(t);
        Step(t++);
    }

    var samples = new double[ticks];
    int liveSum = 0;
    for (int i = 0; i < ticks; i++)
    {
        TopUp(t);
        sim.Events.Clear();
        long start = Stopwatch.GetTimestamp();
        Step(t++);
        samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        liveSum += sim.Ballistics.Pool.Count;
    }

    Array.Sort(samples);
    return (samples.Average(), samples[(int)(ticks * 0.95)], samples[^1], liveSum / ticks);
}

static string RuntimeLabel() =>
    $".NET {Environment.Version}, {Environment.ProcessorCount} cores, {(Debugger.IsAttached ? "debugger" : "no debugger")}";
