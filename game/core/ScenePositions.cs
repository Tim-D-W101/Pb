using Pb.Sim;
using Pb.Sim.Ballistics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Players;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>Helpers for scripted scenes (smoke test, demos): where to stand and how to aim at someone.</summary>
public static class ScenePositions
{
    /// <summary>
    /// A clear standing spot about <paramref name="distance"/> from <paramref name="other"/>, in front of them
    /// if possible (someone facing their cover has it to the side or behind): room to stand, ground underfoot
    /// at their level, and a clear line between the two chests.
    /// </summary>
    public static bool FindSpot(SimWorld sim, PlayerState other, float distance, out SVector3 spot)
    {
        foreach (float d in new[] { distance, distance * 0.75f, distance * 1.3f })
        {
            foreach (float turn in new[] { 0f, 0.45f, -0.45f, 0.9f, -0.9f, 1.6f, -1.6f, 2.4f, -2.4f, System.MathF.PI })
            {
                SVector3 at = other.Position + ViewAngles.FlatForward(other.Yaw + turn) * d;
                SVector3 chest = at + new SVector3(0f, 1.2f, 0f);
                SVector3 theirs = other.Position + new SVector3(0f, 1.2f, 0f);
                bool room = !sim.Collision.SweepSphere(at + new SVector3(0f, 0.45f, 0f), at + new SVector3(0f, 1.6f, 0f), 0.34f, out _);
                bool ground = sim.Collision.SweepSphere(at + new SVector3(0f, 0.3f, 0f), at - new SVector3(0f, 0.3f, 0f), 0f, out SweepHit floor) &&
                              System.MathF.Abs(floor.Point.Y - other.Position.Y) < 0.1f;
                bool clear = !sim.Collision.SweepSphere(chest, theirs, 0f, out _);
                bool inside = sim.Level?.Bounds.Contains(chest) ?? true;
                if (room && ground && clear && inside)
                {
                    spot = at;
                    return true;
                }
            }
        }

        spot = default;
        return false;
    }

    /// <summary>The yaw that faces from <paramref name="from"/> towards <paramref name="to"/>.</summary>
    public static float Facing(SVector3 from, SVector3 to) => System.MathF.Atan2(-(to.X - from.X), -(to.Z - from.Z));

    /// <summary>Yaw and pitch at <paramref name="target"/>'s chest, with holdover for the drop.</summary>
    public static (float Yaw, float Pitch) AimAt(SimWorld sim, PlayerState me, PlayerState target)
    {
        SVector3 chest = target.Position + new SVector3(0f, target.EyeHeight * 0.72f, 0f);
        float distance = SVector3.Distance(me.EyePosition, chest);
        (float yaw, float pitch) = ViewAngles.FromDirection(chest - me.EyePosition);
        ProjectileParams ball = sim.Config.Projectile;
        return (yaw, pitch + System.MathF.Atan(FlatFire.Drop(ball.DragFactor, ball.Gravity, sim.Config.Shot.MuzzleVelocity, distance) / distance));
    }
}
