using System;
using Godot;
using Pb.Net.Discovery;
using Pb.Net.Packing;

namespace Pb.Game.Net;

/// <summary>
/// Hosting: answers searches for games on your network (UDP on the discovery port, net.jsonc's 47821) with the game as
/// it stands. If another game on this computer already answers there, this one can still be joined by address.
/// </summary>
public sealed class LanAnnouncer : IDisposable
{
    private readonly PacketPeerUdp _udp = new();
    private readonly Func<GameAnnouncement> _game;
    private readonly BitWriter _writer = new(512);

    public LanAnnouncer(int port, Func<GameAnnouncement> game)
    {
        _game = game;
        Listening = _udp.Bind(port) == Error.Ok;
        GD.Print(Listening ? $"NET answering searches on UDP port {port}" : $"NET can't answer searches on UDP port {port}: another game here already does");
    }

    public bool Listening { get; }

    /// <summary>Answers the searches that have come since the last call.</summary>
    public void Poll()
    {
        if (!Listening)
        {
            return;
        }

        for (int guard = 0; guard < 64 && _udp.GetAvailablePacketCount() > 0; guard++)
        {
            byte[] packet = _udp.GetPacket();
            string from = _udp.GetPacketIP();
            int port = _udp.GetPacketPort();
            if (from.Length == 0 || !DiscoveryMessage.IsSearch(packet))
            {
                continue;
            }

            _writer.Reset();
            DiscoveryMessage.WriteAnswer(_writer, _game());
            _udp.SetDestAddress(from, port);
            _udp.PutPacket(_writer.Finish().ToArray());
        }
    }

    public void Dispose() => _udp.Close();
}
