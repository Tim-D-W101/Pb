using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Xunit;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>
/// After the play-test (objective rounds over in seconds, or run out with nobody at the objective): rounds played by
/// bots in every slot that used to stall till the clock ran out (a room contested by two sides who couldn't see each
/// other, a carrier and its escort stuck at a door, bots that couldn't get to their spot in the room, three bots holding
/// a door shut by standing in its swing, a bot pressed against an open leaf) now end; and a stale hold gets settled.
/// </summary>
[Collection(BotArenaCollection.Name)]
public class ObjectivePacingTests
{
    private const int Second = 120;

    private readonly ITestOutputHelper _out;

    public ObjectivePacingTests(ITestOutputHelper output)
    {
        _out = output;
    }

    [Theory]
    [InlineData("hold", "oxbarrow_works", 3UL)]   // contested for ten minutes: neither side saw the other
    [InlineData("retrieve", "hospital_wing", 6UL)] // the carrier looked over the door it was opening
    [InlineData("hold", "hospital_wing", 4UL)]     // three bots held the stairwell door a quarter open
    [InlineData("retrieve", "oxbarrow_works", 1UL)] // a bot that couldn't reach its spot by a door waited there for ever
    [InlineData("hold", "cold_store", 2UL)]        // stuck between an opening leaf and the doorway
    [InlineData("hold", "rail_yard", 6UL)]         // pressed against a leaf standing open
    public void A_team_round_that_used_to_stall_ends_before_the_clock(string kindId, string levelId, ulong seed)
    {
        ObjectiveKind kind = kindId == "retrieve" ? ObjectiveKind.Retrieve : ObjectiveKind.Hold;
        BotArena arena = TeamRound(kind, levelId, seed, 600f);
        arena.Run(600 * Second, () => arena.Sim.Match!.Phase == MatchPhase.Ended);
        MatchState match = arena.Sim.Match!;
        _out.WriteLine($"{levelId} {kindId} seed {seed}: {match.Outcome} after {match.Elapsed:0} s");
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.NotEqual(RoundOutcome.TimeUp, match.Outcome);
    }

    [Fact]
    public void A_room_contested_too_long_has_your_bots_in_it_go_for_theirs()
    {
        const ulong seed = 2;
        BotArena arena = BotArena.Create("normal", seed);
        HoldRoom room = ObjectiveFocus.For(ObjectiveKind.Hold, arena.Level.Objectives, TestData.Config.Rules.Objectives, seed)!.Room!;
        // One of theirs in the room, quiet, and you (a bot) at the other side of it.
        CoverPoint[] inside = arena.Squad.Cover.Points.Where(p => room.Contains(p.Position + new Vector3(0f, 0.1f, 0f))).ToArray();
        CoverPoint a = inside.First();
        CoverPoint b = inside.MaxBy(p => Vector3.Distance(p.Position, a.Position))!;
        BotBrain them = arena.AddBotAt(new OpponentSpawn { Id = "theirs", Position = a.Position, Yaw = 0f, Roles = new[] { "sentry" } }, team: 1);
        them.Passive = true;
        arena.PlaceHero(b.Position, b.Position + new Vector3(0f, 0f, -1f));
        BotBrain you = arena.HeroBot("hunter");
        arena.Start(objective: ObjectiveKind.Hold);
        ObjectiveState objective = arena.Sim.Match!.Objective!;
        Assert.Equal(room.Name, objective.Room!.Name);
        HoldRules rules = TestData.Config.Rules.Objectives.Hold;
        bool told = false, went = false;
        arena.Run((int)((rules.StalemateAfter + rules.AlarmInterval * 2f + 2f) * Second), () =>
        {
            told |= you.Senses.For(them.Self.Id) is { FromContact: true };
            went |= you.Mode is BotMode.Push or BotMode.Engage;
            return arena.Sim.Match.Phase == MatchPhase.Ended;
        });
        _out.WriteLine($"room {room.Name}: contested for {objective.ContestedFor:0.0} s; told={told}, went={went}, you {you.Label}, held {objective.Held:0} s");
        Assert.True(told, "you were never told where they were");
        Assert.True(went, "you stood your ground in a contested room");
    }

    /// <summary>A 3 v 3 Teams round with bots in every slot, yours included, for the objective, from the seed's starts.</summary>
    private static BotArena TeamRound(ObjectiveKind kind, string levelId, ulong seed, float timeLimit)
    {
        BotArena arena = BotArena.Create("normal", seed, levelId);
        GameMode mode = TestData.Config.Rules.FindMode("teams")!;
        ObjectiveFocus? focus = ObjectiveFocus.For(kind, arena.Level.Objectives, TestData.Config.Rules.Objectives, seed);
        SpawnPlan plan = SpawnPlanner.Plan(arena.Level, arena.Squad.Cover, arena.Sim.Collision, TestData.Config.Rules.Spawning, TestData.Data.Bots,
            RoundShape.Of(mode, 3, focus), TestData.Config.Movement.StandEyeHeight, seed);
        arena.PlaceHero(plan.You.Position, plan.You.Position + ViewAngles.FlatForward(plan.You.Yaw));
        arena.HeroBot().RestlessAfter = mode.RestlessAfter;
        foreach (OpponentSpawn mate in plan.Teammates)
        {
            arena.AddBotAt(mate, team: 0).RestlessAfter = mode.RestlessAfter;
        }

        foreach (OpponentSpawn o in plan.Opponents)
        {
            arena.AddBotAt(o, team: 1).RestlessAfter = mode.RestlessAfter;
        }

        arena.Start(timeLimit: timeLimit, mode: mode.Kind, objective: kind);
        return arena;
    }
}
