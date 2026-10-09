using System.Text;

namespace Pb.Net.Packing;

/// <summary>
/// Reads what a <see cref="BitWriter"/> wrote. Reading past the end gives zeros and sets <see cref="Overflowed"/>: a
/// short or garbled packet never throws, and whoever reads it checks the flag before trusting what came out.
/// </summary>
public ref struct BitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _bit;

    public BitReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _bit = 0;
        Overflowed = false;
    }

    public bool Overflowed { get; private set; }

    public int BitsLeft => Math.Max(0, _data.Length * 8 - _bit);

    public uint ReadBits(int bits)
    {
        if (bits <= 0)
        {
            return 0;
        }

        if (bits > BitsLeft)
        {
            Overflowed = true;
            _bit = _data.Length * 8;
            return 0;
        }

        uint value = 0;
        int written = 0;
        while (written < bits)
        {
            int index = _bit >> 3;
            int offset = _bit & 7;
            int take = Math.Min(8 - offset, bits - written);
            uint chunk = (uint)(_data[index] >> offset) & ((1u << take) - 1u);
            value |= chunk << written;
            written += take;
            _bit += take;
        }

        return value;
    }

    public bool ReadBool() => ReadBits(1) != 0;

    public byte ReadByte() => (byte)ReadBits(8);

    public ushort ReadUShort() => (ushort)ReadBits(16);

    public uint ReadUInt() => ReadBits(32);

    public int ReadInt() => (int)ReadBits(32);

    public float ReadFloat() => BitConverter.UInt32BitsToSingle(ReadBits(32));

    public uint ReadVarUInt()
    {
        uint value = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            uint b = ReadBits(8);
            value |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0 || Overflowed)
            {
                return value;
            }
        }

        Overflowed = true;
        return value;
    }

    public int ReadVarInt()
    {
        uint z = ReadVarUInt();
        return (int)(z >> 1) ^ -(int)(z & 1);
    }

    public int ReadSigned(int bits)
    {
        uint raw = ReadBits(bits);
        int shift = 32 - bits;
        return (int)(raw << shift) >> shift;
    }

    /// <summary>Text written by <see cref="BitWriter.WriteString"/>, at most <paramref name="maxBytes"/> long (allocates).</summary>
    public string ReadString(int maxBytes = 255)
    {
        uint length = ReadVarUInt();
        if (length > maxBytes || length * 8 > BitsLeft)
        {
            Overflowed = true;
            return "";
        }

        Span<byte> bytes = length <= 256 ? stackalloc byte[(int)length] : new byte[length];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = ReadByte();
        }

        return Encoding.UTF8.GetString(bytes);
    }

    public void ReadBytes(Span<byte> into)
    {
        for (int i = 0; i < into.Length; i++)
        {
            into[i] = ReadByte();
        }
    }
}
