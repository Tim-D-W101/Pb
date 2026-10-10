using System.Numerics;
using Pb.Net.Lobby;
using Pb.Net.Packing;
using Pb.Net.Protocol;
using Pb.Net.Transport;
using Pb.Sim;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;
using Pb.Sim.Tests;

namespace Pb.Net.Tests;

/// <summary>Phase 5 (M5.8): arcade played with others: its round as cast, everyone's comings back on every copy, your own predicted again.</summary>
public class ArcadeNetTests
{
    private const int Second = 120;
    private static readonly LagSettings Ping100 = new() { RoundTrip_ms = 100f };

    private static ArcadeRules Rules => TestData.Config.Rules.Arcade;

    [Theory]
    [InlineData("arcade")]
    [InlineData("arcade_ffa")]
    public void An_arcade_round_is_cast_on_its_own_clock_with_the_tiers_gear_and_no_objective(string modeId)
    {
        LevelLayout area = TestData.Data.Levels["rail_yard"];
        LevelLayout level = area.ForPlace(area.Places[0]);
        NavGrid grid = NavGrid.Build(level, TestData.Data.Bots.Navigation);
        var world = new CollisionWorld();
        level.BuildCollision(world);
        CoverSet cover = CoverSet.Build(level, grid, TestData.Config.Movement.StandEyeHeight, TestData.Config.Movement.CrouchEyeHeight);
        GameMode mode = TestData.Config.Rules.FindMode(modeId)!;
        ObjectiveChoice hold = TestData.Config.Rules.Objectives.Find(ObjectiveKind.Hold)!;
        TierDef tier = TestData.Data.Areas.Areas.Single(a => a.Id == "rail_yard").Tiers[1];
        string[] callsigns = { "Kestrel", "Magpie", "Heron", "Rook", "Wren", "Plover", "Shrike", "Lapwing", "Dunlin", "Merlin" };
        CastRound cast = RoundCasting.Cast(level, cover, world, TestData.Config, TestData.Data.Bots, mode, mode.DefaultSize, hold, tier,
            new[] { new Person("Ada", Side: 0), new Person("Bo", Side: 1) }, 5, 1, callsigns, "rail_yard", null);
        RoundSetupMessage setup = cast.Setup;

        // Arcade's own clock (not the tier's), no objective even when one's asked for, the tier's pickups and pods.
        Assert.Equal((Rules.MatchTime, ObjectiveKind.Eliminate), (setup.TimeLimit, setup.Objective));
        Assert.Equal((tier.Pickups, tier.StartPods, tier.BotPods), (setup.Pickups, setup.StartPods, setup.BotPods));
        Assert.Equal(0, setup.RaceTo);
        MatchSetup played = RoundWorld.MatchSetupOf(TestData.Config, setup);
        Assert.Equal((MatchFormat.Arcade, 0f, mode.Kind), (played.Format, played.Countdown, played.Mode));
        if (mode.Kind == MatchModeKind.FreeForAll)
        {
            Assert.Equal(setup.Roster.Count, setup.Roster.Select(e => e.Team).Distinct().Count());
        }
    }

    [Fact]
    public void The_lobby_plays_arcade_as_one_round()
    {
        var rig = new NetRig();
        var lobby = new LobbyHost(rig.Server, rig.Settings, TestData.Config.Rules,
            new LobbyChoices { LevelId = "cold_store", ModeId = "arcade", Size = 3 }, () => rig.Now, new LobbyMember { Name = "Host" });
        rig.Join("Ada");
        for (int i = 0; i < 10; i++)
        {
            rig.Tick();
            lobby.Update();
        }

        Assert.False(lobby.PlaysMatch);
        Assert.Equal("arcade", lobby.State.Choices.ModeId);
    }

    [Fact]
    public void A_players_arcade_fields_go_in_the_snapshot_and_come_back_as_ticks_of_the_round()
    {
        var sim = new SimWorld(TestData.Config);
        PlayerState p = sim.AddPlayer(3, 1, new Vector3(4f, 0f, -6f), 0.5f);
        p.TimesOut = 4;
        p.ShieldUntilTick = 1000 + 180;
        p.RespawnTick = -1;
        var grid = new PositionQuant(new Aabb(new Vector3(-50f, -5f, -50f), new Vector3(50f, 20f, 50f)));
        var fields = new uint[PuppetFields.Count];
        PuppetFields.Capture(p, 1000, grid, fields);
        var copy = new SimWorld(TestData.Config).AddPlayer(3, 1, Vector3.Zero, 0f);
        PuppetFields.Arcade(copy, fields, 1000);
        Assert.Equal((4, 1180, -1), (copy.TimesOut, copy.ShieldUntilTick, copy.RespawnTick));

        // Out, coming back in a second: no shield, back in at the same tick.
        p.ShieldUntilTick = -1;
        p.RespawnTick = 1000 + Second;
        PuppetFields.Capture(p, 1000, grid, fields);
        PuppetFields.Arcade(copy, fields, 1000);
        Assert.Equal((-1, 1000 + Second), (copy.ShieldUntilTick, copy.RespawnTick));
    }

    /// <summary>Aims <paramref name="shooter"/> at <paramref name="target"/>'s chest, firing every so often while <paramref name="fire"/>.</summary>
    private static InputCommand Shoot(int tick, PlayerState shooter, PlayerState target, bool fire)
    {
        (float yaw, float pitch) = ViewAngles.FromDirection(target.Position + new Vector3(0f, 1.15f, 0f) - shooter.EyePosition);
        return new InputCommand { Tick = tick, Yaw = yaw, Pitch = pitch, Buttons = fire && tick % 12 == 0 ? InputButtons.Fire : InputButtons.None };
    }

    [Fact]
    public void Out_on_a_joining_copy_you_count_down_then_come_back_where_the_server_puts_you_and_are_predicted_again()
    {
        var rig = new NetRig();
        RigClient a = rig.Join("A", Ping100);
        rig.Run(30);
        // The host's bot at the origin; A ten metres north of it, both where arcade brings them back (no level here).
        rig.StartRound("arcade", 300f, (0, 0, false, 0f, 0f), (1, 1, true, 0f, -10f));
        rig.GoLive();
        PlayerState onServer = rig.Sim!.FindPlayer(1)!;
        bool wentOut = false;
        rig.BotScript = (t, p) => Shoot(t, p, onServer, fire: !wentOut && onServer.Alive);
        // A walks north the whole time (on the server too while out: nobody walks them off here).
        a.Script = (t, p) => new InputCommand { Tick = t, Yaw = 0f, Move = new Vector2(0f, 1f), Buttons = InputButtons.Walk };
        rig.Run(3 * Second, () => !onServer.Alive);
        Assert.False(onServer.Alive, "the bot should have put A out");
        wentOut = true;

        // Out: A's copy follows the server and knows when A's back in.
        rig.Run(Second / 2);
        PlayerState me = a.Session!.Local!;
        Assert.True(a.Session.Following);
        Assert.False(me.Alive);
        Assert.True(onServer.RespawnTick > 0);
        Assert.Equal(onServer.RespawnTick, me.RespawnTick);

        // Back in at its start, shielded: the copy has it so, from the server, and predicts A from there.
        rig.Run((int)(Rules.Respawn * Second) + Second / 2, () => a.Events.Any(e => e.Type == SimEventType.PlayerRespawned && e.PlayerId == me.Id));
        SimEvent back = Assert.Single(a.Events, e => e.Type == SimEventType.PlayerRespawned && e.PlayerId == me.Id);
        Assert.True(Vector3.Distance(back.Position, new Vector3(0f, 0f, -10f)) < 0.05f, $"back in at {back.Position}");
        Assert.True(me.Alive && !a.Session.Following);
        Assert.Equal(1, me.TimesOut);
        Assert.Equal(onServer.ShieldUntilTick, me.ShieldUntilTick);
        int corrections = a.Session.Corrections;
        rig.Run(Second);
        float apart = Vector3.Distance(me.Position, onServer.Position);
        Assert.True(apart < 0.5f, $"{apart * 100f:0} cm from where the server has A");
        Assert.True(a.Session.Corrections - corrections <= 1, $"{a.Session.Corrections - corrections} corrections after coming back in");
        Assert.True(me.Position.Z < -10.5f, $"A should be walking north from the start again, at {me.Position}");
    }
}
