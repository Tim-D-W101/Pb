using System;
using System.Globalization;
using Godot;
using Pb.Game.Core;
using Pb.Net;
using Pb.Net.Protocol;
using Pb.Net.Server;
using Pb.Net.Transport;

namespace Pb.Game.Net;

/// <summary>
/// Starts a game with others: hosting one, or joining one at an address ("host" or "host:port"). The network's settings
/// come from net.jsonc, the build stamp and data hash from <see cref="BuildStamp"/>, and the command line can make the
/// connection worse on purpose to try it (<c>--net-lag=MS</c> added to the round trip, <c>--net-jitter=MS</c>,
/// <c>--net-loss=PCT</c> of the unreliable packets each way).
/// </summary>
public static class NetStart
{
    public static NetSettings Settings() => NetSettings.Load(new GodotDataSource());

    /// <summary>Hosting: others join on <paramref name="port"/> (net.jsonc's when null).</summary>
    public static NetSession Host(SceneTree tree, string name, byte look, int? port = null, string password = "")
    {
        NetSettings settings = Settings();
        var identity = new ServerIdentity($"{name}'s game", BuildStamp.Build, BuildStamp.DataHash, password);
        return NetSession.Host(tree, settings, identity, port ?? settings.Port, Lag(), name, look);
    }

    /// <summary>Joining the host at <paramref name="address"/> ("host" or "host:port").</summary>
    public static NetSession Join(SceneTree tree, string address, string name, byte look, string password = "")
    {
        NetSettings settings = Settings();
        (string host, int port) = Address(address, settings.Port);
        var hello = new HelloMessage
        {
            Protocol = NetProtocol.Version, Build = BuildStamp.Build, DataHash = BuildStamp.DataHash, Name = name, Password = password, Look = look,
        };
        return NetSession.Join(tree, settings, host, port, hello, Lag());
    }

    /// <summary>"host" or "host:port" (an IPv6 address in brackets: "[::1]:47820").</summary>
    public static (string Host, int Port) Address(string address, int defaultPort)
    {
        string a = address.Trim();
        if (a.StartsWith('['))
        {
            int close = a.IndexOf(']');
            if (close > 0)
            {
                string inside = a[1..close];
                return close + 1 < a.Length && a[close + 1] == ':' && int.TryParse(a[(close + 2)..], out int p6) ? (inside, p6) : (inside, defaultPort);
            }
        }

        int colon = a.LastIndexOf(':');
        if (colon > 0 && a.IndexOf(':') == colon && int.TryParse(a[(colon + 1)..], out int port))
        {
            return (a[..colon], port);
        }

        return (a, defaultPort);
    }

    /// <summary>The connection made worse on purpose, from the command line (none by default).</summary>
    public static LagSettings Lag() => new()
    {
        RoundTrip_ms = Number("--net-lag"), Jitter_ms = Number("--net-jitter"), Loss_pct = Number("--net-loss"),
    };

    private static float Number(string flag) =>
        float.TryParse(Args.Value(flag), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? Math.Max(0f, value) : 0f;
}
