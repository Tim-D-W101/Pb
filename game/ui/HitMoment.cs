using Godot;
using Pb.Game.Core;

namespace Pb.Game.Ui;

/// <summary>
/// The moment you're hit (Phase 5): the screen effect (<c>hit_moment.gdshader</c>: the flash, the colour draining, the
/// paint's colour flooding in from the side it came from, the vignette), **HIT!** in the paint's colour, popping in and
/// settling, and under it who got you, where and from how far. It holds for the hit moment's hold, eases down while the
/// camera pulls out, and leaves a little of it while you watch yourself walk off (<see cref="Settle"/> clears it).
/// </summary>
public partial class HitMoment : CanvasLayer
{
    private HitMomentDef _def = null!;
    private ColorRect _screen = null!;
    private ShaderMaterial _material = null!;
    private Label _hit = null!;
    private Label _detail = null!;
    private float _age = -1f;
    private float _strength = 1f;
    private float _settleAge = -1f;
    private float _textScale = 1f;

    /// <summary>Whether the moment is showing (from the hit until it's settled away).</summary>
    public bool Active => _age >= 0f;

    /// <summary>How many times it has played (for the smoke tests' report).</summary>
    public int Played { get; private set; }

    public void Initialize(HitMomentDef def)
    {
        _def = def;
        Layer = 6;
        Visible = false;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hit_moment.gdshader") };
        _screen = new ColorRect { Material = _material, MouseFilter = Control.MouseFilterEnum.Ignore };
        _screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_screen);

        _hit = new Label
        {
            Text = "HIT!",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings { FontSize = (int)def.TextSize_px, OutlineSize = 22, OutlineColor = new Color(0f, 0f, 0f, 0.92f) },
        };
        AddChild(_hit);
        _detail = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings { FontSize = 30, OutlineSize = 9, OutlineColor = new Color(0f, 0f, 0f, 0.9f) },
        };
        AddChild(_detail);
    }

    /// <summary>
    /// You're hit by paint of <paramref name="paint"/>, which came from <paramref name="screenAngle"/> round the crosshair
    /// (radians clockwise from up; NaN: unknown), at the settings' <paramref name="strength"/> (0 leaves only the words).
    /// </summary>
    public void Start(Color paint, float screenAngle, string detail, float strength)
    {
        _age = 0f;
        _settleAge = -1f;
        _strength = Mathf.Clamp(strength, 0f, 1f);
        Played++;
        Visible = true;
        _material.SetShaderParameter("paint", paint);
        _material.SetShaderParameter("seed", (float)(Played * 3.7 % 11.0));
        _material.SetShaderParameter("from_dir", float.IsNaN(screenAngle) ? Vector2.Zero : new Vector2(Mathf.Sin(screenAngle), -Mathf.Cos(screenAngle)));
        _hit.LabelSettings.FontColor = paint.Lightened(0.25f);
        _detail.Text = detail;
        Layout();
    }

    public override void _ExitTree()
    {
        _material?.Dispose();
    }

    /// <summary>Fades it all away (you're watching the others now, or the summary's up).</summary>
    public void Settle()
    {
        if (_age >= 0f && _settleAge < 0f)
        {
            _settleAge = 0f;
        }
    }

    public override void _Process(double delta)
    {
        if (_age < 0f)
        {
            return;
        }

        float dt = (float)delta;
        _age += dt;
        Layout();

        // Up at once, held, then easing down to what stays while you watch.
        float hold = _def.Hold_s, ease = _def.Ease_s;
        float level = _age <= hold ? 1f : Mathf.Lerp(1f, _def.Residual, Smooth((_age - hold) / ease));
        float words = _age <= hold + ease ? 1f : Mathf.Clamp(1f - (_age - hold - ease) / 0.6f, 0f, 1f);
        if (_settleAge >= 0f)
        {
            _settleAge += dt;
            float gone = Mathf.Clamp(1f - _settleAge / 0.6f, 0f, 1f);
            level *= gone;
            words *= gone;
            if (gone <= 0f)
            {
                _age = -1f;
                Visible = false;
                return;
            }
        }

        float s = level * _strength;
        _material.SetShaderParameter("desaturate", _def.Desaturate * s);
        _material.SetShaderParameter("tint", _def.Tint * s);
        _material.SetShaderParameter("vignette", _def.Vignette * s);
        _material.SetShaderParameter("flash", _def.Flash_s > 0f ? 0.55f * _strength * Mathf.Clamp(1f - _age / _def.Flash_s, 0f, 1f) : 0f);

        // HIT! pops in at twice its size and settles, with a little shake while the hold lasts.
        _textScale = 1f + Mathf.Exp(-_age / 0.05f);
        float shake = _age < hold ? 4f * Mathf.Exp(-_age / 0.12f) : 0f;
        _hit.Scale = new Vector2(_textScale, _textScale);
        _hit.Position += new Vector2(Mathf.Sin(_age * 61f) * shake, Mathf.Cos(_age * 47f) * shake);
        _hit.Modulate = new Color(1f, 1f, 1f, words);
        _detail.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp((_age - 0.12f) / 0.15f, 0f, 1f) * words);
    }

    private void Layout()
    {
        Vector2 screen = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1600f, 900f);
        Vector2 size = new(screen.X, _def.TextSize_px * 1.4f);
        _hit.Size = size;
        _hit.PivotOffset = size * 0.5f;
        _hit.Position = new Vector2(0f, screen.Y * 0.36f - size.Y * 0.5f);
        _detail.Size = new Vector2(screen.X, 44f);
        _detail.Position = new Vector2(0f, screen.Y * 0.36f + size.Y * 0.42f);
    }

    private static float Smooth(float x)
    {
        x = Mathf.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }
}
