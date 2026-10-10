using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
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

    // Speedball: whether this point's places have been dealt, when a side next looks at moving up and at the buzzers,
    // the dealer's random numbers, and a side's bots (kept, so dealing allocates nothing).
    private readonly List<BotBrain> _side = new(16);
    private readonly List<int> _found = new(64);
    private bool _dealt;
    private float _advanceLeft;
    private float _hangLeft;
    private Pcg32 _dealer;

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
        UpdateSpeedball();
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

    /// <summary>
    /// Speedball, once a tick while a point is on: at the horn each side's bots are dealt their places and bunkers; then a
    /// side ahead by the margin moves one of its bots up a bunker now and then, and one goes to hang the other side's
    /// buzzer when none of that side still in can see it.
    /// </summary>
    private void UpdateSpeedball()
    {
        if (Sim.Match is not { Buzzers: { HungSide: < 0 } buzzers } || Sim.Level?.FieldLayout is not { } layout || !Sim.IsLive)
        {
            return;
        }

        SpeedballBrain rules = Config.Brain.Speedball;
        if (!_dealt)
        {
            _dealt = true;
            _dealer = new Pcg32(SeedHash.Combine(Sim.MatchSeed, 0x5BA11UL));
            Deal(layout, 0);
            Deal(layout, 1);
            _advanceLeft = rules.AdvanceEvery;
            _hangLeft = rules.HangCheck;
            return;
        }

        if ((_advanceLeft -= Sim.Dt) <= 0f)
        {
            _advanceLeft = rules.AdvanceEvery;
            Advance(layout);
        }

        if ((_hangLeft -= Sim.Dt) <= 0f)
        {
            _hangLeft = rules.HangCheck;
            SendToHang(buzzers, 0);
            SendToHang(buzzers, 1);
        }
    }

    /// <summary>Side <paramref name="side"/>'s speedball bots still in, into <see cref="_side"/>.</summary>
    private void GatherSide(int side)
    {
        _side.Clear();
        foreach (BotBrain bot in _bots)
        {
            if (bot.Self.Alive && bot.Self.Team == side && bot.Archetype.Idle == BotIdle.Speedball)
            {
                _side.Add(bot);
            }
        }
    }

    /// <summary>
    /// The breakout: side <paramref name="side"/>'s bots (in a shuffled order) get their places, the back and middle
    /// shares of the side (at least one each with three or more, and at least one up front), and a bunker each; all but
    /// the back sprint for theirs.
    /// </summary>
    private void Deal(FieldLayoutSpec layout, int side)
    {
        GatherSide(side);
        int n = _side.Count;
        for (int i = n - 1; i > 0; i--)
        {
            int j = (int)(_dealer.NextUInt() % (uint)(i + 1));
            (_side[i], _side[j]) = (_side[j], _side[i]);
        }

        SpeedballBrain rules = Config.Brain.Speedball;
        int back = n >= 3 ? Math.Max(1, (int)MathF.Round(n * rules.BackShare)) : 0;
        int mid = n >= 3 ? Math.Max(1, (int)MathF.Round(n * rules.MidShare)) : n == 2 ? 1 : 0;
        mid = Math.Min(mid, Math.Max(0, n - back - 1));
        for (int k = 0; k < n; k++)
        {
            // The back covers the lanes at the horn (it fights whoever it sees running, then takes its bunker); the
            // rest sprint for theirs.
            SpeedballPlace place = k < back ? SpeedballPlace.Back : k < back + mid ? SpeedballPlace.Mid : SpeedballPlace.Front;
            Send(_side[k], layout, place, breakout: place != SpeedballPlace.Back);
        }
    }

    /// <summary>
    /// <paramref name="bot"/> to a bunker for <paramref name="place"/>: one of its side's (or on the halfway line) tagged for
    /// the place that nobody of its side plays from, spread over the snake side, the wedge side and the centre; failing
    /// one, any free bunker of its side. It plays from a free cover point there on its side's side of the bunker.
    /// </summary>
    private void Send(BotBrain bot, FieldLayoutSpec layout, SpeedballPlace place, bool breakout)
    {
        int side = bot.Self.Team;
        // Deep is the other half's front bunkers, then its middle ones.
        int half = place == SpeedballPlace.Deep ? 1 - side : side;
        string tag = place switch { SpeedballPlace.Back => "back", SpeedballPlace.Mid => "mid", _ => "front" };
        ulong tried = 0; // bunkers with no cover point left to take (a layout has fewer than 64)
        while (true)
        {
            int best = -1;
            float bestScore = float.MinValue;
            for (int i = 0; i < layout.Bunkers.Count && i < 64; i++)
            {
                FieldBunker b = layout.Bunkers[i];
                // The halfway line's bunkers are either side's front.
                bool there = b.Side == half || (b.Side == -1 && place != SpeedballPlace.Deep);
                if (!there || (tried & (1UL << i)) != 0 || PlayedFrom(i, side, bot))
                {
                    continue;
                }

                float score = (b.Has(tag) ? 10f : place == SpeedballPlace.Deep && b.Has("mid") ? 5f : 0f) - SameSide(layout, b, side, bot) * 3f +
                              _dealer.NextFloat();
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            if (best < 0)
            {
                return;
            }

            int cover = CoverAt(layout, best, side, bot.Self.Id);
            if (cover >= 0)
            {
                bot.Place = place;
                bot.PlayFrom(best, cover, breakout);
                return;
            }

            tried |= 1UL << best;
        }
    }

    /// <summary>Whether a teammate of <paramref name="bot"/>'s (still in) plays from bunker <paramref name="bunker"/>.</summary>
    private bool PlayedFrom(int bunker, int side, BotBrain bot)
    {
        foreach (BotBrain other in _bots)
        {
            if (other != bot && other.Self.Alive && other.Self.Team == side && other.Bunker == bunker)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>How many of <paramref name="bot"/>'s teammates play on the same side of the field as bunker <paramref name="b"/>.</summary>
    private int SameSide(FieldLayoutSpec layout, FieldBunker b, int side, BotBrain bot)
    {
        int count = 0;
        foreach (BotBrain other in _bots)
        {
            if (other != bot && other.Self.Alive && other.Self.Team == side && other.Bunker >= 0 && other.Bunker < layout.Bunkers.Count)
            {
                FieldBunker theirs = layout.Bunkers[other.Bunker];
                count += (theirs.Has("snake") && b.Has("snake")) || (theirs.Has("wedge") && b.Has("wedge")) || (theirs.Has("centre") && b.Has("centre")) ? 1 : 0;
            }
        }

        return count;
    }

    /// <summary>
    /// A free cover point at bunker <paramref name="bunker"/> on side <paramref name="side"/>'s side of it (claimed for
    /// <paramref name="who"/>), or −1: the one facing most squarely back towards the side's start box.
    /// </summary>
    private int CoverAt(FieldLayoutSpec layout, int bunker, int side, int who)
    {
        FieldBunker b = layout.Bunkers[bunker];
        Cover.Near(b.Position, 8f, _found);
        float home = side == 0 ? 1f : -1f;
        int best = -1;
        float bestScore = float.MinValue;
        for (int k = 0; k < _found.Count; k++)
        {
            int i = _found[k];
            CoverPoint p = Cover.Points[i];
            int holder = Cover.ClaimedBy(i);
            float facing = p.Normal.Z * home;
            if ((holder != -1 && holder != who) || facing < 0.3f || !AtBunker(bunker, p.Position))
            {
                continue;
            }

            float score = facing * 2f + (p.Height == CoverHeight.Full ? 0.5f : 0f) - Flat(p.Position - b.Position) * 0.2f;
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        return best >= 0 && Cover.Claim(best, who) ? best : -1;
    }

    /// <summary>Whether <paramref name="at"/> is by bunker <paramref name="bunker"/> of the field's layout (within a metre of one of its pieces).</summary>
    internal bool AtBunker(int bunker, Vector3 at)
    {
        if (Sim.Level is not { FieldLayout: { } layout } level || bunker < 0 || bunker >= layout.Bunkers.Count)
        {
            return false;
        }

        PropInstance prop = level.Props[layout.Bunkers[bunker].PropIndex];
        for (int i = prop.FirstPrimitive; i < prop.FirstPrimitive + prop.PrimitiveCount; i++)
        {
            Aabb box = level.Primitives[i].Bounds;
            if (at.X > box.Min.X - 1f && at.X < box.Max.X + 1f && at.Z > box.Min.Z - 1f && at.Z < box.Max.Z + 1f)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A side with the margin's more players in moves one bot up a bunker: from the middle up front, else from the back;
    /// with nobody left behind the front, one from the front on into the other half.
    /// </summary>
    internal void Advance(FieldLayoutSpec layout)
    {
        int south = 0, north = 0;
        foreach (PlayerState p in Sim.Players)
        {
            if (p.Alive)
            {
                south += p.Team == 0 ? 1 : 0;
                north += p.Team == 1 ? 1 : 0;
            }
        }

        int margin = Config.Brain.Speedball.AdvanceMargin;
        for (int side = 0; side < 2; side++)
        {
            int lead = side == 0 ? south - north : north - south;
            if (lead < margin)
            {
                continue;
            }

            GatherSide(side);
            BotBrain? up = null;
            foreach (BotBrain bot in _side)
            {
                if (bot.Hanging || bot.BreakingOut || bot.Place is SpeedballPlace.None or SpeedballPlace.Deep)
                {
                    continue;
                }

                // The middle moves up first (the back stays covering the lanes longest), then the back, then the front.
                if (up is null || Rank(bot.Place) > Rank(up.Place))
                {
                    up = bot;
                }
            }

            if (up is not null)
            {
                SpeedballPlace next = up.Place switch
                {
                    SpeedballPlace.Back => SpeedballPlace.Mid,
                    SpeedballPlace.Mid => SpeedballPlace.Front,
                    _ => SpeedballPlace.Deep,
                };
                Send(up, layout, next, breakout: false);
            }
        }
    }

    /// <summary>Which places move up first: the middle, then the back, then the front.</summary>
    private static int Rank(SpeedballPlace place) => place switch
    {
        SpeedballPlace.Mid => 3,
        SpeedballPlace.Back => 2,
        SpeedballPlace.Front => 1,
        _ => 0,
    };

    /// <summary>
    /// Side <paramref name="side"/> looks at the other side's buzzer: if none of that side still in can see its station,
    /// its bot nearest the station goes to hang it (unless one already is).
    /// </summary>
    private void SendToHang(BuzzerSet buzzers, int side)
    {
        int theirs = 1 - side;
        Vector3 station = buzzers.Post(theirs) + new Vector3(0f, 1.2f, 0f);
        BotBrain? nearest = null;
        foreach (BotBrain bot in _bots)
        {
            if (!bot.Self.Alive || bot.Self.Team != side || bot.Archetype.Idle != BotIdle.Speedball)
            {
                continue;
            }

            if (bot.Hanging)
            {
                return;
            }

            if (nearest is null || Flat(bot.Self.Position - station) < Flat(nearest.Self.Position - station))
            {
                nearest = bot;
            }
        }

        if (nearest is null)
        {
            return;
        }

        foreach (PlayerState p in Sim.Players)
        {
            if (p.Alive && p.Present && p.Team == theirs && !Sim.Collision.SweepSphere(p.EyePosition, station, 0.02f, out _))
            {
                return; // someone of theirs watches it
            }
        }

        nearest.HangBuzzer();
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
