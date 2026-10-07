using System.Text.Json;
using System.Text.Json.Serialization;
using Pb.Sim.Data;

namespace Pb.Sim.Match;

/// <summary>One finished round, for the records: where and how it was played, and how it went for you.</summary>
public readonly record struct RoundResult(
    string LevelId, string ModeId, string TierId, RoundOutcome Outcome, float Elapsed, int Shots, int Hits, int Eliminations,
    string ObjectiveId = RecordBook.Eliminate, string PlaceId = RecordBook.WholeArea);

/// <summary>Your record in one place of an area, in one mode, objective and difficulty tier.</summary>
public sealed class LevelRecord
{
    public string Level { get; set; } = "";

    /// <summary>Where in the area ("whole" for all of it; records from before places read as the whole area).</summary>
    public string Place { get; set; } = RecordBook.WholeArea;

    public string Mode { get; set; } = "";

    /// <summary>"eliminate", "retrieve" or "hold" (records from before objectives read as eliminate).</summary>
    public string Objective { get; set; } = RecordBook.Eliminate;

    public string Tier { get; set; } = "";

    public int Rounds { get; set; }

    public int Wins { get; set; }

    /// <summary>The fastest win (s), or 0 before the first.</summary>
    public float BestClear_s { get; set; }

    /// <summary>The best accuracy (0..1) over rounds with at least the records' minimum of shots.</summary>
    public float BestAccuracy { get; set; }

    public int MostEliminations { get; set; }
}

/// <summary>The menu's last choices, to come back to.</summary>
public sealed class LastChoices
{
    public string Level { get; set; } = "";

    public string Place { get; set; } = "";

    public string Mode { get; set; } = "";

    public int Size { get; set; }

    public string Tier { get; set; } = "";

    public string Objective { get; set; } = "";
}

/// <summary>
/// Saved records as they're stored: your records and your last choices. (Saves from the ladder also list the levels
/// it had opened; every area is open now, so that's ignored.)
/// </summary>
public sealed class ProfileData
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Bumped when the format changes (2: records by place, no levels to open).</summary>
    public int Version { get; set; } = 2;

    public List<LevelRecord> Records { get; set; } = new();

    public LastChoices? Last { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>The records in <paramref name="json"/>, or null if it can't be read (the caller starts afresh).</summary>
    public static ProfileData? FromJson(string json)
    {
        try
        {
            ProfileData? data = JsonSerializer.Deserialize<ProfileData>(json, Json);
            if (data is null)
            {
                return null;
            }

            data.Records ??= new List<LevelRecord>();
            data.Records.RemoveAll(r => r is null || string.IsNullOrEmpty(r.Level));
            foreach (LevelRecord r in data.Records)
            {
                r.Objective = string.IsNullOrEmpty(r.Objective) ? RecordBook.Eliminate : r.Objective;
                r.Place = string.IsNullOrEmpty(r.Place) ? RecordBook.WholeArea : r.Place;
            }

            data.Version = 2;
            return data;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Your records (levels/areas.jsonc "records"): your best in each area, place, mode, objective and difficulty tier,
/// and the menu's last choices. They're only for you to beat: every area and every place in it is open from the
/// start, and winning unlocks nothing. Engine-free, so the rules are tested here; the game loads and saves the
/// <see cref="ProfileData"/>.
/// </summary>
public sealed class RecordBook
{
    /// <summary>The objective id of a plain round (the last team standing wins).</summary>
    public const string Eliminate = "eliminate";

    /// <summary>The id of the place that is the whole of an area.</summary>
    public const string WholeArea = "whole";

    private readonly AreaListDef _areas;

    public RecordBook(AreaListDef areas, ProfileData? data = null)
    {
        _areas = areas;
        Data = data ?? new ProfileData();
    }

    public ProfileData Data { get; }

    public LevelRecord? Record(string level, string place, string mode, string tier, string objective = Eliminate) =>
        Data.Records.Find(r => r.Level == level && r.Place == place && r.Mode == mode && r.Tier == tier && r.Objective == objective);

    /// <summary>The id a record keeps for <paramref name="kind"/> ("eliminate", "retrieve", "hold").</summary>
    public static string IdOf(ObjectiveKind kind) => kind switch
    {
        ObjectiveKind.Retrieve => "retrieve",
        ObjectiveKind.Hold => "hold",
        _ => Eliminate,
    };

    /// <summary>Whether you've won in <paramref name="place"/> of <paramref name="level"/> at <paramref name="tier"/>, in any mode.</summary>
    public bool WonOn(string level, string place, string tier) =>
        Data.Records.Exists(r => r.Level == level && r.Place == place && r.Tier == tier && r.Wins > 0);

    /// <summary>Adds a finished round to the records.</summary>
    public void Add(RoundResult result)
    {
        LevelRecord? record = Record(result.LevelId, result.PlaceId, result.ModeId, result.TierId, result.ObjectiveId);
        if (record is null)
        {
            record = new LevelRecord
            {
                Level = result.LevelId, Place = result.PlaceId, Mode = result.ModeId, Objective = result.ObjectiveId, Tier = result.TierId,
            };
            Data.Records.Add(record);
        }

        record.Rounds++;
        record.MostEliminations = Math.Max(record.MostEliminations, result.Eliminations);
        if (result.Shots >= _areas.Records.AccuracyMinShots)
        {
            record.BestAccuracy = MathF.Max(record.BestAccuracy, (float)result.Hits / result.Shots);
        }

        if (result.Outcome.IsWin())
        {
            record.Wins++;
            record.BestClear_s = record.BestClear_s <= 0f ? result.Elapsed : MathF.Min(record.BestClear_s, result.Elapsed);
        }
    }

    /// <summary>Remembers the menu's choices.</summary>
    public void Remember(string level, string place, string mode, int size, string tier, string objective) =>
        Data.Last = new LastChoices { Level = level, Place = place, Mode = mode, Size = size, Tier = tier, Objective = objective };
}
