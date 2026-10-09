using System.Collections.Generic;
using System.Linq;
using Godot;
using Pb.Game.Core;
using Pb.Game.Net;
using Pb.Net.Client;
using Pb.Net.Server;

namespace Pb.Game.Ui;

/// <summary>
/// Playing with others, before the first round: who's in the game and, hosting, Start round, which builds the round the
/// menus chose (or the command line's) with everyone in it. Joined, the round starts when the host sends it; someone who
/// joins while a round is under way waits here for the next. Leave ends the game (hosting, for everyone).
/// <c>--host-wait=N</c> starts the round by itself once N people are in, you included (CI).
/// </summary>
public partial class LobbyMain : Control
{
    private static bool _autoStarted;
    private NetSession? _session;
    private Label _title = null!;
    private Label _status = null!;
    private VBoxContainer _people = null!;
    private string _shown = "";
    private bool _leaving;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        Theme = UiKit.Theme;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _session = NetSession.Current;
        if (_session is null)
        {
            Callable.From(() => GetTree().ChangeSceneToFile(GameSession.MainScene)).CallDeferred();
            return;
        }

        var background = new ColorRect { Name = "Background", Color = new Color(0.07f, 0.075f, 0.08f), MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        VBoxContainer column = UiKit.Column(14);
        column.AddChild(UiKit.Body("PLAYING WITH OTHERS", 18, UiKit.Accent));
        _title = UiKit.Title("", 40);
        column.AddChild(_title);
        _status = UiKit.Body("", 20, UiKit.Dim, wrap: true);
        _status.CustomMinimumSize = new Vector2(580, 0);
        column.AddChild(_status);
        column.AddChild(new HSeparator());
        _people = UiKit.Column(8);
        column.AddChild(_people);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        HBoxContainer buttons = UiKit.Row(12);
        Button? start = null;
        if (_session.Hosting)
        {
            start = UiKit.Button("Start round", StartRound, 260);
            start.Name = "Start";
            buttons.AddChild(start);
        }

        Button leave = UiKit.Button("Leave", Leave, 200);
        leave.Name = "Leave";
        buttons.AddChild(leave);
        column.AddChild(buttons);
        AddChild(UiKit.Overlay(UiKit.Panel(column, 660f), dim: 0f));
        (start ?? leave).CallDeferred(Control.MethodName.GrabFocus);
        _session.Ended += OnEnded;
        Refresh();
    }

    public override void _ExitTree()
    {
        if (_session is not null)
        {
            _session.Ended -= OnEnded;
        }
    }

    public override void _Process(double delta)
    {
        if (_session is null || _leaving)
        {
            return;
        }

        Refresh();
        if (_session.Client is not null && _session.RoundWaiting && _session.Round is { YourPlayerId: >= 0 })
        {
            // The host has started a round you're in.
            _leaving = true;
            GetTree().ChangeSceneToFile(GameSession.LevelScene);
            return;
        }

        if (_session.Hosting && !_autoStarted && Args.Ticks("--host-wait", 0) is > 0 and int wanted && People().Count >= wanted)
        {
            _autoStarted = true;
            GD.Print($"NET {People().Count} in the game: starting the round");
            StartRound();
        }
    }

    /// <summary>Who's in the game, as this copy knows it (hosting, everyone; joined, you and the host until M4.4's lobby).</summary>
    private List<string> People()
    {
        NetSession session = _session!;
        var people = new List<string>();
        if (session.Server is { } server)
        {
            people.Add($"{session.LocalName} (you, hosting)");
            people.AddRange(server.Clients.Where(c => c.Welcomed).Select(c => c.Name));
        }
        else if (session.Client is { Welcome: { } welcome })
        {
            people.Add($"{welcome.Name} (you)");
        }

        return people;
    }

    private void Refresh()
    {
        NetSession session = _session!;
        string title, status;
        if (session.Server is { } server)
        {
            title = server.Identity.Name;
            string[] addresses = IP.GetLocalAddresses().Where(a => a.Contains('.') && !a.StartsWith("127.", System.StringComparison.Ordinal)).ToArray();
            status = $"Others join with this computer's address{(addresses.Length > 0 ? " (" + string.Join(" or ", addresses) + ")" : "")} " +
                     $"and port {session.Settings.Port}. Start the round when everyone's in: bots fill the places left.";
        }
        else
        {
            NetClient client = session.Client!;
            title = client.Welcome?.ServerName ?? "Joining…";
            status = client.State switch
            {
                ClientState.Joining => "Asking the host to let you in…",
                ClientState.Joined when session.Round is { YourPlayerId: < 0 } => "A round is under way: you'll play in the next one.",
                ClientState.Joined => "You're in. The round starts when the host starts it.",
                _ => client.GoneReason,
            };
        }

        List<string> people = People();
        string shown = title + "\n" + status + "\n" + string.Join("\n", people);
        if (shown == _shown)
        {
            return;
        }

        _shown = shown;
        _title.Text = title;
        _status.Text = status;
        foreach (Node child in _people.GetChildren())
        {
            child.QueueFree();
        }

        _people.AddChild(UiKit.Body($"In the game ({people.Count})", 18, UiKit.Accent));
        foreach (string person in people)
        {
            _people.AddChild(UiKit.Body("·  " + person, 20));
        }
    }

    private void StartRound()
    {
        if (_leaving)
        {
            return;
        }

        _leaving = true;
        GetTree().ChangeSceneToFile(GameSession.LevelScene);
    }

    private void Leave()
    {
        _leaving = true;
        _session?.Leave(_session.Hosting ? "The host ended the game." : "left");
        GetTree().ChangeSceneToFile(GameSession.MainScene);
    }

    /// <summary>The game ended under you (the host left, or turned you away): back to the menu, which says why.</summary>
    private void OnEnded(string reason)
    {
        if (_leaving)
        {
            return;
        }

        _leaving = true;
        GD.Print($"NET game over: {reason}");
        _session?.Leave(reason);
        if (DisplayServer.GetName() == "headless")
        {
            GetTree().Quit(1);
            return;
        }

        GameSession.Notice = reason;
        GetTree().ChangeSceneToFile(GameSession.MainScene);
    }
}
