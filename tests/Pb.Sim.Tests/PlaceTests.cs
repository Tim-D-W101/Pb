using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Xunit;

namespace Pb.Sim.Tests;

/// <summary>
/// The places to play in every area: all open, each walled in for walking (not for paint), with its own starts, and
/// only the objectives that fit inside it.
/// </summary>
public class PlaceTests
{
    private static GameData Data => TestData.Data;

    private static LevelLayout Works => Data.Levels["oxbarrow_works"];

    /// <summary>Every part of every area (not the wholes), as test cases.</summary>
    public static IEnumerable<object[]> Parts() =>
        Data.Areas.Areas.SelectMany(a => Data.Levels[a.Id].Places.Where(p => !p.Whole).Select(p => new object[] { a.Id, p.Id }));

    /// <summary>Every place of every area, wholes included.</summary>
    public static IEnumerable<object[]> AllPlaces() =>
        Data.Areas.Areas.SelectMany(a => Data.Levels[a.Id].Places.Select(p => new object[] { a.Id, p.Id }));

    [Fact]
    public void Every_area_offers_the_whole_of_it_and_its_parts_and_none_is_locked()
    {
        Assert.True(Data.Areas.Areas.Length >= 4);
        foreach (AreaEntryDef area in Data.Areas.Areas)
        {
            LevelLayout level = Data.Levels[area.Id];
            Assert.True(level.Places[0].Whole, $"{area.Id} doesn't start with the whole of it");
            Assert.Equal(RecordBook.WholeArea, level.Places[0].Id);
            Assert.True(level.Places.Count >= 4, $"{area.Id} offers only {level.Places.Count} places");
            Assert.Equal(level.Places.Count, level.Places.Select(p => p.Id).Distinct().Count());
        }

        Assert.Equal(new[] { "whole", "warehouse", "offices", "yard", "east_field" }, Works.Places.Select(p => p.Id));
    }

    [Theory]
    [MemberData(nameof(Parts))]
    public void A_place_keeps_only_what_lies_inside_it(string areaId, string placeId)
    {
        LevelLayout area = Data.Levels[areaId];
        PlaceSpec place = area.PlaceOf(placeId);
        LevelLayout level = area.ForPlace(place);
        Assert.Same(place, level.Place);
        Assert.NotEmpty(level.PlayerSpawns);
        Assert.All(level.PlayerSpawns, s => Assert.True(place.Contains(s.Position), $"{place.Id}: an entry outside it"));
        Assert.NotEmpty(level.OpponentSpawns);
        Assert.All(level.OpponentSpawns, s => Assert.True(place.Contains(s.Position), $"{place.Id}: spawn {s.Id} outside it"));
        Assert.All(level.Patrols, r => Assert.All(r.Points, p => Assert.True(place.Contains(p))));
        Assert.All(level.Pickups, p => Assert.True(place.Contains(p.Position), $"{place.Id}: pickup {p.Id} outside it"));
        Assert.True(place.Contains(level.DeadZone), $"{place.Id}: the eliminated walk off outside it");
        Assert.True(place.Contains(level.SpawnArea.Min) && place.Contains(level.SpawnArea.Max), $"{place.Id}: starts dealt outside it");

        // Objectives: the case and the rooms only where they're inside, the ways out inside or else the way in.
        Assert.All(level.Objectives.CaseSpots, c => Assert.True(place.Contains(c.Position)));
        Assert.All(level.Objectives.Exits, e => Assert.True(place.Contains(e.Position), $"{place.Id}: way out {e.Name} outside it"));
        Assert.All(level.Objectives.Rooms, r => Assert.All(r.Boxes, b => Assert.True(place.Contains((b.Min + b.Max) * 0.5f))));
        Assert.Equal(area.Objectives.CaseSpots.Count(c => place.Contains(c.Position)), level.Objectives.CaseSpots.Count);

        // Four walls round it for walking, none for paint: shots fly over the tape. Every door stays.
        Assert.Equal(area.Primitives.Count + 4, level.Primitives.Count);
        Assert.All(level.Primitives.Skip(area.Primitives.Count), p =>
        {
            Assert.Equal(PrimitiveRole.Boundary, p.Role);
            Assert.Equal(PrimitiveFlags.Walk, p.Flags);
        });
        Assert.Equal(area.Doors.Count, level.Doors.Count);
    }

    [Fact]
    public void The_whole_area_keeps_everything()
    {
        foreach (AreaEntryDef entry in Data.Areas.Areas)
        {
            LevelLayout area = Data.Levels[entry.Id];
            Assert.Same(area, area.ForPlace(area.PlaceOf(RecordBook.WholeArea)));
            Assert.Same(area.Objectives, area.ForPlace(area.Places[0]).Objectives);
        }
    }

    [Theory]
    [MemberData(nameof(Parts))]
    public void Nobody_walks_out_of_a_place_and_its_cover_is_all_inside(string areaId, string placeId)
    {
        LevelLayout area = Data.Levels[areaId];
        PlaceSpec place = area.PlaceOf(placeId);
        LevelLayout level = area.ForPlace(place);
        NavGrid grid = NavGrid.Build(level, Data.Bots.Navigation);
        CoverSet cover = CoverSet.Build(level, grid, TestData.Config.Movement.StandEyeHeight, TestData.Config.Movement.CrouchEyeHeight);
        Assert.NotEmpty(cover.Points);
        Assert.All(cover.Points, c => Assert.True(place.Contains(c.Position), $"{placeId}: cover at {c.Position} outside it"));

        // From every entry there's a way to every opponent spawn, case spot and room inside, and none out of the place.
        var path = new List<Vector3>();
        var goals = level.OpponentSpawns.Select(s => (s.Id, s.Position))
            .Concat(level.Objectives.CaseSpots.Select(c => ($"case in {c.Area}", c.Position)))
            .Concat(level.Objectives.Rooms.Select(r => (r.Name, r.Centre)));
        foreach (SpawnPoint entry in level.PlayerSpawns)
        {
            Assert.True(grid.SpanAt(entry.Position) >= 0, $"{placeId}: the entry at {entry.Position} is off the walkable ground");
            foreach ((string name, Vector3 at) in goals)
            {
                Assert.True(grid.FindPath(entry.Position, at, path), $"{placeId}: no way from the entry at {entry.Position} to {name}");
                Assert.All(path, p => Assert.True(place.Contains(p), $"{placeId}: the way to {name} leaves the place at {p}"));
            }
        }

        Assert.True(grid.SpanAt(level.DeadZone) >= 0 || grid.FindPath(level.PlayerSpawns[0].Position, level.DeadZone, path),
            $"{placeId}: the walk-off spot is out of reach");
        foreach (OpponentSpawn outside in area.OpponentSpawns.Where(s => !place.Contains(s.Position)).Take(3))
        {
            Assert.False(grid.FindPath(level.PlayerSpawns[0].Position, outside.Position, path), $"{placeId}: a way out to {outside.Id}");
        }
    }

    [Theory]
    [MemberData(nameof(AllPlaces))]
    public void Every_place_starts_every_mode_size_and_objective_inside_it_and_fairly(string areaId, string placeId)
    {
        LevelLayout area = Data.Levels[areaId];
        PlaceSpec place = area.PlaceOf(placeId);
        LevelLayout level = area.ForPlace(place);
        var sim = new SimWorld(TestData.Config);
        sim.LoadLevel(level);
        NavGrid grid = NavGrid.Build(level, Data.Bots.Navigation);
        CoverSet cover = CoverSet.Build(level, grid, TestData.Config.Movement.StandEyeHeight, TestData.Config.Movement.CrouchEyeHeight);
        ObjectiveRules objectives = TestData.Config.Rules.Objectives;
        foreach (GameMode mode in TestData.Config.Rules.Modes)
        {
            IEnumerable<ObjectiveKind> kinds = mode.Kind == MatchModeKind.FreeForAll
                ? new[] { ObjectiveKind.Eliminate }
                : objectives.Kinds.Select(k => k.Kind).Where(level.Objectives.Offers);
            foreach (ObjectiveKind kind in kinds)
            {
                foreach (int size in mode.Sizes)
                {
                    for (ulong seed = 1; seed <= 2; seed++)
                    {
                        ObjectiveFocus? focus = ObjectiveFocus.For(kind, level.Objectives, objectives, seed);
                        SpawnPlan plan = SpawnPlanner.Plan(level, cover, sim.Collision, TestData.Config.Rules.Spawning, Data.Bots,
                            RoundShape.Of(mode, size, focus), TestData.Config.Movement.StandEyeHeight, seed);
                        string what = $"{placeId}, {mode.Id} {kind} {size}, seed {seed}";
                        Assert.Contains(plan.You, level.PlayerSpawns);
                        Assert.Equal(mode.TeammatesFor(size), plan.Teammates.Count);
                        Assert.Equal(mode.OpponentsFor(size), plan.Opponents.Count);
                        foreach (OpponentSpawn start in plan.Teammates.Concat(plan.Opponents))
                        {
                            Assert.True(place.Contains(start.Position), $"{what}: {start.Id} starts outside the place");
                            Assert.True(grid.SpanAt(start.Position) >= 0, $"{what}: {start.Id} starts off the walkable ground");
                        }

                        // Nobody starts on top of anybody.
                        var everyone = plan.Teammates.Concat(plan.Opponents).Select(s => s.Position).Append(plan.You.Position).ToArray();
                        for (int a = 0; a < everyone.Length; a++)
                        {
                            for (int b = a + 1; b < everyone.Length; b++)
                            {
                                Assert.True(Vector3.Distance(everyone[a], everyone[b]) > 0.6f, $"{what}: two start in the same spot");
                            }
                        }
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllPlaces))]
    public void Every_place_has_a_picture_for_the_menu_from_inside_it(string areaId, string placeId)
    {
        LevelLayout area = Data.Levels[areaId];
        PlaceSpec place = area.PlaceOf(placeId);
        Assert.Contains(place.Still, area.Viewpoints);
        Assert.True(place.Contains(place.Still.Position), $"{placeId}: its picture is taken from outside it");
    }

    [Fact]
    public void Each_part_of_an_area_has_a_picture_of_its_own()
    {
        foreach (AreaEntryDef entry in Data.Areas.Areas)
        {
            LevelLayout area = Data.Levels[entry.Id];
            Assert.Equal(area.Places.Count, area.Places.Select(p => p.Still.Name).Distinct().Count());
        }
    }

    [Fact]
    public void Bad_places_are_named_by_file_and_key()
    {
        var outside = new EditedDataSource(TestData.Source).Edit("levels/oxbarrow_works.jsonc",
            t => t.Replace("\"rect_m\": [26, -48, 54.7, 39.7]", "\"rect_m\": [26, -48, 80, 39.7]"));
        DataException ex = Assert.Throws<DataException>(() => GameData.Load(outside));
        Assert.Contains("levels/oxbarrow_works.jsonc", ex.Message);
        Assert.Contains("places[4].rect_m", ex.Message);

        // A part of the level with no entry of its own and none of the level's inside it.
        var noWayIn = new EditedDataSource(TestData.Source).Edit("levels/oxbarrow_works.jsonc", t => t.Replace(
            "\"playerSpawns\": [\n        { \"position_m\": [-16, 0, 16], \"yaw_deg\": 90 },\n        { \"position_m\": [-33, 0, 30], \"yaw_deg\": 0 }\n      ],\n", ""));
        ex = Assert.Throws<DataException>(() => GameData.Load(noWayIn));
        Assert.Contains("places[2].playerSpawns", ex.Message);

        // The whole level comes first, by the name the records keep.
        var renamed = new EditedDataSource(TestData.Source).Edit("levels/rail_yard.jsonc",
            t => t.Replace("\"id\": \"whole\", \"displayName\": \"The whole yard\"", "\"id\": \"everything\", \"displayName\": \"The whole yard\""));
        ex = Assert.Throws<DataException>(() => GameData.Load(renamed));
        Assert.Contains("levels/rail_yard.jsonc", ex.Message);
        Assert.Contains("places", ex.Message);

        // A picture from a viewpoint the level hasn't got, or from outside the place.
        var noSuchView = new EditedDataSource(TestData.Source).Edit("levels/rail_yard.jsonc",
            t => t.Replace("\"still\": \"between the wagons\"", "\"still\": \"between the carriages\""));
        ex = Assert.Throws<DataException>(() => GameData.Load(noSuchView));
        Assert.Contains("places[2].still", ex.Message);
        Assert.Contains("between the carriages", ex.Message);
        var outsideView = new EditedDataSource(TestData.Source).Edit("levels/rail_yard.jsonc",
            t => t.Replace("\"still\": \"between the wagons\"", "\"still\": \"inside the engine shed\""));
        ex = Assert.Throws<DataException>(() => GameData.Load(outsideView));
        Assert.Contains("places[2].still", ex.Message);
    }
}
