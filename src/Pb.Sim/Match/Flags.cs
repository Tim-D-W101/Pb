using System.Numerics;
using Pb.Sim.Events;
using Pb.Sim.Players;

namespace Pb.Sim.Match;

/// <summary>Where a flag is.</summary>
public enum FlagStatus : byte
{
    /// <summary>At its home, as the point started.</summary>
    Home,

    Carried,

    /// <summary>Lying where its carrier went out (it stays there).</summary>
    Dropped,

    /// <summary>Carried to where it scores: the point's won.</summary>
    Captured,
}

/// <summary>
/// Capture the flag's flags. On a field one flag stands in the middle (<see cref="Centre"/>), either side may take it, and
/// a carrier scores at the other side's buzzer station. Elsewhere each side has its own at its base
/// (<see cref="Bases"/>), nobody takes their own, and a carrier scores by bringing the other side's to their own base.
/// A flag is taken by walking up to it (<see cref="FlagRules.PickupReach"/>), carried without sprinting, dropped where its
/// carrier goes out (and left lying there for whoever may take it), and scores within <see cref="FlagRules.ScoreReach"/>
/// of where its carrier's side scores. The authority runs it; a joining copy is told how it stands (<see cref="ApplyServer"/>).
/// </summary>
public sealed class FlagSet
{
    private readonly int[] _owner;
    private readonly Vector3[] _home;
    private readonly Vector3[] _position;
    private readonly int[] _carrier;
    private readonly FlagStatus[] _status;
    private readonly Vector3[] _scoreAt;

    private FlagSet(int[] owners, Vector3[] homes, Vector3[] scoreAt, FlagRules rules)
    {
        _owner = owners;
        _home = homes;
        _position = (Vector3[])homes.Clone();
        _carrier = new int[homes.Length];
        Array.Fill(_carrier, -1);
        _status = new FlagStatus[homes.Length];
        _scoreAt = scoreAt;
        Rules = rules;
    }

    /// <summary>One flag in the middle of a field, at <paramref name="home"/>; side s scores at <paramref name="stations"/>[1 − s].</summary>
    public static FlagSet Centre(Vector3 home, IReadOnlyList<Vector3> stations, FlagRules rules) =>
        new(new[] { -1 }, new[] { home }, new[] { stations[1], stations[0] }, rules);

    /// <summary>A flag for each side at its base (side 0's at <paramref name="base0"/>), each side scoring at its own.</summary>
    public static FlagSet Bases(Vector3 base0, Vector3 base1, FlagRules rules) =>
        new(new[] { 0, 1 }, new[] { base0, base1 }, new[] { base0, base1 }, rules);

    public FlagRules Rules { get; }

    public int Count => _home.Length;

    /// <summary>One flag in the middle that either side may take (on a field), rather than one each.</summary>
    public bool IsCentre => _owner[0] < 0;

    /// <summary>Flag <paramref name="flag"/>'s side (−1: the centre flag, nobody's).</summary>
    public int Owner(int flag) => _owner[flag];

    public Vector3 Home(int flag) => _home[flag];

    /// <summary>Where it is now: at home, with its carrier (at their feet), or where it was dropped.</summary>
    public Vector3 Position(int flag) => _position[flag];

    /// <summary>Who carries it (−1: nobody).</summary>
    public int Carrier(int flag) => _carrier[flag];

    public FlagStatus Status(int flag) => _status[flag];

    /// <summary>Where side <paramref name="side"/>'s carrier scores: the other side's buzzer station, or their own base.</summary>
    public Vector3 ScoreAt(int side) => _scoreAt[side is 0 or 1 ? side : 0];

    /// <summary>The side that captured a flag (−1 while nobody has), the flag, and who carried it home.</summary>
    public int CapturedBy { get; private set; } = -1;

    public int CapturedFlag { get; private set; } = -1;

    public int ScoredBy { get; private set; } = -1;

    /// <summary>The flag <paramref name="playerId"/> carries (−1: none).</summary>
    public int FlagOf(int playerId)
    {
        for (int i = 0; i < _carrier.Length; i++)
        {
            if (_carrier[i] == playerId && playerId >= 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The side's own flag (−1 on a field, where the one flag is nobody's).</summary>
    public int FlagOfSide(int side)
    {
        for (int i = 0; i < _owner.Length; i++)
        {
            if (_owner[i] == side && side >= 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The flag side <paramref name="side"/> goes for: the centre flag, or the other side's.</summary>
    public int TargetOf(int side) => IsCentre ? 0 : FlagOfSide(1 - side);

    /// <summary>Whether <paramref name="player"/> may take flag <paramref name="flag"/> (in and on a side, not their own).</summary>
    public bool MayTake(PlayerState player, int flag) =>
        player.Alive && player.Present && player.Team is 0 or 1 && _owner[flag] != player.Team;

    /// <summary>
    /// After everyone's moved this tick: carried flags follow their carriers (and score where they score, or fall where a
    /// carrier went out), and a flag lying anywhere is taken by the first who may take it within reach.
    /// </summary>
    internal void Update(SimWorld sim)
    {
        if (CapturedBy >= 0)
        {
            return;
        }

        float reach2 = Rules.PickupReach * Rules.PickupReach, score2 = Rules.ScoreReach * Rules.ScoreReach;
        for (int i = 0; i < _home.Length; i++)
        {
            if (_status[i] == FlagStatus.Carried)
            {
                PlayerState? carrier = sim.FindPlayer(_carrier[i]);
                if (carrier is null || !carrier.Alive || !carrier.Present)
                {
                    // Down where its carrier went out, and left there.
                    if (carrier is not null)
                    {
                        _position[i] = carrier.Position;
                        carrier.SprintBlocked = false;
                    }

                    _status[i] = FlagStatus.Dropped;
                    _carrier[i] = -1;
                    Raise(sim, SimEventType.FlagDropped, carrier, i);
                    continue;
                }

                _position[i] = carrier.Position;
                carrier.SprintBlocked = !Rules.CarrierCanSprint;
                Vector3 to = carrier.Position - ScoreAt(carrier.Team);
                if (to.X * to.X + to.Z * to.Z <= score2 && MathF.Abs(to.Y) < 2f)
                {
                    _status[i] = FlagStatus.Captured;
                    CapturedBy = carrier.Team;
                    CapturedFlag = i;
                    ScoredBy = carrier.Id;
                    carrier.SprintBlocked = false;
                    Raise(sim, SimEventType.FlagCaptured, carrier, i);
                    return;
                }

                continue;
            }

            IReadOnlyList<PlayerState> players = sim.Players;
            for (int p = 0; p < players.Count; p++)
            {
                PlayerState player = players[p];
                Vector3 to = player.Position - _position[i];
                if (!MayTake(player, i) || to.X * to.X + to.Z * to.Z > reach2 || MathF.Abs(to.Y) > 1.5f || FlagOf(player.Id) >= 0)
                {
                    continue;
                }

                _status[i] = FlagStatus.Carried;
                _carrier[i] = player.Id;
                player.SprintBlocked = !Rules.CarrierCanSprint;
                Raise(sim, SimEventType.FlagTaken, player, i);
                break;
            }
        }
    }

    /// <summary>A joining copy: how the server says flag <paramref name="flag"/> stands (and who, if anyone, has captured one).</summary>
    internal void ApplyServer(int flag, Vector3 position, int carrier, FlagStatus status, int capturedBy, int scoredBy)
    {
        _position[flag] = position;
        _carrier[flag] = carrier;
        _status[flag] = status;
        if (capturedBy >= 0 && status == FlagStatus.Captured)
        {
            CapturedBy = capturedBy;
            CapturedFlag = flag;
            ScoredBy = scoredBy;
        }
    }

    private void Raise(SimWorld sim, SimEventType type, PlayerState? player, int flag) => sim.Events.Add(new SimEvent
    {
        Type = type, Tick = sim.Tick, PlayerId = player?.Id ?? -1, TargetId = -1, ColliderId = -1, Team = (byte)(player?.Team ?? 0),
        Position = _position[flag], Extra = flag, Value = _owner[flag],
    });
}
