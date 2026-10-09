using System;
using System.Collections.Generic;
using Godot;
using Pb.Net.Lobby;

namespace Pb.Game.Ui;

/// <summary>
/// Text chat: the last few lines (each name in its side's colour) over a line to type in. Enter sends it, to everyone or,
/// with "Team only" on, your side; Esc leaves the line. In a round (<see cref="Fades"/>) the lines show for a while after
/// they come and then fade, and the line to type in only shows while you type.
/// </summary>
public partial class ChatBox : VBoxContainer
{
    private readonly List<(string Text, double At)> _lines = new();
    private RichTextLabel _shown = null!;
    private LineEdit _entry = null!;
    private Button? _team;
    private int _most;
    private double _now;

    /// <summary>What's typed is sent: the text, and whether only to your side.</summary>
    public Action<string, bool>? Send { get; set; }

    /// <summary>The colour each side's names are in (−1: no side).</summary>
    public Func<int, Color>? SideColor { get; set; }

    /// <summary>In a round: lines fade after a while, and the line to type in shows only while typing.</summary>
    public bool Fades { get; set; }

    public float ShowFor_s { get; set; } = 10f;

    public bool Typing => _entry.HasFocus();

    /// <param name="teams">Offer "Team only" (in teams).</param>
    /// <param name="keepRoom">Keep room for all <paramref name="lines"/> from the start, so what's around doesn't move as lines come (the lobby).</param>
    public void Build(float width, int lines, bool teams, int fontSize = 18, bool keepRoom = false)
    {
        _most = lines;
        AddThemeConstantOverride("separation", 6);
        CustomMinimumSize = new Vector2(width, 0);
        _shown = new RichTextLabel
        {
            Name = "Lines", BbcodeEnabled = true, ScrollFollowing = true, FitContent = true, MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(width, keepRoom ? lines * fontSize * 1.45f : 0f), AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _shown.AddThemeFontSizeOverride("normal_font_size", fontSize);
        AddChild(_shown);
        HBoxContainer row = UiKit.Row(10);
        _entry = new LineEdit
        {
            Name = "Say", PlaceholderText = "Say something (Enter)", SizeFlagsHorizontal = SizeFlags.ExpandFill, MaxLength = 200,
            CustomMinimumSize = new Vector2(0, 40), CaretBlink = true,
        };
        _entry.AddThemeFontSizeOverride("font_size", fontSize);
        _entry.TextSubmitted += Submit;
        _entry.GuiInput += e =>
        {
            if (e.IsActionPressed("ui_cancel") && Typing)
            {
                Close();
                _entry.AcceptEvent();
            }
        };
        row.AddChild(_entry);
        if (teams)
        {
            _team = new Button { Name = "TeamOnly", Text = "Team only", ToggleMode = true, CustomMinimumSize = new Vector2(150, 40), FocusMode = FocusModeEnum.None };
            row.AddChild(_team);
        }

        AddChild(row);
        Redraw();
    }

    /// <summary>A line someone said.</summary>
    public void Add(ChatLine line)
    {
        Color colour = SideColor?.Invoke(line.Side) ?? UiKit.Accent;
        string team = line.TeamOnly ? "[color=#a0a0a0](team)[/color] " : "";
        _lines.Add(($"{team}[color=#{colour.ToHtml(false)}]{Escape(line.Name)}[/color]: {Escape(line.Text)}", _now));
        if (_lines.Count > _most)
        {
            _lines.RemoveAt(0);
        }

        Redraw();
    }

    /// <summary>A line from the game itself (someone joined or left).</summary>
    public void Note(string text)
    {
        _lines.Add(($"[color=#a0a0a0]{Escape(text)}[/color]", _now));
        if (_lines.Count > _most)
        {
            _lines.RemoveAt(0);
        }

        Redraw();
    }

    /// <summary>Done typing (sent, or left with Esc).</summary>
    public Action? Closed { get; set; }

    /// <summary>Starts typing (the chat keys, in a round), to everyone or only your side.</summary>
    public void Open(bool teamOnly = false)
    {
        if (_team is not null)
        {
            _team.ButtonPressed = teamOnly;
        }

        _entry.Visible = true;
        _entry.PlaceholderText = teamOnly ? "To your side (Enter; Esc to leave it)" : "To everyone (Enter; Esc to leave it)";
        _entry.GrabFocus();
        Redraw();
    }

    public void Close()
    {
        _entry.ReleaseFocus();
        _entry.Text = "";
        if (Fades)
        {
            _entry.Visible = false;
        }

        Redraw();
        Closed?.Invoke();
    }

    public override void _Ready()
    {
        if (Fades)
        {
            _entry.Visible = false;
        }
    }

    public override void _Process(double delta)
    {
        _now += delta;
        if (Fades)
        {
            Redraw();
        }
    }

    private void Submit(string text)
    {
        if (text.Trim().Length > 0)
        {
            Send?.Invoke(text, _team?.ButtonPressed ?? false);
        }

        _entry.Text = "";
        if (Fades)
        {
            Close();
        }
    }

    private void Redraw()
    {
        var text = new System.Text.StringBuilder();
        foreach ((string line, double at) in _lines)
        {
            if (Fades && !Typing && _now - at > ShowFor_s)
            {
                continue;
            }

            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append(line);
        }

        string shown = text.ToString();
        if (_shown.Text != shown)
        {
            _shown.Text = shown;
        }
    }

    /// <summary>What someone typed, shown as typed (no markup of their own).</summary>
    private static string Escape(string text) => text.Replace("[", "[lb]");
}
