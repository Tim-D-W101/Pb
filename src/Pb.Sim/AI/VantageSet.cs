using System.Numerics;
using Pb.Sim.Collision;

namespace Pb.Sim.AI;

/// <summary>
/// How good each cover point is to watch from (M3.3): rays at standing eye height across the side the cover faces
/// measure how far you'd see from where you'd shoot (over low cover, or stepped out past an edge) without leaving the
/// level's bounds, averaged over the arc, plus a bonus for height above the ground, and which way the view is most open. Marksmen hold the best vantage
/// within reach of their start, watching that way, and weigh vantage when they pick cover to fight from. Computed once
/// per level, ignoring doors (they move).
/// </summary>
public sealed class VantageSet
{
    private readonly float[] _scores;
    private readonly float[] _yaws;

    private VantageSet(float[] scores, float[] yaws)
    {
        _scores = scores;
        _yaws = yaws;
    }

    /// <summary>Each cover point's score, by its index in the <see cref="CoverSet"/>: about 0 (boxed in) to 1.3 (high, open).</summary>
    public IReadOnlyList<float> Scores => _scores;

    public float this[int cover] => _scores[cover];

    /// <summary>The way to face from a cover point to see furthest (yaw, radians): the middle of its most open stretch of view.</summary>
    public float FacingYaw(int cover) => _yaws[cover];

    /// <summary>Scores every cover point; rays stop at the sides of <paramref name="bounds"/> (nothing beyond is in play).</summary>
    public static VantageSet Build(CoverSet cover, CollisionWorld world, Aabb? bounds, float eyeHeight, BrainParams b)
    {
        var scores = new float[cover.Points.Count];
        var yaws = new float[cover.Points.Count];
        int rays = Math.Max(1, b.VantageRays);
        float range = b.VantageRange;
        var reach = new float[rays];
        for (int i = 0; i < scores.Length; i++)
        {
            CoverPoint p = cover.Points[i];
            // Ray directions are (sin a, 0, cos a); a yaw faces (−sin y, 0, −cos y), so y = a + π.
            float facing = MathF.Atan2(p.Normal.X, p.Normal.Z) + MathF.PI;
            yaws[i] = facing + MathF.PI;
            if (!p.CanShoot)
            {
                continue; // nowhere to look out from
            }

            Vector3 eye = p.Position + new Vector3(0f, eyeHeight, 0f);
            if (p.Height == CoverHeight.Full)
            {
                eye += p.PeekDirection * CoverSet.PeekStep;
            }

            float seen = 0f;
            for (int k = 0; k < rays; k++)
            {
                float angle = RayAngle(facing, b.VantageArc, k, rays);
                var dir = new Vector3(MathF.Sin(angle), 0f, MathF.Cos(angle));
                float length = bounds is { } box ? MathF.Min(range, Exit(box, eye, dir)) : range;
                reach[k] = length <= 0f ? 0f
                    : (world.SweepSphere(eye, eye + dir * length, 0f, out SweepHit hit, includeDynamic: false) ? hit.T : 1f) * length / range;
                seen += reach[k];
            }

            // Face the most open three neighbouring rays (one long gap between walls isn't a view).
            int best = 0;
            float bestOpen = float.MinValue;
            for (int k = 0; k < rays; k++)
            {
                float open = reach[k] + (k > 0 ? reach[k - 1] : reach[k]) + (k < rays - 1 ? reach[k + 1] : reach[k]);
                if (open > bestOpen + 1e-4f)
                {
                    bestOpen = open;
                    best = k;
                }
            }

            yaws[i] = RayAngle(facing, b.VantageArc, best, rays) + MathF.PI;
            scores[i] = seen / rays + MathF.Max(0f, p.Position.Y) * b.VantageHeightBonus;
        }

        return new VantageSet(scores, yaws);
    }

    private static float RayAngle(float facing, float arc, int k, int rays) => facing + arc * ((k + 0.5f) / rays - 0.5f);

    /// <summary>How far a flat ray from <paramref name="from"/> along <paramref name="dir"/> goes before leaving the box's sides.</summary>
    private static float Exit(Aabb box, Vector3 from, Vector3 dir) =>
        MathF.Max(0f, MathF.Min(Exit(box.Min.X, box.Max.X, from.X, dir.X), Exit(box.Min.Z, box.Max.Z, from.Z, dir.Z)));

    private static float Exit(float min, float max, float from, float dir) =>
        dir > 1e-6f ? (max - from) / dir : dir < -1e-6f ? (min - from) / dir : float.MaxValue;

    /// <summary>
    /// The best-scoring cover point within <paramref name="reach"/> of <paramref name="from"/> (flat distance) that
    /// <paramref name="who"/> can claim, at least <paramref name="awayFrom"/> metres from <paramref name="avoid"/>; −1 if none.
    /// </summary>
    public int Best(CoverSet cover, Vector3 from, float reach, int who, List<int> scratch, Vector3 avoid = default, float awayFrom = 0f)
    {
        cover.Near(from, reach, scratch);
        int best = -1;
        float bestScore = float.MinValue;
        for (int k = 0; k < scratch.Count; k++)
        {
            int i = scratch[k];
            int holder = cover.ClaimedBy(i);
            if ((holder != -1 && holder != who) || _scores[i] <= 0f)
            {
                continue;
            }

            Vector3 at = cover.Points[i].Position;
            if (awayFrom > 0f && Flat(at - avoid) < awayFrom)
            {
                continue;
            }

            // A little less for a long walk to it.
            float score = _scores[i] - Flat(at - from) * 0.004f;
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        return best;
    }

    private static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);
}
