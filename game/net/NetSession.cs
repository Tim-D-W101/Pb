using System;
using System.Diagnostics;
using System.Linq;
using Godot;
using Pb.Net;
using Pb.Net.Client;
using Pb.Net.Protocol;
using Pb.Net.Server;
using Pb.Net.Transport;

namespace Pb.Game.Net;

/// <summary>
/// Playing with others, kept across scene changes (the menu, the lobby, each round): hosting (a <see cref="NetServer"/>)
/// or joined to a host (a <see cref="NetClient"/>), over ENet (through a <see cref="LaggedTransport"/> when --net-lag
/// asks), with the round to build next. Between rounds it polls the network itself; during one, the level's tick does.
/// </summary>
public partial class NetSession : Node
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>The session under way, if any.</summary>
    public static NetSession? Current { get; private set; }

    public NetSettings Settings { get; private set; } = null!;

    public NetServer? Server { get; private set; }

    public NetClient? Client { get; private set; }

    public bool Hosting => Server is not null;

    /// <summary>Seconds since the session began (the network's clock).</summary>
    public double Now => _clock.Elapsed.TotalSeconds;

    /// <summary>Your name and character in the game.</summary>
    public string LocalName { get; private set; } = "";

    public byte LocalLook { get; private set; }

    /// <summary>The round to build next: cast by the host, or as a joining copy was sent it.</summary>
    public RoundSetupMessage? Round { get; set; }

    /// <summary>A joining copy: <see cref="Round"/> has come and hasn't been built yet.</summary>
    public bool RoundWaiting { get; set; }

    /// <summary>Rounds played so far this session (the next round's number is one more).</summary>
    public int RoundsPlayed { get; set; }

    /// <summary>A round is under way: the level's tick polls the network, not this.</summary>
    public bool InRound { get; set; }

    /// <summary>Why the session ended, once it has (the host left, the connection was lost, the host turned you away).</summary>
    public string? EndReason { get; private set; }

    /// <summary>A joining copy: the host sent the setup of a round to build.</summary>
    public event Action<RoundSetupMessage>? RoundArrived;

    /// <summary>The session is over, and why.</summary>
    public event Action<string>? Ended;

    /// <summary>Starts hosting on <paramref name="port"/>; your own player is player 0.</summary>
    public static NetSession Host(SceneTree tree, NetSettings settings, ServerIdentity identity, int port, LagSettings lag, string name, byte look)
    {
        Current?.Leave("a new game");
        var session = new NetSession { Name = "NetSession", Settings = settings, LocalName = name, LocalLook = look };
        ITransport transport = EnetTransport.Listen(port, settings.MaxPeople, settings.LinkTimeoutMin, settings.LinkTimeoutMax);
        if (lag.Any)
        {
            transport = new LaggedTransport(transport, lag, () => session.Now);
        }

        session.Server = new NetServer(transport, settings, identity, () => session.Now, 1f / 120f);
        session.Server.Violation += (link, what) => GD.Print($"NET {link.Name} sent {what}");
        session.Server.Joined += link => GD.Print($"NET {link.Name} joined ({link.Peer})");
        session.Server.Left += link => GD.Print($"NET {link.Name} left");
        Attach(tree, session);
        GD.Print($"NET hosting \"{identity.Name}\" on UDP port {port} (build {identity.Build})");
        return session;
    }

    /// <summary>Starts joining the host at <paramref name="address"/>.</summary>
    public static NetSession Join(SceneTree tree, NetSettings settings, string address, int port, HelloMessage hello, LagSettings lag)
    {
        Current?.Leave("a new game");
        var session = new NetSession { Name = "NetSession", Settings = settings, LocalName = hello.Name, LocalLook = hello.Look };
        ITransport transport = EnetTransport.Connect(address, port, settings.LinkTimeoutMin, settings.LinkTimeoutMax);
        if (lag.Any)
        {
            transport = new LaggedTransport(transport, lag, () => session.Now);
        }

        session.Client = new NetClient(transport, settings, () => session.Now, hello, 120f);
        session.Client.Join();
        Attach(tree, session);
        GD.Print($"NET joining {address}:{port} as {hello.Name}");
        return session;
    }

    /// <summary>Ends the session (telling the others), leaving the scene to whoever called.</summary>
    public void Leave(string reason)
    {
        if (Current == this)
        {
            Current = null;
        }

        Client?.Leave(reason);
        Client?.Dispose();
        if (Server is { } server)
        {
            // Everyone who joined is told why the game ended, then the host stops listening.
            foreach (ClientLink link in server.Clients.ToArray())
            {
                if (link.Welcomed)
                {
                    server.Remove(link, RefusedReason.Removed, "The host ended the game.");
                }
            }

            server.Dispose();
        }

        EndReason ??= reason;
        QueueFree();
    }

    /// <summary>Takes in what has arrived (between rounds).</summary>
    public void Poll()
    {
        Server?.Poll();
        if (Client is not { } client)
        {
            return;
        }

        client.Poll();
        if (client.TakeRoundSetup() is { } setup)
        {
            Round = setup;
            RoundWaiting = true;
            RoundArrived?.Invoke(setup);
        }

        if (client.State is ClientState.Gone or ClientState.Refused && EndReason is null)
        {
            EndReason = client.State == ClientState.Refused ? client.Refusal?.Text ?? "The host turned you away." : client.GoneReason;
            GD.Print($"NET ended: {EndReason}");
            Ended?.Invoke(EndReason);
        }
    }

    public override void _Process(double delta)
    {
        if (!InRound)
        {
            Poll();
        }
    }

    public override void _ExitTree()
    {
        if (Current == this)
        {
            Current = null;
        }
    }

    private static void Attach(SceneTree tree, NetSession session)
    {
        Current = session;
        // On the root, it outlives the scenes; it polls even while a menu pauses the tree.
        session.ProcessMode = ProcessModeEnum.Always;
        tree.Root.CallDeferred(Node.MethodName.AddChild, session);
    }
}
