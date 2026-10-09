using Pb.Net.Protocol;
using Pb.Sim;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Pb.Sim.Match;

namespace Pb.Net;

/// <summary>Someone playing in a round: their name, which character they play, and the side they asked for (−1: either).</summary>
public sealed record Person(string Name, byte Look = 0, int Side = -1);

/// <summary>A round as cast: its setup (the roster in the order players are added) and each bot's start, behaviour and route.</summary>
public sealed class CastRound
{
    public required RoundSetupMessage Setup { get; init; }

    /// <summary>For each roster entry, the bot's start (its role and route), or null for a person.</summary>
    public required IReadOnlyList<OpponentSpawn?> BotStarts { get; init; }

    /// <summary>The roster entry each person plays, in the order they were given.</summary>
    public required IReadOnlyList<int> PersonIds { get; init; }
}

/// <summary>
/// Casts a round with people in it: who's on which side, bots to fill the places, and everyone's start from the round's
/// seed (<see cref="SpawnPlanner"/>). Co-op puts every person on one side against the squad; teams puts each on the
/// side they asked for, the sides kept even; free-for-all gives everyone a side of their own. The side that comes in at
/// the entries is the one attacking the objective (with people on both sides they take turns, round by round).
/// People take the roster's first places (the host is player 0), the bots follow.
/// </summary>
public static class RoundCasting
{
    public static CastRound Cast(LevelLayout level, CoverSet cover, CollisionWorld world, SimConfig config, BotConfig bots, GameMode mode, int size,
        ObjectiveChoice objective, TierDef tier, IReadOnlyList<Person> people, ulong seed, int round, IReadOnlyList<string> callsigns,
        string levelId, string? placeId, float? timeLimit = null)
    {
        if (people.Count == 0)
        {
            throw new ArgumentException("a round needs someone in it", nameof(people));
        }

        ObjectiveKind kind = mode.Kind == MatchModeKind.FreeForAll ? ObjectiveKind.Eliminate : objective.Kind;
        int[] sides = Sides(mode.Kind, people);
        if (mode.Kind == MatchModeKind.Teams && (sides.Count(s => s == 0) > size || sides.Count(s => s == 1) > size))
        {
            throw new InvalidOperationException($"{size} a side is too few for everyone who's in");
        }

        // The side that comes in at the entries: with an objective it attacks; with people on both sides, they take turns.
        bool bothSides = sides.Contains(0) && sides.Contains(1);
        byte entering = mode.Kind == MatchModeKind.Teams && bothSides && kind != ObjectiveKind.Eliminate ? (byte)(round % 2)
            : mode.Kind == MatchModeKind.Teams && !sides.Contains(0) ? (byte)1
            : (byte)0;

        ObjectiveFocus? focus = ObjectiveFocus.For(kind, level.Objectives, config.Rules.Objectives, seed);
        RoundShape shape = mode.Kind == MatchModeKind.Solo ? RoundShape.Of(mode, size, people.Count, focus) : RoundShape.Of(mode, size, focus);
        SpawnPlan plan = SpawnPlanner.Plan(level, cover, world, config.Rules.Spawning, bots, shape, config.Movement.StandEyeHeight, seed);

        // Where each person starts: the entering side's first at your start, the rest of it among the teammates; the
        // other side's people among the opponents (first, so near the objective when they defend it).
        var personStart = new SpawnPoint[people.Count];
        var usedMates = new HashSet<int>();
        var usedOpponents = new HashSet<int>();
        int nextMate = 0, nextOpponent = 0;
        bool youTaken = false;
        for (int i = 0; i < people.Count; i++)
        {
            bool sameSide = mode.Kind switch
            {
                MatchModeKind.FreeForAll => !youTaken,
                MatchModeKind.Teams => sides[i] == entering,
                _ => true,
            };
            if (sameSide && !youTaken)
            {
                personStart[i] = plan.You;
                youTaken = true;
            }
            else if (sameSide)
            {
                OpponentSpawn mate = plan.Teammates[nextMate];
                usedMates.Add(nextMate++);
                personStart[i] = new SpawnPoint(mate.Position, mate.Yaw);
            }
            else
            {
                OpponentSpawn other = plan.Opponents[nextOpponent];
                usedOpponents.Add(nextOpponent++);
                personStart[i] = new SpawnPoint(other.Position, other.Yaw);
            }
        }

        var setup = new RoundSetupMessage
        {
            Round = round, LevelId = levelId, PlaceId = placeId, ModeId = mode.Id, Size = size, Objective = kind, TierId = tier.Id, Seed = seed,
            TimeLimit = timeLimit ?? tier.TimeLimit_s, StartPods = tier.StartPods, BotPods = tier.BotPods, Pickups = tier.Pickups,
            Attackers = entering, EndWhenPeopleOut = true,
        };
        var starts = new List<OpponentSpawn?>();
        var ids = new List<int>();
        for (int i = 0; i < people.Count; i++)
        {
            byte team = mode.Kind switch
            {
                MatchModeKind.FreeForAll => (byte)i,
                MatchModeKind.Teams => (byte)sides[i],
                _ => 0,
            };
            setup.Roster.Add(new RosterEntry
            {
                PlayerId = i, Team = team, Name = people[i].Name, Person = true, Look = people[i].Look, Position = personStart[i].Position,
                Yaw = personStart[i].Yaw,
            });
            starts.Add(null);
            ids.Add(i);
        }

        // The bots: the teammates' places left, then the opponents'.
        string[] names = Deal(callsigns, seed);
        int bot = 0;
        byte otherSide = (byte)(1 - entering);
        for (int m = 0; m < plan.Teammates.Count; m++)
        {
            if (!usedMates.Contains(m))
            {
                AddBot(setup, starts, plan.Teammates[m], mode.Kind == MatchModeKind.Teams ? entering : (byte)0, names, ref bot);
            }
        }

        int nextTeam = people.Count;
        for (int o = 0; o < plan.Opponents.Count; o++)
        {
            if (usedOpponents.Contains(o))
            {
                continue;
            }

            byte team = mode.Kind switch
            {
                MatchModeKind.FreeForAll => (byte)nextTeam++,
                MatchModeKind.Teams => otherSide,
                _ => 1,
            };
            AddBot(setup, starts, plan.Opponents[o], team, names, ref bot);
        }

        return new CastRound { Setup = setup, BotStarts = starts, PersonIds = ids };
    }

    /// <summary>
    /// The round's size made to fit everyone who's in: free-for-all needs a place each, teams a side as big as the larger
    /// half, and co-op (the size is the opponents) leaves room in the round for every person, keeping one opponent at least.
    /// </summary>
    public static int FitSize(GameMode mode, int size, int people, int maxPlayers) => mode.Kind switch
    {
        MatchModeKind.FreeForAll => Math.Max(size, people),
        MatchModeKind.Teams => Math.Max(size, (people + 1) / 2),
        _ => Math.Clamp(size, 1, Math.Max(1, maxPlayers - people)),
    };

    /// <summary>
    /// Everyone's side: co-op, everyone on 0; free-for-all, each their own (the roster gives the numbers); teams, the side
    /// they asked for, and anyone who didn't mind on the smaller side.
    /// </summary>
    public static int[] Sides(MatchModeKind kind, IReadOnlyList<Person> people)
    {
        var sides = new int[people.Count];
        if (kind != MatchModeKind.Teams)
        {
            return sides;
        }

        int zero = people.Count(p => p.Side == 0), one = people.Count(p => p.Side == 1);
        for (int i = 0; i < people.Count; i++)
        {
            if (people[i].Side is 0 or 1)
            {
                sides[i] = people[i].Side;
                continue;
            }

            sides[i] = one < zero ? 1 : 0;
            if (sides[i] == 0)
            {
                zero++;
            }
            else
            {
                one++;
            }
        }

        return sides;
    }

    /// <summary>The bots' callsigns in a shuffled order for the round (the same order for the same seed).</summary>
    public static string[] Deal(IReadOnlyList<string> callsigns, ulong seed)
    {
        string[] dealt = callsigns.ToArray();
        var rng = new Pcg32(seed ^ 0xCA115165);
        for (int i = dealt.Length - 1; i > 0; i--)
        {
            int j = (int)(rng.NextUInt() % (uint)(i + 1));
            (dealt[i], dealt[j]) = (dealt[j], dealt[i]);
        }

        return dealt;
    }

    private static void AddBot(RoundSetupMessage setup, List<OpponentSpawn?> starts, OpponentSpawn spawn, byte team, string[] names, ref int bot)
    {
        int id = setup.Roster.Count;
        setup.Roster.Add(new RosterEntry
        {
            PlayerId = id, Team = team, Name = names.Length > 0 ? names[bot % names.Length] : $"Bot {bot + 1}", Person = false, Look = (byte)bot,
            Position = spawn.Position, Yaw = spawn.Yaw,
        });
        starts.Add(spawn);
        bot++;
    }
}
