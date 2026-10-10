using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Pb.Sim;
using Pb.Sim.AI;
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
    // Phase 2 budget: a tick with ten players (hitboxes posed and recorded, balls tested against them) and 1,000 balls.
    (double m10, double p10, double x10, int l10) = MeasureLevel(data, level, 1000, measureTicks, players: 10);
    Console.WriteLine($"With 10 players in the round and {l10} live balls: mean {m10:0.000} ms/tick, p95 {p10:0.000}, max {x10:0.000} " +
                      $"(budget 0.5 ms: {(m10 <= 0.5 ? "within" : "OVER")}).");
    Console.WriteLine();
}

Console.WriteLine("_Sim only (no rendering). The 60 fps check itself runs in the game's stress mode on real hardware._");
Console.WriteLine();
Console.WriteLine($"## Bots ({RuntimeLabel()})");
Console.WriteLine();
foreach ((string id, LevelLayout level) in data.Levels)
{
    var probe = new SimWorld(config);
    probe.LoadLevel(level);
    var watch = Stopwatch.StartNew();
    BotSquad built = BotSquad.ForLevel(probe, data.Bots, level);
    double buildMs = watch.Elapsed.TotalMilliseconds;
    Console.WriteLine($"**{level.DisplayName}**: navigation grid of {built.Grid.SpanCount:N0} places and {built.Cover.Points.Count} cover points, built at load in {buildMs:0} ms.");
    Console.WriteLine();
    Console.WriteLine("| Bots (hard), with you in the yard | Mean ms/tick (brains) | p95 ms/tick | Max ms/tick | Path searches |");
    Console.WriteLine("|---|---|---|---|---|");
    AreaEntryDef entry = data.Areas.Areas.First(l => l.Id == id);
    string[] roster = (entry.Roster ?? Array.Empty<string>()).Take(data.Config.Rules.MaxPlayers - 1).ToArray();
    if (roster.Length > 0 && entry.Tiers is { Length: > 0 } tiers)
    {
        string hard = tiers[^1].Bots;
        foreach (bool freeForAll in new[] { false, true })
        {
            (double mean, double p95, double max, int searches) = MeasureBots(data, level, roster, hard, freeForAll, quick ? 1200 : 3600);
            string who = freeForAll ? "free-for-all: everyone against everyone" : "solo: one squad against you";
            Console.WriteLine($"| {roster.Length}, {who} | {mean:0.000} | {p95:0.000} | {max:0.000} | {searches} |");
        }
    }

    Console.WriteLine();
}

// Phase 4 (architecture §16.14): the server's tick for a full round played over the network, and a correction's replay.
Console.WriteLine($"## Network ({RuntimeLabel()})");
Console.WriteLine();
Console.WriteLine($"The server's tick with ten players, {NetBench.People} of them joined over the in-memory network: its step (taking in their " +
                  "commands first), then packing and sending their snapshots and events. With 1,000 balls in the air, everyone standing:");
Console.WriteLine();
Console.WriteLine("| Area | Live balls | Step, mean ms | p95 | Packing for nine, mean ms | p95 | Sent to each, KB/s |");
Console.WriteLine("|---|---|---|---|---|---|---|");
// Budgets: the step is the sim's (0.5 ms a tick, §10); packing gets as much again, so a host's two ticks a frame keep the
// network within 1 ms of the frame's 3 ms for UI, audio, AI and the network. In a full round, everyone firing at the cap,
// each player is sent at most 25 KB/s (§16.5); with 1,000 balls the impacts fill every packet to its budget instead.
const double stepBudget = 0.5, packBudget = 0.5, sendBudget = 25.0, replayBudget = 0.5;
bool netWithin = true;
var firing = new List<(string Area, NetBench.ServerCost Cost)>();
foreach ((string id, LevelLayout level) in data.Levels)
{
    NetBench.ServerTick(data, level, 1000, 240); // warm-up
    NetBench.ServerCost cost = NetBench.ServerTick(data, level, 1000, measureTicks);
    netWithin &= cost.Step <= stepBudget && cost.Pack <= packBudget;
    Console.WriteLine($"| {level.DisplayName} | {cost.Live} | {cost.Step:0.000} | {cost.StepP95:0.000} | {cost.Pack:0.000} | {cost.PackP95:0.000} | " +
                      $"{cost.KBytesPerClientPerSecond:0.0} |");
    firing.Add((level.DisplayName, NetBench.ServerTick(data, level, 0, Math.Min(measureTicks, 1800))));
}

Console.WriteLine();
Console.WriteLine("A full round's traffic, all ten firing at the cap:");
Console.WriteLine();
Console.WriteLine("| Area | Live balls | Step, mean ms | Packing for nine, mean ms | Sent to each, KB/s |");
Console.WriteLine("|---|---|---|---|---|");
foreach ((string area, NetBench.ServerCost cost) in firing)
{
    netWithin &= cost.Step <= stepBudget && cost.Pack <= packBudget && cost.KBytesPerClientPerSecond <= sendBudget;
    Console.WriteLine($"| {area} | {cost.Live} | {cost.Step:0.000} | {cost.Pack:0.000} | {cost.KBytesPerClientPerSecond:0.0} |");
}

Console.WriteLine();
LevelLayout replayLevel = data.Levels.Values.First();
NetBench.Replay(data, replayLevel, 12, 200); // warm-up
(double replayMean, double replayP99) = NetBench.Replay(data, replayLevel, 12, quick ? 2000 : 10000);
netWithin &= replayMean <= replayBudget;
Console.WriteLine($"A correction's replay on a joining copy, 12 ticks (the round trip at 100 ms) of moving and the marker: mean {replayMean:0.000} ms, " +
                  $"p99 {replayP99:0.000} ms. In the game the engine's collide-and-slide adds to each tick; joining copies print their own.");
Console.WriteLine();
Console.WriteLine($"Budgets: step {stepBudget} ms, packing {packBudget} ms, {sendBudget} KB/s to each player in a full round, replay {replayBudget} ms: " +
                  $"{(netWithin ? "within" : "OVER")}.");
Console.WriteLine();

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
        Loader = c.Loader, Air = c.Air, Movement = c.Movement, Hitboxes = c.Hitboxes, Rules = c.Rules,
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
static (double Mean, double P95, double Max, int Live) MeasureLevel(GameData data, LevelLayout level, int balls, int ticks, int players = 1)
{
    SimConfig c = data.Config;
    var config = new SimConfig
    {
        TickRate = c.TickRate, MatchSeed = c.MatchSeed, BallPoolCapacity = Math.Max(c.BallPoolCapacity, balls * 2),
        Surfaces = c.Surfaces, Projectile = c.Projectile, BreakModel = c.BreakModel, Shot = c.Shot, Fire = c.Fire,
        Loader = c.Loader, Air = c.Air, Movement = c.Movement, Hitboxes = c.Hitboxes, Rules = c.Rules,
    };
    var sim = new SimWorld(config);
    sim.LoadLevel(level);
    sim.AddPlayer(1, 0, level.PlayerSpawn, level.PlayerSpawnYaw);
    // The others stand away from the spawns the balls are fired from: at the pickups and patrol points, or (a level with
    // neither, like the field) in a row from the player starts.
    List<Vector3> spots = level.Pickups.Select(x => x.Position).Concat(level.Patrols.SelectMany(r => r.Points)).ToList();
    bool row = spots.Count == 0;
    if (row)
    {
        spots.AddRange(level.PlayerSpawns.Select(s => s.Position).DefaultIfEmpty(level.PlayerSpawn));
    }

    for (int p = 1; p < players; p++)
    {
        Vector3 at = spots[(p - 1) % spots.Count] + (row ? new Vector3(1.2f * p, 0f, 0f) : Vector3.Zero);
        sim.AddPlayer(1 + p, 1, at, 0f);
    }

    var commands = new InputCommand[players];
    var rng = new Pcg32(7);
    uint sequence = 0;
    float speed = c.Shot.MuzzleVelocity;

    void TopUp(int tick)
    {
        int spawns = 0;
        while (sim.Ballistics.Pool.Count < balls && spawns++ < Math.Max(16, balls / 40) * players)
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
        for (int p = 0; p < commands.Length; p++)
        {
            commands[p] = new InputCommand { Tick = tick, Yaw = sim.Players[p].Yaw };
        }

        sim.Step(commands);
        sim.Events.Clear();
        foreach (PlayerState p in sim.Players)
        {
            // Everyone stays in, so every tick pays for all ten sets of hitboxes.
            p.Alive = true;
            p.Present = true;
        }
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

// A round with the hardest tier's bots at the roster's spawns and you standing in the open (hitboxes off so
// it goes on): the time all the bots' brains take per tick, moved over the navigation grid as in the sim
// tests. In free-for-all every bot is a team of its own, so each watches and fights all the others.
static (double Mean, double P95, double Max, int Searches) MeasureBots(GameData data, LevelLayout level, string[] roster, string difficulty,
    bool freeForAll, int ticks)
{
    var sim = new SimWorld(data.Config);
    sim.LoadLevel(level);
    BotSquad squad = BotSquad.ForLevel(sim, data.Bots, level);
    var mover = new NavGridMover(sim, squad.Grid);
    PlayerState you = sim.AddPlayer(0, 0, level.PlayerSpawn, level.PlayerSpawnYaw);
    var brains = new List<BotBrain>();
    foreach (string spawnId in roster)
    {
        OpponentSpawn spawn = level.OpponentSpawns.First(s => s.Id == spawnId);
        PlayerState bot = sim.AddPlayer(brains.Count + 1, freeForAll ? (byte)(brains.Count + 1) : (byte)1, spawn.Position, spawn.Yaw);
        bot.Name = spawn.Id;
        brains.Add(squad.Add(bot, data.Bots.ArchetypeFor(spawn.Roles)!, data.Bots.Difficulty[difficulty], spawn));
    }

    // You: in the open, in sight of a sentry, firing just wide of whichever bot you can see.
    OpponentSpawn yard = level.OpponentSpawns.FirstOrDefault(s => s.Id == "yard_east") ?? level.OpponentSpawns.First(s => s.Roles.Contains("sentry"));
    you.Position = ClearSpot(sim, squad.Grid, yard, 18f);
    you.Yaw = MathF.Atan2(-(yard.Position.X - you.Position.X), -(yard.Position.Z - you.Position.Z));
    sim.StartMatch(new Pb.Sim.Match.MatchSetup { HeroId = 0, TimeLimit = 3600f, StartPods = 3, BotPods = 3, Pickups = true });
    sim.GoLive();
    sim.PlayerHits.Enabled = false;
    var commands = new InputCommand[sim.Players.Count];
    var samples = new double[ticks];
    int searchesBefore = 0;
    for (int t = 0; t < ticks + 600; t++)
    {
        int tick = sim.Tick;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < brains.Count; i++)
        {
            commands[i + 1] = brains[i].Think(tick);
        }

        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        commands[0] = YouFire(sim, you, tick);
        for (int i = 0; i < commands.Length; i++)
        {
            if (sim.Players[i].Present)
            {
                mover.Step(sim.Players[i], commands[i], sim.Dt);
            }
        }

        sim.Step(commands);
        squad.HearAll(sim.Events.Items);
        sim.Events.Clear();
        if (t == 600)
        {
            searchesBefore = squad.SearchesDone;
        }

        if (t >= 600)
        {
            samples[t - 600] = ms;
        }
    }

    Array.Sort(samples);
    return (samples.Average(), samples[(int)(ticks * 0.95)], samples[^1], squad.SearchesDone - searchesBefore);
}

static Vector3 ClearSpot(SimWorld sim, NavGrid grid, OpponentSpawn from, float distance)
{
    foreach (float turn in new[] { 0f, 0.3f, -0.3f, 0.6f, -0.6f, 0.9f, -0.9f })
    {
        Vector3 at = from.Position + ViewAngles.FlatForward(from.Yaw + turn) * distance;
        if (grid.TrySnap(at, out Vector3 spot) &&
            !sim.Collision.SweepSphere(spot + new Vector3(0f, 1.2f, 0f), from.Position + new Vector3(0f, 1.2f, 0f), 0f, out _))
        {
            return spot;
        }
    }

    return from.Position + ViewAngles.FlatForward(from.Yaw) * distance;
}

static InputCommand YouFire(SimWorld sim, PlayerState you, int tick)
{
    PlayerState? target = null;
    float best = float.MaxValue;
    foreach (PlayerState p in sim.Players)
    {
        Vector3 chest = p.Position + new Vector3(0f, p.EyeHeight * 0.72f, 0f);
        float d = Vector3.Distance(you.Position, p.Position);
        if (p.Team != you.Team && p.Present && d < best && !sim.Collision.SweepSphere(you.EyePosition, chest, 0f, out _))
        {
            best = d;
            target = p;
        }
    }

    if (target is null)
    {
        return new InputCommand { Tick = tick, Yaw = you.Yaw, Pitch = you.Pitch };
    }

    Vector3 point = target.Position + new Vector3(0f, target.EyeHeight * 0.72f, 0f);
    point += Vector3.Normalize(Vector3.Cross(point - you.EyePosition, Vector3.UnitY)) * 1.4f;
    (float yaw, float pitch) = BotAim.Solve(sim.Config, you.EyePosition, point, Vector3.Zero);
    return new InputCommand { Tick = tick, Yaw = yaw, Pitch = pitch, Buttons = tick % 60 == 0 ? InputButtons.Fire : InputButtons.None };
}

static string RuntimeLabel() =>
    $".NET {Environment.Version}, {Environment.ProcessorCount} cores, {(Debugger.IsAttached ? "debugger" : "no debugger")}";
