using Pb.Sim.Data;
using Pb.Sim.Match;
using Xunit;

namespace Pb.Sim.Tests;

/// <summary>The ladder's progress rules (M3.1): what opens what, what the records keep, and the saved file.</summary>
public class LadderTests
{
    private static LadderDef Ladder(string minTier = "easy", int accuracyMinShots = 10)
    {
        static LadderTierDef Tier(string id) => new() { Id = id, DisplayName = id, Bots = id, TimeLimit_s = 600, StartPods = 2, BotPods = 2, Pickups = true };
        LadderTierDef[] tiers = { Tier("easy"), Tier("normal"), Tier("hard") };
        return new LadderDef
        {
            Levels = new[]
            {
                new LadderLevelDef { Id = "one", DisplayName = "One", File = "one.jsonc", Tiers = tiers, Roster = new[] { "a" } },
                new LadderLevelDef { Id = "two", DisplayName = "Two", File = "two.jsonc", Tiers = tiers, Roster = new[] { "a" } },
                new LadderLevelDef { Id = "three", DisplayName = "Three", File = "three.jsonc", Tiers = tiers, Roster = new[] { "a" } },
                new LadderLevelDef { Id = "four", DisplayName = "Four", Note = "Not built yet" },
            },
            Unlock = new UnlockDef { MinTier = minTier },
            Records = new RecordsDef { AccuracyMinShots = accuracyMinShots },
        };
    }

    private static RoundResult Round(string level, RoundOutcome outcome, string tier = "normal", string mode = "solo", float elapsed = 300f,
        int shots = 40, int hits = 8, int eliminations = 3) =>
        new(level, mode, tier, outcome, elapsed, shots, hits, eliminations);

    [Fact]
    public void Only_the_first_level_is_open_at_the_start()
    {
        var progress = new LadderProgress(Ladder());
        Assert.True(progress.IsOpen("one"));
        Assert.False(progress.IsOpen("two"));
        Assert.False(progress.IsOpen("three"));
        Assert.False(progress.IsOpen("nowhere"));
        Assert.Null(progress.OpenedBy("one"));
        Assert.Equal("one", progress.OpenedBy("two")!.Id);
    }

    [Fact]
    public void A_win_opens_the_next_level_and_nothing_else()
    {
        var progress = new LadderProgress(Ladder());
        Assert.Equal("two", progress.Add(Round("one", RoundOutcome.Cleared)));
        Assert.True(progress.IsOpen("two"));
        Assert.False(progress.IsOpen("three"));

        // Winning again opens nothing new; winning on the second opens the third.
        Assert.Null(progress.Add(Round("one", RoundOutcome.Cleared, mode: "ffa")));
        Assert.Equal("three", progress.Add(Round("two", RoundOutcome.Cleared, mode: "teams")));
        Assert.True(progress.IsOpen("three"));

        // A level that isn't built yet can still be opened (it becomes playable once it is).
        Assert.Equal("four", progress.Add(Round("three", RoundOutcome.Cleared)));
    }

    [Theory]
    [InlineData(RoundOutcome.Eliminated)]
    [InlineData(RoundOutcome.Traded)]
    [InlineData(RoundOutcome.TimeUp)]
    public void Losing_opens_nothing(RoundOutcome outcome)
    {
        var progress = new LadderProgress(Ladder());
        Assert.Null(progress.Add(Round("one", outcome)));
        Assert.False(progress.IsOpen("two"));
        Assert.Equal(1, progress.Record("one", "solo", "normal")!.Rounds);
        Assert.Equal(0, progress.Record("one", "solo", "normal")!.Wins);
    }

    [Fact]
    public void A_stricter_rule_needs_a_win_on_its_tier_or_harder()
    {
        var progress = new LadderProgress(Ladder(minTier: "normal"));
        Assert.Null(progress.Add(Round("one", RoundOutcome.Cleared, tier: "easy")));
        Assert.False(progress.IsOpen("two"));
        Assert.Equal("two", progress.Add(Round("one", RoundOutcome.Cleared, tier: "hard")));
        Assert.True(progress.IsOpen("two"));
    }

    [Fact]
    public void Records_keep_the_best()
    {
        var progress = new LadderProgress(Ladder(accuracyMinShots: 10));
        progress.Add(Round("one", RoundOutcome.Cleared, elapsed: 400f, shots: 50, hits: 10, eliminations: 4));
        progress.Add(Round("one", RoundOutcome.Cleared, elapsed: 320f, shots: 20, hits: 2, eliminations: 6));
        progress.Add(Round("one", RoundOutcome.Eliminated, elapsed: 100f, shots: 30, hits: 9, eliminations: 1));
        // Too few shots for the accuracy to count.
        progress.Add(Round("one", RoundOutcome.Eliminated, elapsed: 50f, shots: 3, hits: 3, eliminations: 0));

        LevelRecord r = progress.Record("one", "solo", "normal")!;
        Assert.Equal(4, r.Rounds);
        Assert.Equal(2, r.Wins);
        Assert.Equal(320f, r.BestClear_s);
        Assert.Equal(0.3f, r.BestAccuracy, 3);
        Assert.Equal(6, r.MostEliminations);
        Assert.True(progress.WonOn("one", "normal"));
        Assert.False(progress.WonOn("one", "hard"));
        Assert.Null(progress.Record("one", "ffa", "normal"));
    }

    [Fact]
    public void Each_objective_keeps_its_own_records_and_any_win_opens_the_next_level()
    {
        var progress = new LadderProgress(Ladder());
        progress.Add(Round("one", RoundOutcome.Eliminated, elapsed: 400f));
        string? opened = progress.Add(new RoundResult("one", "solo", "normal", RoundOutcome.Extracted, 210f, 12, 3, 1, "retrieve"));
        Assert.Equal("two", opened);

        LevelRecord retrieve = progress.Record("one", "solo", "normal", "retrieve")!;
        Assert.Equal((1, 1, 210f), (retrieve.Rounds, retrieve.Wins, retrieve.BestClear_s));
        LevelRecord eliminate = progress.Record("one", "solo", "normal")!;
        Assert.Equal((1, 0), (eliminate.Rounds, eliminate.Wins));
        Assert.Null(progress.Record("one", "solo", "normal", "hold"));

        // A profile saved before objectives existed reads its records as eliminate.
        string old = progress.Data.ToJson().Replace("\"objective\": \"eliminate\",", "");
        Assert.DoesNotContain("\"objective\": \"eliminate\"", old);
        var reloaded = new LadderProgress(Ladder(), ProfileData.FromJson(old));
        Assert.Equal(1, reloaded.Record("one", "solo", "normal")!.Rounds);
        Assert.Equal(1, reloaded.Record("one", "solo", "normal", "retrieve")!.Wins);
        Assert.Equal("hold", LadderProgress.IdOf(ObjectiveKind.Hold));
    }

    [Fact]
    public void Open_all_shows_every_level_without_counting_as_a_win()
    {
        var progress = new LadderProgress(Ladder()) { OpenAll = true };
        Assert.True(progress.IsOpen("three"));
        Assert.Empty(progress.Data.Opened);
        progress.OpenAll = false;
        Assert.False(progress.IsOpen("three"));
    }

    [Fact]
    public void The_newest_level_is_the_furthest_open_and_built_one()
    {
        var progress = new LadderProgress(Ladder());
        bool Built(string id) => id != "four";
        Assert.Equal("one", progress.Newest(Built));
        progress.Add(Round("one", RoundOutcome.Cleared));
        Assert.Equal("two", progress.Newest(Built));
        progress.Add(Round("two", RoundOutcome.Cleared));
        progress.Add(Round("three", RoundOutcome.Cleared));
        Assert.Equal("three", progress.Newest(Built));
    }

    [Fact]
    public void The_file_round_trips_and_a_broken_one_starts_afresh()
    {
        var progress = new LadderProgress(Ladder());
        progress.Add(Round("one", RoundOutcome.Cleared, elapsed: 250f));
        progress.Remember("two", "teams", 4, "hard", "hold");
        string json = progress.Data.ToJson();

        ProfileData? loaded = ProfileData.FromJson(json);
        Assert.NotNull(loaded);
        var again = new LadderProgress(Ladder(), loaded);
        Assert.True(again.IsOpen("two"));
        Assert.Equal(250f, again.Record("one", "solo", "normal")!.BestClear_s);
        Assert.Equal("teams", again.Data.Last!.Mode);
        Assert.Equal(4, again.Data.Last.Size);
        Assert.Equal("hold", again.Data.Last.Objective);

        Assert.Null(ProfileData.FromJson("{ not json"));
        Assert.Null(ProfileData.FromJson("null"));
        ProfileData? sparse = ProfileData.FromJson("{ \"version\": 1, \"records\": [ { \"level\": \"\" } ] }");
        Assert.NotNull(sparse);
        Assert.Empty(sparse!.Records);
        Assert.Empty(sparse.Opened);
    }

    [Fact]
    public void The_shipped_ladder_opens_in_order_from_its_first_level()
    {
        LadderDef ladder = TestData.Data.Ladder;
        var progress = new LadderProgress(ladder);
        Assert.True(progress.IsOpen(ladder.Levels[0].Id));
        for (int i = 1; i < ladder.Levels.Length; i++)
        {
            Assert.False(progress.IsOpen(ladder.Levels[i].Id));
        }
    }

    [Fact]
    public void An_unlock_tier_a_level_lacks_fails_to_load()
    {
        var source = new EditedDataSource(TestData.Source)
            .Edit("levels/ladder.jsonc", text => text.Replace("\"minTier\": \"easy\"", "\"minTier\": \"veteran\""));
        DataException ex = Assert.Throws<DataException>(() => GameData.Load(source));
        Assert.Contains("unlock", ex.Message);
        Assert.Contains("veteran", ex.Message);
    }
}
