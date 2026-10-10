using Pb.Net.Packing;
using Pb.Sim;
using Pb.Sim.Match;

namespace Pb.Net.Protocol;

/// <summary>
/// What every player's snapshot shares, as one list of fields: each player (<see cref="PuppetFields"/>), each door leaf's
/// openness, then the round (its phase and clock, the result, the objective, the pickups taken, and in speedball the
/// countdown and the buzzers). Laid out once per round, the same on every copy, from the round's players and level.
/// </summary>
public sealed class WorldFields
{
    public const int DoorBits = 10;
    public const int MaxPickups = 64;

    private const int MatchCount = 24;

    /// <summary>A hang's progress, 0 to 1, in this many bits.</summary>
    private const int HangBits = 8;

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
        m[17] = 32; // the countdown to the horn left (float)
        m[18] = 5; // side 0's buzzer: its hanger + 1
        m[19] = 5; // side 1's
        m[20] = HangBits; // side 0's hang so far
        m[21] = HangBits; // side 1's
        m[22] = 2; // the side whose buzzer was hung + 1
        m[23] = 5; // who hung it + 1
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
            }

            m[17] = BitConverter.SingleToUInt32Bits(match.CountdownLeft);
            if (match.Buzzers is { } buzzers)
            {
                for (int side = 0; side < 2 && side < buzzers.Count; side++)
                {
                    m[18 + side] = (uint)(buzzers.Hanger(side) + 1);
                    m[20 + side] = Quant.Range(buzzers.Progress(side), 0f, 1f, HangBits);
                }

                m[22] = (uint)(buzzers.HungSide + 1);
                m[23] = (uint)(buzzers.HungBy + 1);
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
                new MatchResult((RoundEnd)m[4], (int)m[5] - 1), BitConverter.UInt32BitsToSingle(m[17]));
            if (match.Buzzers is { } buzzers)
            {
                for (int side = 0; side < 2 && side < buzzers.Count; side++)
                {
                    int hungBy = (int)m[22] - 1 == side ? (int)m[23] - 1 : -1;
                    buzzers.ApplyServer(side, (int)m[18 + side] - 1, Quant.FromRange(m[20 + side], 0f, 1f, HangBits), hungBy);
                }
            }
            match.Objective?.ApplyServer(m[6] != 0, Grid.Dequantize(m[7], m[8], m[9]), (int)m[10] - 1, m[11] != 0, (int)m[12] - 1,
                BitConverter.UInt32BitsToSingle(m[13]), (HoldStatus)m[14]);
        }

        PickupSet pickups = sim.Pickups;
        for (int p = 0; p < pickups.Items.Count && p < MaxPickups; p++)
        {
            pickups.SetTakenFromServer(p, (m[15 + p / 32] & (1u << (p % 32))) != 0);
        }
    }
}
