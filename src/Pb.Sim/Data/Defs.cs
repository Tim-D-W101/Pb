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

    /// <summary>Player hitbox rig and elimination rules.</summary>
    public string Hitboxes { get; set; } = "";

    /// <summary>Round rules (mode, settle time, pickups).</summary>
    public string Rules { get; set; } = "";

    public string Range { get; set; } = "";

    public string Stress { get; set; } = "";

    /// <summary>Level kit index (materials, prop types, building templates).</summary>
    public string Kit { get; set; } = "";

    /// <summary>The level ladder: which levels exist and in what order.</summary>
    public string Ladder { get; set; } = "";

    /// <summary>Bots: navigation grid, senses, behaviours and difficulty tiers (M2.5).</summary>
    public string Navigation { get; set; } = "";

    public string Brain { get; set; } = "";

    public string Senses { get; set; } = "";

    public string Archetypes { get; set; } = "";

    public string Difficulty { get; set; } = "";

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Kit), Kit);
        v.NotEmpty(nameof(Ladder), Ladder);
        v.NotEmpty(nameof(Navigation), Navigation);
        v.NotEmpty(nameof(Brain), Brain);
        v.NotEmpty(nameof(Senses), Senses);
        v.NotEmpty(nameof(Archetypes), Archetypes);
        v.NotEmpty(nameof(Difficulty), Difficulty);
        v.NotEmpty(nameof(Projectile), Projectile);
        v.NotEmpty(nameof(Marker), Marker);
        v.NotEmpty(nameof(Loader), Loader);
        v.NotEmpty(nameof(Air), Air);
        v.NotEmpty(nameof(BreakModel), BreakModel);
        v.NotEmpty(nameof(Movement), Movement);
        v.NotEmpty(nameof(Hitboxes), Hitboxes);
        v.NotEmpty(nameof(Rules), Rules);
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

    public bool MuzzleBlockedBreaks { get; set; }

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

    public float LeanAngle_deg { get; set; }

    public float LeanPivotBelowEye_m { get; set; }

    public float LeanInTime_s { get; set; }

    public float LeanReturnTime_s { get; set; }

    public float HeadRadius_m { get; set; }

    public float ShoulderSwapTime_s { get; set; }

    public float SlideMinSpeed_mps { get; set; }

    public float SlideBoost_mps { get; set; }

    public float SlideMaxSpeed_mps { get; set; }

    public float SlideFriction_mps2 { get; set; }

    public float SlideEndSpeed_mps { get; set; }

    public float SlideMaxTime_s { get; set; }

    public float SlideCooldown_s { get; set; }

    public float SlideSteering { get; set; }

    public float SlideEyeHeight_m { get; set; }

    public float SlideCapsuleHeight_m { get; set; }

    public float JumpSpeed_mps { get; set; }

    public float JumpCooldown_s { get; set; }

    public float SprintRecoveryTime_s { get; set; }

    public FootstepsDef Footsteps { get; set; } = new();

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
        v.InRange(nameof(LeanAngle_deg), LeanAngle_deg, 0, 45);
        v.InRange(nameof(LeanPivotBelowEye_m), LeanPivotBelowEye_m, 0.1, CrouchEyeHeight_m);
        v.InRange(nameof(LeanInTime_s), LeanInTime_s, 0.01, 2);
        v.InRange(nameof(LeanReturnTime_s), LeanReturnTime_s, 0.01, 2);
        v.InRange(nameof(HeadRadius_m), HeadRadius_m, 0.05, 0.3);
        v.InRange(nameof(ShoulderSwapTime_s), ShoulderSwapTime_s, 0.01, 3);
        v.InRange(nameof(SlideMinSpeed_mps), SlideMinSpeed_mps, 0.5, 20);
        v.InRange(nameof(SlideBoost_mps), SlideBoost_mps, 0, 10);
        v.InRange(nameof(SlideMaxSpeed_mps), SlideMaxSpeed_mps, SlideMinSpeed_mps, 25);
        v.InRange(nameof(SlideFriction_mps2), SlideFriction_mps2, 0.1, 100);
        v.InRange(nameof(SlideEndSpeed_mps), SlideEndSpeed_mps, 0, SlideMinSpeed_mps);
        v.InRange(nameof(SlideMaxTime_s), SlideMaxTime_s, 0.1, 5);
        v.InRange(nameof(SlideCooldown_s), SlideCooldown_s, 0, 10);
        v.InRange(nameof(SlideSteering), SlideSteering, 0, 1);
        v.InRange(nameof(SlideEyeHeight_m), SlideEyeHeight_m, 0.2, CrouchEyeHeight_m);
        v.InRange(nameof(SlideCapsuleHeight_m), SlideCapsuleHeight_m, Math.Max(0.2, 2 * CapsuleRadius_m), CrouchCapsuleHeight_m);
        v.InRange(nameof(JumpSpeed_mps), JumpSpeed_mps, 0, 10);
        v.InRange(nameof(JumpCooldown_s), JumpCooldown_s, 0, 5);
        v.InRange(nameof(SprintRecoveryTime_s), SprintRecoveryTime_s, 0, 2);
        Footsteps.Validate(v.Scope(nameof(Footsteps)));
    }
}

/// <summary>Footstep noise: what bots can hear, and later what footstep audio plays.</summary>
public sealed class FootstepsDef : IValidatable
{
    public float Stride_m { get; set; }

    public float CrouchRadius_m { get; set; }

    public float WalkRadius_m { get; set; }

    public float RunRadius_m { get; set; }

    public float SprintRadius_m { get; set; }

    public float SlideRadius_m { get; set; }

    public float JumpRadius_m { get; set; }

    public float LandRadius_m { get; set; }

    public float LandMinSpeed_mps { get; set; }

    /// <summary>Loudness by surface name (break_model.jsonc); surfaces not listed count as 1.</summary>
    public Dictionary<string, float> SurfaceLoudness { get; set; } = new();

    public void Validate(Validator v)
    {
        v.InRange(nameof(Stride_m), Stride_m, 0.2, 5);
        foreach ((string key, float value) in new[]
                 {
                     (nameof(CrouchRadius_m), CrouchRadius_m), (nameof(WalkRadius_m), WalkRadius_m), (nameof(RunRadius_m), RunRadius_m),
                     (nameof(SprintRadius_m), SprintRadius_m), (nameof(SlideRadius_m), SlideRadius_m), (nameof(JumpRadius_m), JumpRadius_m),
                     (nameof(LandRadius_m), LandRadius_m),
                 })
        {
            v.InRange(key, value, 0, 200);
        }

        v.InRange(nameof(LandMinSpeed_mps), LandMinSpeed_mps, 0, 50);
        foreach ((string surface, float loudness) in SurfaceLoudness)
        {
            v.InRange($"{Jsonc.KeyOf(nameof(SurfaceLoudness))}.{surface}", loudness, 0, 10);
        }
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

    /// <summary>Camera viewpoints for the game's screenshot tour (looks only), as a level's.</summary>
    [Optional]
    public ViewpointDef[]? Viewpoints { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        LevelDefChecks.Items(v, nameof(Viewpoints), Viewpoints);
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

/// <summary>Player hitboxes and elimination rules (hitboxes.jsonc).</summary>
public sealed class HitboxesDef : IValidatable
{
    public string Surface { get; set; } = "";

    public float LegsWidth_m { get; set; }

    public float LegsDepth_m { get; set; }

    public float TorsoWidth_m { get; set; }

    public float TorsoDepth_m { get; set; }

    public float TorsoTopBelowEye_m { get; set; }

    public PartBoxDef Head { get; set; } = new();

    public PartBoxDef Mask { get; set; } = new();

    public PartBoxDef Arms { get; set; } = new();

    public PartBoxDef Marker { get; set; } = new();

    public PartBoxDef Loader { get; set; } = new();

    public PartBoxDef Tank { get; set; } = new();

    public float EliminatedRaise_m { get; set; }

    public float EliminatedPitch_deg { get; set; }

    public HitboxPart[] LethalParts { get; set; } = Array.Empty<HitboxPart>();

    public bool BallsInFlightCount { get; set; }

    public float MaskSprayRadius_m { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Surface), Surface);
        v.InRange(nameof(LegsWidth_m), LegsWidth_m, 0.05, 1);
        v.InRange(nameof(LegsDepth_m), LegsDepth_m, 0.05, 1);
        v.InRange(nameof(TorsoWidth_m), TorsoWidth_m, 0.05, 1);
        v.InRange(nameof(TorsoDepth_m), TorsoDepth_m, 0.05, 1);
        v.InRange(nameof(TorsoTopBelowEye_m), TorsoTopBelowEye_m, 0, 0.6);
        Head.Validate(v.Scope(nameof(Head)));
        Mask.Validate(v.Scope(nameof(Mask)));
        Arms.Validate(v.Scope(nameof(Arms)));
        Marker.Validate(v.Scope(nameof(Marker)));
        Loader.Validate(v.Scope(nameof(Loader)));
        Tank.Validate(v.Scope(nameof(Tank)));
        v.InRange(nameof(EliminatedRaise_m), EliminatedRaise_m, 0, 1);
        v.InRange(nameof(EliminatedPitch_deg), EliminatedPitch_deg, -90, 90);
        if (LethalParts.Contains(HitboxPart.Body))
        {
            v.Error(nameof(LethalParts), "'body' is for range targets; players have head, torso, arms and legs");
        }

        v.InRange(nameof(MaskSprayRadius_m), MaskSprayRadius_m, 0, 3);
    }
}

/// <summary>One hitbox: size_m = [width, height, depth]; centre_m = [right, up, forward] from the eye.</summary>
public sealed class PartBoxDef : IValidatable
{
    public float[] Size_m { get; set; } = Array.Empty<float>();

    public float[] Centre_m { get; set; } = Array.Empty<float>();

    public void Validate(Validator v)
    {
        v.Vector(nameof(Size_m), Size_m);
        v.Vector(nameof(Centre_m), Centre_m);
        if (Size_m.Length == 3 && Size_m.Any(x => x <= 0f || x > 2f))
        {
            v.Error(nameof(Size_m), "sizes must be in (0, 2] m");
        }
    }
}

public enum MatchModeKind
{
    /// <summary>You against a squad holding the level; they don't fight each other.</summary>
    Solo,

    /// <summary>Everyone against everyone: every player is a team of their own.</summary>
    FreeForAll,

    /// <summary>Two teams: you and bot teammates against a bot team.</summary>
    Teams,
}

/// <summary>Round rules (rules.jsonc).</summary>
public sealed class RulesDef : IValidatable
{
    /// <summary>The modes the menu offers, in order.</summary>
    public ModeDef[] Modes { get; set; } = Array.Empty<ModeDef>();

    /// <summary>The most people in one round, you included.</summary>
    public int MaxPlayers { get; set; }

    public float SettleTime_s { get; set; }

    public bool TradeCountsAsClear { get; set; }

    public float PickupRadius_m { get; set; }

    public float AirPickupBelow { get; set; }

    public SpawningDef Spawning { get; set; } = new();

    public DoorRulesDef Doors { get; set; } = new();

    public void Validate(Validator v)
    {
        v.InRange(nameof(MaxPlayers), MaxPlayers, 2, 32);
        v.InRange(nameof(SettleTime_s), SettleTime_s, 0, 10);
        v.InRange(nameof(PickupRadius_m), PickupRadius_m, 0.1, 5);
        v.InRange(nameof(AirPickupBelow), AirPickupBelow, 0, 1);
        Spawning.Validate(v.Scope(nameof(Spawning)));
        Doors.Validate(v.Scope(nameof(Doors)));
        if (Modes.Length == 0)
        {
            v.Error(nameof(Modes), "needs at least one mode");
        }

        for (int i = 0; i < Modes.Length; i++)
        {
            Modes[i].Validate(v.Item(nameof(Modes), i));
            if (Modes[i].Sizes.Any(size => Modes[i].PlayersFor(size) > MaxPlayers))
            {
                v.Error(nameof(Modes), $"{Modes[i].Id}: a size needs more than maxPlayers ({MaxPlayers}) people");
            }
        }

        if (Modes.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != Modes.Length)
        {
            v.Error(nameof(Modes), "two modes share an id");
        }
    }
}

/// <summary>How doors are used (rules.jsonc "doors").</summary>
public sealed class DoorRulesDef : IValidatable
{
    /// <summary>A door can be worked from this far (eye to the nearest point of the leaf).</summary>
    public float Reach_m { get; set; }

    /// <summary>... and only within this angle of where you look.</summary>
    public float Cone_deg { get; set; }

    /// <summary>A press held longer than this eases the door instead of swinging it all the way.</summary>
    public float HoldTime_s { get; set; }

    /// <summary>How fast a held press eases the door: share of the full swing per second.</summary>
    public float EaseRate { get; set; }

    /// <summary>How far an ajar door stands open, as a share of the full swing.</summary>
    public float Ajar { get; set; }

    /// <summary>Doors marked "random" start shut, open or ajar by these chances.</summary>
    public StartChanceDef[] RandomStart { get; set; } = Array.Empty<StartChanceDef>();

    /// <summary>Bots go through a door once it's this far open (share of the full swing).</summary>
    public float BotPassOpen { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Reach_m), Reach_m, 0.5, 4);
        v.InRange(nameof(Cone_deg), Cone_deg, 5, 90);
        v.InRange(nameof(HoldTime_s), HoldTime_s, 0.05, 2);
        v.InRange(nameof(EaseRate), EaseRate, 0.05, 5);
        v.InRange(nameof(Ajar), Ajar, 0.05, 0.95);
        v.InRange(nameof(BotPassOpen), BotPassOpen, 0.2, 1);
        if (RandomStart.Length == 0 || RandomStart.All(c => c.Weight <= 0f))
        {
            v.Error(nameof(RandomStart), "needs at least one start with a positive weight");
        }

        for (int i = 0; i < RandomStart.Length; i++)
        {
            Validator item = v.Item(nameof(RandomStart), i);
            item.InRange(nameof(StartChanceDef.Weight), RandomStart[i].Weight, 0, 100);
            if (RandomStart[i].Start == DoorStart.Random)
            {
                item.Error(nameof(StartChanceDef.Start), "must be shut, open or ajar");
            }
        }
    }
}

public sealed class StartChanceDef
{
    public DoorStart Start { get; set; }

    public float Weight { get; set; }
}

/// <summary>One mode the menu offers (rules.jsonc "modes").</summary>
public sealed class ModeDef : IValidatable
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Description { get; set; } = "";

    public MatchModeKind Kind { get; set; }

    /// <summary>The sizes offered: opponents (solo), players including you (free-for-all) or players a side (teams).</summary>
    public int[] Sizes { get; set; } = Array.Empty<int>();

    public int DefaultSize { get; set; }

    /// <summary>Behaviours for the bots, with their chances; none for solo, where the level's spawn roles apply.</summary>
    [Optional]
    public RoleWeightDef[] Roles { get; set; } = Array.Empty<RoleWeightDef>();

    /// <summary>A bot with nothing to go on for this long starts hunting (0 = never).</summary>
    [Optional]
    public float RestlessAfter_s { get; set; }

    /// <summary>Everyone in a round of <paramref name="size"/>, you included.</summary>
    public int PlayersFor(int size) => Kind switch
    {
        MatchModeKind.FreeForAll => size,
        MatchModeKind.Teams => size * 2,
        _ => size + 1,
    };

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.NotEmpty(nameof(DisplayName), DisplayName);
        v.NotEmpty(nameof(Description), Description);
        v.InRange(nameof(RestlessAfter_s), RestlessAfter_s, 0, 3600);
        int smallest = Kind == MatchModeKind.FreeForAll ? 2 : 1;
        if (Sizes.Length == 0 || Sizes.Any(size => size < smallest))
        {
            v.Error(nameof(Sizes), $"needs at least one size, each at least {smallest}");
        }

        if (Sizes.Distinct().Count() != Sizes.Length)
        {
            v.Error(nameof(Sizes), "lists a size twice");
        }

        if (!Sizes.Contains(DefaultSize))
        {
            v.Error(nameof(DefaultSize), $"{DefaultSize} isn't one of the sizes");
        }

        if (Kind != MatchModeKind.Solo && Roles.Length == 0)
        {
            v.Error(nameof(Roles), "needs at least one bot behaviour (only solo uses the level's spawn roles)");
        }

        for (int i = 0; i < Roles.Length; i++)
        {
            Roles[i].Validate(v.Item(nameof(Roles), i));
        }
    }
}

/// <summary>How a round deals random starts (see rules.jsonc "spawning").</summary>
public sealed class SpawningDef : IValidatable
{
    public float MinDistanceFromYou_m { get; set; }

    public float MinSpacing_m { get; set; }

    public float CoverShare { get; set; }

    public RoleWeightDef[] CoverRoles { get; set; } = Array.Empty<RoleWeightDef>();

    public float PatrolReach_m { get; set; }

    public float FreeForAllSpacing_m { get; set; }

    public float TeammatesWithin_m { get; set; }

    public float TeammateSpacing_m { get; set; }

    public float TeamSpread_m { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(MinDistanceFromYou_m), MinDistanceFromYou_m, 0, 500);
        v.InRange(nameof(MinSpacing_m), MinSpacing_m, 0, 100);
        v.InRange(nameof(CoverShare), CoverShare, 0, 1);
        v.InRange(nameof(PatrolReach_m), PatrolReach_m, 0, 500);
        v.InRange(nameof(FreeForAllSpacing_m), FreeForAllSpacing_m, 0, 100);
        v.InRange(nameof(TeammatesWithin_m), TeammatesWithin_m, 1, 100);
        v.InRange(nameof(TeammateSpacing_m), TeammateSpacing_m, 0.5, 50);
        v.InRange(nameof(TeamSpread_m), TeamSpread_m, 1, 200);
        if (CoverRoles.Length == 0)
        {
            v.Error(nameof(CoverRoles), "needs at least one role");
        }

        for (int i = 0; i < CoverRoles.Length; i++)
        {
            CoverRoles[i].Validate(v.Item(nameof(CoverRoles), i));
        }
    }
}

public sealed class RoleWeightDef : IValidatable
{
    public string Role { get; set; } = "";

    public float Weight { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Role), Role);
        v.InRange(nameof(Weight), Weight, 0.001, 1000);
    }
}
