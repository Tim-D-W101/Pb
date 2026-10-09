using Pb.Net.Packing;

namespace Pb.Net.Protocol;

/// <summary>
/// Sends a fixed list of fields (each a whole number of known width) as the difference from a baseline both ends hold:
/// one bit for "anything changed", then for each field one bit and, if it changed, its value. Against a baseline of zeros
/// it's a full copy that leaves out the fields that are zero.
/// </summary>
public static class Delta
{
    public static void Write(BitWriter writer, ReadOnlySpan<uint> now, ReadOnlySpan<uint> baseline, ReadOnlySpan<byte> widths)
    {
        bool any = !now.SequenceEqual(baseline);
        writer.WriteBool(any);
        if (!any)
        {
            return;
        }

        for (int i = 0; i < now.Length; i++)
        {
            bool changed = now[i] != baseline[i];
            writer.WriteBool(changed);
            if (changed)
            {
                writer.WriteBits(now[i], widths[i]);
            }
        }
    }

    /// <summary>Reads into <paramref name="fields"/>, which holds the baseline beforehand.</summary>
    public static void Read(ref BitReader reader, Span<uint> fields, ReadOnlySpan<byte> widths)
    {
        if (!reader.ReadBool())
        {
            return;
        }

        for (int i = 0; i < fields.Length; i++)
        {
            if (reader.ReadBool())
            {
                fields[i] = reader.ReadBits(widths[i]);
            }
        }
    }
}
