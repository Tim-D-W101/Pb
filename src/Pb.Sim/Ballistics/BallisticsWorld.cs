using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Events;

namespace Pb.Sim.Ballistics;

/// <summary>
/// Advances every live ball one fixed tick: RK2 integration, a swept collision test from the
/// previous to the new position (so nothing tunnels through thin geometry), then break or bounce
/// resolution. Emits events; never allocates after construction.
/// </summary>
public sealed class BallisticsWorld
{
    /// <summary>How far a bounced ball is pushed off the (inflated) surface so the next sweep starts outside it.</summary>
    private const float SeparationEpsilon = 1e-3f;

    public BallisticsWorld(int capacity, ProjectileParams projectile, BreakModel breakModel)
    {
        Pool = new BallPool(capacity);
        Projectile = projectile;
        BreakModel = breakModel;
    }

    public BallPool Pool { get; }

    public ProjectileParams Projectile { get; set; }

    public BreakModel BreakModel { get; set; }

    public ICollisionWorld? World { get; set; }

    public IHitboxWorld? Hitboxes { get; set; }

    /// <summary>Balls leaving this box are removed (the netted boundary).</summary>
    public Aabb Bounds { get; set; } = new(new Vector3(float.MinValue), new Vector3(float.MaxValue));

    /// <summary>Total balls ever spawned (diagnostics).</summary>
    public long SpawnedTotal { get; private set; }

    /// <summary>
    /// Adds a ball at <paramref name="origin"/>. <paramref name="firstStep"/> is how much of the
    /// current tick is left after the shot's sub-tick fire time; the ball's first integration step
    /// uses it so rapid fire stays evenly spaced.
    /// </summary>
    public bool Spawn(Vector3 origin, Vector3 velocity, int owner, uint sequence, byte team, in Pcg32 rng,
        float firstStep, int tick, SimEventQueue events)
    {
        int index = Pool.Add(origin, velocity, owner, sequence, team, rng, firstStep);
        if (index < 0)
        {
            events.Add(new SimEvent
            {
                Type = SimEventType.BallDespawned, Tick = tick, PlayerId = owner, ShotSequence = sequence,
                Team = team, Position = origin, TargetId = -1, ColliderId = -1, Extra = (int)DespawnReason.PoolFull,
            });
            return false;
        }

        SpawnedTotal++;
        events.Add(new SimEvent
        {
            Type = SimEventType.ShotFired, Tick = tick, PlayerId = owner, ShotSequence = sequence, Team = team,
            Position = origin, Velocity = velocity, TargetId = -1, ColliderId = -1,
        });
        return true;
    }

    /// <summary>
    /// The muzzle-in-cover rule: a shot fired with the barrel behind a wall breaks on that wall at
    /// once. Emits the shot and its break without a ball ever flying.
    /// </summary>
    public void BreakAtMuzzle(Vector3 origin, Vector3 velocity, in SweepHit wall, int owner, uint sequence, byte team, int tick,
        SimEventQueue events)
    {
        SpawnedTotal++;
        events.Add(new SimEvent
        {
            Type = SimEventType.ShotFired, Tick = tick, PlayerId = owner, ShotSequence = sequence, Team = team,
            Position = origin, Velocity = velocity, TargetId = -1, ColliderId = -1,
        });
        events.Add(new SimEvent
        {
            Type = SimEventType.BallBroke, Tick = tick, PlayerId = owner, ShotSequence = sequence, Team = team,
            Surface = wall.Surface, Position = wall.Point, Normal = wall.Normal, Velocity = velocity, Value = velocity.Length(),
            TargetId = -1, ColliderId = wall.ColliderId,
        });
    }

    public void Tick(int tick, float dt, SimEventQueue events)
    {
        float k = Projectile.DragFactor;
        Vector3 g = Projectile.GravityVector;
        int i = 0;
        while (i < Pool.Count)
        {
            float step = Pool.FirstStep[i] > 0f ? Pool.FirstStep[i] : dt;
            Pool.FirstStep[i] = 0f;
            if (StepBall(i, step, k, g, tick, events))
            {
                i++;
            }

            // else: the ball was removed and the last one moved into slot i; process it next.
        }
    }

    /// <returns>false if the ball was removed.</returns>
    private bool StepBall(int i, float h, float k, Vector3 g, int tick, SimEventQueue events)
    {
        BallPool pool = Pool;
        Vector3 p = pool.Position[i];
        Vector3 v = pool.Velocity[i];
        pool.PrevPosition[i] = p;

        Vector3 pNew = p;
        Vector3 vNew = v;
        BallisticsIntegrator.Step(ref pNew, ref vNew, k, g, h);

        float radius = Projectile.Radius;
        bool hitWorld = false;
        SweepHit worldHit = default;
        if (World is not null)
        {
            hitWorld = World.SweepSphere(p, pNew, radius, out worldHit);
        }

        bool hitReceiver = false;
        HitboxHit receiverHit = default;
        if (Hitboxes is not null)
        {
            hitReceiver = Hitboxes.SweepSphere(p, pNew, radius, tick, pool.Owner[i], out receiverHit);
        }

        if (hitReceiver && (!hitWorld || receiverHit.T <= worldHit.T))
        {
            Vector3 impactVelocity = Vector3.Lerp(v, vNew, receiverHit.T);
            return Resolve(i, receiverHit.Point, receiverHit.Normal, receiverHit.Surface, impactVelocity,
                receiverHit.ReceiverId, -1, receiverHit, tick, events);
        }

        if (hitWorld)
        {
            Vector3 impactVelocity = Vector3.Lerp(v, vNew, worldHit.T);
            return Resolve(i, worldHit.Point, worldHit.Normal, worldHit.Surface, impactVelocity,
                -1, worldHit.ColliderId, default, tick, events);
        }

        pool.Position[i] = pNew;
        pool.Velocity[i] = vNew;
        pool.Age[i] += h;

        if (pool.Age[i] > Projectile.MaxLifetime)
        {
            Despawn(i, DespawnReason.Lifetime, tick, events);
            return false;
        }

        if (!Bounds.Contains(pNew))
        {
            Despawn(i, DespawnReason.OutOfBounds, tick, events);
            return false;
        }

        return true;
    }

    private bool Resolve(int i, Vector3 center, Vector3 normal, SurfaceId surface, Vector3 impactVelocity,
        int receiverId, int colliderId, in HitboxHit receiverHit, int tick, SimEventQueue events)
    {
        BallPool pool = Pool;
        float radius = Projectile.Radius;
        float speed = impactVelocity.Length();
        float normalSpeed = MathF.Max(0f, -Vector3.Dot(impactVelocity, normal));
        float breakChance = BreakModel.BreakProbability(surface, normalSpeed);
        bool breaks = pool.Rng[i].NextFloat() < breakChance;

        if (breaks)
        {
            bool lethal = receiverId >= 0 && !pool.Bounced[i];
            var e = new SimEvent
            {
                Type = SimEventType.BallBroke, Tick = tick, PlayerId = pool.Owner[i], ShotSequence = pool.Sequence[i],
                Team = pool.Team[i], Surface = surface, Position = center - normal * radius, Normal = normal,
                Velocity = impactVelocity, Value = speed, TargetId = receiverId, ColliderId = colliderId,
                Lethal = lethal, Extra = receiverId >= 0 ? (int)receiverHit.Part : 0,
            };
            events.Add(e);

            if (lethal)
            {
                e.Type = SimEventType.TargetHit;
                events.Add(e);
                Hitboxes?.OnLethalHit(receiverHit, pool.Owner[i], pool.Sequence[i], tick);
            }

            pool.RemoveAt(i);
            return false;
        }

        ref readonly SurfaceResponse response = ref BreakModel.Response(surface);
        Vector3 normalPart = normal * Vector3.Dot(impactVelocity, normal);
        Vector3 tangentPart = impactVelocity - normalPart;
        Vector3 outgoing = tangentPart * response.TangentRetain - normalPart * response.Restitution;

        pool.Position[i] = center + normal * SeparationEpsilon;
        pool.Velocity[i] = outgoing;
        pool.Bounced[i] = true;
        pool.Bounces[i]++;

        events.Add(new SimEvent
        {
            Type = SimEventType.BallBounced, Tick = tick, PlayerId = pool.Owner[i], ShotSequence = pool.Sequence[i],
            Team = pool.Team[i], Surface = surface, Position = center - normal * radius, Normal = normal,
            Velocity = outgoing, Value = speed, TargetId = receiverId, ColliderId = colliderId,
        });

        if (pool.Bounces[i] > BreakModel.MaxBounces || outgoing.Length() < BreakModel.RestSpeed)
        {
            Despawn(i, DespawnReason.Rest, tick, events);
            return false;
        }

        return true;
    }

    private void Despawn(int i, DespawnReason reason, int tick, SimEventQueue events)
    {
        events.Add(new SimEvent
        {
            Type = SimEventType.BallDespawned, Tick = tick, PlayerId = Pool.Owner[i], ShotSequence = Pool.Sequence[i],
            Team = Pool.Team[i], Position = Pool.Position[i], Velocity = Pool.Velocity[i], TargetId = -1,
            ColliderId = -1, Extra = (int)reason,
        });
        Pool.RemoveAt(i);
    }
}
