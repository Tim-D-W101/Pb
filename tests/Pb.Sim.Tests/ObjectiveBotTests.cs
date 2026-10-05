using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>Bots and the objectives (M3.4), in headless rounds on Oxbarrow Works.</summary>
[Collection(BotArenaCollection.Name)]
public class ObjectiveBotTests
{
    private const int Second = 120;
    private readonly ITestOutputHelper _out;

    public ObjectiveBotTests(ITestOutputHelper output)
    {
        _out = output;
    }

    /// <summary>A bot in your slot, and two of theirs far off and quiet so the round turns on the objective alone.</summary>
    private static (BotArena Arena, BotBrain You) Unopposed(ObjectiveKind kind)
    {
        BotArena arena = BotArena.Create("normal");
        foreach (string spawn in new[] { "east_scrap", "guardhouse" })
        {
            arena.AddBot(spawn).Passive = true;
        }

        BotBrain you = arena.HeroBot("hunter");
        arena.Start(objective: kind);
        return (arena, you);
    }

    [Fact]
    public void A_bot_on_your_side_fetches_the_case_and_carries_it_out()
    {
        (BotArena arena, _) = Unopposed(ObjectiveKind.Retrieve);
        ObjectiveState objective = arena.Sim.Match!.Objective!;
        int takenAt = -1;
        arena.Run(240 * Second, () =>
        {
            if (takenAt < 0 && objective.Carrier == arena.Hero.Id)
            {
                takenAt = arena.Sim.Tick;
            }

            return arena.Sim.Match.Phase == MatchPhase.Ended;
        });
        _out.WriteLine($"case at {objective.Spot.Position} ({objective.Spot.Area}); picked up after {takenAt / (float)Second:0} s; " +
                       $"{arena.Sim.Match.Outcome} after {arena.Sim.Match.Elapsed:0} s through {(objective.ExitUsed >= 0 ? objective.Level.Exits[objective.ExitUsed].Name : "nowhere")}");
        Assert.True(takenAt >= 0, "it never picked the case up");
        Assert.Equal(RoundOutcome.Extracted, arena.Sim.Match.Outcome);
    }

    [Fact]
    public void A_bot_on_your_side_takes_the_room_and_holds_it()
    {
        (BotArena arena, _) = Unopposed(ObjectiveKind.Hold);
        ObjectiveState objective = arena.Sim.Match!.Objective!;
        arena.Run(240 * Second, () => arena.Sim.Match.Phase == MatchPhase.Ended);
        _out.WriteLine($"room {objective.Room!.Name}: held {objective.Held:0} s; {arena.Sim.Match.Outcome} after {arena.Sim.Match.Elapsed:0} s");
        Assert.Equal(RoundOutcome.Held, arena.Sim.Match.Outcome);
    }

    [Fact]
    public void Once_the_case_is_taken_its_holders_go_after_it_and_cut_off_the_ways_out()
    {
        BotArena arena = BotArena.Create("normal");
        BotBrain[] holders = new[] { "yard_east", "water_tower", "office_corridor", "warehouse_bays", "pump_house" }.Select(s => arena.AddBot(s)).ToArray();
        arena.Start(objective: ObjectiveKind.Retrieve);
        ObjectiveState objective = arena.Sim.Match!.Objective!;
        arena.PlaceHero(new Vector3(60f, 0f, 45f), new Vector3(60f, 0f, 40f)); // far off while they settle
        arena.Run(2 * Second);

        // You pick it up.
        arena.Hero.Position = objective.CasePosition;
        arena.Run(Second / 2);
        Assert.Equal(arena.Hero.Id, objective.Carrier);

        Vector3 at = objective.CasePosition;
        ExitSpec exit = objective.Level.Exits.MinBy(e => Flat(e.Position - at));
        BotBrain[] byDistance = holders.OrderBy(b => Flat(b.Self.Position - at)).ToArray();
        int chasers = TestData.Config.Rules.Objectives.Retrieve.Chasers;
        foreach (BotBrain bot in byDistance)
        {
            Awareness? a = bot.Senses.For(arena.Hero.Id);
            _out.WriteLine($"{bot.Self.Name} {Flat(bot.Self.Position - at):0} m off: {bot.Mode}, lead={a?.HasLead} call={a?.FromContact}, " +
                           $"post={(bot.DutyPost is { } p ? $"{p} ({Flat(p - exit.Position):0} m from {exit.Name})" : "none")}");
        }

        Assert.All(byDistance.Take(chasers), b => Assert.True(b.Senses.For(arena.Hero.Id) is { HasLead: true, FromContact: true },
            $"{b.Self.Name} wasn't told where the case went"));
        float reach = TestData.Data.Bots.Brain.ExitGuardInset + TestData.Data.Bots.Brain.GuardSpacing * 3f;
        Assert.All(byDistance.Skip(chasers), b => Assert.True(b.DutyPost is { } post && Flat(post - exit.Position) <= reach,
            $"{b.Self.Name} wasn't sent to {exit.Name}"));

        // A few seconds on, the guards are on their way to it.
        float[] before = byDistance.Skip(chasers).Select(b => Flat(b.Self.Position - exit.Position)).ToArray();
        arena.Hero.Position = at;
        arena.Run(5 * Second);
        float[] after = byDistance.Skip(chasers).Select(b => Flat(b.Self.Position - exit.Position)).ToArray();
        Assert.True(after.Zip(before).Count(p => p.First < p.Second - 2f) >= after.Length - 1, "the guards aren't making for the way out");
    }

    [Fact]
    public void Defenders_come_for_the_room_once_you_are_in_it()
    {
        BotArena arena = BotArena.Create("normal");
        BotBrain[] defenders = new[] { "yard_east", "water_tower", "office_corridor", "warehouse_bays" }.Select(s => arena.AddBot(s)).ToArray();
        arena.Start(objective: ObjectiveKind.Hold);
        ObjectiveState objective = arena.Sim.Match!.Objective!;
        arena.PlaceHero(new Vector3(60f, 0f, 45f), new Vector3(60f, 0f, 40f));
        arena.Run(2 * Second);
        Assert.All(defenders, d => Assert.False(d.Senses.For(arena.Hero.Id) is { HasLead: true }));

        arena.Hero.Position = objective.Room!.Centre;
        arena.Run(Second / 2);
        _out.WriteLine($"you in {objective.Room.Name} ({objective.Status}): " +
                       string.Join("; ", defenders.Select(d => $"{d.Self.Name} {d.Mode} call={d.Senses.For(arena.Hero.Id)?.FromContact}")));
        Assert.Equal(HoldStatus.Ours, objective.Status);
        Assert.All(defenders, d => Assert.True(d.Senses.For(arena.Hero.Id) is { HasLead: true, FromContact: true }, $"{d.Self.Name} never heard"));
    }

    private static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);
}
