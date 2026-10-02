using System;
using Godot;
using Pb.Sim;
using Pb.Sim.Players;

namespace Pb.Game.Ui;

/// <summary>
/// The top of the match HUD (spec §6): the round clock in the middle, your team's players to its left
/// and the opponents to its right, one icon each, filled in the team colour while they're in and greyed
/// out with a cross once they're out.
/// </summary>
public partial class TopBar : Control
{
    private const float ClockHalfWidth = 64f;
    private const float Spacing = 6f;
    private const float Top = 16f;

    private SimWorld _sim = null!;
    private int _heroTeam;
    private Color[] _teams = Array.Empty<Color>();
    private float _icon;
    private Func<string> _clock = () => "";
    private Label _label = null!;

    public void Initialize(SimWorld sim, PlayerState hero, Color[] teamColors, float iconSize, Func<string> clock)
    {
        _sim = sim;
        _heroTeam = hero.Team;
        _teams = teamColors;
        _icon = iconSize;
        _clock = clock;
        MouseFilter = MouseFilterEnum.Ignore;
        // Already in the tree: anchors and offsets both, or it keeps its current (empty) rect.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings { FontSize = 30, OutlineSize = 8, OutlineColor = new Color(0, 0, 0, 0.85f) },
        };
        AddChild(_label);
        _label.SetAnchorsPreset(LayoutPreset.CenterTop, keepOffsets: true);
        _label.OffsetLeft = -ClockHalfWidth;
        _label.OffsetRight = ClockHalfWidth;
        _label.OffsetTop = Top - 12f;
        _label.OffsetBottom = Top + 28f;
    }

    public override void _Process(double delta)
    {
        if (_sim is null)
        {
            return;
        }

        _label.Text = _clock();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_sim is null)
        {
            return;
        }

        float centre = Size.X * 0.5f;
        float y = Top + _icon * 0.5f + 6f;
        int left = 0;
        int right = 0;
        foreach (PlayerState p in _sim.Players)
        {
            bool ours = p.Team == _heroTeam;
            int index = ours ? left++ : right++;
            float offset = ClockHalfWidth + _icon * 0.5f + index * (_icon + Spacing);
            float x = ours ? centre - offset : centre + offset;
            Icon(new Vector2(x, y), _teams[p.Team % _teams.Length], p.Alive);
        }
    }

    private void Icon(Vector2 at, Color colour, bool alive)
    {
        float r = _icon * 0.5f;
        DrawCircle(at, r + 1.5f, new Color(0, 0, 0, 0.75f));
        if (alive)
        {
            DrawCircle(at, r, colour);
            return;
        }

        DrawCircle(at, r, new Color(0.18f, 0.18f, 0.18f, 0.9f));
        float k = r * 0.55f;
        var mark = new Color(0.85f, 0.85f, 0.85f, 0.9f);
        DrawLine(at + new Vector2(-k, -k), at + new Vector2(k, k), mark, 2f);
        DrawLine(at + new Vector2(-k, k), at + new Vector2(k, -k), mark, 2f);
    }
}
