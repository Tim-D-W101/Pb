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

public enum RoundOutcome : byte
{
    None,
    /// <summary>Every opponent is out.</summary>
    Cleared,
    /// <summary>You're out.</summary>
    Eliminated,
    /// <summary>You and the last opponent went out together (see <see cref="MatchRules.TradeCountsAsClear"/>).</summary>
    Traded,
    TimeUp,
}

/// <summary>Round rules (SI), from rules.jsonc.</summary>
public sealed class MatchRules
{
    public required MatchModeKind Mode { get; init; }

    /// <summary>How long a decided round waits for balls still in the air before it ends (s).</summary>
    public required float SettleTime { get; init; }

    public required bool TradeCountsAsClear { get; init; }

    public required float PickupRadius { get; init; }

    public required float AirPickupBelow { get; init; }

    public required SpawnRules Spawning { get; init; }
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
}

/// <summary>One round's settings: who the hero is, plus a difficulty tier from the ladder.</summary>
public sealed class MatchSetup
{
    /// <summary>The human player; in solo mode everyone on another team is an opponent.</summary>
    public required int HeroId { get; init; }

    public required float TimeLimit { get; init; }

    public required int StartPods { get; init; }

    public required int OpponentPods { get; init; }

    public required bool Pickups { get; init; }

    public static MatchSetup From(LadderTierDef tier, int heroId) => new()
    {
        HeroId = heroId,
        TimeLimit = tier.TimeLimit_s,
        StartPods = tier.StartPods,
        OpponentPods = tier.OpponentPods,
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

    public float Accuracy => Shots > 0 ? (float)Hits / Shots : 0f;
}

/// <summary>Decides how a round stands. Modes plug in here, so free-for-all or squads are a new mode, not a rewrite.</summary>
public interface IMatchMode
{
    /// <summary>The outcome if the round ended now, or <see cref="RoundOutcome.None"/> while it's still on.</summary>
    RoundOutcome Evaluate(SimWorld sim, MatchState match);
}

/// <summary>You against everyone else: cleared when no opponent is left, eliminated when you're out.</summary>
public sealed class SoloMode : IMatchMode
{
    public RoundOutcome Evaluate(SimWorld sim, MatchState match)
    {
        PlayerState? hero = sim.FindPlayer(match.Setup.HeroId);
        bool heroOut = hero is not { Alive: true };
        int opponentsIn = 0;
        IReadOnlyList<PlayerState> players = sim.Players;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerState p = players[i];
            if (p.Alive && (hero is null || p.Team != hero.Team))
            {
                opponentsIn++;
            }
        }

        return (heroOut, opponentsIn) switch
        {
            (true, 0) => match.Rules.TradeCountsAsClear ? RoundOutcome.Cleared : RoundOutcome.Traded,
            (true, _) => RoundOutcome.Eliminated,
            (false, 0) => RoundOutcome.Cleared,
            _ => RoundOutcome.None,
        };
    }
}

/// <summary>
/// One round (spec'd in the Phase 2 plan): briefing → live → ended. While live it keeps everyone's
/// stats from the sim's events and asks the mode how the round stands; once it's decided it waits
/// up to the settle time for balls still in the air (so a shot fired as you go down still counts),
/// then ends. Running out of time ends it at once.
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
                case SimEventType.PlayerEliminated when StatsFor(e.PlayerId) is { } s:
                    s.Eliminations++;
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
