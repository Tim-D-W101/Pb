using System.Numerics;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;

namespace Pb.Sim.AI;

/// <summary>
/// All the bots in a round and what they share: the level's navigation grid and cover points (with
/// claims, so two bots don't take the same spot), the sounds of the last sim step, and a budget of
/// path searches per tick. The host feeds it each step's events (<see cref="Hear"/>) after stepping,
/// and each bot's <see cref="BotBrain.Think"/> supplies that bot's command for the next step.
/// </summary>
public sealed class BotSquad
{
    private readonly List<BotBrain> _bots = new();
    private readonly SimEventQueue _incoming = new();
    private readonly SimEventQueue _current = new();
    private readonly List<Contact> _incomingContacts = new(32);
    private readonly List<Contact> _currentContacts = new(32);
    private readonly List<BotBrain> _byDistance = new(16);
    private VantageSet? _vantage;
    private int _syncedTick = int.MinValue;
    private int _searchesLeft;
    private int _nodesLeft;

    // The path search under way, carried on over ticks: whose it is, where to, the grid's serial when it began, and the
    // last tick its bot asked (a bot that stops asking, having changed its mind, frees the grid for the others).
    // Who last shouted, and when (ticks): across everyone, on each side, and each thing on each side.
    private const int Kinds = 16;
    private const int Never = int.MinValue / 4;
    private readonly int[] _sideShout = NewTicks(256);
    private readonly int[] _sideKindShout = NewTicks(256 * Kinds);
    private int _squadShout = Never;

    private BotBrain? _searcher;
    private Vector3 _searchGoal;
    private int _searchSerial;
    private int _searcherAsked;
    private float _alarmLeft;

    // Capture the flag: the round whose parts have been dealt, and each flag's alarm clock. VIP escort: the round whose
    // ways out have been given guards.
    private ObjectiveState? _flagsDealtFor;
    private ObjectiveState? _exitsGuardedFor;
    private readonly float[] _flagAlarmLeft = new float[2];

    public BotSquad(SimWorld sim, BotConfig config, NavGrid grid, CoverSet cover)
    {
        Sim = sim;
        Config = config;
        Grid = grid;
        Cover = cover;
        // Paths go round doors standing open out beside their doorways (and through shut ones, which bots open).
        float radius = config.Navigation.AgentRadius;
        Paths = new NavSearch(grid) { Blocked = feet => sim.Doors.OpenLeafAt(feet, radius, sim.Config.Rules.Doors.BotRouteRound) };
        // Only where a leaf can swing (and on its floor) is it worth asking: the rest of the grid never is, which made a
        // search test every door at every place it looked at, three to four times the cost of the search itself.
        var zones = new List<Collision.Aabb>(sim.Doors.Count);
        for (int i = 0; i < sim.Doors.Count; i++)
        {
            Collision.Aabb swing = sim.Doors.Shape(i).Bounds;
            float pad = radius + 0.05f;
            zones.Add(new Collision.Aabb(new Vector3(swing.Min.X - pad, swing.Min.Y - 0.55f, swing.Min.Z - pad),
                new Vector3(swing.Max.X + pad, swing.Min.Y + 1.25f, swing.Max.Z + pad)));
        }

        grid.LimitBlockedTo(zones);
    }

    public SimWorld Sim { get; }

    /// <summary>The level's roads, to drive the vehicles along (built when first asked for).</summary>
    public Vehicles.RoadGraph Roads => _roads ??= Vehicles.RoadGraph.Build(Sim.Level?.Roads ?? Array.Empty<RoadSpec>());

    private Vehicles.RoadGraph? _roads;
    private BotBrain?[] _vehicleClaims = Array.Empty<BotBrain?>();

    /// <summary>Whether <paramref name="bot"/> may set off for vehicle <paramref name="vehicle"/> (nobody else is).</summary>
    public bool MayClaimVehicle(int vehicle, BotBrain bot) =>
        vehicle >= _vehicleClaims.Length || _vehicleClaims[vehicle] is null || _vehicleClaims[vehicle] == bot || !_vehicleClaims[vehicle]!.Self.Alive;

    /// <summary>Marks vehicle <paramref name="vehicle"/> as <paramref name="bot"/>'s to drive, so no other bot sets off for it.</summary>
    public bool ClaimVehicle(int vehicle, BotBrain bot)
    {
        if (_vehicleClaims.Length < Sim.Vehicles.Count)
        {
            Array.Resize(ref _vehicleClaims, Sim.Vehicles.Count);
        }

        if (vehicle < 0 || vehicle >= _vehicleClaims.Length || !MayClaimVehicle(vehicle, bot))
        {
            return false;
        }

        _vehicleClaims[vehicle] = bot;
        return true;
    }

    public void ReleaseVehicle(int vehicle, BotBrain bot)
    {
        if (vehicle >= 0 && vehicle < _vehicleClaims.Length && _vehicleClaims[vehicle] == bot)
        {
            _vehicleClaims[vehicle] = null;
        }
    }

    public BotConfig Config { get; }

    public NavGrid Grid { get; }

    public CoverSet Cover { get; }

    /// <summary>The bots' path searches: this round's own (the grid may be shared), walking round this round's open doors.</summary>
    public NavSearch Paths { get; }

    public IReadOnlyList<BotBrain> Bots => _bots;

    /// <summary>Path searches begun so far (for the benchmark).</summary>
    public int SearchesDone { get; private set; }

    /// <summary>Places the path searches have looked at so far (for the benchmark and tests).</summary>
    public long NodesSearched { get; private set; }

    /// <summary>Lines the bots have shouted aloud so far.</summary>
    public int Shouts { get; private set; }

    /// <summary>Times a bot (or a person's callout key) has told its side where an enemy is.</summary>
    public int ContactsShared { get; private set; }

    private readonly Contact[] _recentContacts = new Contact[RecentContacts];

    /// <summary>Times a teammate in earshot took one of those as a lead (each teammate who heard it counts).</summary>
    public int ContactsHeard
    {
        get
        {
            int heard = 0;
            foreach (BotBrain bot in _bots)
            {
                heard += bot.Senses.ContactsHeard;
            }

            return heard;
        }
    }
    /// <summary>How good each cover point is to watch from (built when first wanted, or by <see cref="ForLevel"/>).</summary>
    public VantageSet Vantage => _vantage ??=
        VantageSet.Build(Cover, Sim.Collision, Sim.Level?.Bounds, Sim.Config.Movement.StandEyeHeight, Config.Brain);

    /// <summary>A bot calling out where an enemy is, for its teammates: who called, from where, about whom, and where they are.</summary>
    public readonly record struct Contact(int From, byte Team, Vector3 FromEye, int TargetId, Vector3 At);

    /// <summary>Builds the navigation grid, cover points and their vantage for <paramref name="level"/>.</summary>
    public static BotSquad ForLevel(SimWorld sim, BotConfig config, LevelLayout level)
    {
        NavGrid grid = NavGrid.Build(level, config.Navigation);
        CoverSet cover = CoverSet.Build(level, grid, sim.Config.Movement.StandEyeHeight, sim.Config.Movement.CrouchEyeHeight);
        var squad = new BotSquad(sim, config, grid, cover);
        _ = squad.Vantage;
        return squad;
    }

    /// <summary>Uses <paramref name="vantage"/> instead of building it (tests share one across arenas).</summary>
    public void UseVantage(VantageSet vantage) => _vantage = vantage;

    /// <summary><paramref name="from"/> calls out that <paramref name="target"/> is at <paramref name="at"/>; teammates hear it next tick.</summary>
    internal void Share(PlayerState from, int target, Vector3 at)
    {
        var contact = new Contact(from.Id, from.Team, from.EyePosition, target, at);
        _incomingContacts.Add(contact);
        _recentContacts[ContactsShared % RecentContacts] = contact;
        ContactsShared++;
    }

    /// <summary>How many of the latest contacts <see cref="RecentContact"/> keeps.</summary>
    public const int RecentContacts = 64;

    /// <summary>
    /// Contact number <paramref name="number"/> (0 is the round's first; <see cref="ContactsShared"/> are numbered so far),
    /// if it's among the last <see cref="RecentContacts"/>: for the game, which marks a teammate's call on your HUD whether
    /// or not it was shouted aloud, and passes it on to joined copies.
    /// </summary>
    public bool RecentContact(int number, out Contact contact)
    {
        bool kept = number >= 0 && number < ContactsShared && number >= ContactsShared - RecentContacts;
        contact = kept ? _recentContacts[number % RecentContacts] : default;
        return kept;
    }

    /// <summary>Whether a call made at <paramref name="fromEye"/> carries to <paramref name="ear"/>: as the bots hear each other (muffled by walls).</summary>
    public bool CallCarries(Vector3 fromEye, Vector3 ear)
    {
        float range = Config.Brain.CalloutRange;
        float d2 = Vector3.DistanceSquared(fromEye, ear);
        if (d2 > range * range)
        {
            return false;
        }

        float muffled = range * Config.Senses.WallMuffle;
        return d2 <= muffled * muffled || !Sim.Collision.SweepSphere(fromEye, ear, 0f, out _);
    }

    /// <summary>
    /// Whether a bot on <paramref name="side"/> may shout <paramref name="kind"/> aloud now: not within the squad's gap
    /// of anyone's last shout, nor (unless it's a call that always goes, <paramref name="always"/>) within the side's gap
    /// of its last, or of a teammate shouting the same. Yes takes the slot.
    /// </summary>
    internal bool TakeShout(byte side, CalloutKind kind, bool always, int tick)
    {
        BrainParams b = Config.Brain;
        float dt = Sim.Dt;
        int k = side * Kinds + Math.Min((int)kind, Kinds - 1);
        if ((tick - _squadShout) * dt < b.SquadCalloutGap ||
            (!always && ((tick - _sideShout[side]) * dt < b.SideCalloutGap || (tick - _sideKindShout[k]) * dt < b.SideCalloutRepeat)))
        {
            return false;
        }

        _squadShout = tick;
        _sideShout[side] = tick;
        _sideKindShout[k] = tick;
        Shouts++;
        return true;
    }

    private static int[] NewTicks(int count)
    {
        var ticks = new int[count];
        Array.Fill(ticks, Never);
        return ticks;
    }

    /// <summary>The contacts called in the last completed step, as of <paramref name="tick"/>.</summary>
    internal ReadOnlySpan<Contact> ContactsFor(int tick)
    {
        Sync(tick);
        return System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_currentContacts);
    }

    /// <summary>Gives <paramref name="player"/> a brain: the spawn's behaviour at the tier's difficulty.</summary>
    public BotBrain Add(PlayerState player, ArchetypeParams archetype, DifficultyParams tier, OpponentSpawn spawn)
    {
        var brain = new BotBrain(this, player, archetype, tier, spawn);
        _bots.Add(brain);
        return brain;
    }

    /// <summary>Called by the host for each event of a step, after the step and before the events are cleared.</summary>
    public void Hear(in SimEvent e)
    {
        switch (e.Type)
        {
            case SimEventType.ShotFired:
            case SimEventType.BallBroke:
            case SimEventType.Footstep:
            case SimEventType.DoorMoved:
            case SimEventType.PlayerEliminated:
                _incoming.Add(e);
                break;
            case SimEventType.CalledOut when e.TargetId >= 0 && Sim.FindPlayer(e.PlayerId) is { } caller:
                // A person's callout key: their side's bots take it as one of their own callouts.
                Share(caller, e.TargetId, e.Position);
                break;
        }
    }

    /// <summary>Hears a whole step's events at once (headless hosts and tests).</summary>
    public void HearAll(ReadOnlySpan<SimEvent> events)
    {
        for (int i = 0; i < events.Length; i++)
        {
            Hear(events[i]);
        }
    }

    /// <summary>The events of the last completed step, as of <paramref name="tick"/>.</summary>
    internal ReadOnlySpan<SimEvent> HeardFor(int tick)
    {
        Sync(tick);
        return _current.Items;
    }

    /// <summary>
    /// <paramref name="bot"/>'s path from <paramref name="from"/> to <paramref name="goal"/>, searched within this tick's
    /// share: <see cref="PathStatus.Found"/> (and <paramref name="path"/> filled in) or <see cref="PathStatus.Failed"/>,
    /// or <see cref="PathStatus.Running"/> while it (or another bot's search before it) is still going: ask again next
    /// tick. One search runs at a time, a few places a tick, so no tick ever pays for a whole long search.
    /// </summary>
    internal PathStatus SearchPath(BotBrain bot, Vector3 from, Vector3 goal, List<Vector3> path, int tick)
    {
        Sync(tick);
        NavSearch paths = Paths;
        if (_searcher is not null && _searcher != bot && (_searcherAsked < tick - 1 || paths.Serial != _searchSerial || !paths.Searching))
        {
            _searcher = null; // its bot moved on (or something else searched meanwhile)
        }

        if (_searcher == bot && (Vector3.DistanceSquared(_searchGoal, goal) > 0.01f || paths.Serial != _searchSerial || !paths.Searching))
        {
            _searcher = null; // somewhere else now: start again
        }

        if (_searcher is null)
        {
            if (_searchesLeft <= 0 || _nodesLeft <= 0)
            {
                return PathStatus.Running; // this tick's searching is used up
            }

            _searchesLeft--;
            SearchesDone++;
            PathStatus begun = paths.Begin(from, goal, path);
            if (begun != PathStatus.Running)
            {
                return begun;
            }

            _searcher = bot;
            _searchGoal = goal;
            _searchSerial = paths.Serial;
        }
        else if (_searcher != bot)
        {
            return PathStatus.Running; // someone else's search first
        }

        _searcherAsked = tick;
        int budget = _nodesLeft;
        PathStatus status = paths.Continue(ref budget, path);
        NodesSearched += _nodesLeft - budget;
        _nodesLeft = budget;
        if (status != PathStatus.Running)
        {
            _searcher = null;
        }

        return status;
    }

    /// <summary>On the first call of a new tick, last step's sounds become current and the search budget refills.</summary>
    private void Sync(int tick)
    {
        if (tick == _syncedTick)
        {
            return;
        }

        _syncedTick = tick;
        _current.Clear();
        ReadOnlySpan<SimEvent> fresh = _incoming.Items;
        for (int i = 0; i < fresh.Length; i++)
        {
            _current.Add(fresh[i]);
        }

        _incoming.Clear();
        _currentContacts.Clear();
        _currentContacts.AddRange(_incomingContacts);
        _incomingContacts.Clear();
        _searchesLeft = Config.Navigation.SearchesPerTick;
        _nodesLeft = Config.Navigation.SearchNodesPerTick;
        UpdateObjective();
    }

    /// <summary>
    /// The objective's alarms for the defending side, once a tick while the round is live. Retrieve, once the case has
    /// been picked up, every alarm interval: the chasers nearest the case go after its carrier (or guard it where it fell)
    /// and the rest make for the way out nearest it. Hold, while your side is in the room: each defender learns where
    /// the nearest of you in it is. The first alarm goes off at once.
    /// </summary>
    private void UpdateObjective()
    {
        ObjectiveState? objective = Sim.IsLive ? Sim.Match?.Objective : null;
        if (objective is { Done: false, Kind: ObjectiveKind.Flag })
        {
            UpdateFlags(objective);
            return;
        }

        if (objective is { Done: false, Kind: ObjectiveKind.Escort } && !ReferenceEquals(_exitsGuardedFor, objective))
        {
            _exitsGuardedFor = objective;
            GuardEscortExits(objective);
        }

        bool on = objective is { Done: false } && objective.Kind switch
        {
            ObjectiveKind.Retrieve => objective.CaseMoved,
            ObjectiveKind.Hold => objective.Status is HoldStatus.Ours or HoldStatus.Contested,
            // The VIP: the defenders hear where they are once the round has been going a while.
            ObjectiveKind.Escort => !objective.VipOut && Sim.Match!.Elapsed >= objective.EscortRules.AlarmAfter,
            _ => false,
        };
        if (!on)
        {
            _alarmLeft = 0f;
            return;
        }

        _alarmLeft -= Sim.Dt;
        if (_alarmLeft > 0f)
        {
            return;
        }

        if (objective!.Kind == ObjectiveKind.Retrieve)
        {
            _alarmLeft = objective.RetrieveRules.AlarmInterval;
            AlarmCase(objective, objective.RetrieveRules.Chasers);
        }
        else if (objective.Kind == ObjectiveKind.Escort)
        {
            _alarmLeft = objective.EscortRules.AlarmInterval;
            AlarmCase(objective, objective.EscortRules.Chasers);
        }
        else
        {
            _alarmLeft = objective.HoldRules.AlarmInterval;
            AlarmRoom(objective);
            if (objective.Status == HoldStatus.Contested && objective.HoldRules.StalemateAfter > 0f &&
                objective.ContestedFor >= objective.HoldRules.StalemateAfter)
            {
                BreakStalemate(objective);
            }
        }
    }

    /// <summary>
    /// The room contested too long without a break: each of the attacking side's bots in it (or at its door) is told where
    /// the nearest of the defenders in it is, and goes for them (<see cref="BotBrain"/>, holding the room).
    /// </summary>
    private void BreakStalemate(ObjectiveState objective)
    {
        HoldRoom room = objective.Room!;
        IReadOnlyList<PlayerState> players = Sim.Players;
        foreach (BotBrain bot in _bots)
        {
            if (!bot.Self.Alive || bot.Self.Team != objective.Attackers || !room.Contains(bot.Self.Position + new Vector3(0f, 0.1f, 0f)))
            {
                continue;
            }

            PlayerState? nearest = null;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                if (p.Alive && p.Team != objective.Attackers && room.Contains(p.Position + new Vector3(0f, 0.1f, 0f)) &&
                    (nearest is null || Vector3.DistanceSquared(p.Position, bot.Self.Position) < Vector3.DistanceSquared(nearest.Position, bot.Self.Position)))
                {
                    nearest = p;
                }
            }

            if (nearest is not null)
            {
                bot.Told(nearest);
            }
        }
    }

    /// <summary>
    /// Capture the flag, once a tick while the round is live. At the start each side's bots are dealt their parts: the
    /// share of them nearest their base (at least one) guard it, facing the other side's, and the rest go for theirs.
    /// Then: a side's fallen flag is sent home by the nearest of that side still in; and while a side's flag is carried
    /// off, the chasers of that side nearest the carrier go after them, told where they are every alarm interval.
    /// </summary>
    private void UpdateFlags(ObjectiveState objective)
    {
        FlagRules r = objective.FlagRules;
        if (!ReferenceEquals(_flagsDealtFor, objective))
        {
            _flagsDealtFor = objective;
            DealFlagParts(objective);
        }

        foreach (FlagState flag in objective.Flags)
        {
            KeepAttacking(flag.Team, objective);
        }

        for (int f = 0; f < objective.Flags.Count && f < _flagAlarmLeft.Length; f++)
        {
            FlagState flag = objective.Flags[f];
            int returner = -1;
            if (flag.Down)
            {
                SortTeam(flag.Team, flag.Position);
                returner = _byDistance.Count > 0 ? _byDistance[0].Self.Id : -1;
            }

            foreach (BotBrain bot in _bots)
            {
                if (bot.Self.Team == flag.Team)
                {
                    bot.SetFlagDuty(bot.FlagAttacker, bot.Self.Id == returner);
                }
            }

            if (flag.Carrier >= 0 && Sim.FindPlayer(flag.Carrier) is { Alive: true } carrier)
            {
                _flagAlarmLeft[f] -= Sim.Dt;
                if (_flagAlarmLeft[f] <= 0f)
                {
                    _flagAlarmLeft[f] = r.AlarmInterval;
                    SortTeam(flag.Team, carrier.Position);
                    for (int k = 0; k < r.Chasers && k < _byDistance.Count; k++)
                    {
                        _byDistance[k].Alarm(carrier);
                    }
                }
            }
            else
            {
                _flagAlarmLeft[f] = 0f;
            }
        }
    }

    /// <summary>
    /// VIP escort, at the start: the share of the defenders that guard the ways out that count, each sent to the way out
    /// nearest them in turn (so they spread across them), posted a little inside it, facing in.
    /// </summary>
    private void GuardEscortExits(ObjectiveState objective)
    {
        IReadOnlyList<int> exits = objective.CountingExits;
        if (exits.Count == 0)
        {
            return;
        }

        SortDefenders(objective.Attackers, objective.VipStart);
        int guards = _byDistance.Count == 0 ? 0 : Math.Clamp((int)MathF.Round(_byDistance.Count * objective.EscortRules.ExitGuardShare), 1, _byDistance.Count);
        // The defenders farthest from the VIP (nearest the far ways out) go: the last of the sorted list.
        for (int g = 0; g < guards; g++)
        {
            BotBrain bot = _byDistance[_byDistance.Count - 1 - g];
            Vector3 exit = objective.Level.Exits[exits[g % exits.Count]].Position;
            Vector3 inward = Flat(objective.VipStart - exit) > 1e-3f ? Vector3.Normalize((objective.VipStart - exit) with { Y = 0f }) : Vector3.UnitZ;
            Vector3 across = new(-inward.Z, 0f, inward.X);
            float side = (g / exits.Count) * (g / exits.Count % 2 == 0 ? 1f : -1f);
            Guard(bot, exit + inward * Config.Brain.ExitGuardInset + across * side * Config.Brain.GuardSpacing, YawOf(inward), shout: false);
        }
    }

    /// <summary>
    /// Capture the flag: a side whose attackers are all out sends guards after their flag instead (all but the share
    /// that guards, of those still in; one left alone goes itself), the ones nearest the other base first; otherwise
    /// nobody would ever go for it again and the round would sit out the clock.
    /// </summary>
    private void KeepAttacking(byte team, ObjectiveState objective)
    {
        int alive = 0, attackers = 0;
        foreach (BotBrain bot in _bots)
        {
            if (bot.Self.Alive && bot.Self.Team == team && !bot.Self.Unarmed)
            {
                alive++;
                attackers += bot.FlagAttacker ? 1 : 0;
            }
        }

        int guards = alive <= 1 ? 0 : Math.Clamp((int)MathF.Round(alive * objective.FlagRules.DefendShare), 1, alive - 1);
        int wanted = alive - guards;
        if (attackers >= wanted || objective.TheirFlag(team) is not { } theirs)
        {
            return;
        }

        SortTeam(team, theirs.Home);
        for (int k = 0; k < _byDistance.Count && attackers < wanted; k++)
        {
            BotBrain bot = _byDistance[k];
            if (!bot.FlagAttacker)
            {
                bot.SetFlagDuty(attack: true, returner: false);
                attackers++;
            }
        }
    }

    /// <summary>Capture the flag: each side's guards (nearest its base, posted round it facing the other base) and attackers.</summary>
    private void DealFlagParts(ObjectiveState objective)
    {
        FlagRules r = objective.FlagRules;
        foreach (FlagState flag in objective.Flags)
        {
            FlagState? theirs = objective.TheirFlag(flag.Team);
            Vector3 toward = theirs is not null && Flat(theirs.Home - flag.Home) > 1e-3f
                ? Vector3.Normalize((theirs.Home - flag.Home) with { Y = 0f })
                : Vector3.UnitZ;
            Vector3 across = new(-toward.Z, 0f, toward.X);
            SortTeam(flag.Team, flag.Home);
            int guards = _byDistance.Count == 0 ? 0 : Math.Clamp((int)MathF.Round(_byDistance.Count * r.DefendShare), 1, _byDistance.Count);
            for (int k = 0; k < _byDistance.Count; k++)
            {
                BotBrain bot = _byDistance[k];
                bool guard = k < guards;
                bot.SetFlagDuty(attack: !guard, returner: false);
                if (guard)
                {
                    // Either side of the base, across the way they'll come from (not on it: the side's attackers set off
                    // that way), facing it.
                    float side = (k / 2 + 0.5f) * (k % 2 == 0 ? 1f : -1f);
                    Guard(bot, flag.Home + across * side * Config.Brain.GuardSpacing, YawOf(toward), shout: false);
                }
            }
        }
    }

    /// <summary>The bots of <paramref name="team"/> still in, nearest <paramref name="at"/> first (no allocation).</summary>
    private void SortTeam(byte team, Vector3 at)
    {
        _byDistance.Clear();
        foreach (BotBrain bot in _bots)
        {
            if (!bot.Self.Alive || bot.Self.Team != team || bot.Self.Unarmed)
            {
                continue;
            }

            float d = Flat(bot.Self.Position - at);
            int k = _byDistance.Count;
            _byDistance.Add(bot);
            while (k > 0 && Flat(_byDistance[k - 1].Self.Position - at) > d)
            {
                _byDistance[k] = _byDistance[k - 1];
                k--;
            }

            _byDistance[k] = bot;
        }
    }

    private void AlarmCase(ObjectiveState objective, int chasers)
    {
        Vector3 at = objective.CasePosition;
        SortDefenders(objective.Attackers, at);
        PlayerState? carrier = objective.Carrier >= 0 ? Sim.FindPlayer(objective.Carrier) : null;

        // The way out nearest the case (of those it can leave by), guarded from a little inside it.
        Vector3 exit = objective.NearestExit(at).Position;

        Vector3 inward = Flat(at - exit) > 1e-3f ? Vector3.Normalize((at - exit) with { Y = 0f }) : Vector3.UnitZ;
        Vector3 across = new(-inward.Z, 0f, inward.X);
        Vector3 exitPost = exit + inward * Config.Brain.ExitGuardInset;
        for (int k = 0; k < _byDistance.Count; k++)
        {
            BotBrain bot = _byDistance[k];
            if (k < chasers)
            {
                if (carrier is { Alive: true })
                {
                    bot.Alarm(carrier);
                }
                else
                {
                    Guard(bot, at + across * (k == 0 ? -1f : 1f) * Config.Brain.GuardSpacing * 0.5f, YawOf(inward));
                }
            }
            else
            {
                int slot = k - chasers;
                float side = (slot + 1) / 2 * (slot % 2 == 0 ? 1f : -1f);
                Guard(bot, exitPost + across * side * Config.Brain.GuardSpacing, YawOf(inward));
            }
        }
    }

    private void AlarmRoom(ObjectiveState objective)
    {
        HoldRoom room = objective.Room!;
        IReadOnlyList<PlayerState> players = Sim.Players;
        foreach (BotBrain bot in _bots)
        {
            if (!bot.Self.Alive || bot.Self.Team == objective.Attackers)
            {
                continue;
            }

            PlayerState? nearest = null;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                if (p.Alive && p.Team == objective.Attackers && room.Contains(p.Position + new Vector3(0f, 0.1f, 0f)) &&
                    (nearest is null || Flat(p.Position - bot.Self.Position) < Flat(nearest.Position - bot.Self.Position)))
                {
                    nearest = p;
                }
            }

            if (nearest is not null)
            {
                bot.Alarm(nearest);
            }
        }
    }

    /// <summary>A defender's post on the walkable ground nearest <paramref name="spot"/>.</summary>
    private void Guard(BotBrain bot, Vector3 spot, float yaw, bool shout = true) =>
        bot.Guard(Grid.TrySnap(spot, out Vector3 snapped) ? snapped : spot, yaw, shout);

    /// <summary>The defenders still in, nearest <paramref name="at"/> first (an insertion sort into a kept list: no allocation).</summary>
    private void SortDefenders(byte attackers, Vector3 at)
    {
        _byDistance.Clear();
        foreach (BotBrain bot in _bots)
        {
            if (!bot.Self.Alive || bot.Self.Team == attackers)
            {
                continue;
            }

            float d = Flat(bot.Self.Position - at);
            int k = _byDistance.Count;
            _byDistance.Add(bot);
            while (k > 0 && Flat(_byDistance[k - 1].Self.Position - at) > d)
            {
                _byDistance[k] = _byDistance[k - 1];
                k--;
            }

            _byDistance[k] = bot;
        }
    }

    private static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

    private static float YawOf(Vector3 direction) => MathF.Atan2(-direction.X, -direction.Z);
}
