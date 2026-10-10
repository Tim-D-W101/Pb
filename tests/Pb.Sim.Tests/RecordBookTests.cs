using Pb.Sim.Data;
using Pb.Sim.Match;
using Xunit;

namespace Pb.Sim.Tests;

/// <summary>
/// Your records: what they keep for each area, place, mode, objective and difficulty, the saved file, and that
/// nothing is locked (every area and place is open whatever you've won).
/// </summary>
public class RecordBookTests
{
    private static AreaListDef Areas(int accuracyMinShots = 10)
    {
        static TierDef Tier(string id) => new() { Id = id, DisplayName = id, Bots = id, TimeLimit_s = 600, StartPods = 2, BotPods = 2, Pickups = true };
        TierDef[] tiers = { Tier("easy"), Tier("normal"), Tier("hard") };
        return new AreaListDef
        {
            Areas = new[]
            {
                new AreaEntryDef { Id = "one", DisplayName = "One", File = "one.jsonc", Tiers = tiers, Roster = new[] { "a" } },
                new AreaEntryDef { Id = "two", DisplayName = "Two", File = "two.jsonc", Tiers = tiers, Roster = new[] { "a" } },
            },
            Records = new RecordsDef { AccuracyMinShots = accuracyMinShots },
        };
    }

    private static RoundResult Round(string level, RoundOutcome outcome, string tier = "normal", string mode = "solo", float elapsed = 300f,
        int shots = 40, int hits = 8, int eliminations = 3, string place = RecordBook.WholeArea) =>
        new(level, mode, tier, outcome, elapsed, shots, hits, eliminations, RecordBook.Eliminate, place);

    [Theory]
    [InlineData(RoundOutcome.Cleared, 1)]
    [InlineData(RoundOutcome.Eliminated, 0)]
    [InlineData(RoundOutcome.Traded, 0)]
    [InlineData(RoundOutcome.TimeUp, 0)]
    public void A_round_counts_and_only_a_win_counts_as_one(RoundOutcome outcome, int wins)
    {
        var records = new RecordBook(Areas());
        records.Add(Round("one", outcome));
        LevelRecord r = records.Record("one", RecordBook.WholeArea, "solo", "normal")!;
        Assert.Equal((1, wins), (r.Rounds, r.Wins));
    }

    [Fact]
    public void Records_keep_the_best()
    {
        var records = new RecordBook(Areas(accuracyMinShots: 10));
        records.Add(Round("one", RoundOutcome.Cleared, elapsed: 400f, shots: 50, hits: 10, eliminations: 4));
        records.Add(Round("one", RoundOutcome.Cleared, elapsed: 320f, shots: 20, hits: 2, eliminations: 6));
        records.Add(Round("one", RoundOutcome.Eliminated, elapsed: 100f, shots: 30, hits: 9, eliminations: 1));
        // Too few shots for the accuracy to count.
        records.Add(Round("one", RoundOutcome.Eliminated, elapsed: 50f, shots: 3, hits: 3, eliminations: 0));

        LevelRecord r = records.Record("one", RecordBook.WholeArea, "solo", "normal")!;
        Assert.Equal(4, r.Rounds);
        Assert.Equal(2, r.Wins);
        Assert.Equal(320f, r.BestClear_s);
        Assert.Equal(0.3f, r.BestAccuracy, 3);
        Assert.Equal(6, r.MostEliminations);
        Assert.True(records.WonOn("one", RecordBook.WholeArea, "normal"));
        Assert.False(records.WonOn("one", RecordBook.WholeArea, "hard"));
        Assert.Null(records.Record("one", RecordBook.WholeArea, "ffa", "normal"));
    }

    [Fact]
    public void Each_place_keeps_its_own_records()
    {
        var records = new RecordBook(Areas());
        records.Add(Round("one", RoundOutcome.Cleared, elapsed: 200f, place: "warehouse"));
        records.Add(Round("one", RoundOutcome.Eliminated));

        LevelRecord part = records.Record("one", "warehouse", "solo", "normal")!;
        Assert.Equal((1, 1, 200f), (part.Rounds, part.Wins, part.BestClear_s));
        LevelRecord whole = records.Record("one", RecordBook.WholeArea, "solo", "normal")!;
        Assert.Equal((1, 0), (whole.Rounds, whole.Wins));
        Assert.True(records.WonOn("one", "warehouse", "normal"));
        Assert.False(records.WonOn("one", RecordBook.WholeArea, "normal"));
    }

    [Fact]
    public void Each_objective_keeps_its_own_records()
    {
        var records = new RecordBook(Areas());
        records.Add(Round("one", RoundOutcome.Eliminated, elapsed: 400f));
        records.Add(new RoundResult("one", "solo", "normal", RoundOutcome.Extracted, 210f, 12, 3, 1, "retrieve"));

        LevelRecord retrieve = records.Record("one", RecordBook.WholeArea, "solo", "normal", "retrieve")!;
        Assert.Equal((1, 1, 210f), (retrieve.Rounds, retrieve.Wins, retrieve.BestClear_s));
        LevelRecord eliminate = records.Record("one", RecordBook.WholeArea, "solo", "normal")!;
        Assert.Equal((1, 0), (eliminate.Rounds, eliminate.Wins));
        Assert.Null(records.Record("one", RecordBook.WholeArea, "solo", "normal", "hold"));
        Assert.Equal("hold", RecordBook.IdOf(ObjectiveKind.Hold));
    }

    [Fact]
    public void A_save_from_the_ladder_loads_its_records_as_the_whole_areas()
    {
        // As the ladder saved it: levels opened, records without places (or, older still, objectives).
        const string ladder = """
            {
              "version": 1,
              "opened": ["two"],
              "records": [
                { "level": "one", "mode": "solo", "tier": "normal", "rounds": 3, "wins": 2, "bestClear_s": 250 },
                { "level": "one", "mode": "teams", "objective": "hold", "tier": "hard", "rounds": 1, "wins": 1, "bestClear_s": 400 }
              ],
              "last": { "level": "two", "mode": "teams", "size": 4, "tier": "hard", "objective": "hold" }
            }
            """;
        var records = new RecordBook(Areas(), ProfileData.FromJson(ladder));
        Assert.Equal(2, records.Record("one", RecordBook.WholeArea, "solo", "normal")!.Wins);
        Assert.Equal(400f, records.Record("one", RecordBook.WholeArea, "teams", "hard", "hold")!.BestClear_s);
        Assert.Equal("two", records.Data.Last!.Level);
        Assert.Equal("", records.Data.Last.Place);
        Assert.Equal(3, records.Data.Version);
        Assert.DoesNotContain("opened", records.Data.ToJson());
        Assert.Null(records.Data.Loadout);
    }

    [Fact]
    public void The_file_round_trips_and_a_broken_one_starts_afresh()
    {
        var records = new RecordBook(Areas());
        records.Add(Round("one", RoundOutcome.Cleared, elapsed: 250f, place: "yard"));
        records.Remember("two", "engine_shed", "teams", 4, "hard", "hold");
        string json = records.Data.ToJson();

        ProfileData? loaded = ProfileData.FromJson(json);
        Assert.NotNull(loaded);
        var again = new RecordBook(Areas(), loaded);
        Assert.Equal(250f, again.Record("one", "yard", "solo", "normal")!.BestClear_s);
        Assert.Equal("engine_shed", again.Data.Last!.Place);
        Assert.Equal("teams", again.Data.Last.Mode);
        Assert.Equal(4, again.Data.Last.Size);
        Assert.Equal("hold", again.Data.Last.Objective);

        Assert.Null(ProfileData.FromJson("{ not json"));
        Assert.Null(ProfileData.FromJson("null"));
        ProfileData? sparse = ProfileData.FromJson("{ \"version\": 1, \"records\": [ { \"level\": \"\" } ] }");
        Assert.NotNull(sparse);
        Assert.Empty(sparse!.Records);
    }

    [Fact]
    public void Every_shipped_area_is_open_with_its_whole_and_its_parts()
    {
        // Nothing in the data can lock an area or a place: the file has no unlock rules left to give.
        AreaListDef areas = TestData.Data.Areas;
        Assert.True(areas.Areas.Length >= 4, $"{areas.Areas.Length} areas");
        foreach (AreaEntryDef area in areas.Areas)
        {
            Assert.True(TestData.Data.Levels.ContainsKey(area.Id), $"{area.Id} has no level");
            Assert.Contains(TestData.Data.Levels[area.Id].Places, p => p.Whole && p.Id == RecordBook.WholeArea);
        }

        var unlock = new EditedDataSource(TestData.Source)
            .Edit("levels/areas.jsonc", text => text.Replace("\"records\":", "\"unlock\": { \"minTier\": \"easy\" },\n  \"records\":"));
        DataException ex = Assert.Throws<DataException>(() => GameData.Load(unlock));
        Assert.Contains("unlock", ex.Message);
    }
}
