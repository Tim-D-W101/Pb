using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Events;
using Pb.Sim.Match;
using Pb.Sim.Players;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>Phase 5 (M5.7): bots playing capture the flag: their parts, taking and carrying a flag home, chasing a carrier, whole points.</summary>
[Collection(BotArenaCollection.Name)]
public class FlagBotTests
{
    private const int Second = 120;
    private readonly ITestOutputHelper _out;

    public FlagBotTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static void ToTheHorn(BotArena arena)
    {
        arena.Run((int)MathF.Ceiling(arena.Sim.Config.Rules.Flag.Countdown * Second) + 3);
        Assert.True(arena.Sim.IsLive, "the point never went live");
    }

    private static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

    [Fact]
    public void With_a_flag_each_some_go_for_theirs_and_the_rest_take_posts_round_their_own()
    {
        BotArena arena = BotArena.Flag(perSide: 4).StartFlag();
        ToTheHorn(arena);
        FlagSet flags = arena.Sim.Match!.Flags!;
        int defenders = Math.Max(1, (int)MathF.Round(4 * TestData.Data.Bots.Brain.Flag.DefendShare));
        for (int side = 0; side < 2; side++)
        {
            List<BotBrain> mine = arena.Bots.Where(b => b.Self.Team == side).ToList();
            Vector3 home = flags.Home(flags.FlagOfSide(side));
            foreach (BotBrain bot in mine)
            {
                _out.WriteLine($"side {side} {bot.Self.Name}: {(bot.AttacksFlag ? "attacks" : "defends")}, post " +
                               $"{(bot.DutyPost is { } p ? $"{Flat(p - home):0.0} m from its flag" : "none")}");
            }

            Assert.Equal(4 - defenders, mine.Count(b => b.AttacksFlag));
            Assert.All(mine.Where(b => !b.AttacksFlag), b => Assert.True(b.DutyPost is { } post && Flat(post - home) < TestData.Data.Bots.Brain.GuardSpacing * 3f,
                $"{b.Self.Name} has no post at its flag"));
        }
    }

    [Fact]
    public void Unopposed_attackers_take_the_other_flag_and_bring_it_home()
    {
        BotArena arena = BotArena.Flag(perSide: 3).StartFlag();
        ToTheHorn(arena);
        // Theirs: still in, but off the field (so the point goes on, and nobody shoots or is shot).
        foreach (BotBrain bot in arena.Bots.Where(b => b.Self.Team == 1))
        {
            bot.Passive = true;
            bot.Self.Present = false;
            bot.Self.Position = new Vector3(0f, -50f, 0f);
        }

        int every = 20 * Second;
        arena.Run(300 * Second, () =>
        {
            if (arena.Sim.Tick % every == 0)
            {
                FlagSet f = arena.Sim.Match!.Flags!;
                _out.WriteLine($"{arena.Sim.Match.Elapsed:0} s: flag 1 {f.Status(1)} at {f.Position(1)}; " +
                               string.Join(", ", arena.Bots.Where(b => b.Self.Team == 0).Select(b => $"{b.Self.Name} {b.Mode} at {b.Self.Position} ({b.Label})")));
            }

            return arena.Sim.Match!.Phase == MatchPhase.Ended;
        });
        MatchState match = arena.Sim.Match!;
        _out.WriteLine($"{match.Result.Reason} won by {match.Result.Winner} after {match.Elapsed:0} s, carried home by {arena.Sim.FindPlayer(match.Flags!.ScoredBy)?.Name}");
        Assert.Equal(new MatchResult(RoundEnd.Captured, 0), match.Result);
        Assert.Equal(1, match.Flags!.CapturedFlag);
    }

    [Fact]
    public void An_escort_met_on_the_carriers_way_home_turns_and_leads_it_there()
    {
        BotArena arena = BotArena.Flag(perSide: 3).StartFlag();
        ToTheHorn(arena);
        foreach (BotBrain bot in arena.Bots.Where(b => b.Self.Team == 1))
        {
            bot.Passive = true;
            bot.Self.Present = false;
            bot.Self.Position = new Vector3(0f, -50f, 0f);
        }

        FlagSet flags = arena.Sim.Match!.Flags!;
        List<BotBrain> attackers = arena.Bots.Where(b => b.Self.Team == 0 && b.AttacksFlag).ToList();
        Assert.True(attackers.Count >= 2, $"only {attackers.Count} of side 0 go for their flag");
        BotBrain carrier = attackers[0], escort = attackers[1];
        carrier.Self.Position = flags.Home(flags.FlagOfSide(1));
        arena.Run(2);
        Assert.Equal(carrier.Self.Id, flags.Carrier(flags.FlagOfSide(1)));

        // The escort on the carrier's way home, as if met coming the other way (in the game it would stop in a doorway in
        // front of the carrier, and their bodies would hold each other up for good).
        Vector3 home = flags.ScoreAt(0);
        Assert.True(arena.Squad.Grid.TrySnap(Vector3.Lerp(carrier.Self.Position, home, 0.35f), out Vector3 between));
        escort.Self.Position = between;
        float before = Flat(between - home);
        Assert.True(before < Flat(carrier.Self.Position - home), "the escort isn't nearer home than the carrier");
        arena.Run(3 * Second);
        float after = Flat(escort.Self.Position - home);
        _out.WriteLine($"the escort from {before:0.0} m to {after:0.0} m from home; the carrier {Flat(carrier.Self.Position - home):0.0} m from it");
        Assert.True(after < before - 4f, $"the escort went from {before:0.0} to {after:0.0} m from home, not leading the way");
    }

    [Fact]
    public void On_the_field_the_nearest_bot_goes_for_the_flag_and_carries_it_to_their_buzzer()
    {
        BotArena arena = BotArena.Flag("sports_ground", perSide: 3, place: "whole").StartFlag();
        ToTheHorn(arena);
        List<BotBrain> north = arena.Bots.Where(b => b.Self.Team == 1).ToList();
        // Theirs: two out, one still in but quiet in its box (so the south has more in, and goes).
        north[0].Self.Alive = false;
        north[1].Self.Alive = false;
        north[2].Passive = true;
        north[2].Self.Present = false;
        north[2].Self.Position = new Vector3(-30f, 0f, 0f);
        arena.Run(Second, () => arena.Bots.Any(b => b.GoingForFlag));
        BotBrain taker = Assert.Single(arena.Bots, b => b.GoingForFlag);
        Assert.Equal(0, taker.Self.Team);
        FlagSet flags = arena.Sim.Match!.Flags!;
        arena.Run(120 * Second, () => arena.Sim.Match!.Phase == MatchPhase.Ended);
        _out.WriteLine($"{arena.Sim.Match!.Result.Reason} after {arena.Sim.Match.Elapsed:0.0} s, by {arena.Sim.FindPlayer(flags.ScoredBy)?.Name}");
        Assert.Equal(new MatchResult(RoundEnd.Captured, 0), arena.Sim.Match.Result);
        Assert.Equal(taker.Self.Id, flags.ScoredBy);
    }

    [Fact]
    public void Once_their_flag_is_taken_its_side_goes_after_the_carrier()
    {
        BotArena arena = BotArena.Flag(perSide: 4, heroBot: false).StartFlag();
        ToTheHorn(arena);
        FlagSet flags = arena.Sim.Match!.Flags!;
        // You take theirs from under their noses, and are straight back by your own base (not on it), out of their sight.
        Vector3 home = flags.Home(flags.FlagOfSide(0));
        arena.Hero.Position = flags.Home(flags.FlagOfSide(1));
        arena.Run(1);
        Assert.Equal(arena.Hero.Id, flags.Carrier(flags.FlagOfSide(1)));
        Assert.True(arena.Squad.Grid.TrySnap(home + new Vector3(6f, 0f, 0f), out Vector3 by) || arena.Squad.Grid.TrySnap(home - new Vector3(6f, 0f, 0f), out by));
        arena.Hero.Position = by;
        arena.Run((int)(TestData.Data.Bots.Brain.Flag.AlarmInterval * Second) + 2);
        BotBrain[] theirs = arena.Bots.Where(b => b.Self.Team == 1 && b.Self.Alive).OrderBy(b => Flat(b.Self.Position - arena.Hero.Position)).ToArray();
        foreach (BotBrain bot in theirs)
        {
            _out.WriteLine($"{bot.Self.Name}: {bot.Mode}, lead={bot.Senses.For(arena.Hero.Id)?.HasLead}");
        }

        Assert.True(theirs.Take(TestData.Data.Bots.Brain.Flag.Chasers).All(b => b.Senses.For(arena.Hero.Id) is { HasLead: true }),
            "the chasers weren't told where the carrier is");
    }

    [Theory]
    [InlineData("sports_ground", "whole", 1UL)]
    [InlineData("sports_ground", "crossfire", 2UL)]
    [InlineData("oxbarrow_works", null, 3UL)]
    [InlineData("oxbarrow_works", null, 4UL)]
    public void Bots_play_a_whole_flag_point_to_its_end(string level, string? place, ulong seed)
    {
        BotArena arena = BotArena.Flag(level, perSide: level == "sports_ground" ? 5 : 4, seed: seed, place: place).StartFlag();
        FlagRules rules = arena.Sim.Config.Rules.Flag;
        float clock = rules.ClockFor(arena.Level.Field is not null);
        arena.Run((int)((rules.Countdown + clock) * Second) + 4 * Second, () => arena.Sim.Match!.Phase == MatchPhase.Ended);
        MatchState match = arena.Sim.Match!;
        int shots = arena.Log.Count(e => e.Type == SimEventType.ShotFired);
        int taken = arena.Log.Count(e => e.Type == SimEventType.FlagTaken);
        _out.WriteLine($"{level}/{place} seed {seed}: {match.Result.Reason} won by {match.Result.Winner} after {match.Elapsed:0.0} s, {shots} shots, " +
                       $"flag taken {taken} times; {arena.Bots.Count(b => b.Self.Team == 0 && b.Self.Alive)} v {arena.Bots.Count(b => b.Self.Team == 1 && b.Self.Alive)} in");
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.True(shots > 10, $"only {shots} shots in a point");
    }
}
