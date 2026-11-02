using Godot;
using Pb.Game.Core;

namespace Pb.Game.Ui;

/// <summary>What a hit marker says.</summary>
public enum HitMarkKind
{
    /// <summary>Your ball put someone out.</summary>
    Out,
    /// <summary>Your ball bounced off someone: it doesn't count.</summary>
    Bounce,
    /// <summary>Your ball put a teammate out.</summary>
    Teammate,
    /// <summary>Your ball broke on a target (the training ground).</summary>
    Target,
}

/// <summary>
/// The hit markers (Phase 5): an X round the crosshair that pops in big and settles, coloured by what happened (put out,
/// bounced off, a teammate, a target), and after a put-out a line under the crosshair saying who, where, how far and how
/// many are left. A put-out's X is thick with a white flash at its heart; a bounce's is thin and short, in grey, since it
/// doesn't count.
/// </summary>
public partial class HitMarkers : Control
{
    private HitMarkersDef _def = null!;
    private RichTextLabel _line = null!;
    private float _age = 99f;
    private float _lineLeft;
    private HitMarkKind _kind;
    private Color _colour = Colors.White;

    /// <summary>How many markers have shown, by kind (for the smoke tests' report).</summary>
    public int[] Shown { get; } = new int[4];

    public void Configure(HitMarkersDef def)
    {
        _def = def;
        MouseFilter = MouseFilterEnum.Ignore;
        _line = new RichTextLabel
        {
            MouseFilter = MouseFilterEnum.Ignore,
            BbcodeEnabled = true,
            ScrollActive = false,
            FitContent = true,
            AutowrapMode = TextServer.AutowrapMode.Off,
            Visible = false,
        };
        _line.AddThemeFontSizeOverride("normal_font_size", 24);
        _line.AddThemeFontSizeOverride("bold_font_size", 24);
        _line.AddThemeConstantOverride("outline_size", 8);
        _line.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
        AddChild(_line);
    }

    /// <summary>
    /// Shows a marker of <paramref name="kind"/>, and under the crosshair <paramref name="word"/> in the marker's colour
    /// followed by <paramref name="line"/>, if there's one.
    /// </summary>
    public void Show(HitMarkKind kind, string? line = null, string word = "")
    {
        if (_def is null)
        {
            return;
        }

        _kind = kind;
        _age = 0f;
        _colour = Color.FromHtml(kind switch
        {
            HitMarkKind.Out => _def.OutColor,
            HitMarkKind.Bounce => _def.BounceColor,
            HitMarkKind.Teammate => _def.TeammateColor,
            _ => _def.TargetColor,
        });
        Shown[(int)kind]++;
        if (line is not null)
        {
            string lead = word.Length > 0 ? $"[color=#{_colour.ToHtml(false)}][b]{word}[/b][/color]   " : "";
            _line.Text = $"[center]{lead}{line}[/center]";
            _line.Modulate = Colors.White;
            _line.Visible = true;
            _lineLeft = _def.LineTime_s;
        }

        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_lineLeft > 0f)
        {
            _lineLeft -= dt;
            float a = Mathf.Clamp(_lineLeft / 0.5f, 0f, 1f);
            _line.Modulate = _line.Modulate with { A = a };
            _line.Visible = _lineLeft > 0f;
        }

        _line.Size = new Vector2(Size.X, 34f);
        _line.Position = new Vector2(0f, Size.Y * 0.5f + 96f);
        if (_def is not null && _age < _def.Time_s)
        {
            _age += dt;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_def is null || _age >= _def.Time_s)
        {
            return;
        }

        Vector2 c = Size * 0.5f;
        bool counts = _kind is HitMarkKind.Out or HitMarkKind.Teammate;
        // Pops in at `pop` times its size and settles in about 80 ms; fades over the last 40% of its time.
        float scale = 1f + (_def.Pop - 1f) * Mathf.Exp(-_age / 0.035f);
        float alpha = Mathf.Clamp((_def.Time_s - _age) / (_def.Time_s * 0.4f), 0f, 1f);
        float size = _def.Size_px * scale * (counts ? 1f : 0.8f);
        float inner = size * (counts ? 0.42f : 0.55f);
        float width = _def.Thickness_px * (counts ? 1f : 0.6f);
        var outline = new Color(0f, 0f, 0f, 0.85f * alpha);
        Color colour = _colour with { A = alpha };
        foreach (Vector2 d in new[] { new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(-1, -1) })
        {
            Vector2 unit = d.Normalized();
            DrawLine(c + unit * (inner - 1f), c + unit * (size + 1f), outline, width + 3f, true);
            DrawLine(c + unit * inner, c + unit * size, colour, width, true);
            if (_kind == HitMarkKind.Out && _age < 0.09f)
            {
                // The first moments of a put-out: a white-hot core in each stroke.
                DrawLine(c + unit * inner, c + unit * size, new Color(1f, 1f, 1f, 1f - _age / 0.09f), Mathf.Max(1f, width * 0.45f), true);
            }
        }

        if (_kind == HitMarkKind.Teammate)
        {
            DrawArc(c, size + 5f, 0f, Mathf.Tau, 40, colour, 2f, true);
        }
    }
}
