using System.Text;

namespace Pb.Net.Packing;

/// <summary>
/// Packs values bit by bit, least significant bits first, into a buffer it owns and reuses: once it has grown to the
/// largest packet, writing allocates nothing.
/// </summary>
public sealed class BitWriter
{
    private byte[] _buffer;
    private ulong _scratch;
    private int _scratchBits;
    private int _bytes;

    public BitWriter(int capacity = 1500)
    {
        _buffer = new byte[Math.Max(16, capacity)];
    }

    /// <summary>Bits written so far.</summary>
    public int BitCount => _bytes * 8 + _scratchBits;

    /// <summary>Bytes the packet takes once finished.</summary>
    public int ByteCount => _bytes + (_scratchBits + 7) / 8;

    public void Reset()
    {
        _bytes = 0;
        _scratch = 0;
        _scratchBits = 0;
    }

    /// <summary>The low <paramref name="bits"/> (1–32) of <paramref name="value"/>.</summary>
    public void WriteBits(uint value, int bits)
    {
        if (bits <= 0)
        {
            return;
        }

        ulong masked = bits >= 32 ? value : value & ((1u << bits) - 1u);
        _scratch |= masked << _scratchBits;
        _scratchBits += bits;
        while (_scratchBits >= 8)
        {
            Ensure(1);
            _buffer[_bytes++] = (byte)_scratch;
            _scratch >>= 8;
            _scratchBits -= 8;
        }
    }

    public void WriteBool(bool value) => WriteBits(value ? 1u : 0u, 1);

    public void WriteByte(byte value) => WriteBits(value, 8);

    public void WriteUShort(ushort value) => WriteBits(value, 16);

    public void WriteUInt(uint value) => WriteBits(value, 32);

    public void WriteInt(int value) => WriteBits((uint)value, 32);

    /// <summary>A float exactly (its 32 bits).</summary>
    public void WriteFloat(float value) => WriteBits(BitConverter.SingleToUInt32Bits(value), 32);

    /// <summary>An unsigned number in as few whole bytes as it needs (7 bits a byte).</summary>
    public void WriteVarUInt(uint value)
    {
        while (value >= 0x80)
        {
            WriteBits((value & 0x7F) | 0x80, 8);
            value >>= 7;
        }

        WriteBits(value, 8);
    }

    /// <summary>A signed number, small ones either side of 0 in a byte (zig-zag).</summary>
    public void WriteVarInt(int value) => WriteVarUInt((uint)((value << 1) ^ (value >> 31)));

    /// <summary>A signed number in <paramref name="bits"/> (two's complement), clamped to what fits.</summary>
    public void WriteSigned(int value, int bits)
    {
        int max = (1 << (bits - 1)) - 1;
        WriteBits((uint)Math.Clamp(value, -max - 1, max), bits);
    }

    /// <summary>Text as UTF-8 with its length first, cut to <paramref name="maxBytes"/> (not on a per-tick path: it may allocate).</summary>
    public void WriteString(string? text, int maxBytes = 255)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
        int length = Math.Min(bytes.Length, maxBytes);
        WriteVarUInt((uint)length);
        for (int i = 0; i < length; i++)
        {
            WriteByte(bytes[i]);
        }
    }

    public void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            WriteByte(b);
        }
    }

    /// <summary>Pads to a whole byte and returns the packet so far (valid until the next write or reset).</summary>
    public ReadOnlySpan<byte> Finish()
    {
        if (_scratchBits > 0)
        {
            WriteBits(0, 8 - _scratchBits);
        }

        return _buffer.AsSpan(0, _bytes);
    }

    private void Ensure(int more)
    {
        if (_bytes + more > _buffer.Length)
        {
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _bytes + more));
        }
    }
}
