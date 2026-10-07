using System.Text.RegularExpressions;
using Pb.Sim.Data;
using Pb.Sim.Level;

namespace Pb.Sim.Tests;

/// <summary>
/// The sound's data (M3.8): the areas' tones are ones the ambience knows, and the generated voices are on record
/// (provenance, like the art) and belong to the cast.
/// </summary>
public class AudioDataTests
{
    private static string ArtFolder => Path.GetFullPath(Path.Combine(TestData.Source.Root, "..", "art"));

    private static string Presentation => TestData.Source.ReadAllText("presentation.jsonc");

    [Fact]
    public void Every_area_tone_is_one_the_ambience_knows()
    {
        System.Text.RegularExpressions.Match tones = Regex.Match(Presentation, "\"tones\":\\s*\\{([^}]*)\\}");
        Assert.True(tones.Success, "presentation.jsonc has no ambience tones");
        var known = Regex.Matches(tones.Groups[1].Value, "\"([a-z]+)\"\\s*:").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var used = new List<string>();
        foreach (LevelLayout level in TestData.Data.Levels.Values)
        {
            foreach (AreaSpec area in level.Areas.Where(a => a.Tone.Length > 0))
            {
                Assert.True(known.Contains(area.Tone), $"{level.Id}: '{area.Name}' has the tone '{area.Tone}' ({string.Join(", ", known)} are known)");
                Assert.True(area.Indoor, $"{level.Id}: '{area.Name}' has a tone but isn't indoors");
                used.Add(area.Tone);
            }
        }

        // The levels use most of them.
        Assert.True(used.Distinct().Count() >= 4, $"only {string.Join(", ", used.Distinct())} used");
    }

    [Fact]
    public void A_tone_that_isnt_a_name_fails_to_load()
    {
        var source = new EditedDataSource(TestData.Source).Edit("kit/buildings/pump_house.jsonc", t => t.Replace("\"tone\": \"drip\"", "\"tone\": \"Drip!\""));
        var ex = Assert.Throws<DataException>(() => GameData.Load(source));
        Assert.Contains("tone", ex.Message);
    }

    [Fact]
    public void Every_voice_file_is_on_record_and_every_recorded_one_is_there()
    {
        string folder = Path.Combine(ArtFolder, "voices");
        var onDisk = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.ogg").Select(f => "res://art/voices/" + Path.GetFileName(f)).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var recorded = Voices().SelectMany(v => v.Files).ToHashSet(StringComparer.Ordinal);
        Assert.Empty(onDisk.Except(recorded));
        Assert.Empty(recorded.Except(onDisk));
    }

    [Fact]
    public void Voices_on_record_belong_to_the_cast()
    {
        string presentation = Presentation;
        var cast = Regex.Matches(presentation, "\\{\\s*\"id\":\\s*\"([a-z0-9_]+)\",\\s*\"model\"").Select(m => m.Groups[1].Value)
            .Append(Regex.Match(presentation, "\"refereeVoice\":\\s*\"([a-z0-9_]+)\"").Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(cast.Count >= 7, $"the cast is {string.Join(", ", cast)}");
        foreach ((string id, string[] files) in Voices())
        {
            Assert.Contains(id, cast);
            Assert.All(files, f => Assert.StartsWith($"res://art/voices/{id}_", f));
        }
    }

    /// <summary>The voice records in assets.jsonc: each voice's id and files.</summary>
    private static List<(string Id, string[] Files)> Voices()
    {
        string text = TestData.Source.ReadAllText("assets.jsonc");
        var voices = new List<(string, string[])>();
        using var document = System.Text.Json.JsonDocument.Parse(text,
            new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
        foreach (System.Text.Json.JsonElement asset in document.RootElement.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("kind").GetString() == "voice")
            {
                voices.Add((asset.GetProperty("id").GetString()!, asset.GetProperty("files").EnumerateArray().Select(f => f.GetString()!).ToArray()));
            }
        }

        return voices;
    }
}
