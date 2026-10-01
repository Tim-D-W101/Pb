using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Level;

namespace Pb.Sim.Match;

/// <summary>Where everyone starts in a round.</summary>
public sealed class SpawnPlan
{
    public required SpawnPoint You { get; init; }

    /// <summary>The opponents' starts, each with the one role it plays (and a route, for a patroller).</summary>
    public required IReadOnlyList<OpponentSpawn> Opponents { get; init; }
}

/// <summary>
/// Deals a round's random starts from its seed, so you can't learn where everyone is (and the same seed
/// always deals the same starts). You come in at one of the level's player spawns. Opponents start at a
/// mix of the level's opponent spawns and cover points inside its spawn area: at least
/// <see cref="SpawnRules.MinDistanceFromYou"/> from you and out of your sight, and spread out by
/// <see cref="SpawnRules.MinSpacing"/> where the level allows (the spacing is relaxed if it doesn't).
/// Each plays a role picked at random: one of its spawn's roles, or by chance at a cover point.
/// </summary>
public static class SpawnPlanner
{
    /// <summary>Lines of sight are checked to a start's eye and to its chest, this share of the eye height.</summary>
    private const float ChestShare = 0.6f;

    private const float SightRadius = 0.02f;

    public static SpawnPlan Plan(LevelLayout level, CoverSet cover, CollisionWorld world, SpawnRules rules, BotConfig bots,
        int count, float eyeHeight, ulong seed)
    {
        var rng = new Pcg32(SeedHash.Combine(seed, 0x5DA75));
        SpawnPoint you = level.PlayerSpawns[(int)(rng.NextUInt() % (uint)level.PlayerSpawns.Count)];
        Vector3 eye = you.Position + new Vector3(0f, eyeHeight, 0f);

        bool Fair(Vector3 p) =>
            Inside(level.SpawnArea, p) &&
            Vector3.Distance(p, you.Position) >= rules.MinDistanceFromYou &&
            !Seen(world, eye, p, eyeHeight);

        var authored = new List<OpponentSpawn>();
        foreach (OpponentSpawn s in level.OpponentSpawns)
        {
            if (Fair(s.Position))
            {
                authored.Add(s);
            }
        }

        var covers = new List<int>();
        for (int i = 0; i < cover.Points.Count; i++)
        {
            if (Fair(cover.Points[i].Position))
            {
                covers.Add(i);
            }
        }

        Shuffle(authored, ref rng);
        Shuffle(covers, ref rng);

        var result = new List<OpponentSpawn>(count);
        var usedAuthored = new HashSet<string>(StringComparer.Ordinal);
        var usedCover = new HashSet<int>();
        int wantCover = (int)MathF.Round(count * rules.CoverShare);

        bool Spaced(Vector3 p, float spacing)
        {
            foreach (OpponentSpawn s in result)
            {
                if (Vector3.Distance(s.Position, p) < spacing)
                {
                    return false;
                }
            }

            return true;
        }

        void TakeCover(int wanted, float spacing, List<int> pool)
        {
            for (int k = 0; k < pool.Count && wanted > 0 && result.Count < count; k++)
            {
                int index = pool[k];
                CoverPoint point = cover.Points[index];
                if (!usedCover.Contains(index) && Spaced(point.Position, spacing))
                {
                    usedCover.Add(index);
                    result.Add(AtCover(level, rules, bots, point, index, ref rng));
                    wanted--;
                }
            }
        }

        void TakeAuthored(int wanted, float spacing, List<OpponentSpawn> pool)
        {
            for (int k = 0; k < pool.Count && wanted > 0 && result.Count < count; k++)
            {
                OpponentSpawn spawn = pool[k];
                if (!usedAuthored.Contains(spawn.Id) && Spaced(spawn.Position, spacing))
                {
                    usedAuthored.Add(spawn.Id);
                    result.Add(AtSpawn(level, rules, bots, spawn, ref rng));
                    wanted--;
                }
            }
        }

        // Cover points up to their share, then opponent spawns, then cover points again if the spawns
        // ran short; halving the spacing until everyone fits.
        for (float spacing = rules.MinSpacing; ; spacing = spacing > 1f ? spacing * 0.5f : 0f)
        {
            TakeCover(wantCover - usedCover.Count, spacing, covers);
            TakeAuthored(count - result.Count, spacing, authored);
            TakeCover(count - result.Count, spacing, covers);
            if (result.Count >= count || spacing <= 0f)
            {
                break;
            }
        }

        // A level too small to start everyone fairly still starts everyone.
        if (result.Count < count)
        {
            var all = new List<OpponentSpawn>(level.OpponentSpawns);
            Shuffle(all, ref rng);
            TakeAuthored(count - result.Count, 0f, all);
        }

        if (result.Count < count)
        {
            throw new InvalidOperationException($"{level.Id} has room for {result.Count} opponents, not {count}");
        }

        return new SpawnPlan { You = you, Opponents = result };
    }

    private static OpponentSpawn AtSpawn(LevelLayout level, SpawnRules rules, BotConfig bots, OpponentSpawn spawn, ref Pcg32 rng)
    {
        string role = spawn.Roles[(int)(rng.NextUInt() % (uint)spawn.Roles.Count)];
        return new OpponentSpawn
        {
            Id = spawn.Id,
            Position = spawn.Position,
            Yaw = spawn.Yaw,
            Roles = new[] { role },
            Patrol = Patrols(bots, role) ? spawn.Patrol ?? NearestRoute(level, spawn.Position, rules.PatrolReach) : null,
        };
    }

    private static OpponentSpawn AtCover(LevelLayout level, SpawnRules rules, BotConfig bots, CoverPoint point, int index, ref Pcg32 rng)
    {
        string role = PickRole(rules, ref rng);
        return new OpponentSpawn
        {
            Id = $"cover{index}",
            Position = point.Position,
            // Facing the cover's face: the way the cover is meant to protect from.
            Yaw = MathF.Atan2(point.Normal.X, point.Normal.Z),
            Roles = new[] { role },
            Patrol = Patrols(bots, role) ? NearestRoute(level, point.Position, rules.PatrolReach) : null,
        };
    }

    private static bool Patrols(BotConfig bots, string role) =>
        bots.Archetypes.TryGetValue(role, out ArchetypeParams? archetype) && archetype.Idle == BotIdle.Patrol;

    private static string PickRole(SpawnRules rules, ref Pcg32 rng)
    {
        float total = 0f;
        foreach ((string _, float weight) in rules.CoverRoles)
        {
            total += weight;
        }

        float pick = rng.NextFloat() * total;
        foreach ((string role, float weight) in rules.CoverRoles)
        {
            pick -= weight;
            if (pick < 0f)
            {
                return role;
            }
        }

        return rules.CoverRoles[^1].Role;
    }

    /// <summary>The patrol route passing nearest <paramref name="position"/>, if one comes within <paramref name="reach"/>.</summary>
    private static PatrolRoute? NearestRoute(LevelLayout level, Vector3 position, float reach)
    {
        PatrolRoute? best = null;
        float bestDistance = reach;
        foreach (PatrolRoute route in level.Patrols)
        {
            foreach (Vector3 point in route.Points)
            {
                float d = Vector3.Distance(point, position);
                if (d <= bestDistance)
                {
                    bestDistance = d;
                    best = route;
                }
            }
        }

        return best;
    }

    /// <summary>Whether anything of someone standing at <paramref name="feet"/> is in a clear line from <paramref name="eye"/>.</summary>
    private static bool Seen(CollisionWorld world, Vector3 eye, Vector3 feet, float eyeHeight) =>
        !world.SweepSphere(eye, feet + new Vector3(0f, eyeHeight, 0f), SightRadius, out _) ||
        !world.SweepSphere(eye, feet + new Vector3(0f, eyeHeight * ChestShare, 0f), SightRadius, out _);

    private static bool Inside(Aabb box, Vector3 p) =>
        p.X >= box.Min.X && p.X <= box.Max.X && p.Z >= box.Min.Z && p.Z <= box.Max.Z;

    private static void Shuffle<T>(List<T> items, ref Pcg32 rng)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = (int)(rng.NextUInt() % (uint)(i + 1));
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
