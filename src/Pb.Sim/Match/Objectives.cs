using System.Numerics;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Players;

namespace Pb.Sim.Match;

/// <summary>One of the menu's objectives: what it's called and what to do.</summary>
public sealed record ObjectiveChoice(ObjectiveKind Kind, string DisplayName, string Description);

/// <summary>Retrieve (SI), from rules.jsonc "objectives.retrieve".</summary>
public sealed class RetrieveRules
{
    public required float PickupRadius { get; init; }

    public required bool CarrierCanSprint { get; init; }

    public required float ExitRadius { get; init; }

    public required int Guards { get; init; }

    public required float NearShare { get; init; }

    public required float Near { get; init; }

    public required float AlarmInterval { get; init; }

    public required int Chasers { get; init; }
}

/// <summary>Hold (SI), from rules.jsonc "objectives.hold".</summary>
public sealed class HoldRules
{
    public required float HoldTime { get; init; }

    public required int Guards { get; init; }

    public required float NearShare { get; init; }

    public required float Near { get; init; }

    public required float AlarmInterval { get; init; }
}

/// <summary>The objectives the menu offers, and how each is played.</summary>
public sealed class ObjectiveRules
{
    public required IReadOnlyList<ObjectiveChoice> Kinds { get; init; }

    public required RetrieveRules Retrieve { get; init; }

    public required HoldRules Hold { get; init; }

    /// <summary>The behaviour the objective's guards play (they hold their post).</summary>
    public required string GuardRole { get; init; }

    public ObjectiveChoice? Find(ObjectiveKind kind)
    {
        foreach (ObjectiveChoice choice in Kinds)
        {
            if (choice.Kind == kind)
            {
                return choice;
            }
        }

        return null;
    }
}

/// <summary>Who's in the room to hold.</summary>
public enum HoldStatus : byte
{
    /// <summary>Nobody.</summary>
    Empty,

    /// <summary>Your side only: the clock runs.</summary>
    Ours,

    /// <summary>Theirs only.</summary>
    Theirs,

    /// <summary>Both sides: the clock stops.</summary>
    Contested,
}

/// <summary>
/// The round's objective as it stands, part of the <see cref="MatchState"/>. Your side (the hero's team) attacks it and
/// the others defend. Retrieve: the case starts at one of the level's case spots (picked from the match seed); anyone on
/// your side still in picks it up by walking within reach of it; while carried it moves with the carrier, who can't
/// sprint (by the rules); when the carrier goes out it drops where they fell, for a teammate to pick up; carried to a way
/// out, it's done. Hold: one of the level's rooms (from the seed); while anyone of yours is in it and none of theirs, the
/// hold clock runs, and it never runs back; held for the hold time in all, it's done. Updated once a tick while the round
/// is live, after the balls have flown, so a carrier hit this tick drops the case this tick.
/// </summary>
public sealed class ObjectiveState
{
    private readonly ObjectiveRules _rules;

    internal ObjectiveState(ObjectiveKind kind, byte attackers, LevelObjectives level, ObjectiveRules rules, ulong seed)
    {
        Kind = kind;
        Attackers = attackers;
        Level = level;
        _rules = rules;
        switch (kind)
        {
            case ObjectiveKind.Retrieve:
                CaseSpotIndex = PickIndex(level.CaseSpots.Count, seed);
                CasePosition = level.CaseSpots[CaseSpotIndex].Position;
                break;
            case ObjectiveKind.Hold:
                RoomIndex = PickIndex(level.Rooms.Count, seed);
                break;
        }
    }

    /// <summary>Which case spot or room a round plays for, from its seed (the starts are dealt round the same one).</summary>
    internal static int PickIndex(int count, ulong seed)
    {
        var rng = new Pcg32(SeedHash.Combine(seed, 0x0B1EC7));
        return (int)(rng.NextUInt() % (uint)count);
    }

    public ObjectiveKind Kind { get; }

    /// <summary>The side playing for the objective (offline, yours).</summary>
    public byte Attackers { get; }

    public LevelObjectives Level { get; }

    public RetrieveRules RetrieveRules => _rules.Retrieve;

    public HoldRules HoldRules => _rules.Hold;

    /// <summary>The objective is done: the case is out, or the room held for the hold time.</summary>
    public bool Done { get; private set; }

    /// <summary>Retrieve: which of the level's case spots it started at (−1 otherwise).</summary>
    public int CaseSpotIndex { get; } = -1;

    public CaseSpot Spot => Level.CaseSpots[CaseSpotIndex];

    /// <summary>Where the case is: its spot, the carrier's feet, or where it fell.</summary>
    public Vector3 CasePosition { get; private set; }

    /// <summary>Who carries the case, or −1.</summary>
    public int Carrier { get; private set; } = -1;

    /// <summary>The case has been picked up at least once (so its holders know it's gone).</summary>
    public bool CaseMoved { get; private set; }

    /// <summary>The way out it left by, once done.</summary>
    public int ExitUsed { get; private set; } = -1;

    /// <summary>Hold: which of the level's rooms (−1 otherwise).</summary>
    public int RoomIndex { get; } = -1;

    public HoldRoom? Room => RoomIndex >= 0 ? Level.Rooms[RoomIndex] : null;

    /// <summary>Hold: seconds your side has held the room so far.</summary>
    public float Held { get; private set; }

    public HoldStatus Status { get; private set; }

    /// <summary>A joining copy: the objective as the server says it stands.</summary>
    internal void ApplyServer(bool done, Vector3 casePosition, int carrier, bool caseMoved, int exitUsed, float held, HoldStatus status)
    {
        Done = done;
        CasePosition = casePosition;
        Carrier = carrier;
        CaseMoved = caseMoved;
        ExitUsed = exitUsed;
        Held = held;
        Status = status;
    }

    internal void Update(SimWorld sim, float dt)
    {
        if (Done)
        {
            return;
        }

        if (Kind == ObjectiveKind.Retrieve)
        {
            UpdateCase(sim);
        }
        else if (Kind == ObjectiveKind.Hold)
        {
            UpdateRoom(sim, dt);
        }
    }

    private void UpdateCase(SimWorld sim)
    {
        RetrieveRules r = _rules.Retrieve;
        if (Carrier >= 0)
        {
            PlayerState? carrier = sim.FindPlayer(Carrier);
            if (carrier is null || !carrier.Alive)
            {
                if (carrier is not null)
                {
                    CasePosition = carrier.Position;
                    carrier.SprintBlocked = false;
                }

                sim.Events.Add(new SimEvent
                {
                    Type = SimEventType.CaseDropped, Tick = sim.Tick, PlayerId = Carrier, Team = Attackers, Position = CasePosition, TargetId = -1,
                    ColliderId = -1,
                });
                Carrier = -1;
            }
            else
            {
                CasePosition = carrier.Position;
                carrier.SprintBlocked = !r.CarrierCanSprint;
                for (int i = 0; i < Level.Exits.Count; i++)
                {
                    Vector3 d = Level.Exits[i].Position - carrier.Position;
                    if (d.X * d.X + d.Z * d.Z <= r.ExitRadius * r.ExitRadius && MathF.Abs(d.Y) < 2f)
                    {
                        Done = true;
                        ExitUsed = i;
                        carrier.SprintBlocked = false;
                        sim.Events.Add(new SimEvent
                        {
                            Type = SimEventType.CaseExtracted, Tick = sim.Tick, PlayerId = carrier.Id, Team = carrier.Team, Position = CasePosition,
                            Extra = i, TargetId = -1, ColliderId = -1,
                        });
                        return;
                    }
                }

                return;
            }
        }

        // Lying at its spot or where it fell: the first of your side within reach picks it up.
        IReadOnlyList<PlayerState> players = sim.Players;
        float reach2 = r.PickupRadius * r.PickupRadius;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerState p = players[i];
            Vector3 d = CasePosition - p.Position;
            if (!p.Alive || !p.Present || p.Team != Attackers || d.X * d.X + d.Z * d.Z > reach2 || MathF.Abs(d.Y) > 1f)
            {
                continue;
            }

            Carrier = p.Id;
            CaseMoved = true;
            p.SprintBlocked = !r.CarrierCanSprint;
            sim.Events.Add(new SimEvent
            {
                Type = SimEventType.CaseTaken, Tick = sim.Tick, PlayerId = p.Id, Team = p.Team, Position = CasePosition, TargetId = -1, ColliderId = -1,
            });
            return;
        }
    }

    private void UpdateRoom(SimWorld sim, float dt)
    {
        HoldRoom room = Room!;
        bool ours = false, theirs = false;
        IReadOnlyList<PlayerState> players = sim.Players;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerState p = players[i];
            if (!p.Alive || !p.Present || !room.Contains(p.Position + new Vector3(0f, 0.1f, 0f)))
            {
                continue;
            }

            ours |= p.Team == Attackers;
            theirs |= p.Team != Attackers;
        }

        HoldStatus now = ours && theirs ? HoldStatus.Contested : ours ? HoldStatus.Ours : theirs ? HoldStatus.Theirs : HoldStatus.Empty;
        if (now == HoldStatus.Ours)
        {
            Held = MathF.Min(_rules.Hold.HoldTime, Held + dt);
            Done = Held >= _rules.Hold.HoldTime;
        }

        if (now != Status)
        {
            Status = now;
            sim.Events.Add(new SimEvent
            {
                Type = SimEventType.HoldChanged, Tick = sim.Tick, PlayerId = -1, Team = Attackers, Position = room.Centre, Value = Held,
                Extra = (int)now, TargetId = -1, ColliderId = -1,
            });
        }
    }
}

/// <summary>
/// Where a round's objective will be, known before anyone starts so the defenders' starts gather round it: the case's
/// spot or the room's middle (and the room), with how many guards start right there (playing <see cref="GuardRole"/>)
/// and what share of the rest start within <see cref="Near"/>.
/// </summary>
public sealed record ObjectiveFocus(ObjectiveKind Kind, Vector3 At, HoldRoom? Room, int Guards, float NearShare, float Near, string GuardRole)
{
    /// <summary>The focus of a round of <paramref name="kind"/> dealt from <paramref name="seed"/>; null for eliminate.</summary>
    public static ObjectiveFocus? For(ObjectiveKind kind, LevelObjectives level, ObjectiveRules rules, ulong seed)
    {
        switch (kind)
        {
            case ObjectiveKind.Retrieve when level.Offers(kind):
            {
                RetrieveRules r = rules.Retrieve;
                Vector3 at = level.CaseSpots[ObjectiveState.PickIndex(level.CaseSpots.Count, seed)].Position;
                return new ObjectiveFocus(kind, at, null, r.Guards, r.NearShare, r.Near, rules.GuardRole);
            }
            case ObjectiveKind.Hold when level.Offers(kind):
            {
                HoldRules h = rules.Hold;
                HoldRoom room = level.Rooms[ObjectiveState.PickIndex(level.Rooms.Count, seed)];
                return new ObjectiveFocus(kind, room.Centre, room, h.Guards, h.NearShare, h.Near, rules.GuardRole);
            }
            default:
                return null;
        }
    }
}

/// <summary>Retrieve: the case carried out wins for the attackers; otherwise the last team standing.</summary>
public sealed class RetrieveMode : IMatchMode
{
    public static readonly RetrieveMode Instance = new();

    public MatchResult Evaluate(SimWorld sim, MatchState match) =>
        match.Objective is { Done: true } ? new MatchResult(RoundEnd.Extracted, match.Attackers) : LastTeamStandingMode.Instance.Evaluate(sim, match);
}

/// <summary>Hold: the room held for the hold time wins for the attackers; otherwise the last team standing.</summary>
public sealed class HoldMode : IMatchMode
{
    public static readonly HoldMode Instance = new();

    public MatchResult Evaluate(SimWorld sim, MatchState match) =>
        match.Objective is { Done: true } ? new MatchResult(RoundEnd.Held, match.Attackers) : LastTeamStandingMode.Instance.Evaluate(sim, match);
}
