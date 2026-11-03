using System.Numerics;
using System.Runtime.CompilerServices;
using Pb.Sim.Collision;
using Pb.Sim.Core;

namespace Pb.Sim.Vehicles;

/// <summary>Four numbers, one per wheel (front left, front right, rear left, rear right), held inline.</summary>
[InlineArray(VehicleState.WheelCount)]
public struct WheelFloats
{
    private float _first;
}

/// <summary>
/// Everything about a vehicle's motion the next tick follows from: what a joining copy driving it puts back, as the
/// server had it, to replay its own driving from (M5.7).
/// </summary>
public struct VehicleMotion
{
    public Vector3 Position;
    public Quaternion Orientation;
    public Vector3 Velocity;
    public Vector3 AngularVelocity;
    public float Steer;
    public float Rpm;
    public float ShiftFor;
    public float UpsideDownFor;
    public float RestFor;
    public int Gear;
    public byte Contacts;
    public bool Asleep;
    public WheelFloats Compression;

    /// <summary>Close enough to <paramref name="other"/> that replaying from it would change nothing anyone could see.</summary>
    public readonly bool Matches(in VehicleMotion other, float position, float speed)
    {
        if (Vector3.DistanceSquared(Position, other.Position) > position * position ||
            Vector3.DistanceSquared(Velocity, other.Velocity) > speed * speed ||
            Vector3.DistanceSquared(AngularVelocity, other.AngularVelocity) > speed * speed ||
            MathF.Abs(Quaternion.Dot(Orientation, other.Orientation)) < 1f - 1e-4f ||
            Gear != other.Gear || Asleep != other.Asleep)
        {
            return false;
        }

        return true;
    }
}

/// <summary>
/// One vehicle as it stands (M5.7: a rigid body on four sprung wheels): where its body is and which way it's turned, how
/// it's moving and turning, its wheels (how far each spring is pressed, what each stands on, how fast each tyre scrubs
/// over the ground), its engine and gearbox, the driver's controls, and who's in each seat.
/// </summary>
public sealed class VehicleState
{
    public const int WheelCount = 4;

    internal VehicleState(int id, VehicleParams p, Vector3 position, float yaw)
    {
        Id = id;
        Params = p;
        Home = position;
        HomeYaw = yaw;
        Occupants = new int[p.Seats.Count];
        Array.Fill(Occupants, -1);
        PutAt(position, yaw);
    }

    public int Id { get; }

    public VehicleParams Params { get; }

    /// <summary>Where it stood when the round started.</summary>
    public Vector3 Home { get; }

    public float HomeYaw { get; }

    /// <summary>The body's own point: on the ground under its middle when it stands at rest on level ground.</summary>
    public Vector3 Position { get; internal set; }

    /// <summary>How the body is turned (its own x right, y up, −z ahead, as Godot's): heading, pitch and lean together.</summary>
    public Quaternion Orientation { get; internal set; } = Quaternion.Identity;

    /// <summary>Its centre of mass's velocity (m/s).</summary>
    public Vector3 Velocity { get; internal set; }

    /// <summary>How fast it turns, about each world axis (rad/s).</summary>
    public Vector3 AngularVelocity { get; internal set; }

    /// <summary>Last tick's, for drawing it smoothly between ticks.</summary>
    public Vector3 PreviousPosition { get; internal set; }

    public Quaternion PreviousOrientation { get; internal set; } = Quaternion.Identity;

    /// <summary>Which way it's heading on the level (the sim's yaw: 0 faces −Z, positive turns left).</summary>
    public float Yaw { get; private set; }

    public float PreviousYaw { get; private set; }

    /// <summary>Along its heading (m/s; negative backing up).</summary>
    public float Speed { get; private set; }

    /// <summary>The front wheels' turn (rad, positive to the left).</summary>
    public float Steer { get; internal set; }

    /// <summary>The driver's pedal this tick (−1 brake or reverse … 1 full throttle).</summary>
    public float Throttle { get; internal set; }

    /// <summary>The driver's wheel this tick (−1 full right … 1 full left).</summary>
    public float SteerInput { get; internal set; }

    /// <summary>The driver's handbrake this tick.</summary>
    public bool Handbrake { get; internal set; }

    /// <summary>How hard the engine was asked to push this tick (0 … 1), and the brakes (0 … 1): for the engine's note and the lights.</summary>
    public float Drive { get; internal set; }

    public float Braking { get; internal set; }

    /// <summary>−1 reverse, else the forward gear (1 up).</summary>
    public int Gear { get; internal set; } = 1;

    /// <summary>The engine's revs.</summary>
    public float Rpm { get; internal set; }

    /// <summary>A gear change under way: how long till it's in (s), with no drive meanwhile.</summary>
    public float ShiftFor { get; internal set; }

    /// <summary>How far each wheel's spring is pressed (m; at rest, <see cref="VehicleParams.StaticCompression"/>).</summary>
    public WheelFloats Compression;

    /// <summary>The load on each tyre (N).</summary>
    public WheelFloats Load;

    /// <summary>How fast each tyre scrubs over the ground (m/s): sliding sideways, spinning or locked.</summary>
    public WheelFloats Slide;

    /// <summary>Each wheel's turn about its axle (rad, for drawing), and how fast it's turning (rad/s).</summary>
    public WheelFloats Spin;

    public WheelFloats SpinRate;

    /// <summary>What each wheel stands on (when it touches the ground).</summary>
    public readonly SurfaceId[] Surface = new SurfaceId[WheelCount];

    /// <summary>Which wheels touch the ground (a bit each, front left first).</summary>
    public byte Contacts { get; internal set; }

    /// <summary>On its side or over and nearly still this long (s): righted at <see cref="VehicleParams.RightAfter"/>.</summary>
    public float UpsideDownFor { get; internal set; }

    /// <summary>Empty and still this long (s).</summary>
    public float RestFor { get; internal set; }

    /// <summary>At rest and left alone: it isn't stepped till someone gets in or something moves it.</summary>
    public bool Asleep { get; internal set; }

    /// <summary>Who's in each seat (player id), or −1.</summary>
    public int[] Occupants { get; }

    /// <summary>It hit something this tick (a wall, a tree, another vehicle), or stopped for someone in its way.</summary>
    public bool Blocked { get; internal set; }

    /// <summary>The hardest it hit anything this tick (m/s into it), for the bang.</summary>
    public float Impact { get; internal set; }

    /// <summary>How hard it can brake on what its wheels stand on (m/s²): the brakes, or the grip if that's less.</summary>
    public float StopDecel
    {
        get
        {
            float grip = 0f;
            int touching = 0;
            for (int i = 0; i < WheelCount; i++)
            {
                if (Touches(i))
                {
                    grip += Params.Tyres[Math.Min(Surface[i].Value, Params.Tyres.Length - 1)].Grip;
                    touching++;
                }
            }

            float mu = touching > 0 ? grip / touching : Params.LeastGrip;
            return MathF.Min(Params.BrakeDecel, mu * VehicleParams.Gravity);
        }
    }

    /// <summary>Its heading along the level (level), and its right.</summary>
    public Vector3 Forward => ViewAngles.FlatForward(Yaw);

    public Vector3 Right
    {
        get
        {
            Vector3 f = Forward;
            return new Vector3(-f.Z, 0f, f.X);
        }
    }

    /// <summary>The body's own axes in the world: ahead, to its right, its top.</summary>
    public Vector3 BodyForward => Vector3.Transform(-Vector3.UnitZ, Orientation);

    public Vector3 BodyRight => Vector3.Transform(Vector3.UnitX, Orientation);

    public Vector3 Up => Vector3.Transform(Vector3.UnitY, Orientation);

    public Vector3 CenterOfMass => ToWorld(new Vector3(0f, Params.ComHeight, 0f));

    public int Driver => Occupants[Params.DriverSeat];

    /// <summary>A point of the body (x to its right, y up from its own point, z ahead of its middle) in the world.</summary>
    public Vector3 ToWorld(Vector3 body) => Position + Vector3.Transform(new Vector3(body.X, body.Y, -body.Z), Orientation);

    /// <summary>How fast the body's point at <paramref name="world"/> is moving.</summary>
    public Vector3 PointVelocity(Vector3 world) => Velocity + Vector3.Cross(AngularVelocity, world - CenterOfMass);

    /// <summary>Where seat <paramref name="seat"/> is (world): the point the one in it sits on.</summary>
    public Vector3 Seat(int seat) => ToWorld(Params.Seats[seat].At);

    /// <summary>Whether wheel <paramref name="wheel"/> touches the ground.</summary>
    public bool Touches(int wheel) => (Contacts & (1 << wheel)) != 0;

    /// <summary>Which seat <paramref name="playerId"/> is in, or −1.</summary>
    public int SeatOf(int playerId)
    {
        for (int i = 0; i < Occupants.Length; i++)
        {
            if (Occupants[i] == playerId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Its motion, for a joining copy to put back.</summary>
    public VehicleMotion Capture() => new()
    {
        Position = Position,
        Orientation = Orientation,
        Velocity = Velocity,
        AngularVelocity = AngularVelocity,
        Steer = Steer,
        Rpm = Rpm,
        ShiftFor = ShiftFor,
        UpsideDownFor = UpsideDownFor,
        RestFor = RestFor,
        Gear = Gear,
        Contacts = Contacts,
        Asleep = Asleep,
        Compression = Compression,
    };

    /// <summary>Puts back a motion (the server's, on a joining copy driving it).</summary>
    public void Restore(in VehicleMotion m)
    {
        Position = m.Position;
        Orientation = m.Orientation;
        Velocity = m.Velocity;
        AngularVelocity = m.AngularVelocity;
        Steer = m.Steer;
        Rpm = m.Rpm;
        ShiftFor = m.ShiftFor;
        UpsideDownFor = m.UpsideDownFor;
        RestFor = m.RestFor;
        Gear = m.Gear;
        Contacts = m.Contacts;
        Asleep = m.Asleep;
        Compression = m.Compression;
        Settled();
    }

    /// <summary>Stood at <paramref name="position"/> facing <paramref name="yaw"/>, at rest on its wheels, engine idling.</summary>
    internal void PutAt(Vector3 position, float yaw)
    {
        Position = PreviousPosition = position;
        Orientation = PreviousOrientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        Velocity = AngularVelocity = Vector3.Zero;
        Steer = Throttle = SteerInput = Drive = Braking = ShiftFor = UpsideDownFor = RestFor = 0f;
        Handbrake = Blocked = Asleep = false;
        Gear = 1;
        Rpm = Params.IdleRpm;
        for (int i = 0; i < WheelCount; i++)
        {
            Compression[i] = Params.StaticCompression;
            Load[i] = Params.Mass * VehicleParams.Gravity / WheelCount;
            Slide[i] = SpinRate[i] = 0f;
        }

        Contacts = (1 << WheelCount) - 1;
        Settled();
        PreviousYaw = Yaw;
    }

    /// <summary>Last tick's pose, before this one moves it.</summary>
    internal void Remember()
    {
        PreviousPosition = Position;
        PreviousOrientation = Orientation;
        PreviousYaw = Yaw;
    }

    /// <summary>Works out what follows from its pose and motion (heading, speed along it).</summary>
    internal void Settled()
    {
        Vector3 f = BodyForward;
        if (f.X * f.X + f.Z * f.Z > 1e-6f)
        {
            Yaw = MathF.Atan2(-f.X, -f.Z);
        }

        Speed = Vector3.Dot(Velocity, ViewAngles.FlatForward(Yaw));
    }

    /// <summary>A joining copy: the vehicle as the server has it.</summary>
    internal void ApplyServer(Vector3 position, Quaternion orientation, float speed, float steer, float rpm, int gear, float drive,
        float braking, float slideFront, float slideRear, ReadOnlySpan<int> occupants)
    {
        Remember();
        Position = position;
        Orientation = orientation;
        Steer = steer;
        Rpm = rpm;
        Gear = gear;
        Drive = drive;
        Braking = braking;
        Slide[0] = Slide[1] = slideFront;
        Slide[2] = Slide[3] = slideRear;
        Settled();
        Speed = speed;
        Velocity = Forward * speed;
        for (int i = 0; i < Occupants.Length && i < occupants.Length; i++)
        {
            Occupants[i] = occupants[i];
        }
    }
}
