using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;

namespace Pb.Sim.Tests;

/// <summary>Phase 5 (M5.8): arcade's rules: coming back after a hit, the shield, where you come back, and the clock's result.</summary>
public class ArcadeTests
{
    private const int Second = 120;

    private static SimConfig Config => TestData.Config;

    private static ArcadeRules Rules => Config.Rules.Arcade;

    /// <summary>An arcade round's setup: <paramref name="kind"/>, the tier's pods (2 for people, 1 for bots).</summary>
    private static MatchSetup Setup(MatchModeKind kind = MatchModeKind.Teams, float? timeLimit = null)
    {
        MatchSetup setup = new MatchSetup { HeroId = 0, Mode = kind, TimeLimit = 900f, StartPods = 2, BotPods = 1, Pickups = false }
            .As(MatchFormat.Arcade, Config.Rules, onField: false);
        return timeLimit is { } t ? setup.WithTimeLimit(t) : setup;
    }

    /// <summary>Open ground: you (id 0, team 0) at the origin facing −Z, and opponents on team 1 (or, in free-for-all, each their own) in a row ahead.</summary>
    private static (SimWorld Sim, PlayerState Hero, PlayerState[] Opponents) Ground(MatchModeKind kind, params float[] distances)
    {
        var sim = new SimWorld(Config);
        sim.Collision.Add(new PlaneShape(Vector3.UnitY, 0f), Config.Surfaces.Get("turf"), "ground");
        PlayerState hero = sim.AddPlayer(0, 0, Vector3.Zero, 0f);
        PlayerState[] opponents = distances
            .Select((d, i) => sim.AddPlayer(i + 1, kind == MatchModeKind.FreeForAll ? (byte)(i + 1) : (byte)1, new Vector3(i * 3f, 0f, -d), MathF.PI))
            .ToArray();
        sim.StartMatch(Setup(kind));
        sim.GoLive();
        return (sim, hero, opponents);
    }

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

    /// <summary>Aims <paramref name="shooter"/> at <paramref name="target"/>'s chest, pulling the trigger on tick <paramref name="at"/>.</summary>
    private static InputCommand Shoot(PlayerState shooter, PlayerState target, int tick, int at)
    {
        (float yaw, float pitch) = ViewAngles.FromDirection(target.Position + new Vector3(0f, 1.15f, 0f) - shooter.EyePosition);
        return new InputCommand { Yaw = yaw, Pitch = pitch, Buttons = tick == at ? InputButtons.Fire : InputButtons.None };
    }

    [Fact]
    public void Out_you_come_back_after_the_respawn_time_with_full_gear_and_a_moment_of_shield()
    {
        (SimWorld sim, PlayerState hero, PlayerState[] opponents) = Ground(MatchModeKind.Teams, 10f);
        PlayerState them = opponents[0];
        List<SimEvent> events = Run(sim, Second, (t, p) => p == hero ? Shoot(hero, them, t, 2) : default);
        SimEvent outEvent = Assert.Single(events, e => e.Type == SimEventType.PlayerEliminated);
        Assert.Equal((hero.Id, them.Id), (outEvent.PlayerId, outEvent.TargetId));
        Assert.False(them.Alive);
        // Spend some of their paint and air while they're out, to see it come back.
        them.Marker.Paint.FillWith(0);

        // Not before the respawn time…
        int respawnAt = outEvent.Tick + (int)MathF.Round(Rules.Respawn * Second);
        events = Run(sim, respawnAt - sim.Tick);
        Assert.DoesNotContain(events, e => e.Type == SimEventType.PlayerRespawned);
        Assert.False(them.Alive);

        // … then back: in, standing, a full loader and the round's pods, shielded for the shield time.
        events = Run(sim, 1);
        SimEvent back = Assert.Single(events, e => e.Type == SimEventType.PlayerRespawned);
        Assert.Equal((them.Id, them.Team), (back.PlayerId, back.Team));
        Assert.True(them.Alive && them.Present);
        Assert.Equal(Stance.Standing, them.Stance);
        Assert.True(them.Marker.Paint.LoaderFull);
        Assert.Equal(1, them.Marker.Paint.Pods.ToArray().Count(balls => balls > 0)); // the round's pods for a bot
        Assert.Equal(sim.Tick - 1 + (int)MathF.Round(Rules.Shield * Second), them.ShieldUntilTick);
        Assert.Equal(back.Extra, them.ShieldUntilTick);
        MatchState match = sim.Match!;
        Assert.Equal((1, 1, -1), (match.StatsFor(them.Id)!.TimesOut, them.TimesOut, match.StatsFor(them.Id)!.OutTick));
        Assert.Equal(1, match.StatsFor(hero.Id)!.Eliminations);
        Assert.Equal(MatchPhase.Live, match.Phase);
    }

    [Fact]
    public void The_shield_takes_paint_but_not_you_until_it_runs_out()
    {
        (SimWorld sim, PlayerState hero, PlayerState[] opponents) = Ground(MatchModeKind.Teams, 10f);
        PlayerState them = opponents[0];
        Run(sim, Second, (t, p) => p == hero ? Shoot(hero, them, t, 2) : default);
        Run(sim, (int)(Rules.Respawn * Second) + 2);
        Assert.True(them.ShieldedAt(sim.Tick));

        // Shielded: the ball breaks on them (paint) but they stay in.
        List<SimEvent> events = Run(sim, Second / 2, (t, p) => p == hero ? Shoot(hero, them, t, 2) : default);
        Assert.Contains(events, e => e.Type == SimEventType.BallBroke && PlayerHitboxes.IsPlayer(e.TargetId) &&
                                     PlayerHitboxes.PlayerIdOf(e.TargetId) == them.Id);
        Assert.DoesNotContain(events, e => e.Type == SimEventType.PlayerEliminated);
        Assert.True(them.Alive);

        // Once it's run out, a hit puts them out again.
        Run(sim, Math.Max(0, them.ShieldUntilTick - sim.Tick) + 1);
        Assert.False(them.ShieldedAt(sim.Tick));
        events = Run(sim, Second, (t, p) => p == hero ? Shoot(hero, them, t, 2) : default);
        Assert.Contains(events, e => e.Type == SimEventType.PlayerEliminated && e.TargetId == them.Id);
        Assert.Equal(2, sim.Match!.StatsFor(hero.Id)!.Eliminations);
    }

    [Fact]
    public void Firing_ends_the_shield_at_once()
    {
        (SimWorld sim, PlayerState hero, PlayerState[] opponents) = Ground(MatchModeKind.Teams, 10f);
        PlayerState them = opponents[0];
        Run(sim, Second, (t, p) => p == hero ? Shoot(hero, them, t, 2) : default);
        Run(sim, (int)(Rules.Respawn * Second) + 2);
        Assert.True(them.ShieldedAt(sim.Tick));

        // They shoot back (wide): the shield's gone, and the next hit counts.
        Run(sim, 3, (t, p) => p == them ? new InputCommand { Yaw = 0.5f, Buttons = t == 0 ? InputButtons.Fire : InputButtons.None } : default);
        Assert.False(them.ShieldedAt(sim.Tick));
        List<SimEvent> events = Run(sim, Second, (t, p) => p == hero ? Shoot(hero, them, t, 2) : default);
        Assert.Contains(events, e => e.Type == SimEventType.PlayerEliminated && e.TargetId == them.Id);
    }

    [Fact]
    public void Nobody_is_the_last_standing_the_round_goes_to_the_clock_and_the_most_eliminations_wins()
    {
        // 1 v 1: you put them out; they're back, and the round goes on until time up, which you win 1–0.
        var sim = new SimWorld(Config);
        sim.Collision.Add(new PlaneShape(Vector3.UnitY, 0f), Config.Surfaces.Get("turf"), "ground");
        PlayerState hero = sim.AddPlayer(0, 0, Vector3.Zero, 0f);
        PlayerState them = sim.AddPlayer(1, 1, new Vector3(0f, 0f, -10f), MathF.PI);
        MatchState match = sim.StartMatch(Setup(timeLimit: 6f));
        sim.GoLive();
        Run(sim, Second, (t, p) => p == hero ? Shoot(hero, them, t, 2) : default);
        Assert.False(them.Alive);
        Assert.Equal(MatchPhase.Live, match.Phase);
        Run(sim, 6 * Second);
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(new MatchResult(RoundEnd.MostEliminations, 0), match.Result);
        Assert.Equal((RoundOutcome.MostEliminations, RoundOutcome.FewerEliminations), (match.OutcomeFor(0), match.OutcomeFor(1)));
        Assert.True(match.OutcomeFor(0).IsWin());
    }

    [Fact]
    public void Level_on_eliminations_nobody_wins_and_in_free_for_all_it_goes_by_each_players_own()
    {
        (SimWorld level, _, _) = Ground(MatchModeKind.Teams, 10f);
        MatchState even = level.StartMatch(Setup(timeLimit: 2f));
        level.GoLive();
        Run(level, 3 * Second);
        Assert.Equal(new MatchResult(RoundEnd.TimeUp, -1), even.Result);

        // Free-for-all: you put out two of the three; the most eliminations is yours alone.
        (SimWorld ffa, PlayerState hero, PlayerState[] others) = Ground(MatchModeKind.FreeForAll, 10f, 12f, 14f);
        MatchState match = ffa.StartMatch(Setup(MatchModeKind.FreeForAll, timeLimit: 5f));
        ffa.GoLive();
        Run(ffa, Second, (t, p) => p == hero ? Shoot(hero, others[0], t, 2) : default);
        Run(ffa, Second, (t, p) => p == hero ? Shoot(hero, others[2], t, 2) : default);
        Assert.Equal(2, match.StatsFor(hero.Id)!.Eliminations);
        Assert.Equal(1, match.Placing(hero.Id));
        Assert.Equal(2, match.Placing(others[1].Id));
        Run(ffa, 4 * Second);
        Assert.Equal(new MatchResult(RoundEnd.MostEliminations, hero.Team), match.Result);
    }

    [Fact]
    public void Someone_who_left_the_game_doesnt_come_back()
    {
        (SimWorld sim, _, PlayerState[] opponents) = Ground(MatchModeKind.Teams, 10f, 12f);
        sim.Withdraw(opponents[1].Id);
        List<SimEvent> events = Run(sim, (int)((Rules.Respawn + 1f) * Second));
        Assert.Contains(events, e => e.Type == SimEventType.PlayerEliminated && e.TargetId == opponents[1].Id);
        Assert.DoesNotContain(events, e => e.Type == SimEventType.PlayerRespawned);
        Assert.False(opponents[1].Alive);
    }

    [Fact]
    public void Back_in_a_compound_at_a_start_none_of_the_other_side_can_see_clear_of_everyone()
    {
        LevelLayout level = TestData.Data.Levels["oxbarrow_works"];
        var sim = new SimWorld(Config);
        sim.LoadLevel(level);
        // Two of theirs at opponent spawns, ours at the entry; one of ours has gone out and is due back.
        PlayerState who = sim.AddPlayer(0, 0, level.PlayerSpawn, level.PlayerSpawnYaw);
        sim.AddPlayer(1, 0, level.PlayerSpawn + new Vector3(2f, 0f, 0f), level.PlayerSpawnYaw);
        PlayerState[] theirs = level.OpponentSpawns.Take(3).Select((s, i) => sim.AddPlayer(2 + i, 1, s.Position, s.Yaw)).ToArray();
        sim.StartMatch(Setup());
        sim.GoLive();
        who.Alive = false;
        float eye = Config.Movement.StandEyeHeight;
        for (int k = 0; k < 8; k++)
        {
            (Vector3 at, _) = sim.ArcadeStart(who);
            Assert.All(theirs, them => Assert.True(sim.Collision.SweepSphere(them.EyePosition, at + new Vector3(0f, eye, 0f), 0.02f, out _),
                $"{at} is in sight of the one at {them.Position}"));
            Assert.All(sim.Players.Where(p => p != who), p => Assert.True(Vector3.Distance(p.Position, at) >= Rules.StartClearance));
        }
    }

    [Fact]
    public void A_live_arcade_round_steps_without_allocating_respawns_and_all()
    {
        (SimWorld sim, PlayerState hero, PlayerState[] opponents) = Ground(MatchModeKind.Teams, 10f, 12f);
        Run(sim, Second, (t, p) => p == hero ? Shoot(hero, opponents[0], t, 2) : default);
        var commands = new InputCommand[sim.Players.Count];
        // The step that brings them back is among those measured.
        long bytes = Allocations.During(() =>
        {
            for (int t = 0; t < (int)(Rules.Respawn * Second) + Second; t++)
            {
                sim.Step(commands);
                sim.Events.Clear();
            }
        });
        Assert.True(opponents[0].Alive);
        Assert.Equal(0, bytes);
    }
}
