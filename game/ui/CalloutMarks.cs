using System.Collections.Generic;
using Godot;

namespace Pb.Game.Ui;

/// <summary>
/// Where a teammate called "Contact!": a mark on the screen over the spot, with who called and how far it is, for a
/// few seconds (hidden while the spot's behind you).
/// </summary>
public partial class CalloutMarks : Control
{
    private readonly List<(Vector3 At, Color Colour, string Text, double Left)> _marks = new();
    private readonly List<Label> _labels = new();

    /// <summary>How long a mark stays up (s).</summary>
    public float Show_s { get; set; } = 4f;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public void Add(Vector3 at, Color colour, string text)
    {
        _marks.Add((at, colour, text, Show_s));
        if (_marks.Count > 6)
        {
            _marks.RemoveAt(0);
        }
    }

    public override void _Process(double delta)
    {
        for (int i = _marks.Count - 1; i >= 0; i--)
        {
            (Vector3 at, Color colour, string text, double left) = _marks[i];
            left -= delta;
            if (left <= 0)
            {
                _marks.RemoveAt(i);
                continue;
            }

            _marks[i] = (at, colour, text, left);
        }

        while (_labels.Count < _marks.Count)
        {
            var label = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            label.AddThemeFontSizeOverride("font_size", 18);
            label.AddThemeConstantOverride("outline_size", 6);
            label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.8f));
            AddChild(label);
            _labels.Add(label);
        }

        Camera3D? camera = GetViewport().GetCamera3D();
        for (int i = 0; i < _labels.Count; i++)
        {
            Label label = _labels[i];
            if (i >= _marks.Count || camera is null || camera.IsPositionBehind(_marks[i].At + Vector3.Up * 1.4f))
            {
                label.Visible = false;
                continue;
            }

            (Vector3 at, Color colour, string text, double left) = _marks[i];
            Vector3 head = at + Vector3.Up * 1.4f;
            float distance = camera.GlobalPosition.DistanceTo(at);
            label.Text = $"◆\n{text} · {distance:0} m";
            label.AddThemeColorOverride("font_color", colour);
            label.Modulate = new Color(1f, 1f, 1f, (float)Mathf.Clamp(left / 0.6, 0.0, 1.0));
            Vector2 screen = camera.UnprojectPosition(head);
            label.ResetSize();
            label.Position = screen - new Vector2(label.Size.X * 0.5f, 10f);
            label.Visible = true;
        }
    }
}
