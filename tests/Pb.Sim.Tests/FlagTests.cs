using System.Numerics;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;

namespace Pb.Sim.Tests;

/// <summary>
/// Phase 5 (M5.7): capture the flag's rules, on the field (one flag in the middle, scored at the other side's buzzer) and
/// with two bases (each side's own, scored at your own): taking, carrying, dropping, capturing, and who may do which.
/// </summary>
public class FlagTests
{
    private const int Second = 120;

    private static FlagRules Rules => TestData.Config.Rules.Flag;

    private static MatchSetup FlagSetup(float timeLimit) => new()
    {
        HeroId = 0, Mode = MatchModeKind.Teams, Format = MatchFormat.Flag, TimeLimit = timeLimit, Countdown = Rules.Countdown, StartPods = 2,
        BotPods = 2, Pickups = false,
    };

    /// <summary>A flag point on the field's <paramref name="layout"/>th layout, two a side in their start boxes, live.</summary>
    private static (SimWorld Sim, PlayerState[] South, PlayerState[] North) FieldPoint(int layout = 0, float timeLimit = 180f)
    {
        LevelLayout area = TestData.Data.Levels["sports_ground"];
        LevelLayout level = area.ForPlace(area.Places[layout]);
        var sim = new SimWorld(TestData.Config, 1);
        sim.LoadLevel(level);
        FieldSpec field = level.Field!;
        var south = new PlayerState[2];
        var north = new PlayerState[2];
        for (int i = 0; i < 2; i++)
        {
            south[i] = sim.AddPlayer(i, 0, field.StartOf(0, i, 2), FieldSpec.StartYaw(0));
            north[i] = sim.AddPlayer(2 + i, 1, field.StartOf(1, i, 2), FieldSpec.StartYaw(1));
        }

        sim.StartMatch(FlagSetup(timeLimit));
        GoLive(sim);
        return (sim, south, north);
    }

    /// <summary>A two-base point in Oxbarrow Works: side 0 at the gate, side 1 at the opponent start farthest from it.</summary>
    private static (SimWorld Sim, PlayerState[] South, PlayerState[] North) BasesPoint()
    {
        LevelLayout level = TestData.Data.Levels["oxbarrow_works"];
        var sim = new SimWorld(TestData.Config, 1);
        sim.LoadLevel(level);
        Vector3 gate = level.PlayerSpawn;
        Vector3 deep = level.OpponentSpawns.MaxBy(s => Vector3.Distance(s.Position, gate))!.Position;
        var south = new[] { sim.AddPlayer(0, 0, gate, 0f), sim.AddPlayer(1, 0, gate + new Vector3(1.5f, 0f, 0f), 0f) };
        var north = new[] { sim.AddPlayer(2, 1, deep, 0f), sim.AddPlayer(3, 1, deep + new Vector3(1.5f, 0f, 0f), 0f) };
        sim.StartMatch(FlagSetup(Rules.PointTime));
        GoLive(sim);
        return (sim, south, north);
    }

    private static void GoLive(SimWorld sim)
    {
        sim.GoLive();
        Step(sim, (int)MathF.Ceiling(Rules.Countdown * Second) + 1);
        Assert.True(sim.IsLive);
    }

    private static List<SimEvent> Step(SimWorld sim, int ticks)
    {
        var events = new List<SimEvent>();
        var commands = new InputCommand[sim.Players.Count];
        for (int t = 0; t < ticks; t++)
        {
            for (int i = 0; i < commands.Length; i++)
            {
                commands[i] = new InputCommand { Tick = sim.Tick };
            }

            sim.Step(commands);
            events.AddRange(sim.Events.Items.ToArray());
            sim.Events.Clear();
        }

        return events;
    }

    /// <summary>Beside the centre bunker, within reach of the flag on top of it (its foot on the ground).</summary>
    private static Vector3 BesideTheCentre(FlagSet flags) => new(flags.Home(0).X + 1.35f, 0f, flags.Home(0).Z);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void On_the_field_the_flag_stands_on_the_centre_bunker_and_either_side_may_take_it(int layout)
    {
        (SimWorld sim, PlayerState[] south, PlayerState[] north) = FieldPoint(layout);
        FlagSet flags = sim.Match!.Flags!;
        Assert.True(flags.IsCentre);
        Assert.Equal(1, flags.Count);
        Assert.Equal((-1, FlagStatus.Home), (flags.Owner(0), flags.Status(0)));
        // On top of the centre bunker (1.2 m tall in both layouts), at the field's middle.
        Assert.True(Vector3.Distance(new Vector3(0f, 1.2f, 0f), flags.Home(0)) < 0.01f, $"the flag stands at {flags.Home(0)}");

        // North takes it as readily as south would: whoever walks up first.
        north[0].Position = BesideTheCentre(flags);
        List<SimEvent> events = Step(sim, 1);
        Assert.Equal((FlagStatus.Carried, north[0].Id), (flags.Status(0), flags.Carrier(0)));
        Assert.True(north[0].SprintBlocked, "a carrier can't sprint");
        SimEvent taken = Assert.Single(events, e => e.Type == SimEventType.FlagTaken);
        Assert.Equal((north[0].Id, 1, 0), (taken.PlayerId, (int)taken.Team, taken.Extra));

        // It goes where its carrier goes; nobody else can take it from them.
        south[0].Position = north[0].Position + new Vector3(0.3f, 0f, 0f);
        north[0].Position += new Vector3(-3f, 0f, 2f);
        Step(sim, 1);
        Assert.Equal(north[0].Id, flags.Carrier(0));
        Assert.Equal(north[0].Position, flags.Position(0));
    }

    [Fact]
    public void A_carrier_scores_at_the_other_sides_station_and_only_there()
    {
        (SimWorld sim, PlayerState[] south, _) = FieldPoint();
        FlagSet flags = sim.Match!.Flags!;
        FieldSpec field = sim.Level!.Field!;
        south[0].Position = BesideTheCentre(flags);
        Step(sim, 1);
        Assert.Equal(south[0].Id, flags.Carrier(0));

        // Their own station isn't theirs to score at.
        south[0].Position = field.Buzzers[0];
        Step(sim, 30);
        Assert.Equal((FlagStatus.Carried, -1), (flags.Status(0), flags.CapturedBy));

        // The other side's is: the point is theirs.
        south[0].Position = field.Buzzers[1] + new Vector3(Rules.ScoreReach * 0.8f, 0f, 0f);
        List<SimEvent> events = Step(sim, 1);
        Assert.Equal((FlagStatus.Captured, 0, south[0].Id), (flags.Status(0), flags.CapturedBy, flags.ScoredBy));
        Assert.False(south[0].SprintBlocked);
        Assert.Contains(events, e => e.Type == SimEventType.FlagCaptured && e.PlayerId == south[0].Id);
        events.AddRange(Step(sim, (int)(TestData.Config.Rules.SettleTime * Second) + 2));
        MatchState match = sim.Match;
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(new MatchResult(RoundEnd.Captured, 0), match.Result);
        Assert.Equal((RoundOutcome.FlagCaptured, RoundOutcome.FlagLost), (match.OutcomeFor(0), match.OutcomeFor(1)));
        Assert.True(match.OutcomeFor(0).IsWin());
        Assert.Contains(events, e => e.Type == SimEventType.RoundEnded);
    }

    [Fact]
    public void A_carrier_who_goes_out_drops_it_where_they_fell_and_it_stays_for_either_side()
    {
        (SimWorld sim, PlayerState[] south, PlayerState[] north) = FieldPoint();
        FlagSet flags = sim.Match!.Flags!;
        south[0].Position = BesideTheCentre(flags);
        Step(sim, 1);
        var fell = new Vector3(4f, 0f, 7.5f);
        south[0].Position = fell;
        Step(sim, 1);
        south[0].Alive = false;
        List<SimEvent> events = Step(sim, 1);
        Assert.Equal((FlagStatus.Dropped, -1), (flags.Status(0), flags.Carrier(0)));
        Assert.Equal(fell, flags.Position(0));
        Assert.Contains(events, e => e.Type == SimEventType.FlagDropped && e.PlayerId == south[0].Id && e.Position == fell);

        // It doesn't go home: it lies there until someone takes it, of either side.
        Step(sim, 5 * Second);
        Assert.Equal((FlagStatus.Dropped, fell), (flags.Status(0), flags.Position(0)));
        north[1].Position = fell + new Vector3(0.5f, 0f, 0.5f);
        Step(sim, 1);
        Assert.Equal((FlagStatus.Carried, north[1].Id), (flags.Status(0), flags.Carrier(0)));
    }

    [Fact]
    public void With_two_bases_nobody_takes_their_own_and_theirs_scores_at_yours()
    {
        (SimWorld sim, PlayerState[] south, PlayerState[] north) = BasesPoint();
        FlagSet flags = sim.Match!.Flags!;
        Assert.False(flags.IsCentre);
        Assert.Equal(2, flags.Count);
        // Each side's flag stands where its first player starts, and each side scores there.
        Assert.Equal((0, south[0].Position), (flags.Owner(0), flags.Home(0)));
        Assert.Equal((1, flags.Home(1)), (flags.Owner(1), north[0].Position));
        Assert.Equal((flags.Home(0), flags.Home(1)), (flags.ScoreAt(0), flags.ScoreAt(1)));
        Assert.Equal((1, 0), (flags.TargetOf(0), flags.TargetOf(1)));
        Vector3 southBase = flags.Home(0), northBase = flags.Home(1);

        // Standing on their own flags, nobody takes them.
        Step(sim, 30);
        Assert.Equal((FlagStatus.Home, FlagStatus.Home), (flags.Status(0), flags.Status(1)));

        // South takes north's at its base and brings it home; carrying it past north's base scores nothing.
        south[0].Position = northBase + new Vector3(0.5f, 0f, 0f);
        Step(sim, 1);
        Assert.Equal((FlagStatus.Carried, south[0].Id, 1), (flags.Status(1), flags.Carrier(1), flags.FlagOf(south[0].Id)));
        Step(sim, 30);
        Assert.Equal(-1, flags.CapturedBy);
        south[0].Position = southBase + new Vector3(0f, 0f, -1f);
        Step(sim, 1);
        Assert.Equal((0, 1, south[0].Id), (flags.CapturedBy, flags.CapturedFlag, flags.ScoredBy));
        Step(sim, (int)(TestData.Config.Rules.SettleTime * Second) + 2);
        Assert.Equal(new MatchResult(RoundEnd.Captured, 0), sim.Match.Result);
    }

    [Fact]
    public void With_two_bases_a_dropped_flag_stays_where_it_fell_and_its_own_side_cant_take_it()
    {
        (SimWorld sim, PlayerState[] south, PlayerState[] north) = BasesPoint();
        FlagSet flags = sim.Match!.Flags!;
        south[0].Position = flags.Home(1) + new Vector3(0.5f, 0f, 0f);
        Step(sim, 1);
        Vector3 fell = flags.Home(1) + new Vector3(6f, 0f, 0f);
        south[0].Position = fell;
        Step(sim, 1);
        south[0].Alive = false;
        Step(sim, 1);
        Assert.Equal((FlagStatus.Dropped, fell), (flags.Status(1), flags.Position(1)));

        // North walks over it: it's theirs, so it stays (no flag goes home); south's other player takes it up again.
        north[1].Position = fell;
        Step(sim, Second);
        Assert.Equal((FlagStatus.Dropped, fell), (flags.Status(1), flags.Position(1)));
        south[1].Position = fell + new Vector3(0.4f, 0f, 0f);
        Step(sim, 1);
        Assert.Equal((FlagStatus.Carried, south[1].Id), (flags.Status(1), flags.Carrier(1)));
    }

    [Fact]
    public void At_time_up_the_side_with_more_players_in_takes_the_point()
    {
        (SimWorld sim, _, PlayerState[] north) = FieldPoint(timeLimit: 1f);
        north[0].Alive = false;
        Step(sim, 2 * Second);
        Assert.Equal(new MatchResult(RoundEnd.MoreIn, 0), sim.Match!.Result);
    }

    [Fact]
    public void A_live_flag_point_steps_without_allocating()
    {
        (SimWorld sim, PlayerState[] south, _) = FieldPoint();
        FlagSet flags = sim.Match!.Flags!;
        south[0].Position = BesideTheCentre(flags);
        var commands = new InputCommand[sim.Players.Count];
        commands[0] = new InputCommand { Move = new Vector2(0f, 1f) };
        sim.Step(commands);
        sim.Events.Clear();
        Assert.Equal(south[0].Id, flags.Carrier(0));
        long bytes = Allocations.During(() =>
        {
            for (int t = 0; t < 60; t++)
            {
                sim.Step(commands);
                sim.Events.Clear();
            }
        });
        Assert.Equal(0, bytes);
    }
}
