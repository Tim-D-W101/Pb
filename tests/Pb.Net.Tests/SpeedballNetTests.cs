using System.Numerics;
using Pb.Net.Lobby;
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

/// <summary>Phase 5 (M5.6): speedball played with others: the starts, the setup's match, the lobby's score, and the snapshot.</summary>
public class SpeedballNetTests
{
    private const int Second = 120;

    private static readonly Lazy<(LevelLayout Level, CoverSet Cover, CollisionWorld World)> Field = new(() =>
    {
        LevelLayout area = TestData.Data.Levels["sports_ground"];
        LevelLayout level = area.ForPlace(area.Places[0]);
        NavGrid grid = NavGrid.Build(level, TestData.Data.Bots.Navigation);
        var world = new CollisionWorld();
        level.BuildCollision(world);
        CoverSet cover = CoverSet.Build(level, grid, TestData.Config.Movement.StandEyeHeight, TestData.Config.Movement.CrouchEyeHeight);
        return (level, cover, world);
    });

    private static readonly string[] Callsigns = { "Kestrel", "Magpie", "Heron", "Rook", "Wren", "Plover", "Shrike", "Lapwing", "Dunlin", "Merlin" };

    private static SpeedballRules Rules => TestData.Config.Rules.Speedball;

    private static CastRound Cast(int size, MatchScore? match, params Person[] people)
    {
        (LevelLayout level, CoverSet cover, CollisionWorld world) = Field.Value;
        GameMode mode = TestData.Config.Rules.FindMode("speedball")!;
        ObjectiveChoice eliminate = TestData.Config.Rules.Objectives.Find(ObjectiveKind.Eliminate)!;
        TierDef tier = TestData.Data.Areas.Areas.Single(a => a.Id == "sports_ground").Tiers[1];
        return RoundCasting.Cast(level, cover, world, TestData.Config, TestData.Data.Bots, mode, size, eliminate, tier, people, 5, 1, Callsigns,
            "sports_ground", null, match: match);
    }

    [Fact]
    public void A_point_casts_each_side_into_its_own_start_box_with_the_match_so_far()
    {
        FieldSpec field = Field.Value.Level.Field!;
        CastRound cast = Cast(5, new MatchScore(4, 2, 1, 4), new Person("Ada", Side: 0), new Person("Bo", Side: 1), new Person("Cy", Side: 1));
        RoundSetupMessage setup = cast.Setup;
        Assert.Equal(10, setup.Roster.Count);
        for (int side = 0; side < 2; side++)
        {
            Aabb box = field.StartBoxOf(side, -1f, 3f);
            List<RosterEntry> mine = setup.Roster.Where(e => e.Team == side).ToList();
            Assert.Equal(5, mine.Count);
            Assert.All(mine, e => Assert.True(box.Contains(e.Position), $"{e.Name} starts at {e.Position}, outside side {side}'s box"));
            Assert.All(mine, e => Assert.Equal(FieldSpec.StartYaw(side), e.Yaw, 3));
        }

        Assert.Equal(0, setup.Roster.Single(e => e.Name == "Ada").Team);
        Assert.All(setup.Roster.Where(e => e.Name is "Bo" or "Cy"), e => Assert.Equal(1, e.Team));
        Assert.All(cast.BotStarts.Where(s => s is not null), s => Assert.Equal("speedball", s!.Roles[0]));

        // A point: the point's clock, no pickups or objective, the match as it stands; every copy plays it as speedball.
        Assert.Equal((Rules.PointTime, false, ObjectiveKind.Eliminate), (setup.TimeLimit, setup.Pickups, setup.Objective));
        Assert.Equal((4, 2, 1, 4), (setup.RaceTo, setup.Points0, setup.Points1, setup.PointsPlayed));
        MatchSetup played = RoundWorld.MatchSetupOf(TestData.Config, setup);
        Assert.Equal((MatchFormat.Speedball, Rules.Countdown, MatchModeKind.Teams), (played.Format, played.Countdown, played.Mode));
    }

    [Fact]
    public void With_nobody_on_the_south_side_a_bot_takes_its_middle_place()
    {
        CastRound cast = Cast(3, null, new Person("Ada", Side: 1), new Person("Bo", Side: 1));
        RoundSetupMessage setup = cast.Setup;
        Assert.Equal(6, setup.Roster.Count);
        Assert.Equal(3, setup.Roster.Count(e => e.Team == 0 && !e.Person));
        Assert.Equal(new byte[] { 1, 1 }, setup.Roster.Where(e => e.Person).Select(e => e.Team));
        Vector3 middle = Field.Value.Level.Field!.StartOf(0, 1, 3);
        Assert.Contains(setup.Roster, e => e.Team == 0 && Vector3.Distance(e.Position, middle) < 1e-3f);
        // The first point of a match: nothing scored yet, first to the rules' own.
        Assert.Equal((Rules.RaceTo, 0, 0, 0), (setup.RaceTo, setup.Points0, setup.Points1, setup.PointsPlayed));
    }

    [Fact]
    public void The_lobby_keeps_the_match_point_by_point_until_a_side_has_won_it()
    {
        var rig = new NetRig();
        var lobby = new LobbyHost(rig.Server, rig.Settings, TestData.Config.Rules,
            new LobbyChoices { LevelId = "sports_ground", ModeId = "speedball", Size = 3, RaceTo = 2 }, () => rig.Now, new LobbyMember { Name = "Host" });
        RigClient ada = rig.Join("Ada");
        for (int i = 0; i < 10; i++)
        {
            rig.Tick();
            lobby.Update();
        }

        Assert.True(lobby.PlaysMatch);
        Assert.Equal(2, lobby.RaceTo);
        Assert.False(lobby.MatchOn);
        var people = new[] { new PersonInRound(LobbyHost.HostId, 0, 0), new PersonInRound(ada.Net.Welcome!.ClientId, 1, 1) };
        foreach (LobbyMember m in lobby.State.Members)
        {
            m.Ready = true;
        }

        // A point to the south, then one nobody takes: the match goes on, everyone still ready for the next point.
        lobby.RoundOver(new MatchResult(RoundEnd.Hung, 0), people, Array.Empty<StatsEntry>());
        Assert.True(lobby.MatchOn);
        lobby.NextPoint();
        Assert.Equal(LobbyPhase.Loading, lobby.Phase);
        Assert.All(lobby.State.Members, m => Assert.True(m.Ready));
        lobby.RoundStarted();
        lobby.RoundOver(new MatchResult(RoundEnd.TimeUp, -1), people, Array.Empty<StatsEntry>());
        Assert.True(lobby.MatchOn);
        Assert.Equal(new MatchScore(2, 1, 0, 2), lobby.Score);

        // Everyone sees the match as it stands.
        for (int i = 0; i < 10; i++)
        {
            rig.Tick();
            lobby.Update();
        }

        LobbyState seen = ada.Net.Lobby!;
        Assert.Equal((2, 1, 0, 2), (seen.Choices.RaceTo, seen.MatchPoints[0], seen.MatchPoints[1], seen.MatchPlayed));

        // The south's second point wins it: a match won, and the next starts from nothing once they're back in the lobby.
        lobby.RoundOver(new MatchResult(RoundEnd.LastStanding, 0), people, Array.Empty<StatsEntry>());
        Assert.False(lobby.MatchOn);
        Assert.Equal(new[] { 1, 0 }, lobby.State.SideWins);
        Assert.Equal(2, lobby.State.Members.Single(m => m.Host).RoundsWon);
        lobby.BackToLobby();
        Assert.Equal(new MatchScore(2, 0, 0, 0), lobby.Score);
        Assert.Equal(new[] { 1, 0 }, lobby.State.SideWins);
    }

    /// <summary>A speedball point on the field, two a side in their start boxes, its briefing over (the countdown under way).</summary>
    private static (SimWorld Sim, PlayerState[] South) Point(SimRole role)
    {
        var sim = new SimWorld(TestData.Config, 3) { Role = role };
        sim.LoadLevel(Field.Value.Level);
        FieldSpec field = sim.Level!.Field!;
        var south = new PlayerState[2];
        for (int i = 0; i < 2; i++)
        {
            south[i] = sim.AddPlayer(i, 0, field.StartOf(0, i, 2), FieldSpec.StartYaw(0));
            sim.AddPlayer(2 + i, 1, field.StartOf(1, i, 2), FieldSpec.StartYaw(1));
        }

        sim.StartMatch(new MatchSetup
        {
            People = new[] { 0, 2 }, Mode = MatchModeKind.Teams, Format = MatchFormat.Speedball, TimeLimit = Rules.PointTime, Countdown = Rules.Countdown,
            StartPods = 2, BotPods = 2, Pickups = false,
        });
        sim.GoLive();
        return (sim, south);
    }

    private static void Step(SimWorld sim, int ticks, PlayerState? holding = null)
    {
        var commands = new InputCommand[sim.Players.Count];
        for (int t = 0; t < ticks; t++)
        {
            for (int i = 0; i < commands.Length; i++)
            {
                commands[i] = new InputCommand { Tick = sim.Tick, Buttons = sim.Players[i] == holding ? InputButtons.Interact : InputButtons.None };
            }

            sim.Step(commands);
            sim.Events.Clear();
        }
    }

    [Fact]
    public void A_joining_copy_has_the_countdown_and_the_buzzers_as_the_server_has_them()
    {
        (SimWorld server, PlayerState[] south) = Point(SimRole.Authority);
        (SimWorld client, _) = Point(SimRole.Client);
        var fields = new WorldFields(server.Players.Count, server.Doors.Count, Pb.Net.Server.NetServer.Grid(server));
        var world = new uint[fields.Count];
        void Send()
        {
            fields.Capture(server, world);
            fields.ApplyRound(client, world);
        }

        // Half way through the countdown to the horn.
        Step(server, (int)(Rules.Countdown * Second / 2));
        Send();
        Assert.Equal(MatchPhase.Countdown, client.Match!.Phase);
        Assert.Equal(server.Match!.CountdownLeft, client.Match.CountdownLeft);

        // Live, and half way through hanging the north's buzzer.
        Step(server, (int)(Rules.Countdown * Second));
        BuzzerSet buzzers = server.Match.Buzzers!;
        south[0].Position = buzzers.Post(1);
        Step(server, (int)(Rules.HangTime * Second / 2), holding: south[0]);
        Send();
        BuzzerSet seen = client.Match.Buzzers!;
        Assert.Equal(MatchPhase.Live, client.Match.Phase);
        Assert.Equal((south[0].Id, -1), (seen.Hanger(1), seen.Hanger(0)));
        Assert.Equal(buzzers.Progress(1), seen.Progress(1), 2);
        Assert.Equal(-1, seen.HungSide);

        // Hung: the point to the south, by whoever hung it.
        Step(server, (int)(Rules.HangTime * Second), holding: south[0]);
        Assert.Equal(1, buzzers.HungSide);
        Send();
        Assert.Equal((1, south[0].Id), (seen.HungSide, seen.HungBy));
        Assert.Equal(server.Match.Result, client.Match.Result);
        Assert.Equal(RoundEnd.Hung, client.Match.Result.Reason);
    }
}
