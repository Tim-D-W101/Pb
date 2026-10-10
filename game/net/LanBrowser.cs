using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pb.Net.Discovery;
using Pb.Net.Packing;

namespace Pb.Game.Net;

/// <summary>
/// Finds games on your network: every second it broadcasts a search on the discovery port (and asks this computer
/// too, for a game hosted here), and lists the hosts that answer, forgetting any not heard from for a few seconds.
/// </summary>
public sealed class LanBrowser : IDisposable
{
    private const double Every_s = 1.0;
    private const double Forget_s = 4.0;

    private readonly PacketPeerUdp _udp = new();
    private readonly int _port;
    private readonly BitWriter _writer = new(64);
    private readonly Dictionary<string, (GameAnnouncement Game, double Seen)> _found = new();
    private double _searched = double.NegativeInfinity;

    public LanBrowser(int discoveryPort)
    {
        _port = discoveryPort;
        _udp.SetBroadcastEnabled(true);
        Bound = _udp.Bind(0) == Error.Ok;
    }

    public bool Bound { get; }

    /// <summary>The games heard from lately, by name: the address to join ("host:port") and what they said.</summary>
    public IReadOnlyList<(string Address, GameAnnouncement Game)> Games =>
        _found.OrderBy(f => f.Value.Game.Name, StringComparer.CurrentCultureIgnoreCase).Select(f => (f.Key, f.Value.Game)).ToList();

    /// <summary>Searches again when it's time, and takes in the answers; <paramref name="now"/> in seconds.</summary>
    public void Poll(double now)
    {
        if (!Bound)
        {
            return;
        }

        if (now - _searched >= Every_s)
        {
            _searched = now;
            _writer.Reset();
            DiscoveryMessage.WriteSearch(_writer);
            byte[] search = _writer.Finish().ToArray();
            foreach (string to in new[] { "255.255.255.255", "127.0.0.1" })
            {
                _udp.SetDestAddress(to, _port);
                _udp.PutPacket(search);
            }
        }

        for (int guard = 0; guard < 64 && _udp.GetAvailablePacketCount() > 0; guard++)
        {
            byte[] packet = _udp.GetPacket();
            string from = _udp.GetPacketIP();
            if (from.Length > 0 && DiscoveryMessage.ReadAnswer(packet) is { } game)
            {
                _found[$"{from}:{game.Port}"] = (game, now);
            }
        }

        foreach (string gone in _found.Where(f => now - f.Value.Seen > Forget_s).Select(f => f.Key).ToArray())
        {
            _found.Remove(gone);
        }
    }

    public void Dispose() => _udp.Close();
}
