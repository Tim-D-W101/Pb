using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;

namespace Pb.Sim.Tests;

/// <summary>Phase 5 (M5.6): speedball's rules: the starts, the countdown, the buzzers, how a point ends, and the match.</summary>
public class SpeedballTests
{
    private const int Second = 120;

    private static LevelLayout Ground => TestData.Data.Levels["sports_ground"];

    private static SpeedballRules Rules => TestData.Config.Rules.Speedball;

    /// <summary>A speedball point on the field, <paramref name="perSide"/> a side in their start boxes, the briefing over.</summary>
    private static (SimWorld Sim, PlayerState[] South, PlayerState[] North) Point(int perSide = 2, ulong seed = 1, bool live = true)
    {
        var sim = new SimWorld(TestData.Config, seed);
        sim.LoadLevel(Ground);
        FieldSpec field = Ground.Field!;
        var south = new PlayerState[perSide];
        var north = new PlayerState[perSide];
        for (int i = 0; i < perSide; i++)
        {
            south[i] = sim.AddPlayer(i, 0, field.StartOf(0, i, perSide), FieldSpec.StartYaw(0));
            north[i] = sim.AddPlayer(perSide + i, 1, field.StartOf(1, i, perSide), FieldSpec.StartYaw(1));
        }

        sim.StartMatch(new MatchSetup
        {
            HeroId = 0, Mode = MatchModeKind.Teams, Format = MatchFormat.Speedball, TimeLimit = Rules.PointTime, Countdown = Rules.Countdown,
            StartPods = 2, BotPods = 2, Pickups = false,
        });
        sim.GoLive();
        if (live)
        {
            Step(sim, (int)MathF.Ceiling(Rules.Countdown * Second) + 1);
        }

        return (sim, south, north);
    }

    private static List<SimEvent> Step(SimWorld sim, int ticks, Func<int, PlayerState, InputButtons>? buttons = null)
    {
        var events = new List<SimEvent>();
        var commands = new InputCommand[sim.Players.Count];
        for (int t = 0; t < ticks; t++)
        {
            for (int i = 0; i < commands.Length; i++)
            {
                commands[i] = new InputCommand { Tick = sim.Tick, Buttons = buttons?.Invoke(t, sim.Players[i]) ?? InputButtons.None };
            }

            sim.Step(commands);
            events.AddRange(sim.Events.Items.ToArray());
            sim.Events.Clear();
        }

        return events;
    }

    /// <summary>Holding Interact, for <paramref name="who"/> only.</summary>
    private static Func<int, PlayerState, InputButtons> Holding(PlayerState who) => (_, p) => p == who ? InputButtons.Interact : InputButtons.None;

    [Fact]
    public void Each_side_starts_in_a_row_across_its_start_box_facing_up_the_field()
    {
        FieldSpec field = Ground.Field!;
        NavGrid grid = NavGrid.Build(Ground, TestData.Data.Bots.Navigation);
        for (int side = 0; side < 2; side++)
        {
            Collision.Aabb box = field.StartBoxOf(side, -1f, 3f);
            var starts = Enumerable.Range(0, 5).Select(i => field.StartOf(side, i, 5)).ToArray();
            foreach (Vector3 start in starts)
            {
                Assert.True(box.Contains(start), $"side {side}'s start {start} is outside its box {box.Min}–{box.Max}");
                Assert.True(grid.SpanAt(start) >= 0, $"side {side}'s start {start} has nowhere to stand");
            }

            for (int i = 1; i < starts.Length; i++)
            {
                Assert.True(Vector3.Distance(starts[i], starts[i - 1]) > 1f, "two starts too close together");
            }

            // Facing the other side's box.
            Vector3 ahead = ViewAngles.FlatForward(FieldSpec.StartYaw(side));
            Assert.True(Vector3.Dot(ahead, field.StartOf(1 - side, 2, 5) - starts[2]) > 40f);
        }
    }

    [Fact]
    public void Nobody_moves_in_the_countdown_and_the_horn_puts_the_point_live()
    {
        (SimWorld sim, _, _) = Point(live: false);
        MatchState match = sim.Match!;
        Assert.Equal(MatchPhase.Countdown, match.Phase);
        Assert.False(sim.IsLive);
        Assert.Equal(Rules.Countdown, match.CountdownLeft, 3);

        List<SimEvent> wait = Step(sim, (int)(Rules.Countdown * Second) - 2);
        Assert.Equal(MatchPhase.Countdown, match.Phase);
        Assert.DoesNotContain(wait, e => e.Type == SimEventType.ShotFired);
        Assert.Equal(0f, match.Elapsed);

        List<SimEvent> horn = Step(sim, 3);
        Assert.Contains(horn, e => e.Type == SimEventType.MatchPhaseChanged && (MatchPhase)e.Extra == MatchPhase.Live);
        Assert.Equal(MatchPhase.Live, match.Phase);
        Assert.True(sim.IsLive);
    }

    [Fact]
    public void Holding_interact_at_the_other_sides_buzzer_for_the_hang_time_wins_the_point()
    {
        (SimWorld sim, _, PlayerState[] north) = Point();
        BuzzerSet buzzers = sim.Match!.Buzzers!;
        PlayerState hanger = north[0];
        hanger.Position = buzzers.Post(0) + new Vector3(0.6f, 0f, -0.3f);

        List<SimEvent> started = Step(sim, 1, Holding(hanger));
        Assert.Contains(started, e => e.Type == SimEventType.BuzzerHanging && e.PlayerId == hanger.Id && e.Extra == 0 && e.Value == 1f);
        Assert.Equal(hanger.Id, buzzers.Hanger(0));

        List<SimEvent> hung = Step(sim, (int)(Rules.HangTime * Second) + 1, Holding(hanger));
        Assert.Contains(hung, e => e.Type == SimEventType.BuzzerHung && e.PlayerId == hanger.Id && e.Extra == 0);
        Step(sim, (int)(TestData.Config.Rules.SettleTime * Second) + 2);
        Assert.Equal(MatchPhase.Ended, sim.Match.Phase);
        Assert.Equal(new MatchResult(RoundEnd.Hung, 1), sim.Match.Result);
        Assert.Equal(RoundOutcome.BuzzerLost, sim.Match.Outcome); // told from the hero's side, the south
        Assert.Equal(RoundOutcome.BuzzerHung, sim.Match.OutcomeFor(1));
        Assert.True(sim.Match.OutcomeFor(1).IsWin());
    }

    [Fact]
    public void Letting_go_or_stepping_off_starts_the_hang_again()
    {
        (SimWorld sim, _, PlayerState[] north) = Point();
        BuzzerSet buzzers = sim.Match!.Buzzers!;
        PlayerState hanger = north[0];
        hanger.Position = buzzers.Post(0) + new Vector3(0.5f, 0f, 0f);
        int most = (int)(Rules.HangTime * Second * 0.75f);

        Step(sim, most, Holding(hanger));
        Assert.InRange(buzzers.Progress(0), 0.7f, 0.8f);
        List<SimEvent> letGo = Step(sim, 1);
        Assert.Contains(letGo, e => e.Type == SimEventType.BuzzerHanging && e.PlayerId == hanger.Id && e.Value == 0f);
        Assert.Equal(-1, buzzers.Hanger(0));
        Assert.Equal(0f, buzzers.Progress(0));

        Step(sim, most, Holding(hanger));
        hanger.Position = buzzers.Post(0) + new Vector3(Rules.HangReach + 0.3f, 0f, 0f);
        Step(sim, 1, Holding(hanger));
        Assert.Equal(0f, buzzers.Progress(0));

        // Out, mid-hang: gone too.
        hanger.Position = buzzers.Post(0) + new Vector3(0.5f, 0f, 0f);
        Step(sim, most, Holding(hanger));
        hanger.Alive = false;
        Step(sim, 1, Holding(hanger));
        Assert.Equal(0f, buzzers.Progress(0));
        Assert.Equal(-1, buzzers.HungSide);
    }

    [Fact]
    public void Nobody_hangs_their_own_buzzer_and_one_hangs_it_at_a_time()
    {
        (SimWorld sim, PlayerState[] south, PlayerState[] north) = Point();
        BuzzerSet buzzers = sim.Match!.Buzzers!;
        south[0].Position = buzzers.Post(0) + new Vector3(0.4f, 0f, 0f);
        Step(sim, (int)(Rules.HangTime * Second) * 2, Holding(south[0]));
        Assert.Equal(-1, buzzers.Hanger(0));
        Assert.Equal(MatchPhase.Live, sim.Match.Phase);

        // Two of the north at the south's buzzer: the first there hangs it, and the other can't take over.
        north[0].Position = buzzers.Post(0) + new Vector3(0.4f, 0f, 0f);
        north[1].Position = buzzers.Post(0) + new Vector3(-0.4f, 0f, 0f);
        Step(sim, 10, Holding(north[0]));
        Step(sim, 30, (_, p) => p == north[0] || p == north[1] ? InputButtons.Interact : InputButtons.None);
        Assert.Equal(north[0].Id, buzzers.Hanger(0));
        Assert.InRange(buzzers.Progress(0), 39f / (Rules.HangTime * Second) - 0.01f, 41f / (Rules.HangTime * Second) + 0.01f);
    }

    [Fact]
    public void At_time_up_the_side_with_more_in_wins_and_level_nobody_does()
    {
        (SimWorld sim, PlayerState[] south, _) = Point(perSide: 3);
        south[2].Alive = false;
        Step(sim, (int)(Rules.PointTime * Second) + 2);
        Assert.Equal(MatchPhase.Ended, sim.Match!.Phase);
        Assert.Equal(new MatchResult(RoundEnd.MoreIn, 1), sim.Match.Result);
        Assert.Equal(RoundOutcome.BehindAtTime, sim.Match.Outcome);
        Assert.Equal(RoundOutcome.AheadAtTime, sim.Match.OutcomeFor(1));

        (SimWorld level, _, _) = Point(perSide: 3, seed: 2);
        Step(level, (int)(Rules.PointTime * Second) + 2);
        Assert.Equal(new MatchResult(RoundEnd.TimeUp, -1), level.Match!.Result);
        Assert.False(level.Match.Outcome.IsWin());
    }

    [Fact]
    public void Putting_the_other_side_out_wins_the_point_as_ever()
    {
        (SimWorld sim, _, PlayerState[] north) = Point();
        foreach (PlayerState p in north)
        {
            p.Alive = false;
        }

        Step(sim, (int)(TestData.Config.Rules.SettleTime * Second) + 2);
        Assert.Equal(new MatchResult(RoundEnd.LastStanding, 0), sim.Match!.Result);
        Assert.Equal(RoundOutcome.Cleared, sim.Match.Outcome);
    }

    [Fact]
    public void A_match_goes_to_the_first_side_to_the_target_and_level_points_score_nothing()
    {
        var match = new MatchSeries(Rules.RaceTo);
        Assert.Equal(4, match.RaceTo);
        int[] points = { 0, 1, -1, 0, 1, 1, 0 };
        foreach (int winner in points)
        {
            match.Add(winner);
            Assert.False(match.Done);
        }

        Assert.Equal(3, match.PointsOf(0));
        Assert.Equal(3, match.PointsOf(1));
        Assert.Equal(7, match.Played);
        match.Add(new MatchResult(RoundEnd.Hung, 1));
        Assert.True(match.Done);
        Assert.Equal(1, match.Winner);
        match.Add(0);
        Assert.Equal(8, match.Played); // over: nothing more counts

        var restored = new MatchSeries(4);
        restored.Restore(3, 4, 8);
        Assert.Equal(1, restored.Winner);
    }

    [Fact]
    public void Speedball_is_the_fields_mode_and_only_the_fields()
    {
        MatchRules rules = TestData.Config.Rules;
        GameMode speedball = rules.FindMode("speedball")!;
        Assert.Equal(MatchFormat.Speedball, speedball.Format);
        Assert.Equal(MatchModeKind.Teams, speedball.Kind);
        foreach (AreaEntryDef area in TestData.Data.Areas.Areas)
        {
            IReadOnlyList<GameMode> modes = rules.ModesFor(area);
            Assert.NotEmpty(modes);
            bool field = TestData.Data.Levels[area.Id].Field is not null;
            Assert.Equal(field, modes.Contains(speedball));
            if (!field)
            {
                Assert.All(modes, m => Assert.Equal(MatchFormat.Round, m.Format));
            }
        }
    }

    [Fact]
    public void A_live_point_steps_without_allocating()
    {
        (SimWorld sim, _, PlayerState[] north) = Point();
        north[0].Position = sim.Match!.Buzzers!.Post(0) + new Vector3(0.5f, 0f, 0f);
        var commands = new InputCommand[sim.Players.Count];
        commands[2] = new InputCommand { Buttons = InputButtons.Interact };
        sim.Step(commands);
        sim.Events.Clear();
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
