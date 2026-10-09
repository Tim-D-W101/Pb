using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pb.Sim;
using Pb.Sim.Data;
using Pb.Sim.Players;

namespace Pb.Game.Ui;

/// <summary>
/// Held on Tab: everyone in the round, side by side (or, in free-for-all, one list by eliminations): who's still in,
/// their eliminations and hits so far, and for people their ping (bots are marked so).
/// </summary>
public partial class Scoreboard : PanelContainer
{
    private SimWorld _sim = null!;
    private Func<int, Color> _teamColor = null!;
    private Func<PlayerState, int?> _ping = null!;
    private Func<PlayerState, bool> _person = null!;
    private MatchModeKind _mode;
    private int _you;
    private VBoxContainer _rows = null!;
    private Label _title = null!;
    private double _sinceRefresh = 1.0;

    /// <param name="ping">A person's ping (ms), or null for a bot (or nobody known).</param>
    /// <param name="person">Whether a player is a person (else a bot).</param>
    public void Initialize(SimWorld sim, MatchModeKind mode, int you, Func<int, Color> teamColor, Func<PlayerState, int?> ping, Func<PlayerState, bool> person,
        string title)
    {
        _sim = sim;
        _mode = mode;
        _you = you;
        _teamColor = teamColor;
        _ping = ping;
        _person = person;
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(760, 0);
        VBoxContainer column = UiKit.Column(8);
        _title = UiKit.Title(title, 28);
        column.AddChild(_title);
        _rows = UiKit.Column(4);
        column.AddChild(_rows);
        AddChild(column);
        Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            _sinceRefresh = 1.0;
            return;
        }

        _sinceRefresh += delta;
        if (_sinceRefresh >= 0.25)
        {
            _sinceRefresh = 0;
            Refresh();
        }
    }

    private void Refresh()
    {
        foreach (Node child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }

        _rows.AddChild(Row("", "Put out", "Hits", "", "Ping", UiKit.Dim, header: true));
        IEnumerable<IGrouping<int, PlayerState>> sides = _mode == MatchModeKind.FreeForAll
            ? new[] { _sim.Players.GroupBy(_ => 0).First() }
            : _sim.Players.GroupBy(p => (int)p.Team).OrderBy(g => g.Key);
        PlayerState? me = _sim.FindPlayer(_you);
        foreach (IGrouping<int, PlayerState> side in sides)
        {
            if (_mode != MatchModeKind.FreeForAll)
            {
                string name = me is not null && side.Key == me.Team ? "Your side" : "Their side";
                _rows.AddChild(UiKit.Body($"{name}  ·  {side.Count(p => p.Alive)} of {side.Count()} still in", 20, _teamColor(side.Key)));
            }

            foreach (PlayerState p in side.OrderByDescending(p => p.Eliminations).ThenByDescending(p => p.Hits).ThenBy(p => p.Id))
            {
                string ping = _person(p) ? _ping(p) is { } ms ? $"{ms} ms" : "" : "bot";
                string status = p.Left ? "left" : p.Alive ? "in" : "out";
                Color colour = p.Id == _you ? UiKit.Accent : p.Alive ? _teamColor(p.Team) : UiKit.Dim;
                string name = p.Id == _you && p.Name != "You" ? p.Name + " (you)" : p.Name;
                _rows.AddChild(Row(name, p.Eliminations.ToString(), p.Hits.ToString(), status, ping, colour, header: false));
            }

            _rows.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        }
    }

    private static HBoxContainer Row(string name, string eliminations, string hits, string status, string ping, Color colour, bool header)
    {
        HBoxContainer row = UiKit.Row(12);
        int size = header ? 16 : 20;
        Color text = header ? UiKit.Dim : colour;
        void Cell(string value, float width, HorizontalAlignment align)
        {
            Label label = UiKit.Body(value, size, text);
            label.CustomMinimumSize = new Vector2(width, 0);
            label.HorizontalAlignment = align;
            row.AddChild(label);
        }

        Cell(name, 330f, HorizontalAlignment.Left);
        Cell(eliminations, 80f, HorizontalAlignment.Right);
        Cell(hits, 80f, HorizontalAlignment.Right);
        Cell(status, 90f, HorizontalAlignment.Right);
        Cell(ping, 100f, HorizontalAlignment.Right);
        return row;
    }
}
