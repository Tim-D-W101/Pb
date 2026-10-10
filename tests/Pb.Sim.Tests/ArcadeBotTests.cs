using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Match;
using Pb.Sim.Players;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>Phase 5 (M5.8): bots in arcade: back in the fight after a hit, and whole rounds to the clock.</summary>
[Collection(BotArenaCollection.Name)]
public class ArcadeBotTests
{
    private const int Second = 120;
    private readonly ITestOutputHelper _out;

    public ArcadeBotTests(ITestOutputHelper output)
    {
        _out = output;
    }

    [Fact]
    public void A_bot_put_out_comes_back_fresh_and_plays_on()
    {
        BotArena arena = BotArena.Arcade(size: 3).StartArcade();
        arena.Run(2 * Second);
        BotBrain bot = arena.Bots.First(b => b.Self.Team == 1);
        // Out, as a ball would put it: it walks off, then comes back after the respawn time.
        bot.Self.Alive = false;
        bot.Self.RespawnTick = arena.Sim.Tick + (int)(arena.Sim.Config.Rules.Arcade.Respawn * Second);
        arena.Run(Second);
        Assert.Equal(BotMode.Out, bot.Mode);
        arena.Run((int)(arena.Sim.Config.Rules.Arcade.Respawn * Second));
        Assert.True(bot.Self.Alive);
        Vector3 back = Assert.Single(arena.Log, e => e.Type == SimEventType.PlayerRespawned && e.PlayerId == bot.Self.Id).Position;

        // A fresh start: not out, its home where it came back, and on the move again.
        arena.Run(4 * Second);
        _out.WriteLine($"{bot.Self.Name} back at {back}: {bot.Label} at {bot.Self.Position} after 4 s");
        Assert.NotEqual(BotMode.Out, bot.Mode);
        Assert.Equal(back, bot.Home);
        Assert.True(Vector3.Distance(bot.Self.Position, back) > 1f || bot.Mode != BotMode.Idle, $"{bot.Self.Name} stood where it came back");
    }

    [Theory]
    [InlineData("oxbarrow_works", null, "arcade", 3, 1UL)]
    [InlineData("rail_yard", null, "arcade_ffa", 6, 2UL)]
    [InlineData("sports_ground", "whole", "arcade", 4, 3UL)]
    public void Bots_play_a_whole_arcade_round_to_the_clock(string level, string? place, string modeId, int size, ulong seed)
    {
        BotArena arena = BotArena.Arcade(level, modeId, size, seed: seed, place: place);
        MatchModeKind kind = TestData.Config.Rules.FindMode(modeId)!.Kind;
        arena.StartArcade(kind, timeLimit: 90f);
        arena.Run(95 * Second, () => arena.Sim.Match!.Phase == MatchPhase.Ended);
        MatchState match = arena.Sim.Match!;
        int outs = arena.Log.Count(e => e.Type == SimEventType.PlayerEliminated);
        int backs = arena.Log.Count(e => e.Type == SimEventType.PlayerRespawned);
        string scores = string.Join(", ", match.Stats.Select(s => $"{arena.Sim.FindPlayer(s.PlayerId)!.Name} {s.Eliminations}/{s.TimesOut}"));
        _out.WriteLine($"{level}/{place} {modeId} seed {seed}: {match.Result.Reason} won by {match.Result.Winner} after {match.Elapsed:0} s; " +
                       $"{outs} out, {backs} back; {scores}");
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.True(match.Elapsed >= 89.9f, $"it ended after {match.Elapsed:0.0} s, before the clock");
        Assert.True(match.Result.Reason is RoundEnd.MostEliminations or RoundEnd.TimeUp);
        Assert.True(outs >= 3, $"only {outs} out in 90 s");
        Assert.True(backs >= outs - arena.Sim.Players.Count, $"{outs} out but {backs} back");
    }
}
