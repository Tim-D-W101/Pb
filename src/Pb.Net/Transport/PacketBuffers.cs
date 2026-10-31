namespace Pb.Net.Transport;

/// <summary>
/// Byte arrays for packets held in memory (the in-memory network's inboxes, a lagged connection's queues), kept for
/// reuse by their owner and never given back. <see cref="System.Buffers.ArrayPool{T}.Shared"/> isn't used for these:
/// it frees what it holds whenever a full collection runs under memory pressure (and after a while unused), so a
/// network tick after one allocated afresh, at random. Sizes are powers of two from 64 bytes, so a few arrays serve
/// every packet. Not thread-safe: one owner, one thread, as every transport here is used.
/// </summary>
public sealed class PacketBuffers
{
    private const int SmallestShift = 6;
    private const int Buckets = 26 - SmallestShift; // 64 B .. 32 MB

    private readonly Stack<byte[]>[] _free = new Stack<byte[]>[Buckets];

    public PacketBuffers()
    {
        for (int i = 0; i < Buckets; i++)
        {
            _free[i] = new Stack<byte[]>(16);
        }
    }

    /// <summary>Arrays made so far (a test can check that steady traffic stops making them).</summary>
    public int Made { get; private set; }

    /// <summary>An array of at least <paramref name="length"/> bytes: a kept one if there is one, else a new one.</summary>
    public byte[] Rent(int length)
    {
        int bucket = BucketOf(length);
        if (_free[bucket].TryPop(out byte[]? kept))
        {
            return kept;
        }

        Made++;
        return new byte[1 << (bucket + SmallestShift)];
    }

    /// <summary>Gives back an array <see cref="Rent"/> made, to be handed out again.</summary>
    public void Return(byte[] buffer)
    {
        int bucket = BucketOf(buffer.Length);
        if (buffer.Length == 1 << (bucket + SmallestShift))
        {
            _free[bucket].Push(buffer);
        }
    }

    private static int BucketOf(int length)
    {
        int shift = SmallestShift;
        while (shift < SmallestShift + Buckets - 1 && 1 << shift < length)
        {
            shift++;
        }

        if (1 << shift < length)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "a packet larger than any buffer");
        }

        return shift - SmallestShift;
    }
}
