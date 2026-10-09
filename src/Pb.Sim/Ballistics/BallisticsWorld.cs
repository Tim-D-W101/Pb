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
    /// uses it so rapid fire stays evenly spaced. <paramref name="rewind"/> is its shooter's lag compensation
    /// (<see cref="BallPool.Rewind"/>); a <paramref name="remote"/> ball is one a joining copy flies for show
    /// (<see cref="BallPool.Remote"/>). The shot's event carries the first step in <see cref="SimEvent.Value"/> and, in
    /// <see cref="SimEvent.Extra"/>, <paramref name="streamDraws"/>: how many numbers the shot drew from its random
    /// stream before the ball took it over, so another copy can fly the same ball.
    /// </summary>
    public bool Spawn(Vector3 origin, Vector3 velocity, int owner, uint sequence, byte team, in Pcg32 rng,
        float firstStep, int tick, SimEventQueue events, byte rewind = 0, bool remote = false, int streamDraws = 0)
    {
        int index = Pool.Add(origin, velocity, owner, sequence, team, rng, firstStep, rewind, remote);
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
            Position = origin, Velocity = velocity, TargetId = -1, ColliderId = -1, Value = firstStep, Extra = streamDraws,
        });
        return true;
    }

    /// <summary>A joining copy: the server says this ball bounced here, so it carries on from there (the server's event says so).</summary>
    public void Redirect(int index, Vector3 position, Vector3 velocity)
    {
        Pool.Position[index] = position;
        Pool.Velocity[index] = velocity;
        Pool.Bounced[index] = true;
        Pool.Bounces[index]++;
    }

    /// <summary>Moves a ball along its flight for <paramref name="h"/> s, hitting nothing (a joining copy catching a shown ball up).</summary>
    public void Advance(int index, float h)
    {
        Vector3 p = Pool.Position[index];
        Vector3 v = Pool.Velocity[index];
        Pool.PrevPosition[index] = p;
        BallisticsIntegrator.Step(ref p, ref v, Projectile.DragFactor, Projectile.GravityVector, h);
        Pool.Position[index] = p;
        Pool.Velocity[index] = v;
        Pool.Age[index] += h;
        Pool.FirstStep[index] = 0f;
    }

    /// <summary>A joining copy: takes a ball out of the air without an event (the server's event says how it ended).</summary>
    public void Remove(int index) => Pool.RemoveAt(index);

    /// <summary>
    /// A joining copy: puts a ball back in the air, already bounced, when the server says it carried on after this copy's
    /// own flight of it ended. No event: the server's bounce is the one heard.
    /// </summary>
    public bool Resume(Vector3 position, Vector3 velocity, int owner, uint sequence, byte team, in Pcg32 rng, bool remote)
    {
        int index = Pool.Add(position, velocity, owner, sequence, team, rng, 0f, 0, remote);
        if (index < 0)
        {
            return false;
        }

        Pool.Bounced[index] = true;
        Pool.Bounces[index] = 1;
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

        // A ball flown for show on a joining copy hits nothing here: the server's events say where it bounced and ended.
        float radius = Projectile.Radius;
        bool remote = pool.Remote[i];
        bool hitWorld = false;
        SweepHit worldHit = default;
        if (World is not null && !remote)
        {
            hitWorld = World.SweepSphere(p, pNew, radius, out worldHit);
        }

        // Players as they were when the shooter saw them (lag compensation).
        bool hitReceiver = false;
        HitboxHit receiverHit = default;
        if (Hitboxes is not null && !remote)
        {
            hitReceiver = Hitboxes.SweepSphere(p, pNew, radius, tick - pool.Rewind[i], pool.Owner[i], out receiverHit);
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
            bool lethal = receiverId >= 0 && !pool.Bounced[i] && Hitboxes!.CountsAsHit(receiverHit, pool.Owner[i], tick);
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
