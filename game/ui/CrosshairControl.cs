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

    private float _markerLeft;
    private float _markerTime = 0.35f;
    private Color _markerColor = Colors.White;

    /// <summary>Flashes the hit marker: four ticks round the crosshair that fade out.</summary>
    public void Flash(float seconds, Color color)
    {
        _markerTime = seconds;
        _markerLeft = seconds;
        _markerColor = color;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_markerLeft > 0f)
        {
            _markerLeft -= (float)delta;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        Vector2 c = Size * 0.5f;
        if (_markerLeft > 0f)
        {
            var colour = _markerColor with { A = Mathf.Clamp(_markerLeft / _markerTime, 0f, 1f) };
            float inner = _gap + _size + 3f;
            float outer = inner + 9f;
            foreach (Vector2 d in new[] { new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(-1, -1) })
            {
                Vector2 unit = d.Normalized();
                DrawLine(c + unit * inner, c + unit * outer, new Color(0, 0, 0, colour.A * 0.8f), _thickness + 2f);
                DrawLine(c + unit * inner, c + unit * outer, colour, _thickness);
            }
        }

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
