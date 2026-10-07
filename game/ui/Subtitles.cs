using Godot;

namespace Pb.Game.Ui;

/// <summary>
/// Subtitles for what players shout ("Magpie: Contact!"), above the toasts at the bottom of the
/// screen: the speaker's callsign in their team colour, at most two lines, each for a few seconds.
/// </summary>
public partial class Subtitles : VBoxContainer
{
    private const string Expires = "expires";
    private const int MaxLines = 2;

    private float _time = 2.5f;
    private double _now;

    public void Configure(float time)
    {
        _time = time;
        MouseFilter = MouseFilterEnum.Ignore;
        Alignment = AlignmentMode.End;
    }

    /// <param name="size">Their size, as a share of the usual (the settings').</param>
    public void Show(string speaker, Color colour, string line, float size = 1f)
    {
        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(900, 0),
            Text = $"[center][color=#{colour.ToHtml(false)}]{speaker}[/color]: {line}[/center]",
        };
        label.AddThemeFontSizeOverride("normal_font_size", Mathf.RoundToInt(24 * size));
        label.AddThemeConstantOverride("outline_size", 6);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        label.SetMeta(Expires, _now + _time);
        AddChild(label);
        while (GetChildCount() > MaxLines)
        {
            Node oldest = GetChild(0);
            RemoveChild(oldest);
            oldest.QueueFree();
        }
    }

    public override void _Process(double delta)
    {
        _now += delta;
        for (int i = GetChildCount() - 1; i >= 0; i--)
        {
            var line = (Control)GetChild(i);
            double left = (double)line.GetMeta(Expires) - _now;
            if (left <= 0)
            {
                RemoveChild(line);
                line.QueueFree();
                continue;
            }

            line.Modulate = new Color(1f, 1f, 1f, (float)Mathf.Clamp(left * 2, 0, 1));
        }
    }
}
