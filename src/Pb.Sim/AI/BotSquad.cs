using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
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
    private int _syncedTick = int.MinValue;
    private int _searchesLeft;

    public BotSquad(SimWorld sim, BotConfig config, NavGrid grid, CoverSet cover)
    {
        Sim = sim;
        Config = config;
        Grid = grid;
        Cover = cover;
    }

    public SimWorld Sim { get; }

    public BotConfig Config { get; }

    public NavGrid Grid { get; }

    public CoverSet Cover { get; }

    public IReadOnlyList<BotBrain> Bots => _bots;

    /// <summary>Path searches run so far (for the benchmark).</summary>
    public int SearchesDone { get; private set; }

    /// <summary>Builds the navigation grid and cover points for <paramref name="level"/>.</summary>
    public static BotSquad ForLevel(SimWorld sim, BotConfig config, LevelLayout level)
    {
        NavGrid grid = NavGrid.Build(level, config.Navigation);
        CoverSet cover = CoverSet.Build(level, grid, sim.Config.Movement.StandEyeHeight, sim.Config.Movement.CrouchEyeHeight);
        return new BotSquad(sim, config, grid, cover);
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
        _searchesLeft = Config.Navigation.SearchesPerTick;
    }
}
