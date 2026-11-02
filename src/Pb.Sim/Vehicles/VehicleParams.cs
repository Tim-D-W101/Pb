using System.Numerics;
using Pb.Sim.Core;
using Pb.Sim.Data;

namespace Pb.Sim.Vehicles;

/// <summary>One seat: where it is in the vehicle's frame (x right, y up, z ahead), and whether its occupant drives or fires.</summary>
public readonly record struct SeatParams(string Id, Vector3 At, bool Drives, bool Fires);

/// <summary>A kind of vehicle (SI), from vehicles.jsonc.</summary>
public sealed class VehicleParams
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required float Length { get; init; }

    public required float Width { get; init; }

    public required float SideHeight { get; init; }

    public required float Floor { get; init; }

    public required float Wheelbase { get; init; }

    public required float MaxSpeed { get; init; }

    public required float ReverseSpeed { get; init; }

    public required float Accel { get; init; }

    public required float Brake { get; init; }

    public required float Coast { get; init; }

    public required float MaxSteer { get; init; }

    public required float SteerRate { get; init; }

    public required float EnterRadius { get; init; }

    public required float ExitBelow { get; init; }

    public required float SeatedEyeHeight { get; init; }

    public required float StopShort { get; init; }

    public required float Brush { get; init; }

    public required IReadOnlyList<SeatParams> Seats { get; init; }

    /// <summary>The driver's seat.</summary>
    public int DriverSeat
    {
        get
        {
            for (int i = 0; i < Seats.Count; i++)
            {
                if (Seats[i].Drives)
                {
                    return i;
                }
            }

            return 0;
        }
    }

    public static VehicleParams From(VehicleDef d) => new()
    {
        Id = d.Id,
        DisplayName = d.DisplayName,
        Length = d.Length_m,
        Width = d.Width_m,
        SideHeight = d.SideHeight_m,
        Floor = d.Floor_m,
        Wheelbase = d.Wheelbase_m,
        MaxSpeed = d.MaxSpeed_mps,
        ReverseSpeed = d.ReverseSpeed_mps,
        Accel = d.Accel_mps2,
        Brake = d.Brake_mps2,
        Coast = d.Coast_mps2,
        MaxSteer = d.MaxSteer_deg * Units.DegreesToRadians,
        SteerRate = d.SteerRate_degps * Units.DegreesToRadians,
        EnterRadius = d.EnterRadius_m,
        ExitBelow = d.ExitBelow_mps,
        SeatedEyeHeight = d.SeatedEyeHeight_m,
        StopShort = d.StopShort_m,
        Brush = d.Brush_m,
        Seats = d.Seats.Select(s => new SeatParams(s.Id, new Vector3(s.At_m[0], s.At_m[1], s.At_m[2]), s.Drives, s.Fires)).ToArray(),
    };
}
