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

    private static MatchSetup Setup(float timeLimit = 900f, int startPods = 2, int opponentPods = 1, bool pickups = true) => new()
    {
        HeroId = 0, TimeLimit = timeLimit, StartPods = startPods, OpponentPods = opponentPods, Pickups = pickups,
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
    public void Tiers_set_the_gear_and_their_rosters_must_name_real_spawns()
    {
        LadderTierDef hard = TestData.Data.Ladder.Levels.First(l => l.Id == "oxbarrow_works").Tiers!.First(t => t.Id == "hard");
        MatchSetup setup = MatchSetup.From(hard, heroId: 0);
        Assert.Equal(hard.TimeLimit_s, setup.TimeLimit);

        (SimWorld sim, PlayerState hero, PlayerState[] opponents) = Field(10f);
        sim.StartMatch(setup);
        Assert.Equal(hard.StartPods * hero.Marker.Paint.Params.PodCapacity, hero.Marker.Paint.PodsRemaining);
        Assert.Equal(hard.OpponentPods * hero.Marker.Paint.Params.PodCapacity, opponents[0].Marker.Paint.PodsRemaining);
        Assert.Equal(hero.Marker.Paint.Params.Capacity, hero.Marker.Paint.Loader);

        var edited = new EditedDataSource(TestData.Source).Edit("levels/ladder.jsonc", s => s.Replace("\"pump_house\", \"east_scrap\"", "\"pump_house\", \"nowhere\""));
        var ex = Assert.Throws<DataException>(() => GameData.Load(edited));
        Assert.Contains("levels/ladder.jsonc", ex.Message);
        Assert.Contains("tiers.hard.opponents", ex.Message);
        Assert.Contains("nowhere", ex.Message);
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
        long before = GC.GetAllocatedBytesForCurrentThread();
        StepTicks(240, 240);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(MatchPhase.Live, sim.Match!.Phase);
        Assert.Equal(40, sim.Match.StatsFor(hero.Id)!.Shots); // 10 a second, under the marker's rate cap
    }
}
