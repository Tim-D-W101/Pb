using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Pb.Net;
using Pb.Net.Client;
using Pb.Net.Lobby;
using Pb.Net.Protocol;
using Pb.Net.Server;
using Pb.Net.Transport;
using Pb.Sim.Match;

namespace Pb.Game.Net;

/// <summary>
/// Playing with others, kept across scene changes (the menu, the lobby, each round): hosting (a <see cref="NetServer"/>)
/// or joined to a host (a <see cref="NetClient"/>), over ENet (through a <see cref="LaggedTransport"/> when --net-lag
/// asks), with the round to build next. Between rounds it polls the network itself; during one, the level's tick does.
/// </summary>
public partial class NetSession : Node
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _hostVersion;
    private LanAnnouncer? _announcer;

    /// <summary>The session under way, if any.</summary>
    public static NetSession? Current { get; private set; }

    public NetSettings Settings { get; private set; } = null!;

    public NetServer? Server { get; private set; }

    public NetClient? Client { get; private set; }

    public bool Hosting => Server is not null;

    /// <summary>Hosting: the lobby (who's in, sides, ready, the choices, the countdown, the vote, chat, the score).</summary>
    public LobbyHost? Lobby { get; private set; }

    /// <summary>The lobby as this copy sees it: its own when hosting, else as the host last sent it (null until it has).</summary>
    public LobbyState? LobbyView => Lobby?.State ?? Client?.Lobby;

    /// <summary>Goes up whenever the lobby changes (for screens to know when to refresh).</summary>
    public int LobbyVersion => Lobby is not null ? _hostVersion : Client?.LobbyVersion ?? 0;

    /// <summary>Your member number in the lobby (the host's is 0; −1 until let in).</summary>
    public int MemberId => Hosting ? LobbyHost.HostId : Client?.Welcome?.ClientId ?? -1;

    public LobbyMember? Me => LobbyView?.Find(MemberId);

    /// <summary>The lobby's countdown or vote: its time left now (s).</summary>
    public float TimeLeft => Lobby?.TimeLeft
        ?? (Client?.Lobby is { } seen ? (float)Math.Max(0.0, seen.TimeLeft - (Now - Client.LobbyAt)) : 0f);

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

    /// <summary>Starts hosting on <paramref name="port"/> with these choices in the lobby; your own player is player 0.</summary>
    public static NetSession Host(SceneTree tree, NetSettings settings, ServerIdentity identity, int port, LagSettings lag, string name, byte look,
        MatchRules rules, LobbyChoices choices)
    {
        Current?.Leave("a new game");
        var session = new NetSession { Name = "NetSession", Settings = settings, LocalName = name, LocalLook = look };
        ITransport transport = EnetTransport.Listen(port, settings.MaxPeople, settings.LinkTimeoutMin, settings.LinkTimeoutMax);
        if (lag.Any)
        {
            transport = new LaggedTransport(transport, lag, () => session.Now);
        }

        session.Server = new NetServer(transport, settings, identity, () => session.Now, 1f / 120f) { HostName = name };
        session.Lobby = new LobbyHost(session.Server, settings, rules, choices, () => session.Now, new LobbyMember { Name = name, Look = look });
        session.Lobby.Changed += () => session._hostVersion++;
        session.Server.Violation += (link, what) => GD.Print($"NET {link.Name} sent {what}");
        session.Server.Joined += link => GD.Print($"NET {link.Name} joined ({link.Peer})");
        session.Server.Left += link => GD.Print($"NET {link.Name} left");
        session.Server.Refused += (name, why) => GD.Print($"NET turned away {name}: {why}");
        Attach(tree, session);
        GD.Print($"NET hosting \"{identity.Name}\" on UDP port {port} (build {identity.Build})");
        return session;
    }

    /// <summary>
    /// A dedicated server: hosts on <paramref name="port"/> with nobody of its own playing, the lobby starting with
    /// <paramref name="choices"/>.
    /// </summary>
    public static NetSession Serve(SceneTree tree, NetSettings settings, ServerIdentity identity, int port, MatchRules rules, LobbyChoices choices,
        int maxPeople)
    {
        Current?.Leave("a new game");
        var limited = maxPeople == settings.MaxPeople ? settings : settings.WithMaxPeople(maxPeople);
        var session = new NetSession { Name = "NetSession", Settings = limited, Dedicated = true };
        ITransport transport = EnetTransport.Listen(port, limited.MaxPeople, limited.LinkTimeoutMin, limited.LinkTimeoutMax);
        session.Server = new NetServer(transport, limited, identity, () => session.Now, 1f / 120f) { Dedicated = true };
        session.Lobby = new LobbyHost(session.Server, limited, rules, choices, () => session.Now);
        session.Lobby.Changed += () => session._hostVersion++;
        session.Server.Violation += (link, what) => ServerLog.Line($"{link.Name} sent {what}");
        session.Server.Joined += link => ServerLog.Line($"{link.Name} joined ({session.Server.Clients.Count(c => c.Welcomed)} in the game)");
        session.Server.Left += link => ServerLog.Line($"{link.Name} left");
        session.Server.Refused += (name, why) => ServerLog.Line($"turned away {name}: {why}");
        Attach(tree, session);
        ServerLog.Line($"serving \"{identity.Name}\" on UDP port {port} (build {identity.Build}, at most {limited.MaxPeople} people)");
        return session;
    }

    /// <summary>A dedicated server: nobody of its own plays.</summary>
    public bool Dedicated { get; private set; }

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
        Lobby?.Dispose();
        _announcer?.Dispose();
        _announcer = null;
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

    /// <summary>Hosting: answers searches for games on your network with what <paramref name="game"/> says.</summary>
    public void Announce(Func<Pb.Net.Discovery.GameAnnouncement> game)
    {
        _announcer?.Dispose();
        _announcer = new LanAnnouncer(Settings.DiscoveryPort, game);
    }

    /// <summary>Asks the lobby for something: a side, ready, a character, a vote (hosting, it's decided here).</summary>
    public void Ask(LobbyAsk ask, int value)
    {
        if (Lobby is { } lobby)
        {
            lobby.Ask(LobbyHost.HostId, ask, value);
        }
        else
        {
            Client?.Ask(ask, value);
        }
    }

    /// <summary>Says something in the chat, to everyone or only your side.</summary>
    public void Say(string text, bool teamOnly)
    {
        if (Lobby is { } lobby)
        {
            lobby.Say(LobbyHost.HostId, text, teamOnly);
        }
        else
        {
            Client?.Say(text, teamOnly);
        }
    }

    /// <summary>Chat lines since the last call.</summary>
    public IReadOnlyList<ChatLine> TakeChat() => Lobby?.TakeChat() ?? Client?.TakeChat() ?? Array.Empty<ChatLine>();

    public override void _Process(double delta)
    {
        if (!InRound)
        {
            Poll();
        }

        // The lobby keeps time (the countdown, the vote) and goes out to everyone, between rounds and during them.
        Lobby?.Update();
        _announcer?.Poll();
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
