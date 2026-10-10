using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>
/// Every area's level, held to the same bar: a grid that reaches everything the level names from every
/// way in, starts for every mode and objective, and whole bot rounds that play out without errors.
/// </summary>
[Collection(BotArenaCollection.Name)]
public class AreaLevelTests
{
    private const int Second = 120;
    private readonly ITestOutputHelper _out;

    public AreaLevelTests(ITestOutputHelper output)
    {
        _out = output;
    }

    public static IEnumerable<object[]> Levels() =>
        TestData.Data.Areas.Areas.Select(l => new object[] { l.Id });

    [Theory]
    [MemberData(nameof(Levels))]
    public void The_grid_reaches_everything_the_level_names_from_every_way_in(string levelId)
    {
        (LevelLayout level, NavGrid grid, _) = BotArena.SharedFor(levelId);
        _out.WriteLine($"{grid.Columns}×{grid.Rows} columns, {grid.SpanCount} spans ({grid.LinkedSpanCount} linked)");
        Assert.True(grid.LinkedSpanCount > grid.SpanCount * 0.85f);

        var goals = new List<(string Name, Vector3 At)>();
        goals.AddRange(level.OpponentSpawns.Select(s => ($"spawn {s.Id}", s.Position)));
        goals.AddRange(level.Patrols.SelectMany(r => r.Points.Select((p, i) => ($"patrol {r.Id}[{i}]", p))));
        goals.AddRange(level.Pickups.Select(p => ($"pickup {p.Id}", p.Position)));
        goals.AddRange(level.Objectives.CaseSpots.Select((c, i) => ($"case spot {i} ({c.Area})", c.Position)));
        goals.AddRange(level.Objectives.Exits.Select(e => ($"the way out {e.Name}", e.Position)));
        goals.AddRange(level.Objectives.Rooms.Select(r => ($"the middle of {r.Name}", r.Centre)));
        var path = new List<Vector3>();
        foreach (SpawnPoint entry in level.PlayerSpawns)
        {
            foreach ((string name, Vector3 at) in goals)
            {
                Assert.True(grid.FindPath(entry.Position, at, path), $"no path from {entry.Position} to {name} at {at}");
                Vector3 end = path[^1];
                Assert.True(Vector3.Distance(end, at) < 1.5f, $"the path from {entry.Position} to {name} ends {Vector3.Distance(end, at):0.00} m away, at {end}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Levels))]
    public void Every_mode_and_objective_deals_starts_at_every_size(string levelId)
    {
        (LevelLayout level, _, CoverSet cover) = BotArena.SharedFor(levelId);
        var world = new CollisionWorld();
        level.BuildCollision(world);
        // Every compound offers the objectives; a field (the Sports Ground) has no buildings to carry a case out of or hold.
        bool field = level.Field is not null;
        Assert.True(field || level.Objectives.Offers(ObjectiveKind.Retrieve), "no case spots or ways out");
        Assert.True(field || level.Objectives.Offers(ObjectiveKind.Hold), "no rooms to hold");
        ObjectiveRules rules = TestData.Config.Rules.Objectives;
        foreach (GameMode mode in TestData.Config.Rules.Modes)
        {
            foreach (ObjectiveKind kind in mode.Kind == MatchModeKind.FreeForAll || field
                         ? new[] { ObjectiveKind.Eliminate }
                         : new[] { ObjectiveKind.Eliminate, ObjectiveKind.Retrieve, ObjectiveKind.Hold })
            {
                foreach (int size in mode.Sizes)
                {
                    ulong seed = (ulong)(size * 31 + mode.Id.Length + (int)kind * 7);
                    ObjectiveFocus? focus = ObjectiveFocus.For(kind, level.Objectives, rules, seed);
                    SpawnPlan plan = SpawnPlanner.Plan(level, cover, world, TestData.Config.Rules.Spawning, TestData.Data.Bots,
                        RoundShape.Of(mode, size, focus), TestData.Config.Movement.StandEyeHeight, seed);
                    Assert.Equal(mode.TeammatesFor(size), plan.Teammates.Count);
                    Assert.Equal(mode.OpponentsFor(size), plan.Opponents.Count);
                    var ids = plan.Teammates.Concat(plan.Opponents).Select(o => o.Id).ToList();
                    Assert.Equal(ids.Count, ids.Distinct().Count());
                }
            }
        }
    }

    /// <summary>Each level in each mode, and with each objective it offers, from random starts.</summary>
    public static IEnumerable<object[]> Rounds() =>
        Levels().Select(l => (string)l[0]).SelectMany(level => new[]
        {
            new object[] { level, "solo", 6, ObjectiveKind.Eliminate, 1UL },
            new object[] { level, "teams", 4, ObjectiveKind.Eliminate, 2UL },
            new object[] { level, "ffa", 8, ObjectiveKind.Eliminate, 3UL },
            new object[] { level, "teams", 3, ObjectiveKind.Retrieve, 4UL },
            new object[] { level, "solo", 5, ObjectiveKind.Hold, 5UL },
        }.Where(round => (ObjectiveKind)round[3] == ObjectiveKind.Eliminate || TestData.Data.Levels[level].Objectives.Offers((ObjectiveKind)round[3])));

    [Theory]
    [MemberData(nameof(Rounds))]
    public void Bot_rounds_play_out_without_errors(string levelId, string modeId, int size, ObjectiveKind objective, ulong seed)
    {
        // Bots in every slot, yours included. Any exception fails the test.
        BotArena arena = BotArena.Create("normal", seed, levelId);
        GameMode mode = TestData.Config.Rules.FindMode(modeId)!;
        ObjectiveFocus? focus = ObjectiveFocus.For(objective, arena.Level.Objectives, TestData.Config.Rules.Objectives, seed);
        SpawnPlan plan = SpawnPlanner.Plan(arena.Level, arena.Squad.Cover, arena.Sim.Collision, TestData.Config.Rules.Spawning, TestData.Data.Bots,
            RoundShape.Of(mode, size, focus), TestData.Config.Movement.StandEyeHeight, seed);
        arena.PlaceHero(plan.You.Position, plan.You.Position + ViewAngles.FlatForward(plan.You.Yaw));
        arena.HeroBot().RestlessAfter = mode.RestlessAfter;
        foreach (OpponentSpawn mate in plan.Teammates)
        {
            arena.AddBotAt(mate, team: 0).RestlessAfter = mode.RestlessAfter;
        }

        byte team = 1;
        foreach (OpponentSpawn o in plan.Opponents)
        {
            arena.AddBotAt(o, mode.Kind == MatchModeKind.FreeForAll ? team++ : (byte)1).RestlessAfter = mode.RestlessAfter;
        }

        _out.WriteLine($"you at {plan.You.Position}; with you: {string.Join(", ", plan.Teammates.Select(o => $"{o.Id} {o.Roles[0]} at {o.Position}"))}; " +
                       $"against you: {string.Join(", ", plan.Opponents.Select(o => $"{o.Id} {o.Roles[0]} at {o.Position} ({Vector3.Distance(o.Position, plan.You.Position):0} m)"))}");
        arena.Start(timeLimit: 240f, mode: mode.Kind, objective: objective);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        arena.Run(250 * Second, () => arena.Sim.Match!.Phase == MatchPhase.Ended);
        MatchState match = arena.Sim.Match!;
        _out.WriteLine($"{match.Outcome} after {match.Elapsed:0} s ({watch.Elapsed.TotalSeconds:0.0} s to run)");
        foreach (SimEvent e in arena.Log.Where(e => e.Type == SimEventType.PlayerEliminated))
        {
            PlayerState? shooter = arena.Sim.FindPlayer(e.PlayerId), victim = arena.Sim.FindPlayer(e.TargetId);
            _out.WriteLine($"  t={e.Tick / (float)Second:0.0}s {shooter?.Name} (team {shooter?.Team}) put out {victim?.Name} (team {victim?.Team})");
        }

        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.NotEqual(RoundOutcome.None, match.Outcome);
        Assert.Contains(arena.Log, e => e.Type == SimEventType.ShotFired);
    }
}
