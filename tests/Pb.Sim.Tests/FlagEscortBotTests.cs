using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>Bots playing capture the flag and VIP escort (M5.3), in headless rounds on Oxbarrow Works.</summary>
[Collection(BotArenaCollection.Name)]
public class FlagEscortBotTests
{
    private const int Second = 120;
    private readonly ITestOutputHelper _out;

    public FlagEscortBotTests(ITestOutputHelper output)
    {
        _out = output;
    }

    /// <summary>
    /// Your side (a bot in your slot and <paramref name="mates"/> more) against two of theirs far off and quiet, so the
    /// round turns on the objective alone.
    /// </summary>
    private static (BotArena Arena, BotBrain You) Unopposed(ObjectiveKind kind, params string[] mates)
    {
        BotArena arena = BotArena.Create("normal");
        foreach (string spawn in mates)
        {
            arena.AddBotAt(arena.Level.OpponentSpawns.First(s => s.Id == spawn), team: 0);
        }

        foreach (string spawn in new[] { "east_scrap", "guardhouse" })
        {
            arena.AddBot(spawn).Passive = true;
        }

        BotBrain you = arena.HeroBot("hunter");
        arena.Start(objective: kind, mode: MatchModeKind.Teams);
        return (arena, you);
    }

    [Fact]
    public void Your_side_takes_their_flag_and_carries_it_home()
    {
        (BotArena arena, _) = Unopposed(ObjectiveKind.Flag, "yard_east", "water_tower");
        ObjectiveState objective = arena.Sim.Match!.Objective!;
        FlagState theirs = objective.TheirFlag(0)!;
        int takenAt = -1;
        arena.Run(240 * Second, () =>
        {
            if (takenAt < 0 && theirs.Carrier >= 0)
            {
                takenAt = arena.Sim.Tick;
            }

            return arena.Sim.Match.Phase == MatchPhase.Ended;
        });
        foreach (BotBrain bot in arena.Bots)
        {
            _out.WriteLine($"{bot.Self.Name} team {bot.Self.Team} at {bot.Self.Position}: {bot.Label}, attacker {bot.FlagAttacker}, post {bot.DutyPost}");
        }

        _out.WriteLine($"their flag home at {theirs.Home}, ours at {objective.FlagOf(0)!.Home}; taken after {takenAt / (float)Second:0} s; " +
                       $"{arena.Sim.Match.Outcome} after {arena.Sim.Match.Elapsed:0} s");
        Assert.True(takenAt >= 0, "nobody took their flag");
        Assert.Equal(RoundOutcome.Captured, arena.Sim.Match.Outcome);
    }

    [Fact]
    public void When_your_flag_is_taken_your_side_goes_after_the_carrier()
    {
        // You (no bot) run off with theirs; their side's bots are told where you are.
        BotArena arena = BotArena.Create("normal");
        BotBrain[] theirs = new[] { "yard_east", "water_tower", "office_corridor", "warehouse_bays" }.Select(s => arena.AddBot(s)).ToArray();
        arena.Start(objective: ObjectiveKind.Flag, mode: MatchModeKind.Teams);
        ObjectiveState objective = arena.Sim.Match!.Objective!;
        FlagState flag = objective.TheirFlag(0)!;
        arena.Run(2 * Second);
        int guards = theirs.Count(b => !b.FlagAttacker);
        Assert.InRange(guards, 1, theirs.Length - 1);
        Assert.All(theirs.Where(b => !b.FlagAttacker), b => Assert.True(b.DutyPost is { } post && Flat(post - flag.Home) <= 6f,
            $"{b.Self.Name} isn't guarding their flag"));

        arena.Hero.Position = flag.Home;
        arena.Run(Second / 2);
        Assert.Equal(arena.Hero.Id, flag.Carrier);
        int told = theirs.Count(b => b.Senses.For(arena.Hero.Id) is { HasLead: true });
        _out.WriteLine($"{guards} guards; {told} of {theirs.Length} told where the carrier is");
        Assert.True(told >= Math.Min(TestData.Config.Rules.Objectives.Flag.Chasers, theirs.Length), "the carrier's whereabouts weren't passed on");
    }

    [Fact]
    public void Your_side_brings_the_vip_out()
    {
        (BotArena arena, _) = Unopposed(ObjectiveKind.Escort, "yard_east", "water_tower");
        ObjectiveState objective = arena.Sim.Match!.Objective!;
        PlayerState vip = arena.Sim.FindPlayer(objective.Vip)!;
        _out.WriteLine($"VIP {vip.Name} from {objective.VipStart}; ways out that count: " +
                       string.Join(", ", objective.CountingExits.Select(i => arena.Level.Objectives.Exits[i].Name)));
        Assert.NotEqual(arena.Hero.Id, objective.Vip);
        arena.Run(240 * Second, () => arena.Sim.Match.Phase == MatchPhase.Ended);
        _out.WriteLine($"{arena.Sim.Match.Outcome} after {arena.Sim.Match.Elapsed:0} s through " +
                       (objective.ExitUsed >= 0 ? arena.Level.Objectives.Exits[objective.ExitUsed].Name : "nowhere"));
        Assert.Equal(RoundOutcome.Escorted, arena.Sim.Match.Outcome);
    }

    [Fact]
    public void Defenders_guard_the_vips_ways_out_from_the_start()
    {
        BotArena arena = BotArena.Create("normal");
        arena.AddBotAt(arena.Level.OpponentSpawns.First(s => s.Id == "yard_east"), team: 0);
        BotBrain[] theirs = new[] { "water_tower", "office_corridor", "warehouse_bays", "pump_house" }.Select(s => arena.AddBot(s)).ToArray();
        arena.Start(objective: ObjectiveKind.Escort, mode: MatchModeKind.Teams);
        ObjectiveState objective = arena.Sim.Match!.Objective!;
        arena.Run(Second / 2);
        float reach = TestData.Data.Bots.Brain.ExitGuardInset + TestData.Data.Bots.Brain.GuardSpacing * 2f;
        int guarding = theirs.Count(b => b.DutyPost is { } post &&
                                         objective.CountingExits.Any(i => Flat(post - arena.Level.Objectives.Exits[i].Position) <= reach));
        _out.WriteLine($"{guarding} of {theirs.Length} sent to the ways out that count");
        Assert.InRange(guarding, 1, theirs.Length);
        Assert.Equal((int)MathF.Round(theirs.Length * TestData.Config.Rules.Objectives.Escort.ExitGuardShare), guarding);
    }

    private static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);
}
