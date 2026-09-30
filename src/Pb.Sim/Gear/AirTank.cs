namespace Pb.Sim.Gear;

public sealed class AirParams
{
    /// <summary>Tank volume (m³).</summary>
    public required float Volume { get; init; }

    /// <summary>Full-fill pressure (Pa).</summary>
    public required float FillPressure { get; init; }

    /// <summary>Regulator output (Pa). Below this the marker sees tank pressure and velocity falls.</summary>
    public required float RegulatorPressure { get; init; }

    /// <summary>Below this pressure the marker can't cycle (Pa).</summary>
    public required float CutoffPressure { get; init; }

    /// <summary>Gas drawn per shot (Pa·m³).</summary>
    public required float GasPerShot { get; init; }

    /// <summary>Velocity factor = ((p − cutoff)/(regulator − cutoff))^exponent below the regulator.</summary>
    public required float VelocityExponent { get; init; }

    /// <summary>HUD warning threshold (Pa).</summary>
    public required float LowWarningPressure { get; init; }

    /// <summary>Pressure drop per shot (Pa).</summary>
    public float PressureDropPerShot => GasPerShot / Volume;

    /// <summary>Shots at full velocity from a fresh fill.</summary>
    public float FullVelocityShots => (FillPressure - RegulatorPressure) / PressureDropPerShot;
}

/// <summary>Compressed-air tank bookkeeping (spec §1.4): each shot drops pressure; low pressure means slow balls.</summary>
public sealed class AirTank
{
    public AirTank(AirParams parameters)
    {
        Params = parameters;
        Pressure = parameters.FillPressure;
    }

    public AirParams Params { get; private set; }

    /// <summary>Current tank pressure (Pa).</summary>
    public float Pressure { get; private set; }

    public bool CanFire => Pressure > Params.CutoffPressure;

    public bool BelowRegulator => Pressure < Params.RegulatorPressure;

    /// <summary>Muzzle velocity multiplier for the next shot, 1 at or above regulator pressure.</summary>
    public float VelocityFactor
    {
        get
        {
            if (Pressure >= Params.RegulatorPressure)
            {
                return 1f;
            }

            if (Pressure <= Params.CutoffPressure)
            {
                return 0f;
            }

            float x = (Pressure - Params.CutoffPressure) / (Params.RegulatorPressure - Params.CutoffPressure);
            return MathF.Pow(x, Params.VelocityExponent);
        }
    }

    /// <summary>Tank pressure as a 0..1 fraction of a full fill (drives the shot sound's pitch).</summary>
    public float FillFraction => Math.Clamp(Pressure / Params.FillPressure, 0f, 1f);

    public void ConsumeShot() => Pressure = MathF.Max(0f, Pressure - Params.PressureDropPerShot);

    public void Fill() => Pressure = Params.FillPressure;

    public void Reconfigure(AirParams parameters)
    {
        Params = parameters;
        Pressure = Math.Min(Pressure, parameters.FillPressure);
    }
}
