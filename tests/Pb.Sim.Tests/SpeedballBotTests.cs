using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>Phase 5 (M5.6): bots playing speedball: the breakout, holding a bunker, moving up, hanging the buzzer, whole points.</summary>
[Collection(BotArenaCollection.Name)]
public class SpeedballBotTests
{
    private const int Second = 120;
    private readonly ITestOutputHelper _out;

    public SpeedballBotTests(ITestOutputHelper output)
    {
        _out = output;
    }

    /// <summary>Runs <paramref name="arena"/>'s countdown out, and the first live tick, when the squad deals.</summary>
    private static void ToTheHorn(BotArena arena)
    {
        arena.Run((int)MathF.Ceiling(arena.Sim.Config.Rules.Speedball.Countdown * Second) + 3);
        Assert.True(arena.Sim.IsLive, "the point never went live");
    }

    [Theory]
    [InlineData("whole")]
    [InlineData("crossfire")]
    public void At_the_horn_every_bot_is_dealt_a_place_and_a_bunker_of_its_side_and_sprints_there(string place)
    {
        BotArena arena = BotArena.Speedball(place: place).StartSpeedball();
        FieldLayoutSpec layout = arena.Level.FieldLayout!;
        List<BotBrain> bots = arena.Bots.ToList();
        Assert.All(bots, b => Assert.Equal(SpeedballPlace.None, b.Place));
        ToTheHorn(arena);

        foreach (BotBrain bot in bots)
        {
            Assert.NotEqual(SpeedballPlace.None, bot.Place);
            Assert.InRange(bot.Bunker, 0, layout.Bunkers.Count - 1);
            FieldBunker bunker = layout.Bunkers[bot.Bunker];
            Assert.True(bunker.Side == bot.Self.Team || bunker.Side == -1, $"{bot.Self.Name} was dealt the other side's {bunker.Prop.Id}");
            Assert.Equal(bot.Self.Id, arena.Squad.Cover.ClaimedBy(bot.PostCover));
            Assert.True(arena.Squad.AtBunker(bot.Bunker, bot.DutyPost!.Value));
            // All but the back sprint for their bunkers; the back covers the lanes first.
            Assert.Equal(bot.Place != SpeedballPlace.Back, bot.BreakingOut);
        }

        for (int side = 0; side < 2; side++)
        {
            List<BotBrain> mine = bots.Where(b => b.Self.Team == side).ToList();
            Assert.Equal(1, mine.Count(b => b.Place == SpeedballPlace.Back));
            Assert.Equal(2, mine.Count(b => b.Place == SpeedballPlace.Mid));
            Assert.Equal(2, mine.Count(b => b.Place == SpeedballPlace.Front));
            Assert.Equal(mine.Count, mine.Select(b => b.Bunker).Distinct().Count());
        }

        var sprinted = new HashSet<int>();
        var arrivedAt = new Dictionary<int, int>();
        int start = arena.Sim.Tick;
        arena.Run(8 * Second, () =>
        {
            foreach (BotBrain bot in bots)
            {
                if (bot.Self.Sprinting)
                {
                    sprinted.Add(bot.Self.Id);
                }

                if (bot.Self.Alive && !arrivedAt.ContainsKey(bot.Self.Id) && bot.DutyPost is { } post &&
                    Vector3.Distance(bot.Self.Position with { Y = 0f }, post with { Y = 0f }) < 0.8f)
                {
                    arrivedAt[bot.Self.Id] = arena.Sim.Tick - start;
                }
            }

            return false;
        });

        foreach (BotBrain bot in bots)
        {
            string arrived = arrivedAt.TryGetValue(bot.Self.Id, out int t) ? $"there after {t / (float)Second:0.0} s" : bot.Self.Alive ? "not there" : "out";
            _out.WriteLine($"{bot.Self.Name} (side {bot.Self.Team}, {bot.Place}): {layout.Bunkers[bot.Bunker].Prop.Id} at {layout.Bunkers[bot.Bunker].Position}, " +
                           $"{(sprinted.Contains(bot.Self.Id) ? "sprinted" : "didn't sprint")}, {arrived}, now {bot.Label}");
        }

        int alive = bots.Count(b => b.Self.Alive);
        Assert.True(arrivedAt.Count >= Math.Min(alive, bots.Count - 2), $"only {arrivedAt.Count} of {bots.Count} bots got to their bunkers");
        Assert.True(sprinted.Count >= 6, $"only {sprinted.Count} bots sprinted at the breakout");
        Assert.All(bots.Where(b => b.Place != SpeedballPlace.Back && b.Self.Alive), b => Assert.Contains(b.Self.Id, sprinted));
    }

    [Fact]
    public void With_nobody_of_theirs_watching_their_buzzer_the_nearest_bot_goes_and_hangs_it()
    {
        BotArena arena = BotArena.Speedball(perSide: 2).StartSpeedball();
        BuzzerSet buzzers = arena.Sim.Match!.Buzzers!;
        List<BotBrain> north = arena.Bots.Where(b => b.Self.Team == 1).ToList();
        ToTheHorn(arena);
        // Theirs: one out, one still in but off the field (nobody watches the station, and the point goes on).
        north[0].Self.Alive = false;
        north[1].Self.Present = false;
        north[1].Self.Position = new Vector3(-30f, 0f, 0f);
        arena.Run(Second, () => arena.Bots.Any(b => b.Hanging));
        List<BotBrain> south = arena.Bots.Where(b => b.Self.Team == 0).ToList();
        BotBrain hanger = Assert.Single(south, b => b.Hanging);
        Vector3 station = buzzers.Post(1);
        Assert.Equal(south.Min(b => Vector3.Distance(b.Self.Position, station)), Vector3.Distance(hanger.Self.Position, station), 2);

        SpeedballRules rules = arena.Sim.Config.Rules.Speedball;
        arena.Run(40 * Second, () => arena.Sim.Match.Phase == MatchPhase.Ended);
        _out.WriteLine($"{arena.Sim.Match.Result.Reason} won by {arena.Sim.Match.Result.Winner} after {arena.Sim.Match.Elapsed:0.0} s");
        Assert.Equal(new MatchResult(RoundEnd.Hung, 0), arena.Sim.Match.Result);
        Assert.Equal(hanger.Self.Id, buzzers.HungBy);
        Assert.True(arena.Sim.Match.Elapsed > rules.HangTime);
    }

    [Fact]
    public void Nobody_goes_to_hang_a_buzzer_its_side_still_watches()
    {
        BotArena arena = BotArena.Speedball(perSide: 2).StartSpeedball();
        List<BotBrain> north = arena.Bots.Where(b => b.Self.Team == 1).ToList();
        ToTheHorn(arena);
        foreach (BotBrain bot in north)
        {
            bot.Passive = true; // in their start box, by their buzzer, seeing it
        }

        arena.Run(3 * Second);
        Assert.DoesNotContain(arena.Bots, b => b.Hanging);
    }

    [Fact]
    public void A_side_ahead_moves_up_a_bunker_at_a_time_and_on_into_the_other_half()
    {
        BotArena arena = BotArena.Speedball().StartSpeedball();
        ToTheHorn(arena);
        FieldLayoutSpec layout = arena.Level.FieldLayout!;
        List<BotBrain> south = arena.Bots.Where(b => b.Self.Team == 0).ToList();
        foreach (BotBrain bot in arena.Bots.Where(b => b.Self.Team == 1).Take(3))
        {
            bot.Self.Alive = false;
        }

        foreach (BotBrain bot in south)
        {
            bot.EndBreakout(); // nobody's moved up mid-run
        }

        // Five in against two: one move a call, the middle first, then the back, then on from the front.
        var seen = new List<string>();
        for (int call = 0; call < 8; call++)
        {
            Dictionary<int, SpeedballPlace> before = south.ToDictionary(b => b.Self.Id, b => b.Place);
            arena.Squad.Advance(layout);
            List<BotBrain> moved = south.Where(b => b.Place != before[b.Self.Id]).ToList();
            BotBrain up = Assert.Single(moved);
            seen.Add($"{before[up.Self.Id]}→{up.Place}");
            Assert.Equal(up.Self.Id, arena.Squad.Cover.ClaimedBy(up.PostCover));
            if (up.Place == SpeedballPlace.Deep)
            {
                FieldBunker deep = layout.Bunkers[up.Bunker];
                Assert.Equal(1, deep.Side);
                Assert.True(arena.Squad.Cover.Points[up.PostCover].Normal.Z > 0.3f, "a deep spot on the far side of its bunker");
            }
        }

        _out.WriteLine(string.Join(", ", seen));
        Assert.Equal(new[] { "Mid→Front", "Mid→Front", "Back→Mid", "Mid→Front" }, seen.Take(4));
        Assert.All(seen.Skip(4), m => Assert.Equal("Front→Deep", m));
        Assert.Equal(5, south.Select(b => b.Bunker).Distinct().Count());

        // Level again: nobody moves.
        foreach (BotBrain bot in south.Take(3))
        {
            bot.Self.Alive = false;
        }

        Dictionary<int, int> bunkers = south.ToDictionary(b => b.Self.Id, b => b.Bunker);
        arena.Squad.Advance(layout);
        Assert.All(south, b => Assert.Equal(bunkers[b.Self.Id], b.Bunker));
    }

    [Theory]
    [InlineData("whole", 1UL)]
    [InlineData("whole", 2UL)]
    [InlineData("crossfire", 3UL)]
    [InlineData("crossfire", 4UL)]
    public void Bots_play_a_whole_point_to_its_end(string place, ulong seed)
    {
        BotArena arena = BotArena.Speedball(place: place, seed: seed).StartSpeedball();
        SpeedballRules rules = arena.Sim.Config.Rules.Speedball;
        arena.Run((int)((rules.Countdown + rules.PointTime) * Second) + 4 * Second, () => arena.Sim.Match!.Phase == MatchPhase.Ended);
        MatchState match = arena.Sim.Match!;
        int shots = arena.Log.Count(e => e.Type == SimEventType.ShotFired);
        int outs = arena.Log.Count(e => e.Type == SimEventType.PlayerEliminated);
        _out.WriteLine($"{place} seed {seed}: {match.Result.Reason} won by {match.Result.Winner} after {match.Elapsed:0.0} s, {shots} shots, {outs} out; " +
                       $"south {arena.Bots.Count(b => b.Self.Team == 0 && b.Self.Alive)} in, north {arena.Bots.Count(b => b.Self.Team == 1 && b.Self.Alive)} in");
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.True(shots > 20, $"only {shots} shots in a point");
        Assert.True(outs > 0, "nobody went out");
    }
}
