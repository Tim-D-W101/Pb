using Godot;

namespace Pb.Game.Ui;

/// <summary>
/// The elimination feed (spec §6), top right: "Magpie ▸ You · mask · 18 m", newest on top, names in
/// their team colours. Lines fade out after a few seconds.
/// </summary>
public partial class KillFeed : VBoxContainer
{
    private const string Expires = "expires";

    private int _lines = 5;
    private float _time = 6f;
    private double _now;

    public void Configure(int lines, float time)
    {
        _lines = lines;
        _time = time;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 4);
    }

    public void Add(string shooter, Color shooterColour, string victim, Color victimColour, string detail)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(Part(shooter, shooterColour));
        row.AddChild(Part("▸", new Color(0.9f, 0.9f, 0.9f)));
        row.AddChild(Part(victim, victimColour));
        row.AddChild(Part("· " + detail, new Color(0.75f, 0.75f, 0.72f)));
        row.SetMeta(Expires, _now + _time);
        AddChild(row);
        MoveChild(row, 0);
        while (GetChildCount() > _lines)
        {
            Node oldest = GetChild(GetChildCount() - 1);
            RemoveChild(oldest);
            oldest.QueueFree();
        }
    }

    public override void _Process(double delta)
    {
        _now += delta;
        for (int i = GetChildCount() - 1; i >= 0; i--)
        {
            var row = (Control)GetChild(i);
            double left = (double)row.GetMeta(Expires) - _now;
            if (left <= 0)
            {
                RemoveChild(row);
                row.QueueFree();
                continue;
            }

            row.Modulate = new Color(1f, 1f, 1f, (float)Mathf.Clamp(left, 0, 1));
        }
    }

    private static Label Part(string text, Color colour) => new()
    {
        Text = text,
        MouseFilter = MouseFilterEnum.Ignore,
        LabelSettings = new LabelSettings { FontSize = 20, FontColor = colour, OutlineSize = 6, OutlineColor = new Color(0, 0, 0, 0.85f) },
    };
}
