using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Players;

namespace Pb.Sim.Vehicles;

/// <summary>
/// A vehicle's body as a paint collider: an oriented box from the ground up to its sides' top, following the vehicle,
/// filed in the collision grid under the whole level's bounds (it can be anywhere), so the broadphase never needs
/// rebuilding as it moves. Balls break on it and never count.
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

/// <summary>One vehicle as it stands: where, which way, how fast, its wheels' turn, and who's in each seat.</summary>
public sealed class VehicleState
{
    internal VehicleState(int id, VehicleParams p, Vector3 position, float yaw)
    {
        Id = id;
        Params = p;
        Home = position;
        HomeYaw = yaw;
        Position = PreviousPosition = position;
        Yaw = PreviousYaw = yaw;
        Occupants = new int[p.Seats.Count];
        Array.Fill(Occupants, -1);
    }

    public int Id { get; }

    public VehicleParams Params { get; }

    /// <summary>Where it stood when the round started.</summary>
    public Vector3 Home { get; }

    public float HomeYaw { get; }

    /// <summary>Its point on the ground, under the middle of the body.</summary>
    public Vector3 Position { get; internal set; }

    /// <summary>Which way it faces (the sim's yaw: 0 faces −Z, positive turns left).</summary>
    public float Yaw { get; internal set; }

    /// <summary>Last tick's, for drawing it smoothly between ticks.</summary>
    public Vector3 PreviousPosition { get; internal set; }

    public float PreviousYaw { get; internal set; }

    /// <summary>Along its heading (m/s; negative backing up).</summary>
    public float Speed { get; internal set; }

    /// <summary>The front wheels' turn (rad, positive to the left).</summary>
    public float Steer { get; internal set; }

    /// <summary>The driver's pedal this tick (−1 brake or reverse … 1 full throttle).</summary>
    public float Throttle { get; internal set; }

    /// <summary>The driver's wheel this tick (−1 full right … 1 full left).</summary>
    public float SteerInput { get; internal set; }

    /// <summary>Who's in each seat (player id), or −1.</summary>
    public int[] Occupants { get; }

    /// <summary>It was stopped by something in its way this tick (a wall, a tree, someone).</summary>
    public bool Blocked { get; internal set; }

    public Vector3 Forward => ViewAngles.FlatForward(Yaw);

    public Vector3 Right
    {
        get
        {
            Vector3 f = Forward;
            return new Vector3(-f.Z, 0f, f.X);
        }
    }

    public int Driver => Occupants[Params.DriverSeat];

    /// <summary>Where seat <paramref name="seat"/> is (world): the point the one in it sits on.</summary>
    public Vector3 Seat(int seat)
    {
        Vector3 at = Params.Seats[seat].At;
        return Position + Right * at.X + new Vector3(0f, at.Y, 0f) + Forward * at.Z;
    }

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

    /// <summary>A joining copy: the vehicle as the server has it.</summary>
    internal void ApplyServer(Vector3 position, float yaw, float speed, float steer, ReadOnlySpan<int> occupants)
    {
        PreviousPosition = Position;
        PreviousYaw = Yaw;
        Position = position;
        Yaw = yaw;
        Speed = speed;
        Steer = steer;
        for (int i = 0; i < Occupants.Length && i < occupants.Length; i++)
        {
            Occupants[i] = occupants[i];
        }
    }
}

/// <summary>
/// The level's vehicles (Phase 5): where they are, who's in them, and how they drive, decided by the sim so the server
/// rules and bots drive by the same rules as people (vehicles.jsonc lists the field's rules). Each tick: interact gets
/// a player in (the nearest free seat in reach, standing still enough) or out (beside their seat, the vehicle nearly
/// stopped); the driver's move input works the pedal (forward and back) and the wheel (left and right); the vehicle moves
/// by a bicycle model and stops short of anything in its way — the level's paint colliders, another vehicle, anyone
/// standing in its path (a vehicle never hits anyone); and whoever's in it sits in their seat, moving with it. Someone
/// hit gets out. Steps without allocating.
/// </summary>
public sealed class VehicleSet
{
    /// <summary>Spheres along the body's length that test what it would drive into.</summary>
    private const int Probes = 3;

    private readonly List<VehicleState> _vehicles = new();
    private readonly List<VehicleShape> _shapes = new();
    private readonly List<VehicleSpot> _spots = new();
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
        _spots.Clear();
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
            _spots.Add(spot);
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
            v.Position = v.PreviousPosition = v.Home;
            v.Yaw = v.PreviousYaw = v.HomeYaw;
            v.Speed = v.Steer = v.Throttle = v.SteerInput = 0f;
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

    /// <summary>
    /// The free seat nearest <paramref name="player"/> within reach (of its side of the vehicle), the driver's first among
    /// equals, or (−1, −1).
    /// </summary>
    public (int Vehicle, int Seat) FreeSeatNear(Vector3 position)
    {
        int best = -1, bestSeat = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < _vehicles.Count; i++)
        {
            VehicleState v = _vehicles[i];
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

    /// <summary>The driver's input this tick: forward and back work the pedal, left and right the wheel.</summary>
    internal void Drive(PlayerState player, in InputCommand cmd)
    {
        if (Of(player) is { } v && v.Driver == player.Id)
        {
            v.Throttle = Math.Clamp(cmd.Move.Y, -1f, 1f);
            // Positive steer turns left (the sim's yaw convention); right on the stick is positive x.
            v.SteerInput = -Math.Clamp(cmd.Move.X, -1f, 1f);
        }
    }

    /// <summary>Moves every vehicle a tick, then seats everyone in them where their seat now is.</summary>
    internal void Step(SimWorld sim, float dt)
    {
        IReadOnlyList<PlayerState> players = sim.Players;
        for (int i = 0; i < _vehicles.Count; i++)
        {
            VehicleState v = _vehicles[i];
            v.PreviousPosition = v.Position;
            v.PreviousYaw = v.Yaw;

            // Whoever's out gets out (hit, or gone), and a vehicle without a driver rolls to a stop.
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

            VehicleParams k = v.Params;
            float throttle = v.Driver >= 0 && sim.IsLive ? v.Throttle : 0f;
            float steerTarget = v.Driver >= 0 ? v.SteerInput * k.MaxSteer : 0f;
            v.Steer = MoveToward(v.Steer, steerTarget, k.SteerRate * dt);
            float speed = v.Speed;
            if (MathF.Abs(throttle) > 0.05f && SomeoneInTheWay(sim, v, MathF.Abs(speed) > 0.1f ? speed : throttle))
            {
                // Someone in the way it's going: it brakes for them, whatever the pedal says.
                throttle = 0f;
                speed = MoveToward(speed, 0f, k.Brake * dt);
            }
            else if (throttle > 0.05f)
            {
                speed = speed < 0f ? MoveToward(speed, 0f, k.Brake * dt) : MathF.Min(k.MaxSpeed, speed + k.Accel * throttle * dt);
            }
            else if (throttle < -0.05f)
            {
                speed = speed > 0f ? MoveToward(speed, 0f, k.Brake * dt) : MathF.Max(-k.ReverseSpeed, speed + k.Accel * throttle * dt);
            }
            else
            {
                speed = MoveToward(speed, 0f, k.Coast * dt);
            }

            // A bicycle: the back axle rolls along its heading (half the wheelbase behind the body's middle), the front
            // wheels turn it.
            float yaw = v.Yaw + speed * MathF.Tan(v.Steer) / k.Wheelbase * dt;
            Vector3 back = v.Position - v.Forward * (k.Wheelbase * 0.5f) + ViewAngles.FlatForward((v.Yaw + yaw) * 0.5f) * (speed * dt);
            Vector3 next = back + ViewAngles.FlatForward(yaw) * (k.Wheelbase * 0.5f);
            v.Blocked = false;
            if (MathF.Abs(speed) > 1e-4f && Obstructed(sim, v, next, yaw))
            {
                // Something in the way: it stops where it is (no further, and no turning on the spot into it).
                v.Blocked = true;
                speed = 0f;
                next = v.Position;
                yaw = v.Yaw;
            }

            v.Speed = speed;
            v.Position = next;
            v.Yaw = WrapAngle(yaw);
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
    }

    /// <summary>Puts <paramref name="player"/> on <paramref name="seat"/> of <paramref name="v"/>, moving with it, eyes at sitting height.</summary>
    private static void Seat(VehicleState v, int seat, PlayerState player)
    {
        player.Position = v.Seat(seat);
        player.Velocity = v.Forward * v.Speed;
        player.EyeHeight = v.Params.SeatedEyeHeight;
        player.Sprinting = false;
        player.Stance = Stance.Standing;
    }

    /// <summary>
    /// Whether driving to <paramref name="next"/> facing <paramref name="yaw"/> runs into something: the level (a few
    /// spheres along the body, swept from where they are), the level's edge, another vehicle, or anyone not in this one
    /// standing where the body would be (with a little room to spare).
    /// </summary>
    private bool Obstructed(SimWorld sim, VehicleState v, Vector3 next, float yaw)
    {
        VehicleParams k = v.Params;
        float r = k.Width * 0.5f;
        // The probes reach the body's ends (the corners are rounded off) and skim the ground just under its floor.
        float probe = r * 0.9f;
        float reach = MathF.Max(0f, k.Length * 0.5f - probe);
        Vector3 forwardNow = v.Forward, forwardNext = ViewAngles.FlatForward(yaw);
        var up = new Vector3(0f, MathF.Max(k.Floor, probe + 0.05f), 0f);
        for (int p = 0; p < Probes; p++)
        {
            float along = -reach + 2f * reach * p / (Probes - 1);
            Vector3 from = v.Position + forwardNow * along + up;
            Vector3 to = next + forwardNext * along + up;
            if (sim.Collision.SweepSphere(from, to, probe, out SweepHit hit, includeDynamic: false) && hit.T < 1f)
            {
                return true;
            }
        }

        if (next.X < _bounds.Min.X + r || next.X > _bounds.Max.X - r || next.Z < _bounds.Min.Z + r || next.Z > _bounds.Max.Z - r)
        {
            return true;
        }

        // Another vehicle: their bodies, as discs along each, mustn't overlap.
        for (int i = 0; i < _vehicles.Count; i++)
        {
            VehicleState o = _vehicles[i];
            if (o == v)
            {
                continue;
            }

            float ro = o.Params.Width * 0.5f, reachO = MathF.Max(0f, o.Params.Length * 0.5f - ro);
            for (int a = 0; a < Probes; a++)
            {
                Vector3 pa = next + forwardNext * (-reach + 2f * reach * a / (Probes - 1));
                for (int b = 0; b < Probes; b++)
                {
                    Vector3 pb = o.Position + o.Forward * (-reachO + 2f * reachO * b / (Probes - 1));
                    if (Flat(pa - pb) < r + ro && Flat(pa - pb) < Flat(v.Position + forwardNow * (-reach + 2f * reach * a / (Probes - 1)) - pb))
                    {
                        return true;
                    }
                }
            }
        }

        // Anyone it would come closer to than brushing past (not those in it).
        IReadOnlyList<PlayerState> players = sim.Players;
        float near = sim.Config.Movement.CapsuleRadius + k.Brush;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerState p = players[i];
            if (!p.Present || p.Vehicle == v.Id || MathF.Abs(p.Position.Y - next.Y) > 2f)
            {
                continue;
            }

            float then = Clearance(p.Position, next, forwardNext, k);
            if (then < near && then < Clearance(p.Position, v.Position, forwardNow, k))
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
    /// Whether anyone (not in it) stands in the vehicle's way ahead (or behind it, backing up: the way it's going) within
    /// the distance it needs to stop <see cref="VehicleParams.StopShort"/> short of them: it brakes for them.
    /// </summary>
    private static bool SomeoneInTheWay(SimWorld sim, VehicleState v, float speed)
    {
        VehicleParams k = v.Params;
        float radius = sim.Config.Movement.CapsuleRadius;
        float reach = speed * speed / (2f * k.Brake) + k.StopShort + radius + MathF.Abs(speed) * (1f / 60f);
        float way = speed >= 0f ? 1f : -1f;
        Vector3 forward = v.Forward, right = v.Right;
        IReadOnlyList<PlayerState> players = sim.Players;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerState p = players[i];
            if (!p.Present || p.Vehicle == v.Id || MathF.Abs(p.Position.Y - v.Position.Y) > 2f)
            {
                continue;
            }

            Vector3 rel = p.Position - v.Position;
            float along = Vector3.Dot(rel, forward) * way - k.Length * 0.5f;
            float aside = MathF.Abs(Vector3.Dot(rel, right)) - k.Width * 0.5f;
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
        Vector3 f = v.Forward;
        s.AxisZ = f;
        s.AxisX = new Vector3(-f.Z, 0f, f.X);
        s.AxisY = Vector3.UnitY;
        // From a little off the ground (balls skim under the floor between the wheels) to the sides' top.
        float bottom = 0.15f, top = v.Params.SideHeight;
        s.Half = new Vector3(v.Params.Width * 0.5f, (top - bottom) * 0.5f, v.Params.Length * 0.5f);
        s.Center = v.Position + new Vector3(0f, (top + bottom) * 0.5f, 0f);
    }

    /// <summary>
    /// A joining copy: vehicle <paramref name="i"/> as the server has it, and everyone in it but this copy's own player
    /// (<paramref name="localId"/>, seated by <see cref="SeatFromServer"/>) sitting in their seats.
    /// </summary>
    public void ApplyServer(int i, Vector3 position, float yaw, float speed, float steer, ReadOnlySpan<int> occupants,
        IReadOnlyList<PlayerState> players, int localId)
    {
        if (i < 0 || i >= _vehicles.Count)
        {
            return;
        }

        VehicleState v = _vehicles[i];
        v.ApplyServer(position, yaw, speed, steer, occupants);
        Pose(i);
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

    private static float MoveToward(float value, float target, float step) =>
        value < target ? MathF.Min(target, value + step) : MathF.Max(target, value - step);

    private static float WrapAngle(float a)
    {
        a %= MathF.Tau;
        return a > MathF.PI ? a - MathF.Tau : a <= -MathF.PI ? a + MathF.Tau : a;
    }

    private static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);
}
