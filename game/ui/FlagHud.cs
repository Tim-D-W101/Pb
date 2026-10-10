using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Match;
using Pb.Sim.Players;

namespace Pb.Game.Ui;

/// <summary>
/// Capture the flag on the HUD: markers with their distance (the flag you're after, or whoever of yours carries it; where
/// you score while you carry one; your own flag while it's away from home), held to the screen's edge when they're
/// behind you or off to the side, and a line under the match's score saying how the flags stand. Display only.
/// </summary>
public partial class FlagHud : Control
{
    private const float EdgeMargin = 48f;

    /// <summary>A carrier's marker this high over their feet (m): over the flag on their back, clear of their name.</summary>
    private const float CarrierMark = 3.4f;

    /// <summary>A carrier of yours or theirs this close (m) gets no marker.</summary>
    private const float NearCarrier = 8f;

    private readonly List<(Vector3 At, string Label, Color Colour)> _marks = new();
    private SimWorld _sim = null!;
    private PlayerState _you = null!;
    private FlagSet _flags = null!;
    private FlagsViewDef _view = null!;
    private Func<int, Color> _colourOf = _ => Colors.White;
    private Label _status = null!;

    public void Initialize(SimWorld sim, PlayerState you, FlagSet flags, FlagsViewDef view, Func<int, Color> colourOf)
    {
        _sim = sim;
        _you = you;
        _flags = flags;
        _view = view;
        _colourOf = colourOf;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _status = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings { FontSize = 18, OutlineSize = 6, OutlineColor = new Color(0f, 0f, 0f, 0.8f) },
        };
        AddChild(_status);
        _status.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        _status.OffsetLeft = -450f;
        _status.OffsetRight = 450f;
        _status.OffsetTop = 88f;
        _status.OffsetBottom = 114f;
    }

    public override void _Process(double delta)
    {
        if (_flags is null)
        {
            return;
        }

        Visible = _sim.Match is { Phase: not MatchPhase.Ended } && _flags.CapturedBy < 0;
        (string line, bool bad) = StatusLine();
        _status.Text = line;
        _status.Modulate = bad ? UiKit.Bad : _colourOf(_you.Team);
        Marks();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (GetViewport().GetCamera3D() is not { } camera)
        {
            return;
        }

        foreach ((Vector3 at, string label, Color colour) in _marks)
        {
            DrawMark(camera, at, label, colour);
        }
    }

    private bool Centre => _flags.IsCentre;

    /// <summary>"the flag" on a field (nobody's), "their flag" with a flag each.</summary>
    private string TheirFlag => Centre ? "THE FLAG" : "THEIR FLAG";

    private string ScoreAt => Centre ? "THEIR BUZZER" : "YOUR BASE";

    private string Who(int id) => id == _you.Id ? "YOU" : (_sim.FindPlayer(id)?.Name ?? "SOMEONE").ToUpperInvariant();

    private (string Line, bool Bad) StatusLine()
    {
        int target = _flags.TargetOf(_you.Team);
        int own = _flags.FlagOfSide(_you.Team);
        if (_flags.FlagOf(_you.Id) >= 0)
        {
            return ($"YOU HAVE {TheirFlag} · GET IT TO {ScoreAt} (NO SPRINTING)", false);
        }

        // Your own flag away from home matters most.
        if (own >= 0 && _flags.Carrier(own) is var taker and >= 0)
        {
            return ($"THEY HAVE YOUR FLAG · STOP {Who(taker)}", true);
        }

        if (target >= 0 && _flags.Carrier(target) is var carrier and >= 0)
        {
            return _sim.FindPlayer(carrier)?.Team == _you.Team
                ? ($"{Who(carrier)} HAS {TheirFlag} · COVER THEM", false)
                : ($"{Who(carrier)} HAS THE FLAG · STOP THEM", true);
        }

        if (own >= 0 && _flags.Status(own) == FlagStatus.Dropped)
        {
            return ("YOUR FLAG IS DOWN WHERE ITS CARRIER FELL", true);
        }

        if (target >= 0 && _flags.Status(target) == FlagStatus.Dropped)
        {
            return ($"{TheirFlag} IS DOWN · PICK IT UP", false);
        }

        return (Centre ? "TAKE THE FLAG FROM THE CENTRE BUNKER TO THEIR BUZZER" : "TAKE THEIR FLAG HOME · KEEP YOURS", false);
    }

    /// <summary>What the markers point at this frame.</summary>
    private void Marks()
    {
        _marks.Clear();
        if (!_you.Alive || _sim.Match?.Phase != MatchPhase.Live)
        {
            return;
        }

        int target = _flags.TargetOf(_you.Team);
        int own = _flags.FlagOfSide(_you.Team);
        Color ours = _colourOf(_you.Team);
        Color neutral = Color.FromHtml(_view.CentreColor);
        if (_flags.FlagOf(_you.Id) >= 0)
        {
            _marks.Add((_flags.ScoreAt(_you.Team).ToGodot() + new Vector3(0f, 1.5f, 0f), ScoreAt, ours));
        }
        else if (target >= 0)
        {
            Color colour = _flags.Owner(target) < 0 ? neutral : _colourOf(_flags.Owner(target));
            if (_flags.Carrier(target) is var carrier and >= 0 && _sim.FindPlayer(carrier) is { } who)
            {
                // Close by, their name over their head says who it is (and a marker above them would sit on the top bar).
                if (System.Numerics.Vector3.Distance(who.Position, _you.Position) >= NearCarrier)
                {
                    _marks.Add((who.Position.ToGodot() + new Vector3(0f, CarrierMark, 0f), who.Team == _you.Team ? $"ESCORT {Who(carrier)}" : $"STOP {Who(carrier)}",
                        who.Team == _you.Team ? ours : UiKit.Bad));
                }
            }
            else
            {
                string label = _flags.Status(target) == FlagStatus.Dropped ? $"{TheirFlag} · DOWN" : TheirFlag;
                _marks.Add((_flags.Position(target).ToGodot() + new Vector3(0f, 1.2f, 0f), label, colour));
            }
        }

        if (own >= 0 && _flags.Status(own) != FlagStatus.Home)
        {
            PlayerState? taker = _flags.Carrier(own) >= 0 ? _sim.FindPlayer(_flags.Carrier(own)) : null;
            if (taker is null)
            {
                _marks.Add((_flags.Position(own).ToGodot() + new Vector3(0f, 1.2f, 0f), "YOUR FLAG · DOWN", ours));
            }
            else if (System.Numerics.Vector3.Distance(taker.Position, _you.Position) >= NearCarrier)
            {
                _marks.Add((taker.Position.ToGodot() + new Vector3(0f, CarrierMark, 0f), $"YOUR FLAG · {Who(taker.Id)}", UiKit.Bad));
            }
        }
    }

    private void DrawMark(Camera3D camera, Vector3 target, string label, Color colour)
    {
        Vector2 size = GetViewportRect().Size;
        Vector3 toTarget = target - camera.GlobalPosition;
        bool behind = camera.GlobalTransform.Basis.Z.Dot(toTarget) > 0f;
        Vector2 at = camera.UnprojectPosition(target);
        if (behind)
        {
            at = size - at; // mirrored, so it points the way to turn
        }

        Vector2 middle = size * 0.5f;
        Vector2 offset = at - middle;
        var half = new Vector2(middle.X - EdgeMargin, middle.Y - EdgeMargin);
        if (behind || MathF.Abs(offset.X) > half.X || MathF.Abs(offset.Y) > half.Y)
        {
            if (behind && offset.LengthSquared() < 1f)
            {
                offset = new Vector2(0f, half.Y);
            }

            float scale = MathF.Min(half.X / MathF.Max(1e-3f, MathF.Abs(offset.X)), half.Y / MathF.Max(1e-3f, MathF.Abs(offset.Y)));
            at = middle + offset * MathF.Min(1f, scale);
        }

        // A little flag: its pole, and a pennant off it.
        float r = _view.MarkerSize_px * 0.5f;
        DrawLine(at + new Vector2(-r * 0.6f, r), at + new Vector2(-r * 0.6f, -r), new Color(0f, 0f, 0f, 0.8f), 3f, antialiased: true);
        var pennant = new[] { at + new Vector2(-r * 0.6f, -r), at + new Vector2(r, -r * 0.45f), at + new Vector2(-r * 0.6f, r * 0.1f) };
        DrawColoredPolygon(pennant, colour with { A = 0.9f });
        DrawPolyline(new[] { pennant[0], pennant[1], pennant[2], pennant[0] }, new Color(0f, 0f, 0f, 0.7f), 1.5f, antialiased: true);
        DrawLine(at + new Vector2(-r * 0.6f, r), at + new Vector2(-r * 0.6f, -r), colour.Lightened(0.4f), 1.5f, antialiased: true);
        float metres = (target - _you.EyePosition.ToGodot()).Length();
        string text = $"{label} · {metres:0} m";
        Font font = ThemeDB.FallbackFont;
        const int FontSize = 16;
        Vector2 extent = font.GetStringSize(text, HorizontalAlignment.Left, -1, FontSize);
        var textAt = new Vector2(Mathf.Clamp(at.X - extent.X * 0.5f, 8f, size.X - extent.X - 8f), at.Y + r + 18f);
        DrawString(font, textAt + new Vector2(1f, 1f), text, HorizontalAlignment.Left, -1, FontSize, new Color(0f, 0f, 0f, 0.8f));
        DrawString(font, textAt, text, HorizontalAlignment.Left, -1, FontSize, colour);
    }
}
