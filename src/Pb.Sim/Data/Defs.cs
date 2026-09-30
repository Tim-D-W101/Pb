using Pb.Sim.Collision;
using Pb.Sim.Gear;
using Pb.Sim.Range;

namespace Pb.Sim.Data;

// Data-file definitions. Property names mirror the JSON keys (camelCase + unit suffix), so units stay
// visible in code; GameData converts them into SI parameter objects.
#pragma warning disable CA1707 // Identifiers should not contain underscores: the suffix is the unit.

/// <summary>sim.jsonc: tick rate, seed and which file supplies each component.</summary>
public sealed class SimDef : IValidatable
{
    public float TickRate_hz { get; set; }

    public ulong MatchSeed { get; set; }

    public int BallPoolCapacity { get; set; }

    public SimFilesDef Files { get; set; } = new();

    public void Validate(Validator v)
    {
        v.InRange(nameof(TickRate_hz), TickRate_hz, 30, 480);
        v.InRange(nameof(BallPoolCapacity), BallPoolCapacity, 64, 65536);
        Files.Validate(v.Scope(nameof(Files)));
    }
}

public sealed class SimFilesDef : IValidatable
{
    public string Projectile { get; set; } = "";

    public string Marker { get; set; } = "";

    public string Loader { get; set; } = "";

    public string Air { get; set; } = "";

    public string BreakModel { get; set; } = "";

    public string Movement { get; set; } = "";

    public string Range { get; set; } = "";

    public string Stress { get; set; } = "";

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Projectile), Projectile);
        v.NotEmpty(nameof(Marker), Marker);
        v.NotEmpty(nameof(Loader), Loader);
        v.NotEmpty(nameof(Air), Air);
        v.NotEmpty(nameof(BreakModel), BreakModel);
        v.NotEmpty(nameof(Movement), Movement);
        v.NotEmpty(nameof(Range), Range);
        v.NotEmpty(nameof(Stress), Stress);
    }
}

public sealed class ProjectileDef : IValidatable
{
    public string Id { get; set; } = "";

    public float Diameter_mm { get; set; }

    public float Mass_g { get; set; }

    public float DragCoefficient { get; set; }

    public float AirDensity_kgm3 { get; set; }

    public float Gravity_mps2 { get; set; }

    public float MaxLifetime_s { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.InRange(nameof(Diameter_mm), Diameter_mm, 5, 60);
        v.InRange(nameof(Mass_g), Mass_g, 0.1, 50);
        v.InRange(nameof(DragCoefficient), DragCoefficient, 0, 2);
        v.InRange(nameof(AirDensity_kgm3), AirDensity_kgm3, 0, 2);
        v.InRange(nameof(Gravity_mps2), Gravity_mps2, 0, 30);
        v.InRange(nameof(MaxLifetime_s), MaxLifetime_s, 0.5, 60);
    }
}

public sealed class MarkerDef : IValidatable
{
    public string Id { get; set; } = "";

    public float MuzzleVelocity_mps { get; set; }

    public float VelocityVariance_mps { get; set; }

    public float DispersionHalfAngle_deg { get; set; }

    public float MovingDispersion_degPerMps { get; set; }

    public float MaxDispersion_deg { get; set; }

    public float InheritShooterVelocity { get; set; }

    public float RateCap_bps { get; set; }

    public FireMode DefaultFireMode { get; set; }

    public RampingDef Ramping { get; set; } = new();

    public int BufferedPulls { get; set; }

    public float[] MuzzleOffset_m { get; set; } = Array.Empty<float>();

    public float ConvergenceDistance_m { get; set; }

    public float MinAimDistance_m { get; set; }

    public float MaxAimDistance_m { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.InRange(nameof(MuzzleVelocity_mps), MuzzleVelocity_mps, 10, 200);
        v.InRange(nameof(VelocityVariance_mps), VelocityVariance_mps, 0, 20);
        v.InRange(nameof(DispersionHalfAngle_deg), DispersionHalfAngle_deg, 0, 15);
        v.InRange(nameof(MovingDispersion_degPerMps), MovingDispersion_degPerMps, 0, 5);
        v.InRange(nameof(MaxDispersion_deg), MaxDispersion_deg, DispersionHalfAngle_deg, 30);
        v.InRange(nameof(InheritShooterVelocity), InheritShooterVelocity, 0, 1);
        v.InRange(nameof(RateCap_bps), RateCap_bps, 0.5, 40);
        Ramping.Validate(v.Scope(nameof(Ramping)));
        v.InRange(nameof(BufferedPulls), BufferedPulls, 0, 8);
        v.Vector(nameof(MuzzleOffset_m), MuzzleOffset_m);
        v.InRange(nameof(ConvergenceDistance_m), ConvergenceDistance_m, 1, 500);
        v.InRange(nameof(MinAimDistance_m), MinAimDistance_m, 0.1, 50);
        v.InRange(nameof(MaxAimDistance_m), MaxAimDistance_m, MinAimDistance_m, 2000);
    }
}

public sealed class RampingDef : IValidatable
{
    public int AfterShots { get; set; }

    public float MinPullRate_hz { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(AfterShots), AfterShots, 0, 20);
        v.InRange(nameof(MinPullRate_hz), MinPullRate_hz, 0.5, 30);
    }
}

public sealed class LoaderDef : IValidatable
{
    public string Id { get; set; } = "";

    public int Capacity { get; set; }

    public PodsDef Pods { get; set; } = new();

    public float RefillTime_s { get; set; }

    public bool CancelOnFire { get; set; }

    public bool CancelOnSprint { get; set; }

    public bool PartialOnInterrupt { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.InRange(nameof(Capacity), Capacity, 1, 1000);
        Pods.Validate(v.Scope(nameof(Pods)));
        v.InRange(nameof(RefillTime_s), RefillTime_s, 0.05, 30);
    }
}

public sealed class PodsDef : IValidatable
{
    public int Count { get; set; }

    public int Capacity { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Count), Count, 0, 12);
        v.InRange(nameof(Capacity), Capacity, 1, 1000);
    }
}

public sealed class AirDef : IValidatable
{
    public string Id { get; set; } = "";

    public float TankVolume_L { get; set; }

    public float FillPressure_bar { get; set; }

    public float RegulatorOutput_bar { get; set; }

    public float CutoffPressure_bar { get; set; }

    public float GasPerShot_barL { get; set; }

    public float VelocityExponent { get; set; }

    public float LowWarning_bar { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.InRange(nameof(TankVolume_L), TankVolume_L, 0.05, 20);
        v.InRange(nameof(FillPressure_bar), FillPressure_bar, 10, 500);
        v.InRange(nameof(RegulatorOutput_bar), RegulatorOutput_bar, 1, FillPressure_bar);
        v.InRange(nameof(CutoffPressure_bar), CutoffPressure_bar, 0, RegulatorOutput_bar);
        v.InRange(nameof(GasPerShot_barL), GasPerShot_barL, 0.001, 50);
        v.InRange(nameof(VelocityExponent), VelocityExponent, 0.05, 5);
        v.InRange(nameof(LowWarning_bar), LowWarning_bar, 0, FillPressure_bar);
    }
}

public sealed class BreakModelDef : IValidatable
{
    public SurfaceDef[] Surfaces { get; set; } = Array.Empty<SurfaceDef>();

    public float RestSpeed_mps { get; set; }

    public int MaxBounces { get; set; }

    public void Validate(Validator v)
    {
        if (Surfaces.Length == 0)
        {
            v.Error(nameof(Surfaces), "must list at least one surface");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Surfaces.Length; i++)
        {
            Validator item = v.Item(nameof(Surfaces), i);
            Surfaces[i].Validate(item);
            if (!seen.Add(Surfaces[i].Name))
            {
                item.Error(nameof(SurfaceDef.Name), $"duplicate surface '{Surfaces[i].Name}'");
            }
        }

        v.InRange(nameof(RestSpeed_mps), RestSpeed_mps, 0, 50);
        v.InRange(nameof(MaxBounces), MaxBounces, 0, 50);
    }
}

public sealed class SurfaceDef : IValidatable
{
    public string Name { get; set; } = "";

    public float Break50_mps { get; set; }

    public float BreakWidth_mps { get; set; }

    public float Restitution { get; set; }

    public float TangentRetain { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Name), Name);
        v.InRange(nameof(Break50_mps), Break50_mps, 0, 300);
        v.InRange(nameof(BreakWidth_mps), BreakWidth_mps, 0.1, 100);
        v.InRange(nameof(Restitution), Restitution, 0, 1);
        v.InRange(nameof(TangentRetain), TangentRetain, 0, 1);
    }
}

public sealed class MovementDef : IValidatable
{
    public float WalkSpeed_mps { get; set; }

    public float RunSpeed_mps { get; set; }

    public float SprintSpeed_mps { get; set; }

    public float CrouchSpeed_mps { get; set; }

    public float GroundAccel_mps2 { get; set; }

    public float GroundDecel_mps2 { get; set; }

    public float AirAccel_mps2 { get; set; }

    public float StandEyeHeight_m { get; set; }

    public float CrouchEyeHeight_m { get; set; }

    public float StanceTransitionSpeed_mps { get; set; }

    public float StandCapsuleHeight_m { get; set; }

    public float CrouchCapsuleHeight_m { get; set; }

    public float CapsuleRadius_m { get; set; }

    public float SprintMinForwardInput { get; set; }

    public float MaxPitch_deg { get; set; }

    public float Gravity_mps2 { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(WalkSpeed_mps), WalkSpeed_mps, 0.1, 20);
        v.InRange(nameof(RunSpeed_mps), RunSpeed_mps, 0.1, 20);
        v.InRange(nameof(SprintSpeed_mps), SprintSpeed_mps, 0.1, 20);
        v.InRange(nameof(CrouchSpeed_mps), CrouchSpeed_mps, 0.1, 20);
        v.InRange(nameof(GroundAccel_mps2), GroundAccel_mps2, 0.1, 500);
        v.InRange(nameof(GroundDecel_mps2), GroundDecel_mps2, 0.1, 500);
        v.InRange(nameof(AirAccel_mps2), AirAccel_mps2, 0, 500);
        v.InRange(nameof(StandEyeHeight_m), StandEyeHeight_m, 0.5, 2.5);
        v.InRange(nameof(CrouchEyeHeight_m), CrouchEyeHeight_m, 0.3, StandEyeHeight_m);
        v.InRange(nameof(StanceTransitionSpeed_mps), StanceTransitionSpeed_mps, 0.1, 50);
        v.InRange(nameof(StandCapsuleHeight_m), StandCapsuleHeight_m, 0.5, 2.5);
        v.InRange(nameof(CrouchCapsuleHeight_m), CrouchCapsuleHeight_m, 0.3, StandCapsuleHeight_m);
        v.InRange(nameof(CapsuleRadius_m), CapsuleRadius_m, 0.1, 1);
        v.InRange(nameof(SprintMinForwardInput), SprintMinForwardInput, 0, 1);
        v.InRange(nameof(MaxPitch_deg), MaxPitch_deg, 10, 89.9);
        v.InRange(nameof(Gravity_mps2), Gravity_mps2, 0, 50);
    }
}

public sealed class RangeDef : IValidatable
{
    public string Id { get; set; } = "";

    public GroundDef Ground { get; set; } = new();

    public BoundaryDef Boundary { get; set; } = new();

    public SpawnDef Spawn { get; set; } = new();

    public DistanceMarkersDef DistanceMarkers { get; set; } = new();

    public TargetKindDef[] TargetKinds { get; set; } = Array.Empty<TargetKindDef>();

    public TargetDef[] Targets { get; set; } = Array.Empty<TargetDef>();

    public PropDef[] Props { get; set; } = Array.Empty<PropDef>();

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        Ground.Validate(v.Scope(nameof(Ground)));
        Boundary.Validate(v.Scope(nameof(Boundary)));
        Spawn.Validate(v.Scope(nameof(Spawn)));
        DistanceMarkers.Validate(v.Scope(nameof(DistanceMarkers)));

        var kinds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < TargetKinds.Length; i++)
        {
            TargetKinds[i].Validate(v.Item(nameof(TargetKinds), i));
            kinds.Add(TargetKinds[i].Id);
        }

        for (int i = 0; i < Targets.Length; i++)
        {
            Validator item = v.Item(nameof(Targets), i);
            Targets[i].Validate(item);
            if (!kinds.Contains(Targets[i].Kind))
            {
                item.Error(nameof(TargetDef.Kind), $"unknown target kind '{Targets[i].Kind}'");
            }
        }

        for (int i = 0; i < Props.Length; i++)
        {
            Props[i].Validate(v.Item(nameof(Props), i));
        }
    }
}

public sealed class GroundDef : IValidatable
{
    public float Width_m { get; set; }

    public float Length_m { get; set; }

    public float BackMargin_m { get; set; }

    public string Surface { get; set; } = "";

    public void Validate(Validator v)
    {
        v.InRange(nameof(Width_m), Width_m, 2, 1000);
        v.InRange(nameof(Length_m), Length_m, 2, 1000);
        v.InRange(nameof(BackMargin_m), BackMargin_m, 0, 100);
        v.NotEmpty(nameof(Surface), Surface);
    }
}

public sealed class BoundaryDef : IValidatable
{
    public float Height_m { get; set; }

    public float Margin_m { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Height_m), Height_m, 1, 500);
        v.InRange(nameof(Margin_m), Margin_m, 0, 100);
    }
}

public sealed class SpawnDef : IValidatable
{
    public float[] Position_m { get; set; } = Array.Empty<float>();

    public float Yaw_deg { get; set; }

    public void Validate(Validator v)
    {
        v.Vector(nameof(Position_m), Position_m);
        v.InRange(nameof(Yaw_deg), Yaw_deg, -360, 360);
    }
}

public sealed class DistanceMarkersDef : IValidatable
{
    public float Spacing_m { get; set; }

    public float Max_m { get; set; }

    /// <summary>Distances that get a sign (default: every line). Fewer far signs keeps the horizon readable.</summary>
    [Optional]
    public float[]? Labels_m { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Spacing_m), Spacing_m, 1, 100);
        v.InRange(nameof(Max_m), Max_m, Spacing_m, 1000);
    }
}

public sealed class TargetKindDef : IValidatable
{
    public string Id { get; set; } = "";

    public string Surface { get; set; } = "";

    public TargetPartDef[] Parts { get; set; } = Array.Empty<TargetPartDef>();

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.NotEmpty(nameof(Surface), Surface);
        if (Parts.Length == 0)
        {
            v.Error(nameof(Parts), "must list at least one part");
        }

        for (int i = 0; i < Parts.Length; i++)
        {
            Parts[i].Validate(v.Item(nameof(Parts), i));
        }
    }
}

public sealed class TargetPartDef : IValidatable
{
    public HitboxPart Part { get; set; }

    public PartShapeKind Shape { get; set; }

    [Optional]
    public float[]? From_m { get; set; }

    [Optional]
    public float[]? To_m { get; set; }

    [Optional]
    public float[]? Center_m { get; set; }

    [Optional]
    public float Radius_m { get; set; }

    [Optional]
    public float[]? Size_m { get; set; }

    public void Validate(Validator v)
    {
        switch (Shape)
        {
            case PartShapeKind.Capsule:
                v.Vector(nameof(From_m), From_m);
                v.Vector(nameof(To_m), To_m);
                v.InRange(nameof(Radius_m), Radius_m, 0.01, 5);
                break;
            case PartShapeKind.Sphere:
                v.Vector(nameof(Center_m), Center_m);
                v.InRange(nameof(Radius_m), Radius_m, 0.01, 5);
                break;
            case PartShapeKind.Box:
                v.Vector(nameof(Center_m), Center_m);
                v.Vector(nameof(Size_m), Size_m);
                break;
        }
    }
}

public sealed class TargetDef : IValidatable
{
    public string Id { get; set; } = "";

    [Optional]
    public string? Label { get; set; }

    public string Kind { get; set; } = "";

    public float[] Position_m { get; set; } = Array.Empty<float>();

    [Optional]
    public float Yaw_deg { get; set; }

    [Optional]
    public MotionDef? Motion { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.NotEmpty(nameof(Kind), Kind);
        v.Vector(nameof(Position_m), Position_m);
        Motion?.Validate(v.Scope(nameof(Motion)));
    }
}

public sealed class MotionDef : IValidatable
{
    public float[] Axis { get; set; } = Array.Empty<float>();

    public float Amplitude_m { get; set; }

    public float Speed_mps { get; set; }

    public void Validate(Validator v)
    {
        v.Vector(nameof(Axis), Axis);
        v.InRange(nameof(Amplitude_m), Amplitude_m, 0, 100);
        v.InRange(nameof(Speed_mps), Speed_mps, 0, 20);
    }
}

public sealed class PropDef : IValidatable
{
    public string Id { get; set; } = "";

    public PropShapeKind Shape { get; set; }

    public float[] Position_m { get; set; } = Array.Empty<float>();

    [Optional]
    public float Yaw_deg { get; set; }

    [Optional]
    public float[]? Size_m { get; set; }

    [Optional]
    public float Radius_m { get; set; }

    [Optional]
    public float Height_m { get; set; }

    public string Surface { get; set; } = "";

    [Optional]
    public string? Color { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.Vector(nameof(Position_m), Position_m);
        v.NotEmpty(nameof(Surface), Surface);
        switch (Shape)
        {
            case PropShapeKind.Box:
            case PropShapeKind.Wedge:
                v.Vector(nameof(Size_m), Size_m);
                if (Size_m is { Length: 3 } && (Size_m[0] <= 0 || Size_m[1] <= 0 || Size_m[2] <= 0))
                {
                    v.Error(nameof(Size_m), "all sizes must be > 0");
                }

                break;
            case PropShapeKind.Cylinder:
                v.InRange(nameof(Radius_m), Radius_m, 0.01, 50);
                v.InRange(nameof(Height_m), Height_m, 0.01, 50);
                break;
        }
    }
}

public sealed class StressDef : IValidatable
{
    public int TargetLiveBalls { get; set; }

    public int MaxSpawnPerTick { get; set; }

    public CannonDef[] Cannons { get; set; } = Array.Empty<CannonDef>();

    public void Validate(Validator v)
    {
        v.InRange(nameof(TargetLiveBalls), TargetLiveBalls, 0, 100000);
        v.InRange(nameof(MaxSpawnPerTick), MaxSpawnPerTick, 1, 1000);
        for (int i = 0; i < Cannons.Length; i++)
        {
            Cannons[i].Validate(v.Item(nameof(Cannons), i));
        }
    }
}

public sealed class CannonDef : IValidatable
{
    public float[] Position_m { get; set; } = Array.Empty<float>();

    public float Yaw_deg { get; set; }

    public float YawSpread_deg { get; set; }

    public float PitchMin_deg { get; set; }

    public float PitchMax_deg { get; set; }

    public void Validate(Validator v)
    {
        v.Vector(nameof(Position_m), Position_m);
        v.InRange(nameof(YawSpread_deg), YawSpread_deg, 0, 180);
        v.InRange(nameof(PitchMin_deg), PitchMin_deg, -89, 89);
        v.InRange(nameof(PitchMax_deg), PitchMax_deg, PitchMin_deg, 89);
    }
}

#pragma warning restore CA1707
