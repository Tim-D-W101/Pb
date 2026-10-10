using System.Numerics;
using Pb.Net.Lobby;
using Pb.Net.Packing;
using Pb.Net.Protocol;
using Pb.Sim;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;
using Pb.Sim.Tests;

namespace Pb.Net.Tests;

/// <summary>Phase 5 (M5.7): capture the flag played with others: the starts at each base, the point's clock, the flags in the snapshot, the lobby's match.</summary>
public class FlagNetTests
{
    private const int Second = 120;

    private static readonly string[] Callsigns = { "Kestrel", "Magpie", "Heron", "Rook", "Wren", "Plover", "Shrike", "Lapwing", "Dunlin", "Merlin" };

    private static readonly Dictionary<string, (LevelLayout Level, CoverSet Cover, CollisionWorld World)> Built = new();

    private static FlagRules Rules => TestData.Config.Rules.Flag;

    private static (LevelLayout Level, CoverSet Cover, CollisionWorld World) Build(string id)
    {
        lock (Built)
        {
            if (!Built.TryGetValue(id, out var built))
            {
                LevelLayout area = TestData.Data.Levels[id];
                LevelLayout level = area.ForPlace(area.Places[0]);
                NavGrid grid = NavGrid.Build(level, TestData.Data.Bots.Navigation);
                var world = new CollisionWorld();
                level.BuildCollision(world);
                CoverSet cover = CoverSet.Build(level, grid, TestData.Config.Movement.StandEyeHeight, TestData.Config.Movement.CrouchEyeHeight);
                Built[id] = built = (level, cover, world);
            }

            return built;
        }
    }

    private static CastRound Cast(string levelId, int size, params Person[] people)
    {
        (LevelLayout level, CoverSet cover, CollisionWorld world) = Build(levelId);
        GameMode mode = TestData.Config.Rules.FindMode("flag")!;
        ObjectiveChoice eliminate = TestData.Config.Rules.Objectives.Find(ObjectiveKind.Eliminate)!;
        TierDef tier = TestData.Data.Areas.Areas.Single(a => a.Id == levelId).Tiers[1];
        return RoundCasting.Cast(level, cover, world, TestData.Config, TestData.Data.Bots, mode, size, eliminate, tier, people, 5, 1, Callsigns, levelId, null);
    }

    [Fact]
    public void In_a_compound_each_side_starts_at_its_base_on_the_ground_and_every_copy_plays_it_as_a_flag_point()
    {
        CastRound cast = Cast("oxbarrow_works", 4, new Person("Ada", Side: 0), new Person("Bo", Side: 1));
        RoundSetupMessage setup = cast.Setup;
        Assert.Equal(8, setup.Roster.Count);
        RosterEntry base0 = setup.Roster.First(e => e.Team == 0), base1 = setup.Roster.First(e => e.Team == 1);
        Assert.Equal(("Ada", "Bo"), (base0.Name, base1.Name));
        Assert.Contains(Build("oxbarrow_works").Level.PlayerSpawns, s => Vector3.Distance(s.Position, base0.Position) < 1e-3f);
        Assert.True(MathF.Abs(base1.Position.Y - base0.Position.Y) <= TestData.Config.Rules.Spawning.BaseHeight, $"their base {base1.Position} isn't on the ground");
        Assert.True(Vector3.Distance(base0.Position, base1.Position) >= TestData.Config.Rules.Spawning.MinDistanceFromYou);

        // The point's clock (away from a field), no pickups, the match from nothing; every copy plays a flag point.
        Assert.Equal((Rules.PointTime, false, ObjectiveKind.Eliminate), (setup.TimeLimit, setup.Pickups, setup.Objective));
        Assert.Equal((Rules.RaceTo, 0, 0, 0), (setup.RaceTo, setup.Points0, setup.Points1, setup.PointsPlayed));
        MatchSetup played = RoundWorld.MatchSetupOf(TestData.Config, setup);
        Assert.Equal((MatchFormat.Flag, Rules.Countdown, MatchModeKind.Teams), (played.Format, played.Countdown, played.Mode));

        // The flags stand where each side's first player starts.
        var sim = new SimWorld(TestData.Config, setup.Seed);
        sim.LoadLevel(Build("oxbarrow_works").Level);
        foreach (RosterEntry e in setup.Roster)
        {
            sim.AddPlayer(e.PlayerId, e.Team, e.Position, e.Yaw);
        }

        sim.StartMatch(played);
        FlagSet flags = sim.Match!.Flags!;
        Assert.False(flags.IsCentre);
        Assert.Equal(base0.Position, flags.Home(flags.FlagOfSide(0)));
        Assert.Equal(base1.Position, flags.Home(flags.FlagOfSide(1)));
    }

    [Fact]
    public void On_the_field_a_flag_point_starts_from_the_boxes_on_the_fields_shorter_clock()
    {
        CastRound cast = Cast("sports_ground", 5, new Person("Ada", Side: 0), new Person("Bo", Side: 1));
        RoundSetupMessage setup = cast.Setup;
        FieldSpec field = Build("sports_ground").Level.Field!;
        Assert.Equal(10, setup.Roster.Count);
        for (int side = 0; side < 2; side++)
        {
            Aabb box = field.StartBoxOf(side, -1f, 3f);
            Assert.All(setup.Roster.Where(e => e.Team == side), e => Assert.True(box.Contains(e.Position), $"{e.Name} starts at {e.Position}, outside side {side}'s box"));
        }

        Assert.All(cast.BotStarts.Where(s => s is not null), s => Assert.Equal(Rules.FieldRole, s!.Roles[0]));
        Assert.Equal((Rules.FieldPointTime, false), (setup.TimeLimit, setup.Pickups));
    }

    [Fact]
    public void The_lobby_plays_a_flag_match_point_by_point()
    {
        var rig = new NetRig();
        var lobby = new LobbyHost(rig.Server, rig.Settings, TestData.Config.Rules,
            new LobbyChoices { LevelId = "oxbarrow_works", ModeId = "flag", Size = 3 }, () => rig.Now, new LobbyMember { Name = "Host" });
        RigClient ada = rig.Join("Ada");
        for (int i = 0; i < 10; i++)
        {
            rig.Tick();
            lobby.Update();
        }

        Assert.True(lobby.PlaysMatch);
        Assert.Equal(Rules.RaceTo, lobby.RaceTo);
        var people = new[] { new PersonInRound(LobbyHost.HostId, 0, 0), new PersonInRound(ada.Net.Welcome!.ClientId, 1, 1) };
        lobby.RoundOver(new MatchResult(RoundEnd.Captured, 1), people, Array.Empty<StatsEntry>());
        Assert.True(lobby.MatchOn);
        Assert.Equal(new MatchScore(Rules.RaceTo, 0, 1, 1), lobby.Score);
    }

    /// <summary>A flag point on the field, two a side in their start boxes, its countdown over.</summary>
    private static (SimWorld Sim, PlayerState[] South, PlayerState[] North) Point(SimRole role)
    {
        var sim = new SimWorld(TestData.Config, 3) { Role = role };
        sim.LoadLevel(Build("sports_ground").Level);
        FieldSpec field = sim.Level!.Field!;
        var south = new PlayerState[2];
        var north = new PlayerState[2];
        for (int i = 0; i < 2; i++)
        {
            south[i] = sim.AddPlayer(i, 0, field.StartOf(0, i, 2), FieldSpec.StartYaw(0));
            north[i] = sim.AddPlayer(2 + i, 1, field.StartOf(1, i, 2), FieldSpec.StartYaw(1));
        }

        sim.StartMatch(new MatchSetup
        {
            People = new[] { 0, 2 }, Mode = MatchModeKind.Teams, Format = MatchFormat.Flag, TimeLimit = Rules.FieldPointTime, Countdown = Rules.Countdown,
            StartPods = 2, BotPods = 2, Pickups = false,
        });
        sim.GoLive();
        Step(sim, (int)(Rules.Countdown * Second) + 2);
        return (sim, south, north);
    }

    private static void Step(SimWorld sim, int ticks)
    {
        var commands = new InputCommand[sim.Players.Count];
        for (int t = 0; t < ticks; t++)
        {
            for (int i = 0; i < commands.Length; i++)
            {
                commands[i] = new InputCommand { Tick = sim.Tick };
            }

            sim.Step(commands);
            sim.Events.Clear();
        }
    }

    [Fact]
    public void A_joining_copy_has_the_flag_as_the_server_has_it_taken_dropped_and_captured()
    {
        (SimWorld server, PlayerState[] south, PlayerState[] north) = Point(SimRole.Authority);
        (SimWorld client, _, _) = Point(SimRole.Client);
        var fields = new WorldFields(server.Players.Count, server.Doors.Count, Pb.Net.Server.NetServer.Grid(server));
        var world = new uint[fields.Count];
        void Send()
        {
            fields.Capture(server, world);
            fields.ApplyRound(client, world);
        }

        FlagSet flags = server.Match!.Flags!, seen = client.Match!.Flags!;
        Assert.Equal(MatchPhase.Live, server.Match.Phase);

        // Taken: the south's first walks up to it (beside the centre bunker).
        Vector3 home = flags.Home(0);
        south[0].Position = new Vector3(home.X, 0f, home.Z + 1.2f);
        Step(server, 2);
        Send();
        Assert.Equal((FlagStatus.Carried, south[0].Id), (seen.Status(0), seen.Carrier(0)));

        // Down where its carrier went out, a few metres on.
        south[0].Position = new Vector3(home.X + 3f, 0f, home.Z + 6f);
        Step(server, 1);
        south[0].Alive = false;
        Step(server, 1);
        Send();
        Assert.Equal((FlagStatus.Dropped, -1), (seen.Status(0), seen.Carrier(0)));
        Assert.True(Vector3.Distance(flags.Position(0), seen.Position(0)) <= PositionQuant.MaxStep, $"the copy has it at {seen.Position(0)}, not {flags.Position(0)}");

        // The north's first picks it up and carries it to the south's buzzer: their point, and every copy knows who scored.
        north[0].Position = flags.Position(0);
        Step(server, 2);
        Assert.Equal(north[0].Id, flags.Carrier(0));
        north[0].Position = flags.ScoreAt(1);
        Step(server, 2);
        Assert.Equal(1, flags.CapturedBy);
        Send();
        Assert.Equal((FlagStatus.Captured, 1, north[0].Id, 0), (seen.Status(0), seen.CapturedBy, seen.ScoredBy, seen.CapturedFlag));
        Assert.Equal(server.Match.Result, client.Match.Result);
        Assert.Equal(new MatchResult(RoundEnd.Captured, 1), client.Match.Result);
    }
}
