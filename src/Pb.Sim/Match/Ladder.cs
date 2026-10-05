using System.Text.Json;
using System.Text.Json.Serialization;
using Pb.Sim.Data;

namespace Pb.Sim.Match;

/// <summary>One finished round, for the records: where and how it was played, and how it went for you.</summary>
public readonly record struct RoundResult(
    string LevelId, string ModeId, string TierId, RoundOutcome Outcome, float Elapsed, int Shots, int Hits, int Eliminations);

/// <summary>Your record on one level, mode and difficulty tier.</summary>
public sealed class LevelRecord
{
    public string Level { get; set; } = "";

    public string Mode { get; set; } = "";

    public string Tier { get; set; } = "";

    public int Rounds { get; set; }

    public int Wins { get; set; }

    /// <summary>The fastest win (s), or 0 before the first.</summary>
    public float BestClear_s { get; set; }

    /// <summary>The best accuracy (0..1) over rounds with at least the ladder's minimum of shots.</summary>
    public float BestAccuracy { get; set; }

    public int MostEliminations { get; set; }
}

/// <summary>The menu's last choices, to come back to.</summary>
public sealed class LastChoices
{
    public string Level { get; set; } = "";

    public string Mode { get; set; } = "";

    public int Size { get; set; }

    public string Tier { get; set; } = "";

    public string Objective { get; set; } = "";
}

/// <summary>Saved progress as it's stored: the levels opened, your records and your last choices.</summary>
public sealed class ProfileData
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Bumped when the format changes in a way older files need converting for.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Levels opened by a win (the first level is always open and isn't listed).</summary>
    public List<string> Opened { get; set; } = new();

    public List<LevelRecord> Records { get; set; } = new();

    public LastChoices? Last { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>The progress in <paramref name="json"/>, or null if it can't be read (the caller starts afresh).</summary>
    public static ProfileData? FromJson(string json)
    {
        try
        {
            ProfileData? data = JsonSerializer.Deserialize<ProfileData>(json, Json);
            if (data is null)
            {
                return null;
            }

            data.Opened ??= new List<string>();
            data.Records ??= new List<LevelRecord>();
            data.Records.RemoveAll(r => r is null || string.IsNullOrEmpty(r.Level));
            return data;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// The ladder's progress (levels/ladder.jsonc "unlock" and "records"): the first level is always open, and each
/// later one opens when you win a round on the level before it, in any mode, on the rule's lowest tier or a
/// harder one. Records keep your best on each level, mode and tier. Engine-free, so the rules are tested here;
/// the game loads and saves the <see cref="ProfileData"/>.
/// </summary>
public sealed class LadderProgress
{
    private readonly LadderDef _ladder;

    public LadderProgress(LadderDef ladder, ProfileData? data = null)
    {
        _ladder = ladder;
        Data = data ?? new ProfileData();
    }

    public ProfileData Data { get; }

    /// <summary>Every level counts as open (settings switch, <c>--unlock-all</c>); never written to <see cref="Data"/>.</summary>
    public bool OpenAll { get; set; }

    /// <summary>Whether <paramref name="levelId"/> may be played (it still needs a level file to be playable).</summary>
    public bool IsOpen(string levelId)
    {
        int index = IndexOf(levelId);
        if (index < 0)
        {
            return false;
        }

        return OpenAll || index == 0 || Data.Opened.Contains(levelId) || OpensNext(_ladder.Levels[index - 1]);
    }

    /// <summary>The level a win on which opens <paramref name="levelId"/>, or null for the first (or an unknown id).</summary>
    public LadderLevelDef? OpenedBy(string levelId)
    {
        int index = IndexOf(levelId);
        return index > 0 ? _ladder.Levels[index - 1] : null;
    }

    /// <summary>The lowest tier whose wins count towards opening the next level (null: any).</summary>
    public string? MinTier => _ladder.Unlock.MinTier.Length > 0 ? _ladder.Unlock.MinTier : null;

    public LevelRecord? Record(string level, string mode, string tier) =>
        Data.Records.Find(r => r.Level == level && r.Mode == mode && r.Tier == tier);

    /// <summary>Whether you've won on <paramref name="level"/> at <paramref name="tier"/>, in any mode.</summary>
    public bool WonOn(string level, string tier) => Data.Records.Exists(r => r.Level == level && r.Tier == tier && r.Wins > 0);

    /// <summary>
    /// The furthest level up the ladder that's open and built (<paramref name="playable"/> says which are),
    /// or the first playable one.
    /// </summary>
    public string? Newest(Func<string, bool> playable)
    {
        string? newest = null;
        foreach (LadderLevelDef level in _ladder.Levels)
        {
            if (playable(level.Id) && (newest is null || IsOpen(level.Id)))
            {
                newest = level.Id;
            }
        }

        return newest;
    }

    /// <summary>
    /// Adds a finished round to the records. Returns the level the round opened, if it opened one (never when
    /// every level is merely shown open by <see cref="OpenAll"/>: the win still opens it for real).
    /// </summary>
    public string? Add(RoundResult result)
    {
        LevelRecord? record = Record(result.LevelId, result.ModeId, result.TierId);
        if (record is null)
        {
            record = new LevelRecord { Level = result.LevelId, Mode = result.ModeId, Tier = result.TierId };
            Data.Records.Add(record);
        }

        bool won = result.Outcome.IsWin();
        record.Rounds++;
        record.MostEliminations = Math.Max(record.MostEliminations, result.Eliminations);
        if (result.Shots >= _ladder.Records.AccuracyMinShots)
        {
            record.BestAccuracy = MathF.Max(record.BestAccuracy, (float)result.Hits / result.Shots);
        }

        if (!won)
        {
            return null;
        }

        record.Wins++;
        record.BestClear_s = record.BestClear_s <= 0f ? result.Elapsed : MathF.Min(record.BestClear_s, result.Elapsed);

        int index = IndexOf(result.LevelId);
        if (index < 0 || index + 1 >= _ladder.Levels.Length || !Counts(_ladder.Levels[index], result.TierId))
        {
            return null;
        }

        string next = _ladder.Levels[index + 1].Id;
        if (Data.Opened.Contains(next))
        {
            return null;
        }

        Data.Opened.Add(next);
        return next;
    }

    /// <summary>Remembers the menu's choices.</summary>
    public void Remember(string level, string mode, int size, string tier, string objective) =>
        Data.Last = new LastChoices { Level = level, Mode = mode, Size = size, Tier = tier, Objective = objective };

    private int IndexOf(string levelId) => Array.FindIndex(_ladder.Levels, l => l.Id == levelId);

    /// <summary>A win on <paramref name="level"/> at <paramref name="tier"/> opens the next level.</summary>
    private bool Counts(LadderLevelDef level, string tier)
    {
        if (MinTier is not { } min || level.Tiers is not { Length: > 0 } tiers)
        {
            return true;
        }

        int at = Array.FindIndex(tiers, t => t.Id == tier);
        int lowest = Array.FindIndex(tiers, t => t.Id == min);
        return at >= 0 && at >= Math.Max(0, lowest);
    }

    private bool OpensNext(LadderLevelDef level) =>
        Data.Records.Exists(r => r.Level == level.Id && r.Wins > 0 && Counts(level, r.Tier));
}
