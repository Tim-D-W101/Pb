using System.Buffers;
using Pb.Sim.Core;

namespace Pb.Net.Transport;

/// <summary>A pretend bad connection: the round trip it adds, jitter, and the share of unreliable packets lost or doubled.</summary>
public sealed record LagSettings
{
    public static readonly LagSettings None = new();

    /// <summary>Added to the round trip (ms), half each way.</summary>
    public float RoundTrip_ms { get; init; }

    /// <summary>Each packet's delay varies by up to this either way (ms), which also puts unreliable packets out of order.</summary>
    public float Jitter_ms { get; init; }

    /// <summary>Unreliable packets lost each way (%).</summary>
    public float Loss_pct { get; init; }

    /// <summary>Unreliable packets that arrive twice (%).</summary>
    public float Duplicate_pct { get; init; }

    public bool Any => RoundTrip_ms > 0f || Jitter_ms > 0f || Loss_pct > 0f || Duplicate_pct > 0f;
}

/// <summary>
/// Wraps a connection to make it slow, jittery and lossy both ways (<c>--net-lag</c>, tests, CI): sends are held for
/// half the round trip, and so is what arrives. Reliable packets and connection events are never lost and keep their
/// order; unreliable ones are lost, doubled and reordered as the settings say.
/// </summary>
public sealed class LaggedTransport : ITransport
{
    private readonly ITransport _inner;
    private readonly Func<double> _clock;
    private readonly List<Held> _outgoing = new();
    private readonly List<Held> _incoming = new();
    private readonly Dictionary<int, double> _lastReliableOut = new();
    private readonly Dictionary<int, double> _lastReliableIn = new();
    private Pcg32 _rng;
    private byte[]? _lent;

    /// <param name="clock">Seconds, from any start.</param>
    public LaggedTransport(ITransport inner, LagSettings settings, Func<double> clock, ulong seed = 1)
    {
        _inner = inner;
        Settings = settings;
        _clock = clock;
        _rng = new Pcg32(seed, 0x1A66ED);
    }

    public LagSettings Settings { get; set; }

    public void Send(int peer, NetChannel channel, ReadOnlySpan<byte> data)
    {
        Hold(_outgoing, _lastReliableOut, TransportEventKind.Data, peer, channel, data);
        Flush();
    }

    public bool Poll(out TransportEvent received)
    {
        if (_lent is not null)
        {
            ArrayPool<byte>.Shared.Return(_lent);
            _lent = null;
        }

        Flush();
        while (_inner.Poll(out TransportEvent e))
        {
            Hold(_incoming, _lastReliableIn, e.Kind, e.Peer, e.Kind == TransportEventKind.Data ? e.Channel : NetChannel.Reliable, e.Data.Span);
        }

        double now = _clock();
        if (_incoming.Count > 0 && _incoming[0].At <= now)
        {
            Held next = _incoming[0];
            _incoming.RemoveAt(0);
            _lent = next.Buffer;
            received = new TransportEvent
            {
                Kind = next.Kind, Peer = next.Peer, Channel = next.Channel,
                Data = next.Buffer is null ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>(next.Buffer, 0, next.Length),
            };
            return true;
        }

        received = default;
        return false;
    }

    public void Disconnect(int peer) => _inner.Disconnect(peer);

    public void Dispose()
    {
        foreach (Held h in _outgoing.Concat(_incoming))
        {
            if (h.Buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(h.Buffer);
            }
        }

        _outgoing.Clear();
        _incoming.Clear();
        _inner.Dispose();
    }

    /// <summary>Sends whatever has been held long enough.</summary>
    public void Flush()
    {
        double now = _clock();
        while (_outgoing.Count > 0 && _outgoing[0].At <= now)
        {
            Held next = _outgoing[0];
            _outgoing.RemoveAt(0);
            _inner.Send(next.Peer, next.Channel, next.Buffer is null ? ReadOnlySpan<byte>.Empty : next.Buffer.AsSpan(0, next.Length));
            if (next.Buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(next.Buffer);
            }
        }
    }

    private void Hold(List<Held> queue, Dictionary<int, double> lastReliable, TransportEventKind kind, int peer, NetChannel channel,
        ReadOnlySpan<byte> data)
    {
        LagSettings s = Settings;
        bool unreliable = kind == TransportEventKind.Data && channel == NetChannel.Unreliable;
        if (unreliable && s.Loss_pct > 0f && _rng.NextFloat() * 100f < s.Loss_pct)
        {
            return;
        }

        int copies = unreliable && s.Duplicate_pct > 0f && _rng.NextFloat() * 100f < s.Duplicate_pct ? 2 : 1;
        for (int c = 0; c < copies; c++)
        {
            double at = _clock() + (s.RoundTrip_ms * 0.5 + (s.Jitter_ms > 0f ? _rng.Symmetric(s.Jitter_ms) : 0f)) / 1000.0;
            if (!unreliable)
            {
                // Reliable packets and connection events keep their order.
                if (lastReliable.TryGetValue(peer, out double last) && at < last)
                {
                    at = last;
                }

                lastReliable[peer] = at;
            }

            byte[]? buffer = null;
            if (data.Length > 0)
            {
                buffer = ArrayPool<byte>.Shared.Rent(data.Length);
                data.CopyTo(buffer);
            }

            var held = new Held(at, kind, peer, channel, buffer, data.Length);
            int index = queue.Count;
            while (index > 0 && queue[index - 1].At > at)
            {
                index--;
            }

            queue.Insert(index, held);
        }
    }

    private readonly record struct Held(double At, TransportEventKind Kind, int Peer, NetChannel Channel, byte[]? Buffer, int Length);
}
