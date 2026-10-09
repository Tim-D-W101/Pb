using System.Diagnostics;
using System.Numerics;
using Pb.Net;
using Pb.Net.Client;
using Pb.Net.Protocol;
using Pb.Net.Server;
using Pb.Net.Transport;
using Pb.Sim;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Pb.Sim.Players;

/// <summary>
/// Phase 4's costs (architecture §16.14).
/// <list type="bullet">
/// <item>The server's tick with ten players and 1,000 balls in the air, nine of the players joined over the in-memory
/// network: its step (taking in their commands first), then the snapshots and events it packs and sends them.</item>
/// <item>A correction's replay on a joining copy: 12 ticks (the round trip at 100 ms), each moving the player by the
/// movement rules and replaying its look and marker. In the game, the engine's collide-and-slide adds to each tick;
/// joining copies print what their replays took.</item>
/// </list>
/// </summary>
internal static class NetBench
{
    public const int People = 9;

    public readonly record struct ServerCost(double Step, double StepP95, double Pack, double PackP95, double KBytesPerClientPerSecond, int Live);

    /// <param name="balls">Kept in the air (fired from the level's opponent spawns), with everyone standing still; or 0, and
    /// everyone fires at the cap instead (a full round's traffic, which the 25 KB/s budget is for).</param>
    public static ServerCost ServerTick(GameData data, LevelLayout level, int balls, int ticks)
    {
        bool everyoneFires = balls == 0;
        SimConfig config = Roomy(data.Config, balls);
        float dt = config.Dt;
        NetSettings settings = NetSettings.Load(FileSystemDataSource.FindRepoData());
        double now = 0.0;
        var network = new LoopbackNetwork();
        var server = new NetServer(network.Listen(), settings, new ServerIdentity("Bench", "bench", "bench"), () => now, dt);
        var clients = new NetClient[People];
        var seqs = new int[People];
        for (int i = 0; i < People; i++)
        {
            clients[i] = new NetClient(network.Connect(), settings, () => now,
                new HelloMessage { Build = "bench", DataHash = "bench", Name = $"Player {i + 1}" }, config.TickRate);
            clients[i].Join();
            seqs[i] = -1;
        }

        for (int i = 0; i < 60; i++)
        {
            now += dt;
            server.Poll();
            foreach (NetClient c in clients)
            {
                c.Poll();
            }
        }

        // Everyone for themselves, so every tick pays for all ten sets of hitboxes: nine people and a bot, away from the
        // spawns the balls are fired from.
        List<Vector3> spots = level.Pickups.Select(x => x.Position).Concat(level.Patrols.SelectMany(r => r.Points)).ToList();
        var setup = new RoundSetupMessage
        {
            Round = 1, LevelId = "", ModeId = "ffa", Size = People + 1, TierId = "normal", Seed = 7, TimeLimit = 3600f, StartPods = 3, BotPods = 2,
        };
        for (int p = 0; p <= People; p++)
        {
            setup.Roster.Add(new RosterEntry
            {
                PlayerId = p, Team = (byte)p, Name = $"Player {p + 1}", Person = p < People, Position = p == 0 ? level.PlayerSpawn : spots[(p - 1) % spots.Count],
                Yaw = 0f,
            });
        }

        SimWorld sim = RoundWorld.Build(config, setup, level);
        int given = 0;
        var ids = new Dictionary<ClientLink, int>();
        server.BeginRound(sim, setup, link =>
        {
            if (!ids.TryGetValue(link, out int id))
            {
                id = given < People ? given++ : -1;
                ids[link] = id;
            }

            return id;
        });
        var commands = new InputCommand[sim.Players.Count];
        var rng = new Pcg32(7);
        uint sequence = 0;

        void ClientsTick()
        {
            for (int i = 0; i < clients.Length; i++)
            {
                NetClient c = clients[i];
                c.Poll();
                if (c.TakeRoundSetup() is { } s)
                {
                    c.BeginRound(s.Round, new WorldFields(sim.Players.Count, sim.Doors.Count, NetServer.Grid(sim)));
                    c.SendLoaded(s.Round);
                    seqs[i] = 0;
                }

                if (seqs[i] >= 0)
                {
                    c.AdvanceClock(dt);
                    int seq = seqs[i]++;
                    c.SendCommand(seq, Pressed(seq, i), (int)Math.Floor(c.RenderTick));
                }
            }
        }

        void ServerTick()
        {
            server.Poll();
            for (int i = 0; i < sim.Players.Count; i++)
            {
                PlayerState p = sim.Players[i];
                if (server.LinkOf(p.Id) is null || !server.TryCommand(p.Id, sim.Tick, out commands[i]))
                {
                    commands[i] = Pressed(sim.Tick, i);
                }
            }

            sim.Step(commands);
            foreach (PlayerState p in sim.Players)
            {
                p.Alive = true;
                p.Present = true;
            }
        }

        // Standing still, or turning slowly while pulling the trigger every other tick (a pull a shot: the marker fires at its cap).
        InputCommand Pressed(int tick, int who) => everyoneFires
            ? new InputCommand { Tick = tick, Yaw = who * 0.6f + tick * 0.004f, Pitch = 0.05f, Buttons = tick % 2 == 0 ? InputButtons.Fire : InputButtons.None }
            : new InputCommand { Tick = tick };

        void TopUp(int tick)
        {
            if (everyoneFires)
            {
                return;
            }

            int spawns = 0;
            while (sim.Ballistics.Pool.Count < balls && spawns++ < Math.Max(16, balls / 40) * 10)
            {
                OpponentSpawn from = level.OpponentSpawns[(int)(rng.NextUInt() % (uint)level.OpponentSpawns.Count)];
                float heading = rng.NextFloat() * MathF.Tau;
                float elevation = (rng.NextFloat() - 0.3f) * 0.6f;
                var direction = new Vector3(MathF.Sin(heading) * MathF.Cos(elevation), MathF.Sin(elevation), MathF.Cos(heading) * MathF.Cos(elevation));
                sim.Ballistics.Spawn(from.Position + new Vector3(0f, 1.5f, 0f), direction * config.Shot.MuzzleVelocity, 1, sequence++, 0, rng, dt, tick,
                    sim.Events);
            }
        }

        // Everyone builds the round and it goes live; then the balls fill up to the count.
        for (int i = 0; i < 600 && !server.AllLoaded(); i++)
        {
            now += dt;
            ServerTick();
            server.AfterStep(sim);
            sim.Events.Clear();
            ClientsTick();
        }

        sim.GoLive();
        for (int t = 0; t < 4000 && (sim.Ballistics.Pool.Count < balls * 0.97 || t < (everyoneFires ? 240 : 480)); t++)
        {
            now += dt;
            TopUp(sim.Tick);
            ServerTick();
            server.AfterStep(sim);
            sim.Events.Clear();
            ClientsTick();
        }

        var steps = new double[ticks];
        var packs = new double[ticks];
        long bytesFrom = server.BytesSent;
        int liveSum = 0;
        for (int i = 0; i < ticks; i++)
        {
            now += dt;
            TopUp(sim.Tick);
            long start = Stopwatch.GetTimestamp();
            ServerTick();
            long stepped = Stopwatch.GetTimestamp();
            server.AfterStep(sim);
            long packed = Stopwatch.GetTimestamp();
            steps[i] = Stopwatch.GetElapsedTime(start, stepped).TotalMilliseconds;
            packs[i] = Stopwatch.GetElapsedTime(stepped, packed).TotalMilliseconds;
            liveSum += sim.Ballistics.Pool.Count;
            sim.Events.Clear();
            ClientsTick();
        }

        double perClient = (server.BytesSent - bytesFrom) / 1000.0 / (ticks * dt) / People;
        Array.Sort(steps);
        Array.Sort(packs);
        return new ServerCost(steps.Average(), steps[(int)(ticks * 0.95)], packs.Average(), packs[(int)(ticks * 0.95)], perClient, liveSum / ticks);
    }

    /// <summary>A correction's replay of <paramref name="length"/> ticks, <paramref name="replays"/> times: mean and 99th percentile (ms).</summary>
    public static (double Mean, double P99) Replay(GameData data, LevelLayout level, int length, int replays)
    {
        SimConfig config = data.Config;
        var setup = new RoundSetupMessage { Round = 1, LevelId = "", ModeId = "ffa", Size = 2, TierId = "normal", Seed = 7, TimeLimit = 3600f, StartPods = 3 };
        setup.Roster.Add(new RosterEntry { PlayerId = 0, Team = 0, Name = "You", Person = true, Position = level.PlayerSpawn, Yaw = level.PlayerSpawnYaw });
        setup.Roster.Add(new RosterEntry { PlayerId = 1, Team = 1, Name = "Other", Person = false, Position = level.OpponentSpawns[0].Position, Yaw = 0f });
        SimWorld sim = RoundWorld.Build(config, setup, level);
        sim.GoLive();
        PlayerState you = sim.Players[0];
        var pressed = new InputCommand[length];
        for (int k = 0; k < length; k++)
        {
            pressed[k] = new InputCommand
            {
                Tick = k, Move = new Vector2(0.3f, 1f), Yaw = you.Yaw + k * 0.01f, Pitch = 0.05f, Buttons = k % 6 == 0 ? InputButtons.Fire : InputButtons.Sprint,
            };
        }

        var samples = new double[replays];
        Vector3 from = you.Position;
        for (int r = 0; r < replays; r++)
        {
            you.Position = from;
            you.Velocity = Vector3.Zero;
            long start = Stopwatch.GetTimestamp();
            for (int k = 0; k < length; k++)
            {
                MovementResult moved = MovementModel.Step(you, pressed[k], config.Movement, config.Dt, true, sim.Collision, sim.Ladders);
                you.Velocity = new Vector3(moved.HorizontalVelocity.X, 0f, moved.HorizontalVelocity.Z);
                you.Position += you.Velocity * config.Dt;
                sim.ReplayLocal(you, pressed[k], sim.Time + k * config.Dt);
            }

            samples[r] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Array.Sort(samples);
        return (samples.Average(), samples[(int)(replays * 0.99)]);
    }

    private static SimConfig Roomy(SimConfig c, int balls) => new()
    {
        TickRate = c.TickRate, MatchSeed = c.MatchSeed, BallPoolCapacity = Math.Max(c.BallPoolCapacity, balls * 2), Surfaces = c.Surfaces,
        Projectile = c.Projectile, BreakModel = c.BreakModel, Shot = c.Shot, Fire = c.Fire, Loader = c.Loader, Air = c.Air, Movement = c.Movement,
        Hitboxes = c.Hitboxes, Rules = c.Rules,
    };
}
