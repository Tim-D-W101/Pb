using System.Numerics;
using Pb.Sim.Ballistics;
using Pb.Sim.Core;

namespace Pb.Sim.AI;

/// <summary>
/// Where to point a marker to hit a target: it leads a moving target by the ball's time of flight and
/// holds over for the drop, using the same flat-fire maths as the ballistics (accurate to about 1%
/// within ±10° of level, which covers paintball ranges).
/// </summary>
public static class BotAim
{
    /// <summary>Yaw and pitch from <paramref name="eye"/> that hit <paramref name="target"/>, moving at <paramref name="velocity"/>.</summary>
    public static (float Yaw, float Pitch) Solve(SimConfig config, Vector3 eye, Vector3 target, Vector3 velocity)
    {
        ProjectileParams ball = config.Projectile;
        float speed = config.Shot.MuzzleVelocity;
        Vector3 aim = target;
        for (int i = 0; i < 3; i++)
        {
            Vector3 d = aim - eye;
            float flat = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
            aim = target + velocity * FlatFire.TimeOfFlight(ball.DragFactor, speed, flat);
        }

        Vector3 to = aim - eye;
        float distance = MathF.Max(0.5f, MathF.Sqrt(to.X * to.X + to.Z * to.Z));
        (float yaw, float pitch) = ViewAngles.FromDirection(to);
        return (yaw, pitch + MathF.Atan(FlatFire.Drop(ball.DragFactor, ball.Gravity, speed, distance) / distance));
    }

    /// <summary>Wraps an angle into (−π, π].</summary>
    public static float Wrap(float angle)
    {
        while (angle > MathF.PI)
        {
            angle -= MathF.Tau;
        }

        while (angle <= -MathF.PI)
        {
            angle += MathF.Tau;
        }

        return angle;
    }

    /// <summary>Turns <paramref name="current"/> toward <paramref name="target"/> by at most <paramref name="maxStep"/> (radians).</summary>
    public static float TurnTowards(float current, float target, float maxStep)
    {
        float delta = Wrap(target - current);
        return MathF.Abs(delta) <= maxStep ? current + delta : current + MathF.CopySign(maxStep, delta);
    }
}
