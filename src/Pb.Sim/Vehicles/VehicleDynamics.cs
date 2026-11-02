using System.Numerics;
using Pb.Sim.Collision;

namespace Pb.Sim.Vehicles;

/// <summary>What the vehicle's controls come to this tick, once the rules have had their say.</summary>
internal struct VehicleControls
{
    /// <summary>The engine's push at the wheels, all of them together (N; negative in reverse).</summary>
    public float Drive;

    /// <summary>The engine holding it back off the throttle, all the driven wheels together (N).</summary>
    public float EngineBrake;

    /// <summary>The brakes (0 … 1).</summary>
    public float Brake;

    public bool Handbrake;

    /// <summary>Where the front wheels are being turned to (rad, positive left).</summary>
    public float Steer;
}

/// <summary>
/// A vehicle's motion (M5.7): a rigid body on four sprung wheels, stepped in substeps. Each wheel's ray down from its
/// mount finds the ground; its spring and damper push the body up there (and the anti-roll bars share it across each
/// axle), so the load on each tyre shifts as the body pitches and leans. Each tyre pushes along the ground as the
/// engine, the brakes and rolling ask, and sideways against its slip angle, together never more than the load times
/// the surface's grip (the friction circle): past that it slides. The handbrake's locked wheels slide, pushing back only
/// against the way they slide. Engine (an automatic gearbox, revs and the power falling away at the red line) and steering
/// once a tick; everything else per substep. Never allocates.
/// </summary>
internal static class VehicleDynamics
{
    public const int Substeps = 2;

    /// <summary>The bump stop: this many times the spring's rate past the travel, and the damper's.</summary>
    private const float BumpStop = 8f;

    /// <summary>The most load a tyre takes (times the vehicle's weight): a guard against a wheel's ray starting in the ground.</summary>
    private const float MaxLoad = 4f;

    /// <summary>The engine (once a tick): the gear, the revs, and what the controls ask of the wheels.</summary>
    public static VehicleControls Engine(VehicleState v, float drive, float brake, bool reverse, bool handbrake, bool running, float dt)
    {
        VehicleParams k = v.Params;
        float speed = v.Speed;
        int top = k.GearSpeeds.Count;

        // The gearbox: into reverse (or out of it) from a standstill, else up and down the gears with the speed.
        if (reverse && v.Gear != -1)
        {
            v.Gear = -1;
            v.ShiftFor = k.Shift;
        }
        else if (!reverse && v.Gear == -1 && drive > 0f)
        {
            v.Gear = 1;
            v.ShiftFor = k.Shift;
        }
        else if (v.Gear >= 1)
        {
            float forward = MathF.Max(0f, speed);
            if (v.Gear < top && forward > k.GearSpeeds[v.Gear - 1] * k.ShiftUp)
            {
                v.Gear++;
                v.ShiftFor = k.Shift;
            }
            else if (v.Gear > 1 && forward < k.GearSpeeds[v.Gear - 2] * k.ShiftDown)
            {
                v.Gear--;
                v.ShiftFor = k.Shift;
            }
        }

        bool shifting = v.ShiftFor > 0f;
        v.ShiftFor = MathF.Max(0f, v.ShiftFor - dt);

        // The revs follow the wheels in gear (the clutch slipping below a crawl, as the throttle has them), and flare as
        // the driven wheels spin.
        float gearTop = v.Gear == -1 ? k.ReverseSpeed : k.GearSpeeds[v.Gear - 1];
        float spin = 0f;
        for (int i = 0; i < VehicleState.WheelCount; i++)
        {
            spin = MathF.Max(spin, v.Slide[i]);
        }

        float share = MathF.Abs(speed) / gearTop;
        float clutch = k.IdleRpm + (drive > 0f && running ? drive * (0.45f * k.MaxRpm - k.IdleRpm) : 0f);
        float revs = running ? MathF.Max(clutch, k.MaxRpm * MathF.Min(1.05f, share + (drive > 0f ? 0.5f * spin / gearTop : 0f))) : 0f;
        v.Rpm += Math.Clamp(revs - v.Rpm, -k.RevRate * dt, k.RevRate * dt);

        float power = share <= k.PowerBand ? 1f : MathF.Max(0f, (1f - share) / (1f - k.PowerBand));
        float force = v.Gear == -1 ? -k.ReverseForce : k.DriveForce[v.Gear - 1];
        v.Drive = running && !shifting ? drive : 0f;
        v.Braking = brake;
        return new VehicleControls
        {
            Drive = v.Drive * force * power,
            EngineBrake = drive <= 0.05f && brake <= 0f && v.Gear >= 1 ? k.EngineBrake * MathF.Min(1f, share * 2f) : 0f,
            Brake = brake,
            Handbrake = handbrake,
            Steer = v.SteerInput * k.MaxSteer * (1f - (1f - k.SteerAtSpeed) * Math.Clamp(MathF.Abs(speed) / k.MaxSpeed, 0f, 1f)),
        };
    }

    /// <summary>Moves the vehicle a tick (<see cref="Substeps"/> of them) under <paramref name="c"/>.</summary>
    public static void Step(VehicleState v, CollisionWorld world, in VehicleControls c, float dt)
    {
        float steerTo = v.Driver >= 0 ? c.Steer : 0f;
        v.Steer += Math.Clamp(steerTo - v.Steer, -v.Params.SteerRate * dt, v.Params.SteerRate * dt);
        float h = dt / Substeps;
        for (int s = 0; s < Substeps; s++)
        {
            Substep(v, world, c, h);
        }

        v.Settled();
    }

    private static void Substep(VehicleState v, CollisionWorld world, in VehicleControls c, float h)
    {
        VehicleParams k = v.Params;
        Quaternion q = v.Orientation;
        Vector3 up = Vector3.Transform(Vector3.UnitY, q);
        Vector3 right = Vector3.Transform(Vector3.UnitX, q);
        Vector3 ahead = Vector3.Transform(-Vector3.UnitZ, q);
        Vector3 com = v.Position + up * k.ComHeight;
        float reach = k.RestLength + k.WheelRadius;
        float stop = k.StaticCompression + k.BumpTravel;
        float weight = k.Mass * VehicleParams.Gravity;

        // The springs: where each wheel's ray down from its mount meets the ground, and how hard each pushes there.
        Span<Vector3> at = stackalloc Vector3[VehicleState.WheelCount];
        Span<Vector3> ground = stackalloc Vector3[VehicleState.WheelCount];
        Span<float> pressed = stackalloc float[VehicleState.WheelCount];
        Span<float> load = stackalloc float[VehicleState.WheelCount];
        byte contacts = 0;
        for (int i = 0; i < VehicleState.WheelCount; i++)
        {
            Vector3 mount = v.ToWorld(k.WheelMount(i));
            pressed[i] = 0f;
            load[i] = 0f;
            if (!world.SweepSphere(mount, mount - up * reach, 0f, out SweepHit hit, includeDynamic: false))
            {
                continue;
            }

            contacts |= (byte)(1 << i);
            at[i] = hit.Point;
            ground[i] = hit.Normal;
            v.Surface[i] = hit.Surface;
            float x = k.RestLength - (hit.T * reach - k.WheelRadius);
            pressed[i] = MathF.Max(0f, x);
            // How fast it's being pressed: from last substep's, or (just touching down) how fast the mount comes down.
            float rate = v.Touches(i)
                ? (pressed[i] - v.Compression[i]) / h
                : MathF.Max(0f, -Vector3.Dot(v.Velocity + Vector3.Cross(v.AngularVelocity, mount - com), up));
            float push = k.SpringRate * pressed[i] + k.DamperRate * rate;
            float over = pressed[i] - stop;
            if (over > 0f)
            {
                push += k.SpringRate * BumpStop * MathF.Min(over, 0.1f) + k.DamperRate * BumpStop * MathF.Max(0f, rate);
            }

            load[i] = push;
        }

        // The anti-roll bars: each axle's leaning resisted, load moved to the side pressed harder.
        float frontBar = k.AntiRoll * (pressed[0] - pressed[1]), rearBar = k.AntiRoll * (pressed[2] - pressed[3]);
        load[0] += frontBar;
        load[1] -= frontBar;
        load[2] += rearBar;
        load[3] -= rearBar;

        Vector3 force = new(0f, -weight, 0f);
        Vector3 torque = Vector3.Zero;
        float cos = MathF.Cos(v.Steer), sin = MathF.Sin(v.Steer);
        float each = k.Mass / VehicleState.WheelCount;
        for (int i = 0; i < VehicleState.WheelCount; i++)
        {
            v.Compression[i] = pressed[i];
            float n = (contacts & (1 << i)) != 0 ? Math.Clamp(load[i], 0f, weight * MaxLoad) : 0f;
            v.Load[i] = n;
            if (n <= 0f)
            {
                v.Slide[i] = 0f;
                continue;
            }

            // The tyre's own directions along the ground: the way it rolls (the front ones steered) and its right.
            Vector3 g = ground[i];
            bool front = i < 2;
            Vector3 heading = front ? ahead * cos - right * sin : ahead;
            Vector3 along = heading - g * Vector3.Dot(heading, g);
            float length = along.Length();
            if (length < 1e-3f)
            {
                continue;
            }

            along /= length;
            Vector3 side = Vector3.Cross(along, g);
            Vector3 p = at[i];
            Vector3 velocity = v.Velocity + Vector3.Cross(v.AngularVelocity, p - com);
            float vf = Vector3.Dot(velocity, along), vs = Vector3.Dot(velocity, side);
            TyreGrip tyre = k.Tyres[Math.Min(v.Surface[i].Value, k.Tyres.Length - 1)];
            float grip = tyre.Grip * n;
            Vector3 f;
            if (!front && c.Handbrake)
            {
                // Locked: it slides, pushing back only against the way it slides (never past stopping it).
                float sliding = MathF.Sqrt(vf * vf + vs * vs);
                f = sliding > 1e-4f ? -(along * vf + side * vs) * (MathF.Min(grip * k.HandbrakeGrip, sliding * each / h) / sliding) : Vector3.Zero;
                v.Slide[i] = sliding;
                v.SpinRate[i] = 0f;
            }
            else
            {
                // Along: the engine's push, less what holds it back (rolling, the engine off the throttle, the brakes),
                // which only ever stops the wheel, never turns it back; the brakes no harder than the grip (they don't lock).
                float driveShare = (front ? k.FrontDrive : 1f - k.FrontDrive) * 0.5f;
                float brakeShare = (front ? k.BrakeFront : 1f - k.BrakeFront) * 0.5f;
                float drive = c.Drive * driveShare;
                float hold = tyre.Rolling * n + c.EngineBrake * driveShare + MathF.Min(c.Brake * k.BrakeForce * brakeShare, grip);
                float fx = drive - MathF.Sign(vf) * MathF.Min(hold, MathF.Abs(vf) * each / h);

                // Sideways: against its slip angle, levelling off at its grip (never past stopping the slide).
                float slip = MathF.Atan2(vs, MathF.Max(MathF.Abs(vf), k.SlipSpeed));
                float fy = -grip * MathF.Tanh(k.CorneringStiffness * slip / tyre.Grip);
                float stopSide = MathF.Abs(vs) * each / h;
                fy = Math.Clamp(fy, -stopSide, stopSide);

                // Together never more than its grip: past that it slides (and a driven wheel asked for more spins).
                float total = MathF.Sqrt(fx * fx + fy * fy);
                float spin = 0f;
                if (total > grip)
                {
                    float scale = grip / total;
                    fx *= scale;
                    fy *= scale;
                    spin = MathF.Abs(drive) > grip ? MathF.Min(8f, (MathF.Abs(drive) - grip) / grip * 3f) : 0f;
                }

                f = along * fx + side * fy;
                v.Slide[i] = MathF.Sqrt(vs * vs + spin * spin);
                v.SpinRate[i] = (vf + MathF.Sign(drive) * spin) / k.WheelRadius;
            }

            Vector3 total3 = f + up * n;
            force += total3;
            torque += Vector3.Cross(p - com, total3);
            v.Spin[i] = (v.Spin[i] + v.SpinRate[i] * h) % MathF.Tau;
        }

        for (int i = 0; i < VehicleState.WheelCount; i++)
        {
            if ((contacts & (1 << i)) == 0)
            {
                // In the air the wheel spins down slowly (the engine's ones keep turning as it pushes).
                v.SpinRate[i] *= 0.995f;
                v.Spin[i] = (v.Spin[i] + v.SpinRate[i] * h) % MathF.Tau;
            }
        }

        v.Contacts = contacts;

        // Air drag, then the body moved and turned.
        force -= v.Velocity * (k.Drag * v.Velocity.Length());
        Vector3 velocityNext = v.Velocity + force * (h / k.Mass);
        Vector3 spinNext = v.AngularVelocity + InverseInertia(q, k.Inertia, torque) * h;
        com += velocityNext * h;
        Quaternion turn = new Quaternion(spinNext * (0.5f * h), 0f) * q;
        q = Quaternion.Normalize(new Quaternion(q.X + turn.X, q.Y + turn.Y, q.Z + turn.Z, q.W + turn.W));
        v.Velocity = velocityNext;
        v.AngularVelocity = spinNext;
        v.Orientation = q;
        v.Position = com - Vector3.Transform(Vector3.UnitY, q) * k.ComHeight;
    }

    /// <summary>The body's inverse inertia (about its centre of mass) applied to a world vector.</summary>
    public static Vector3 InverseInertia(Quaternion q, Vector3 inertia, Vector3 w)
    {
        Vector3 b = Vector3.Transform(w, Quaternion.Conjugate(q));
        return Vector3.Transform(new Vector3(b.X / inertia.X, b.Y / inertia.Y, b.Z / inertia.Z), q);
    }

    /// <summary>
    /// The body knocked at <paramref name="point"/> against something with outward normal <paramref name="normal"/>: no
    /// longer moving into it (bouncing back <paramref name="bounce"/> of the way it came), and scraping along it with
    /// <paramref name="friction"/>. Returns how fast it was going into it.
    /// </summary>
    public static float Knock(VehicleState v, Vector3 point, Vector3 normal, float bounce, float friction)
    {
        VehicleParams k = v.Params;
        Quaternion q = v.Orientation;
        Vector3 r = point - v.CenterOfMass;
        Vector3 velocity = v.Velocity + Vector3.Cross(v.AngularVelocity, r);
        float into = Vector3.Dot(velocity, normal);
        if (into >= 0f)
        {
            return 0f;
        }

        float soft = 1f / k.Mass + Vector3.Dot(normal, Vector3.Cross(InverseInertia(q, k.Inertia, Vector3.Cross(r, normal)), r));
        float j = -(1f + bounce) * into / soft;
        Vector3 impulse = normal * j;
        Vector3 skid = velocity - normal * into;
        float skidding = skid.Length();
        if (skidding > 1e-4f)
        {
            Vector3 t = skid / skidding;
            float softT = 1f / k.Mass + Vector3.Dot(t, Vector3.Cross(InverseInertia(q, k.Inertia, Vector3.Cross(r, t)), r));
            impulse -= t * MathF.Min(friction * j, skidding / softT);
        }

        v.Velocity += impulse / k.Mass;
        v.AngularVelocity += InverseInertia(q, k.Inertia, Vector3.Cross(r, impulse));
        return -into;
    }

    /// <summary>
    /// Two bodies' bumps touching at <paramref name="point"/> along <paramref name="normal"/> (from <paramref name="b"/> to
    /// <paramref name="a"/>): neither goes on into the other. <paramref name="moveB"/> false treats <paramref name="b"/> as
    /// immovable (a joining copy's own vehicle against one the server has).
    /// </summary>
    public static void Knock(VehicleState a, VehicleState b, Vector3 point, Vector3 normal, bool moveB)
    {
        Vector3 ra = point - a.CenterOfMass, rb = point - b.CenterOfMass;
        Vector3 relative = a.Velocity + Vector3.Cross(a.AngularVelocity, ra) - (b.Velocity + Vector3.Cross(b.AngularVelocity, rb));
        float into = Vector3.Dot(relative, normal);
        if (into >= 0f)
        {
            return;
        }

        VehicleParams ka = a.Params, kb = b.Params;
        float soft = 1f / ka.Mass + Vector3.Dot(normal, Vector3.Cross(InverseInertia(a.Orientation, ka.Inertia, Vector3.Cross(ra, normal)), ra));
        if (moveB)
        {
            soft += 1f / kb.Mass + Vector3.Dot(normal, Vector3.Cross(InverseInertia(b.Orientation, kb.Inertia, Vector3.Cross(rb, normal)), rb));
        }

        float bounce = MathF.Min(ka.BodyBounce, kb.BodyBounce);
        Vector3 impulse = normal * (-(1f + bounce) * into / soft);
        a.Velocity += impulse / ka.Mass;
        a.AngularVelocity += InverseInertia(a.Orientation, ka.Inertia, Vector3.Cross(ra, impulse));
        if (moveB)
        {
            b.Velocity -= impulse / kb.Mass;
            b.AngularVelocity -= InverseInertia(b.Orientation, kb.Inertia, Vector3.Cross(rb, impulse));
        }
    }
}
