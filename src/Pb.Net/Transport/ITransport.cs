namespace Pb.Net.Transport;

/// <summary>How a packet travels: unreliable packets can be lost and come out of order; reliable ones arrive, in order.</summary>
public enum NetChannel : byte
{
    Unreliable,
    Reliable,
}

public enum TransportEventKind : byte
{
    Connected,
    Disconnected,
    Data,
}

/// <summary>Something that arrived from a peer: it connected, it went, or it sent a packet.</summary>
public readonly struct TransportEvent
{
    public TransportEventKind Kind { get; init; }

    /// <summary>A server's peers are its players (1, 2, …); a player's only peer is the server (0).</summary>
    public int Peer { get; init; }

    public NetChannel Channel { get; init; }

    /// <summary>The packet, valid until the next <see cref="ITransport.Poll"/>.</summary>
    public ReadOnlyMemory<byte> Data { get; init; }
}

/// <summary>
/// The connection between copies of the game: ENet over UDP in the game, in memory for tests, either of them through a
/// <see cref="LaggedTransport"/> to try it with a bad connection.
/// </summary>
public interface ITransport : IDisposable
{
    /// <summary>Sends a packet (copied, so the caller may reuse its buffer at once).</summary>
    void Send(int peer, NetChannel channel, ReadOnlySpan<byte> data);

    /// <summary>The next thing that has arrived, if any; its data stays valid until the next call.</summary>
    bool Poll(out TransportEvent received);

    /// <summary>Hangs up on a peer (a player, or for a player the server).</summary>
    void Disconnect(int peer);

    /// <summary>The connection's own measure of the round trip to a peer (s), or −1 if it has none.</summary>
    float RoundTrip(int peer) => -1f;
}
