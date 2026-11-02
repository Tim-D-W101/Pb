namespace Pb.Sim.Data;

/// <summary>vehicles.jsonc: what can be driven, and what each surface gives a tyre.</summary>
public sealed class VehiclesDef : IValidatable
{
    public VehicleDef[] Vehicles { get; set; } = Array.Empty<VehicleDef>();

    public TyreDef[] Tyres { get; set; } = Array.Empty<TyreDef>();

    public void Validate(Validator v)
    {
        for (int i = 0; i < Vehicles.Length; i++)
        {
            Vehicles[i].Validate(v.Item(nameof(Vehicles), i));
        }

        if (Vehicles.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != Vehicles.Length)
        {
            v.Error(nameof(Vehicles), "lists a vehicle twice");
        }

        for (int i = 0; i < Tyres.Length; i++)
        {
            Tyres[i].Validate(v.Item(nameof(Tyres), i));
        }

        if (!Tyres.Any(t => t.Surface == "default"))
        {
            v.Error(nameof(Tyres), "needs a \"default\" for the surfaces it doesn't list");
        }
    }
}

/// <summary>What a surface gives a tyre: its grip (a friction coefficient) and rolling resistance (likewise).</summary>
public sealed class TyreDef : IValidatable
{
    public string Surface { get; set; } = "";

    public float Grip { get; set; }

    public float Rolling { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Surface), Surface);
        v.InRange(nameof(Grip), Grip, 0.05, 2);
        v.InRange(nameof(Rolling), Rolling, 0, 0.5);
    }
}

/// <summary>One kind of vehicle (vehicles.jsonc explains each key).</summary>
public sealed class VehicleDef : IValidatable
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public float Length_m { get; set; }

    public float Width_m { get; set; }

    public float SideHeight_m { get; set; }

    public float Floor_m { get; set; }

    public float Wheelbase_m { get; set; }

    public float Track_m { get; set; }

    public float WheelRadius_m { get; set; }

    public float Mass_kg { get; set; }

    public float ComHeight_m { get; set; }

    public float MountHeight_m { get; set; }

    public float SuspensionHz { get; set; }

    public float SuspensionDamping { get; set; }

    public float BumpTravel_m { get; set; }

    public float AntiRoll_Npm { get; set; }

    public float[] GearSpeeds_mps { get; set; } = Array.Empty<float>();

    public float[] DriveForce_N { get; set; } = Array.Empty<float>();

    public float ReverseSpeed_mps { get; set; }

    public float ReverseForce_N { get; set; }

    public float IdleRpm { get; set; }

    public float MaxRpm { get; set; }

    public float PowerBand { get; set; }

    public float ShiftUp { get; set; }

    public float ShiftDown { get; set; }

    public float Shift_s { get; set; }

    public float FrontDrive { get; set; }

    public float RevRate_rpmps { get; set; }

    public float EngineBrake_N { get; set; }

    public float BrakeForce_N { get; set; }

    public float BrakeFront { get; set; }

    public float HandbrakeGrip { get; set; }

    public float Drag { get; set; }

    public float CorneringStiffness { get; set; }

    public float SlipSpeed_mps { get; set; }

    public float MaxSteer_deg { get; set; }

    public float SteerAtSpeed { get; set; }

    public float SteerRate_degps { get; set; }

    public float BodyBounce { get; set; }

    public float BodyFriction { get; set; }

    public float RestBelow_mps { get; set; }

    public float HoldBelow_mps { get; set; }

    public float RightAfter_s { get; set; }

    public float EnterRadius_m { get; set; }

    public float ExitBelow_mps { get; set; }

    public float StopShort_m { get; set; }

    public float Brush_m { get; set; }

    public float SeatedEyeHeight_m { get; set; }

    public SeatDef[] Seats { get; set; } = Array.Empty<SeatDef>();

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.NotEmpty(nameof(DisplayName), DisplayName);
        v.InRange(nameof(Length_m), Length_m, 1, 12);
        v.InRange(nameof(Width_m), Width_m, 0.8, 4);
        v.InRange(nameof(SideHeight_m), SideHeight_m, 0.3, 3);
        v.InRange(nameof(Floor_m), Floor_m, 0.05, 1.5);
        v.InRange(nameof(Wheelbase_m), Wheelbase_m, 0.5, 10);
        v.InRange(nameof(Track_m), Track_m, 0.5, 4);
        v.InRange(nameof(WheelRadius_m), WheelRadius_m, 0.1, 1);
        v.InRange(nameof(Mass_kg), Mass_kg, 50, 20000);
        v.InRange(nameof(ComHeight_m), ComHeight_m, 0.1, 3);
        v.InRange(nameof(MountHeight_m), MountHeight_m, WheelRadius_m + 0.05, 3);
        v.InRange(nameof(SuspensionHz), SuspensionHz, 0.5, 5);
        v.InRange(nameof(SuspensionDamping), SuspensionDamping, 0.05, 2);
        v.InRange(nameof(BumpTravel_m), BumpTravel_m, 0.02, 1);
        v.InRange(nameof(AntiRoll_Npm), AntiRoll_Npm, 0, 200000);
        v.InRange(nameof(ReverseSpeed_mps), ReverseSpeed_mps, 0.5, 10);
        v.InRange(nameof(ReverseForce_N), ReverseForce_N, 10, 100000);
        v.InRange(nameof(IdleRpm), IdleRpm, 300, 3000);
        v.InRange(nameof(MaxRpm), MaxRpm, IdleRpm + 500, 15000);
        v.InRange(nameof(PowerBand), PowerBand, 0.3, 0.98);
        v.InRange(nameof(ShiftUp), ShiftUp, 0.5, 1);
        v.InRange(nameof(ShiftDown), ShiftDown, 0.1, 0.9);
        v.InRange(nameof(Shift_s), Shift_s, 0, 2);
        v.InRange(nameof(FrontDrive), FrontDrive, 0, 1);
        v.InRange(nameof(RevRate_rpmps), RevRate_rpmps, 100, 200000);
        v.InRange(nameof(EngineBrake_N), EngineBrake_N, 0, 20000);
        v.InRange(nameof(BrakeForce_N), BrakeForce_N, 100, 100000);
        v.InRange(nameof(BrakeFront), BrakeFront, 0, 1);
        v.InRange(nameof(HandbrakeGrip), HandbrakeGrip, 0.05, 1);
        v.InRange(nameof(Drag), Drag, 0, 5);
        v.InRange(nameof(CorneringStiffness), CorneringStiffness, 0.5, 40);
        v.InRange(nameof(SlipSpeed_mps), SlipSpeed_mps, 0.1, 10);
        v.InRange(nameof(MaxSteer_deg), MaxSteer_deg, 5, 60);
        v.InRange(nameof(SteerAtSpeed), SteerAtSpeed, 0.1, 1);
        v.InRange(nameof(SteerRate_degps), SteerRate_degps, 5, 720);
        v.InRange(nameof(BodyBounce), BodyBounce, 0, 1);
        v.InRange(nameof(BodyFriction), BodyFriction, 0, 2);
        v.InRange(nameof(RestBelow_mps), RestBelow_mps, 0.001, 1);
        v.InRange(nameof(HoldBelow_mps), HoldBelow_mps, 0.05, 3);
        v.InRange(nameof(RightAfter_s), RightAfter_s, 0.2, 30);
        v.InRange(nameof(EnterRadius_m), EnterRadius_m, 0.5, 5);
        v.InRange(nameof(ExitBelow_mps), ExitBelow_mps, 0.1, 5);
        v.InRange(nameof(StopShort_m), StopShort_m, 0.1, 5);
        v.InRange(nameof(Brush_m), Brush_m, 0.02, 1);
        v.InRange(nameof(SeatedEyeHeight_m), SeatedEyeHeight_m, 0.4, 1.6);
        if (Brush_m > StopShort_m)
        {
            v.Error(nameof(Brush_m), "can't be more than stopShort_m");
        }

        if (ShiftDown >= ShiftUp)
        {
            v.Error(nameof(ShiftDown), "has to be below shiftUp, or it would change gear back and forth");
        }

        if (Floor_m >= SideHeight_m)
        {
            v.Error(nameof(Floor_m), "the floor has to be below the sides' top");
        }

        if (GearSpeeds_mps.Length == 0 || GearSpeeds_mps.Length != DriveForce_N.Length)
        {
            v.Error(nameof(GearSpeeds_mps), "needs a speed for each gear, and driveForce_N a force for each");
        }

        for (int i = 0; i < GearSpeeds_mps.Length; i++)
        {
            v.InRange($"{nameof(GearSpeeds_mps)}[{i}]", GearSpeeds_mps[i], i == 0 ? 0.5 : GearSpeeds_mps[i - 1] + 0.1, 60);
        }

        for (int i = 0; i < DriveForce_N.Length; i++)
        {
            v.InRange($"{nameof(DriveForce_N)}[{i}]", DriveForce_N[i], 10, 100000);
        }

        if (Seats.Length == 0 || Seats.Count(s => s.Drives) != 1)
        {
            v.Error(nameof(Seats), "needs seats, exactly one of them the driver's");
        }

        for (int i = 0; i < Seats.Length; i++)
        {
            Seats[i].Validate(v.Item(nameof(Seats), i));
        }
    }
}

public sealed class SeatDef : IValidatable
{
    public string Id { get; set; } = "";

    /// <summary>Where the seat is: x to the vehicle's right, y up from the ground, z ahead of its centre.</summary>
    public float[] At_m { get; set; } = Array.Empty<float>();

    public bool Drives { get; set; }

    public bool Fires { get; set; }

    /// <summary>How much wider the one in it fires than standing (a driver's one-handed aim); 1 for none.</summary>
    [Optional]
    public float SpreadScale { get; set; } = 1f;

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.Vector(nameof(At_m), At_m);
        v.InRange(nameof(SpreadScale), SpreadScale, 1, 10);
    }
}
