using System;
using System.Collections.Generic;
using Godot;
using Pb.Net.Transport;

namespace Pb.Game.Net;

/// <summary>
/// <see cref="ITransport"/> over the ENet built into Godot (UDP): channel 0 carries the unreliable packets (commands,
/// snapshots), channel 1 the reliable ones. A host listens on a port for up to its most people; a player connects to
/// one host, which is its peer 0.
/// </summary>
public sealed class EnetTransport : ITransport
{
    private const int Channels = 2;

    private readonly ENetConnection _connection;
    private readonly bool _server;
    private readonly Dictionary<ulong, (int Id, ENetPacketPeer Peer)> _byPeer = new();
    private readonly Dictionary<int, ENetPacketPeer> _byId = new();
    private readonly Queue<TransportEvent> _arrived = new();
    private readonly int _timeoutMin_ms;
    private readonly int _timeoutMax_ms;
    private int _nextId = 1;
    private bool _disposed;

    private EnetTransport(ENetConnection connection, bool server, float timeoutMin, float timeoutMax)
    {
        _connection = connection;
        _server = server;
        _timeoutMin_ms = (int)(timeoutMin * 1000f);
        _timeoutMax_ms = (int)(timeoutMax * 1000f);
    }

    /// <summary>
    /// Listens on <paramref name="port"/> (every address) for up to <paramref name="peers"/> players. A player who confirms
    /// nothing for <paramref name="timeoutMin"/> s at the least, <paramref name="timeoutMax"/> s at the most, is gone.
    /// </summary>
    public static EnetTransport Listen(int port, int peers, float timeoutMin, float timeoutMax)
    {
        var connection = new ENetConnection();
        Error error = connection.CreateHostBound("*", port, peers, Channels);
        if (error != Error.Ok)
        {
            throw new InvalidOperationException($"Can't listen on UDP port {port} ({error}): is another game already using it?");
        }

        return new EnetTransport(connection, server: true, timeoutMin, timeoutMax);
    }

    /// <summary>Starts connecting to a host; the connection shows as <see cref="TransportEventKind.Connected"/> from peer 0.</summary>
    public static EnetTransport Connect(string address, int port, float timeoutMin, float timeoutMax)
    {
        var connection = new ENetConnection();
        Error error = connection.CreateHost(1, Channels);
        if (error != Error.Ok)
        {
            throw new InvalidOperationException($"Can't open a connection ({error}).");
        }

        var transport = new EnetTransport(connection, server: false, timeoutMin, timeoutMax);
        ENetPacketPeer? peer = connection.ConnectToHost(address, port, Channels);
        if (peer is null)
        {
            throw new InvalidOperationException($"Can't reach {address}:{port}.");
        }

        transport.Add(peer, 0);
        return transport;
    }

    public void Send(int peer, NetChannel channel, ReadOnlySpan<byte> data)
    {
        if (_disposed || !_byId.TryGetValue(peer, out ENetPacketPeer? target))
        {
            return;
        }

        int flags = channel == NetChannel.Reliable ? (int)ENetPacketPeer.FlagReliable : 0;
        target.Send(channel == NetChannel.Reliable ? 1 : 0, data.ToArray(), flags);
    }

    public bool Poll(out TransportEvent received)
    {
        if (_arrived.Count == 0 && !_disposed)
        {
            Service();
        }

        return _arrived.TryDequeue(out received);
    }

    public float RoundTrip(int peer) =>
        _byId.TryGetValue(peer, out ENetPacketPeer? target) ? (float)target.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime) / 1000f : -1f;

    public void Disconnect(int peer)
    {
        if (_byId.TryGetValue(peer, out ENetPacketPeer? target))
        {
            target.PeerDisconnectLater();
            _connection.Flush();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (ENetPacketPeer peer in _byId.Values)
        {
            peer.PeerDisconnectNow();
        }

        _connection.Flush();
        _connection.Destroy();
        _disposed = true;
    }

    /// <summary>Takes in everything ENet has (without waiting) and sends what's queued.</summary>
    private void Service()
    {
        for (int guard = 0; guard < 4096; guard++)
        {
            Godot.Collections.Array result = _connection.Service();
            var type = (ENetConnection.EventType)(int)result[0];
            if (type == ENetConnection.EventType.None)
            {
                break;
            }

            var peer = result[1].As<ENetPacketPeer>();
            switch (type)
            {
                case ENetConnection.EventType.Connect:
                    int id = _server ? Add(peer, _nextId++) : 0;
                    _arrived.Enqueue(new TransportEvent { Kind = TransportEventKind.Connected, Peer = id });
                    break;
                case ENetConnection.EventType.Disconnect:
                    if (_byPeer.Remove(Key(peer), out var gone))
                    {
                        _byId.Remove(gone.Id);
                        _arrived.Enqueue(new TransportEvent { Kind = TransportEventKind.Disconnected, Peer = gone.Id });
                    }

                    break;
                case ENetConnection.EventType.Receive:
                    byte[] packet = peer.GetPacket();
                    int channel = (int)result[3];
                    if (_byPeer.TryGetValue(Key(peer), out var from))
                    {
                        _arrived.Enqueue(new TransportEvent
                        {
                            Kind = TransportEventKind.Data, Peer = from.Id, Channel = channel == 1 ? NetChannel.Reliable : NetChannel.Unreliable,
                            Data = packet,
                        });
                    }

                    break;
                case ENetConnection.EventType.Error:
                    _arrived.Enqueue(new TransportEvent { Kind = TransportEventKind.Disconnected, Peer = 0 });
                    return;
            }
        }
    }

    private int Add(ENetPacketPeer peer, int id)
    {
        // Generous: neither end hears from the other while it builds a level.
        peer.SetTimeout(32, _timeoutMin_ms, _timeoutMax_ms);
        _byPeer[Key(peer)] = (id, peer);
        _byId[id] = peer;
        return id;
    }

    private static ulong Key(ENetPacketPeer peer) => peer.GetInstanceId();
}
