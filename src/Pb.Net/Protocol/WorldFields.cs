using System.Numerics;
using Pb.Net.Packing;
using Pb.Sim;
using Pb.Sim.Match;
using Pb.Sim.Vehicles;

namespace Pb.Net.Protocol;

/// <summary>
/// What every player's snapshot shares, as one list of fields: each player (<see cref="PuppetFields"/>), each door leaf's
/// openness, each vehicle (where it is, which way it faces, its speed and wheels, who's in each seat), then the round
/// (its phase and clock, the result, the objective, the pickups taken). Laid out once per round, the same on every copy,
/// from the round's players and level.
/// </summary>
public sealed class WorldFields
{
    public const int DoorBits = 10;
    public const int MaxPickups = 64;

    /// <summary>A vehicle's fields before its seats: x, y, z, yaw, speed, steer.</summary>
    private const int VehicleHead = 6;

    private const int YawBits = 12, SpeedBits = 10, SteerBits = 8, SeatBits = 5;

    /// <summary>The fastest a vehicle is sent as going, either way (m/s).</summary>
    private const float MaxVehicleSpeed = 16f;

    /// <summary>The most a vehicle's wheels are sent as turned, either way (rad).</summary>
    private const float MaxSteer = 1f;

    private const int MatchCount = 29;

    /// <summary>Where capture the flag's flags start among the round's fields (after the pickups), five fields each.</summary>
    private const int FlagsAt = 19;

    private readonly byte[] _widths;

    private readonly int[] _vehicleAt;
    private readonly int _vehicleFields;

    public WorldFields(int players, int doors, in PositionQuant grid, IReadOnlyList<VehicleState>? vehicles = null)
    {
        Players = players;
        Doors = doors;
        Grid = grid;
        Vehicles = vehicles?.Count ?? 0;
        _vehicleAt = new int[Vehicles + 1];
        for (int v = 0; v < Vehicles; v++)
        {
            _vehicleAt[v + 1] = _vehicleAt[v] + VehicleHead + vehicles![v].Occupants.Length;
        }

        _vehicleFields = _vehicleAt[Vehicles];
        Count = players * PuppetFields.Count + doors + _vehicleFields + MatchCount;
        _widths = new byte[Count];
        Span<byte> puppet = stackalloc byte[PuppetFields.Count];
        PuppetFields.Widths(grid, puppet);
        for (int i = 0; i < players; i++)
        {
            puppet.CopyTo(_widths.AsSpan(i * PuppetFields.Count));
        }

        for (int d = 0; d < doors; d++)
        {
            _widths[DoorsAt + d] = DoorBits;
        }

        for (int v = 0; v < Vehicles; v++)
        {
            Span<byte> w = _widths.AsSpan(VehiclesAt + _vehicleAt[v], _vehicleAt[v + 1] - _vehicleAt[v]);
            w[0] = (byte)grid.BitsX;
            w[1] = (byte)grid.BitsY;
            w[2] = (byte)grid.BitsZ;
            w[3] = YawBits;
            w[4] = SpeedBits;
            w[5] = SteerBits;
            w[VehicleHead..].Fill(SeatBits); // who's in each seat + 1
        }

        Span<byte> m = _widths.AsSpan(MatchAt);
        m[0] = 2; // phase
        m[1] = 32; // live from tick
        m[2] = 32; // end tick
        m[3] = 32; // elapsed (float)
        m[4] = 4; // why it ended
        m[5] = 5; // winner + 1
        m[6] = 1; // objective done
        m[7] = (byte)grid.BitsX;
        m[8] = (byte)grid.BitsY;
        m[9] = (byte)grid.BitsZ;
        m[10] = 5; // carrier + 1
        m[11] = 1; // case moved
        m[12] = 6; // way out + 1
        m[13] = 32; // held (float)
        m[14] = 2; // hold status
        m[15] = 32; // pickups taken 0–31
        m[16] = 32; // pickups taken 32–63
        m[17] = 5; // capture the flag: the side that captured + 1
        m[18] = 1; // VIP escort: the VIP is out
        for (int f = 0; f < 2; f++)
        {
            m[FlagsAt + f * 5] = (byte)grid.BitsX;
            m[FlagsAt + f * 5 + 1] = (byte)grid.BitsY;
            m[FlagsAt + f * 5 + 2] = (byte)grid.BitsZ;
            m[FlagsAt + f * 5 + 3] = 5; // carrier + 1
            m[FlagsAt + f * 5 + 4] = 1; // at home
        }
    }

    public int Players { get; }

    public int Doors { get; }

    public int Vehicles { get; }

    public PositionQuant Grid { get; }

    public int Count { get; }

    public ReadOnlySpan<byte> Widths => _widths;

    private int DoorsAt => Players * PuppetFields.Count;

    private int VehiclesAt => DoorsAt + Doors;

    private int MatchAt => VehiclesAt + _vehicleFields;

    public Span<uint> Puppet(Span<uint> fields, int player) => fields.Slice(player * PuppetFields.Count, PuppetFields.Count);

    public ReadOnlySpan<uint> Puppet(ReadOnlySpan<uint> fields, int player) => fields.Slice(player * PuppetFields.Count, PuppetFields.Count);

    /// <summary>The world as the server has it now.</summary>
    public void Capture(SimWorld sim, Span<uint> into)
    {
        for (int i = 0; i < Players; i++)
        {
            PuppetFields.Capture(sim.Players[i], Grid, Puppet(into, i));
        }

        for (int d = 0; d < Doors; d++)
        {
            into[DoorsAt + d] = Quant.Range(sim.Doors.Open(d), 0f, 1f, DoorBits);
        }

        for (int v = 0; v < Vehicles && v < sim.Vehicles.Count; v++)
        {
            VehicleState car = sim.Vehicles[v];
            Span<uint> f = Vehicle(into, v);
            Grid.Quantize(car.Position, out f[0], out f[1], out f[2]);
            f[3] = Quant.Angle(car.Yaw, YawBits);
            f[4] = Quant.Range(car.Speed, -MaxVehicleSpeed, MaxVehicleSpeed, SpeedBits);
            f[5] = Quant.Range(car.Steer, -MaxSteer, MaxSteer, SteerBits);
            for (int s = 0; s < car.Occupants.Length && VehicleHead + s < f.Length; s++)
            {
                f[VehicleHead + s] = (uint)(car.Occupants[s] + 1);
            }
        }

        Span<uint> m = into.Slice(MatchAt, MatchCount);
        m.Clear();
        if (sim.Match is { } match)
        {
            m[0] = (uint)match.Phase;
            m[1] = (uint)match.LiveFromTick;
            m[2] = (uint)match.EndTick;
            m[3] = BitConverter.SingleToUInt32Bits(match.Elapsed);
            m[4] = (uint)match.Result.Reason;
            m[5] = (uint)(match.Result.Winner + 1);
            if (match.Objective is { } o)
            {
                m[6] = o.Done ? 1u : 0;
                Grid.Quantize(o.CasePosition, out m[7], out m[8], out m[9]);
                m[10] = (uint)(o.Carrier + 1);
                m[11] = o.CaseMoved ? 1u : 0;
                m[12] = (uint)(o.ExitUsed + 1);
                m[13] = BitConverter.SingleToUInt32Bits(o.Held);
                m[14] = (uint)o.Status;
                m[17] = (uint)(o.CapturedBy + 1);
                m[18] = o.VipOut ? 1u : 0;
                for (int f = 0; f < o.Flags.Count && f < 2; f++)
                {
                    FlagState flag = o.Flags[f];
                    Grid.Quantize(flag.Position, out m[FlagsAt + f * 5], out m[FlagsAt + f * 5 + 1], out m[FlagsAt + f * 5 + 2]);
                    m[FlagsAt + f * 5 + 3] = (uint)(flag.Carrier + 1);
                    m[FlagsAt + f * 5 + 4] = flag.AtHome ? 1u : 0;
                }
            }
        }

        PickupSet pickups = sim.Pickups;
        for (int p = 0; p < pickups.Items.Count && p < MaxPickups; p++)
        {
            if (pickups.IsTaken(p))
            {
                m[15 + p / 32] |= 1u << (p % 32);
            }
        }
    }

    /// <summary>A joining copy: the doors as a snapshot has them (drawn, like everyone else, a little behind the server).</summary>
    public void ApplyDoors(SimWorld sim, ReadOnlySpan<uint> fields)
    {
        for (int d = 0; d < Doors && d < sim.Doors.Count; d++)
        {
            float open = Quant.FromRange(fields[DoorsAt + d], 0f, 1f, DoorBits);
            if (MathF.Abs(open - sim.Doors.Open(d)) > 1e-4f)
            {
                sim.Doors.SetOpenFromServer(d, open);
            }
        }
    }

    /// <summary>
    /// A joining copy: the vehicles <paramref name="t"/> of the way from snapshot <paramref name="a"/> to <paramref name="b"/>
    /// (drawn, like everyone else, a little behind the server), and who's in them as the nearer has it. This copy's own
    /// player's seat is <see cref="SeatOf"/>'s, from the newest.
    /// </summary>
    public void ApplyVehicles(SimWorld sim, ReadOnlySpan<uint> a, ReadOnlySpan<uint> b, float t)
    {
        Span<int> occupants = stackalloc int[8];
        for (int v = 0; v < Vehicles && v < sim.Vehicles.Count; v++)
        {
            ReadOnlySpan<uint> fa = Vehicle(a, v), fb = Vehicle(b, v), near = t < 0.5f ? fa : fb;
            Vector3 position = Vector3.Lerp(Grid.Dequantize(fa[0], fa[1], fa[2]), Grid.Dequantize(fb[0], fb[1], fb[2]), t);
            float ya = Quant.FromAngle(fa[3], YawBits), yb = Quant.FromAngle(fb[3], YawBits);
            float turn = MathF.IEEERemainder(yb - ya, MathF.Tau);
            float speed = Lerp(Quant.FromRange(fa[4], -MaxVehicleSpeed, MaxVehicleSpeed, SpeedBits),
                Quant.FromRange(fb[4], -MaxVehicleSpeed, MaxVehicleSpeed, SpeedBits), t);
            float steer = Lerp(Quant.FromRange(fa[5], -MaxSteer, MaxSteer, SteerBits), Quant.FromRange(fb[5], -MaxSteer, MaxSteer, SteerBits), t);
            int seats = Math.Min(occupants.Length, near.Length - VehicleHead);
            for (int s = 0; s < seats; s++)
            {
                occupants[s] = (int)near[VehicleHead + s] - 1;
            }

            sim.Vehicles.ApplyServer(v, position, ya + turn * t, speed, steer, occupants[..seats], sim.Players, sim.LocalPlayerId);
        }
    }

    /// <summary>The vehicle and seat <paramref name="playerId"/> is in as <paramref name="fields"/> have it, or (−1, −1).</summary>
    public (int Vehicle, int Seat) SeatOf(ReadOnlySpan<uint> fields, int playerId)
    {
        for (int v = 0; v < Vehicles; v++)
        {
            ReadOnlySpan<uint> f = Vehicle(fields, v);
            for (int s = VehicleHead; s < f.Length; s++)
            {
                if ((int)f[s] - 1 == playerId)
                {
                    return (v, s - VehicleHead);
                }
            }
        }

        return (-1, -1);
    }

    private Span<uint> Vehicle(Span<uint> fields, int v) => fields.Slice(VehiclesAt + _vehicleAt[v], _vehicleAt[v + 1] - _vehicleAt[v]);

    private ReadOnlySpan<uint> Vehicle(ReadOnlySpan<uint> fields, int v) =>
        fields.Slice(VehiclesAt + _vehicleAt[v], _vehicleAt[v + 1] - _vehicleAt[v]);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>A joining copy: the round and the pickups as a snapshot has them (the newest, for your own player's sake).</summary>
    public void ApplyRound(SimWorld sim, ReadOnlySpan<uint> fields)
    {
        ReadOnlySpan<uint> m = fields.Slice(MatchAt, MatchCount);
        if (sim.Match is { } match)
        {
            match.ApplyServer((MatchPhase)m[0], (int)m[1], (int)m[2], BitConverter.UInt32BitsToSingle(m[3]),
                new MatchResult((RoundEnd)m[4], (int)m[5] - 1));
            match.Objective?.ApplyServer(m[6] != 0, Grid.Dequantize(m[7], m[8], m[9]), (int)m[10] - 1, m[11] != 0, (int)m[12] - 1,
                BitConverter.UInt32BitsToSingle(m[13]), (HoldStatus)m[14]);
            Span<(System.Numerics.Vector3, int, bool)> flags = stackalloc (System.Numerics.Vector3, int, bool)[2];
            for (int f = 0; f < 2; f++)
            {
                flags[f] = (Grid.Dequantize(m[FlagsAt + f * 5], m[FlagsAt + f * 5 + 1], m[FlagsAt + f * 5 + 2]), (int)m[FlagsAt + f * 5 + 3] - 1,
                    m[FlagsAt + f * 5 + 4] != 0);
            }

            match.Objective?.ApplyServerExtra((int)m[17] - 1, m[18] != 0, flags);
        }

        PickupSet pickups = sim.Pickups;
        for (int p = 0; p < pickups.Items.Count && p < MaxPickups; p++)
        {
            pickups.SetTakenFromServer(p, (m[15 + p / 32] & (1u << (p % 32))) != 0);
        }
    }
}
