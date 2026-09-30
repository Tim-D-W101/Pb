using Godot;

namespace Pb.Game.Ui;

/// <summary>Minimal toggleable crosshair: four ticks and a centre dot, outlined.</summary>
public partial class CrosshairControl : Control
{
    private float _size = 6f;
    private float _gap = 3f;
    private float _thickness = 2f;
    private Color _color = Colors.White;

    public void Configure(float size, float gap, float thickness, Color color)
    {
        _size = size;
        _gap = gap;
        _thickness = thickness;
        _color = color;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 c = Size * 0.5f;
        var outline = new Color(0, 0, 0, 0.8f);
        foreach ((Color col, float extra) in new[] { (outline, 2f), (_color, 0f) })
        {
            float w = _thickness + extra;
            DrawLine(c + new Vector2(_gap, 0), c + new Vector2(_gap + _size, 0), col, w);
            DrawLine(c - new Vector2(_gap, 0), c - new Vector2(_gap + _size, 0), col, w);
            DrawLine(c + new Vector2(0, _gap), c + new Vector2(0, _gap + _size), col, w);
            DrawLine(c - new Vector2(0, _gap), c - new Vector2(0, _gap + _size), col, w);
            DrawCircle(c, (w + 0.5f) * 0.5f, col);
        }
    }
}
