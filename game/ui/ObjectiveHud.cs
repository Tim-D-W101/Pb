using System;
using Godot;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Ui;

/// <summary>
/// The objective on the HUD: a marker with its distance (Retrieve: the case's building until you've seen the case from
/// close enough, then the case itself; the nearest way out while you carry it; whoever of yours carries it. Hold: the
/// room), held to the screen's edge when it's behind you or off to the side; and a line under the top bar saying how it
/// stands, with the hold clock as a bar. Display only.
/// </summary>
public partial class ObjectiveHud : Control
{
    private const float EdgeMargin = 48f;

    private SimWorld _sim = null!;
    private PlayerState _you = null!;
    private ObjectiveState _objective = null!;
    private ObjectivesViewDef _view = null!;
    private Color _colour;
    private Label _status = null!;
    private bool _seen;
    private Vector3 _target;
    private string _label = "";
    private bool _hasTarget;

    public void Initialize(SimWorld sim, PlayerState you, ObjectiveState objective, ObjectivesViewDef view)
    {
        _sim = sim;
        _you = you;
        _objective = objective;
        _view = view;
        _colour = Color.FromHtml(view.Color);
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _status = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        _status.AddThemeFontSizeOverride("font_size", 20);
        _status.AddThemeColorOverride("font_color", _colour);
        _status.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.8f));
        _status.AddThemeConstantOverride("outline_size", 6);
        _status.SetAnchorsPreset(LayoutPreset.CenterTop);
        _status.OffsetLeft = -450f;
        _status.OffsetRight = 450f;
        _status.OffsetTop = 58f;
        _status.OffsetBottom = 86f;
        AddChild(_status);
    }

    public override void _Process(double delta)
    {
        if (_objective is null)
        {
            return;
        }

        Visible = !_objective.Done && _sim.Match?.Phase != MatchPhase.Ended;
        _status.Text = StatusLine();
        _hasTarget = Target(out _target, out _label);
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawHoldBar();

        if (!_hasTarget || GetViewport().GetCamera3D() is not { } camera)
        {
            return;
        }

        Vector2 size = GetViewportRect().Size;
        Vector3 toTarget = _target - camera.GlobalPosition;
        bool behind = camera.GlobalTransform.Basis.Z.Dot(toTarget) > 0f;
        Vector2 at = camera.UnprojectPosition(_target);
        if (behind)
        {
            at = size - at; // mirrored, so it points the way to turn
        }

        // Off screen (or behind): held to the edge, on the line from the middle.
        Vector2 middle = size * 0.5f;
        Vector2 offset = at - middle;
        var half = new Vector2(middle.X - EdgeMargin, middle.Y - EdgeMargin);
        bool outside = behind || MathF.Abs(offset.X) > half.X || MathF.Abs(offset.Y) > half.Y;
        if (outside)
        {
            if (behind && offset.LengthSquared() < 1f)
            {
                offset = new Vector2(0f, half.Y);
            }

            float scale = MathF.Min(half.X / MathF.Max(1e-3f, MathF.Abs(offset.X)), half.Y / MathF.Max(1e-3f, MathF.Abs(offset.Y)));
            at = middle + offset * MathF.Min(1f, scale);
        }

        float r = _view.MarkerSize_px * 0.5f;
        var diamond = new[] { at + new Vector2(0f, -r), at + new Vector2(r, 0f), at + new Vector2(0f, r), at + new Vector2(-r, 0f) };
        DrawColoredPolygon(diamond, _colour with { A = 0.85f });
        DrawPolyline(new[] { diamond[0], diamond[1], diamond[2], diamond[3], diamond[0] }, new Color(0f, 0f, 0f, 0.7f), 1.5f, antialiased: true);
        float metres = (_target - _you.EyePosition.ToGodot()).Length();
        string text = $"{_label} · {metres:0} m";
        Font font = ThemeDB.FallbackFont;
        const int FontSize = 16;
        Vector2 extent = font.GetStringSize(text, HorizontalAlignment.Left, -1, FontSize);
        var textAt = new Vector2(Mathf.Clamp(at.X - extent.X * 0.5f, 8f, size.X - extent.X - 8f), at.Y + r + 18f);
        DrawString(font, textAt + new Vector2(1f, 1f), text, HorizontalAlignment.Left, -1, FontSize, new Color(0f, 0f, 0f, 0.8f));
        DrawString(font, textAt, text, HorizontalAlignment.Left, -1, FontSize, _colour);
    }

    /// <summary>Hold: the clock as a bar under the status line, red while it's contested.</summary>
    private void DrawHoldBar()
    {
        if (_objective is not { Kind: ObjectiveKind.Hold })
        {
            return;
        }

        Vector2 size = GetViewportRect().Size;
        var bar = new Rect2(size.X * 0.5f - 160f, 92f, 320f, 8f);
        DrawRect(bar, new Color(0f, 0f, 0f, 0.55f));
        float share = Mathf.Clamp(_objective.Held / _objective.HoldRules.HoldTime, 0f, 1f);
        Color fill = _objective.Status == HoldStatus.Contested ? UiKit.Bad : _colour;
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * share, bar.Size.Y)), fill);
        DrawRect(bar, new Color(1f, 1f, 1f, 0.35f), filled: false, width: 1f);
    }

    private string StatusLine()
    {
        if (_objective.Kind == ObjectiveKind.Hold)
        {
            string room = ModeText.The(_objective.Room!.Name).ToUpperInvariant();
            string clock = $"{_objective.Held:0} / {_objective.HoldRules.HoldTime:0} s";
            return _objective.Status switch
            {
                HoldStatus.Ours => $"HOLDING {room} · {clock}",
                HoldStatus.Contested => $"{room} IS CONTESTED · {clock}",
                HoldStatus.Theirs => $"THEY HOLD {room} · {clock}",
                _ => $"HOLD {room} · {clock}",
            };
        }

        if (_objective.Carrier == _you.Id)
        {
            return "YOU HAVE THE CASE · GET IT TO A WAY OUT (NO SPRINTING)";
        }

        if (_objective.Carrier >= 0 && _sim.FindPlayer(_objective.Carrier) is { } carrier)
        {
            return $"{carrier.Name.ToUpperInvariant()} HAS THE CASE · COVER THEM";
        }

        return _objective.CaseMoved ? "THE CASE IS DOWN · PICK IT UP" : $"FIND THE CASE · {_objective.Spot.Area.ToUpperInvariant()}";
    }

    /// <summary>What the marker points at, and what it says.</summary>
    private bool Target(out Vector3 at, out string label)
    {
        at = Vector3.Zero;
        label = "";
        if (_objective.Done || !_you.Alive)
        {
            return false;
        }

        if (_objective.Kind == ObjectiveKind.Hold)
        {
            at = _objective.Room!.Centre.ToGodot() + new Vector3(0f, 1.2f, 0f);
            label = _objective.Room.Name.ToUpperInvariant();
            return true;
        }

        if (_objective.Carrier == _you.Id)
        {
            ExitSpec nearest = _objective.Level.Exits[0];
            foreach (ExitSpec exit in _objective.Level.Exits)
            {
                if (SVector3.Distance(exit.Position, _you.Position) < SVector3.Distance(nearest.Position, _you.Position))
                {
                    nearest = exit;
                }
            }

            at = nearest.Position.ToGodot() + new Vector3(0f, 1.5f, 0f);
            label = nearest.Name.ToUpperInvariant();
            return true;
        }

        if (_objective.Carrier >= 0 && _sim.FindPlayer(_objective.Carrier) is { } carrier)
        {
            at = carrier.Position.ToGodot() + new Vector3(0f, 2.1f, 0f);
            label = $"ESCORT {carrier.Name.ToUpperInvariant()}";
            return true;
        }

        // Lying: its building until you've seen it close enough (or it's been moved, and your side knows where it fell).
        Vector3 casePoint = _objective.CasePosition.ToGodot() + new Vector3(0f, 0.3f, 0f);
        if (!_seen && (_objective.CaseMoved || Sees(casePoint)))
        {
            _seen = true;
        }

        if (_seen)
        {
            at = casePoint;
            label = "CASE";
            return true;
        }

        Pb.Sim.Collision.Aabb box = _objective.Spot.AreaBox;
        at = new Vector3((box.Min.X + box.Max.X) * 0.5f, MathF.Max(box.Min.Y, 0f) + 1.5f, (box.Min.Z + box.Max.Z) * 0.5f);
        label = $"CASE · {_objective.Spot.Area.ToUpperInvariant()}";
        return true;
    }

    private bool Sees(Vector3 point)
    {
        SVector3 eye = _you.EyePosition;
        SVector3 target = point.ToSim();
        return SVector3.Distance(eye, target) <= _view.SeenWithin_m && !_sim.Collision.SweepSphere(eye, target, 0f, out _, includeDynamic: true);
    }
}
