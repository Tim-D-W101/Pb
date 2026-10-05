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

    /// <summary>Your bot teammates' starts (teams), near yours, each with the one role it plays.</summary>
    public IReadOnlyList<OpponentSpawn> Teammates { get; init; } = Array.Empty<OpponentSpawn>();

    /// <summary>The opponents' starts, each with the one role it plays (and a route, for a patroller).</summary>
    public required IReadOnlyList<OpponentSpawn> Opponents { get; init; }
}

/// <summary>
/// Who a round's starts are for: the mode, how many on each side, the roles bots are dealt, and the objective the
/// opponents defend, if there is one.
/// </summary>
public sealed record RoundShape(MatchModeKind Kind, int Teammates, int Opponents, IReadOnlyList<(string Role, float Weight)> Roles,
    ObjectiveFocus? Objective = null)
{
    /// <summary>You against <paramref name="opponents"/>, each playing a role from its spawn.</summary>
    public static RoundShape Solo(int opponents, ObjectiveFocus? objective = null) =>
        new(MatchModeKind.Solo, 0, opponents, Array.Empty<(string, float)>(), objective);

    public static RoundShape Of(GameMode mode, int size, ObjectiveFocus? objective = null) =>
        new(mode.Kind, mode.TeammatesFor(size), mode.OpponentsFor(size), mode.Roles, mode.Kind == MatchModeKind.FreeForAll ? null : objective);
}

/// <summary>
/// Deals a round's random starts from its seed, so you can't learn where everyone is (and the same seed
/// always deals the same starts). You come in at one of the level's player spawns. Opponents start at a
/// mix of the level's opponent spawns and cover points inside its spawn area, at least
/// <see cref="SpawnRules.MinDistanceFromYou"/> from you and out of your sight, spread out where the level
/// allows (the spacing is relaxed if it doesn't). By mode:
/// <list type="bullet">
/// <item>solo: opponents <see cref="SpawnRules.MinSpacing"/> apart, each playing one of its spawn's roles,
/// or by chance at a cover point;</item>
/// <item>free-for-all: everyone <see cref="SpawnRules.FreeForAllSpacing"/> apart and, where the level
/// allows, out of each other's sight;</item>
/// <item>teams: your teammates near you, and the other team grouped round a spot on the far side, out of
/// sight of your whole team.</item>
/// </list>
/// In free-for-all and teams every bot plays a role dealt from the mode's chances. With an objective, its defenders'
/// guards start as near it as fair starts allow (in the room, for hold) and play the guard role, a share of the rest
/// start near it, and in teams the other team gathers round it rather than a far spot.
/// </summary>
public static class SpawnPlanner
{
    /// <summary>Lines of sight are checked to a start's eye and to its chest, this share of the eye height.</summary>
    private const float ChestShare = 0.6f;

    private const float SightRadius = 0.02f;

    /// <summary>The other team's spot is picked from this share of fair starts farthest from you.</summary>
    private const float FarShare = 1f / 3f;

    /// <summary>A start at a spawn or a cover point.</summary>
    private readonly record struct Candidate(Vector3 Position, OpponentSpawn? Spawn, int CoverIndex)
    {
        public string Key => Spawn?.Id ?? $"cover{CoverIndex}";
    }

    /// <summary>Solo starts: you against <paramref name="count"/> opponents.</summary>
    public static SpawnPlan Plan(LevelLayout level, CoverSet cover, CollisionWorld world, SpawnRules rules, BotConfig bots,
        int count, float eyeHeight, ulong seed) =>
        Plan(level, cover, world, rules, bots, RoundShape.Solo(count), eyeHeight, seed);

    public static SpawnPlan Plan(LevelLayout level, CoverSet cover, CollisionWorld world, SpawnRules rules, BotConfig bots,
        RoundShape shape, float eyeHeight, ulong seed)
    {
        var rng = new Pcg32(SeedHash.Combine(seed, 0x5DA75));
        SpawnPoint you = YourStart(level, rules, shape.Objective, ref rng);
        var planner = new Planner(level, cover, world, rules, bots, shape, eyeHeight, you, rng);
        return planner.Run();
    }

    /// <summary>One of the level's player spawns at random; with an objective, one well clear of it (the farthest if none is).</summary>
    private static SpawnPoint YourStart(LevelLayout level, SpawnRules rules, ObjectiveFocus? objective, ref Pcg32 rng)
    {
        IReadOnlyList<SpawnPoint> spawns = level.PlayerSpawns;
        if (objective is null)
        {
            return spawns[(int)(rng.NextUInt() % (uint)spawns.Count)];
        }

        float clear = rules.MinDistanceFromYou + rules.ObjectiveClearance;
        List<SpawnPoint> far = spawns.Where(s => Vector3.Distance(s.Position, objective.At) >= clear).ToList();
        if (far.Count == 0)
        {
            far.Add(spawns.MaxBy(s => Vector3.Distance(s.Position, objective.At)));
        }

        return far[(int)(rng.NextUInt() % (uint)far.Count)];
    }

    private sealed class Planner
    {
        private readonly LevelLayout _level;
        private readonly CoverSet _cover;
        private readonly CollisionWorld _world;
        private readonly SpawnRules _rules;
        private readonly BotConfig _bots;
        private readonly RoundShape _shape;
        private readonly float _eyeHeight;
        private readonly SpawnPoint _you;
        private readonly HashSet<string> _used = new(StringComparer.Ordinal);
        private readonly List<OpponentSpawn> _teammates = new();
        private readonly List<OpponentSpawn> _opponents = new();
        private Pcg32 _rng;
        private int _fromCover;
        private int _guardsLeft;

        public Planner(LevelLayout level, CoverSet cover, CollisionWorld world, SpawnRules rules, BotConfig bots, RoundShape shape,
            float eyeHeight, SpawnPoint you, Pcg32 rng)
        {
            _level = level;
            _cover = cover;
            _world = world;
            _rules = rules;
            _bots = bots;
            _shape = shape;
            _eyeHeight = eyeHeight;
            _you = you;
            _rng = rng;
        }

        public SpawnPlan Run()
        {
            if (_shape.Teammates > 0)
            {
                PlaceTeammates();
            }

            // Every opponent starts fairly: inside the spawn area, away from you, and out of sight of
            // you and your teammates.
            var fair = new List<Candidate>();
            foreach (Candidate c in Candidates())
            {
                if (Inside(_level.SpawnArea, c.Position) && Vector3.Distance(c.Position, _you.Position) >= _rules.MinDistanceFromYou &&
                    !SeenByYourSide(c.Position))
                {
                    fair.Add(c);
                }
            }

            if (_shape.Objective is { } objective)
            {
                PlaceDefenders(fair, objective);
            }

            switch (_shape.Kind)
            {
                case MatchModeKind.Teams:
                    PlaceOtherTeam(fair);
                    break;
                case MatchModeKind.FreeForAll:
                    // Out of each other's sight while they can still be kept apart, then just apart.
                    List<Candidate> pool = Shuffled(fair);
                    Fill(_opponents, _shape.Opponents, pool, _rules.FreeForAllSpacing, apartFromSight: true, spacingFloor: _rules.MinSpacing);
                    Fill(_opponents, _shape.Opponents, pool, _rules.FreeForAllSpacing, apartFromSight: false);
                    break;
                default:
                    PlaceSquad(fair);
                    break;
            }

            // A level too small to start everyone fairly still starts everyone.
            if (_opponents.Count < _shape.Opponents)
            {
                var all = new List<Candidate>();
                foreach (OpponentSpawn s in _level.OpponentSpawns)
                {
                    all.Add(new Candidate(s.Position, s, -1));
                }

                Fill(_opponents, _shape.Opponents, Shuffled(all), 0f, apartFromSight: false);
            }

            if (_opponents.Count < _shape.Opponents || _teammates.Count < _shape.Teammates)
            {
                throw new InvalidOperationException(
                    $"{_level.Id} has room for {_opponents.Count} opponents and {_teammates.Count} teammates, not {_shape.Opponents} and {_shape.Teammates}");
            }

            return new SpawnPlan { You = _you, Teammates = _teammates, Opponents = _opponents };
        }

        /// <summary>Solo: cover points up to their share, then opponent spawns, then cover points again if the spawns ran short.</summary>
        private void PlaceSquad(List<Candidate> fair)
        {
            List<Candidate> spawns = Shuffled(fair.Where(c => c.Spawn is not null));
            List<Candidate> covers = Shuffled(fair.Where(c => c.Spawn is null));
            int wantCover = (int)MathF.Round(_shape.Opponents * _rules.CoverShare);
            int count = _shape.Opponents;
            for (float spacing = _rules.MinSpacing; ; spacing = Relax(spacing))
            {
                Take(_opponents, Math.Min(count, _opponents.Count + wantCover - _fromCover), covers, spacing, apartFromSight: false);
                Take(_opponents, count, spawns, spacing, apartFromSight: false);
                Take(_opponents, count, covers, spacing, apartFromSight: false);
                if (_opponents.Count >= count || spacing <= 0f)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// The objective's defenders: its guards as near it as fair starts allow (inside the room, for hold, if any fair
        /// start is), a little apart; then the near share of the rest within reach of it, spread out.
        /// </summary>
        private void PlaceDefenders(List<Candidate> fair, ObjectiveFocus objective)
        {
            int guards = Math.Min(objective.Guards, _shape.Opponents);
            List<Candidate> inside = objective.Room is { } room ? fair.Where(c => room.Contains(c.Position + new Vector3(0f, 0.1f, 0f))).ToList() : fair;
            List<Candidate> nearest = (inside.Count > 0 ? inside : fair).OrderBy(c => Vector3.Distance(c.Position, objective.At)).ToList();
            _guardsLeft = guards;
            Fill(_opponents, guards, nearest, _rules.TeammateSpacing, apartFromSight: false, spacingFloor: 1f);
            _guardsLeft = 0;

            int near = Math.Min(_shape.Opponents, _opponents.Count + (int)MathF.Round((_shape.Opponents - guards) * objective.NearShare));
            List<Candidate> round = Shuffled(fair.Where(c => Vector3.Distance(c.Position, objective.At) <= objective.Near));
            Fill(_opponents, near, round, _rules.MinSpacing, apartFromSight: false, spacingFloor: _rules.TeammateSpacing);
        }

        /// <summary>Teams: the other team round a spot picked from the fair starts farthest from you (or round the objective).</summary>
        private void PlaceOtherTeam(List<Candidate> fair)
        {
            if (fair.Count == 0)
            {
                return;
            }

            List<Candidate> byDistance = fair.OrderByDescending(c => Vector3.Distance(c.Position, _you.Position)).ToList();
            int far = Math.Max(1, (int)MathF.Ceiling(byDistance.Count * FarShare));
            Vector3 spot = _shape.Objective?.At ?? byDistance[(int)(_rng.NextUInt() % (uint)far)].Position;
            List<Candidate> pool = Shuffled(fair);
            for (float spread = _rules.TeamSpread; _opponents.Count < _shape.Opponents && spread < 1000f; spread *= 1.5f)
            {
                float reach = spread;
                Fill(_opponents, _shape.Opponents, pool.Where(c => Vector3.Distance(c.Position, spot) <= reach).ToList(), _rules.MinSpacing,
                    apartFromSight: false, spacingFloor: _rules.TeammateSpacing);
            }
        }

        /// <summary>Teams: your teammates within reach of you (widened if the level has no room), a little apart.</summary>
        private void PlaceTeammates()
        {
            List<Candidate> pool = Shuffled(Candidates().Where(c => Vector3.Distance(c.Position, _you.Position) >= _rules.TeammateSpacing));
            for (float reach = _rules.TeammatesWithin; _teammates.Count < _shape.Teammates && reach < 1000f; reach *= 1.5f)
            {
                float within = reach;
                Fill(_teammates, _shape.Teammates, pool.Where(c => Vector3.Distance(c.Position, _you.Position) <= within).ToList(),
                    _rules.TeammateSpacing, apartFromSight: false, spacingFloor: _rules.TeammateSpacing);
            }
        }

        /// <summary>
        /// Takes starts from <paramref name="pool"/> until <paramref name="into"/> has <paramref name="count"/>,
        /// halving the spacing down to <paramref name="spacingFloor"/> until it does or the pool runs out.
        /// </summary>
        private void Fill(List<OpponentSpawn> into, int count, List<Candidate> pool, float spacing, bool apartFromSight, float spacingFloor = 0f)
        {
            for (float s = spacing; into.Count < count; s = Relax(s))
            {
                Take(into, count, pool, MathF.Max(s, spacingFloor), apartFromSight);
                if (s <= spacingFloor || s <= 0f)
                {
                    break;
                }
            }
        }

        private void Take(List<OpponentSpawn> into, int count, List<Candidate> pool, float spacing, bool apartFromSight)
        {
            for (int k = 0; k < pool.Count && into.Count < count; k++)
            {
                Candidate c = pool[k];
                if (!_used.Contains(c.Key) && Spaced(into, c.Position, spacing) && (!apartFromSight || !SeenByAny(into, c.Position)))
                {
                    _used.Add(c.Key);
                    into.Add(Start(c, into == _teammates));
                    _fromCover += c.Spawn is null && into == _opponents ? 1 : 0;
                }
            }
        }

        /// <summary>A bot's start at <paramref name="c"/>, with the role it plays (and a route, for a patroller).</summary>
        private OpponentSpawn Start(Candidate c, bool teammate)
        {
            string role = !teammate && _guardsLeft-- > 0 ? _shape.Objective!.GuardRole
                : _shape.Roles.Count > 0 ? PickRole(_shape.Roles)
                : c.Spawn is { } spawn ? spawn.Roles[(int)(_rng.NextUInt() % (uint)spawn.Roles.Count)]
                : PickRole(_rules.CoverRoles);
            bool patrols = Patrols(role);
            if (c.Spawn is { } at)
            {
                return new OpponentSpawn
                {
                    Id = at.Id,
                    Position = at.Position,
                    Yaw = teammate ? _you.Yaw : at.Yaw,
                    Roles = new[] { role },
                    Patrol = patrols ? at.Patrol ?? NearestRoute(at.Position) : null,
                };
            }

            CoverPoint point = _cover.Points[c.CoverIndex];
            return new OpponentSpawn
            {
                Id = c.Key,
                Position = point.Position,
                // Teammates face the way you do; others face the cover's face, the way it's meant to protect from.
                Yaw = teammate ? _you.Yaw : MathF.Atan2(point.Normal.X, point.Normal.Z),
                Roles = new[] { role },
                Patrol = patrols ? NearestRoute(point.Position) : null,
            };
        }

        private IEnumerable<Candidate> Candidates()
        {
            foreach (OpponentSpawn s in _level.OpponentSpawns)
            {
                yield return new Candidate(s.Position, s, -1);
            }

            for (int i = 0; i < _cover.Points.Count; i++)
            {
                yield return new Candidate(_cover.Points[i].Position, null, i);
            }
        }

        private List<Candidate> Shuffled(IEnumerable<Candidate> items)
        {
            var list = items.ToList();
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = (int)(_rng.NextUInt() % (uint)(i + 1));
                (list[i], list[j]) = (list[j], list[i]);
            }

            return list;
        }

        private bool SeenByYourSide(Vector3 feet)
        {
            if (Seen(_you.Position, feet))
            {
                return true;
            }

            foreach (OpponentSpawn mate in _teammates)
            {
                if (Seen(mate.Position, feet))
                {
                    return true;
                }
            }

            return false;
        }

        private bool SeenByAny(List<OpponentSpawn> placed, Vector3 feet)
        {
            foreach (OpponentSpawn other in placed)
            {
                if (Seen(other.Position, feet))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether anything of someone standing at <paramref name="feet"/> is in a clear line from the eye of someone standing at <paramref name="from"/>.</summary>
        private bool Seen(Vector3 from, Vector3 feet)
        {
            Vector3 eye = from + new Vector3(0f, _eyeHeight, 0f);
            return !_world.SweepSphere(eye, feet + new Vector3(0f, _eyeHeight, 0f), SightRadius, out _) ||
                   !_world.SweepSphere(eye, feet + new Vector3(0f, _eyeHeight * ChestShare, 0f), SightRadius, out _);
        }

        private bool Patrols(string role) =>
            _bots.Archetypes.TryGetValue(role, out ArchetypeParams? archetype) && archetype.Idle == BotIdle.Patrol;

        private string PickRole(IReadOnlyList<(string Role, float Weight)> roles)
        {
            float total = 0f;
            foreach ((string _, float weight) in roles)
            {
                total += weight;
            }

            float pick = _rng.NextFloat() * total;
            foreach ((string role, float weight) in roles)
            {
                pick -= weight;
                if (pick < 0f)
                {
                    return role;
                }
            }

            return roles[^1].Role;
        }

        /// <summary>The patrol route passing nearest <paramref name="position"/>, if one comes within reach.</summary>
        private PatrolRoute? NearestRoute(Vector3 position)
        {
            PatrolRoute? best = null;
            float bestDistance = _rules.PatrolReach;
            foreach (PatrolRoute route in _level.Patrols)
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

        private static bool Spaced(List<OpponentSpawn> placed, Vector3 p, float spacing)
        {
            foreach (OpponentSpawn s in placed)
            {
                if (Vector3.Distance(s.Position, p) < spacing)
                {
                    return false;
                }
            }

            return true;
        }

        private static float Relax(float spacing) => spacing > 1f ? spacing * 0.5f : 0f;

        private static bool Inside(Aabb box, Vector3 p) =>
            p.X >= box.Min.X && p.X <= box.Max.X && p.Z >= box.Min.Z && p.Z <= box.Max.Z;
    }
}
