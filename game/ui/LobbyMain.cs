using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pb.Game.Core;
using Pb.Game.Net;
using Pb.Net.Client;
using Pb.Net.Lobby;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Pb.Sim.Match;

namespace Pb.Game.Ui;

/// <summary>
/// Playing with others, between rounds:
/// <list type="bullet">
/// <item>who's in and on which side, with the bots that fill the places left;</item>
/// <item>the round the host has chosen, which the host picks here: the area, place, mode, size, objective and difficulty,
/// whether to balance the sides, and whether to vote on where to play between rounds;</item>
/// <item>your character, turning in your side's colour;</item>
/// <item>ready-up and the countdown, the vote when it's on, and text chat.</item>
/// </list>
/// Everyone ready starts the countdown. The host can also start without waiting, and can remove someone. Leave ends
/// the game when you're hosting, and otherwise just leaves it. When the countdown runs out, the round starts for
/// everyone. <c>--host-wait=N</c> starts the first countdown by itself once N people are in, you included, and the next
/// ones straight away (CI); <c>--ready</c>
/// readies you up once you're in, and <c>--say=TEXT</c> says something in the chat (CI and screenshots).
/// </summary>
public partial class LobbyMain : Control
{
    private bool _autoStarted;
    private NetSession? _session;
    private GameData _data = null!;
    private PresentationDef _view = null!;
    private Label _title = null!;
    private Label _status = null!;
    private VBoxContainer _sides = null!;
    private VBoxContainer _choices = null!;
    private CharacterPreview _preview = null!;
    private HBoxContainer _looks = null!;
    private ChatBox _chat = null!;
    private Button _ready = null!;
    private Button? _start;
    private int _seen = -1;
    private LobbyChoices? _built;
    private LobbyPhase _builtPhase = (LobbyPhase)255;
    private string[] _names = Array.Empty<string>();
    private bool _leaving;
    private bool _scripted;

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

        var source = new GodotDataSource();
        _data = GameData.Load(source);
        _view = Jsonc.Load<PresentationDef>(source, PresentationDef.File);
        GameSettings settings = GameSettings.Load(_view);
        _view.UseTeamColors(settings.TeamColors);

        var background = new ColorRect { Name = "Background", Color = new Color(0.07f, 0.075f, 0.08f), MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);
        var margin = new MarginContainer { Name = "Margin" };
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" })
        {
            margin.AddThemeConstantOverride("margin_" + side, 36);
        }

        AddChild(margin);
        VBoxContainer page = UiKit.Column(12);
        margin.AddChild(page);

        // The game's name and what's happening, with Leave up in the corner.
        HBoxContainer header = UiKit.Row(16);
        VBoxContainer heading = UiKit.Column(4);
        heading.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        heading.AddChild(UiKit.Body("PLAYING WITH OTHERS", 18, UiKit.Accent));
        _title = UiKit.Title("", 40);
        heading.AddChild(_title);
        _status = UiKit.Body("", 20, UiKit.Dim, wrap: true);
        _status.Name = "Status";
        heading.AddChild(_status);
        header.AddChild(heading);
        Button leave = UiKit.Button(_session.Hosting ? "End the game" : "Leave", Leave, 220);
        leave.Name = "Leave";
        leave.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        header.AddChild(leave);
        page.AddChild(header);

        // Who's in, the round, and you.
        // Three columns that fit a 1600-pixel-wide screen with the panels' own margins.
        HBoxContainer columns = UiKit.Row(16);
        columns.SizeFlagsVertical = SizeFlags.ExpandFill;
        _sides = UiKit.Column(10);
        columns.AddChild(Card("WHO'S IN", _sides, 440f));
        _choices = UiKit.Column(10);
        columns.AddChild(Card("THE ROUND", _choices, 520f));
        VBoxContainer you = UiKit.Column(12);
        _preview = new CharacterPreview { Name = "Character" };
        _preview.Build(_data.Config, _view, new Vector2(220, 220));
        you.AddChild(_preview);
        _looks = UiKit.Row(8);
        you.AddChild(_looks);
        _ready = UiKit.Button("Ready", () => _session.Ask(LobbyAsk.Ready, _session.Me is { Ready: true } ? 0 : 1), 290);
        _ready.Name = "Ready";
        you.AddChild(_ready);
        if (_session.Hosting)
        {
            _start = UiKit.Button("Start now", () => _session.Lobby!.Start(), 290);
            _start.Name = "Start";
            you.AddChild(_start);
        }

        columns.AddChild(Card("YOU", you, 300f));
        page.AddChild(columns);

        _chat = new ChatBox { Name = "Chat" };
        _chat.Build(1380f, 3, teams: true, keepRoom: true);
        _chat.SideColor = SideColor;
        _chat.Send = (text, team) => _session.Say(text, team);
        page.AddChild(UiKit.Panel(_chat));

        if (_session.Lobby is { } lobby)
        {
            lobby.CountdownFinished += BuildRound;
        }

        _session.Ended += OnEnded;
        _ready.CallDeferred(Control.MethodName.GrabFocus);
        Refresh();
    }

    public override void _ExitTree()
    {
        if (_session is null)
        {
            return;
        }

        _session.Ended -= OnEnded;
        if (_session.Lobby is { } lobby)
        {
            lobby.CountdownFinished -= BuildRound;
        }
    }

    public override void _Process(double delta)
    {
        if (_session is null || _leaving)
        {
            return;
        }

        foreach (ChatLine line in _session.TakeChat())
        {
            _chat.Add(line);
        }

        if (_session.LobbyVersion != _seen)
        {
            Refresh();
        }

        _status.Text = StatusLine();
        if (!_scripted && _session.Me is { } me)
        {
            // CI and screenshots: ready up and say something, once in.
            _scripted = true;
            if (Args.Has("--ready") && !me.Ready)
            {
                _session.Ask(LobbyAsk.Ready, 1);
            }

            if (Args.Value("--say") is { Length: > 0 } said)
            {
                _session.Say(said, teamOnly: false);
            }
        }

        if (_session.Client is not null && _session.RoundWaiting && _session.Round is { YourPlayerId: >= 0 })
        {
            // The host has started a round you're in.
            Go(GameSession.LevelScene);
            return;
        }

        // --host-wait=N (CI): the first countdown once N people are in; after a round, straight away with whoever's still in.
        if (_session.Lobby is { Phase: LobbyPhase.Lobby } lobby && !_autoStarted && Args.Ticks("--host-wait", 0) is > 0 and int wanted &&
            (lobby.State.Members.Count >= wanted || lobby.State.RoundsPlayed > 0))
        {
            _autoStarted = true;
            GD.Print($"NET {lobby.State.Members.Count} in the game: counting down");
            lobby.Start();
        }
    }

    /// <summary>
    /// A titled panel holding one of the page's columns. What doesn't fit scrolls inside it (ten people in the game, say), so
    /// the chat below always stays on the screen.
    /// </summary>
    private static Control Card(string title, Control content, float width)
    {
        VBoxContainer column = UiKit.Column(12);
        column.AddChild(UiKit.Body(title, 18, UiKit.Dim));
        var scroll = new ScrollContainer
        {
            Name = "Scroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(content);
        column.AddChild(scroll);
        PanelContainer panel = UiKit.Panel(column, width);
        panel.SizeFlagsVertical = SizeFlags.ExpandFill;
        return panel;
    }

    private Color SideColor(int side) => side < 0 ? UiKit.Accent : Color.FromHtml(_view.TeamColors[side % _view.TeamColors.Length]);

    /// <summary>The lobby again, as it now stands: who's in, the round (or the vote), you.</summary>
    private void Refresh()
    {
        NetSession session = _session!;
        _seen = session.LobbyVersion;
        if (session.LobbyView is not { } lobby)
        {
            _title.Text = "Joining…";
            return;
        }

        _title.Text = lobby.ServerName;
        NoteComings(lobby);
        ShowSides(lobby);
        if (lobby.Phase == LobbyPhase.Vote)
        {
            ShowVote(lobby);
        }
        else if (_built != lobby.Choices || _builtPhase != lobby.Phase)
        {
            ShowChoices(lobby);
        }

        LobbyMember? me = session.Me;
        _preview.Show(me?.Look ?? session.LocalLook, SideColor(me?.Side ?? 0));
        ShowLooks(me?.Look ?? session.LocalLook);
        bool between = lobby.Phase is LobbyPhase.Lobby or LobbyPhase.Countdown;
        _ready.Disabled = me is null || !between;
        _ready.Text = me is { Ready: true } ? "Ready ✓  (press to wait)" : "Ready";
        if (_start is not null)
        {
            _start.Disabled = lobby.Phase != LobbyPhase.Lobby;
        }
    }

    /// <summary>"Bo joined", "Bo left" in the chat.</summary>
    private void NoteComings(LobbyState lobby)
    {
        string[] names = lobby.Members.Select(m => m.Name).ToArray();
        if (_names.Length > 0)
        {
            foreach (string name in names.Except(_names))
            {
                _chat.Note($"{name} joined");
            }

            foreach (string name in _names.Except(names))
            {
                _chat.Note($"{name} left");
            }
        }

        _names = names;
    }

    /// <summary>Who's in, on each side in teams, with the places the bots fill, and (for you) a side to move to.</summary>
    private void ShowSides(LobbyState lobby)
    {
        Clear(_sides);
        ChosenRound round = RoundChoices.Resolve(_data, lobby.Choices);
        int people = lobby.Members.Count;
        switch (round.Mode.Kind)
        {
            case MatchModeKind.Teams:
                int largest = Math.Max(lobby.Members.Count(m => m.Side == 0), lobby.Members.Count(m => m.Side == 1));
                foreach (int side in new[] { 0, 1 })
                {
                    LobbyMember[] on = lobby.Members.Where(m => m.Side == side).ToArray();
                    int size = Math.Max(round.Size, largest);
                    _sides.AddChild(UiKit.Body($"Side {(side == 0 ? "A" : "B")}  ({on.Length} of {size})", 22, SideColor(side)));
                    foreach (LobbyMember m in on)
                    {
                        _sides.AddChild(MemberRow(m));
                    }

                    if (size - on.Length > 0)
                    {
                        _sides.AddChild(UiKit.Body($"   + {size - on.Length} bot{(size - on.Length == 1 ? "" : "s")}", 18, UiKit.Dim));
                    }

                    if (_session!.Me is { } me && me.Side != side && lobby.Phase is LobbyPhase.Lobby or LobbyPhase.Countdown)
                    {
                        int to = side;
                        Button move = UiKit.Button($"Play on side {(side == 0 ? "A" : "B")}", () => _session.Ask(LobbyAsk.Side, to), 300);
                        move.Name = $"Side_{side}";
                        _sides.AddChild(move);
                    }

                    _sides.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
                }

                if (lobby.Choices.Balance)
                {
                    _sides.AddChild(UiKit.Body("The sides are kept even: a move that would put one two ahead isn't allowed.", 16, UiKit.Dim, wrap: true));
                }

                break;
            case MatchModeKind.FreeForAll:
            {
                int size = Math.Max(round.Size, people);
                _sides.AddChild(UiKit.Body($"Everyone for themselves ({people} of {size})", 22, UiKit.Accent));
                foreach (LobbyMember m in lobby.Members)
                {
                    _sides.AddChild(MemberRow(m));
                }

                if (size - people > 0)
                {
                    _sides.AddChild(UiKit.Body($"   + {size - people} bot{(size - people == 1 ? "" : "s")}", 18, UiKit.Dim));
                }

                break;
            }

            default:
            {
                int against = RoundCastingSize(round, people);
                _sides.AddChild(UiKit.Body($"Together ({people})", 22, SideColor(0)));
                foreach (LobbyMember m in lobby.Members)
                {
                    _sides.AddChild(MemberRow(m));
                }

                _sides.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
                _sides.AddChild(UiKit.Body($"Against the squad: {against} bot{(against == 1 ? "" : "s")}", 22, SideColor(1)));
                break;
            }
        }

        _sides.AddChild(UiKit.Body($"{people} of the {lobby.MaxPeople} people a game takes.", 16, UiKit.Dim));
    }

    private int RoundCastingSize(ChosenRound round, int people) =>
        Pb.Net.RoundCasting.FitSize(round.Mode, round.Size, people, _data.Config.Rules.MaxPlayers);

    /// <summary>"Bo · ready · 45 ms  [Remove]".</summary>
    private Control MemberRow(LobbyMember m)
    {
        HBoxContainer row = UiKit.Row(10);
        bool you = m.Id == _session!.MemberId;
        var parts = new List<string> { m.Name + (you ? " (you)" : "") };
        if (m.Host)
        {
            parts.Add("host");
        }

        parts.Add(m.Ready ? "ready" : "not ready");
        if (!m.Host)
        {
            parts.Add($"{m.Ping_ms} ms");
        }

        if (m.RoundsWon > 0 || m.Eliminations > 0)
        {
            parts.Add($"{m.RoundsWon} won, {m.Eliminations} out");
        }

        Label label = UiKit.Body("●  " + string.Join(" · ", parts), 19, m.Ready ? UiKit.Text : UiKit.Dim);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(label);
        if (_session.Lobby is { } lobby && !m.Host)
        {
            int id = m.Id;
            Button remove = UiKit.Button("Remove", () => lobby.Remove(id), 120);
            remove.Name = $"Remove_{m.Id}";
            row.AddChild(remove);
        }

        return row;
    }

    /// <summary>The round chosen: the host picks it here; everyone else reads it.</summary>
    private void ShowChoices(LobbyState lobby)
    {
        Clear(_choices);
        _built = lobby.Choices;
        _builtPhase = lobby.Phase;
        LobbyChoices c = lobby.Choices;
        ChosenRound round = RoundChoices.Resolve(_data, c);
        if (_session!.Lobby is not { } host || lobby.Phase is not (LobbyPhase.Lobby or LobbyPhase.Countdown))
        {
            _choices.AddChild(UiKit.Title(round.Where, 30));
            _choices.AddChild(UiKit.Body(round.How, 22, UiKit.Accent));
            Label about = UiKit.Body(round.Place.Description, 18, UiKit.Dim, wrap: true);
            about.CustomMinimumSize = new Vector2(540, 0);
            _choices.AddChild(about);
            _choices.AddChild(UiKit.Body(round.Mode.Description, 18, UiKit.Dim, wrap: true));
            var flags = new List<string>();
            if (round.Mode.Kind == MatchModeKind.Teams)
            {
                flags.Add(c.Balance ? "sides kept even" : "sides as people choose");
            }

            flags.Add(c.Vote ? "a vote on where to play between rounds" : "the host picks where to play");
            _choices.AddChild(UiKit.Body(string.Join(" · ", flags), 18, UiKit.Dim, wrap: true));
            return;
        }

        void Set(LobbyChoices next) => host.SetChoices(RoundChoices.Fit(_data, next));
        AreaEntryDef[] areas = _data.Areas.Areas.Where(a => _data.Levels.ContainsKey(a.Id)).ToArray();
        _choices.AddChild(UiKit.OptionRow("Area", areas.Select(a => a.DisplayName).ToArray(), Math.Max(0, Array.IndexOf(areas, round.Entry)),
            k => Set(c with { LevelId = areas[k].Id, PlaceId = null })));
        PlaceSpec[] places = round.Area.Places.ToArray();
        _choices.AddChild(UiKit.OptionRow("Where in it", places.Select(p => p.DisplayName).ToArray(), Math.Max(0, Array.IndexOf(places, round.Place)),
            k => Set(c with { PlaceId = places[k].Whole ? null : places[k].Id })));
        IReadOnlyList<GameMode> modes = _data.Config.Rules.Modes;
        _choices.AddChild(UiKit.OptionRow("Mode", modes.Select(m => m.DisplayName).ToArray(), Math.Max(0, modes.ToList().IndexOf(round.Mode)),
            k => Set(c with { ModeId = modes[k].Id, Size = modes[k].DefaultSize })));
        int[] sizes = round.Mode.Sizes.ToArray();
        _choices.AddChild(UiKit.OptionRow(round.Mode.Kind == MatchModeKind.Solo ? "Opponents" : "Players",
            sizes.Select(n => ModeText.Size(round.Mode, n)).ToArray(), Math.Max(0, Array.IndexOf(sizes, round.Size)), k => Set(c with { Size = sizes[k] })));
        if (round.Mode.Kind != MatchModeKind.FreeForAll)
        {
            ObjectiveChoice[] offered = RoundChoices.Offered(_data, round.Area, round.Place);
            if (offered.Length > 1)
            {
                _choices.AddChild(UiKit.OptionRow("Objective", offered.Select(o => o.DisplayName).ToArray(),
                    Math.Max(0, Array.IndexOf(offered, round.Objective)), k => Set(c with { ObjectiveId = RecordBook.IdOf(offered[k].Kind) })));
            }
        }

        TierDef[] tiers = round.Entry.Tiers;
        _choices.AddChild(UiKit.OptionRow("Difficulty", tiers.Select(t => t.DisplayName).ToArray(), Math.Max(0, Array.IndexOf(tiers, round.Tier)),
            k => Set(c with { TierId = tiers[k].Id })));
        if (round.Mode.Kind == MatchModeKind.Teams)
        {
            _choices.AddChild(UiKit.CheckRow("Keep the sides even", c.Balance, on => Set(c with { Balance = on })));
        }

        _choices.AddChild(UiKit.CheckRow("Vote between rounds", c.Vote, on => Set(c with { Vote = on })));
        Label how = UiKit.Body($"{round.Where} · {round.How}", 18, UiKit.Accent, wrap: true);
        how.CustomMinimumSize = new Vector2(540, 0);
        _choices.AddChild(how);
    }

    /// <summary>The vote: each place on offer with its votes so far; yours lit.</summary>
    private void ShowVote(LobbyState lobby)
    {
        Clear(_choices);
        _built = null;
        _builtPhase = LobbyPhase.Vote;
        _choices.AddChild(UiKit.Title("Where next?", 30));
        int mine = _session!.Me?.Vote ?? -1;
        for (int i = 0; i < lobby.VoteOptions.Count; i++)
        {
            int index = i;
            int votes = lobby.Members.Count(m => m.Vote == i);
            Button option = UiKit.Button($"{lobby.VoteOptions[i].Name}   ({votes} vote{(votes == 1 ? "" : "s")})",
                () => _session.Ask(LobbyAsk.Vote, index), 540);
            option.Name = $"Vote_{i}";
            option.ToggleMode = true;
            option.ButtonPressed = i == mine;
            _choices.AddChild(option);
        }

        _choices.AddChild(UiKit.Body("The place with the most votes is played next (the first of them on a tie).", 16, UiKit.Dim, wrap: true));
    }

    /// <summary>The characters to choose from, yours lit.</summary>
    private void ShowLooks(int look)
    {
        int count = Math.Max(1, _view.Characters.Models.Length);
        if (_looks.GetChildCount() == count + 1)
        {
            for (int i = 0; i < count; i++)
            {
                ((Button)_looks.GetChild(i + 1)).ButtonPressed = i == look % count;
            }

            return;
        }

        Clear(_looks);
        _looks.AddChild(UiKit.Body("Character", 20));
        for (int i = 0; i < count; i++)
        {
            int index = i;
            var button = new Button
            {
                Name = $"Look_{i}", Text = $"{i + 1}", ToggleMode = true, ButtonPressed = i == look % count,
                CustomMinimumSize = new Vector2(56, 40), FocusMode = FocusModeEnum.All,
            };
            button.Pressed += () => _session!.Ask(LobbyAsk.Look, index);
            _looks.AddChild(button);
        }
    }

    /// <summary>What's happening, for the line under the game's name.</summary>
    private string StatusLine()
    {
        NetSession session = _session!;
        if (session.Client is { State: ClientState.Joining })
        {
            return "Asking the host to let you in…";
        }

        if (session.LobbyView is not { } lobby)
        {
            return "Waiting for the host…";
        }

        int left = (int)Math.Ceiling(session.TimeLeft);
        return lobby.Phase switch
        {
            LobbyPhase.Countdown => $"Starting in {left}…" + (lobby.Forced ? "" : "  (anyone unreadying stops it)"),
            LobbyPhase.Vote => $"Vote for where to play next: {left} s left.",
            LobbyPhase.Loading => "Building the round…",
            LobbyPhase.Round => "A round is under way: you'll be in the next one.",
            LobbyPhase.Summary => "The round's over: back here in a moment.",
            _ when session.Hosting => $"Others join at this computer's address{Addresses()}, port {session.Settings.Port}. " +
                                      "Everyone ready starts the countdown; Start now doesn't wait.",
            _ => "Ready up when you are: the round starts once everyone is.",
        };
    }

    private static string Addresses()
    {
        string[] addresses = IP.GetLocalAddresses().Where(a => a.Contains('.') && !a.StartsWith("127.", StringComparison.Ordinal)).ToArray();
        return addresses.Length > 0 ? " (" + string.Join(" or ", addresses) + ")" : "";
    }

    /// <summary>Hosting: the countdown ran out; the round is built (the lobby says it's loading).</summary>
    private void BuildRound()
    {
        ChosenRound round = RoundChoices.Resolve(_data, _session!.LobbyView!.Choices);
        GD.Print($"NET building the round: {round.Where} · {round.How}");
        Go(GameSession.LevelScene);
    }

    private void Go(string scene)
    {
        if (_leaving)
        {
            return;
        }

        _leaving = true;
        GetTree().ChangeSceneToFile(scene);
    }

    private void Leave()
    {
        _leaving = true;
        _session?.Leave(_session.Hosting ? "The host ended the game." : "left");
        GetTree().ChangeSceneToFile(GameSession.MainScene);
    }

    /// <summary>The game ended under you (the host left, or removed you): back to the menu, which says why.</summary>
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

    private static void Clear(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}
