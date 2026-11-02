using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Players;

namespace Pb.Sim.Vehicles;

/// <summary>
/// A vehicle's body as a paint collider: an oriented box from just off the ground up to its sides' top, following the
/// vehicle (leaning and pitching with it), filed in the collision grid under the whole level's bounds (it can be
/// anywhere), so the broadphase never needs rebuilding as it moves. Balls break on it and never count.
/// </summary>
public sealed class VehicleShape : Shape
{
    private Aabb _anywhere;

    public Vector3 Center { get; internal set; }

    public Vector3 AxisX { get; internal set; } = Vector3.UnitX;

    public Vector3 AxisY { get; internal set; } = Vector3.UnitY;

    public Vector3 AxisZ { get; internal set; } = Vector3.UnitZ;

    public Vector3 Half { get; internal set; }

    public override Aabb Bounds => _anywhere;

    internal void SetAnywhere(Aabb bounds) => _anywhere = bounds;

    public override bool Sweep(Vector3 p0, Vector3 d, float r, out float t, out Vector3 normal)
    {
        // Filed everywhere, it's asked about every ball in the level: most pass nowhere near it, so a sphere round the box
        // turns them away before the box itself is tested.
        float reach = Half.Length() + r;
        float along = Math.Clamp(Vector3.Dot(Center - p0, d) / MathF.Max(d.LengthSquared(), 1e-12f), 0f, 1f);
        if (Vector3.DistanceSquared(p0 + d * along, Center) > reach * reach)
        {
            t = 0f;
            normal = default;
            return false;
        }

        return BoxShape.SweepOriented(Center, AxisX, AxisY, AxisZ, Half, p0, d, r, out t, out normal);
    }
}

/// <summary>
/// The level's vehicles (Phase 5; M5.7 their handling): where they are, who's in them, and how they drive, decided by the
/// sim so the server rules and bots drive by the same rules as people (vehicles.jsonc lists the field's rules). Each
/// tick: interact gets a player in (the nearest free seat in reach, the vehicle near enough stopped) or out (beside their
/// seat, the vehicle nearly stopped); the driver's move input works the pedal (forward and back: throttle, brake, reverse)
/// and the wheel (left and right), jump the handbrake; each vehicle moves as a sprung rigid body
/// (<see cref="VehicleDynamics"/>), knocks against the level, the level's edge and other vehicles, and never touches
/// anyone standing in its way (it brakes for them, and stops dead rather than come closer than brushing past); whoever's in
/// it sits in their seat, moving with it. Someone hit gets out. An empty vehicle at rest sleeps. Steps without allocating.
/// </summary>
public sealed class VehicleSet
{
    /// <summary>Empty and still this long (s), a vehicle sleeps till something moves it.</summary>
    private const float SleepAfter = 0.5f;

    private readonly List<VehicleState> _vehicles = new();
    private readonly List<VehicleShape> _shapes = new();
    private Aabb _bounds;

    public IReadOnlyList<VehicleState> All => _vehicles;

    public int Count => _vehicles.Count;

    public VehicleState this[int i] => _vehicles[i];

    /// <summary>Puts the level's vehicles where they stand, their bodies into the paint world.</summary>
    internal void Load(IReadOnlyList<VehicleSpot> spots, IReadOnlyDictionary<string, VehicleParams> kinds, CollisionWorld world, Aabb bounds,
        SurfaceId body)
    {
        _vehicles.Clear();
        _shapes.Clear();
        _bounds = bounds;
        foreach (VehicleSpot spot in spots)
        {
            if (!kinds.TryGetValue(spot.Vehicle, out VehicleParams? p))
            {
                throw new InvalidOperationException($"the level has a '{spot.Vehicle}', which vehicles.jsonc doesn't");
            }

            var vehicle = new VehicleState(_vehicles.Count, p, spot.Position, spot.Yaw);
            var shape = new VehicleShape();
            shape.SetAnywhere(new Aabb(bounds.Min - new Vector3(10f), bounds.Max + new Vector3(10f)));
            _vehicles.Add(vehicle);
            _shapes.Add(shape);
            Pose(vehicle.Id);
            world.Add(shape, body, $"vehicle#{vehicle.Id}:{p.Id}", dynamic: true);
        }
    }

    /// <summary>A new round: every vehicle back where it stood, empty and still.</summary>
    internal void Reset(IReadOnlyList<PlayerState> players)
    {
        for (int i = 0; i < _vehicles.Count; i++)
        {
            VehicleState v = _vehicles[i];
            v.PutAt(v.Home, v.HomeYaw);
            Array.Fill(v.Occupants, -1);
            Pose(i);
        }

        foreach (PlayerState p in players)
        {
            p.Vehicle = -1;
            p.Seat = -1;
        }
    }

    /// <summary>The vehicle <paramref name="player"/> is in, or null.</summary>
    public VehicleState? Of(PlayerState player) => player.Vehicle >= 0 && player.Vehicle < _vehicles.Count ? _vehicles[player.Vehicle] : null;

    /// <summary>Whether <paramref name="player"/> may fire where they sit (anyone not in a vehicle may).</summary>
    public bool MayFire(PlayerState player) => Of(player) is not { } v || player.Seat < 0 || v.Params.Seats[player.Seat].Fires;

    /// <summary>How much wider <paramref name="player"/>'s shots go where they sit (a driver's one-handed aim); 1 out of a vehicle.</summary>
    public float SpreadScale(PlayerState player) =>
        Of(player) is { } v && player.Seat >= 0 && player.Seat < v.Params.Seats.Count ? v.Params.Seats[player.Seat].SpreadScale : 1f;

    /// <summary>
    /// The free seat nearest <paramref name="position"/> within reach (of its side of the vehicle), the driver's first among
    /// equals, of a vehicle slow enough to get into, or (−1, −1).
    /// </summary>
    public (int Vehicle, int Seat) FreeSeatNear(Vector3 position)
    {
        int best = -1, bestSeat = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < _vehicles.Count; i++)
        {
            VehicleState v = _vehicles[i];
            if (MathF.Abs(v.Speed) > v.Params.ExitBelow || v.Up.Y < 0.5f)
            {
                continue;
            }

            for (int s = 0; s < v.Occupants.Length; s++)
            {
                if (v.Occupants[s] >= 0)
                {
                    continue;
                }

                Vector3 door = Door(v, s);
                float d = Flat(door - position);
                if (d <= v.Params.EnterRadius && d < bestDistance - (v.Params.Seats[s].Drives ? 0.3f : 0f))
                {
                    best = i;
                    bestSeat = s;
                    bestDistance = d;
                }
            }
        }

        return (best, bestSeat);
    }

    /// <summary>
    /// A press of interact (<paramref name="pressed"/>): out of the vehicle they're in, if it's slow enough (beside it,
    /// wherever there's room); or into the nearest free seat in reach of its door (walking or running up to it). True if
    /// it got anyone in or out, or was used up trying (a door doesn't get the press as well).
    /// </summary>
    internal bool Interact(SimWorld sim, PlayerState player, bool pressed)
    {
        if (!pressed)
        {
            return false;
        }

        if (Of(player) is { } inside)
        {
            if (MathF.Abs(inside.Speed) > inside.Params.ExitBelow)
            {
                return true; // too fast to get out: the press is used up anyway
            }

            GetOut(sim, inside, player);
            return true;
        }

        if (!player.Alive || !player.Present || player.OnLadder)
        {
            return false;
        }

        (int vehicle, int seat) = FreeSeatNear(player.Position);
        if (vehicle < 0)
        {
            return false;
        }

        GetIn(sim, _vehicles[vehicle], seat, player);
        return true;
    }

    /// <summary>Puts <paramref name="player"/> into <paramref name="seat"/> (free) of <paramref name="v"/>.</summary>
    internal void GetIn(SimWorld sim, VehicleState v, int seat, PlayerState player)
    {
        v.Occupants[seat] = player.Id;
        v.Asleep = false;
        v.RestFor = 0f;
        player.Vehicle = v.Id;
        player.Seat = seat;
        player.Sprinting = false;
        Seat(v, seat, player);
        sim.Events.Add(new SimEvent
        {
            Type = SimEventType.VehicleEntered, Tick = sim.Tick, PlayerId = player.Id, TargetId = v.Id, Team = player.Team, Extra = seat,
            Position = player.Position, ColliderId = -1,
        });
    }

    /// <summary>Takes <paramref name="player"/> out of <paramref name="v"/>, on their seat's side if there's room, else the other, else behind.</summary>
    internal void GetOut(SimWorld sim, VehicleState v, PlayerState player)
    {
        int seat = v.SeatOf(player.Id);
        if (seat >= 0)
        {
            v.Occupants[seat] = -1;
        }

        Vector3 door = seat >= 0 ? Door(v, seat) : v.Position + v.Right * (v.Params.Width * 0.5f + 0.6f);
        Span<Vector3> spots = stackalloc Vector3[3];
        spots[0] = door;
        spots[1] = v.Position - (door - v.Position) with { Y = 0f };
        spots[2] = v.Position - v.Forward * (v.Params.Length * 0.5f + 0.7f);
        Vector3 at = door;
        foreach (Vector3 spot in spots)
        {
            if (!sim.Collision.SweepSphere(spot + new Vector3(0f, 1.2f, 0f), spot + new Vector3(0f, 0.5f, 0f), 0.35f, out _, includeDynamic: false))
            {
                at = spot;
                break;
            }
        }

        player.Vehicle = -1;
        player.Seat = -1;
        player.Position = at with { Y = v.Position.Y };
        player.Velocity = Vector3.Zero;
        player.EyeHeight = sim.Config.Movement.StandEyeHeight;
        sim.Events.Add(new SimEvent
        {
            Type = SimEventType.VehicleLeft, Tick = sim.Tick, PlayerId = player.Id, TargetId = v.Id, Team = player.Team, Extra = seat,
            Position = player.Position, ColliderId = -1,
        });
    }

    /// <summary>The driver's input this tick: forward and back work the pedal, left and right the wheel, jump the handbrake.</summary>
    internal void Drive(PlayerState player, in InputCommand cmd)
    {
        if (Of(player) is { } v && v.Driver == player.Id)
        {
            v.Throttle = Math.Clamp(cmd.Move.Y, -1f, 1f);
            // Positive steer turns left (the sim's yaw convention); right on the stick is positive x.
            v.SteerInput = -Math.Clamp(cmd.Move.X, -1f, 1f);
            v.Handbrake = cmd.Has(InputButtons.Jump);
        }
    }

    /// <summary>Moves every vehicle a tick, then seats everyone in them where their seat now is.</summary>
    internal void Step(SimWorld sim, float dt)
    {
        for (int i = 0; i < _vehicles.Count; i++)
        {
            VehicleState v = _vehicles[i];
            v.Remember();
            v.Blocked = false;
            v.Impact = 0f;

            // Whoever's out gets out (hit, or gone).
            for (int s = 0; s < v.Occupants.Length; s++)
            {
                if (v.Occupants[s] >= 0 && sim.FindPlayer(v.Occupants[s]) is not { Alive: true, Present: true })
                {
                    if (sim.FindPlayer(v.Occupants[s]) is { } gone)
                    {
                        GetOut(sim, v, gone);
                    }
                    else
                    {
                        v.Occupants[s] = -1;
                    }
                }
            }

            Move(sim, v, dt);
        }

        for (int a = 0; a < _vehicles.Count; a++)
        {
            for (int b = a + 1; b < _vehicles.Count; b++)
            {
                Touch(_vehicles[a], _vehicles[b], moveB: true);
            }
        }

        for (int i = 0; i < _vehicles.Count; i++)
        {
            Settle(sim, i, dt);
        }
    }

    /// <summary>
    /// A joining copy driving vehicle <paramref name="i"/>: moves it a tick by this copy's own controls, as the server
    /// will (the others stay where the server has them, and don't give way), and seats everyone in it. Never tells
    /// anyone anything: getting in and out is the server's to say.
    /// </summary>
    public void Predict(SimWorld sim, int i, float dt)
    {
        if (i < 0 || i >= _vehicles.Count)
        {
            return;
        }

        VehicleState v = _vehicles[i];
        v.Remember();
        v.Blocked = false;
        v.Impact = 0f;
        Move(sim, v, dt);
        for (int o = 0; o < _vehicles.Count; o++)
        {
            if (o != i)
            {
                Touch(v, _vehicles[o], moveB: false);
            }
        }

        Settle(sim, i, dt);
    }

    /// <summary>The controls (the driver's, as the rules let them have them) and the vehicle moved by them, knocking into the level.</summary>
    private void Move(SimWorld sim, VehicleState v, float dt)
    {
        VehicleParams k = v.Params;
        bool driven = v.Driver >= 0 && sim.IsLive;
        if (v.Asleep)
        {
            if (!driven)
            {
                return;
            }

            v.Asleep = false;
            v.RestFor = 0f;
        }

        // The pedal: forward pulls away (or brakes, rolling back), back brakes (or backs up from a standstill); off it a
        // crawl is held still. Empty (or before the round's on) the brakes are on: it's parked.
        float pedal = driven ? v.Throttle : 0f;
        float speed = v.Speed;
        float drive = 0f, brake = 0f;
        bool reverse = false;
        if (!driven)
        {
            brake = 1f;
        }
        else if (pedal > 0.05f)
        {
            if (speed < -0.5f)
            {
                brake = pedal;
            }
            else
            {
                drive = pedal;
            }
        }
        else if (pedal < -0.05f)
        {
            if (speed > 0.5f)
            {
                brake = -pedal;
            }
            else
            {
                drive = -pedal;
                reverse = true;
            }
        }
        else if (MathF.Abs(speed) < k.HoldBelow)
        {
            brake = 1f;
        }

        // Someone in the way it's going (or about to go): it brakes for them, whatever the pedal says.
        Vector3 flat = v.Velocity with { Y = 0f };
        float going = flat.Length();
        Vector3 way = going > 0.1f ? flat / going : (reverse ? -v.Forward : v.Forward);
        if ((drive > 0f || going > 0.1f) && SomeoneInTheWay(sim, v, way, going))
        {
            drive = 0f;
            brake = 1f;
            v.Blocked = true;
        }

        VehicleControls controls = VehicleDynamics.Engine(v, drive, brake, reverse, driven && v.Handbrake, v.Driver >= 0, dt);
        VehicleDynamics.Step(v, sim.Collision, controls, dt);
        Collide(sim, v);
    }

    /// <summary>
    /// The body's bumps swept from where they were to where they are: knocked back out of the level (and its edge) and no
    /// longer moving into it.
    /// </summary>
    private void Collide(SimWorld sim, VehicleState v)
    {
        VehicleParams k = v.Params;
        float r = k.ProbeRadius;
        for (int p = 0; p < k.Probes.Length; p++)
        {
            Vector3 local = new(k.Probes[p].X, k.Probes[p].Y, -k.Probes[p].Z);
            Vector3 from = v.PreviousPosition + Vector3.Transform(local, v.PreviousOrientation);
            Vector3 to = v.Position + Vector3.Transform(local, v.Orientation);
            if (sim.Collision.SweepSphere(from, to, r, out SweepHit hit, includeDynamic: false))
            {
                Vector3 n = hit.Normal;
                float into = -Vector3.Dot(to - from, n) * (1f - hit.T);
                if (into > 0f)
                {
                    v.Position += n * (into + 1e-3f);
                    to += n * (into + 1e-3f);
                }

                Knocked(v, VehicleDynamics.Knock(v, to - n * r, n, k.BodyBounce, k.BodyFriction), n);
            }

            // The level's edge.
            Edge(v, ref to, to.X - r - _bounds.Min.X, Vector3.UnitX, r);
            Edge(v, ref to, _bounds.Max.X - (to.X + r), -Vector3.UnitX, r);
            Edge(v, ref to, to.Z - r - _bounds.Min.Z, Vector3.UnitZ, r);
            Edge(v, ref to, _bounds.Max.Z - (to.Z + r), -Vector3.UnitZ, r);
        }

        v.Settled();
    }

    private static void Edge(VehicleState v, ref Vector3 probe, float room, Vector3 inward, float r)
    {
        if (room >= 0f)
        {
            return;
        }

        v.Position -= inward * room;
        probe -= inward * room;
        Knocked(v, VehicleDynamics.Knock(v, probe - inward * r, inward, v.Params.BodyBounce, v.Params.BodyFriction), inward);
    }

    /// <summary>It hit something going <paramref name="speed"/> into it: the hardest knock this tick, and stopped by it if it was in its way.</summary>
    private static void Knocked(VehicleState v, float speed, Vector3 normal)
    {
        v.Impact = MathF.Max(v.Impact, speed);
        if (normal.Y < 0.7f && speed > 0f)
        {
            v.Blocked = true;
        }
    }

    /// <summary>Two vehicles' bumps overlapping: pushed apart and no longer moving into each other (<paramref name="moveB"/> false: only <paramref name="a"/> gives way).</summary>
    private static void Touch(VehicleState a, VehicleState b, bool moveB)
    {
        VehicleParams ka = a.Params, kb = b.Params;
        float reach = (ka.Length + kb.Length) * 0.5f + 0.5f;
        if (a.Asleep && b.Asleep || Vector3.DistanceSquared(a.Position, b.Position) > reach * reach)
        {
            return;
        }

        float ra = ka.ProbeRadius, rb = kb.ProbeRadius;
        float share = moveB ? kb.Mass / (ka.Mass + kb.Mass) : 1f;
        for (int i = 0; i < ka.Probes.Length; i++)
        {
            for (int j = 0; j < kb.Probes.Length; j++)
            {
                Vector3 pa = a.ToWorld(ka.Probes[i]), pb = b.ToWorld(kb.Probes[j]);
                Vector3 d = pa - pb;
                float distance = d.Length();
                if (distance >= ra + rb || distance < 1e-4f)
                {
                    continue;
                }

                Vector3 n = d / distance;
                float depth = ra + rb - distance;
                a.Position += n * (depth * share);
                if (moveB)
                {
                    b.Position -= n * (depth * (1f - share));
                }

                Vector3 at = pb + n * rb;
                float speed = MathF.Max(0f, -Vector3.Dot(a.PointVelocity(at) - b.PointVelocity(at), n));
                VehicleDynamics.Knock(a, b, at, n, moveB);
                a.Asleep = b.Asleep = false;
                a.RestFor = b.RestFor = 0f;
                Knocked(a, speed, n);
                Knocked(b, speed, -n);
            }
        }

        a.Settled();
        b.Settled();
    }

    /// <summary>
    /// After the move: never closer to anyone than brushing past (back where it was, stopped, if it would be); righted if
    /// it's over; asleep if it's empty and at rest; its paint collider moved; everyone in it seated.
    /// </summary>
    private void Settle(SimWorld sim, int i, float dt)
    {
        VehicleState v = _vehicles[i];
        VehicleParams k = v.Params;
        if (!v.Asleep)
        {
            if (Crowds(sim, v))
            {
                v.Position = v.PreviousPosition;
                v.Orientation = v.PreviousOrientation;
                v.Velocity = v.AngularVelocity = Vector3.Zero;
                v.Blocked = true;
                v.Settled();
            }

            // Over (on its side or worse) and nearly still: righted, as marshals would.
            bool over = v.Up.Y < 0.35f && v.Velocity.LengthSquared() < 1f && v.AngularVelocity.LengthSquared() < 1f;
            v.UpsideDownFor = over ? v.UpsideDownFor + dt : 0f;
            if (v.UpsideDownFor >= k.RightAfter)
            {
                RightOnGround(sim, v);
            }
            else if (!float.IsFinite(v.Position.X + v.Position.Y + v.Position.Z + v.Velocity.X + v.AngularVelocity.X + v.Orientation.W) ||
                     v.Position.Y < _bounds.Min.Y - 1f)
            {
                v.PutAt(v.Home, v.HomeYaw); // lost (it never should be): back where it started
            }

            // Empty and still: asleep till something moves it.
            float rest = k.RestBelow * k.RestBelow;
            bool still = v.Velocity.LengthSquared() < rest && v.AngularVelocity.LengthSquared() < rest && v.Contacts == (1 << VehicleState.WheelCount) - 1;
            v.RestFor = v.Driver < 0 && still ? v.RestFor + dt : 0f;
            if (v.RestFor >= SleepAfter)
            {
                v.Asleep = true;
                v.Velocity = v.AngularVelocity = Vector3.Zero;
                v.Rpm = v.Drive = 0f;
                for (int w = 0; w < VehicleState.WheelCount; w++)
                {
                    v.Slide[w] = v.SpinRate[w] = 0f;
                }

                v.Settled();
            }
        }

        Pose(i);

        // Everyone in it sits in their seat, moving with it.
        for (int s = 0; s < v.Occupants.Length; s++)
        {
            if (v.Occupants[s] >= 0 && sim.FindPlayer(v.Occupants[s]) is { } occupant)
            {
                Seat(v, s, occupant);
            }
        }
    }

    /// <summary>Back on its wheels where it lies, facing the way it was, a hand's breadth off the ground.</summary>
    private static void RightOnGround(SimWorld sim, VehicleState v)
    {
        Vector3 at = v.Position;
        float y = at.Y;
        if (sim.Collision.SweepSphere(at + new Vector3(0f, 3f, 0f), at - new Vector3(0f, 3f, 0f), 0f, out SweepHit hit, includeDynamic: false))
        {
            y = hit.Point.Y;
        }

        v.PutAt(new Vector3(at.X, y + 0.1f, at.Z), v.Yaw); // whoever's in it stays in their seat
    }

    /// <summary>Puts <paramref name="player"/> on <paramref name="seat"/> of <paramref name="v"/>, moving with it, eyes at sitting height.</summary>
    private static void Seat(VehicleState v, int seat, PlayerState player)
    {
        player.Position = v.Seat(seat);
        player.Velocity = v.PointVelocity(player.Position);
        player.EyeHeight = v.Params.SeatedEyeHeight;
        player.Sprinting = false;
        player.Stance = Stance.Standing;
    }

    /// <summary>
    /// Whether the body (where it's got to) has come closer to anyone not in it than brushing past them, and closer than it
    /// was: it mustn't.
    /// </summary>
    private static bool Crowds(SimWorld sim, VehicleState v)
    {
        VehicleParams k = v.Params;
        IReadOnlyList<PlayerState> players = sim.Players;
        float near = sim.Config.Movement.CapsuleRadius + k.Brush;
        Vector3 forwardNow = v.Forward, forwardBefore = ViewAngles.FlatForward(v.PreviousYaw);
        for (int i = 0; i < players.Count; i++)
        {
            PlayerState p = players[i];
            if (!p.Present || p.Vehicle == v.Id || MathF.Abs(p.Position.Y - v.Position.Y) > 2f)
            {
                continue;
            }

            float then = Clearance(p.Position, v.Position, forwardNow, k);
            if (then < near && then < Clearance(p.Position, v.PreviousPosition, forwardBefore, k) - 1e-4f)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// How far <paramref name="point"/> is outside the body's footprint (a rectangle about <paramref name="center"/>,
    /// long along <paramref name="forward"/>); negative inside it.
    /// </summary>
    private static float Clearance(Vector3 point, Vector3 center, Vector3 forward, VehicleParams k)
    {
        Vector3 rel = point - center;
        float x = MathF.Abs(rel.X * -forward.Z + rel.Z * forward.X) - k.Width * 0.5f;
        float z = MathF.Abs(rel.X * forward.X + rel.Z * forward.Z) - k.Length * 0.5f;
        float ox = MathF.Max(x, 0f), oz = MathF.Max(z, 0f);
        return MathF.Sqrt(ox * ox + oz * oz) + MathF.Min(MathF.Max(x, z), 0f);
    }

    /// <summary>
    /// Whether anyone (not in it) stands in the vehicle's way along <paramref name="way"/> (the way it's sliding or rolling,
    /// or about to drive) within the distance it needs to stop <see cref="VehicleParams.StopShort"/> short of them, braking
    /// as hard as the ground under it allows: it brakes for them.
    /// </summary>
    private static bool SomeoneInTheWay(SimWorld sim, VehicleState v, Vector3 way, float speed)
    {
        VehicleParams k = v.Params;
        float radius = sim.Config.Movement.CapsuleRadius;
        float reach = speed * speed / (2f * v.StopDecel) + k.StopShort + radius + speed * (2f / 60f);
        Vector3 forward = v.Forward, right = v.Right;
        var across = new Vector3(-way.Z, 0f, way.X);
        // The body's reach along the way it's going, and across it.
        float halfAlong = MathF.Abs(Vector3.Dot(way, forward)) * k.Length * 0.5f + MathF.Abs(Vector3.Dot(way, right)) * k.Width * 0.5f;
        float halfAcross = MathF.Abs(Vector3.Dot(across, forward)) * k.Length * 0.5f + MathF.Abs(Vector3.Dot(across, right)) * k.Width * 0.5f;
        IReadOnlyList<PlayerState> players = sim.Players;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerState p = players[i];
            if (!p.Present || p.Vehicle == v.Id || MathF.Abs(p.Position.Y - v.Position.Y) > 2f)
            {
                continue;
            }

            Vector3 rel = (p.Position - v.Position) with { Y = 0f };
            float along = Vector3.Dot(rel, way) - halfAlong;
            float aside = MathF.Abs(Vector3.Dot(rel, across)) - halfAcross;
            if (along > -0.2f && along < reach && aside < radius + k.Brush)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Where someone gets in and out of <paramref name="seat"/>: beside it, outside the body.</summary>
    public static Vector3 Door(VehicleState v, int seat)
    {
        Vector3 at = v.Params.Seats[seat].At;
        float side = at.X >= 0f ? 1f : -1f;
        return v.Position + v.Right * (side * (v.Params.Width * 0.5f + 0.55f)) + v.Forward * at.Z;
    }

    /// <summary>Puts the vehicle's paint collider where its body is.</summary>
    private void Pose(int i)
    {
        VehicleState v = _vehicles[i];
        VehicleShape s = _shapes[i];
        s.AxisX = v.BodyRight;
        s.AxisY = v.Up;
        s.AxisZ = v.BodyForward;
        // From a little off the ground (balls skim under the floor between the wheels) to the sides' top.
        float bottom = 0.15f, top = v.Params.SideHeight;
        s.Half = new Vector3(v.Params.Width * 0.5f, (top - bottom) * 0.5f, v.Params.Length * 0.5f);
        s.Center = v.ToWorld(new Vector3(0f, (top + bottom) * 0.5f, 0f));
    }

    /// <summary>
    /// A joining copy: vehicle <paramref name="i"/> as the server has it, and everyone in it but this copy's own player
    /// (<paramref name="localId"/>, seated by <see cref="SeatFromServer"/>) sitting in their seats. A vehicle this copy is
    /// driving (<paramref name="predicted"/>) keeps its own motion: only who's in it is the server's.
    /// </summary>
    public void ApplyServer(int i, Vector3 position, Quaternion orientation, float speed, float steer, float rpm, int gear, float drive,
        float braking, float slideFront, float slideRear, ReadOnlySpan<int> occupants, IReadOnlyList<PlayerState> players, int localId,
        bool predicted = false)
    {
        if (i < 0 || i >= _vehicles.Count)
        {
            return;
        }

        VehicleState v = _vehicles[i];
        if (predicted)
        {
            for (int s = 0; s < v.Occupants.Length && s < occupants.Length; s++)
            {
                v.Occupants[s] = occupants[s];
            }
        }
        else
        {
            v.ApplyServer(position, orientation, speed, steer, rpm, gear, drive, braking, slideFront, slideRear, occupants);
            Pose(i);
        }

        for (int k = 0; k < players.Count; k++)
        {
            PlayerState p = players[k];
            if (p.Id == localId)
            {
                if (p.Vehicle == v.Id && p.Seat >= 0)
                {
                    Seat(v, p.Seat, p); // where the drawn vehicle has their seat
                }

                continue;
            }

            int seat = v.SeatOf(p.Id);
            if (seat >= 0)
            {
                p.Vehicle = v.Id;
                p.Seat = seat;
                p.EyeHeight = v.Params.SeatedEyeHeight;
                if (predicted)
                {
                    Seat(v, seat, p); // in the vehicle this copy drives: where this copy has it
                }
            }
            else if (p.Vehicle == v.Id)
            {
                p.Vehicle = -1;
                p.Seat = -1;
            }
        }
    }

    /// <summary>A joining copy: its own player in <paramref name="vehicle"/>'s <paramref name="seat"/> as the server last had them (−1: in none).</summary>
    public void SeatFromServer(PlayerState player, int vehicle, int seat)
    {
        if (vehicle >= 0 && vehicle < _vehicles.Count && seat >= 0 && seat < _vehicles[vehicle].Occupants.Length)
        {
            player.Vehicle = vehicle;
            player.Seat = seat;
            Seat(_vehicles[vehicle], seat, player);
        }
        else if (player.Vehicle >= 0)
        {
            player.Vehicle = -1;
            player.Seat = -1;
        }
    }

    /// <summary>A joining copy driving vehicle <paramref name="i"/>: its motion put back as the server had it.</summary>
    public void Restore(int i, in VehicleMotion motion)
    {
        if (i >= 0 && i < _vehicles.Count)
        {
            _vehicles[i].Restore(motion);
            Pose(i);
        }
    }

    private static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);
}
