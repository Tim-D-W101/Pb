using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>Phase 3 objectives (M3.4): Retrieve and Hold, their rules, their starts and the level data behind them.</summary>
public class ObjectiveTests
{
    private const int Second = 120;
    private readonly ITestOutputHelper _out;

    public ObjectiveTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static LevelLayout Level => TestData.Data.Levels["oxbarrow_works"];

    /// <summary>A live Teams round on Oxbarrow Works: you and a teammate (team 0) outside the gate, two of theirs (team 1) inside.</summary>
    private static (SimWorld Sim, PlayerState Hero, PlayerState Mate, PlayerState[] Them) Round(ObjectiveKind kind, ulong seed = 1,
        float timeLimit = 900f, MatchModeKind mode = MatchModeKind.Teams)
    {
        var sim = new SimWorld(TestData.Config, seed);
        sim.LoadLevel(Level);
        PlayerState hero = sim.AddPlayer(0, 0, new Vector3(8f, 0f, 45f), 0f);
        PlayerState mate = sim.AddPlayer(1, 0, new Vector3(11f, 0f, 45f), 0f);
        PlayerState[] them = { sim.AddPlayer(2, 1, new Vector3(45f, 0f, 20f), 0f), sim.AddPlayer(3, 1, new Vector3(-50f, 0f, 35f), 0f) };
        sim.StartMatch(new MatchSetup
        {
            HeroId = 0, Mode = mode, TimeLimit = timeLimit, StartPods = 2, BotPods = 2, Pickups = false, Objective = kind,
        });
        sim.GoLive();
        return (sim, hero, mate, them);
    }

    private static List<SimEvent> Step(SimWorld sim, int ticks, Action<int>? each = null)
    {
        var events = new List<SimEvent>();
        var commands = new InputCommand[sim.Players.Count];
        for (int t = 0; t < ticks; t++)
        {
            each?.Invoke(t);
            sim.Step(commands);
            events.AddRange(sim.Events.Items.ToArray());
            sim.Events.Clear();
        }

        return events;
    }

    [Fact]
    public void The_case_starts_at_a_spot_dealt_from_the_seed()
    {
        var spots = new HashSet<int>();
        for (ulong seed = 1; seed <= 16; seed++)
        {
            ObjectiveState objective = Round(ObjectiveKind.Retrieve, seed).Sim.Match!.Objective!;
            Assert.InRange(objective.CaseSpotIndex, 0, Level.Objectives.CaseSpots.Count - 1);
            Assert.Equal(Level.Objectives.CaseSpots[objective.CaseSpotIndex].Position, objective.CasePosition);
            Assert.Equal(objective.CaseSpotIndex, Round(ObjectiveKind.Retrieve, seed).Sim.Match!.Objective!.CaseSpotIndex);
            // The starts are dealt round the same spot.
            Assert.Equal(objective.CasePosition, ObjectiveFocus.For(ObjectiveKind.Retrieve, Level.Objectives, TestData.Config.Rules.Objectives, seed)!.At);
            spots.Add(objective.CaseSpotIndex);
        }

        Assert.True(spots.Count >= 3, $"16 seeds dealt only {spots.Count} different spots");
    }

    [Fact]
    public void Your_side_picks_up_the_case_and_carrying_it_out_wins()
    {
        (SimWorld sim, PlayerState hero, _, _) = Round(ObjectiveKind.Retrieve);
        ObjectiveState objective = sim.Match!.Objective!;
        hero.Position = objective.CasePosition + new Vector3(0.6f, 0f, 0f);
        List<SimEvent> taken = Step(sim, 1);
        Assert.Contains(taken, e => e.Type == SimEventType.CaseTaken && e.PlayerId == hero.Id);
        Assert.Equal(hero.Id, objective.Carrier);
        Assert.True(hero.SprintBlocked, "the carrier can still sprint");

        // It goes where they go ...
        hero.Position = new Vector3(0f, 0f, 20f);
        Step(sim, 1);
        Assert.Equal(hero.Position, objective.CasePosition);
        Assert.Equal(RoundOutcome.None, sim.Match.Outcome);

        // ... and out through a way out.
        ExitSpec exit = Level.Objectives.Exits[0];
        hero.Position = exit.Position + new Vector3(2f, 0f, 1f);
        List<SimEvent> extracted = Step(sim, 2);
        Assert.Contains(extracted, e => e.Type == SimEventType.CaseExtracted && e.PlayerId == hero.Id && e.Extra == 0);
        Assert.Equal(MatchPhase.Ended, sim.Match.Phase);
        Assert.Equal(RoundOutcome.Extracted, sim.Match.Outcome);
        Assert.True(sim.Match.Outcome.IsWin());
        Assert.False(hero.SprintBlocked);
    }

    [Fact]
    public void Their_side_cant_pick_the_case_up()
    {
        (SimWorld sim, _, _, PlayerState[] them) = Round(ObjectiveKind.Retrieve);
        ObjectiveState objective = sim.Match!.Objective!;
        them[0].Position = objective.CasePosition;
        List<SimEvent> events = Step(sim, Second);
        Assert.DoesNotContain(events, e => e.Type == SimEventType.CaseTaken);
        Assert.Equal(-1, objective.Carrier);
        Assert.False(objective.CaseMoved);
    }

    [Fact]
    public void A_carrier_put_out_drops_the_case_where_they_fell_for_a_teammate_to_pick_up()
    {
        (SimWorld sim, PlayerState hero, PlayerState mate, _) = Round(ObjectiveKind.Retrieve);
        ObjectiveState objective = sim.Match!.Objective!;
        hero.Position = objective.CasePosition;
        Step(sim, 1);
        var fell = new Vector3(-6f, 0f, 12f);
        hero.Position = fell;
        Step(sim, 1);

        hero.Alive = false;
        List<SimEvent> dropped = Step(sim, 1);
        Assert.Contains(dropped, e => e.Type == SimEventType.CaseDropped && e.PlayerId == hero.Id && e.Position == fell);
        Assert.Equal(-1, objective.Carrier);
        Assert.Equal(fell, objective.CasePosition);
        Assert.False(hero.SprintBlocked);
        Assert.Equal(RoundOutcome.None, sim.Match.Outcome); // your teammate is still in

        mate.Position = fell + new Vector3(0f, 0f, 0.8f);
        List<SimEvent> again = Step(sim, 1);
        Assert.Contains(again, e => e.Type == SimEventType.CaseTaken && e.PlayerId == mate.Id);
        Assert.Equal(mate.Id, objective.Carrier);
    }

    [Fact]
    public void A_carrier_cant_sprint()
    {
        (SimWorld sim, PlayerState hero, _, _) = Round(ObjectiveKind.Retrieve);
        MovementParams movement = TestData.Config.Movement;
        var sprint = new InputCommand { Move = new Vector2(0f, 1f), Buttons = InputButtons.Sprint };
        Assert.True(MovementModel.Step(hero, sprint, movement, 1f / 120f, grounded: true).Sprinting);

        hero.Position = sim.Match!.Objective!.CasePosition;
        Step(sim, 1);
        MovementResult carrying = MovementModel.Step(hero, sprint, movement, 1f / 120f, grounded: true);
        Assert.False(carrying.Sprinting);
        Assert.True(carrying.HorizontalVelocity.Length() <= movement.RunSpeed + 1e-3f);
    }

    [Fact]
    public void Running_out_of_time_before_the_case_is_out_loses()
    {
        (SimWorld sim, PlayerState hero, _, _) = Round(ObjectiveKind.Retrieve, timeLimit: 2f);
        hero.Position = sim.Match!.Objective!.CasePosition;
        Step(sim, (int)(2.1f * Second));
        Assert.Equal(RoundOutcome.TimeUp, sim.Match.Outcome);
        Assert.False(sim.Match.Outcome.IsWin());
    }

    [Fact]
    public void Putting_the_other_side_out_still_wins_and_being_put_out_still_loses()
    {
        (SimWorld sim, _, _, PlayerState[] them) = Round(ObjectiveKind.Retrieve);
        foreach (PlayerState p in them)
        {
            p.Alive = false;
        }

        Step(sim, 2);
        Assert.Equal(RoundOutcome.Cleared, sim.Match!.Outcome);

        (SimWorld lost, PlayerState hero, PlayerState mate, _) = Round(ObjectiveKind.Hold);
        hero.Alive = false;
        mate.Alive = false;
        Step(lost, 2);
        Assert.Equal(RoundOutcome.Eliminated, lost.Match!.Outcome);
    }

    [Fact]
    public void Holding_the_room_alone_for_the_hold_time_wins()
    {
        (SimWorld sim, PlayerState hero, _, _) = Round(ObjectiveKind.Hold);
        ObjectiveState objective = sim.Match!.Objective!;
        float holdTime = TestData.Config.Rules.Objectives.Hold.HoldTime;
        hero.Position = objective.Room!.Centre;
        List<SimEvent> events = Step(sim, (int)(holdTime * 0.5f * Second));
        Assert.Contains(events, e => e.Type == SimEventType.HoldChanged && e.Extra == (int)HoldStatus.Ours);
        Assert.Equal(HoldStatus.Ours, objective.Status);
        Assert.InRange(objective.Held, holdTime * 0.5f - 0.05f, holdTime * 0.5f + 0.05f);
        Assert.Equal(RoundOutcome.None, sim.Match.Outcome);

        Step(sim, (int)(holdTime * 0.5f * Second) + 2);
        Assert.True(objective.Done);
        Assert.Equal(RoundOutcome.Held, sim.Match.Outcome);
        Assert.True(sim.Match.Outcome.IsWin());
    }

    [Fact]
    public void Contested_time_doesnt_count_and_leaving_doesnt_lose_whats_held()
    {
        (SimWorld sim, PlayerState hero, _, PlayerState[] them) = Round(ObjectiveKind.Hold);
        ObjectiveState objective = sim.Match!.Objective!;
        HoldRoom room = objective.Room!;
        hero.Position = room.Centre;
        Step(sim, 10 * Second);
        Assert.InRange(objective.Held, 9.95f, 10.05f);

        them[0].Position = room.Centre + new Vector3(0.5f, 0f, 0f);
        List<SimEvent> contested = Step(sim, 10 * Second);
        Assert.Contains(contested, e => e.Type == SimEventType.HoldChanged && e.Extra == (int)HoldStatus.Contested);
        Assert.InRange(objective.Held, 9.95f, 10.05f);

        hero.Position = new Vector3(0f, 0f, 20f); // out in the yard
        List<SimEvent> left = Step(sim, 5 * Second);
        Assert.Contains(left, e => e.Type == SimEventType.HoldChanged && e.Extra == (int)HoldStatus.Theirs);
        Assert.InRange(objective.Held, 9.95f, 10.05f);

        them[0].Position = new Vector3(40f, 0f, 20f);
        hero.Position = room.Centre;
        Step(sim, 5 * Second);
        Assert.InRange(objective.Held, 14.9f, 15.1f);
        Assert.Equal(RoundOutcome.None, sim.Match.Outcome);
    }

    [Fact]
    public void Free_for_all_is_always_last_one_standing()
    {
        TierDef tier = TestData.Data.Areas.Areas[0].Tiers[0];
        Assert.Equal(ObjectiveKind.Eliminate, MatchSetup.From(tier, 0, MatchModeKind.FreeForAll, ObjectiveKind.Retrieve).Objective);
        (SimWorld sim, _, _, _) = Round(ObjectiveKind.Retrieve, mode: MatchModeKind.FreeForAll);
        Assert.Null(sim.Match!.Objective);
    }

    [Fact]
    public void An_objective_needs_a_level_with_places_for_it()
    {
        var sim = new SimWorld(TestData.Config);
        sim.AddPlayer(0, 0, Vector3.Zero, 0f);
        Assert.Throws<InvalidOperationException>(() => sim.StartMatch(new MatchSetup
        {
            HeroId = 0, TimeLimit = 60f, StartPods = 1, BotPods = 1, Pickups = false, Objective = ObjectiveKind.Hold,
        }));
    }

    [Fact]
    public void Oxbarrow_has_case_spots_in_buildings_ways_out_and_rooms()
    {
        LevelObjectives places = Level.Objectives;
        Assert.True(places.Offers(ObjectiveKind.Retrieve) && places.Offers(ObjectiveKind.Hold));
        Assert.Equal(5, places.CaseSpots.Count);
        Assert.All(places.CaseSpots, s => Assert.True(s.AreaBox.Contains(s.Position + new Vector3(0f, 0.1f, 0f))));
        Assert.Contains(places.CaseSpots, s => s.Area == "pump house");
        Assert.Contains(places.CaseSpots, s => s.Area == "warehouse mezzanine");
        Assert.Equal(new[] { "the main gate", "the west breach", "the north-east collapse" }, places.Exits.Select(e => e.Name));
        Assert.Equal(3, places.Rooms.Count);
        Assert.All(places.Rooms, r => Assert.True(r.Contains(r.Centre + new Vector3(0f, 0.1f, 0f)), $"{r.Name}'s middle isn't in it"));
        Assert.Equal(2, places.Rooms.First(r => r.Name == "warehouse mezzanine").Boxes.Count);
    }

    [Fact]
    public void Case_spots_outdoors_and_unknown_rooms_fail_to_load()
    {
        var outdoors = new EditedDataSource(TestData.Source).Edit("levels/oxbarrow_works.jsonc", t => t.Replace("[33, 0, -31]", "[10, 0, 10]"));
        DataException spot = Assert.Throws<DataException>(() => GameData.Load(outdoors));
        Assert.Contains("caseSpots_m[4]", spot.Message);
        Assert.Contains("indoor", spot.Message);

        var noRoom = new EditedDataSource(TestData.Source).Edit("levels/oxbarrow_works.jsonc", t => t.Replace("\"pump house\", \"warehouse", "\"boiler room\", \"warehouse"));
        DataException room = Assert.Throws<DataException>(() => GameData.Load(noRoom));
        Assert.Contains("no area is called 'boiler room'", room.Message);
    }

    [Fact]
    public void Stepping_an_objective_round_allocates_nothing()
    {
        foreach (ObjectiveKind kind in new[] { ObjectiveKind.Retrieve, ObjectiveKind.Hold })
        {
            (SimWorld sim, PlayerState hero, _, PlayerState[] them) = Round(kind);
            ObjectiveState objective = sim.Match!.Objective!;
            Vector3 start = kind == ObjectiveKind.Retrieve ? objective.CasePosition : objective.Room!.Centre;
            var commands = new InputCommand[sim.Players.Count];

            void Steps(int from, int count)
            {
                for (int t = from; t < from + count; t++)
                {
                    // You wander round the case or the room (carrying, holding), and one of theirs comes and goes.
                    hero.Position = start + new Vector3(MathF.Sin(t * 0.01f) * 0.4f, 0f, 0f);
                    them[0].Position = t % 600 < 300 ? start + new Vector3(0.5f, 0f, 0f) : new Vector3(45f, 0f, 20f);
                    sim.Step(commands);
                    sim.Events.Clear();
                }
            }

            Steps(0, 700);
            Assert.Equal(0, Allocations.During(() => Steps(700, 700)));
        }
    }

    [Fact]
    public void The_defenders_start_round_the_objective()
    {
        LevelLayout level = Level;
        NavGrid grid = NavGrid.Build(level, TestData.Data.Bots.Navigation);
        var world = new CollisionWorld();
        level.BuildCollision(world);
        CoverSet cover = CoverSet.Build(level, grid, TestData.Config.Movement.StandEyeHeight, TestData.Config.Movement.CrouchEyeHeight);
        ObjectiveRules rules = TestData.Config.Rules.Objectives;
        for (ulong seed = 1; seed <= 6; seed++)
        {
            foreach (ObjectiveKind kind in new[] { ObjectiveKind.Retrieve, ObjectiveKind.Hold })
            {
                ObjectiveFocus focus = ObjectiveFocus.For(kind, level.Objectives, rules, seed)!;
                SpawnPlan plan = SpawnPlanner.Plan(level, cover, world, TestData.Config.Rules.Spawning, TestData.Data.Bots,
                    RoundShape.Solo(6, focus), TestData.Config.Movement.StandEyeHeight, seed);
                OpponentSpawn[] guards = plan.Opponents.Take(focus.Guards).ToArray();
                float[] distances = plan.Opponents.Select(o => Vector3.Distance(o.Position, focus.At)).ToArray();
                _out.WriteLine($"seed {seed} {kind} at {focus.At}{(focus.Room is { } r ? $" ({r.Name})" : "")}: " +
                               string.Join(", ", plan.Opponents.Select((o, i) => $"{o.Roles[0]} {distances[i]:0} m")));
                Assert.All(guards, g => Assert.Equal(rules.GuardRole, g.Roles[0]));
                if (focus.Room is { } room)
                {
                    Assert.All(guards, g => Assert.True(room.Contains(g.Position + new Vector3(0f, 0.1f, 0f)) || Vector3.Distance(g.Position, focus.At) < 10f,
                        $"a guard starts {Vector3.Distance(g.Position, focus.At):0} m from {room.Name}, outside it"));
                }
                else
                {
                    Assert.All(guards, g => Assert.True(Vector3.Distance(g.Position, focus.At) < 10f, $"a guard starts {Vector3.Distance(g.Position, focus.At):0} m from the case"));
                }

                int near = distances.Count(d => d <= focus.Near);
                Assert.True(near >= focus.Guards + 2, $"only {near} of 6 start within {focus.Near} m");
            }
        }
    }
}
