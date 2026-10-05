using System.Collections.Generic;
using Godot;
using Pb.Game.Ballistics;
using Pb.Game.Core;
using Pb.Sim.Events;

namespace Pb.Game.Ui;

/// <summary>
/// Paint on your mask (spec §1.3): when the sim says a break sprayed your mask, drops of that paint
/// colour land on the screen, sized and as opaque as the spray was strong, and clear over a couple
/// of seconds.
/// </summary>
public partial class MaskSprayOverlay : CanvasLayer, ISimEventListener
{
    private readonly List<Drop> _drops = new();
    private readonly RandomNumberGenerator _rng = new() { Seed = 4242 };
    private ImageTexture[] _textures = System.Array.Empty<ImageTexture>();
    private Color[] _teamColors = System.Array.Empty<Color>();
    private MaskSprayViewDef _def = null!;
    private int _playerId;

    public void Initialize(PresentationDef view, int playerId)
    {
        _def = view.MaskSpray;
        _playerId = playerId;
        Layer = 5;
        _textures = SplatTextures.Create(variants: 4, size: 256, seed: 913);
        _teamColors = new Color[view.TeamColors.Length];
        for (int i = 0; i < _teamColors.Length; i++)
        {
            _teamColors[i] = Color.FromHtml(view.TeamColors[i]);
        }
    }

    public void OnSimEvent(in SimEvent e)
    {
        if (e.Type != SimEventType.MaskSprayed || e.TargetId != _playerId || _def is null)
        {
            return;
        }

        Vector2 screen = GetViewport().GetVisibleRect().Size;
        int count = Mathf.Max(1, Mathf.RoundToInt(_def.Drops * e.Value));
        Color colour = _teamColors[e.Team % _teamColors.Length];
        for (int i = 0; i < count; i++)
        {
            float size = screen.Y * Mathf.Lerp(_def.MinSize, _def.MaxSize, _rng.Randf()) * (0.5f + 0.5f * e.Value);
            var rect = new TextureRect
            {
                Texture = _textures[_rng.RandiRange(0, _textures.Length - 1)],
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Size = new Vector2(size, size),
                PivotOffset = new Vector2(size, size) * 0.5f,
                Position = new Vector2(_rng.Randf() * screen.X, _rng.Randf() * screen.Y) - new Vector2(size, size) * 0.5f,
                Rotation = _rng.Randf() * Mathf.Tau,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            AddChild(rect);
            _drops.Add(new Drop(rect, colour, _def.MaxOpacity * Mathf.Clamp(0.4f + e.Value, 0f, 1f), _def.Duration_s * (0.7f + 0.3f * _rng.Randf())));
        }
    }

    public override void _Process(double delta)
    {
        for (int i = _drops.Count - 1; i >= 0; i--)
        {
            Drop d = _drops[i];
            d.Age += (float)delta;
            if (d.Age >= d.Life)
            {
                d.Rect.QueueFree();
                _drops.RemoveAt(i);
                continue;
            }

            // Stays put for a moment, then thins out as it runs and clears.
            float fade = 1f - Mathf.SmoothStep(0.25f * d.Life, d.Life, d.Age);
            Color c = d.Colour;
            c.A = d.Opacity * fade;
            d.Rect.Modulate = c;
            d.Rect.Position += new Vector2(0f, (float)delta * 6f * d.Age);
        }
    }

    private sealed class Drop
    {
        public Drop(TextureRect rect, Color colour, float opacity, float life)
        {
            Rect = rect;
            Colour = colour;
            Opacity = opacity;
            Life = life;
        }

        public TextureRect Rect { get; }

        public Color Colour { get; }

        public float Opacity { get; }

        public float Life { get; }

        public float Age { get; set; }
    }
}
