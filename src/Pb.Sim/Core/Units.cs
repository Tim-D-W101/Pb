namespace Pb.Sim.Core;

/// <summary>
/// Conversions from the human-friendly units used in data files to the SI units used everywhere
/// in code (m, kg, s, Pa, m³, rad).
/// </summary>
public static class Units
{
    public const float MillimetresToMetres = 0.001f;
    public const float GramsToKilograms = 0.001f;
    public const float BarToPascals = 100_000f;
    public const float LitresToCubicMetres = 0.001f;
    public const float DegreesToRadians = MathF.PI / 180f;
    public const float RadiansToDegrees = 180f / MathF.PI;

    /// <summary>1 bar·L = 1e5 Pa × 1e-3 m³ = 100 Pa·m³.</summary>
    public const float BarLitresToPascalCubicMetres = BarToPascals * LitresToCubicMetres;
}
