using System.Numerics;
using Pb.Net.Protocol;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Tests;

namespace Pb.Net.Tests;

/// <summary>Phase 4 (M4.3): casting a round with people in it: sides, bots to fill, and everyone's start.</summary>
public class CastingTests
{
    private static readonly Lazy<(LevelLayout Level, CoverSet Cover, CollisionWorld World)> Oxbarrow = new(() =>
    {
        LevelLayout level = TestData.Data.Levels["oxbarrow_works"];
        NavGrid grid = NavGrid.Build(level, TestData.Data.Bots.Navigation);
        var world = new CollisionWorld();
        level.BuildCollision(world);
        CoverSet cover = CoverSet.Build(level, grid, TestData.Config.Movement.StandEyeHeight, TestData.Config.Movement.CrouchEyeHeight);
        return (level, cover, world);
    });

    private static readonly string[] Callsigns = { "Kestrel", "Magpie", "Heron", "Rook", "Wren", "Plover", "Shrike", "Lapwing", "Dunlin", "Merlin" };

    private static CastRound Cast(string modeId, int size, ObjectiveKind objective, int round, params Person[] people) =>
        CastWith(modeId, size, objective, round, bots: true, people);

    private static CastRound CastWith(string modeId, int size, ObjectiveKind objective, int round, bool bots, params Person[] people)
    {
        (LevelLayout level, CoverSet cover, CollisionWorld world) = Oxbarrow.Value;
        GameMode mode = TestData.Config.Rules.FindMode(modeId)!;
        ObjectiveChoice choice = TestData.Config.Rules.Objectives.Find(objective)!;
        TierDef tier = TestData.Data.Areas.Areas[0].Tiers[1];
        return RoundCasting.Cast(level, cover, world, TestData.Config, TestData.Data.Bots, mode, size, choice, tier, people, 77, round, Callsigns,
            level.Id, null, fillWithBots: bots);
    }

    [Fact]
    public void Each_person_is_cast_in_their_kit_and_the_bots_in_none()
    {
        // Phase 5 (M5.3): what each person wears goes in the roster; a bot's is dealt from the seed on every copy.
        Pb.Sim.Gear.Loadout kit = TestData.Data.Gear.Deal(9, 1, character: 2);
        CastRound cast = Cast("teams", 3, ObjectiveKind.Eliminate, 1, new Person("Ada", 2, Side: 0, Kit: kit), new Person("Bo", Side: 1));
        RosterEntry ada = cast.Setup.Roster.Single(e => e.Name == "Ada");
        Assert.True(ada.Kit!.SameAs(kit));
        Assert.Null(cast.Setup.Roster.Single(e => e.Name == "Bo").Kit);
        Assert.All(cast.Setup.Roster.Where(e => !e.Person), e => Assert.Null(e.Kit));
    }

    [Fact]
    public void Without_bots_teams_and_free_for_all_are_people_alone_and_co_op_keeps_the_squad()
    {
        CastRound teams = CastWith("teams", 3, ObjectiveKind.Eliminate, 1, bots: false, new Person("Ada", Side: 0), new Person("Bo", Side: 1), new Person("Cy", Side: 1));
        Assert.Equal(3, teams.Setup.Roster.Count);
        Assert.All(teams.Setup.Roster, e => Assert.True(e.Person));
        CastRound ffa = CastWith("ffa", 6, ObjectiveKind.Eliminate, 1, bots: false, new Person("Ada"), new Person("Bo"));
        Assert.Equal(2, ffa.Setup.Roster.Count);
        CastRound coop = CastWith("solo", 4, ObjectiveKind.Eliminate, 1, bots: false, new Person("Ada"), new Person("Bo"));
        Assert.Equal(6, coop.Setup.Roster.Count);
        Assert.Equal(4, coop.Setup.Roster.Count(e => !e.Person && e.Team == 1));
    }

    [Fact]
    public void Co_op_puts_everyone_on_one_side_together_against_the_squad()
    {
        CastRound cast = Cast("solo", 6, ObjectiveKind.Eliminate, 1, new Person("Ada"), new Person("Bo"), new Person("Cy"));
        List<RosterEntry> roster = cast.Setup.Roster;
        Assert.Equal(9, roster.Count);
        Assert.Equal(new[] { 0, 1, 2 }, cast.PersonIds);
        Assert.All(roster.Take(3), e => Assert.True(e.Person && e.Team == 0));
        Assert.All(roster.Skip(3), e => Assert.True(!e.Person && e.Team == 1));
        Assert.All(cast.BotStarts.Skip(3), s => Assert.NotNull(s));
        float together = roster.Take(3).Max(a => roster.Take(3).Max(b => Vector3.Distance(a.Position, b.Position)));
        Assert.True(together < 25f, $"people {together:0.0} m apart");
        Assert.Equal(new[] { 0, 1, 2 }, RoundWorld.MatchSetupOf(TestData.Config, cast.Setup).People);
        Assert.Equal(roster.Select(e => e.PlayerId), Enumerable.Range(0, roster.Count));
    }

    [Fact]
    public void Teams_keep_each_side_even_and_fill_it_with_bots()
    {
        CastRound cast = Cast("teams", 4, ObjectiveKind.Eliminate, 1, new Person("Ada", Side: 1), new Person("Bo"), new Person("Cy"), new Person("Di"));
        List<RosterEntry> roster = cast.Setup.Roster;
        Assert.Equal(8, roster.Count);
        Assert.Equal(4, roster.Count(e => e.Team == 0));
        Assert.Equal(4, roster.Count(e => e.Team == 1));
        Assert.Equal(1, roster[0].Team);
        Assert.Equal(2, roster.Take(4).Count(e => e.Team == 0));
        Assert.Equal(2, roster.Take(4).Count(e => e.Team == 1));
        // The two sides start apart.
        float nearest = roster.Where(e => e.Team == 0).Min(a => roster.Where(e => e.Team == 1).Min(b => Vector3.Distance(a.Position, b.Position)));
        Assert.True(nearest > 15f, $"the sides start {nearest:0.0} m apart");
    }

    [Fact]
    public void With_people_on_both_sides_the_sides_take_turns_to_attack()
    {
        CastRound one = Cast("teams", 3, ObjectiveKind.Hold, 1, new Person("Ada", Side: 0), new Person("Bo", Side: 1));
        CastRound two = Cast("teams", 3, ObjectiveKind.Hold, 2, new Person("Ada", Side: 0), new Person("Bo", Side: 1));
        Assert.NotEqual(one.Setup.Attackers, two.Setup.Attackers);
        // People only on side 1: they always attack.
        CastRound alone = Cast("teams", 3, ObjectiveKind.Hold, 2, new Person("Ada", Side: 1));
        Assert.Equal(1, alone.Setup.Attackers);
        Assert.Equal(1, alone.Setup.Roster[0].Team);
    }

    [Fact]
    public void Free_for_all_gives_everyone_a_side_of_their_own()
    {
        CastRound cast = Cast("ffa", 8, ObjectiveKind.Retrieve, 1, new Person("Ada"), new Person("Bo"), new Person("Cy"));
        List<RosterEntry> roster = cast.Setup.Roster;
        Assert.Equal(8, roster.Count);
        Assert.Equal(8, roster.Select(e => e.Team).Distinct().Count());
        Assert.Equal(ObjectiveKind.Eliminate, cast.Setup.Objective);
        Assert.Equal(3, roster.Count(e => e.Person));
    }

    [Fact]
    public void The_same_seed_casts_the_same_round_everywhere()
    {
        CastRound a = Cast("teams", 5, ObjectiveKind.Retrieve, 3, new Person("Ada"), new Person("Bo"));
        CastRound b = Cast("teams", 5, ObjectiveKind.Retrieve, 3, new Person("Ada"), new Person("Bo"));
        Assert.Equal(a.Setup.Roster, b.Setup.Roster);
        Assert.True(a.Setup.EndWhenPeopleOut);
    }

    [Theory]
    [InlineData("solo", 6, 3, 10, 6)]
    [InlineData("solo", 9, 3, 10, 7)]
    [InlineData("solo", 4, 10, 10, 1)]
    [InlineData("ffa", 4, 6, 10, 6)]
    [InlineData("ffa", 8, 3, 10, 8)]
    [InlineData("teams", 2, 5, 10, 3)]
    [InlineData("teams", 4, 3, 10, 4)]
    public void A_round_is_made_big_enough_for_everyone_who_is_in(string modeId, int size, int people, int most, int fitted)
    {
        GameMode mode = TestData.Config.Rules.FindMode(modeId)!;
        Assert.Equal(fitted, RoundCasting.FitSize(mode, size, people, most));
        if (people <= 4)
        {
            CastRound cast = Cast(modeId, fitted, ObjectiveKind.Eliminate, 1, Enumerable.Range(0, people).Select(i => new Person($"P{i}")).ToArray());
            Assert.Equal(people, cast.Setup.Roster.Count(e => e.Person));
        }
    }
}
