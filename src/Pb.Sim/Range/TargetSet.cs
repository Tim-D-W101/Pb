using System.Numerics;
using Pb.Sim.Collision;

namespace Pb.Sim.Range;

/// <summary>
/// Range targets as hit receivers. Each query moves the ball's segment into the target's local
/// frame, so target shapes are built once and moving targets cost nothing extra. Phase 1 targets
/// move as a pure function of time, so the tick passed in needs no history yet; player hitboxes
/// (Phase 2) implement the same interface with a rewind buffer.
/// </summary>
public sealed class TargetSet : IHitboxWorld
{
    private readonly List<Entry> _entries = new();

    public int Count => _entries.Count;

    public TargetSpec this[int index] => _entries[index].Spec;

    public int HitCount(int index) => _entries[index].Hits;

    public void Load(IReadOnlyList<TargetSpec> targets)
    {
        _entries.Clear();
        foreach (TargetSpec spec in targets)
        {
            var shapes = new Shape[spec.Kind.Parts.Count];
            float reach = 0f;
            for (int i = 0; i < shapes.Length; i++)
            {
                shapes[i] = spec.Kind.Parts[i].CreateLocalShape();
                Aabb b = shapes[i].Bounds;
                reach = MathF.Max(reach, MathF.Max(b.Max.Length(), b.Min.Length()));
            }

            _entries.Add(new Entry(spec, shapes, reach, Quaternion.CreateFromAxisAngle(Vector3.UnitY, spec.Yaw)));
        }
    }

    /// <summary>Moves targets to their positions at <paramref name="time"/> (call once per tick).</summary>
    public void Update(double time)
    {
        foreach (Entry e in _entries)
        {
            e.Position = e.Spec.PositionAt(time);
        }
    }

    public void ResetHits()
    {
        foreach (Entry e in _entries)
        {
            e.Hits = 0;
        }
    }

    public int IndexOf(int receiverId) => receiverId >= 0 && receiverId < _entries.Count ? receiverId : -1;

    public bool SweepSphere(Vector3 from, Vector3 to, float radius, int tick, int ignoreOwnerId, out HitboxHit hit)
    {
        hit = default;
        float best = float.MaxValue;
        Vector3 d = to - from;
        for (int index = 0; index < _entries.Count; index++)
        {
            Entry e = _entries[index];

            // Cheap reject: distance from the target's origin to the segment against its reach.
            if (DistanceSquaredToSegment(e.Position, from, to) > (e.Reach + radius) * (e.Reach + radius))
            {
                continue;
            }

            Vector3 localFrom = Vector3.Transform(from - e.Position, e.InverseRotation);
            Vector3 localD = Vector3.Transform(d, e.InverseRotation);
            for (int p = 0; p < e.Shapes.Length; p++)
            {
                if (e.Shapes[p].Sweep(localFrom, localD, radius, out float t, out Vector3 n) && t < best)
                {
                    best = t;
                    hit.Normal = Vector3.Transform(n, e.Rotation);
                    hit.ReceiverId = index;
                    hit.Part = e.Spec.Kind.Parts[p].Part;
                    hit.Surface = e.Spec.Kind.Surface;
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

    public void OnLethalHit(in HitboxHit hit, int shooterId, uint shotSequence, int tick)
    {
        if (hit.ReceiverId >= 0 && hit.ReceiverId < _entries.Count)
        {
            _entries[hit.ReceiverId].Hits++;
        }
    }

    private static float DistanceSquaredToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        float t = lengthSquared > 0f ? Math.Clamp(Vector3.Dot(p - a, ab) / lengthSquared, 0f, 1f) : 0f;
        return Vector3.DistanceSquared(p, a + ab * t);
    }

    private sealed class Entry
    {
        public Entry(TargetSpec spec, Shape[] shapes, float reach, Quaternion rotation)
        {
            Spec = spec;
            Shapes = shapes;
            Reach = reach;
            Rotation = rotation;
            InverseRotation = Quaternion.Conjugate(rotation);
            Position = spec.BasePosition;
        }

        public TargetSpec Spec { get; }

        public Shape[] Shapes { get; }

        public float Reach { get; }

        public Quaternion Rotation { get; }

        public Quaternion InverseRotation { get; }

        public Vector3 Position { get; set; }

        public int Hits { get; set; }
    }
}
