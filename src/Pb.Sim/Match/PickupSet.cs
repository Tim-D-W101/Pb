using System.Numerics;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Players;

namespace Pb.Sim.Match;

/// <summary>
/// The level's paint and air pickups. Anyone still in the round who walks within the pickup radius
/// takes one, if they have room for it: a pod goes into an empty pod slot (you never carry more
/// pods than the harness holds), air tops up a tank that's below the threshold. Each is taken once.
/// </summary>
public sealed class PickupSet
{
    private readonly List<PickupSpec> _items = new();
    private bool[] _taken = Array.Empty<bool>();

    public IReadOnlyList<PickupSpec> Items => _items;

    /// <summary>Off for tiers without pickups (and on the range).</summary>
    public bool Active { get; set; }

    public bool IsTaken(int index) => _taken[index];

    public void Load(IReadOnlyList<PickupSpec> pickups)
    {
        _items.Clear();
        _items.AddRange(pickups);
        _taken = new bool[_items.Count];
        Active = _items.Count > 0;
    }

    public void Reset() => Array.Clear(_taken);

    /// <summary>A joining copy: whether the server says a pickup has gone.</summary>
    internal void SetTakenFromServer(int index, bool taken) => _taken[index] = taken;

    internal void Update(SimWorld sim, MatchRules rules)
    {
        if (!Active)
        {
            return;
        }

        float r2 = rules.PickupRadius * rules.PickupRadius;
        IReadOnlyList<PlayerState> players = sim.Players;
        for (int k = 0; k < players.Count; k++)
        {
            PlayerState p = players[k];
            if (!p.Alive || !p.Present)
            {
                continue;
            }

            for (int i = 0; i < _items.Count; i++)
            {
                PickupSpec item = _items[i];
                Vector3 d = item.Position - p.Position;
                if (_taken[i] || d.X * d.X + d.Z * d.Z > r2 || MathF.Abs(d.Y) > 1f || !TryGive(p, item.Kind, rules))
                {
                    continue;
                }

                _taken[i] = true;
                sim.Events.Add(new SimEvent
                {
                    Type = SimEventType.PickupTaken, Tick = sim.Tick, PlayerId = p.Id, Team = p.Team, TargetId = i, ColliderId = -1,
                    Position = item.Position, Extra = (int)item.Kind,
                });
            }
        }
    }

    private static bool TryGive(PlayerState p, PickupKind kind, MatchRules rules)
    {
        switch (kind)
        {
            case PickupKind.Pod:
                return p.Marker.Paint.TryAddPod();
            case PickupKind.Air when p.Marker.Air.FillFraction < rules.AirPickupBelow:
                p.Marker.Air.Fill();
                return true;
            default:
                return false;
        }
    }
}
