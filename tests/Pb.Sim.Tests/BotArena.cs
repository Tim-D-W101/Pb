using System.Collections.Concurrent;
using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Data;
using Pb.Sim.Core;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;
using Xunit;

namespace Pb.Sim.Tests;

/// <summary>
/// Test classes that use <see cref="BotArena"/> run one at a time: arenas share the level's navigation grid (whose
/// searches use scratch buffers) and cover set (with claims).
/// </summary>
[CollectionDefinition(Name)]
public sealed class BotArenaCollection
{
    public const string Name = "Bot arena";
}

/// <summary>
/// A whole round without the engine: a level (Oxbarrow Works unless named), you (player 0, team 0, scripted) and
/// bots (team 1) with real brains, moved over the navigation grid by <see cref="NavGridMover"/>. Every tick runs the
/// same order as the game: commands, movement, sim step, then the step's events to the bots.
/// </summary>
internal sealed class BotArena
{
    private static readonly ConcurrentDictionary<string, Lazy<(LevelLayout Level, NavGrid Grid, CoverSet Cover)>> Shared = new();

    private readonly List<BotBrain?> _brains = new();
    private InputCommand[] _commands = Array.Empty<InputCommand>();

    private BotArena(string tier, ulong seed, string levelId, string? place = null)
    {
        (LevelLayout level, NavGrid grid, CoverSet cover) = SharedFor(levelId, place);
        cover.ReleaseAll();
        Level = level;
        SimConfig config = TestData.Config;
        if (seed != 0)
        {
            config = new SimConfig
            {
                TickRate = config.TickRate, MatchSeed = seed, BallPoolCapacity = config.BallPoolCapacity, Surfaces = config.Surfaces,
                Projectile = config.Projectile, BreakModel = config.BreakModel, Shot = config.Shot, Fire = config.Fire,
                Loader = config.Loader, Air = config.Air, Movement = config.Movement, Hitboxes = config.Hitboxes, Rules = config.Rules,
            };
        }

        Sim = new SimWorld(config);
        Sim.LoadLevel(level);
        Squad = new BotSquad(Sim, TestData.Data.Bots, grid, cover);
        Mover = new NavGridMover(Sim, grid);
        Tier = TestData.Data.Bots.Difficulty[tier];
        Hero = Sim.AddPlayer(0, 0, level.PlayerSpawn, level.PlayerSpawnYaw);
        Hero.Name = "You";
        _brains.Add(null);
    }

    public LevelLayout Level { get; }

    public SimWorld Sim { get; }

    public BotSquad Squad { get; }

    public NavGridMover Mover { get; }

    public DifficultyParams Tier { get; }

    public PlayerState Hero { get; }

    /// <summary>Your commands; idle when null.</summary>
    public Func<int, PlayerState, InputCommand>? HeroScript { get; set; }

    /// <summary>Every event of interest so far (shots, breaks, eliminations, refills).</summary>
    public List<SimEvent> Log { get; } = new();

    public IEnumerable<BotBrain> Bots => _brains.Where(b => b is not null)!;

    public static BotArena Create(string tier = "normal", ulong seed = 0, string level = "oxbarrow_works") => new(tier, seed, level);

    /// <summary>
    /// A level (in one of its places, for a field's other layouts) with its navigation grid and cover set, built once and
    /// shared by every arena on it.
    /// </summary>
    public static (LevelLayout Level, NavGrid Grid, CoverSet Cover) SharedFor(string levelId, string? place = null) =>
        Shared.GetOrAdd(place is null ? levelId : $"{levelId}/{place}", _ => new Lazy<(LevelLayout, NavGrid, CoverSet)>(() =>
        {
            LevelLayout whole = TestData.Data.Levels[levelId];
            LevelLayout level = place is null ? whole : whole.ForPlace(whole.Places.First(p => p.Id == place));
            NavGrid grid = NavGrid.Build(level, TestData.Data.Bots.Navigation);
            CoverSet cover = CoverSet.Build(level, grid, TestData.Config.Movement.StandEyeHeight, TestData.Config.Movement.CrouchEyeHeight);
            return (level, grid, cover);
        })).Value;

    /// <summary>Adds a bot at the named spawn with that spawn's behaviour.</summary>
    public BotBrain AddBot(string spawnId, string? archetype = null) =>
        AddBotAt(Level.OpponentSpawns.First(s => s.Id == spawnId), team: 1, archetype);

    /// <summary>A bot on <paramref name="team"/> at <paramref name="spawn"/>, playing its first role (or <paramref name="archetype"/>).</summary>
    public BotBrain AddBotAt(OpponentSpawn spawn, byte team, string? archetype = null)
    {
        PlayerState state = Sim.AddPlayer(_brains.Count, team, spawn.Position, spawn.Yaw);
        state.Name = spawn.Id;
        ArchetypeParams behaviour = archetype is null ? TestData.Data.Bots.ArchetypeFor(spawn.Roles)! : TestData.Data.Bots.Archetypes[archetype];
        BotBrain brain = Squad.Add(state, behaviour, Tier, spawn);
        _brains.Add(brain);
        return brain;
    }

    /// <summary>Puts a brain in your slot (team 0) with the given behaviour, starting from where you stand.</summary>
    public BotBrain HeroBot(string archetype = "hunter")
    {
        var spawn = new OpponentSpawn { Id = "you", Position = Hero.Position, Yaw = Hero.Yaw, Roles = new[] { archetype } };
        BotBrain brain = Squad.Add(Hero, TestData.Data.Bots.Archetypes[archetype], Tier, spawn);
        _brains[0] = brain;
        return brain;
    }

    /// <summary>
    /// A speedball point on the Sports Ground (in <paramref name="place"/>'s layout): you and <paramref name="perSide"/> − 1
    /// bots on the south side against <paramref name="perSide"/> on the north, everyone in their start box, every bot
    /// playing speedball (you too, unless <paramref name="heroBot"/> is false). <see cref="StartSpeedball"/> starts it.
    /// </summary>
    public static BotArena Speedball(int perSide = 5, string tier = "normal", ulong seed = 0, string place = "whole", bool heroBot = true)
    {
        var arena = new BotArena(tier, seed, "sports_ground", place);
        FieldSpec field = arena.Level.Field!;
        arena.Hero.Position = field.StartOf(0, 0, perSide);
        arena.Hero.Yaw = FieldSpec.StartYaw(0);
        if (heroBot)
        {
            arena.HeroBot("speedball");
        }

        for (int side = 0; side < 2; side++)
        {
            for (int i = side == 0 ? 1 : 0; i < perSide; i++)
            {
                var spawn = new OpponentSpawn
                {
                    Id = $"{(side == 0 ? "south" : "north")}{i}", Position = field.StartOf(side, i, perSide), Yaw = FieldSpec.StartYaw(side),
                    Roles = new[] { "speedball" },
                };
                arena.AddBotAt(spawn, (byte)side, "speedball");
            }
        }

        return arena;
    }

    /// <summary>Starts a speedball point: the briefing over, the countdown running (the horn comes by itself).</summary>
    public BotArena StartSpeedball(int pods = 2)
    {
        SpeedballRules rules = Sim.Config.Rules.Speedball;
        Sim.StartMatch(new MatchSetup
        {
            HeroId = 0, Mode = MatchModeKind.Teams, Format = MatchFormat.Speedball, TimeLimit = rules.PointTime, Countdown = rules.Countdown,
            StartPods = pods, BotPods = pods, Pickups = false,
        });
        Sim.GoLive();
        _commands = new InputCommand[Sim.Players.Count];
        return this;
    }

    /// <summary>Starts the round (gear, stats) and goes live.</summary>
    public BotArena Start(int heroPods = 2, int botPods = 2, float timeLimit = 900f, MatchModeKind mode = MatchModeKind.Solo,
        ObjectiveKind objective = ObjectiveKind.Eliminate)
    {
        Sim.StartMatch(new MatchSetup
        {
            HeroId = 0, Mode = mode, TimeLimit = timeLimit, StartPods = heroPods, BotPods = botPods, Pickups = true, Objective = objective,
        });
        Sim.GoLive();
        _commands = new InputCommand[Sim.Players.Count];
        return this;
    }

    public void Run(int ticks, Func<bool>? until = null)
    {
        for (int t = 0; t < ticks; t++)
        {
            Tick();
            if (until?.Invoke() == true)
            {
                return;
            }
        }
    }

    public void Tick()
    {
        int tick = Sim.Tick;
        float dt = Sim.Dt;
        for (int i = 0; i < Sim.Players.Count; i++)
        {
            PlayerState p = Sim.Players[i];
            BotBrain? brain = _brains[i];
            InputCommand cmd = brain is not null
                ? brain.Think(tick)
                : p.Present ? HeroScript?.Invoke(tick, p) ?? new InputCommand { Tick = tick, Yaw = p.Yaw, Pitch = p.Pitch } : default;
            cmd.Tick = tick;
            if (p.Present)
            {
                Mover.Step(p, cmd, dt);
            }

            _commands[i] = cmd;
        }

        Sim.Step(_commands);
        ReadOnlySpan<SimEvent> events = Sim.Events.Items;
        Squad.HearAll(events);
        foreach (SimEvent e in events)
        {
            if (e.Type is SimEventType.ShotFired or SimEventType.PlayerEliminated or SimEventType.RefillStarted or SimEventType.RoundEnded)
            {
                Log.Add(e);
            }
        }

        Sim.Events.Clear();
    }

    /// <summary>A clear standing spot about <paramref name="distance"/> in front of a bot, with a clear line between the chests.</summary>
    public Vector3 SpotInFront(BotBrain bot, float distance)
    {
        PlayerState me = bot.Self;
        foreach (float d in new[] { distance, distance * 0.85f, distance * 1.15f, distance * 0.7f })
        {
            foreach (float turn in new[] { 0f, 0.25f, -0.25f, 0.5f, -0.5f, 0.75f, -0.75f, 1.0f, -1.0f, 1.2f, -1.2f })
            {
                Vector3 at = me.Position + ViewAngles.FlatForward(me.Yaw + turn) * d;
                if (!Squad.Grid.TrySnap(at, out Vector3 spot) || Vector3.Distance(spot with { Y = 0f }, at with { Y = 0f }) > 0.6f ||
                    MathF.Abs(spot.Y - me.Position.Y) > 0.3f)
                {
                    continue;
                }

                if (!Sim.Collision.SweepSphere(spot + new Vector3(0f, 1.2f, 0f), me.Position + new Vector3(0f, 1.2f, 0f), 0f, out SweepHit _))
                {
                    return spot;
                }
            }
        }

        throw new InvalidOperationException($"no clear spot {distance} m in front of {me.Name}");
    }

    /// <summary>Puts you at <paramref name="at"/> facing <paramref name="towards"/>.</summary>
    public void PlaceHero(Vector3 at, Vector3 towards)
    {
        Hero.Position = at;
        Hero.Yaw = MathF.Atan2(-(towards.X - at.X), -(towards.Z - at.Z));
    }

    /// <summary>
    /// A script that aims at <paramref name="target"/>'s chest (or <paramref name="miss"/> metres to its side,
    /// to put it under fire without hitting it) and pulls the trigger every <paramref name="interval"/> ticks.
    /// </summary>
    public Func<int, PlayerState, InputCommand> ShootAt(PlayerState target, int interval, int startTick = 0, float miss = 0f) => (tick, me) =>
    {
        Vector3 chest = target.Position + new Vector3(0f, target.EyeHeight * 0.72f, 0f);
        if (miss != 0f)
        {
            Vector3 across = Vector3.Normalize(Vector3.Cross(chest - me.EyePosition, Vector3.UnitY));
            chest += across * miss;
        }

        (float yaw, float pitch) = BotAim.Solve(Sim.Config, me.EyePosition, chest, Vector3.Zero);
        bool pull = tick >= startTick && (tick - startTick) % interval == 0 && target.Alive;
        return new InputCommand { Tick = tick, Yaw = yaw, Pitch = pitch, Buttons = pull ? InputButtons.Fire : InputButtons.None };
    };

    public int ShotsBy(int playerId) => Log.Count(e => e.Type == SimEventType.ShotFired && e.PlayerId == playerId);
}
