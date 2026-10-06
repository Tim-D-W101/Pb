using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;

namespace Pb.Sim.Tests;

/// <summary>Phase 2 round rules: briefing, outcomes, trades, time limit, pickups, stats and tiers.</summary>
public class MatchTests
{
    private static SimConfig Config => TestData.Config;

    private static MatchSetup Setup(float timeLimit = 900f, int startPods = 2, int botPods = 1, bool pickups = true,
        MatchModeKind mode = MatchModeKind.Solo) => new()
    {
        HeroId = 0, Mode = mode, TimeLimit = timeLimit, StartPods = startPods, BotPods = botPods, Pickups = pickups,
    };

    /// <summary>Open ground with the hero (id 0, team 0) at the origin facing −Z and opponents (team 1) in a row ahead.</summary>
    private static (SimWorld Sim, PlayerState Hero, PlayerState[] Opponents) Field(params float[] opponentDistances)
    {
        var sim = new SimWorld(Config);
        sim.Collision.Add(new PlaneShape(Vector3.UnitY, 0f), Config.Surfaces.Get("turf"), "ground");
        PlayerState hero = sim.AddPlayer(0, 0, Vector3.Zero, 0f);
        PlayerState[] opponents = opponentDistances
            .Select((d, i) => sim.AddPlayer(i + 1, 1, new Vector3(i * 3f, 0f, -d), MathF.PI))
            .ToArray();
        return (sim, hero, opponents);
    }

    /// <summary>Steps the sim with one command per player (missing ones idle) and collects the events.</summary>
    private static List<SimEvent> Run(SimWorld sim, int ticks, Func<int, PlayerState, InputCommand>? command = null)
    {
        var events = new List<SimEvent>();
        var commands = new InputCommand[sim.Players.Count];
        for (int t = 0; t < ticks; t++)
        {
            for (int i = 0; i < commands.Length; i++)
            {
                commands[i] = command?.Invoke(t, sim.Players[i]) ?? default;
            }

            sim.Step(commands);
            events.AddRange(sim.Events.Items.ToArray());
            sim.Events.Clear();
        }

        return events;
    }

    /// <summary>Aims <paramref name="shooter"/> at <paramref name="target"/>'s chest and pulls the trigger on tick <paramref name="at"/>.</summary>
    private static InputCommand Shoot(PlayerState shooter, PlayerState target, int tick, int at)
    {
        (float yaw, float pitch) = ViewAngles.FromDirection(target.Position + new Vector3(0f, 1.15f, 0f) - shooter.EyePosition);
        return new InputCommand { Yaw = yaw, Pitch = pitch, Buttons = tick == at ? InputButtons.Fire : InputButtons.None };
    }

    [Fact]
    public void The_briefing_holds_fire_and_the_clock_until_the_round_goes_live()
    {
        (SimWorld sim, PlayerState hero, PlayerState[] opponents) = Field(10f);
        MatchState match = sim.StartMatch(Setup());
        Assert.Equal(MatchPhase.Briefing, match.Phase);
        Assert.False(sim.IsLive);

        List<SimEvent> briefing = Run(sim, 60, (t, p) => p == hero ? Shoot(hero, opponents[0], t, 10) : default);
        Assert.DoesNotContain(briefing, e => e.Type == SimEventType.ShotFired);
        Assert.Equal(0f, match.Elapsed);

        sim.GoLive();
        Assert.Equal(MatchPhase.Live, match.Phase);
        // A shot off to the side, so the round goes on.
        List<SimEvent> live = Run(sim, 60, (t, p) =>
            p == hero ? new InputCommand { Yaw = MathF.PI / 2f, Buttons = t == 10 ? InputButtons.Fire : InputButtons.None } : default);
        Assert.Contains(live, e => e.Type == SimEventType.MatchPhaseChanged && e.Extra == (int)MatchPhase.Live);
        Assert.Contains(live, e => e.Type == SimEventType.ShotFired);
        Assert.Equal(60 * Config.Dt, match.Elapsed, 3);
    }

    [Fact]
    public void Clearing_every_opponent_wins_and_the_stats_add_up()
    {
        (SimWorld sim, PlayerState hero, PlayerState[] opponents) = Field(10f, 12f);
        MatchState match = sim.StartMatch(Setup());
        sim.GoLive();
        List<SimEvent> events = Run(sim, 240, (t, p) =>
            p != hero ? default
            : t < 120 ? Shoot(hero, opponents[0], t, 20)
            : Shoot(hero, opponents[1], t, 140));

        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(RoundOutcome.Cleared, match.Outcome);
        SimEvent ended = Assert.Single(events, e => e.Type == SimEventType.RoundEnded);
        Assert.Equal((int)RoundOutcome.Cleared, ended.Extra);

        PlayerStats stats = match.StatsFor(hero.Id)!;
        Assert.Equal(2, stats.Shots);
        Assert.Equal(2, stats.Hits);
        Assert.Equal(2, stats.Eliminations);
        Assert.Equal(1f, stats.Accuracy);
        Assert.Equal(match.Elapsed, stats.TimeIn, 3);

        // Nothing changes once it's over, and nobody can fire.
        float elapsed = match.Elapsed;
        List<SimEvent> after = Run(sim, 30, (t, p) => p == hero ? Shoot(hero, opponents[0], t, 5) : default);
        Assert.DoesNotContain(after, e => e.Type is SimEventType.ShotFired or SimEventType.RoundEnded);
        Assert.Equal(elapsed, match.Elapsed);
    }

    [Fact]
    public void Going_out_together_with_the_last_opponent_is_a_trade()
    {
        // Both fire on the same tick at 15 m: both balls are in the air when the first one lands.
        (SimWorld sim, PlayerState hero, PlayerState[] opponents) = Field(15f);
        PlayerState opponent = opponents[0];
        MatchState match = sim.StartMatch(Setup());
        sim.GoLive();
        Run(sim, 120, (t, p) => p == hero ? Shoot(hero, opponent, t, 10) : Shoot(opponent, hero, t, 10));

        Assert.False(hero.Alive);
        Assert.False(opponent.Alive);
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(Config.Rules.TradeCountsAsClear ? RoundOutcome.Cleared : RoundOutcome.Traded, match.Outcome);
    }

    [Fact]
    public void Being_hit_loses_the_round_and_time_running_out_ends_it()
    {
        (SimWorld sim, PlayerState hero, PlayerState[] opponents) = Field(10f, 25f);
        MatchState match = sim.StartMatch(Setup());
        sim.GoLive();
        Run(sim, 120, (t, p) => p == opponents[0] ? Shoot(opponents[0], hero, t, 10) : default);
        Assert.Equal(RoundOutcome.Eliminated, match.Outcome);

        (SimWorld sim2, _, _) = Field(10f);
        MatchState timed = sim2.StartMatch(Setup(timeLimit: 0.5f));
        sim2.GoLive();
        List<SimEvent> events = Run(sim2, 70);
        Assert.Equal(RoundOutcome.TimeUp, timed.Outcome);
        Assert.InRange(timed.Elapsed, 0.5f, 0.5f + 1.5f * Config.Dt);
        Assert.Equal(0f, timed.TimeLeft);
        Assert.Single(events, e => e.Type == SimEventType.RoundEnded);
    }

    [Fact]
    public void Pickups_go_to_whoever_has_room_and_only_once()
    {
        var sim = new SimWorld(Config);
        LevelLayout level = TestData.Data.Levels["oxbarrow_works"];
        sim.LoadLevel(level);
        PickupSpec pod = level.Pickups.First(p => p.Kind == PickupKind.Pod);
        PickupSpec air = level.Pickups.First(p => p.Kind == PickupKind.Air);
        PlayerState hero = sim.AddPlayer(0, 0, pod.Position, 0f);
        sim.AddPlayer(1, 1, level.OpponentSpawns[^1].Position, 0f); // someone left to clear, so the round goes on
        int slots = hero.Marker.Paint.Params.PodCount;
        MatchState match = sim.StartMatch(Setup(startPods: slots));
        sim.GoLive();

        // Every pod slot is full: the pod stays where it is.
        Assert.DoesNotContain(Run(sim, 2), e => e.Type == SimEventType.PickupTaken);

        // With an empty slot it's taken, once, and the slot is full again.
        hero.Marker.Paint.FillWith(slots - 1);
        SimEvent taken = Assert.Single(Run(sim, 2), e => e.Type == SimEventType.PickupTaken);
        Assert.Equal(hero.Id, taken.PlayerId);
        Assert.Equal((int)PickupKind.Pod, taken.Extra);
        Assert.Equal(slots * hero.Marker.Paint.Params.PodCapacity, hero.Marker.Paint.PodsRemaining);
        hero.Marker.Paint.FillWith(0);
        Assert.DoesNotContain(Run(sim, 2), e => e.Type == SimEventType.PickupTaken);
        Assert.Equal(1, match.StatsFor(hero.Id)!.Pickups);

        // Air only tops up a tank that's run down.
        hero.Position = air.Position;
        Assert.DoesNotContain(Run(sim, 2), e => e.Type == SimEventType.PickupTaken);
        while (hero.Marker.Air.FillFraction >= Config.Rules.AirPickupBelow)
        {
            hero.Marker.Air.ConsumeShot();
        }

        Assert.Single(Run(sim, 2), e => e.Type == SimEventType.PickupTaken && e.Extra == (int)PickupKind.Air);
        Assert.Equal(1f, hero.Marker.Air.FillFraction, 3);
    }

    [Fact]
    public void Tiers_set_the_gear_and_the_roster_must_name_real_spawns()
    {
        TierDef hard = TestData.Data.Areas.Areas.First(l => l.Id == "oxbarrow_works").Tiers.First(t => t.Id == "hard");
        MatchSetup setup = MatchSetup.From(hard, heroId: 0);
        Assert.Equal(hard.TimeLimit_s, setup.TimeLimit);

        (SimWorld sim, PlayerState hero, PlayerState[] opponents) = Field(10f);
        sim.StartMatch(setup);
        Assert.Equal(hard.StartPods * hero.Marker.Paint.Params.PodCapacity, hero.Marker.Paint.PodsRemaining);
        Assert.Equal(hard.BotPods * hero.Marker.Paint.Params.PodCapacity, opponents[0].Marker.Paint.PodsRemaining);
        Assert.Equal(hero.Marker.Paint.Params.Capacity, hero.Marker.Paint.Loader);

        var edited = new EditedDataSource(TestData.Source).Edit("levels/areas.jsonc", s => s.Replace("\"pump_house\", \"east_scrap\"", "\"pump_house\", \"nowhere\""));
        var ex = Assert.Throws<DataException>(() => GameData.Load(edited));
        Assert.Contains("levels/areas.jsonc", ex.Message);
        Assert.Contains("areas.oxbarrow_works.roster", ex.Message);
        Assert.Contains("nowhere", ex.Message);
    }

    /// <summary>Open ground with players at (x, z) on the given teams, hero first (id 0), facing −Z.</summary>
    private static (SimWorld Sim, PlayerState[] Players) Teams(params (byte Team, float X, float Z)[] players)
    {
        var sim = new SimWorld(Config);
        sim.Collision.Add(new PlaneShape(Vector3.UnitY, 0f), Config.Surfaces.Get("turf"), "ground");
        PlayerState[] added = players.Select((p, i) => sim.AddPlayer(i, p.Team, new Vector3(p.X, 0f, p.Z), MathF.PI)).ToArray();
        return (sim, added);
    }

    /// <summary>Each shot is (shooter, target, tick): the shooter aims at the target's chest and fires on that tick.</summary>
    private static Func<int, PlayerState, InputCommand> Script(params (PlayerState Shooter, PlayerState Target, int Tick)[] shots) => (t, p) =>
    {
        foreach ((PlayerState shooter, PlayerState target, int tick) in shots)
        {
            if (p == shooter && t >= tick - 5 && t <= tick)
            {
                return Shoot(shooter, target, t, tick);
            }
        }

        return default;
    };

    [Fact]
    public void In_teams_the_round_goes_on_while_a_teammate_is_in_and_the_last_team_standing_wins()
    {
        (SimWorld sim, PlayerState[] p) = Teams((0, 0f, 0f), (0, 6f, 0f), (1, 0f, -14f), (1, 12f, -14f));
        PlayerState hero = p[0], mate = p[1], first = p[2], second = p[3];
        MatchState match = sim.StartMatch(Setup(botPods: 3, mode: MatchModeKind.Teams));
        Assert.Equal(3 * mate.Marker.Paint.Params.PodCapacity, mate.Marker.Paint.PodsRemaining);
        sim.GoLive();

        // Their first player gets you: your teammate is still in, so the round goes on.
        Run(sim, 60, Script((first, hero, 10)));
        Assert.False(hero.Alive);
        Assert.Equal(MatchPhase.Live, match.Phase);
        Assert.Equal(RoundOutcome.None, match.Outcome);

        // Your teammate puts both of theirs out: your team wins.
        Run(sim, 240, Script((mate, first, 10), (mate, second, 130)));
        Assert.False(first.Alive);
        Assert.False(second.Alive);
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(RoundOutcome.Cleared, match.Outcome);
        Assert.Equal(2, match.StatsFor(mate.Id)!.Eliminations);
    }

    [Fact]
    public void A_team_is_out_when_its_last_player_is()
    {
        (SimWorld sim, PlayerState[] p) = Teams((0, 0f, 0f), (0, 6f, 0f), (1, 3f, -14f));
        PlayerState hero = p[0], mate = p[1], them = p[2];
        MatchState match = sim.StartMatch(Setup(mode: MatchModeKind.Teams));
        sim.GoLive();
        Run(sim, 240, Script((them, hero, 10), (them, mate, 130)));
        Assert.Equal(RoundOutcome.Eliminated, match.Outcome);
    }

    [Fact]
    public void A_teammates_hit_puts_you_out_but_counts_as_no_elimination()
    {
        (SimWorld sim, PlayerState[] p) = Teams((0, 0f, 0f), (0, 0f, -8f), (1, 20f, -30f));
        PlayerState hero = p[0], mate = p[1];
        MatchState match = sim.StartMatch(Setup(mode: MatchModeKind.Teams));
        sim.GoLive();
        List<SimEvent> events = Run(sim, 60, Script((mate, hero, 10)));

        Assert.False(hero.Alive);
        Assert.Contains(events, e => e.Type == SimEventType.PlayerEliminated && e.PlayerId == mate.Id && e.TargetId == hero.Id);
        Assert.Equal(0, match.StatsFor(mate.Id)!.Eliminations);
        Assert.Equal(0, match.StatsFor(mate.Id)!.Hits);
        Assert.Equal(0, mate.Eliminations);
        Assert.Equal(RoundOutcome.None, match.Outcome); // your teammate is still in
    }

    [Fact]
    public void In_free_for_all_the_rest_play_on_after_you_are_out_and_placings_follow_the_order_out()
    {
        // Everyone on a team of their own, in a row 10 m apart, with you facing them.
        (SimWorld sim, PlayerState[] p) = Teams((0, 0f, 0f), (1, 0f, -10f), (2, 10f, -10f), (3, 20f, -10f));
        PlayerState hero = p[0], a = p[1], b = p[2], c = p[3];
        MatchState match = sim.StartMatch(Setup(mode: MatchModeKind.FreeForAll));
        sim.GoLive();

        Run(sim, 60, Script((a, hero, 10)));
        Assert.False(hero.Alive);
        Assert.Equal(RoundOutcome.None, match.Outcome); // two others are still fighting

        Run(sim, 60, Script((b, a, 10)));
        Assert.False(a.Alive);
        Assert.Equal(RoundOutcome.None, match.Outcome);

        Run(sim, 120, Script((c, b, 10)));
        Assert.False(b.Alive);
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(RoundOutcome.Eliminated, match.Outcome); // someone else won
        Assert.Equal(new[] { 4, 3, 2, 1 }, p.Select(x => match.Placing(x.Id)));
        Assert.Equal(1, match.StatsFor(c.Id)!.Eliminations);
    }

    [Fact]
    public void The_last_one_standing_in_free_for_all_wins_and_shares_no_place()
    {
        (SimWorld sim, PlayerState[] p) = Teams((0, 0f, 0f), (1, -4f, -12f), (2, 4f, -12f));
        PlayerState hero = p[0];
        MatchState match = sim.StartMatch(Setup(mode: MatchModeKind.FreeForAll));
        sim.GoLive();
        Run(sim, 240, Script((hero, p[1], 10), (hero, p[2], 130)));

        Assert.Equal(RoundOutcome.Cleared, match.Outcome);
        Assert.Equal(1, match.Placing(hero.Id));
        Assert.Equal(new[] { 3, 2 }, new[] { match.Placing(p[1].Id), match.Placing(p[2].Id) });
        Assert.Equal(2, match.StatsFor(hero.Id)!.Eliminations);
    }

    [Fact]
    public void Players_out_on_the_same_tick_share_a_place()
    {
        (SimWorld sim, PlayerState[] p) = Teams((0, 0f, 0f), (1, 0f, -20f), (2, 20f, -20f), (3, -20f, -20f));
        MatchState match = sim.StartMatch(Setup(mode: MatchModeKind.FreeForAll));
        match.StatsFor(p[3].Id)!.OutTick = 20;
        match.StatsFor(p[1].Id)!.OutTick = 50;
        match.StatsFor(p[2].Id)!.OutTick = 50;

        Assert.Equal(1, match.Placing(p[0].Id)); // still in
        Assert.Equal(2, match.Placing(p[1].Id));
        Assert.Equal(2, match.Placing(p[2].Id));
        Assert.Equal(4, match.Placing(p[3].Id));
        Assert.Equal(0, match.Placing(99));
    }

    [Fact]
    public void A_live_free_for_all_round_steps_without_allocating()
    {
        (SimWorld sim, PlayerState[] p) = Teams((0, 0f, 0f), (1, 0f, -30f), (2, 30f, -30f), (3, -30f, -30f));
        sim.StartMatch(Setup(mode: MatchModeKind.FreeForAll));
        sim.GoLive();
        var commands = new InputCommand[sim.Players.Count];

        void StepTicks(int from, int count)
        {
            for (int tick = from; tick < from + count; tick++)
            {
                commands[0] = new InputCommand { Tick = tick, Yaw = MathF.PI / 2f, Buttons = tick % 12 == 0 ? InputButtons.Fire : InputButtons.None };
                sim.Step(commands);
                sim.Events.Clear();
            }
        }

        StepTicks(0, 240);
        Assert.Equal(0, Allocations.During(() => StepTicks(240, 240)));
        Assert.Equal(MatchPhase.Live, sim.Match!.Phase);
    }

    [Fact]
    public void A_live_round_steps_without_allocating()
    {
        // The hero fires to the side every few ticks, so balls, stats, pickups and the outcome check all run.
        (SimWorld sim, PlayerState hero, _) = Field(30f, 40f);
        sim.Pickups.Load(new[] { new PickupSpec { Id = "pod", Kind = PickupKind.Pod, Position = new Vector3(20f, 0f, 20f) } });
        sim.StartMatch(Setup());
        sim.GoLive();
        var commands = new InputCommand[sim.Players.Count];

        void StepTicks(int from, int count)
        {
            for (int tick = from; tick < from + count; tick++)
            {
                commands[0] = new InputCommand { Tick = tick, Yaw = MathF.PI / 2f, Buttons = tick % 12 == 0 ? InputButtons.Fire : InputButtons.None };
                sim.Step(commands);
                sim.Events.Clear();
            }
        }

        StepTicks(0, 240); // warm up
        Assert.Equal(20, sim.Match!.StatsFor(hero.Id)!.Shots); // 10 a second, under the marker's rate cap

        Assert.Equal(0, Allocations.During(() => StepTicks(240, 240)));
        Assert.Equal(MatchPhase.Live, sim.Match.Phase);
    }
}
