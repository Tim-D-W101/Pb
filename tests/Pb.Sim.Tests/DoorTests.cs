using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Players;
using Xunit;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>
/// Doors (M3.2), on the guardhouse by Oxbarrow Works' main gate: a panel door in its east wall (x = 4, centred on
/// z = 34.5), hinged at its north edge and opening inwards (west).
/// </summary>
[Collection(BotArenaCollection.Name)]
public class DoorTests
{
    private const int Second = 120;
    private static readonly Vector3 Doorway = new(4f, 0f, 34.5f);
    private readonly ITestOutputHelper _out;

    public DoorTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static (SimWorld Sim, int Leaf) Guardhouse(ulong? seed = null)
    {
        var sim = new SimWorld(TestData.Config, seed);
        sim.LoadLevel(TestData.Data.Levels["oxbarrow_works"]);
        return (sim, LeafAt(sim, Doorway));
    }

    private static int LeafAt(SimWorld sim, Vector3 doorway)
    {
        for (int i = 0; i < sim.Doors.Count; i++)
        {
            if (Vector3.Distance(sim.Doors[i].ShutCenter with { Y = 0f }, doorway) < 0.3f)
            {
                return i;
            }
        }

        throw new InvalidOperationException("no door leaf at the guardhouse doorway");
    }

    /// <summary>Steps <paramref name="ticks"/> with each player's command from <paramref name="command"/>.</summary>
    private static List<SimEvent> Run(SimWorld sim, int ticks, Func<int, PlayerState, InputCommand> command)
    {
        var events = new List<SimEvent>();
        var commands = new InputCommand[sim.Players.Count];
        for (int t = 0; t < ticks; t++)
        {
            for (int i = 0; i < commands.Length; i++)
            {
                commands[i] = command(t, sim.Players[i]);
                commands[i].Tick = sim.Tick;
            }

            sim.Step(commands);
            foreach (SimEvent e in sim.Events.Items)
            {
                events.Add(e);
            }

            sim.Events.Clear();
        }

        return events;
    }

    /// <summary>Facing west, towards the guardhouse door from its yard side.</summary>
    private const float West = MathF.PI * 0.5f;

    private static InputCommand Press(PlayerState p, bool down) =>
        new() { Yaw = p.Yaw, Pitch = p.Pitch, Buttons = down ? InputButtons.Interact : InputButtons.None };

    [Fact]
    public void The_level_hangs_its_doors_where_the_files_say()
    {
        LevelLayout level = TestData.Data.Levels["oxbarrow_works"];
        // 21 doorways with leaves, the office's front door a pair.
        Assert.Equal(22, level.Doors.Count);
        Assert.Equal(2, level.Doors.Count(d => d.Partner >= 0));

        (SimWorld sim, int leaf) = Guardhouse();
        DoorSpec s = sim.Doors[leaf];
        Assert.Equal("panel", s.Kind.Id);
        Assert.Equal(0.9f, s.Width, 3);
        Assert.Equal(2.1f, s.Height, 3);
        Assert.True(Vector3.Distance(s.Side, -Vector3.UnitX) < 1e-3f, $"opens towards {s.Side}, not inwards (west)");
        Assert.Equal(ApertureKind.Door, level.Apertures[s.Aperture].Kind);

        // Shut, it stands in the doorway; open, it's swung round into the room.
        sim.Doors.SetOpen(leaf, 0f);
        Assert.True(Vector3.Distance(sim.Doors.Shape(leaf).Center with { Y = 0f }, Doorway) < 0.15f);
        sim.Doors.SetOpen(leaf, 1f);
        Vector3 open = sim.Doors.Shape(leaf).Center;
        Assert.True(open.X < Doorway.X - 0.35f && open.Z < Doorway.Z, $"open leaf at {open}");
    }

    [Fact]
    public void A_shut_door_stops_paint_and_sight_and_an_open_one_does_not()
    {
        (SimWorld sim, int leaf) = Guardhouse();
        Vector3 yard = new(7f, 1.2f, 34.5f), room = new(2.5f, 1.2f, 34.5f);
        float ball = TestData.Config.Projectile.Radius;

        sim.Doors.SetOpen(leaf, 0f);
        Assert.True(sim.Collision.SweepSphere(yard, room, ball, out SweepHit shut));
        Assert.Equal(sim.Doors.ColliderOf(leaf), shut.ColliderId);
        Assert.Equal(TestData.Config.Surfaces.Get("wood"), shut.Surface);

        sim.Doors.SetOpen(leaf, 1f);
        bool hit = sim.Collision.SweepSphere(yard, room, ball, out SweepHit open);
        Assert.False(hit && open.ColliderId == sim.Doors.ColliderOf(leaf), "the open door still stopped the line");

        // Things built once from the level as it stands can leave the doors out.
        sim.Doors.SetOpen(leaf, 0f);
        sim.Collision.SkipDynamic = true;
        bool skipped = sim.Collision.SweepSphere(yard, room, ball, out SweepHit through);
        Assert.False(skipped && through.ColliderId == sim.Doors.ColliderOf(leaf));
    }

    [Fact]
    public void A_tap_on_interact_swings_the_door_you_face_all_the_way_and_nothing_else()
    {
        (SimWorld sim, int leaf) = Guardhouse();
        sim.Doors.SetOpen(leaf, 0f);
        PlayerState you = sim.AddPlayer(0, 0, new Vector3(5.1f, 0f, 34.5f), West);

        Run(sim, Second, (t, p) => Press(p, t == 2));
        Assert.Equal(1f, sim.Doors.Open(leaf), 3);

        // Again: it shuts.
        Run(sim, Second, (t, p) => Press(p, t == 2));
        Assert.Equal(0f, sim.Doors.Open(leaf), 3);

        // Facing away, or too far off, nothing happens.
        you.Yaw = -West;
        Run(sim, Second, (t, p) => Press(p, t == 2));
        Assert.Equal(0f, sim.Doors.Open(leaf), 3);
        you.Yaw = West;
        you.Position = new Vector3(7.5f, 0f, 34.5f);
        Run(sim, Second, (t, p) => Press(p, t == 2));
        Assert.Equal(0f, sim.Doors.Open(leaf), 3);
    }

    [Fact]
    public void Holding_interact_eases_the_door_open_and_letting_go_leaves_it_there()
    {
        (SimWorld sim, int leaf) = Guardhouse();
        sim.Doors.SetOpen(leaf, 0f);
        sim.AddPlayer(0, 0, new Vector3(5.1f, 0f, 34.5f), West);
        DoorRules rules = TestData.Config.Rules.Doors;

        Run(sim, Second, (t, p) => Press(p, t < Second));
        float eased = sim.Doors.Open(leaf);
        Run(sim, Second, (t, p) => Press(p, false));
        _out.WriteLine($"held 1 s: {eased:0.000} open, then {sim.Doors.Open(leaf):0.000} after letting go");
        Assert.InRange(eased, rules.EaseRate * 0.8f, rules.EaseRate * 1.1f);
        Assert.Equal(eased, sim.Doors.Open(leaf), 2);
    }

    [Fact]
    public void A_door_stops_rather_than_swing_into_someone()
    {
        (SimWorld sim, int leaf) = Guardhouse();
        sim.Doors.SetOpen(leaf, 0f);
        sim.AddPlayer(0, 0, new Vector3(5.1f, 0f, 34.5f), West);
        // Someone standing just inside, in the leaf's way.
        PlayerState inside = sim.AddPlayer(1, 1, new Vector3(3.45f, 0f, 34.55f), 0f);

        Run(sim, 2 * Second, (t, p) => p.Id == 0 ? Press(p, t == 2) : default);
        float open = sim.Doors.Open(leaf);
        float gap = sim.Doors.Shape(leaf).DistanceTo(inside.Position + new Vector3(0f, 1f, 0f), out _);
        _out.WriteLine($"stopped at {open:0.00} open, {gap:0.00} m from the body's middle");
        Assert.InRange(open, 0.05f, 0.9f);
        Assert.True(gap >= TestData.Config.Movement.CapsuleRadius - 0.02f, "the leaf swung into them");

        // Once they step aside it carries on.
        inside.Position = new Vector3(2f, 0f, 36.4f);
        Run(sim, Second, (t, p) => p.Id == 0 ? Press(p, false) : default);
        Assert.Equal(1f, sim.Doors.Open(leaf), 3);
    }

    [Fact]
    public void Opening_and_shutting_make_a_noise()
    {
        (SimWorld sim, int leaf) = Guardhouse();
        sim.Doors.SetOpen(leaf, 0f);
        sim.AddPlayer(0, 0, new Vector3(5.1f, 0f, 34.5f), West);

        List<SimEvent> opening = Run(sim, Second, (t, p) => Press(p, t == 2));
        List<SimEvent> shutting = Run(sim, Second, (t, p) => Press(p, t == 2));
        SimEvent start = Assert.Single(opening, e => e.Type == SimEventType.DoorMoved);
        Assert.Equal((int)DoorMotion.Opening, start.Extra);
        Assert.Equal(0, start.PlayerId);
        Assert.Equal(leaf, start.TargetId);
        Assert.Equal(sim.Doors[leaf].Kind.Noise, start.Value);
        Assert.Equal(new[] { (int)DoorMotion.Closing, (int)DoorMotion.Shut },
            shutting.Where(e => e.Type == SimEventType.DoorMoved).Select(e => e.Extra).ToArray());
    }

    [Fact]
    public void A_bot_hears_a_door_open_nearby()
    {
        BotArena arena = BotArena.Create("normal");
        int leaf = LeafAt(arena.Sim, Doorway);
        arena.Sim.Doors.SetOpen(leaf, 0f);
        BotBrain bot = arena.AddBot("guardhouse");
        arena.Start();
        // You open the door from the yard, behind the sentry's back.
        bot.Self.Yaw = 0f;
        arena.PlaceHero(new Vector3(5.1f, 0f, 34.5f), Doorway);
        arena.HeroScript = (tick, me) => new InputCommand { Tick = tick, Yaw = West, Pitch = 0f, Buttons = tick == 5 ? InputButtons.Interact : InputButtons.None };
        bool heard = false;
        arena.Run(Second, () =>
        {
            heard |= bot.Senses.For(0) is { HasLead: true };
            return heard;
        });
        Assert.True(heard, "the sentry didn't hear the door");
    }

    [Fact]
    public void A_bot_opens_a_shut_door_on_its_way()
    {
        BotArena arena = BotArena.Create("normal");
        int leaf = LeafAt(arena.Sim, Doorway);
        BotBrain bot = arena.AddBot("guardhouse");
        arena.Start();
        arena.Sim.Doors.SetOpen(leaf, 0f);
        // A shot out in the yard, out of sight behind the shut door: the sentry comes out to look.
        arena.PlaceHero(new Vector3(13f, 0f, 33f), new Vector3(20f, 0f, 33f));
        arena.HeroScript = (tick, me) => new InputCommand { Tick = tick, Yaw = me.Yaw, Pitch = -0.6f, Buttons = tick == 30 ? InputButtons.Fire : InputButtons.None };
        bool outside = false;
        float opened = 0f;
        arena.Run(20 * Second, () =>
        {
            opened = MathF.Max(opened, arena.Sim.Doors.Open(leaf));
            // Through the doorway: past the wall's outer face (it's 0.25 m thick, centred on x = 4).
            outside |= bot.Self.Position.X > Doorway.X + 0.2f;
            return outside;
        });
        _out.WriteLine($"door opened to {opened:0.00}; bot at {bot.Self.Position}, {bot.Mode}");
        Assert.True(opened >= TestData.Config.Rules.Doors.BotPassOpen, "the bot never opened the door");
        Assert.True(outside, "the bot never came out of the guardhouse");
    }

    [Fact]
    public void A_bot_opens_a_door_that_swings_towards_it()
    {
        // The pump house's steel door swings out into the yard: a bot coming in has to stand clear of it to open it.
        BotArena arena = BotArena.Create("normal");
        var doorway = new Vector3(32f, 0f, -27.9f);
        int leaf = LeafAt(arena.Sim, doorway);
        Assert.True(Vector3.Dot(arena.Sim.Doors[leaf].Side, Vector3.UnitZ) > 0.9f, "the door doesn't swing out into the yard");
        var outside = new OpponentSpawn { Id = "yard", Position = new Vector3(32f, 0f, -21f), Yaw = 0f, Roles = new[] { "sentry" } };
        BotBrain bot = arena.AddBotAt(outside, team: 1, "sentry");
        arena.Start();
        arena.Sim.Doors.SetOpen(leaf, 0f);
        // A shot inside, behind the shut door: the sentry goes in to look.
        arena.PlaceHero(new Vector3(36.5f, 0f, -32.5f), new Vector3(37f, 0f, -33f));
        arena.HeroScript = (tick, me) => new InputCommand { Tick = tick, Yaw = me.Yaw, Pitch = -0.6f, Buttons = tick == 30 ? InputButtons.Fire : InputButtons.None };
        bool inside = false;
        float opened = 0f;
        arena.Run(25 * Second, () =>
        {
            opened = MathF.Max(opened, arena.Sim.Doors.Open(leaf));
            inside |= bot.Self.Position.Z < doorway.Z - 0.3f;
            return inside;
        });
        _out.WriteLine($"door opened to {opened:0.00}; bot at {bot.Self.Position}, {bot.Mode}");
        Assert.True(opened >= TestData.Config.Rules.Doors.BotPassOpen, "the bot never opened the door");
        Assert.True(inside, "the bot never went into the pump house");
    }

    [Fact]
    public void Paths_go_round_a_door_standing_open_across_the_way()
    {
        // The warehouse's north door opens out into the rear alley: standing open, its leaf sticks out across it.
        BotArena arena = BotArena.Create("normal");
        int leaf = LeafAt(arena.Sim, new Vector3(-4f, 0f, -36.1f));
        arena.Sim.Doors.SetOpen(leaf, 1f);
        float radius = TestData.Data.Bots.Navigation.AgentRadius;
        var path = new List<Vector3>();
        Assert.True(arena.Squad.Grid.FindPath(new Vector3(-20f, 0f, -36.6f), new Vector3(10f, 0f, -36.6f), path));
        var points = new List<Vector3> { new(-20f, 0f, -36.6f) };
        points.AddRange(path);
        for (int i = 1; i < points.Count; i++)
        {
            for (float t = 0f; t <= 1f; t += 0.05f)
            {
                Vector3 at = Vector3.Lerp(points[i - 1], points[i], t);
                Assert.False(arena.Sim.Doors.OpenLeafAt(at, radius * 0.9f, 0.9f), $"the path walks into the open leaf at {at}");
            }
        }

        // Shut, the way along the alley is straight again.
        arena.Sim.Doors.SetOpen(leaf, 0f);
        Assert.True(arena.Squad.Grid.FindPath(new Vector3(-20f, 0f, -36.6f), new Vector3(10f, 0f, -36.6f), path));
        Assert.True(path.Count <= 2, $"a {path.Count}-corner path along an empty alley");
    }

    [Fact]
    public void A_bot_opens_a_door_standing_ajar_towards_it()
    {
        // The pump house's west door swings out towards anyone coming from the west; ajar, its leaf is in their way.
        BotArena arena = BotArena.Create("normal");
        var doorway = new Vector3(29.9f, 0f, -31f);
        int leaf = LeafAt(arena.Sim, doorway);
        var outside = new OpponentSpawn { Id = "west", Position = new Vector3(24f, 0f, -31.8f), Yaw = -MathF.PI / 2f, Roles = new[] { "sentry" } };
        BotBrain bot = arena.AddBotAt(outside, team: 1, "sentry");
        arena.Start();
        arena.Sim.Doors.SetOpen(leaf, TestData.Config.Rules.Doors.Ajar);
        arena.PlaceHero(new Vector3(36.5f, 0f, -32.5f), new Vector3(37f, 0f, -33f));
        arena.HeroScript = (tick, me) => new InputCommand { Tick = tick, Yaw = me.Yaw, Pitch = -0.6f, Buttons = tick == 30 ? InputButtons.Fire : InputButtons.None };
        bool inside = false;
        arena.Run(25 * Second, () => inside |= bot.Self.Position.X > doorway.X + 0.4f);
        _out.WriteLine($"door at {arena.Sim.Doors.Open(leaf):0.00}; bot at {bot.Self.Position}, {bot.Mode}");
        Assert.True(inside, "the bot never got past the ajar door");
    }

    [Fact]
    public void Doors_start_the_same_from_the_same_seed_and_differently_from_another()
    {
        float[] Starts(ulong seed)
        {
            (SimWorld sim, int _) = Guardhouse(seed);
            return Enumerable.Range(0, sim.Doors.Count).Select(sim.Doors.Open).ToArray();
        }

        Assert.Equal(Starts(11), Starts(11));
        Assert.NotEqual(Starts(11), Starts(12));
        // Both leaves of a pair always start alike.
        (SimWorld s, int _) = Guardhouse(13);
        for (int i = 0; i < s.Doors.Count; i++)
        {
            if (s.Doors[i].Partner >= 0)
            {
                Assert.Equal(s.Doors.Open(i), s.Doors.Open(s.Doors[i].Partner));
            }
        }
    }

    [Fact]
    public void Stepping_with_doors_on_the_move_does_not_allocate()
    {
        (SimWorld sim, int leaf) = Guardhouse();
        sim.Doors.SetOpen(leaf, 0f);
        sim.AddPlayer(0, 0, new Vector3(5.1f, 0f, 34.5f), West);
        var commands = new InputCommand[1];
        void Steps(int from, int count)
        {
            for (int t = from; t < from + count; t++)
            {
                commands[0] = new InputCommand { Tick = t, Yaw = West, Buttons = t % 90 == 0 ? InputButtons.Interact : InputButtons.None };
                sim.Step(commands);
                sim.Events.Clear();
            }
        }

        Steps(0, 400);
        Assert.Equal(0, Allocations.During(() => Steps(400, 400)));
    }

    [Fact]
    public void A_leaf_naming_an_unknown_door_fails_to_load()
    {
        var source = new EditedDataSource(TestData.Source)
            .Edit("kit/buildings/guardhouse.jsonc", text => text.Replace("\"door\": \"panel\"", "\"door\": \"barn\""));
        DataException ex = Assert.Throws<DataException>(() => GameData.Load(source));
        Assert.Contains("guardhouse", ex.Message);
        Assert.Contains("unknown door 'barn'", ex.Message);
    }

    [Fact]
    public void Only_door_openings_take_a_leaf()
    {
        var source = new EditedDataSource(TestData.Source)
            .Edit("kit/buildings/guardhouse.jsonc", text => text.Replace(
                "\"sill_m\": 1.0, \"height_m\": 0.9, \"kind\": \"window\" }",
                "\"sill_m\": 1.0, \"height_m\": 0.9, \"kind\": \"window\", \"leaf\": { \"door\": \"panel\" } }"));
        DataException ex = Assert.Throws<DataException>(() => GameData.Load(source));
        Assert.Contains("only door openings take a leaf", ex.Message);
    }
}
