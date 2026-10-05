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
    private float _alarmLeft;

    public BotSquad(SimWorld sim, BotConfig config, NavGrid grid, CoverSet cover)
    {
        Sim = sim;
        Config = config;
        Grid = grid;
        Cover = cover;
        // Paths go round doors standing open out beside their doorways (and through shut ones, which bots open).
        float radius = config.Navigation.AgentRadius;
        grid.Blocked = feet => sim.Doors.OpenLeafAt(feet, radius, sim.Config.Rules.Doors.BotRouteRound);
    }

    public SimWorld Sim { get; }

    public BotConfig Config { get; }

    public NavGrid Grid { get; }

    public CoverSet Cover { get; }

    public IReadOnlyList<BotBrain> Bots => _bots;

    /// <summary>Path searches run so far (for the benchmark).</summary>
    public int SearchesDone { get; private set; }

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
    internal void Share(PlayerState from, int target, Vector3 at) =>
        _incomingContacts.Add(new Contact(from.Id, from.Team, from.EyePosition, target, at));

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

    /// <summary>Takes one of this tick's path searches; false when they're used up (try next tick).</summary>
    internal bool TryReserveSearch(int tick)
    {
        Sync(tick);
        if (_searchesLeft <= 0)
        {
            return false;
        }

        _searchesLeft--;
        SearchesDone++;
        return true;
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
