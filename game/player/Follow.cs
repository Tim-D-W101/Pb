using Godot;

namespace Pb.Game.Player;

/// <summary>
/// Drawn things that follow the sim (a crouch, a lean, a shoulder swap, the hips coming down for the legs' reach) as fast
/// as they can without their pace changing faster than a set acceleration: from one frame to the next their pace changes
/// by at most that × the frame time, so they ease in and out alike at any frame rate (a spring of a set time snapped at
/// low frame rates, or pulled far behind to be smooth there).
/// </summary>
public static class Follow
{
    /// <summary>
    /// Moves <paramref name="shown"/> (at <paramref name="speed"/>) towards <paramref name="target"/> over
    /// <paramref name="dt"/>, as fast as it can without its speed changing faster than <paramref name="accel"/>, and
    /// without running past it. Worked in steps of at most 1/240 s.
    /// </summary>
    public static void Toward(ref float shown, ref float speed, float target, float accel, float dt)
    {
        int steps = Mathf.Clamp(Mathf.CeilToInt(dt * 240f), 1, 64);
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            float off = target - shown;
            // The fastest it can go from here and still stop at the target, but no further than the target this step.
            float want = Mathf.Sign(off) * Mathf.Sqrt(2f * accel * Mathf.Abs(off));
            if (Mathf.Abs(want * h) > Mathf.Abs(off))
            {
                want = off / h;
            }

            speed += Mathf.Clamp(want - speed, -accel * h, accel * h);
            shown += speed * h;
        }
    }
}
