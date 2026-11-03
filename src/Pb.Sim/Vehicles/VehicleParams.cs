using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Data;

namespace Pb.Sim.Vehicles;

/// <summary>One seat: where it is in the vehicle's frame (x right, y up, z ahead), and whether its occupant drives or fires.</summary>
public readonly record struct SeatParams(string Id, Vector3 At, bool Drives, bool Fires, float SpreadScale = 1f);

/// <summary>What a surface gives a tyre: grip (a friction coefficient) and rolling resistance (likewise).</summary>
public readonly record struct TyreGrip(float Grip, float Rolling);

/// <summary>A kind of vehicle (SI), from vehicles.jsonc, with the constants its dynamics work from.</summary>
public sealed class VehicleParams
{
    public const float Gravity = 9.81f;

    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required float Length { get; init; }

    public required float Width { get; init; }

    public required float SideHeight { get; init; }

    public required float Floor { get; init; }

    public required float Wheelbase { get; init; }

    public required float Track { get; init; }

    public required float WheelRadius { get; init; }

    public required float Mass { get; init; }

    public required float ComHeight { get; init; }

    public required float MountHeight { get; init; }

    /// <summary>Each wheel's spring (N/m) and damper (N·s/m), from the bounce frequency and damping ratio.</summary>
    public required float SpringRate { get; init; }

    public required float DamperRate { get; init; }

    /// <summary>How far a spring is compressed with the body at rest on four wheels (m).</summary>
    public required float StaticCompression { get; init; }

    /// <summary>From a wheel's mount to its centre with the spring unloaded (m): the ray down is this plus the radius.</summary>
    public required float RestLength { get; init; }

    public required float BumpTravel { get; init; }

    public required float AntiRoll { get; init; }

    public required IReadOnlyList<float> GearSpeeds { get; init; }

    public required IReadOnlyList<float> DriveForce { get; init; }

    public required float ReverseSpeed { get; init; }

    public required float ReverseForce { get; init; }

    public required float IdleRpm { get; init; }

    public required float MaxRpm { get; init; }

    /// <summary>The drive force holds to this share of the red line, then falls away to none at it.</summary>
    public required float PowerBand { get; init; }

    /// <summary>The gearbox changes up at this share of the gear's top speed, and down below this share of the one under it.</summary>
    public required float ShiftUp { get; init; }

    public required float ShiftDown { get; init; }

    /// <summary>A gear change takes this long (s), with no drive.</summary>
    public required float Shift { get; init; }

    /// <summary>The front wheels' share of the drive (0: rear-wheel drive).</summary>
    public required float FrontDrive { get; init; }

    /// <summary>How fast the revs can rise or fall (rpm/s).</summary>
    public required float RevRate { get; init; }

    public required float EngineBrake { get; init; }

    public required float BrakeForce { get; init; }

    /// <summary>The front wheels' share of the braking.</summary>
    public required float BrakeFront { get; init; }

    /// <summary>A locked wheel slides, pushing back with this share of its grip, all of it against the way it slides.</summary>
    public required float HandbrakeGrip { get; init; }

    public required float Drag { get; init; }

    public required float CorneringStiffness { get; init; }

    /// <summary>Below this speed (m/s) a tyre's slip angle is taken against it, so a crawl doesn't make the angle wild.</summary>
    public required float SlipSpeed { get; init; }

    /// <summary>The body hitting something: how much it bounces back, and its friction scraping along.</summary>
    public required float BodyBounce { get; init; }

    public required float BodyFriction { get; init; }

    /// <summary>Still enough to be at rest (m/s, and rad/s turning): an empty vehicle at rest sleeps till something moves it.</summary>
    public required float RestBelow { get; init; }

    /// <summary>Off the pedals (or empty) and slower than this (m/s), the brakes hold it still.</summary>
    public required float HoldBelow { get; init; }

    public required float MaxSteer { get; init; }

    public required float SteerAtSpeed { get; init; }

    public required float SteerRate { get; init; }

    public required float RightAfter { get; init; }

    /// <summary>The body's moments of inertia about its own right, up and forward axes (kg·m²): a box of its size.</summary>
    public required Vector3 Inertia { get; init; }

    public required float EnterRadius { get; init; }

    public required float ExitBelow { get; init; }

    public required float StopShort { get; init; }

    public required float Brush { get; init; }

    public required float SeatedEyeHeight { get; init; }

    public required IReadOnlyList<SeatParams> Seats { get; init; }

    /// <summary>What each surface (by its id) gives a tyre.</summary>
    public required TyreGrip[] Tyres { get; init; }

    /// <summary>The field's speed limit: the top gear's speed (m/s).</summary>
    public float MaxSpeed => GearSpeeds[^1];

    /// <summary>The hardest it slows on the brakes alone (m/s²), for how far it needs to stop.</summary>
    public float BrakeDecel => BrakeForce / Mass;

    /// <summary>The least grip any surface gives its tyres: to plan by when it isn't on the ground.</summary>
    public float LeastGrip
    {
        get
        {
            float least = float.MaxValue;
            foreach (TyreGrip t in Tyres)
            {
                least = MathF.Min(least, t.Grip);
            }

            return least;
        }
    }

    /// <summary>Wheel <paramref name="i"/>'s mount in the body's frame (x right, y up, z ahead): front left, front right, rear left, rear right.</summary>
    public Vector3 WheelMount(int i) => new((i % 2 == 0 ? -0.5f : 0.5f) * Track, MountHeight, (i < 2 ? 0.5f : -0.5f) * Wheelbase);

    /// <summary>The spheres the body's bumps are found with (body frame, z ahead): two rows of three, filling it from just above the wheels' reach to its sides' top.</summary>
    public required Vector3[] Probes { get; init; }

    public required float ProbeRadius { get; init; }

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

    public static VehicleParams From(VehicleDef d, SurfaceRegistry surfaces, IReadOnlyList<TyreDef> tyres)
    {
        float perWheel = d.Mass_kg / 4f;
        float omega = MathF.Tau * d.SuspensionHz;
        float k = perWheel * omega * omega;
        float c = 2f * d.SuspensionDamping * MathF.Sqrt(k * perWheel);
        float sag = perWheel * Gravity / k;
        TyreDef fallback = tyres.First(t => t.Surface == "default");
        var table = new TyreGrip[Math.Max(1, surfaces.Count)];
        for (int i = 0; i < table.Length; i++)
        {
            string name = i < surfaces.Count ? surfaces.Names[i] : "";
            TyreDef t = tyres.FirstOrDefault(x => x.Surface == name) ?? fallback;
            table[i] = new TyreGrip(t.Grip, t.Rolling);
        }

        float l = d.Length_m, w = d.Width_m, h = d.SideHeight_m;
        // The body's bumps: spheres as wide as fits two across, from the top of the sides down (they stop short of the
        // ground: the wheels hold the body off it), at its ends and middle.
        float pr = MathF.Min(w * 0.25f, h * 0.4f);
        var probes = new Vector3[6];
        for (int i = 0; i < 6; i++)
        {
            probes[i] = new Vector3((i % 2 == 0 ? -1f : 1f) * (w * 0.5f - pr), h - pr, (i / 2 - 1) * (l * 0.5f - pr));
        }

        return new VehicleParams
        {
            Id = d.Id,
            DisplayName = d.DisplayName,
            Length = l,
            Width = w,
            SideHeight = h,
            Floor = d.Floor_m,
            Wheelbase = d.Wheelbase_m,
            Track = d.Track_m,
            WheelRadius = d.WheelRadius_m,
            Mass = d.Mass_kg,
            ComHeight = d.ComHeight_m,
            MountHeight = d.MountHeight_m,
            SpringRate = k,
            DamperRate = c,
            StaticCompression = sag,
            RestLength = d.MountHeight_m - d.WheelRadius_m + sag,
            BumpTravel = d.BumpTravel_m,
            AntiRoll = d.AntiRoll_Npm,
            GearSpeeds = d.GearSpeeds_mps,
            DriveForce = d.DriveForce_N,
            ReverseSpeed = d.ReverseSpeed_mps,
            ReverseForce = d.ReverseForce_N,
            IdleRpm = d.IdleRpm,
            MaxRpm = d.MaxRpm,
            PowerBand = d.PowerBand,
            ShiftUp = d.ShiftUp,
            ShiftDown = d.ShiftDown,
            Shift = d.Shift_s,
            FrontDrive = d.FrontDrive,
            RevRate = d.RevRate_rpmps,
            EngineBrake = d.EngineBrake_N,
            BrakeForce = d.BrakeForce_N,
            BrakeFront = d.BrakeFront,
            HandbrakeGrip = d.HandbrakeGrip,
            Drag = d.Drag,
            CorneringStiffness = d.CorneringStiffness,
            SlipSpeed = d.SlipSpeed_mps,
            BodyBounce = d.BodyBounce,
            BodyFriction = d.BodyFriction,
            RestBelow = d.RestBelow_mps,
            HoldBelow = d.HoldBelow_mps,
            MaxSteer = d.MaxSteer_deg * Units.DegreesToRadians,
            SteerAtSpeed = d.SteerAtSpeed,
            SteerRate = d.SteerRate_degps * Units.DegreesToRadians,
            RightAfter = d.RightAfter_s,
            Inertia = new Vector3(d.Mass_kg / 12f * (h * h + l * l), d.Mass_kg / 12f * (w * w + l * l), d.Mass_kg / 12f * (w * w + h * h)),
            EnterRadius = d.EnterRadius_m,
            ExitBelow = d.ExitBelow_mps,
            StopShort = d.StopShort_m,
            Brush = d.Brush_m,
            SeatedEyeHeight = d.SeatedEyeHeight_m,
            Seats = d.Seats.Select(s => new SeatParams(s.Id, new Vector3(s.At_m[0], s.At_m[1], s.At_m[2]), s.Drives, s.Fires, s.SpreadScale)).ToArray(),
            Tyres = table,
            Probes = probes,
            ProbeRadius = pr,
        };
    }
}
