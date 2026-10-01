using Pb.Sim.Data;

namespace Pb.Sim.Tests;

public class DataTests
{
    [Fact]
    public void All_shipped_data_loads_and_validates()
    {
        GameData data = GameData.Load(TestData.Source);
        Assert.NotEmpty(data.Range.Targets);
        Assert.NotEmpty(data.Range.Props);
        Assert.NotEmpty(data.Stress.Cannons);
        Assert.True(data.Config.BallPoolCapacity >= data.Stress.TargetLiveBalls,
            "pool must hold the stress-mode ball count");
    }

    /// <summary>
    /// Spec: original IP only. Every texture and model the kit uses needs a provenance record in
    /// assets.jsonc (written by tools/art/import.sh): the Higgsfield job, prompt and download URL.
    /// </summary>
    [Fact]
    public void Every_kit_texture_and_model_has_a_provenance_record()
    {
        Assert.Empty(Unrecorded(TestData.Data, TestData.Source));
    }

    [Fact]
    public void A_texture_without_a_provenance_record_is_caught()
    {
        // A material of its own, so changes to the shipped materials can't break the test.
        var source = new EditedDataSource(TestData.Source).Edit("kit/materials.jsonc", t => t.Replace("\"materials\": [",
            "\"materials\": [ { \"id\": \"unrecorded_test\", \"surface\": \"concrete\", \"color\": \"#808080\", \"pattern\": \"plain\", " +
            "\"tile_m\": 1.0, \"roughness\": 0.9, \"weathering\": 0.5, \"albedo\": \"res://art/textures/unrecorded.png\" },"));
        Assert.Equal(new[] { "res://art/textures/unrecorded.png" }, Unrecorded(GameData.Load(source), source));
    }

    [Fact]
    public void Range_targets_sit_at_the_spec_distances()
    {
        var distances = TestData.Data.Range.Targets.Where(t => t.Motion is null).Select(t => -t.BasePosition.Z).OrderBy(z => z).ToArray();
        Assert.Equal(new[] { 10f, 20f, 30f, 40f }, distances);
    }

    [Fact]
    public void Unknown_key_is_rejected_with_file_and_key()
    {
        var source = new EditedDataSource(TestData.Source)
            .Edit("markers/standard.jsonc", t => t.Replace("\"rateCap_bps\"", "\"muzzleVelocityy_mps\": 1,\n  \"rateCap_bps\""));
        var ex = Assert.Throws<DataException>(() => GameData.Load(source));
        Assert.Contains("markers/standard.jsonc", ex.Message);
        Assert.Contains("muzzleVelocityy_mps", ex.Message);
    }

    [Fact]
    public void Missing_key_is_rejected_with_its_name()
    {
        var source = new EditedDataSource(TestData.Source)
            .Edit("markers/standard.jsonc", t => t.Replace("\"rateCap_bps\": 10.5,", string.Empty));
        var ex = Assert.Throws<DataException>(() => GameData.Load(source));
        Assert.Contains("markers/standard.jsonc", ex.Message);
        Assert.Contains("rateCap_bps", ex.Message);
    }

    [Fact]
    public void Out_of_range_value_is_rejected_with_path()
    {
        var source = new EditedDataSource(TestData.Source)
            .Edit("loaders/standard.jsonc", t => t.Replace("\"capacity\": 140", "\"capacity\": 0"));
        var ex = Assert.Throws<DataException>(() => GameData.Load(source));
        Assert.Contains("loaders/standard.jsonc", ex.Message);
        Assert.Contains("pods.capacity", ex.Message);
        Assert.Contains("must be in", ex.Message);
    }

    [Fact]
    public void Unknown_surface_in_range_names_the_prop()
    {
        var source = new EditedDataSource(TestData.Source)
            .Edit("ranges/phase1.jsonc", t => t.Replace("\"surface\": \"panel\"", "\"surface\": \"pannel\""));
        var ex = Assert.Throws<DataException>(() => GameData.Load(source));
        Assert.Contains("ranges/phase1.jsonc", ex.Message);
        Assert.Contains("props[4].surface", ex.Message);
        Assert.Contains("pannel", ex.Message);
    }

    [Fact]
    public void Malformed_json_reports_the_line()
    {
        var source = new EditedDataSource(TestData.Source)
            .Edit("projectiles/paintball_68.jsonc", t => t.Replace("\"mass_g\": 3.2,", "\"mass_g\": 3.2,,"));
        var ex = Assert.Throws<DataException>(() => GameData.Load(source));
        Assert.Contains("projectiles/paintball_68.jsonc", ex.Message);
        Assert.Contains("line", ex.Message);
    }

    /// <summary>Textures and models the kit uses that assets.jsonc has no record of.</summary>
    private static List<string> Unrecorded(GameData data, IDataSource source)
    {
        ProvenanceFile provenance = Jsonc.Load<ProvenanceFile>(source, "assets.jsonc");
        var recorded = provenance.Assets.SelectMany(a => a.Files).ToHashSet(StringComparer.Ordinal);
        return data.Kit.Materials.SelectMany(m => new[] { m.Def.Albedo, m.Def.Normal, m.Def.RoughnessMap })
            .Concat(data.Kit.Props.Values.Select(p => p.Def.Model))
            .Where(path => !string.IsNullOrWhiteSpace(path) && !recorded.Contains(path))
            .Select(path => path!)
            .ToList();
    }

    private sealed class ProvenanceFile : IValidatable
    {
        public AssetRecord[] Assets { get; set; } = Array.Empty<AssetRecord>();

        public void Validate(Validator v)
        {
            for (int i = 0; i < Assets.Length; i++)
            {
                Assets[i].Validate(v.Item(nameof(Assets), i));
            }
        }
    }

    private sealed class AssetRecord : IValidatable
    {
        public string Id { get; set; } = "";

        public string Kind { get; set; } = "";

        public string Job { get; set; } = "";

        public string Generator { get; set; } = "";

        public string Prompt { get; set; } = "";

        public string Source { get; set; } = "";

        public string Imported { get; set; } = "";

        public string[] Files { get; set; } = Array.Empty<string>();

        public string Notes { get; set; } = "";

        public void Validate(Validator v)
        {
            v.NotEmpty(nameof(Id), Id);
            v.NotEmpty(nameof(Job), Job);
            v.NotEmpty(nameof(Generator), Generator);
            v.NotEmpty(nameof(Prompt), Prompt);
            v.NotEmpty(nameof(Source), Source);
            if (Kind is not ("texture" or "model"))
            {
                v.Error(nameof(Kind), $"'{Kind}' is not texture or model");
            }

            if (Files.Length == 0)
            {
                v.Error(nameof(Files), "lists no files");
            }
        }
    }
}
