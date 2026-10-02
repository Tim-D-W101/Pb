using System.Numerics;
using Pb.Sim.Collision;

namespace Pb.Sim.Ballistics;

public struct TrajectoryResult
{
    public int PointCount;
    public bool Impact;
    public Vector3 ImpactPoint;
    public Vector3 ImpactNormal;
    public float FlightTime;
}

/// <summary>
/// Predicts one ball's path with the exact integrator and collision used in play, minus randomness.
/// Used by the arc-preview debug overlay and (Phase 2) by bots to lead targets.
/// </summary>
public static class TrajectoryPredictor
{
    public static TrajectoryResult Predict(ProjectileParams projectile, ICollisionWorld? world, IHitboxWorld? hitboxes,
        int tick, Vector3 origin, Vector3 velocity, float dt, float maxTime, Span<Vector3> points)
    {
        var result = new TrajectoryResult();
        float k = projectile.DragFactor;
        Vector3 g = projectile.GravityVector;
        Vector3 p = origin;
        Vector3 v = velocity;
        if (points.Length > 0)
        {
            points[result.PointCount++] = p;
        }

        for (float time = 0f; time < maxTime; time += dt)
        {
            Vector3 pNew = p;
            Vector3 vNew = v;
            BallisticsIntegrator.Step(ref pNew, ref vNew, k, g, dt);

            float best = float.MaxValue;
            Vector3 normal = default;
            if (world is not null && world.SweepSphere(p, pNew, projectile.Radius, out SweepHit sh))
            {
                best = sh.T;
                normal = sh.Normal;
            }

            if (hitboxes is not null && hitboxes.SweepSphere(p, pNew, projectile.Radius, tick, int.MinValue, out HitboxHit hh) && hh.T < best)
            {
                best = hh.T;
                normal = hh.Normal;
            }

            if (best <= 1f)
            {
                Vector3 contact = Vector3.Lerp(p, pNew, best);
                result.Impact = true;
                result.ImpactPoint = contact - normal * projectile.Radius;
                result.ImpactNormal = normal;
                result.FlightTime = time + dt * best;
                if (result.PointCount < points.Length)
                {
                    points[result.PointCount++] = contact;
                }

                return result;
            }

            p = pNew;
            v = vNew;
            if (result.PointCount < points.Length)
            {
                points[result.PointCount++] = p;
            }
        }

        result.FlightTime = maxTime;
        return result;
    }
}
