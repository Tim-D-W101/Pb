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
        _incomingContacts.Add(new Contact(from.Id, from.Team, from.EyePosition, target, at));
        ContactsShared++;
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
        bool on = objective is { Done: false } && objective.Kind switch
        {
            ObjectiveKind.Retrieve => objective.CaseMoved,
            ObjectiveKind.Hold => objective.Status is HoldStatus.Ours or HoldStatus.Contested,
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
            AlarmCase(objective);
        }
        else
        {
            _alarmLeft = objective.HoldRules.AlarmInterval;
            AlarmRoom(objective);
        }
    }

    private void AlarmCase(ObjectiveState objective)
    {
        Vector3 at = objective.CasePosition;
        SortDefenders(objective.Attackers, at);
        PlayerState? carrier = objective.Carrier >= 0 ? Sim.FindPlayer(objective.Carrier) : null;

        // The way out nearest the case, guarded from a little inside it.
        IReadOnlyList<ExitSpec> exits = objective.Level.Exits;
        Vector3 exit = exits[0].Position;
        for (int i = 1; i < exits.Count; i++)
        {
            if (Flat(exits[i].Position - at) < Flat(exit - at))
            {
                exit = exits[i].Position;
            }
        }

        Vector3 inward = Flat(at - exit) > 1e-3f ? Vector3.Normalize((at - exit) with { Y = 0f }) : Vector3.UnitZ;
        Vector3 across = new(-inward.Z, 0f, inward.X);
        Vector3 exitPost = exit + inward * Config.Brain.ExitGuardInset;
        int chasers = objective.RetrieveRules.Chasers;
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
    private void Guard(BotBrain bot, Vector3 spot, float yaw) => bot.Guard(Grid.TrySnap(spot, out Vector3 snapped) ? snapped : spot, yaw);

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
