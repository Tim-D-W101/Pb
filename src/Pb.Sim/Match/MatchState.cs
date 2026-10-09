using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Players;

namespace Pb.Sim.Match;

public enum MatchPhase : byte
{
    /// <summary>The briefing card is up: nobody moves or fires and the clock hasn't started.</summary>
    Briefing,
    Live,
    /// <summary>The round is decided; the summary follows.</summary>
    Ended,
}

/// <summary>How a round ended, from one side (yours, offline): see <see cref="MatchState.OutcomeFor"/>.</summary>
public enum RoundOutcome : byte
{
    None,
    /// <summary>Your side is the last one standing: every opponent is out (in teams, your team won).</summary>
    Cleared,
    /// <summary>Your side is out: you (solo, free-for-all) or your whole team.</summary>
    Eliminated,
    /// <summary>The last ones standing went out together (see <see cref="MatchRules.TradeCountsAsClear"/>).</summary>
    Traded,
    TimeUp,
    /// <summary>Retrieve: your side carried the case out.</summary>
    Extracted,
    /// <summary>Hold: your side held the room for the hold time.</summary>
    Held,
    /// <summary>Defending an objective (online, with people on both sides): the clock ran out before the other side did it.</summary>
    HeldOff,
    /// <summary>Retrieve, defending: the other side carried the case out.</summary>
    CaseLost,
    /// <summary>Hold, defending: the other side held the room.</summary>
    RoomLost,
}

public static class RoundOutcomes
{
    /// <summary>The round was won from your side: the last one standing, the objective done, or defended to the end.</summary>
    public static bool IsWin(this RoundOutcome outcome) =>
        outcome is RoundOutcome.Cleared or RoundOutcome.Extracted or RoundOutcome.Held or RoundOutcome.HeldOff;
}

/// <summary>Why a round ended: the same for everyone in it.</summary>
public enum RoundEnd : byte
{
    None,
    /// <summary>One team is the last standing: <see cref="MatchResult.Winner"/>.</summary>
    LastStanding,
    /// <summary>The last ones standing went out together: nobody is left.</summary>
    Traded,
    /// <summary>The clock ran out. With an objective its defenders win; without one, nobody does.</summary>
    TimeUp,
    /// <summary>Retrieve: the attackers carried the case out.</summary>
    Extracted,
    /// <summary>Hold: the attackers held the room for the hold time.</summary>
    Held,
    /// <summary>
    /// Online (<see cref="MatchSetup.EndWhenPeopleOut"/>): no person was still in, so nobody was left to play it out
    /// for. The team with the most players still in wins; a tie, nobody.
    /// </summary>
    PeopleOut,
}

/// <summary>How a round stands, or how it ended, from nobody's side: why, and the winning team (−1 for none).</summary>
public readonly record struct MatchResult(RoundEnd Reason, int Winner)
{
    public static readonly MatchResult Undecided = new(RoundEnd.None, -1);

    public bool Decided => Reason != RoundEnd.None;
}

/// <summary>Round rules (SI), from rules.jsonc.</summary>
public sealed class MatchRules
{
    /// <summary>The modes the menu offers, in order.</summary>
    public required IReadOnlyList<GameMode> Modes { get; init; }

    /// <summary>The most people in one round, you included.</summary>
    public required int MaxPlayers { get; init; }

    /// <summary>How long a decided round waits for balls still in the air before it ends (s).</summary>
    public required float SettleTime { get; init; }

    public required bool TradeCountsAsClear { get; init; }

    public required float PickupRadius { get; init; }

    public required float AirPickupBelow { get; init; }

    public required SpawnRules Spawning { get; init; }

    /// <summary>How doors are worked.</summary>
    public required Level.DoorRules Doors { get; init; }

    /// <summary>The objectives the menu offers and how each is played.</summary>
    public required ObjectiveRules Objectives { get; init; }

    /// <summary>The mode with this id, or null.</summary>
    public GameMode? FindMode(string id)
    {
        foreach (GameMode mode in Modes)
        {
            if (mode.Id == id)
            {
                return mode;
            }
        }

        return null;
    }
}

/// <summary>
/// A mode the menu offers: who plays whom, and the sizes on offer. The size means opponents (solo),
/// players including you (free-for-all) or players a side (teams).
/// </summary>
public sealed class GameMode
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required MatchModeKind Kind { get; init; }

    public required IReadOnlyList<int> Sizes { get; init; }

    public required int DefaultSize { get; init; }

    /// <summary>Behaviours for the bots with their relative chances; empty for solo, where the level's spawn roles apply.</summary>
    public required IReadOnlyList<(string Role, float Weight)> Roles { get; init; }

    /// <summary>A bot with nothing to go on for this long starts hunting (s; 0 = never).</summary>
    public required float RestlessAfter { get; init; }

    /// <summary>Your bot teammates in a round of <paramref name="size"/>.</summary>
    public int TeammatesFor(int size) => Kind == MatchModeKind.Teams ? size - 1 : 0;

    /// <summary>Everyone against you (or, in free-for-all, everyone else) in a round of <paramref name="size"/>.</summary>
    public int OpponentsFor(int size) => Kind == MatchModeKind.FreeForAll ? size - 1 : size;

    /// <summary>Everyone in a round of <paramref name="size"/>, you included.</summary>
    public int PlayersFor(int size) => 1 + TeammatesFor(size) + OpponentsFor(size);
}

/// <summary>How a round deals random starts (<see cref="SpawnPlanner"/>), in metres.</summary>
public sealed class SpawnRules
{
    public required float MinDistanceFromYou { get; init; }

    public required float MinSpacing { get; init; }

    public required float CoverShare { get; init; }

    /// <summary>Behaviours for opponents placed at cover points, with their relative chances.</summary>
    public required IReadOnlyList<(string Role, float Weight)> CoverRoles { get; init; }

    public required float PatrolReach { get; init; }

    /// <summary>Free-for-all: players start at least this far apart (relaxed if the level can't fit them).</summary>
    public required float FreeForAllSpacing { get; init; }

    /// <summary>Teams: your teammates start within this distance of you, at least <see cref="TeammateSpacing"/> apart.</summary>
    public required float TeammatesWithin { get; init; }

    public required float TeammateSpacing { get; init; }

    /// <summary>Teams: the other team starts within this distance of a spot on the far side from you.</summary>
    public required float TeamSpread { get; init; }

    /// <summary>With an objective, you come in at least <see cref="MinDistanceFromYou"/> plus this from it.</summary>
    public required float ObjectiveClearance { get; init; }

    /// <summary>These rules with every distance scaled by <paramref name="scale"/> (for a smaller place to play).</summary>
    public SpawnRules Scaled(float scale) => scale == 1f ? this : new SpawnRules
    {
        MinDistanceFromYou = MinDistanceFromYou * scale,
        MinSpacing = MinSpacing * scale,
        CoverShare = CoverShare,
        CoverRoles = CoverRoles,
        PatrolReach = PatrolReach * scale,
        FreeForAllSpacing = FreeForAllSpacing * scale,
        TeammatesWithin = TeammatesWithin * scale,
        TeammateSpacing = TeammateSpacing,
        TeamSpread = TeamSpread * scale,
        ObjectiveClearance = ObjectiveClearance * scale,
    };
}

/// <summary>One round's settings: who the hero is and the mode, plus an area's difficulty tier.</summary>
public sealed class MatchSetup
{
    private static readonly int[] NoPeople = Array.Empty<int>();

    /// <summary>The players who are people; everyone else is a bot. Offline, it's you alone.</summary>
    public IReadOnlyList<int> People { get; init; } = NoPeople;

    /// <summary>
    /// The one person in a round played alone (it sets <see cref="People"/>): everyone on another team is an opponent.
    /// Reads the first person, or −1 if there's nobody.
    /// </summary>
    public int HeroId
    {
        get => People.Count > 0 ? People[0] : -1;
        init => People = new[] { value };
    }

    /// <summary>The team that attacks the objective; null for the first person's.</summary>
    public byte? Attackers { get; init; }

    /// <summary>
    /// Online: the round also ends once no person is still in it, since nobody is left to play it out for (see
    /// <see cref="RoundEnd.PeopleOut"/>). Offline the bots play on behind your summary.
    /// </summary>
    public bool EndWhenPeopleOut { get; init; }

    /// <summary>Whether <paramref name="playerId"/> is one of <see cref="People"/>.</summary>
    public bool IsPerson(int playerId)
    {
        for (int i = 0; i < People.Count; i++)
        {
            if (People[i] == playerId)
            {
                return true;
            }
        }

        return false;
    }

    public MatchModeKind Mode { get; init; } = MatchModeKind.Solo;

    public required float TimeLimit { get; init; }

    public required int StartPods { get; init; }

    /// <summary>Spare pods every bot starts with, teammates and opponents alike (<see cref="StartPods"/> is every person's).</summary>
    public required int BotPods { get; init; }

    public required bool Pickups { get; init; }

    /// <summary>How the round is won besides being the last team standing; <see cref="Attackers"/> attack it, the others defend.</summary>
    public ObjectiveKind Objective { get; init; } = ObjectiveKind.Eliminate;

    public static MatchSetup From(TierDef tier, int heroId, MatchModeKind mode = MatchModeKind.Solo,
        ObjectiveKind objective = ObjectiveKind.Eliminate) => new()
    {
        HeroId = heroId,
        Mode = mode,
        TimeLimit = tier.TimeLimit_s,
        StartPods = tier.StartPods,
        BotPods = tier.BotPods,
        Pickups = tier.Pickups,
        Objective = mode == MatchModeKind.FreeForAll ? ObjectiveKind.Eliminate : objective,
    };
}

/// <summary>What one player did this round.</summary>
public sealed class PlayerStats
{
    public PlayerStats(int playerId)
    {
        PlayerId = playerId;
    }

    public int PlayerId { get; }

    public int Shots { get; internal set; }

    /// <summary>Balls that broke on an opponent (out already or not).</summary>
    public int Hits { get; internal set; }

    public int Eliminations { get; internal set; }

    public int Pickups { get; internal set; }

    /// <summary>Live time spent still in the round (s).</summary>
    public float TimeIn { get; internal set; }

    /// <summary>The tick they went out on, or −1 while they're still in.</summary>
    public int OutTick { get; internal set; } = -1;

    public float Accuracy => Shots > 0 ? (float)Hits / Shots : 0f;

    /// <summary>A joining copy: the numbers the server kept.</summary>
    internal void ApplyServer(int shots, int hits, int eliminations, int pickups, float timeIn, int outTick)
    {
        Shots = shots;
        Hits = hits;
        Eliminations = eliminations;
        Pickups = pickups;
        TimeIn = timeIn;
        OutTick = outTick;
    }
}

/// <summary>Decides how a round stands. Modes plug in here, so a new way to win is a new mode, not a rewrite.</summary>
public interface IMatchMode
{
    /// <summary>The result if the round ended now, or <see cref="MatchResult.Undecided"/> while it's still on.</summary>
    MatchResult Evaluate(SimWorld sim, MatchState match);
}

/// <summary>
/// The last team standing wins, in every mode: solo is you (a team of one, or everyone who joined) against the squad's
/// team, teams is two teams, and free-for-all is everyone on a team of their own. The round goes on while at least two
/// teams are in, so in free-for-all the rest play on without you. Online it also ends once no person is still in it
/// (<see cref="MatchSetup.EndWhenPeopleOut"/>).
/// </summary>
public sealed class LastTeamStandingMode : IMatchMode
{
    public static readonly LastTeamStandingMode Instance = new();

    public MatchResult Evaluate(SimWorld sim, MatchState match)
    {
        int firstTeam = -1;
        bool twoTeams = false;
        bool personIn = false;
        IReadOnlyList<PlayerState> players = sim.Players;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerState p = players[i];
            if (!p.Alive)
            {
                continue;
            }

            personIn |= match.Setup.IsPerson(p.Id);
            if (firstTeam < 0)
            {
                firstTeam = p.Team;
            }
            else if (p.Team != firstTeam)
            {
                twoTeams = true;
            }
        }

        if (twoTeams)
        {
            return match.Setup.EndWhenPeopleOut && !personIn && match.Setup.People.Count > 0
                ? new MatchResult(RoundEnd.PeopleOut, MostStillIn(players))
                : MatchResult.Undecided;
        }

        return firstTeam >= 0 ? new MatchResult(RoundEnd.LastStanding, firstTeam) : new MatchResult(RoundEnd.Traded, -1);
    }

    /// <summary>The team with the most players still in, or −1 if two share the most.</summary>
    private static int MostStillIn(IReadOnlyList<PlayerState> players)
    {
        Span<int> counts = stackalloc int[256];
        counts.Clear();
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].Alive)
            {
                counts[players[i].Team]++;
            }
        }

        int best = -1, bestCount = 0;
        bool tie = false;
        for (int t = 0; t < counts.Length; t++)
        {
            if (counts[t] > bestCount)
            {
                best = t;
                bestCount = counts[t];
                tie = false;
            }
            else if (counts[t] == bestCount && bestCount > 0)
            {
                tie = true;
            }
        }

        return tie ? -1 : best;
    }
}

/// <summary>
/// One round (spec'd in the Phase 2 plan): briefing → live → ended. While live it keeps everyone's
/// stats from the sim's events (and the order they went out, for free-for-all placings) and asks the
/// mode how the round stands; once it's decided it waits up to the settle time for balls still in the
/// air (so a shot fired as you go down still counts), then ends. Running out of time ends it at once.
/// </summary>
public sealed class MatchState
{
    private readonly List<PlayerStats> _stats = new();
    private int _settleFrom = -1;

    internal MatchState(MatchSetup setup, MatchRules rules, IMatchMode mode, byte attackers, int heroTeam, ObjectiveState? objective = null)
    {
        Setup = setup;
        Rules = rules;
        Mode = mode;
        Attackers = attackers;
        HeroTeam = heroTeam;
        Objective = objective;
    }

    public MatchSetup Setup { get; }

    public MatchRules Rules { get; }

    public IMatchMode Mode { get; }

    /// <summary>The case or the room, when the round has an objective (null for eliminate).</summary>
    public ObjectiveState? Objective { get; }

    public MatchPhase Phase { get; private set; } = MatchPhase.Briefing;

    /// <summary>The team that attacks the objective (the first person's, unless the setup names one).</summary>
    public byte Attackers { get; }

    /// <summary>The first person's team: whose side <see cref="Outcome"/> is told from (−1 with nobody).</summary>
    public int HeroTeam { get; }

    /// <summary>How the round ended, from nobody's side; <see cref="MatchResult.Undecided"/> until it has.</summary>
    public MatchResult Result { get; private set; } = MatchResult.Undecided;

    /// <summary>How the round ended for the first person's side (offline: yours); <see cref="RoundOutcome.None"/> until it has.</summary>
    public RoundOutcome Outcome => OutcomeFor(HeroTeam);

    /// <summary>How the round ended for <paramref name="team"/>; <see cref="RoundOutcome.None"/> until it has.</summary>
    public RoundOutcome OutcomeFor(int team) => Result.Reason switch
    {
        RoundEnd.LastStanding or RoundEnd.PeopleOut => team == Result.Winner ? RoundOutcome.Cleared : RoundOutcome.Eliminated,
        RoundEnd.Traded => Rules.TradeCountsAsClear ? RoundOutcome.Cleared : RoundOutcome.Traded,
        RoundEnd.TimeUp => Objective is not null && team == Result.Winner ? RoundOutcome.HeldOff : RoundOutcome.TimeUp,
        RoundEnd.Extracted => team == Attackers ? RoundOutcome.Extracted : RoundOutcome.CaseLost,
        RoundEnd.Held => team == Attackers ? RoundOutcome.Held : RoundOutcome.RoomLost,
        _ => RoundOutcome.None,
    };

    public int LiveFromTick { get; private set; } = -1;

    public int EndTick { get; private set; } = -1;

    /// <summary>Live time so far (s); frozen once the round ends.</summary>
    public float Elapsed { get; private set; }

    public float TimeLeft => MathF.Max(0f, Setup.TimeLimit - Elapsed);

    public IReadOnlyList<PlayerStats> Stats => _stats;

    public PlayerStats? StatsFor(int playerId)
    {
        foreach (PlayerStats s in _stats)
        {
            if (s.PlayerId == playerId)
            {
                return s;
            }
        }

        return null;
    }

    /// <summary>
    /// Where a player finished (free-for-all): 1 + everyone who outlasted them. Everyone still in shares
    /// first place, and players out on the same tick share a place. 0 for someone not in the round.
    /// </summary>
    public int Placing(int playerId)
    {
        PlayerStats? me = StatsFor(playerId);
        if (me is null)
        {
            return 0;
        }

        if (me.OutTick < 0)
        {
            return 1;
        }

        int ahead = 0;
        foreach (PlayerStats s in _stats)
        {
            if (s.OutTick < 0 || s.OutTick > me.OutTick)
            {
                ahead++;
            }
        }

        return ahead + 1;
    }

    internal void AddPlayer(int playerId) => _stats.Add(new PlayerStats(playerId));

    /// <summary>A joining copy: the round as the server says it stands (the copy never runs the round itself).</summary>
    internal void ApplyServer(MatchPhase phase, int liveFromTick, int endTick, float elapsed, MatchResult result)
    {
        Phase = phase;
        LiveFromTick = liveFromTick;
        EndTick = endTick;
        Elapsed = elapsed;
        Result = result;
    }

    internal void GoLive(SimWorld sim)
    {
        if (Phase != MatchPhase.Briefing)
        {
            return;
        }

        Phase = MatchPhase.Live;
        LiveFromTick = sim.Tick;
        sim.Events.Add(new SimEvent
        {
            Type = SimEventType.MatchPhaseChanged, Tick = sim.Tick, PlayerId = -1, TargetId = -1, ColliderId = -1, Extra = (int)Phase,
        });
    }

    /// <summary>After the tick's balls have flown: stats from this tick's events, then the outcome.</summary>
    internal void Update(SimWorld sim, int firstEvent)
    {
        if (Phase != MatchPhase.Live)
        {
            return;
        }

        float dt = sim.Dt;
        Elapsed += dt;
        CountEvents(sim, firstEvent);
        IReadOnlyList<PlayerState> players = sim.Players;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerState p = players[i];
            if (p.Alive && StatsFor(p.Id) is { } s)
            {
                s.TimeIn += dt;
            }
        }

        Objective?.Update(sim, dt);
        if (Elapsed >= Setup.TimeLimit && Objective is not { Done: true })
        {
            // Without an objective nobody wins on time; with one, its defenders do.
            End(sim, new MatchResult(RoundEnd.TimeUp, Objective is null ? -1 : DefendingTeam(sim)));
            return;
        }

        MatchResult now = Mode.Evaluate(sim, this);
        if (!now.Decided)
        {
            return;
        }

        if (_settleFrom < 0)
        {
            _settleFrom = sim.Tick;
        }

        bool settled = sim.Ballistics.Pool.Count == 0 || (sim.Tick - _settleFrom) * dt >= Rules.SettleTime;
        if (settled)
        {
            End(sim, now);
        }
    }

    private void CountEvents(SimWorld sim, int firstEvent)
    {
        ReadOnlySpan<SimEvent> events = sim.Events.Items;
        for (int i = firstEvent; i < events.Length; i++)
        {
            ref readonly SimEvent e = ref events[i];
            switch (e.Type)
            {
                case SimEventType.ShotFired when StatsFor(e.PlayerId) is { } s:
                    s.Shots++;
                    break;
                case SimEventType.BallBroke when PlayerHitboxes.IsPlayer(e.TargetId) && StatsFor(e.PlayerId) is { } s:
                    PlayerState? victim = sim.FindPlayer(PlayerHitboxes.PlayerIdOf(e.TargetId));
                    if (victim is not null && victim.Team != e.Team)
                    {
                        s.Hits++;
                    }

                    break;
                case SimEventType.PlayerEliminated:
                    if (StatsFor(e.TargetId) is { } victimStats)
                    {
                        victimStats.OutTick = e.Tick;
                    }

                    // Only opponents count: putting out a teammate is no achievement.
                    if (StatsFor(e.PlayerId) is { } shooterStats && sim.FindPlayer(e.PlayerId) is { } shooter && shooter.Team != e.Team)
                    {
                        shooterStats.Eliminations++;
                    }

                    break;
                case SimEventType.PickupTaken when StatsFor(e.PlayerId) is { } s:
                    s.Pickups++;
                    break;
            }
        }
    }

    /// <summary>The team defending the objective: the first team in the round that isn't the attackers (−1 if none).</summary>
    private int DefendingTeam(SimWorld sim)
    {
        IReadOnlyList<PlayerState> players = sim.Players;
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].Team != Attackers)
            {
                return players[i].Team;
            }
        }

        return -1;
    }

    /// <summary>
    /// Ends the round. Its event carries the result: <see cref="SimEvent.Extra"/> the reason, <see cref="SimEvent.TargetId"/>
    /// the winning team (−1 for none), <see cref="SimEvent.Value"/> the outcome for the first person's side.
    /// </summary>
    private void End(SimWorld sim, MatchResult result)
    {
        Phase = MatchPhase.Ended;
        Result = result;
        EndTick = sim.Tick;
        sim.Events.Add(new SimEvent
        {
            Type = SimEventType.RoundEnded, Tick = sim.Tick, PlayerId = -1, TargetId = result.Winner, ColliderId = -1,
            Extra = (int)result.Reason, Value = (int)Outcome,
        });
    }
}
