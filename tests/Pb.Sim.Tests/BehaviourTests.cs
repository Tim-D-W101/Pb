using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Players;
using Xunit;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>The Phase 3 behaviours (M3.3): vantage, the Marksman, the Flanker, and bots passing on what they see.</summary>
[Collection(BotArenaCollection.Name)]
public class BehaviourTests
{
    private const int Second = 120;
    private readonly ITestOutputHelper _out;

    public BehaviourTests(ITestOutputHelper output)
    {
        _out = output;
    }

    [Fact]
    public void Vantage_ranks_long_high_views_above_boxed_in_corners()
    {
        BotArena arena = BotArena.Create("normal");
        CoverSet cover = arena.Squad.Cover;
        VantageSet vantage = arena.Squad.Vantage;
        LevelLayout level = arena.Level;
        float[] scores = Enumerable.Range(0, cover.Points.Count).Where(i => cover.Points[i].CanShoot).Select(i => vantage[i]).OrderBy(v => v).ToArray();
        float median = scores[scores.Length / 2];
        int best = Enumerable.Range(0, cover.Points.Count).MaxBy(i => vantage[i]);
        _out.WriteLine($"{scores.Length} points; median {median:0.00}, best {vantage[best]:0.00} at {cover.Points[best].Position} in {level.AreaAt(cover.Points[best].Position)?.Name}");
        Assert.All(scores, s => Assert.InRange(s, 0f, 1.6f));

        Assert.True(vantage[best] > 0.6f, "nowhere has much of a view");

        // Height counts: on average the mezzanine's points beat the warehouse floor's.
        float Mean(Func<CoverPoint, bool> where) => Enumerable.Range(0, cover.Points.Count)
            .Where(i => cover.Points[i].CanShoot && where(cover.Points[i])).Select(i => vantage[i]).Average();
        Aabb warehouse = level.Areas.First(a => a.Name == "warehouse floor").Box;
        float up = Mean(p => warehouse.Contains(p.Position with { Y = 1f }) && p.Position.Y > 3.5f);
        float down = Mean(p => warehouse.Contains(p.Position with { Y = 1f }) && p.Position.Y < 0.5f);
        _out.WriteLine($"warehouse mezzanine {up:0.00}, floor {down:0.00}");
        Assert.True(up > down);
    }

    [Fact]
    public void A_marksman_takes_a_vantage_near_its_start_and_watches_from_it()
    {
        BotArena arena = BotArena.Create("normal");
        BotBrain bot = arena.AddBot("office_up_south", "marksman");
        arena.Start();
        arena.PlaceHero(new Vector3(60f, 0f, 45f), new Vector3(60f, 0f, 40f)); // far out of the way
        arena.Run(20 * Second);

        CoverSet cover = arena.Squad.Cover;
        int at = bot.CoverIndex;
        Assert.True(at >= 0, "the marksman holds no spot");
        float score = arena.Squad.Vantage[at];
        var reachable = new List<int>();
        cover.Near(bot.Home, bot.Archetype.OverwatchReach, reachable);
        float[] near = reachable.Where(i => cover.Points[i].CanShoot).Select(i => arena.Squad.Vantage[i]).OrderBy(v => v).ToArray();
        float quartile = near[(int)(near.Length * 0.75f)];
        _out.WriteLine($"holds point {at} (vantage {score:0.00}; 3rd quartile within reach {quartile:0.00}) at {cover.Points[at].Position}; {bot.Mode}");
        Assert.Equal(BotMode.Idle, bot.Mode);
        Assert.True(score >= quartile, "it settled for a poor view");
        Assert.True(Vector3.Distance(bot.Self.Position, cover.Points[at].Position) < 1.5f, "it isn't at its spot");
    }

    [Fact]
    public void A_marksman_hits_a_still_target_at_long_range_with_careful_shots()
    {
        BotArena arena = BotArena.Create("hard");
        BotBrain bot = arena.AddBot("rear_alley", "marksman");
        arena.Start();
        arena.PlaceHero(new Vector3(60f, 0f, 45f), new Vector3(60f, 0f, 40f));
        arena.Run(15 * Second); // settle at the vantage

        // You step out about 38 m in front of it and stand still.
        Vector3 spot = new[] { 42f, 38f, 34f }.Select(d => TrySpot(arena, bot, d)).First(s => s is not null)!.Value;
        arena.PlaceHero(spot, bot.Self.Position);
        float range = Vector3.Distance(spot, bot.Self.Position);
        int before = arena.ShotsBy(bot.Self.Id);
        arena.Run(25 * Second, () => !arena.Hero.Alive);
        int shots = arena.ShotsBy(bot.Self.Id) - before;
        _out.WriteLine($"range {range:0.0} m: out={!arena.Hero.Alive} after {shots} shots");
        Assert.False(arena.Hero.Alive, $"a hard marksman didn't hit a still target at {range:0} m");
        Assert.InRange(range, 25f, 50f);
    }

    [Fact]
    public void A_marksman_moves_on_once_balls_land_round_it()
    {
        BotArena arena = BotArena.Create("normal");
        BotBrain bot = arena.AddBot("office_up_south", "marksman");
        arena.Start();
        arena.PlaceHero(new Vector3(60f, 0f, 45f), new Vector3(60f, 0f, 40f));
        arena.Run(15 * Second);

        Vector3 spot = new[] { 30f, 26f, 22f, 18f }.Select(d => TrySpot(arena, bot, d)).First(s => s is not null)!.Value;
        arena.PlaceHero(spot, bot.Self.Position);
        // You keep shooting just past it.
        arena.HeroScript = arena.ShootAt(bot.Self, interval: 20, startTick: arena.Sim.Tick + 30, miss: 1.4f);
        var spots = new List<Vector3>();
        bool moving = false;
        arena.Run(25 * Second, () =>
        {
            if (bot.CoverIndex >= 0 && (spots.Count == 0 || spots[^1] != arena.Squad.Cover.Points[bot.CoverIndex].Position))
            {
                spots.Add(arena.Squad.Cover.Points[bot.CoverIndex].Position);
            }

            moving |= bot.Callout == CalloutKind.Moving;
            return !arena.Hero.Alive || !bot.Self.Alive;
        });
        float furthest = spots.Count < 2 ? 0f : spots.Skip(1).Max(s => Vector3.Distance(s with { Y = 0f }, spots[0] with { Y = 0f }));
        _out.WriteLine($"spots {string.Join(" → ", spots.Select(s => $"({s.X:0},{s.Z:0})"))}; called moving={moving}");
        Assert.True(moving, "it never called that it was moving");
        Assert.True(furthest >= 7.5f, $"it only shuffled {furthest:0.0} m");
    }

    [Fact]
    public void A_contact_shout_reaches_teammates_within_earshot_and_not_beyond()
    {
        BotArena arena = BotArena.Create("normal");
        BotBrain caller = arena.AddBot("yard_east", "sentry");
        Vector3 spot = arena.SpotInFront(caller, 18f);
        // A teammate a few metres behind it, facing away from you, and one about 70 m off.
        BotBrain mate = arena.AddBotAt(Spawn("mate", SpotBehind(arena, caller, 9f), awayFrom: spot), team: 1, "sentry");
        BotBrain far = arena.AddBotAt(Spawn("far", FarFrom(arena, caller.Self.Position, 70f), awayFrom: spot), team: 1, "sentry");
        arena.Start();
        // You stand in the caller's sight.
        arena.PlaceHero(spot, caller.Self.Position);
        bool mateHeard = false;
        arena.Run(6 * Second, () =>
        {
            mateHeard |= mate.Senses.For(0) is { HasLead: true, FromContact: true };
            return mateHeard;
        });
        Awareness? farOff = far.Senses.For(0);
        _out.WriteLine($"caller {caller.Mode} at {caller.Self.Position}; near teammate at {mate.Self.Position}: lead from the call={mateHeard}; " +
                       $"far teammate {Vector3.Distance(far.Self.Position, caller.Self.Position):0} m off: lead={farOff?.HasLead}, from the call={farOff?.FromContact}");
        Assert.Equal(BotMode.Engage, caller.Mode);
        Assert.True(mateHeard, "the teammate within earshot never got the call");
        Assert.False(farOff?.FromContact ?? false, "a teammate 70 m off heard the call");
    }

    [Fact]
    public void A_call_stays_worth_acting_on_after_hearing_the_enemy_and_then_goes_stale()
    {
        BotArena arena = BotArena.Create("normal");
        BotBrain caller = arena.AddBot("yard_east", "sentry");
        Vector3 spot = arena.SpotInFront(caller, 18f);
        BotBrain mate = arena.AddBotAt(Spawn("mate", SpotBehind(arena, caller, 9f), awayFrom: spot), team: 1, "sentry");
        arena.Start();
        arena.PlaceHero(spot, caller.Self.Position);
        arena.Run(6 * Second, () => mate.Senses.For(0) is { FromContact: true });
        Assert.True(mate.Senses.For(0) is { FromContact: true }, "the teammate never got the call");

        // The caller goes quiet; you fire a shot the teammate hears. The call still counts ...
        caller.Passive = true;
        arena.Run(Second / 4);
        arena.HeroScript = arena.ShootAt(caller.Self, interval: 1000, startTick: arena.Sim.Tick + 1, miss: 3f);
        arena.Run(Second / 4);
        Awareness heard = mate.Senses.For(0)!;
        _out.WriteLine($"after the shot: lead {heard.SinceLead:0.00} s ago (seen={heard.LastKnownSeen}), call {heard.SinceContact:0.00} s ago, from the call={heard.FromContact}");
        Assert.True(heard.SinceLead < heard.SinceContact - 0.1f, "the teammate didn't hear the shot");
        Assert.True(heard.FromContact, "hearing you wiped out the teammate's call");

        // ... until it's older than the contact memory.
        arena.HeroScript = null;
        arena.Run((int)((TestData.Data.Bots.Brain.ContactMemory + 0.5f) * Second));
        Assert.False(mate.Senses.For(0)!.FromContact, "the call never went stale");
    }

    [Fact]
    public void A_flanker_goes_round_to_the_side_out_of_sight_on_a_teammates_call()
    {
        BotArena arena = BotArena.Create("normal");
        BotBrain caller = arena.AddBot("yard_east", "sentry");
        Vector3 spot = arena.SpotInFront(caller, 18f);
        BotBrain flanker = arena.AddBotAt(Spawn("flank", SpotBehind(arena, caller, 10f), awayFrom: spot), team: 1, "flanker");
        arena.Start();
        arena.PlaceHero(spot, caller.Self.Position);
        Vector3 start = flanker.Self.Position;
        bool flanking = false;
        var modes = new List<string>();
        arena.Run(8 * Second, () =>
        {
            if (caller.Mode == BotMode.Engage)
            {
                caller.Passive = true; // it called it; now it keeps quiet, so the round goes on
            }

            string now = $"{flanker.Mode}{(flanker.Senses.For(0) is { HasLead: true } a ? (a.FromContact ? "(call)" : a.Spotted ? "(seen)" : "(heard)") : "")}";
            if (modes.Count == 0 || modes[^1] != now)
            {
                modes.Add(now);
            }

            flanking |= flanker.Mode == BotMode.Flank && flanker.Path.Count > 0;
            return flanking;
        });
        _out.WriteLine($"flanker: {string.Join(" → ", modes)}");
        Assert.True(flanking, $"the flanker never flanked ({flanker.Mode})");
        Vector3 goal = flanker.Path[^1];

        // Its spot is well off the line between you and the caller ...
        Vector3 line = Vector3.Normalize((caller.Self.Position - spot) with { Y = 0f });
        Vector3 side = Vector3.Normalize((goal - spot) with { Y = 0f });
        float angle = MathF.Acos(Math.Clamp(Vector3.Dot(line, side), -1f, 1f)) * 180f / MathF.PI;
        // ... and the way there is less in your sight than walking straight at you.
        var direct = new List<Vector3>();
        Assert.True(arena.Squad.Grid.FindPath(start, spot, direct));
        float flankSeen = PathExposure(arena, Prepend(start, flanker.Path), spot);
        float directSeen = PathExposure(arena, Prepend(start, direct), spot);
        _out.WriteLine($"flank spot {goal} at {angle:0}° off the caller's line; in your sight {flankSeen:P0} of the way vs {directSeen:P0} straight at you");
        Assert.True(angle >= 45f, $"only {angle:0}° off the line");
        Assert.True(flankSeen <= directSeen, "the flank route is more exposed than walking straight in");
    }

    [Fact]
    public void A_flanker_looks_out_from_its_spot_and_opens_up_from_the_side()
    {
        BotArena arena = BotArena.Create("normal");
        BotBrain caller = arena.AddBot("yard_east", "sentry");
        Vector3 spot = arena.SpotInFront(caller, 18f);
        BotBrain flanker = arena.AddBotAt(Spawn("flank", SpotBehind(arena, caller, 10f), awayFrom: spot), team: 1, "flanker");
        arena.Start();
        // You face away from the caller, so the flanker comes round unnoticed and holds its fire all the way.
        arena.PlaceHero(spot, spot * 2f - caller.Self.Position);
        var labels = new List<string>();
        int flankSpot = -1;
        bool lookedOut = false;
        arena.Run(30 * Second, () =>
        {
            if (caller.Mode == BotMode.Engage)
            {
                caller.Passive = true; // it called it; now it keeps quiet
            }

            if (labels.Count == 0 || labels[^1] != flanker.Label)
            {
                labels.Add(flanker.Label);
            }

            if (flanker.Mode == BotMode.Flank)
            {
                flankSpot = flanker.CoverIndex;
                lookedOut |= flanker.Phase == CoverPhase.Peeking;
            }

            return flanker.Mode is BotMode.Engage or BotMode.Search;
        });
        _out.WriteLine($"flanker: {string.Join(" → ", labels)}");
        Assert.True(flankSpot >= 0, "it never flanked");
        Assert.True(lookedOut, "it never looked out from its spot");
        Assert.Equal(BotMode.Engage, flanker.Mode);
        float off = Vector3.Distance(flanker.Self.Position with { Y = 0f }, arena.Squad.Cover.Points[flankSpot].Position with { Y = 0f });
        _out.WriteLine($"it opened up {off:0.0} m from its spot");
        Assert.True(off < CoverSet.PeekStep + 0.5f, "it left its spot before it saw you");
    }

    /// <summary>A spawn at <paramref name="at"/> facing directly away from <paramref name="awayFrom"/>.</summary>
    private static OpponentSpawn Spawn(string id, Vector3 at, Vector3 awayFrom) =>
        new() { Id = id, Position = at, Yaw = MathF.Atan2(awayFrom.X - at.X, awayFrom.Z - at.Z), Roles = new[] { "sentry" } };

    /// <summary>A standing spot about <paramref name="distance"/> behind a bot with a clear line between their heads.</summary>
    private static Vector3 SpotBehind(BotArena arena, BotBrain bot, float distance)
    {
        PlayerState me = bot.Self;
        Vector3 eye = me.Position + new Vector3(0f, 1.6f, 0f);
        foreach (float d in new[] { distance, distance * 0.8f, distance * 1.2f, distance * 0.6f })
        {
            foreach (float turn in new[] { 0f, 0.3f, -0.3f, 0.6f, -0.6f, 0.9f, -0.9f })
            {
                Vector3 at = me.Position + ViewAngles.FlatForward(me.Yaw + MathF.PI + turn) * d;
                if (arena.Squad.Grid.TrySnap(at, out Vector3 spot) && Vector3.Distance(spot with { Y = 0f }, at with { Y = 0f }) < 0.6f &&
                    MathF.Abs(spot.Y - me.Position.Y) < 0.3f &&
                    !arena.Sim.Collision.SweepSphere(spot + new Vector3(0f, 1.6f, 0f), eye, 0f, out SweepHit _))
                {
                    return spot;
                }
            }
        }

        throw new InvalidOperationException($"nowhere clear {distance} m behind {me.Name}");
    }

    /// <summary>A walkable spot at least <paramref name="distance"/> (flat) from <paramref name="origin"/>.</summary>
    private static Vector3 FarFrom(BotArena arena, Vector3 origin, float distance)
    {
        for (int k = 0; k < 16; k++)
        {
            float a = k * MathF.Tau / 16f;
            Vector3 at = origin + new Vector3(MathF.Sin(a), 0f, MathF.Cos(a)) * (distance + 2f);
            if (arena.Squad.Grid.TrySnap(at, out Vector3 spot) && Vector3.Distance(spot with { Y = 0f }, origin with { Y = 0f }) >= distance)
            {
                return spot;
            }
        }

        throw new InvalidOperationException($"nowhere {distance} m from {origin}");
    }

    private static List<Vector3> Prepend(Vector3 first, IReadOnlyList<Vector3> rest)
    {
        var all = new List<Vector3> { first };
        all.AddRange(rest);
        return all;
    }

    /// <summary>Share of a path, sampled every half metre, that someone standing at <paramref name="watcher"/> could see (chest height).</summary>
    private static float PathExposure(BotArena arena, IReadOnlyList<Vector3> path, Vector3 watcher)
    {
        Vector3 eye = watcher + new Vector3(0f, 1.62f, 0f);
        int seen = 0, samples = 0;
        for (int i = 1; i < path.Count; i++)
        {
            float length = Vector3.Distance(path[i - 1], path[i]);
            for (float d = 0f; d < length; d += 0.5f)
            {
                Vector3 at = Vector3.Lerp(path[i - 1], path[i], d / MathF.Max(length, 1e-3f)) + new Vector3(0f, 1.2f, 0f);
                samples++;
                if (!arena.Sim.Collision.SweepSphere(eye, at, 0f, out SweepHit _, includeDynamic: false))
                {
                    seen++;
                }
            }
        }

        return samples == 0 ? 0f : seen / (float)samples;
    }

    /// <summary>
    /// Ground about <paramref name="distance"/> (flat) from a bot, as near the way it faces as can be had, where it could see
    /// your chest from its eyes (on any floor: a marksman upstairs looks down on the yard); null if there's none.
    /// </summary>
    private static Vector3? TrySpot(BotArena arena, BotBrain bot, float distance)
    {
        PlayerState me = bot.Self;
        Vector3 eye = me.EyePosition;
        foreach (float d in new[] { distance, distance * 0.9f, distance * 1.1f })
        {
            for (int k = 0; k <= 16; k++)
            {
                float turn = (k + 1) / 2 * 0.1f * (k % 2 == 0 ? 1f : -1f);
                Vector3 at = me.Position + ViewAngles.FlatForward(me.Yaw + turn) * d;
                foreach (float floor in new[] { 0f, me.Position.Y })
                {
                    if (arena.Squad.Grid.TrySnap(at with { Y = floor }, out Vector3 spot) && Vector3.Distance(spot with { Y = 0f }, at with { Y = 0f }) < 0.6f &&
                        !arena.Sim.Collision.SweepSphere(eye, spot + new Vector3(0f, 1.2f, 0f), 0f, out SweepHit _))
                    {
                        return spot;
                    }
                }
            }
        }

        return null;
    }
}
