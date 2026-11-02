using Pb.Net.Packing;
using Pb.Sim;
using Pb.Sim.Match;

namespace Pb.Net.Protocol;

/// <summary>
/// What every player's snapshot shares, as one list of fields: each player (<see cref="PuppetFields"/>), each door leaf's
/// openness, then the round (its phase and clock, the result, the objective, the pickups taken). Laid out once per round,
/// the same on every copy, from the round's players and level.
/// </summary>
public sealed class WorldFields
{
    public const int DoorBits = 10;
    public const int MaxPickups = 64;

    private const int MatchCount = 29;

    /// <summary>Where capture the flag's flags start among the round's fields (after the pickups), five fields each.</summary>
    private const int FlagsAt = 19;

    private readonly byte[] _widths;

    public WorldFields(int players, int doors, in PositionQuant grid)
    {
        Players = players;
        Doors = doors;
        Grid = grid;
        Count = players * PuppetFields.Count + doors + MatchCount;
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

    public PositionQuant Grid { get; }

    public int Count { get; }

    public ReadOnlySpan<byte> Widths => _widths;

    private int DoorsAt => Players * PuppetFields.Count;

    private int MatchAt => DoorsAt + Doors;

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
