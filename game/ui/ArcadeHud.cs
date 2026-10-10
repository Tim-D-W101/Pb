using System;
using System.Collections.Generic;
using Godot;
using Pb.Sim;
using Pb.Sim.Match;
using Pb.Sim.Players;

namespace Pb.Game.Ui;

/// <summary>
/// Arcade's part of the HUD: the score under the top bar (each side's eliminations, or in free-for-all yours and the
/// leader's) and, while you're shielded just after coming back in, a line under the crosshair saying so, shrinking as
/// the shield runs out.
/// </summary>
public partial class ArcadeHud : Control
{
    private SimWorld _sim = null!;
    private PlayerState _you = null!;
    private Func<int, Color> _colourOf = _ => Colors.White;
    private bool _freeForAll;
    private Label _score = null!;
    private Label _shield = null!;
    private float _shieldFor;
    private int _ours = -1;
    private int _theirs = -1;
    private int _leader = -2;

    public void Initialize(SimWorld sim, PlayerState you, Func<int, Color> colourOf, bool freeForAll, float shieldFor)
    {
        _sim = sim;
        _you = you;
        _colourOf = colourOf;
        _freeForAll = freeForAll;
        _shieldFor = shieldFor;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _score = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings { FontSize = 22, OutlineSize = 7, OutlineColor = new Color(0f, 0f, 0f, 0.8f) },
        };
        AddChild(_score);
        _score.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        _score.OffsetLeft = -450f;
        _score.OffsetRight = 450f;
        _score.OffsetTop = 58f;
        _score.OffsetBottom = 88f;
        _shield = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = UiKit.Accent,
            LabelSettings = new LabelSettings { FontSize = 20, OutlineSize = 6, OutlineColor = new Color(0f, 0f, 0f, 0.8f) },
            Visible = false,
        };
        AddChild(_shield);
        _shield.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        _shield.OffsetLeft = -300f;
        _shield.OffsetRight = 300f;
        _shield.OffsetTop = 78f;
        _shield.OffsetBottom = 104f;
    }

    public override void _Process(double delta)
    {
        if (_sim?.Match is not { } match)
        {
            return;
        }

        UpdateScore();
        bool shielded = _you.Alive && _you.ShieldedAt(_sim.Tick) && match.Phase == MatchPhase.Live;
        _shield.Visible = shielded;
        if (shielded)
        {
            float left = (_you.ShieldUntilTick - _sim.Tick) * _sim.Dt;
            _shield.Text = $"SHIELDED {left:0.0} s · FIRING ENDS IT";
        }

        QueueRedraw();
    }

    /// <summary>
    /// The score line, made again only when it changes: from everyone's own count, which a joining copy has from each
    /// snapshot (the match's numbers come only at its end there).
    /// </summary>
    private void UpdateScore()
    {
        IReadOnlyList<PlayerState> players = _sim.Players;
        if (!_freeForAll)
        {
            int ours = 0, theirs = 0;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].Team == _you.Team)
                {
                    ours += players[i].Eliminations;
                }
                else
                {
                    theirs += players[i].Eliminations;
                }
            }

            if (ours != _ours || theirs != _theirs)
            {
                (_ours, _theirs) = (ours, theirs);
                _score.Text = $"YOUR SIDE {ours}  –  {theirs} THEIRS";
                _score.Modulate = ours > theirs ? _colourOf(_you.Team) : ours < theirs ? _colourOf(1 - _you.Team) : Colors.White;
            }

            return;
        }

        // Free-for-all: yours, and the best of everyone else's.
        int yours = 0, best = -1, bestId = -1;
        bool level = false;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerState p = players[i];
            if (p == _you)
            {
                yours = p.Eliminations;
            }
            else if (p.Eliminations > best)
            {
                (best, bestId, level) = (p.Eliminations, p.Id, false);
            }
            else if (p.Eliminations == best)
            {
                level = true;
            }
        }

        int leader = level ? -1 : bestId;
        if (yours == _ours && best == _theirs && leader == _leader)
        {
            return;
        }

        (_ours, _theirs, _leader) = (yours, best, leader);
        string name = _sim.FindPlayer(bestId)?.Name.ToUpperInvariant() ?? "NOBODY";
        _score.Text = best < 0 ? $"YOU {yours}"
            : yours > best ? $"YOU {yours} · NEXT {(level ? "SEVERAL" : name)} {best}"
            : yours == best ? $"YOU {yours} · LEVEL WITH {(level ? "SEVERAL" : name)}"
            : $"YOU {yours} · {(level ? "SEVERAL LEAD" : name + " LEADS")} WITH {best}";
        _score.Modulate = yours > best ? _colourOf(_you.Team) : Colors.White;
    }

    public override void _Draw()
    {
        if (!_shield.Visible || _shieldFor <= 0f)
        {
            return;
        }

        // The shield running out: a bar under its line, emptying.
        float left = Mathf.Clamp((_you.ShieldUntilTick - _sim.Tick) * _sim.Dt / _shieldFor, 0f, 1f);
        var bar = new Rect2(Size.X * 0.5f - 110f, Size.Y * 0.5f + 108f, 220f, 5f);
        DrawRect(bar, new Color(0f, 0f, 0f, 0.5f));
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * left, bar.Size.Y)), UiKit.Accent);
    }
}
