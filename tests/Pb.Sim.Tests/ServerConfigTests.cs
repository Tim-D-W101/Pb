using Pb.Game.Net;
using Pb.Sim.Data;

namespace Pb.Sim.Tests;

/// <summary>Phase 4 (M4.5): the dedicated server's server.jsonc, as shipped.</summary>
public class ServerConfigTests
{
    [Fact]
    public void The_shipped_server_config_names_only_rounds_the_data_has()
    {
        ServerDef def = Jsonc.Load<ServerDef>(TestData.Source, ServerDef.File);
        Assert.NotEmpty(def.Rotation);
        Assert.InRange(def.MaxPeople, 1, 10);
        foreach (RotationDef r in def.Rotation)
        {
            Assert.True(TestData.Data.Levels.TryGetValue(r.Level, out Level.LevelLayout? level), $"no area '{r.Level}'");
            Assert.True(r.Place is null || level!.Places.Any(p => p.Id == r.Place), $"{r.Level} has no place '{r.Place}'");
            Match.GameMode mode = Assert.IsType<Match.GameMode>(TestData.Config.Rules.FindMode(r.Mode));
            // Phase 5 (M5.6): only a mode the area offers (the field plays speedball alone).
            Assert.Contains(mode, TestData.Config.Rules.ModesFor(TestData.Data.Areas.Areas.First(a => a.Id == r.Level)));
            Assert.Contains(r.Size, mode.Sizes);
            Assert.Contains(TestData.Data.Areas.Areas.First(a => a.Id == r.Level).Tiers, t => t.Id == r.Tier);
        }
    }
}
