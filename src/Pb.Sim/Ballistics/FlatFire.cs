namespace Pb.Sim.Ballistics;

/// <summary>
/// Closed-form flat-fire approximation for quadratic drag. It's accurate to about 1% for shots
/// within ±10° of level, which covers normal play. Bots (Phase 2) and the autopilot use it
/// for holdover; the full integrator (<see cref="TrajectoryPredictor"/>) is used where exactness matters.
/// </summary>
public static class FlatFire
{
    /// <summary>Speed after travelling <paramref name="distance"/> (m/s): v = v0·e^(−k·x).</summary>
    public static float Speed(float dragFactor, float muzzleSpeed, float distance) =>
        muzzleSpeed * MathF.Exp(-dragFactor * distance);

    /// <summary>Time to cover <paramref name="distance"/> horizontally (s): (e^(k·x) − 1) / (k·v0).</summary>
    public static float TimeOfFlight(float dragFactor, float muzzleSpeed, float distance) =>
        (MathF.Exp(dragFactor * distance) - 1f) / (dragFactor * muzzleSpeed);

    /// <summary>Drop below the launch line after <paramref name="distance"/> (m).</summary>
    public static float Drop(float dragFactor, float gravity, float muzzleSpeed, float distance)
    {
        float k2 = 2f * dragFactor;
        return gravity / (k2 * muzzleSpeed * muzzleSpeed) * ((MathF.Exp(k2 * distance) - 1f) / k2 - distance);
    }
}
