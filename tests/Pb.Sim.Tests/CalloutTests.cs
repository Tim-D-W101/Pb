using System.Numerics;
using System.Text.RegularExpressions;
using Pb.Sim.AI;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;
using Xunit;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>
/// The bots' shouts after the play-test (they were overdone): occasional, never piling on one another, nobody echoing
/// a teammate; and the calls that tell teammates where you are reach every teammate in earshot and nobody else.
/// </summary>
[Collection(BotArenaCollection.Name)]
public class CalloutTests
{
    private const int Second = 120;

    /// <summary>The calls that always go aloud ("Hit!" and the case's).</summary>
    private static readonly HashSet<CalloutKind> Always = new() { CalloutKind.Hit, CalloutKind.CaseTaken, CalloutKind.CaseDown };

    private readonly ITestOutputHelper _out;

    public CalloutTests(ITestOutputHelper output)
    {
        _out = output;
    }

    [Fact]
    public void Shouts_in_a_full_team_round_keep_every_gap_and_stay_occasional()
    {
        BrainParams b = TestData.Data.Bots.Brain;
        List<Shout> shouts = TeamRound(TestData.Data.Bots, 120);
        float minutes = 120f / 60f;
        _out.WriteLine($"{shouts.Count} shouts in 2 minutes of a 5 v 5: " + string.Join(", ", shouts.Select(s => $"{s.At:0.0}s {s.Bot}/{s.Side} {s.Kind}")));
        Assert.NotEmpty(shouts);

        for (int i = 1; i < shouts.Count; i++)
        {
            Assert.True(shouts[i].At - shouts[i - 1].At >= b.SquadCalloutGap - 1e-3f,
                $"{shouts[i - 1]} and {shouts[i]} closer than the squad's gap of {b.SquadCalloutGap} s");
        }

        foreach (Shout s in shouts.Where(s => !Always.Contains(s.Kind)))
        {
            Shout? sideBefore = shouts.LastOrDefault(o => o.Side == s.Side && o.At < s.At);
            Assert.True(sideBefore is null || s.At - sideBefore.At >= b.SideCalloutGap - 1e-3f, $"{s} within the side's gap of {sideBefore}");
            Shout? sameOnSide = shouts.LastOrDefault(o => o.Side == s.Side && o.Kind == s.Kind && o.At < s.At);
            Assert.True(sameOnSide is null || s.At - sameOnSide.At >= b.SideCalloutRepeat - 1e-3f, $"{s} echoes {sameOnSide}");
            Shout? sameBot = shouts.LastOrDefault(o => o.Bot == s.Bot && o.Kind == s.Kind && o.At < s.At);
            Assert.True(sameBot is null || s.At - sameBot.At >= b.CalloutRepeat - 1e-3f, $"{s} repeats {sameBot}");
        }

        // Occasional: two sides can't manage more than one shout each every side's gap, plus the calls that always go.
        float everyday = shouts.Count(s => !Always.Contains(s.Kind)) / minutes;
        Assert.True(everyday <= 2 * 60f / b.SideCalloutGap, $"{everyday:0} everyday shouts a minute");
    }

    [Fact]
    public void The_same_round_with_the_old_shouting_has_well_over_twice_the_shouts()
    {
        // The settings before the play-test: one shout per bot every 4 s, every one aloud, no limits by side or squad.
        var old = new EditedDataSource(TestData.Source).Edit("bots/brain.jsonc", text =>
        {
            text = Regex.Replace(text, "\"calloutCooldown_s\": [0-9.]+", "\"calloutCooldown_s\": 4");
            text = Regex.Replace(text, "\"calloutRepeat_s\": [0-9.]+", "\"calloutRepeat_s\": 0");
            text = Regex.Replace(text, "\"calloutChance\": [0-9.]+", "\"calloutChance\": 1");
            text = Regex.Replace(text, "\"sideCalloutGap_s\": [0-9.]+", "\"sideCalloutGap_s\": 0");
            text = Regex.Replace(text, "\"sideCalloutRepeat_s\": [0-9.]+", "\"sideCalloutRepeat_s\": 0");
            return Regex.Replace(text, "\"squadCalloutGap_s\": [0-9.]+", "\"squadCalloutGap_s\": 0");
        });
        BotConfig oldBots = GameData.Load(old).Bots;
        int before = TeamRound(oldBots, 90).Count;
        int now = TeamRound(TestData.Data.Bots, 90).Count;
        _out.WriteLine($"90 s of a 5 v 5: {before} shouts with the old settings, {now} now");
        Assert.True(now * 2 < before, $"{now} shouts now against {before} before");
    }

    [Fact]
    public void Every_teammate_in_earshot_hears_a_call_and_the_other_side_never_does()
    {
        BotArena arena = BotArena.Create("normal");
        BotBrain caller = arena.AddBot("yard_east", "sentry");
        Vector3 spot = arena.SpotInFront(caller, 18f);
        Vector3 behind = caller.Self.Position - ViewAngles.FlatForward(caller.Self.Yaw) * 6f;
        Vector3 aside = caller.Self.Position + ViewAngles.Right(caller.Self.Yaw) * 6f;
        BotBrain mateA = arena.AddBotAt(Facing("mate_a", Snap(arena, behind), awayFrom: spot), team: 1, "sentry");
        BotBrain mateB = arena.AddBotAt(Facing("mate_b", Snap(arena, aside), awayFrom: spot), team: 1, "sentry");
        // Someone on another side standing by them, who notices nothing of its own (so it starts no fight).
        BotBrain stranger = arena.AddBotAt(Facing("stranger", Snap(arena, behind + ViewAngles.Right(caller.Self.Yaw) * 2f), awayFrom: spot),
            team: 2, "sentry");
        stranger.Passive = true;
        arena.Start(mode: MatchModeKind.FreeForAll);
        arena.PlaceHero(spot, caller.Self.Position);
        arena.Run(6 * Second, () => mateA.Senses.For(0) is { FromContact: true } && mateB.Senses.For(0) is { FromContact: true });

        _out.WriteLine($"caller {caller.Mode}; shared {arena.Squad.ContactsShared}, heard {arena.Squad.ContactsHeard} " +
                       $"(A {mateA.Senses.ContactsHeard}, B {mateB.Senses.ContactsHeard}, the other side {stranger.Senses.ContactsHeard})");
        Assert.Equal(BotMode.Engage, caller.Mode);
        Assert.True(arena.Squad.ContactsShared >= 1, "the caller told nobody");
        Assert.True(mateA.Senses.ContactsHeard >= 1 && mateA.Senses.For(0) is { HasLead: true }, "the teammate behind never heard the call");
        Assert.True(mateB.Senses.ContactsHeard >= 1 && mateB.Senses.For(0) is { HasLead: true }, "the teammate beside never heard the call");
        Assert.Equal(0, stranger.Senses.ContactsHeard);
        Assert.False(stranger.Senses.For(0)?.FromContact ?? false, "the other side took the call as its own");
        Assert.Equal(arena.Squad.ContactsHeard, mateA.Senses.ContactsHeard + mateB.Senses.ContactsHeard + caller.Senses.ContactsHeard);

        // The game marks a teammate's contact on your HUD from the squad's latest contacts, said aloud or not, if the call
        // carries to you as it does to the bots: the caller's first is kept, and carries to its teammate behind it.
        Assert.True(arena.Squad.RecentContact(0, out BotSquad.Contact first), "the first contact isn't kept");
        Assert.Equal(caller.Self.Id, first.From);
        Assert.Equal(0, first.TargetId);
        Assert.True(arena.Squad.CallCarries(first.FromEye, mateA.Self.EyePosition), "the call doesn't carry to the teammate behind");
        Assert.False(arena.Squad.RecentContact(arena.Squad.ContactsShared, out _), "a contact not yet made");
    }

    [Fact]
    public void The_squad_keeps_its_latest_contacts_for_the_hud_and_a_call_carries_no_further_than_the_bots_hear()
    {
        TeamRound(TestData.Data.Bots, 120, out BotArena arena);
        BotBrain caller = arena.Bots.First();
        int shared = arena.Squad.ContactsShared;
        _out.WriteLine($"{shared} contacts shared");
        Assert.True(shared >= 10, $"only {shared} contacts in a 5 v 5");
        var ids = arena.Bots.Select(b => b.Self.Id).ToHashSet();
        for (int n = Math.Max(0, shared - BotSquad.RecentContacts); n < shared; n++)
        {
            Assert.True(arena.Squad.RecentContact(n, out BotSquad.Contact c), $"contact {n} of {shared} isn't kept");
            Assert.Contains(c.From, ids);
            Assert.NotEqual(c.From, c.TargetId);
        }

        Assert.False(arena.Squad.RecentContact(shared, out _), "a contact not yet made");
        Assert.False(arena.Squad.RecentContact(-1, out _));
        Assert.True(shared <= BotSquad.RecentContacts || !arena.Squad.RecentContact(shared - BotSquad.RecentContacts - 1, out _),
            "an old contact is still kept");

        float range = arena.BotConfig.Brain.CalloutRange;
        Vector3 eye = caller.Self.EyePosition;
        Assert.False(arena.Squad.CallCarries(eye, eye + new Vector3(range + 1f, 0f, 0f)), "a call carries beyond the callout range");
    }

    /// <summary>A 5 v 5 played by bots alone for <paramref name="seconds"/>, and every shout made aloud in it.</summary>
    private static List<Shout> TeamRound(BotConfig bots, int seconds) => TeamRound(bots, seconds, out _);

    private static List<Shout> TeamRound(BotConfig bots, int seconds, out BotArena played)
    {
        const ulong seed = 21;
        BotArena arena = BotArena.Create("normal", seed, bots: bots);
        GameMode mode = TestData.Config.Rules.FindMode("teams")!;
        SpawnPlan plan = SpawnPlanner.Plan(arena.Level, arena.Squad.Cover, arena.Sim.Collision, TestData.Config.Rules.Spawning, bots,
            RoundShape.Of(mode, 5), TestData.Config.Movement.StandEyeHeight, seed);
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

        arena.Start(timeLimit: seconds + 30, mode: mode.Kind);
        var shouts = new List<Shout>();
        var seen = new Dictionary<BotBrain, int>();
        arena.Run(seconds * Second, () =>
        {
            foreach (BotBrain bot in arena.Bots)
            {
                if (bot.CalloutTick > seen.GetValueOrDefault(bot, -1))
                {
                    seen[bot] = bot.CalloutTick;
                    shouts.Add(new Shout(bot.CalloutTick / (float)Second, bot.Self.Name, bot.Self.Team, bot.Callout));
                }
            }

            return arena.Sim.Match!.Phase == MatchPhase.Ended;
        });
        Assert.Equal(arena.Squad.Shouts, shouts.Count);
        played = arena;
        return shouts;
    }

    private static Vector3 Snap(BotArena arena, Vector3 at) => arena.Squad.Grid.TrySnap(at, out Vector3 snapped) ? snapped : at;

    private static OpponentSpawn Facing(string id, Vector3 at, Vector3 awayFrom) =>
        new() { Id = id, Position = at, Yaw = MathF.Atan2(-(at.X - awayFrom.X), -(at.Z - awayFrom.Z)), Roles = new[] { "sentry" } };

    private sealed record Shout(float At, string Bot, byte Side, CalloutKind Kind)
    {
        public override string ToString() => $"{Bot} (side {Side}) {Kind} at {At:0.00} s";
    }
}
