using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pb.Game.Core;
using Pb.Game.Net;
using Pb.Game.Platform;
using Pb.Net.Discovery;
using Pb.Net.Platform;
using Pb.Sim.Data;

namespace Pb.Game.Ui;

/// <summary>
/// The main menu's Play with others: your name and your kit (the gear locker), then
/// <list type="bullet">
/// <item><b>Host a game</b>, with a password if you want one: others on your network see it in their list, and over the
/// internet they join by address once UDP port 47820 is forwarded to you;</item>
/// <item><b>Join a game</b>: one on your network as it answers, an address typed in, or one you joined before.</item>
/// </list>
/// Both go to the lobby. Who you are and the games on your network come from the platform (<see cref="Platforms"/>).
/// The pretend lag (settings) makes your connection feel slower, to try it.
/// </summary>
public partial class PlayWithOthers : VBoxContainer
{
    private GameData _data = null!;
    private PresentationDef _view = null!;
    private GameSettings _settings = null!;
    private LineEdit _name = null!;
    private LineEdit _address = null!;
    private LineEdit _hostPassword = null!;
    private LineEdit _joinPassword = null!;
    private VBoxContainer _found = null!;
    private Label _character = null!;
    private Label _problem = null!;
    private string _shown = "?";

    /// <summary>Back to the title.</summary>
    public Action? Back { get; set; }

    /// <summary>Opens the gear locker, where you pick your character and kit.</summary>
    public Action? OpenLocker { get; set; }

    public void Build(GameData data, PresentationDef view, GameSettings settings)
    {
        _data = data;
        _view = view;
        _settings = settings;
        AddThemeConstantOverride("separation", 16);
        HBoxContainer header = UiKit.Row(12);
        Label title = UiKit.Title("Play with others", 40);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        Button back = UiKit.Button("Back", () => Back?.Invoke(), 200);
        back.Name = "Back";
        header.AddChild(back);
        AddChild(header);
        Label lead = UiKit.Body("Up to ten in a round: together against the bots, side against side, or everyone for themselves. Bots fill " +
                                "the places nobody takes. Rounds with others don't go in your records.", 18, UiKit.Dim, wrap: true);
        lead.CustomMinimumSize = new Vector2(1180, 0);
        AddChild(lead);

        // You: your name, and your character and kit from the locker.
        HBoxContainer you = UiKit.Row(16);
        Label nameLabel = UiKit.Body("Your name");
        nameLabel.CustomMinimumSize = new Vector2(150, 0);
        you.AddChild(nameLabel);
        IIdentity identity = Platforms.Current.Identity;
        _name = new LineEdit
        {
            Name = "Name", Text = identity.ChoosesName ? settings.PlayerName : identity.Name, PlaceholderText = "Type your name",
            MaxLength = GameSettings.NameLength, CustomMinimumSize = new Vector2(320, 44), Editable = identity.ChoosesName,
        };
        _name.TextChanged += _ => Remember();
        you.AddChild(_name);
        you.AddChild(new Control { CustomMinimumSize = new Vector2(24, 0) });
        Button locker = UiKit.Button("Gear locker", () => OpenLocker?.Invoke(), 240);
        locker.Name = "Locker";
        you.AddChild(locker);
        _character = UiKit.Body("", 18, UiKit.Dim);
        _character.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        you.AddChild(_character);
        ShowCharacter();
        AddChild(you);

        HBoxContainer columns = UiKit.Row(20);
        AddChild(columns);

        // Hosting.
        VBoxContainer host = UiKit.Column(12);
        host.AddChild(UiKit.Title("Host a game", 28));
        Label hostAbout = UiKit.Body("You pick where to play in the lobby. Others on your network see your game in their list; over the internet, " +
                                     $"forward UDP port {NetStart.Settings().Port} on your router to this computer and give them your address.", 17, UiKit.Dim, wrap: true);
        hostAbout.CustomMinimumSize = new Vector2(440, 0);
        host.AddChild(hostAbout);
        _hostPassword = Field("HostPassword", "Only if you want one");
        host.AddChild(Labelled("Password", _hostPassword));
        Button hostButton = UiKit.Button("Host a game", Host, 300);
        hostButton.Name = "Host";
        host.AddChild(hostButton);
        columns.AddChild(UiKit.Panel(host, 500f));

        // Joining.
        VBoxContainer join = UiKit.Column(12);
        join.AddChild(UiKit.Title("Join a game", 28));
        join.AddChild(UiKit.Body("ON YOUR NETWORK", 16, UiKit.Dim));
        _found = UiKit.Column(8);
        join.AddChild(_found);
        join.AddChild(UiKit.Body("OR BY ADDRESS", 16, UiKit.Dim));
        HBoxContainer typed = UiKit.Row(10);
        _address = Field("Address", "192.168.1.20, or name:port");
        _address.CustomMinimumSize = new Vector2(380, 44);
        _address.TextSubmitted += a => Join(a);
        typed.AddChild(_address);
        Button joinButton = UiKit.Button("Join", () => Join(_address.Text), 160);
        joinButton.Name = "Join";
        typed.AddChild(joinButton);
        join.AddChild(typed);
        if (settings.RecentAddresses.Count > 0)
        {
            var recent = new HFlowContainer();
            recent.AddThemeConstantOverride("h_separation", 8);
            recent.AddThemeConstantOverride("v_separation", 8);
            foreach (string address in settings.RecentAddresses)
            {
                string a = address;
                Button again = UiKit.Button(a, () => Join(a), 160);
                again.Name = "Recent";
                recent.AddChild(again);
            }

            join.AddChild(UiKit.Body("JOINED BEFORE", 16, UiKit.Dim));
            join.AddChild(recent);
        }

        _joinPassword = Field("JoinPassword", "Only if the game has one");
        join.AddChild(Labelled("Password", _joinPassword));
        columns.AddChild(UiKit.Panel(join, 660f));

        _problem = UiKit.Body("", 18, UiKit.Bad, wrap: true);
        _problem.CustomMinimumSize = new Vector2(1180, 0);
        AddChild(_problem);
        string lag = settings.PretendLag_ms > 0f ? $"Pretend lag is on: {settings.PretendLag_ms:0} ms (Settings → Gameplay)." : "";
        if (lag.Length > 0)
        {
            AddChild(UiKit.Body(lag, 17, UiKit.Accent));
        }
    }

    public override void _Process(double delta)
    {
        // Only searching while the screen is up (the platform searches as it's polled).
        ISessionBrowser sessions = Platforms.Current.Sessions;
        sessions.Searching = IsVisibleInTree();
        if (sessions.Searching)
        {
            ShowFound(sessions.Found);
        }
    }

    public override void _ExitTree() => Platforms.Current.Sessions.Searching = false;

    private void ShowFound(IReadOnlyList<FoundGame> games)
    {
        string shown = string.Join("\n", games.Select(g => $"{g.Address} {g.Game}"));
        if (shown == _shown)
        {
            return;
        }

        _shown = shown;
        foreach (Node child in _found.GetChildren())
        {
            _found.RemoveChild(child);
            child.QueueFree();
        }

        if (games.Count == 0)
        {
            _found.AddChild(UiKit.Body(Platforms.Current.Sessions.CanSearch ? "Looking for games…" : "Can't search this network (no network?).", 18,
                UiKit.Dim));
            return;
        }

        foreach ((string address, GameAnnouncement game) in games)
        {
            bool same = game.Build == BuildStamp.Build;
            var parts = new List<string> { game.Name, $"{game.People} of {game.MaxPeople}" };
            if (game.InRound)
            {
                parts.Add("a round is on");
            }

            if (game.Password)
            {
                parts.Add("password");
            }

            string line = $"{string.Join(" · ", parts)}\n{game.Where} · {game.How}" + (same ? "" : "\nAnother version: both of you update with Play.bat");
            string a = address;
            Button button = UiKit.Button(line, () => Join(a), 640, enabled: same && game.People < game.MaxPeople);
            button.Name = "Game";
            button.Alignment = HorizontalAlignment.Left;
            button.CustomMinimumSize = new Vector2(640, 72);
            _found.AddChild(button);
        }
    }

    /// <summary>Which character you'll play, as the locker last saved it.</summary>
    public void ShowCharacter()
    {
        int count = Math.Max(1, _view.Characters.Models.Length);
        _character.Text = $"Character {_settings.PlayerLook % count + 1}, in your kit";
    }

    private static LineEdit Field(string name, string placeholder) =>
        new() { Name = name, PlaceholderText = placeholder, CustomMinimumSize = new Vector2(300, 44), MaxLength = 64 };

    private static HBoxContainer Labelled(string label, Control field)
    {
        HBoxContainer row = UiKit.Row(12);
        Label l = UiKit.Body(label, 18);
        l.CustomMinimumSize = new Vector2(110, 0);
        row.AddChild(l);
        row.AddChild(field);
        return row;
    }

    /// <summary>Your name, saved as you change it.</summary>
    private void Remember()
    {
        if (Platforms.Current.Identity.ChoosesName)
        {
            _settings.PlayerName = GameSettings.CleanName(_name.Text);
        }

        _settings.Save();
    }

    private string PlayName() =>
        !Platforms.Current.Identity.ChoosesName ? Platforms.Current.Identity.Name : GameSettings.CleanName(_name.Text) is { Length: > 0 } name ? name : "Player";

    private void Host()
    {
        Remember();
        try
        {
            NetStart.Host(GetTree(), _data, PlayName(), (byte)_settings.PlayerLook, password: _hostPassword.Text.Trim(), pretendLag_ms: _settings.PretendLag_ms);
        }
        catch (InvalidOperationException ex)
        {
            _problem.Text = ex.Message;
            return;
        }

        GetTree().ChangeSceneToFile(GameSession.LobbyScene);
    }

    private void Join(string address)
    {
        string a = address.Trim();
        if (a.Length == 0)
        {
            _problem.Text = "Type the host's address first (theirs on your network, or on the internet).";
            _address.GrabFocus();
            return;
        }

        Remember();
        _settings.RememberAddress(a);
        _settings.Save();
        try
        {
            NetStart.Join(GetTree(), a, PlayName(), (byte)_settings.PlayerLook, _joinPassword.Text.Trim(), _settings.PretendLag_ms,
                Platforms.Current.Identity.Id);
        }
        catch (InvalidOperationException ex)
        {
            _problem.Text = ex.Message;
            return;
        }

        GetTree().ChangeSceneToFile(GameSession.LobbyScene);
    }
}
