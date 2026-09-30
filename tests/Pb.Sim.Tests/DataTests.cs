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
}
