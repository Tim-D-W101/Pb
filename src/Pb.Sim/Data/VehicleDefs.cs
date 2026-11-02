namespace Pb.Sim.Data;

/// <summary>vehicles.jsonc: what can be driven.</summary>
public sealed class VehiclesDef : IValidatable
{
    public VehicleDef[] Vehicles { get; set; } = Array.Empty<VehicleDef>();

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

    public float MaxSpeed_mps { get; set; }

    public float ReverseSpeed_mps { get; set; }

    public float Accel_mps2 { get; set; }

    public float Brake_mps2 { get; set; }

    public float Coast_mps2 { get; set; }

    public float MaxSteer_deg { get; set; }

    public float SteerRate_degps { get; set; }

    public float EnterRadius_m { get; set; }

    public float ExitBelow_mps { get; set; }

    public float SeatedEyeHeight_m { get; set; }

    public float StopShort_m { get; set; }

    public float Brush_m { get; set; }

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
        v.InRange(nameof(MaxSpeed_mps), MaxSpeed_mps, 1, 30);
        v.InRange(nameof(ReverseSpeed_mps), ReverseSpeed_mps, 0.5, 10);
        v.InRange(nameof(Accel_mps2), Accel_mps2, 0.5, 20);
        v.InRange(nameof(Brake_mps2), Brake_mps2, 0.5, 30);
        v.InRange(nameof(Coast_mps2), Coast_mps2, 0.1, 10);
        v.InRange(nameof(MaxSteer_deg), MaxSteer_deg, 5, 60);
        v.InRange(nameof(SteerRate_degps), SteerRate_degps, 5, 720);
        v.InRange(nameof(EnterRadius_m), EnterRadius_m, 0.5, 5);
        v.InRange(nameof(ExitBelow_mps), ExitBelow_mps, 0.1, 5);
        v.InRange(nameof(SeatedEyeHeight_m), SeatedEyeHeight_m, 0.4, 1.6);
        v.InRange(nameof(StopShort_m), StopShort_m, 0.1, 5);
        v.InRange(nameof(Brush_m), Brush_m, 0.02, 1);
        if (Brush_m > StopShort_m)
        {
            v.Error(nameof(Brush_m), "can't be more than stopShort_m");
        }
        if (Floor_m >= SideHeight_m)
        {
            v.Error(nameof(Floor_m), "the floor has to be below the sides' top");
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
