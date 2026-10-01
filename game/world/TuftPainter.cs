using System;
using Godot;
using Pb.Sim.Core;

namespace Pb.Game.World;

/// <summary>
/// Paints the weed atlas for <see cref="WeedField"/>: blades, leaves, stalks and seed heads as tapered
/// curves. Each stroke is anti-aliased once against its own coverage, then laid over the strokes
/// before it, so overlapping blades read as separate blades.
/// </summary>
public sealed class TuftPainter
{
    private static readonly Color GrassRoot = new(0.36f, 0.33f, 0.2f);
    private static readonly Color GrassTip = new(0.86f, 0.8f, 0.56f);
    private static readonly Color StalkRoot = new(0.55f, 0.48f, 0.3f);
    private static readonly Color StalkTip = new(0.78f, 0.68f, 0.45f);
    private static readonly Color HeadRoot = new(0.8f, 0.68f, 0.42f);
    private static readonly Color HeadTip = new(0.92f, 0.82f, 0.56f);
    private static readonly Color LeafRoot = new(0.26f, 0.32f, 0.13f);
    private static readonly Color LeafTip = new(0.5f, 0.56f, 0.27f);
    private static readonly Color Blossom = new(0.93f, 0.88f, 0.62f);
    private static readonly Color StemRoot = new(0.38f, 0.31f, 0.2f);
    private static readonly Color StemTip = new(0.62f, 0.54f, 0.37f);
    private static readonly Color Leaflet = new(0.44f, 0.45f, 0.24f);

    private readonly int _width;
    private readonly int _height;
    private readonly float[] _rgba;
    private readonly float[] _cover;
    private readonly Color[] _strokeColor;
    private Pcg32 _rng;
    private int _x0;
    private int _x1;
    private int _minX;
    private int _minY;
    private int _maxX;
    private int _maxY;

    public TuftPainter(int width, int height, Pcg32 rng)
    {
        _width = width;
        _height = height;
        _rgba = new float[width * height * 4];
        _cover = new float[width * height];
        _strokeColor = new Color[width * height];
        _rng = rng;
    }

    private float Ground => _height - 2f;

    private float Tall => _height - 16f;

    /// <summary>Paints variant <paramref name="variant"/> into the cell starting at column <paramref name="cellX"/>.</summary>
    public void Paint(int variant, int cellX, int cellWidth)
    {
        // A margin keeps mipmaps of neighbouring variants from bleeding into each other.
        _x0 = cellX + 3;
        _x1 = cellX + cellWidth - 3;
        float centre = cellX + cellWidth * 0.5f;
        switch (variant)
        {
            case 0:
                Grass(centre, 34, 0.45f, 1f);
                break;
            case 1:
                SeedGrass(centre);
                break;
            case 2:
                Broadleaf(centre);
                break;
            default:
                WiryWeed(centre);
                break;
        }
    }

    /// <summary>
    /// The atlas as sRGB bytes. Clear pixels take their cell's average colour, so filtering at a blade's
    /// edge never pulls in black.
    /// </summary>
    public byte[] ToBytes(int cellWidth)
    {
        var bytes = new byte[_width * _height * 4];
        for (int cell = 0; cell * cellWidth < _width; cell++)
        {
            float r = 0f, g = 0f, b = 0f, weight = 0f;
            for (int y = 0; y < _height; y++)
            {
                for (int x = cell * cellWidth; x < (cell + 1) * cellWidth; x++)
                {
                    int i = (y * _width + x) * 4;
                    r += _rgba[i];
                    g += _rgba[i + 1];
                    b += _rgba[i + 2];
                    weight += _rgba[i + 3];
                }
            }

            var average = weight > 0f ? new Color(r / weight, g / weight, b / weight) : GrassTip;
            for (int y = 0; y < _height; y++)
            {
                for (int x = cell * cellWidth; x < (cell + 1) * cellWidth; x++)
                {
                    int i = (y * _width + x) * 4;
                    float a = _rgba[i + 3];
                    Color c = a > 0.004f ? new Color(_rgba[i] / a, _rgba[i + 1] / a, _rgba[i + 2] / a) : average;
                    bytes[i] = ToByte(c.R);
                    bytes[i + 1] = ToByte(c.G);
                    bytes[i + 2] = ToByte(c.B);
                    bytes[i + 3] = ToByte(a);
                }
            }
        }

        return bytes;
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);

    private void Grass(float centre, int blades, float minHeight, float maxHeight)
    {
        for (int i = 0; i < blades; i++)
        {
            float x = centre + (_rng.NextFloat() + _rng.NextFloat() - 1f) * 34f;
            float lean = _rng.Symmetric(0.4f) + (x - centre) / 64f * 0.35f;
            Blade(new Vector2(x, Ground), lean, _rng.Range(minHeight, maxHeight) * Tall, _rng.Range(2.6f, 4.2f), 0.7f, Shade(GrassRoot), Shade(GrassTip));
        }
    }

    /// <summary>Short grass with a few tall stalks ending in seed heads.</summary>
    private void SeedGrass(float centre)
    {
        Grass(centre, 12, 0.3f, 0.65f);
        for (int i = 0; i < 4; i++)
        {
            var root = new Vector2(centre + _rng.Symmetric(18f), Ground);
            float lean = _rng.Symmetric(0.25f);
            float height = _rng.Range(0.8f, 0.98f) * Tall;
            Vector2 control = Clamp(root + Along(lean * 0.4f) * height * 0.5f);
            Vector2 tip = Clamp(root + Along(lean) * height);
            Stroke(root, control, tip, t => Mathf.Lerp(1.9f, 1.2f, t), Shade(StalkRoot), Shade(StalkTip));
            Vector2 end = (tip - control).Normalized();
            Vector2 start = tip - end * 26f;
            Stroke(start, (start + tip) * 0.5f, Clamp(tip + end * 4f), t => 1.2f + 5f * MathF.Pow(MathF.Sin(MathF.PI * t), 0.7f),
                Shade(HeadRoot), Shade(HeadTip));
        }
    }

    /// <summary>A rosette of broad, drooping leaves with a couple of flowering stalks.</summary>
    private void Broadleaf(float centre)
    {
        for (int i = 0; i < 2; i++)
        {
            var root = new Vector2(centre + _rng.Symmetric(10f), Ground);
            float lean = _rng.Symmetric(0.3f);
            float height = _rng.Range(0.7f, 0.95f) * Tall;
            Vector2 control = Clamp(root + Along(lean * 0.3f) * height * 0.5f);
            Vector2 tip = Clamp(root + Along(lean) * height);
            Stroke(root, control, tip, t => Mathf.Lerp(1.8f, 1.1f, t), Shade(LeafRoot), Shade(StalkTip));
            for (int k = 0; k < 4; k++)
            {
                Dot(tip + new Vector2(_rng.Symmetric(6f), _rng.Symmetric(6f)), _rng.Range(2.5f, 3.5f), Shade(Blossom));
            }
        }

        for (int i = 0; i < 8; i++)
        {
            float side = i % 2 == 0 ? 1f : -1f;
            float angle = side * _rng.Range(0.35f, 1.25f);
            float length = _rng.Range(70f, 120f) * (1f - 0.3f * MathF.Abs(angle) / 1.25f);
            var root = new Vector2(centre + _rng.Symmetric(6f), Ground);
            Vector2 tip = root + Along(angle) * length + new Vector2(0f, length * 0.18f * MathF.Abs(MathF.Sin(angle)));
            Vector2 control = root + Along(angle * 0.75f) * length * 0.6f;
            float width = _rng.Range(14f, 22f);
            Stroke(root, Clamp(control), Clamp(tip), t => width * MathF.Pow(MathF.Sin(MathF.PI * MathF.Min(1f, t * 1.05f)), 0.75f) + 1f,
                Shade(LeafRoot), Shade(LeafTip));
        }
    }

    /// <summary>Thin branching stems with small leaflets.</summary>
    private void WiryWeed(float centre)
    {
        for (int i = 0; i < 5; i++)
        {
            var root = new Vector2(centre + _rng.Symmetric(12f), Ground);
            float lean = _rng.Symmetric(0.4f);
            float height = _rng.Range(0.55f, 0.95f) * Tall;
            Vector2 control = Clamp(root + Along(lean * 0.3f) * height * 0.5f);
            Vector2 tip = Clamp(root + Along(lean) * height);
            Stroke(root, control, tip, t => Mathf.Lerp(2.2f, 0.8f, t), Shade(StemRoot), Shade(StemTip));
            for (int k = 0; k < 3; k++)
            {
                float t = _rng.Range(0.35f, 0.85f);
                Vector2 at = Bezier(root, control, tip, t);
                Vector2 tangent = (2f * (1f - t) * (control - root) + 2f * t * (tip - control)).Normalized();
                Vector2 direction = tangent.Rotated((k % 2 == 0 ? 1f : -1f) * _rng.Range(0.5f, 0.9f));
                float length = _rng.Range(25f, 55f);
                Vector2 middle = Clamp(at + direction * length * 0.5f + new Vector2(0f, -length * 0.1f));
                Vector2 end = Clamp(at + direction * length);
                Stroke(at, middle, end, s => Mathf.Lerp(1.4f, 0.6f, s), Shade(StemRoot), Shade(StemTip));
                for (int l = 0; l < 3; l++)
                {
                    Dot(Bezier(at, middle, end, _rng.Range(0.3f, 1f)), _rng.Range(2f, 3.2f), Shade(Leaflet));
                }
            }
        }
    }

    /// <summary>A blade from <paramref name="root"/>, leaning <paramref name="lean"/> radians and curving further over near its tip.</summary>
    private void Blade(Vector2 root, float lean, float height, float rootWidth, float tipWidth, Color rootColor, Color tipColor)
    {
        Vector2 control = Clamp(root + Along(lean * 0.5f) * height * 0.55f);
        Vector2 tip = Clamp(root + Along(lean * 1.4f) * height);
        Stroke(root, control, tip, t => Mathf.Lerp(rootWidth, tipWidth, t), rootColor, tipColor);
    }

    private void Dot(Vector2 at, float radius, Color color) => Stroke(at, at, at, _ => radius * 2f, color, color);

    /// <summary>A quadratic curve whose width (px) and colour vary from start to end.</summary>
    private void Stroke(Vector2 p0, Vector2 p1, Vector2 p2, Func<float, float> width, Color c0, Color c1)
    {
        _minX = int.MaxValue;
        _minY = int.MaxValue;
        _maxX = int.MinValue;
        _maxY = int.MinValue;
        int steps = Math.Max(2, (int)((p0.DistanceTo(p1) + p1.DistanceTo(p2)) * 2f));
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            Disc(Bezier(p0, p1, p2, t), MathF.Max(0.6f, width(t) * 0.5f), c0.Lerp(c1, t));
        }

        for (int y = _minY; y <= _maxY; y++)
        {
            for (int x = _minX; x <= _maxX; x++)
            {
                int p = y * _width + x;
                float a = _cover[p];
                if (a <= 0f)
                {
                    continue;
                }

                Color c = _strokeColor[p];
                int i = p * 4;
                _rgba[i] = c.R * a + _rgba[i] * (1f - a);
                _rgba[i + 1] = c.G * a + _rgba[i + 1] * (1f - a);
                _rgba[i + 2] = c.B * a + _rgba[i + 2] * (1f - a);
                _rgba[i + 3] = a + _rgba[i + 3] * (1f - a);
                _cover[p] = 0f;
            }
        }
    }

    private void Disc(Vector2 centre, float radius, Color color)
    {
        int x0 = Math.Max(_x0, (int)MathF.Floor(centre.X - radius - 1f));
        int x1 = Math.Min(_x1 - 1, (int)MathF.Ceiling(centre.X + radius + 1f));
        int y0 = Math.Max(0, (int)MathF.Floor(centre.Y - radius - 1f));
        int y1 = Math.Min(_height - 1, (int)MathF.Ceiling(centre.Y + radius + 1f));
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float d = new Vector2(x + 0.5f - centre.X, y + 0.5f - centre.Y).Length();
                float cover = Math.Clamp(radius + 0.5f - d, 0f, 1f);
                int p = y * _width + x;
                if (cover > _cover[p])
                {
                    _cover[p] = cover;
                    _strokeColor[p] = color;
                    _minX = Math.Min(_minX, x);
                    _maxX = Math.Max(_maxX, x);
                    _minY = Math.Min(_minY, y);
                    _maxY = Math.Max(_maxY, y);
                }
            }
        }
    }

    private Color Shade(Color c)
    {
        Color shaded = c * _rng.Range(0.85f, 1.15f);
        shaded.A = 1f;
        return shaded;
    }

    private Vector2 Clamp(Vector2 p) => new(Math.Clamp(p.X, _x0 + 1f, _x1 - 1f), Math.Clamp(p.Y, 2f, _height - 1f));

    /// <summary>Unit vector <paramref name="lean"/> radians off straight up (y is down in the image).</summary>
    private static Vector2 Along(float lean) => new(MathF.Sin(lean), -MathF.Cos(lean));

    private static Vector2 Bezier(Vector2 p0, Vector2 p1, Vector2 p2, float t)
    {
        float u = 1f - t;
        return u * u * p0 + 2f * u * t * p1 + t * t * p2;
    }
}
