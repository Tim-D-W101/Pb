using System.Numerics;

namespace Pb.Sim.Ballistics;

/// <summary>Paintball physical properties, SI units. Built from projectiles/*.jsonc.</summary>
public sealed class ProjectileParams
{
    public required float Radius { get; init; }

    public required float Mass { get; init; }

    public required float DragCoefficient { get; init; }

    public required float AirDensity { get; init; }

    /// <summary>Magnitude of gravity (m/s²); it acts along −Y.</summary>
    public required float Gravity { get; init; }

    public required float MaxLifetime { get; init; }

    public float CrossSection => MathF.PI * Radius * Radius;

    /// <summary>k in a = −k·|v|·v + g, i.e. ½·ρ·Cd·A/m (1/m). Defaults give ≈ 0.02115.</summary>
    public float DragFactor => 0.5f * AirDensity * DragCoefficient * CrossSection / Mass;

    public Vector3 GravityVector => new(0f, -Gravity, 0f);

    public float TerminalVelocity => MathF.Sqrt(Gravity / DragFactor);
}

/// <summary>
/// The one integrator every ball uses: explicit midpoint (RK2) on quadratic drag plus gravity.
/// At 120 Hz it tracks a high-precision reference to ~0.01% (semi-implicit Euler is ~4.5% off at 30 m).
/// </summary>
public static class BallisticsIntegrator
{
    public static Vector3 Acceleration(Vector3 velocity, float dragFactor, Vector3 gravity) =>
        -dragFactor * velocity.Length() * velocity + gravity;

    public static void Step(ref Vector3 position, ref Vector3 velocity, float dragFactor, Vector3 gravity, float dt)
    {
        Vector3 a1 = Acceleration(velocity, dragFactor, gravity);
        Vector3 vMid = velocity + a1 * (0.5f * dt);
        Vector3 a2 = Acceleration(vMid, dragFactor, gravity);
        position += vMid * dt;
        velocity += a2 * dt;
    }
}
