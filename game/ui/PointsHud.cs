using System;
using Godot;
using Pb.Sim;
using Pb.Sim.Match;
using Pb.Sim.Players;

namespace Pb.Game.Ui;

/// <summary>
/// A match of points' part of the HUD (speedball, capture the flag): the countdown to the horn, big in the middle, then
/// "GO"; the match's score under the top bar; and in speedball a bar while a buzzer is being hung, in your side's colour
/// when it's theirs and in red when it's yours.
/// </summary>
public partial class PointsHud : Control
{
    private SimWorld _sim = null!;
    private PlayerState _you = null!;
    private Func<(int Ours, int Theirs, int RaceTo, int Point)?> _score = () => null;
    private Color _ours;
    private float _hornShow;
    private Label _count = null!;
    private Label _status = null!;
    private float _goLeft;
    private MatchPhase _was = MatchPhase.Briefing;

    /// <param name="score">The match so far: your side's points and theirs, the points to win, and the point being played.</param>
    public void Initialize(SimWorld sim, PlayerState you, Color ours, float hornShow, Func<(int Ours, int Theirs, int RaceTo, int Point)?> score)
    {
        _sim = sim;
        _you = you;
        _ours = ours;
        _hornShow = hornShow;
        _score = score;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _count = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings { FontSize = 120, OutlineSize = 14, OutlineColor = new Color(0f, 0f, 0f, 0.85f) },
        };
        AddChild(_count);
        _count.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        _count.OffsetLeft = -300f;
        _count.OffsetRight = 300f;
        _count.OffsetTop = -190f;
        _count.OffsetBottom = -40f;
        _status = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings { FontSize = 20, OutlineSize = 6, OutlineColor = new Color(0f, 0f, 0f, 0.8f) },
        };
        AddChild(_status);
        _status.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        _status.OffsetLeft = -450f;
        _status.OffsetRight = 450f;
        _status.OffsetTop = 58f;
        _status.OffsetBottom = 86f;
    }

    public override void _Process(double delta)
    {
        if (_sim?.Match is not { } match)
        {
            return;
        }

        MatchPhase phase = match.Phase;
        if (_was == MatchPhase.Countdown && phase == MatchPhase.Live)
        {
            _goLeft = _hornShow;
        }

        _was = phase;
        _goLeft = MathF.Max(0f, _goLeft - (float)delta);
        _count.Visible = phase == MatchPhase.Countdown || _goLeft > 0f;
        _count.Text = phase == MatchPhase.Countdown ? $"{Math.Max(1, (int)MathF.Ceiling(match.CountdownLeft))}" : "GO";
        _count.Modulate = phase == MatchPhase.Countdown ? Colors.White : _ours;

        string line = _score() is { } s ? $"POINT {s.Point} · {s.Ours}–{s.Theirs} · FIRST TO {s.RaceTo}" : "";
        Color colour = Colors.White;
        int theirs = 1 - _you.Team;
        BuzzerSet? buzzers = match.Buzzers;
        if (buzzers is not null && buzzers.Hanger(_you.Team) >= 0)
        {
            line = "THEY'RE HANGING YOUR BUZZER";
            colour = UiKit.Bad;
        }
        else if (buzzers is not null && buzzers.Hanger(theirs) is var hanger and >= 0)
        {
            line = hanger == _you.Id ? "HANGING THEIR BUZZER: KEEP HOLDING"
                : $"{(_sim.FindPlayer(hanger)?.Name ?? "YOUR SIDE").ToUpperInvariant()} IS HANGING THEIR BUZZER";
            colour = _ours;
        }

        _status.Visible = phase != MatchPhase.Ended && line.Length > 0;
        _status.Text = line;
        _status.Modulate = colour;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_sim?.Match is not { Buzzers: { } buzzers, Phase: MatchPhase.Live })
        {
            return;
        }

        // The hang under way: the bar fills over the hang time, and empties if the hanger lets go.
        for (int side = 0; side < buzzers.Count; side++)
        {
            if (buzzers.Hanger(side) < 0)
            {
                continue;
            }

            Vector2 size = GetViewportRect().Size;
            var bar = new Rect2(size.X * 0.5f - 160f, 92f, 320f, 8f);
            DrawRect(bar, new Color(0f, 0f, 0f, 0.55f));
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * buzzers.Progress(side), bar.Size.Y)), side == _you.Team ? UiKit.Bad : _ours);
            DrawRect(bar, new Color(1f, 1f, 1f, 0.35f), filled: false, width: 1f);
            return;
        }
    }
}
