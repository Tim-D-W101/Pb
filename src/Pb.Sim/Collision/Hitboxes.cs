using System.Numerics;

namespace Pb.Sim.Collision;

/// <summary>Which part of a receiver was hit. Every part is lethal by default (spec §1.2).</summary>
public enum HitboxPart : byte
{
    Body,
    Mask,
    Marker,
    Loader,
    Tank,
}

public struct HitboxHit
{
    public float T;
    public Vector3 Point;
    public Vector3 Normal;
    public int ReceiverId;
    public HitboxPart Part;
    public SurfaceId Surface;
}

/// <summary>
/// Things a ball can eliminate: range targets now, players from Phase 2. Queries take the tick so
/// the server can test against hitboxes rewound to the shooter's view (lag compensation, Phase 3).
/// </summary>
public interface IHitboxWorld
{
    bool SweepSphere(Vector3 from, Vector3 to, float radius, int tick, int ignoreOwnerId, out HitboxHit hit);

    /// <summary>Called for a lethal (unbounced) break so the receiver can record it.</summary>
    void OnLethalHit(in HitboxHit hit, int shooterId, uint shotSequence, int tick);
}
