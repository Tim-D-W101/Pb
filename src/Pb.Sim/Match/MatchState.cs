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

/// <summary>How a round ended, from your side.</summary>
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
}

/// <summary>One round's settings: who the hero is and the mode, plus a difficulty tier from the ladder.</summary>
public sealed class MatchSetup
{
    /// <summary>The human player: everyone on another team is an opponent.</summary>
    public required int HeroId { get; init; }

    public MatchModeKind Mode { get; init; } = MatchModeKind.Solo;

    public required float TimeLimit { get; init; }

    public required int StartPods { get; init; }

    /// <summary>Spare pods every bot starts with, teammates and opponents alike.</summary>
    public required int BotPods { get; init; }

    public required bool Pickups { get; init; }

    public static MatchSetup From(LadderTierDef tier, int heroId, MatchModeKind mode = MatchModeKind.Solo) => new()
    {
        HeroId = heroId,
        Mode = mode,
        TimeLimit = tier.TimeLimit_s,
        StartPods = tier.StartPods,
        BotPods = tier.BotPods,
        Pickups = tier.Pickups,
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
}

/// <summary>Decides how a round stands. Modes plug in here, so a new way to win is a new mode, not a rewrite.</summary>
public interface IMatchMode
{
    /// <summary>The outcome if the round ended now, or <see cref="RoundOutcome.None"/> while it's still on.</summary>
    RoundOutcome Evaluate(SimWorld sim, MatchState match);
}

/// <summary>
/// The last team standing wins, in every mode: solo is you (a team of one) against the squad's team,
/// teams is two teams, and free-for-all is everyone on a team of their own. The round goes on while
/// your team and another are both in, or, once yours is out, while two others still are (free-for-all:
/// the rest play on without you).
/// </summary>
public sealed class LastTeamStandingMode : IMatchMode
{
    public static readonly LastTeamStandingMode Instance = new();

    public RoundOutcome Evaluate(SimWorld sim, MatchState match)
    {
        PlayerState? hero = sim.FindPlayer(match.Setup.HeroId);
        int heroTeam = hero?.Team ?? -1;
        bool oursIn = false;
        int otherTeam = -1;
        bool othersFighting = false;
        IReadOnlyList<PlayerState> players = sim.Players;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerState p = players[i];
            if (!p.Alive)
            {
                continue;
            }

            if (p.Team == heroTeam)
            {
                oursIn = true;
            }
            else if (otherTeam < 0)
            {
                otherTeam = p.Team;
            }
            else if (p.Team != otherTeam)
            {
                othersFighting = true;
            }
        }

        bool othersIn = otherTeam >= 0;
        return (oursIn, othersIn) switch
        {
            (true, true) => RoundOutcome.None,
            (true, false) => RoundOutcome.Cleared,
            (false, true) => othersFighting ? RoundOutcome.None : RoundOutcome.Eliminated,
            _ => match.Rules.TradeCountsAsClear ? RoundOutcome.Cleared : RoundOutcome.Traded,
        };
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

    internal MatchState(MatchSetup setup, MatchRules rules, IMatchMode mode)
    {
        Setup = setup;
        Rules = rules;
        Mode = mode;
    }

    public MatchSetup Setup { get; }

    public MatchRules Rules { get; }

    public IMatchMode Mode { get; }

    public MatchPhase Phase { get; private set; } = MatchPhase.Briefing;

    public RoundOutcome Outcome { get; private set; }

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

        if (Elapsed >= Setup.TimeLimit)
        {
            End(sim, RoundOutcome.TimeUp);
            return;
        }

        RoundOutcome now = Mode.Evaluate(sim, this);
        if (now == RoundOutcome.None)
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

    private void End(SimWorld sim, RoundOutcome outcome)
    {
        Phase = MatchPhase.Ended;
        Outcome = outcome;
        EndTick = sim.Tick;
        sim.Events.Add(new SimEvent
        {
            Type = SimEventType.RoundEnded, Tick = sim.Tick, PlayerId = -1, TargetId = -1, ColliderId = -1, Extra = (int)outcome,
        });
    }
}
