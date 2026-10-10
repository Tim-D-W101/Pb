using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Events;

namespace Pb.Sim.Players;

/// <summary>
/// Players as hit receivers. Each player's pose is recorded every tick, keeping the last 200 ms for
/// lag compensation, and a ball is swept against the boxes posed for the tick it asks about (its shooter's
/// view of the others: <see cref="BallPool.Rewind"/>). A break that counts eliminates the player on the spot
/// (spec §1.2): any lethal part, never after a bounce, and balls already in the air still count after their
/// shooter is out unless the rules say otherwise. On a joining copy nothing it sees puts anyone out
/// (<see cref="Decides"/>): the server decides.
/// </summary>
public sealed class PlayerHitboxes : IHitboxWorld
{
    /// <summary>Receiver ids for players are this plus the player's id; lower ids are range targets.</summary>
    public const int ReceiverIdBase = 1000;

    /// <summary>Ticks of pose history kept (200 ms at 120 Hz).</summary>
    public const int HistoryTicks = 24;

    /// <summary>Broadphase: every part of a player lies within this distance of a point 1 m above the feet.</summary>
    private const float Reach = 1.6f;

    private readonly SimWorld _sim;
    private readonly List<History> _history = new();

    internal PlayerHitboxes(SimWorld sim)
    {
        _sim = sim;
    }

    /// <summary>Off on the training range: balls pass through players there, as in Phase 1.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether a break that counts puts its player out here. False on a joining copy (<see cref="SimRole.Client"/>):
    /// its balls still break on players and say so, but only the server's word puts anyone out.
    /// </summary>
    public bool Decides { get; set; } = true;

    public static bool IsPlayer(int receiverId) => receiverId >= ReceiverIdBase;

    public static int PlayerIdOf(int receiverId) => receiverId - ReceiverIdBase;

    public static int ReceiverIdOf(PlayerState player) => ReceiverIdBase + player.Id;

    /// <summary>Records every player's pose for <paramref name="tick"/> (after movement, before balls fly).</summary>
    public void Record(int tick)
    {
        IReadOnlyList<PlayerState> players = _sim.Players;
        while (_history.Count < players.Count)
        {
            _history.Add(new History());
        }

        int slot = tick % HistoryTicks;
        for (int i = 0; i < players.Count; i++)
        {
            _history[i].Poses[slot] = HitboxPose.Of(players[i]);
            _history[i].Ticks[slot] = tick;
        }
    }

    /// <summary>The history slot holding player <paramref name="index"/>'s pose for <paramref name="tick"/>, or −1.</summary>
    private int SlotOf(int index, int tick)
    {
        if (index < _history.Count && tick >= 0)
        {
            int slot = tick % HistoryTicks;
            if (_history[index].Ticks[slot] == tick)
            {
                return slot;
            }
        }

        return -1;
    }

    /// <summary>The pose recorded for <paramref name="tick"/>, or the player's current pose if that's not in the history.</summary>
    public HitboxPose PoseAt(int index, int tick)
    {
        int slot = SlotOf(index, tick);
        return slot >= 0 ? _history[index].Poses[slot] : HitboxPose.Of(_sim.Players[index]);
    }

    /// <summary>Poses <paramref name="player"/>'s hitboxes as they are now (for drawing characters).</summary>
    public void PoseNow(PlayerState player, Span<PosedBox> parts) =>
        HitboxRig.Pose(HitboxPose.Of(player), _sim.Config.Hitboxes, _sim.Config.Movement.LeanPivotBelowEye, parts);

    public bool SweepSphere(Vector3 from, Vector3 to, float radius, int tick, int ignoreOwnerId, out HitboxHit hit)
    {
        hit = default;
        if (!Enabled)
        {
            return false;
        }

        IReadOnlyList<PlayerState> players = _sim.Players;
        HitboxParams rig = _sim.Config.Hitboxes;
        float pivot = _sim.Config.Movement.LeanPivotBelowEye;
        Span<PosedBox> parts = stackalloc PosedBox[HitboxRig.PartCount];
        float best = float.MaxValue;
        Vector3 d = to - from;

        for (int i = 0; i < players.Count; i++)
        {
            PlayerState player = players[i];
            if (player.Id == ignoreOwnerId)
            {
                continue;
            }

            // Cheap rejection first, from where the player was at that tick (read in place, so balls asking about
            // different ticks cost no more than balls asking about one); the full pose only when close.
            int slot = SlotOf(i, tick);
            bool present;
            Vector3 feet;
            if (slot >= 0)
            {
                ref readonly HitboxPose recorded = ref _history[i].Poses[slot];
                present = recorded.Present;
                feet = recorded.Position;
            }
            else
            {
                present = player.Present;
                feet = player.Position;
            }

            float reach = Reach + radius;
            if (!present || DistanceSquaredToSegment(feet + new Vector3(0f, 1f, 0f), from, to) > reach * reach)
            {
                continue;
            }

            HitboxPose pose = slot >= 0 ? _history[i].Poses[slot] : HitboxPose.Of(player);

            HitboxRig.Pose(pose, rig, pivot, parts);
            for (int k = 0; k < parts.Length; k++)
            {
                ref readonly PosedBox b = ref parts[k];
                if (BoxShape.SweepOriented(b.Center, b.AxisX, b.AxisY, b.AxisZ, b.HalfExtents, from, d, radius, out float t, out Vector3 n) &&
                    t < best)
                {
                    best = t;
                    hit.Normal = n;
                    hit.ReceiverId = ReceiverIdOf(player);
                    hit.Part = b.Part;
                    hit.Surface = rig.Surface;
                }
            }
        }

        if (best == float.MaxValue)
        {
            return false;
        }

        hit.T = best;
        hit.Point = from + d * best;
        return true;
    }

    public bool CountsAsHit(in HitboxHit hit, int shooterId, int tick)
    {
        HitboxParams rules = _sim.Config.Hitboxes;
        PlayerState? victim = _sim.FindPlayer(PlayerIdOf(hit.ReceiverId));
        if (victim is not { Alive: true } || !rules.IsLethal(hit.Part))
        {
            return false;
        }

        // Balls from a shooter who has since been eliminated count only if the rules say so.
        return rules.BallsInFlightCount || _sim.FindPlayer(shooterId) is not { Alive: false };
    }

    public void OnLethalHit(in HitboxHit hit, int shooterId, uint shotSequence, int tick)
    {
        PlayerState? victim = _sim.FindPlayer(PlayerIdOf(hit.ReceiverId));
        if (victim is null || !victim.Alive || !Decides)
        {
            return;
        }

        victim.Alive = false;
        victim.Sprinting = false;
        victim.EliminatedBy = shooterId;
        victim.EliminatedTick = tick;
        victim.EliminatedPart = hit.Part;
        if (_sim.FindPlayer(shooterId) is { } shooter && shooter.Team != victim.Team)
        {
            shooter.Eliminations++;
        }

        _sim.Events.Add(new SimEvent
        {
            Type = SimEventType.PlayerEliminated, Tick = tick, PlayerId = shooterId, TargetId = victim.Id, Team = victim.Team,
            ShotSequence = shotSequence, Position = hit.Point, Normal = hit.Normal, Extra = (int)hit.Part, ColliderId = -1,
        });
    }

    private static float DistanceSquaredToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        float t = lengthSquared > 0f ? Math.Clamp(Vector3.Dot(p - a, ab) / lengthSquared, 0f, 1f) : 0f;
        return Vector3.DistanceSquared(p, a + ab * t);
    }

    private sealed class History
    {
        public readonly HitboxPose[] Poses = new HitboxPose[HistoryTicks];
        public readonly int[] Ticks = CreateTicks();

        private static int[] CreateTicks()
        {
            var ticks = new int[HistoryTicks];
            Array.Fill(ticks, -1);
            return ticks;
        }
    }
}

/// <summary>Everything a ball can eliminate: range targets and players, behind one interface.</summary>
public sealed class HitReceivers : IHitboxWorld
{
    private readonly IHitboxWorld _targets;
    private readonly PlayerHitboxes _players;

    public HitReceivers(IHitboxWorld targets, PlayerHitboxes players)
    {
        _targets = targets;
        _players = players;
    }

    public bool SweepSphere(Vector3 from, Vector3 to, float radius, int tick, int ignoreOwnerId, out HitboxHit hit)
    {
        bool target = _targets.SweepSphere(from, to, radius, tick, ignoreOwnerId, out HitboxHit targetHit);
        bool player = _players.SweepSphere(from, to, radius, tick, ignoreOwnerId, out HitboxHit playerHit);
        hit = player && (!target || playerHit.T < targetHit.T) ? playerHit : targetHit;
        return target || player;
    }

    public bool CountsAsHit(in HitboxHit hit, int shooterId, int tick) =>
        PlayerHitboxes.IsPlayer(hit.ReceiverId) ? _players.CountsAsHit(hit, shooterId, tick) : _targets.CountsAsHit(hit, shooterId, tick);

    public void OnLethalHit(in HitboxHit hit, int shooterId, uint shotSequence, int tick)
    {
        if (PlayerHitboxes.IsPlayer(hit.ReceiverId))
        {
            _players.OnLethalHit(hit, shooterId, shotSequence, tick);
        }
        else
        {
            _targets.OnLethalHit(hit, shooterId, shotSequence, tick);
        }
    }
}
