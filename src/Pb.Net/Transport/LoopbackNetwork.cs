using System.Buffers;

namespace Pb.Net.Transport;

/// <summary>
/// An in-memory network for tests: one server end and any number of player ends, every packet delivered at once and in
/// order. Wrap an end in a <see cref="LaggedTransport"/> to make it slow, jittery or lossy.
/// </summary>
public sealed class LoopbackNetwork
{
    private readonly List<End> _players = new();
    private End? _server;

    /// <summary>The server's end (one per network).</summary>
    public ITransport Listen()
    {
        if (_server is not null)
        {
            throw new InvalidOperationException("this network already has a server");
        }

        _server = new End(this, isServer: true, id: 0);
        return _server;
    }

    /// <summary>A new player's end, connected straight away (the server sees it as its next peer).</summary>
    public ITransport Connect()
    {
        End server = _server ?? throw new InvalidOperationException("listen first");
        var player = new End(this, isServer: false, id: _players.Count + 1);
        _players.Add(player);
        server.Enqueue(TransportEventKind.Connected, player.Id, NetChannel.Reliable, ReadOnlySpan<byte>.Empty);
        player.Enqueue(TransportEventKind.Connected, 0, NetChannel.Reliable, ReadOnlySpan<byte>.Empty);
        return player;
    }

    private sealed class End : ITransport
    {
        private readonly LoopbackNetwork _network;
        private readonly bool _isServer;
        private readonly Queue<(TransportEventKind Kind, int Peer, NetChannel Channel, byte[]? Buffer, int Length)> _inbox = new();
        private byte[]? _lent;
        private bool _closed;

        public End(LoopbackNetwork network, bool isServer, int id)
        {
            _network = network;
            _isServer = isServer;
            Id = id;
        }

        public int Id { get; }

        public void Enqueue(TransportEventKind kind, int peer, NetChannel channel, ReadOnlySpan<byte> data)
        {
            if (_closed)
            {
                return;
            }

            byte[]? buffer = null;
            if (data.Length > 0)
            {
                buffer = ArrayPool<byte>.Shared.Rent(data.Length);
                data.CopyTo(buffer);
            }

            _inbox.Enqueue((kind, peer, channel, buffer, data.Length));
        }

        public void Send(int peer, NetChannel channel, ReadOnlySpan<byte> data)
        {
            if (_closed)
            {
                return;
            }

            if (_isServer)
            {
                if (peer >= 1 && peer <= _network._players.Count)
                {
                    _network._players[peer - 1].Enqueue(TransportEventKind.Data, 0, channel, data);
                }
            }
            else
            {
                _network._server?.Enqueue(TransportEventKind.Data, Id, channel, data);
            }
        }

        public bool Poll(out TransportEvent received)
        {
            ReturnLent();
            if (!_inbox.TryDequeue(out var next))
            {
                received = default;
                return false;
            }

            _lent = next.Buffer;
            received = new TransportEvent
            {
                Kind = next.Kind, Peer = next.Peer, Channel = next.Channel,
                Data = next.Buffer is null ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>(next.Buffer, 0, next.Length),
            };
            return true;
        }

        public void Disconnect(int peer)
        {
            if (_isServer)
            {
                if (peer >= 1 && peer <= _network._players.Count)
                {
                    End player = _network._players[peer - 1];
                    player.Enqueue(TransportEventKind.Disconnected, 0, NetChannel.Reliable, ReadOnlySpan<byte>.Empty);
                    player._closed = true;
                }
            }
            else
            {
                _network._server?.Enqueue(TransportEventKind.Disconnected, Id, NetChannel.Reliable, ReadOnlySpan<byte>.Empty);
                _closed = true;
            }
        }

        public void Dispose()
        {
            if (!_closed)
            {
                if (_isServer)
                {
                    for (int i = 0; i < _network._players.Count; i++)
                    {
                        Disconnect(i + 1);
                    }
                }
                else
                {
                    Disconnect(0);
                }
            }

            ReturnLent();
        }

        private void ReturnLent()
        {
            if (_lent is not null)
            {
                ArrayPool<byte>.Shared.Return(_lent);
                _lent = null;
            }
        }
    }
}
