using System.Numerics;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Players;

namespace Pb.Sim.Match;

/// <summary>One of the menu's objectives: what it's called and what to do, and whether only two teams play it.</summary>
public sealed record ObjectiveChoice(ObjectiveKind Kind, string DisplayName, string Description, bool TeamsOnly = false)
{
    /// <summary>Whether it can be played in <paramref name="mode"/> (free-for-all is always last one standing).</summary>
    public bool PlayableIn(MatchModeKind mode) =>
        Kind == ObjectiveKind.Eliminate || (mode != MatchModeKind.FreeForAll && (!TeamsOnly || mode == MatchModeKind.Teams));
}

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

    /// <summary>A way out nearer than this to where the case was found doesn't count (the farthest does if none is that far).</summary>
    public float MinExitDistance { get; init; }
}

/// <summary>Hold (SI), from rules.jsonc "objectives.hold".</summary>
public sealed class HoldRules
{
    public required float HoldTime { get; init; }

    public required int Guards { get; init; }

    public required float NearShare { get; init; }

    public required float Near { get; init; }

    public required float AlarmInterval { get; init; }

    /// <summary>Contested this long (s), the attackers in the room learn where the defenders in it are, and go for them.</summary>
    public float StalemateAfter { get; init; }
}

/// <summary>Capture the flag (SI), from rules.jsonc "objectives.flag".</summary>
public sealed class FlagRules
{
    public required float PickupRadius { get; init; }

    public required float CaptureRadius { get; init; }

    public required bool CarrierCanSprint { get; init; }

    public required bool ReturnOnTouch { get; init; }

    /// <summary>A flag lying where it fell this long goes home by itself (s; 0: never).</summary>
    public required float ReturnAfter { get; init; }

    public required bool NeedOwnFlagHome { get; init; }

    public required float DefendShare { get; init; }

    public required int Chasers { get; init; }

    public required float AlarmInterval { get; init; }
}

/// <summary>VIP escort (SI), from rules.jsonc "objectives.escort".</summary>
public sealed class EscortRules
{
    public required float SpeedShare { get; init; }

    /// <summary>Only ways out at least this share of the farthest one's distance from the VIP's start count.</summary>
    public required float FarShare { get; init; }

    /// <summary>This share of the defenders (at least one) make for the ways out that count from the start, to guard them.</summary>
    public required float ExitGuardShare { get; init; }

    /// <summary>Bots: escorts keep this far ahead of the VIP on the way out ...</summary>
    public required float Lead { get; init; }

    /// <summary>... and the VIP waits while none of them is within this.</summary>
    public required float WaitFor { get; init; }

    public required float ExitRadius { get; init; }

    public required float MinExitDistance { get; init; }

    public required float AlarmAfter { get; init; }

    public required float AlarmInterval { get; init; }

    public required int Chasers { get; init; }
}

/// <summary>Capture the flag: one side's flag, where it stands at home, where it is now and who carries it.</summary>
public sealed class FlagState
{
    internal FlagState(byte team, Vector3 home)
    {
        Team = team;
        Home = home;
        Position = home;
    }

    /// <summary>The side the flag belongs to (the other side takes it).</summary>
    public byte Team { get; }

    /// <summary>Its base: where that side started (the start nearest the middle of theirs).</summary>
    public Vector3 Home { get; internal set; }

    /// <summary>Where it is: at home, on the carrier, or where it fell.</summary>
    public Vector3 Position { get; internal set; }

    /// <summary>Who carries it, or −1.</summary>
    public int Carrier { get; internal set; } = -1;

    /// <summary>Standing at its base.</summary>
    public bool AtHome { get; internal set; } = true;

    /// <summary>Lying where it fell (not at home, not carried).</summary>
    public bool Down => !AtHome && Carrier < 0;

    /// <summary>How long it has lain where it fell (s).</summary>
    public float DownFor { get; internal set; }
}

/// <summary>The objectives the menu offers, and how each is played.</summary>
public sealed class ObjectiveRules
{
    public required IReadOnlyList<ObjectiveChoice> Kinds { get; init; }

    public required RetrieveRules Retrieve { get; init; }

    public required HoldRules Hold { get; init; }

    public required FlagRules Flag { get; init; }

    public required EscortRules Escort { get; init; }

    /// <summary>The behaviour the objective's guards play (they hold their post).</summary>
    public required string GuardRole { get; init; }

    /// <summary>Defenders with nothing better to do (hunting, gone restless, roaming) keep within this of the objective (m; 0 = anywhere).</summary>
    public float DefendWithin { get; init; }

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
/// hold clock runs, and it never runs back; held for the hold time in all, it's done.
/// <para>
/// Capture the flag (both sides attack and defend): each side's flag stands at its base, where that side starts. Anyone
/// of the other side takes it by walking within reach; it moves with its carrier (who can't sprint, by the rules) and
/// falls where they're hit; its own side touching it there sends it home, and left lying it goes home by itself; carried
/// to the carrier's own base, it's captured and that side has won. VIP escort: one of the attacking side (a bot if there
/// is one) is the VIP, unarmed and slower; brought to a way out far enough from where they started, they're out safe.
/// Put out, the defenders have won (the mode says so). The VIP is held as the "carrier" of the case, which is where they
/// are, so whatever escorts or chases a carried case does the same for the VIP.
/// </para>
/// Updated once a tick while the round is live, after the balls have flown, so a carrier hit this tick drops what they
/// carry this tick.
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
                CountingExits = ExitsFarFrom(level, CasePosition, rules.Retrieve.MinExitDistance);
                break;
            case ObjectiveKind.Hold:
                RoomIndex = PickIndex(level.Rooms.Count, seed);
                break;
        }
    }

    /// <summary>
    /// With everyone at their starts: capture the flag's bases (each side's start nearest the middle of its starts) and
    /// the VIP (from the seed: the attacking side's bots first, a person only when there's no bot), who's then unarmed and
    /// slower, and the ways out that count for them. The same on every copy of the round: the roster and the seed are.
    /// </summary>
    internal void SetUp(IReadOnlyList<PlayerState> players, MatchSetup setup, ulong seed)
    {
        if (Kind == ObjectiveKind.Flag)
        {
            var flags = new List<FlagState>();
            foreach (byte team in players.Where(p => p.Present).Select(p => p.Team).Distinct().OrderBy(t => t).Take(2))
            {
                PlayerState[] side = players.Where(p => p.Present && p.Team == team).ToArray();
                Vector3 middle = side.Aggregate(Vector3.Zero, (sum, p) => sum + p.Position) / side.Length;
                Vector3 home = side.MinBy(p => Vector3.DistanceSquared(p.Position, middle))!.Position;
                flags.Add(new FlagState(team, home));
            }

            Flags = flags.ToArray();
        }
        else if (Kind == ObjectiveKind.Escort)
        {
            PlayerState[] side = players.Where(p => p.Present && p.Team == Attackers).OrderBy(p => p.Id).ToArray();
            PlayerState[] bots = side.Where(p => !setup.IsPerson(p.Id)).ToArray();
            PlayerState[] pool = bots.Length > 0 ? bots : side;
            if (pool.Length == 0)
            {
                return;
            }

            var rng = new Pcg32(SeedHash.Combine(seed, 0x7E5C0));
            PlayerState vip = pool[(int)(rng.NextUInt() % (uint)pool.Length)];
            Vip = vip.Id;
            Carrier = vip.Id;
            CaseMoved = true;
            VipStart = vip.Position;
            CasePosition = vip.Position;
            // The far ways out (the team's own start is often by a gate): across the area, past the other side.
            float farthest = Level.Exits.Count == 0 ? 0f : Level.Exits.Max(e => Flat(e.Position - vip.Position));
            CountingExits = ExitsFarFrom(Level, vip.Position, MathF.Max(_rules.Escort.MinExitDistance, _rules.Escort.FarShare * farthest));
            MakeVip(vip);
        }
    }

    /// <summary>The VIP's lot: no marker to fire, a slower pace, no sprinting.</summary>
    internal void MakeVip(PlayerState vip)
    {
        vip.Unarmed = true;
        vip.SpeedScale = _rules.Escort.SpeedShare;
        vip.SprintBlocked = true;
    }

    /// <summary>
    /// The ways out at least <paramref name="min"/> from <paramref name="from"/> (a case carried a few steps from where it
    /// lay to a gap in the fence made a round of seconds); the farthest if none is that far.
    /// </summary>
    private static int[] ExitsFarFrom(LevelObjectives level, Vector3 from, float min)
    {
        if (level.Exits.Count == 0)
        {
            return Array.Empty<int>();
        }

        int[] far = Enumerable.Range(0, level.Exits.Count).Where(i => Flat(level.Exits[i].Position - from) >= min).ToArray();
        return far.Length > 0 ? far : new[] { Enumerable.Range(0, level.Exits.Count).MaxBy(i => Flat(level.Exits[i].Position - from)) };
    }

    private static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

    /// <summary>
    /// Retrieve (and escort): the ways out the case (the VIP) can leave by (indices into the level's), those far enough
    /// from where it lay (where they started).
    /// </summary>
    public IReadOnlyList<int> CountingExits { get; private set; } = Array.Empty<int>();

    /// <summary>Whether the case can leave by way out <paramref name="exit"/>.</summary>
    public bool ExitCounts(int exit)
    {
        for (int i = 0; i < CountingExits.Count; i++)
        {
            if (CountingExits[i] == exit)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The way out the case can leave by nearest <paramref name="from"/>.</summary>
    public ExitSpec NearestExit(Vector3 from)
    {
        ExitSpec best = Level.Exits[CountingExits[0]];
        for (int i = 1; i < CountingExits.Count; i++)
        {
            ExitSpec e = Level.Exits[CountingExits[i]];
            if (Flat(e.Position - from) < Flat(best.Position - from))
            {
                best = e;
            }
        }

        return best;
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

    public FlagRules FlagRules => _rules.Flag;

    public EscortRules EscortRules => _rules.Escort;

    /// <summary>
    /// The objective is done: the case is out, the room held for the hold time, a flag captured (<see cref="CapturedBy"/>
    /// says by whom) or the VIP out safe.
    /// </summary>
    public bool Done { get; private set; }

    /// <summary>Capture the flag: each side's flag (two), in team order; empty otherwise.</summary>
    public IReadOnlyList<FlagState> Flags { get; private set; } = Array.Empty<FlagState>();

    /// <summary>Capture the flag: the side that captured a flag, once done (−1 before).</summary>
    public int CapturedBy { get; private set; } = -1;

    /// <summary>Capture the flag: <paramref name="team"/>'s own flag, or null.</summary>
    public FlagState? FlagOf(int team)
    {
        for (int i = 0; i < Flags.Count; i++)
        {
            if (Flags[i].Team == team)
            {
                return Flags[i];
            }
        }

        return null;
    }

    /// <summary>Capture the flag: the flag <paramref name="team"/> plays for (the other side's), or null.</summary>
    public FlagState? TheirFlag(int team)
    {
        for (int i = 0; i < Flags.Count; i++)
        {
            if (Flags[i].Team != team)
            {
                return Flags[i];
            }
        }

        return null;
    }

    /// <summary>Capture the flag: the flag <paramref name="playerId"/> carries, or null.</summary>
    public FlagState? CarriedBy(int playerId)
    {
        for (int i = 0; i < Flags.Count; i++)
        {
            if (Flags[i].Carrier == playerId)
            {
                return Flags[i];
            }
        }

        return null;
    }

    /// <summary>VIP escort: the VIP (−1 otherwise).</summary>
    public int Vip { get; private set; } = -1;

    /// <summary>VIP escort: where the VIP started.</summary>
    public Vector3 VipStart { get; private set; }

    /// <summary>VIP escort: the VIP was put out (the defenders have won).</summary>
    public bool VipOut { get; private set; }

    /// <summary>
    /// Someone whose going out decides the round, so worth shooting first: the VIP, or a flag's or the case's carrier.
    /// </summary>
    public bool IsKeyPlayer(int playerId) => !Done && playerId >= 0 && (playerId == Carrier || playerId == Vip || CarriedBy(playerId) is not null);

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

    /// <summary>Hold: how long the room has been contested without a break (s).</summary>
    public float ContestedFor { get; private set; }

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

    /// <summary>A joining copy: capture the flag's flags and the VIP as the server says they stand.</summary>
    internal void ApplyServerExtra(int capturedBy, bool vipOut, ReadOnlySpan<(Vector3 Position, int Carrier, bool AtHome)> flags)
    {
        CapturedBy = capturedBy;
        VipOut = vipOut;
        for (int i = 0; i < Flags.Count && i < flags.Length; i++)
        {
            FlagState f = Flags[i];
            f.Position = flags[i].Position;
            f.Carrier = flags[i].Carrier;
            f.AtHome = flags[i].AtHome;
        }
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
        else if (Kind == ObjectiveKind.Flag)
        {
            UpdateFlags(sim, dt);
        }
        else if (Kind == ObjectiveKind.Escort)
        {
            UpdateVip(sim);
        }
    }

    private void UpdateFlags(SimWorld sim, float dt)
    {
        FlagRules r = _rules.Flag;
        IReadOnlyList<PlayerState> players = sim.Players;
        float reach2 = r.PickupRadius * r.PickupRadius;
        for (int f = 0; f < Flags.Count; f++)
        {
            FlagState flag = Flags[f];
            if (flag.Carrier >= 0)
            {
                PlayerState? carrier = sim.FindPlayer(flag.Carrier);
                if (carrier is null || !carrier.Alive || !carrier.Present)
                {
                    // Hit (or gone): it falls where they were.
                    if (carrier is not null)
                    {
                        flag.Position = carrier.Position;
                        carrier.SprintBlocked = carrier.Unarmed;
                    }

                    sim.Events.Add(new SimEvent
                    {
                        Type = SimEventType.FlagDropped, Tick = sim.Tick, PlayerId = flag.Carrier, Team = flag.Team, Position = flag.Position,
                        TargetId = -1, ColliderId = -1,
                    });
                    flag.Carrier = -1;
                    flag.DownFor = 0f;
                    continue;
                }

                flag.Position = carrier.Position;
                carrier.SprintBlocked = !r.CarrierCanSprint || carrier.Unarmed;
                // Home with it: captured (with their own flag at home, if the rules want that).
                FlagState? own = FlagOf(carrier.Team);
                if (own is not null && Near(carrier.Position, own.Home, r.CaptureRadius) && (!r.NeedOwnFlagHome || own.AtHome))
                {
                    Done = true;
                    CapturedBy = carrier.Team;
                    carrier.SprintBlocked = carrier.Unarmed;
                    sim.Events.Add(new SimEvent
                    {
                        Type = SimEventType.FlagCaptured, Tick = sim.Tick, PlayerId = carrier.Id, Team = carrier.Team, Position = own.Home,
                        Extra = flag.Team, TargetId = -1, ColliderId = -1,
                    });
                    return;
                }

                continue;
            }

            if (flag.Down)
            {
                flag.DownFor += dt;
                if (r.ReturnAfter > 0f && flag.DownFor >= r.ReturnAfter)
                {
                    SendHome(sim, flag, -1);
                    continue;
                }
            }

            // At home or lying where it fell: the first one within reach takes it (the other side) or, lying, sends it
            // home (its own side).
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                Vector3 d = flag.Position - p.Position;
                if (!p.Alive || !p.Present || p.Unarmed || d.X * d.X + d.Z * d.Z > reach2 || MathF.Abs(d.Y) > 1.2f)
                {
                    continue;
                }

                if (p.Team == flag.Team)
                {
                    if (flag.Down && r.ReturnOnTouch)
                    {
                        SendHome(sim, flag, p.Id);
                        break;
                    }

                    continue;
                }

                if (CarriedBy(p.Id) is not null)
                {
                    continue; // one flag at a time
                }

                bool wasHome = flag.AtHome;
                flag.Carrier = p.Id;
                flag.AtHome = false;
                flag.DownFor = 0f;
                p.SprintBlocked = !r.CarrierCanSprint;
                sim.Events.Add(new SimEvent
                {
                    Type = SimEventType.FlagTaken, Tick = sim.Tick, PlayerId = p.Id, Team = flag.Team, Position = flag.Position,
                    Extra = wasHome ? 1 : 0, TargetId = -1, ColliderId = -1,
                });
                break;
            }
        }
    }

    private static void SendHome(SimWorld sim, FlagState flag, int by)
    {
        flag.Position = flag.Home;
        flag.AtHome = true;
        flag.Carrier = -1;
        flag.DownFor = 0f;
        sim.Events.Add(new SimEvent
        {
            Type = SimEventType.FlagReturned, Tick = sim.Tick, PlayerId = by, Team = flag.Team, Position = flag.Home, TargetId = -1, ColliderId = -1,
        });
    }

    private void UpdateVip(SimWorld sim)
    {
        if (Vip < 0 || sim.FindPlayer(Vip) is not { } vip)
        {
            return;
        }

        CasePosition = vip.Position;
        if (!vip.Alive || !vip.Present)
        {
            // Out: the mode gives the round to the defenders.
            VipOut = true;
            return;
        }

        float radius = _rules.Escort.ExitRadius;
        for (int k = 0; k < CountingExits.Count; k++)
        {
            int i = CountingExits[k];
            Vector3 d = Level.Exits[i].Position - vip.Position;
            if (d.X * d.X + d.Z * d.Z <= radius * radius && MathF.Abs(d.Y) < 2f)
            {
                Done = true;
                ExitUsed = i;
                sim.Events.Add(new SimEvent
                {
                    Type = SimEventType.VipEscaped, Tick = sim.Tick, PlayerId = vip.Id, Team = vip.Team, Position = vip.Position, Extra = i,
                    TargetId = -1, ColliderId = -1,
                });
                return;
            }
        }
    }

    private static bool Near(Vector3 a, Vector3 b, float radius)
    {
        Vector3 d = a - b;
        return d.X * d.X + d.Z * d.Z <= radius * radius && MathF.Abs(d.Y) < 2f;
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
                for (int k = 0; k < CountingExits.Count; k++)
                {
                    int i = CountingExits[k];
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
        ContestedFor = now == HoldStatus.Contested ? ContestedFor + dt : 0f;
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
                // Capture the flag and the VIP play from each side's usual starts.
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

/// <summary>Capture the flag: a flag carried home wins for the side that took it; otherwise the last team standing.</summary>
public sealed class FlagMode : IMatchMode
{
    public static readonly FlagMode Instance = new();

    public MatchResult Evaluate(SimWorld sim, MatchState match) =>
        match.Objective is { Done: true, CapturedBy: >= 0 } o ? new MatchResult(RoundEnd.Captured, o.CapturedBy) : LastTeamStandingMode.Instance.Evaluate(sim, match);
}

/// <summary>
/// VIP escort: the VIP out safe wins for the attackers, the VIP put out for the defenders; otherwise the last team
/// standing (the VIP alone, unarmed, still counts as in).
/// </summary>
public sealed class EscortMode : IMatchMode
{
    public static readonly EscortMode Instance = new();

    public MatchResult Evaluate(SimWorld sim, MatchState match) => match.Objective switch
    {
        { Done: true } => new MatchResult(RoundEnd.Escorted, match.Attackers),
        { VipOut: true } => new MatchResult(RoundEnd.VipOut, match.DefendingTeamOf(sim)),
        _ => LastTeamStandingMode.Instance.Evaluate(sim, match),
    };
}
