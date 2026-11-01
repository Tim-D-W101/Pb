using System.Numerics;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>Capture the flag and VIP escort (M5.3): their rules, round by round, without bots.</summary>
public class FlagEscortTests
{
    private readonly ITestOutputHelper _out;

    public FlagEscortTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static LevelLayout Level => TestData.Data.Levels["oxbarrow_works"];

    private static FlagRules FlagRules => TestData.Config.Rules.Objectives.Flag;

    private static EscortRules EscortRules => TestData.Config.Rules.Objectives.Escort;

    /// <summary>
    /// A live Teams round on Oxbarrow Works: you and two of yours (team 0) by the west breach, three of theirs (team 1) by the
    /// north-east collapse. Who's a person is the setup's: by default only you.
    /// </summary>
    private static (SimWorld Sim, PlayerState[] Ours, PlayerState[] Theirs) Round(ObjectiveKind kind, ulong seed = 1, float timeLimit = 900f,
        int[]? people = null)
    {
        var sim = new SimWorld(TestData.Config, seed);
        sim.LoadLevel(Level);
        PlayerState[] ours =
        {
            sim.AddPlayer(0, 0, new Vector3(-52f, 0f, -20f), 0f), sim.AddPlayer(1, 0, new Vector3(-50f, 0f, -18f), 0f),
            sim.AddPlayer(2, 0, new Vector3(-50f, 0f, -24f), 0f),
        };
        PlayerState[] theirs =
        {
            sim.AddPlayer(3, 1, new Vector3(36f, 0f, -36f), 0f), sim.AddPlayer(4, 1, new Vector3(38f, 0f, -34f), 0f),
            sim.AddPlayer(5, 1, new Vector3(34f, 0f, -38f), 0f),
        };
        sim.StartMatch(new MatchSetup
        {
            People = people ?? new[] { 0 }, Mode = MatchModeKind.Teams, TimeLimit = timeLimit, StartPods = 2, BotPods = 2, Pickups = false,
            Objective = kind,
        });
        sim.GoLive();
        return (sim, ours, theirs);
    }

    private static List<SimEvent> Step(SimWorld sim, int ticks, Func<int, InputCommand>? command = null)
    {
        var events = new List<SimEvent>();
        var commands = new InputCommand[sim.Players.Count];
        for (int t = 0; t < ticks; t++)
        {
            if (command is not null)
            {
                for (int i = 0; i < commands.Length; i++)
                {
                    commands[i] = command(i);
                }
            }

            sim.Step(commands);
            events.AddRange(sim.Events.Items.ToArray());
            sim.Events.Clear();
        }

        return events;
    }

    [Fact]
    public void Each_sides_flag_stands_at_the_start_nearest_the_middle_of_its_starts()
    {
        (SimWorld sim, PlayerState[] ours, PlayerState[] theirs) = Round(ObjectiveKind.Flag);
        ObjectiveState o = sim.Match!.Objective!;
        Assert.Equal(2, o.Flags.Count);
        Assert.Equal(ours[0].Position, o.FlagOf(0)!.Home); // (−52, −20) is nearest the middle of the three
        Assert.Equal(theirs[0].Position, o.FlagOf(1)!.Home);
        Assert.All(o.Flags, f => Assert.True(f.AtHome && f.Carrier < 0 && f.Position == f.Home));
        Assert.Same(o.FlagOf(1), o.TheirFlag(0));
        Assert.Same(o.FlagOf(0), o.TheirFlag(1));
        // Played by two teams: the menu offers it in teams only.
        ObjectiveChoice choice = TestData.Config.Rules.Objectives.Find(ObjectiveKind.Flag)!;
        Assert.True(choice.PlayableIn(MatchModeKind.Teams));
        Assert.False(choice.PlayableIn(MatchModeKind.Solo));
        Assert.False(choice.PlayableIn(MatchModeKind.FreeForAll));
    }

    [Fact]
    public void Theirs_is_taken_carried_home_and_captured()
    {
        (SimWorld sim, PlayerState[] ours, _) = Round(ObjectiveKind.Flag);
        ObjectiveState o = sim.Match!.Objective!;
        FlagState theirs = o.TheirFlag(0)!;
        PlayerState runner = ours[1];

        // Your own flag can't be taken by your side.
        runner.Position = o.FlagOf(0)!.Home + new Vector3(0.3f, 0f, 0f);
        Step(sim, 2);
        Assert.True(o.FlagOf(0)!.AtHome);

        runner.Position = theirs.Home + new Vector3(FlagRules.PickupRadius * 0.8f, 0f, 0f);
        List<SimEvent> taken = Step(sim, 1);
        Assert.Contains(taken, e => e.Type == SimEventType.FlagTaken && e.PlayerId == runner.Id && e.Team == 1 && e.Extra == 1);
        Assert.Equal(runner.Id, theirs.Carrier);
        Assert.False(theirs.AtHome);
        Assert.Equal(!FlagRules.CarrierCanSprint, runner.SprintBlocked);
        Assert.True(o.IsKeyPlayer(runner.Id));

        // It goes where they go; nothing's decided until it's home.
        runner.Position = new Vector3(0f, 0f, -10f);
        Step(sim, 1);
        Assert.Equal(runner.Position, theirs.Position);
        Assert.Equal(MatchPhase.Live, sim.Match.Phase);

        runner.Position = o.FlagOf(0)!.Home + new Vector3(FlagRules.CaptureRadius * 0.8f, 0f, 0f);
        List<SimEvent> home = Step(sim, 2);
        Assert.Contains(home, e => e.Type == SimEventType.FlagCaptured && e.PlayerId == runner.Id && e.Team == 0 && e.Extra == 1);
        Assert.Equal(MatchPhase.Ended, sim.Match.Phase);
        Assert.Equal(RoundEnd.Captured, sim.Match.Result.Reason);
        Assert.Equal(0, sim.Match.Result.Winner);
        Assert.Equal(RoundOutcome.Captured, sim.Match.OutcomeFor(0));
        Assert.Equal(RoundOutcome.FlagLost, sim.Match.OutcomeFor(1));
        Assert.True(sim.Match.Outcome.IsWin());
        Assert.False(RoundOutcome.FlagLost.IsWin());
    }

    [Fact]
    public void A_carrier_hit_drops_it_their_own_side_sends_it_home_or_it_goes_home_by_itself()
    {
        (SimWorld sim, PlayerState[] ours, PlayerState[] theirs) = Round(ObjectiveKind.Flag);
        ObjectiveState o = sim.Match!.Objective!;
        FlagState flag = o.TheirFlag(0)!;
        PlayerState runner = ours[1];
        runner.Position = flag.Home;
        Step(sim, 1);
        Assert.Equal(runner.Id, flag.Carrier);

        // Hit out in the open: it falls where they were.
        Vector3 fell = new(-5f, 0f, -12f);
        runner.Position = fell;
        Step(sim, 1);
        runner.Alive = false;
        List<SimEvent> dropped = Step(sim, 1);
        Assert.Contains(dropped, e => e.Type == SimEventType.FlagDropped && e.PlayerId == runner.Id && e.Team == 1);
        Assert.True(flag.Down);
        Assert.Equal(fell, flag.Position);
        Assert.False(runner.SprintBlocked);

        // Another of yours picks it up again ...
        ours[2].Position = fell;
        Step(sim, 1);
        Assert.Equal(ours[2].Id, flag.Carrier);
        ours[2].Alive = false;
        Step(sim, 1);
        Assert.True(flag.Down);

        // ... but theirs touching it sends it home.
        theirs[1].Position = flag.Position + new Vector3(0.5f, 0f, 0f);
        List<SimEvent> back = Step(sim, 1);
        Assert.Contains(back, e => e.Type == SimEventType.FlagReturned && e.PlayerId == theirs[1].Id && e.Team == 1);
        Assert.True(flag.AtHome);
        Assert.Equal(flag.Home, flag.Position);

        // Left lying long enough (in a round of its own: both carriers above are out), it goes home by itself.
        (SimWorld again, PlayerState[] mates, _) = Round(ObjectiveKind.Flag);
        FlagState lying = again.Match!.Objective!.TheirFlag(0)!;
        mates[1].Position = lying.Home;
        Step(again, 1);
        mates[1].Position = fell;
        Step(again, 1);
        mates[1].Alive = false;
        Step(again, 1);
        Assert.True(lying.Down);
        List<SimEvent> wait = Step(again, (int)(FlagRules.ReturnAfter * TestData.Config.TickRate) + 2);
        Assert.Contains(wait, e => e.Type == SimEventType.FlagReturned && e.PlayerId == -1);
        Assert.True(lying.AtHome);
    }

    [Fact]
    public void Capture_the_flag_out_of_time_is_nobodys()
    {
        (SimWorld sim, _, _) = Round(ObjectiveKind.Flag, timeLimit: 2f);
        Step(sim, 3 * (int)TestData.Config.TickRate);
        Assert.Equal(RoundEnd.TimeUp, sim.Match!.Result.Reason);
        Assert.Equal(-1, sim.Match.Result.Winner);
        Assert.Equal(RoundOutcome.TimeUp, sim.Match.OutcomeFor(0));
        Assert.Equal(RoundOutcome.TimeUp, sim.Match.OutcomeFor(1));
    }

    [Fact]
    public void The_vip_is_one_of_the_attacking_sides_bots_unarmed_slower_and_the_same_on_every_copy()
    {
        var picked = new HashSet<int>();
        for (ulong seed = 1; seed <= 12; seed++)
        {
            (SimWorld sim, PlayerState[] ours, _) = Round(ObjectiveKind.Escort, seed);
            ObjectiveState o = sim.Match!.Objective!;
            // Never the person (you escort them), always on your side, and the same again from the same seed.
            Assert.Contains(o.Vip, new[] { 1, 2 });
            Assert.Equal(o.Vip, Round(ObjectiveKind.Escort, seed).Sim.Match!.Objective!.Vip);
            PlayerState vip = sim.FindPlayer(o.Vip)!;
            Assert.True(vip.Unarmed && vip.SprintBlocked);
            Assert.Equal(EscortRules.SpeedShare, vip.SpeedScale);
            Assert.All(ours.Where(p => p.Id != o.Vip), p => Assert.False(p.Unarmed));
            Assert.Equal(o.Vip, o.Carrier);
            Assert.True(o.IsKeyPlayer(o.Vip));
            picked.Add(o.Vip);
        }

        Assert.Equal(2, picked.Count);

        // A side of people only: one of them.
        (SimWorld people, _, _) = Round(ObjectiveKind.Escort, 3, people: new[] { 0, 1, 2 });
        Assert.Contains(people.Match!.Objective!.Vip, new[] { 0, 1, 2 });
    }

    [Fact]
    public void The_vip_cant_fire_and_moves_slower()
    {
        (SimWorld sim, PlayerState[] ours, _) = Round(ObjectiveKind.Escort);
        ObjectiveState o = sim.Match!.Objective!;
        PlayerState vip = sim.FindPlayer(o.Vip)!;
        PlayerState mate = ours.First(p => p.Id != vip.Id && p.Id != 0);
        // Both pull the trigger a few times, standing ...
        List<SimEvent> events = Step(sim, (int)TestData.Config.TickRate, i => new InputCommand
        {
            Buttons = sim.Tick % 20 < 10 ? InputButtons.Fire : InputButtons.None, Yaw = 0f,
        });
        Assert.DoesNotContain(events, e => e.Type == SimEventType.ShotFired && e.PlayerId == vip.Id);
        Assert.Contains(events, e => e.Type == SimEventType.ShotFired && e.PlayerId == mate.Id);

        // ... then try to sprint straight on for two seconds (the movement rules, as a host runs them each tick).
        Vector3 vipFrom = vip.Position, mateFrom = mate.Position;
        var sprint = new InputCommand { Buttons = InputButtons.Sprint, Move = new Vector2(0f, 1f), Yaw = 0f };
        for (int t = 0; t < 2 * (int)TestData.Config.TickRate; t++)
        {
            foreach (PlayerState p in new[] { vip, mate })
            {
                MovementResult r = MovementModel.Step(p, sprint, TestData.Config.Movement, sim.Dt, grounded: true);
                p.Velocity = r.HorizontalVelocity;
                p.Position += r.HorizontalVelocity * sim.Dt;
            }
        }
        float vipRan = Flat(vip.Position - vipFrom), mateRan = Flat(mate.Position - mateFrom);
        _out.WriteLine($"in 2 s the VIP covered {vipRan:0.0} m, a teammate {mateRan:0.0} m");
        // No sprint, and the running speed scaled: at most the VIP's pace for the two seconds (less the speeding up).
        float pace = TestData.Config.Movement.RunSpeed * EscortRules.SpeedShare * 2f;
        Assert.InRange(vipRan, pace * 0.85f, pace + 0.05f);
        Assert.True(mateRan > vipRan + 3f, "the teammate didn't sprint away from the VIP");
    }

    [Fact]
    public void The_vip_out_through_a_far_way_out_wins_for_the_attackers()
    {
        (SimWorld sim, _, _) = Round(ObjectiveKind.Escort);
        ObjectiveState o = sim.Match!.Objective!;
        PlayerState vip = sim.FindPlayer(o.Vip)!;
        float farthest = Level.Objectives.Exits.Max(e => Flat(e.Position - o.VipStart));
        _out.WriteLine($"VIP {vip.Name} at {o.VipStart}; counting: {string.Join(", ", o.CountingExits.Select(i => Level.Objectives.Exits[i].Name))}");
        Assert.NotEmpty(o.CountingExits);
        Assert.All(o.CountingExits, i => Assert.True(Flat(Level.Objectives.Exits[i].Position - o.VipStart) >=
                                                     MathF.Max(EscortRules.MinExitDistance, EscortRules.FarShare * farthest) - 1e-3f));

        // The west breach is right by where your side starts: it doesn't count.
        int near = Enumerable.Range(0, Level.Objectives.Exits.Count).MinBy(i => Flat(Level.Objectives.Exits[i].Position - o.VipStart));
        Assert.False(o.ExitCounts(near));
        vip.Position = Level.Objectives.Exits[near].Position;
        Step(sim, 2);
        Assert.False(o.Done);

        int far = o.CountingExits[0];
        vip.Position = Level.Objectives.Exits[far].Position + new Vector3(1f, 0f, 1f);
        List<SimEvent> events = Step(sim, 2);
        Assert.Contains(events, e => e.Type == SimEventType.VipEscaped && e.PlayerId == vip.Id && e.Extra == far);
        Assert.Equal(RoundEnd.Escorted, sim.Match.Result.Reason);
        Assert.Equal(RoundOutcome.Escorted, sim.Match.OutcomeFor(0));
        Assert.Equal(RoundOutcome.VipEscaped, sim.Match.OutcomeFor(1));
    }

    [Fact]
    public void The_vip_put_out_or_the_time_running_out_wins_for_the_defenders()
    {
        (SimWorld sim, _, _) = Round(ObjectiveKind.Escort);
        ObjectiveState o = sim.Match!.Objective!;
        sim.FindPlayer(o.Vip)!.Alive = false;
        Step(sim, (int)(TestData.Config.Rules.SettleTime * TestData.Config.TickRate) + 2);
        Assert.True(o.VipOut);
        Assert.Equal(RoundEnd.VipOut, sim.Match.Result.Reason);
        Assert.Equal(1, sim.Match.Result.Winner);
        Assert.Equal(RoundOutcome.VipLost, sim.Match.OutcomeFor(0));
        Assert.Equal(RoundOutcome.VipStopped, sim.Match.OutcomeFor(1));
        Assert.True(RoundOutcome.VipStopped.IsWin());

        (SimWorld late, _, _) = Round(ObjectiveKind.Escort, timeLimit: 2f);
        Step(late, 3 * (int)TestData.Config.TickRate);
        Assert.Equal(RoundEnd.TimeUp, late.Match!.Result.Reason);
        Assert.Equal(1, late.Match.Result.Winner);
        Assert.Equal(RoundOutcome.HeldOff, late.Match.OutcomeFor(1));
    }

    private static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);
}
