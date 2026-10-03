using System;
using Godot;

namespace Pb.Game.World;

/// <summary>What a ground-detail card shows; each has two painted variants in the atlas.</summary>
public enum GroundDetailKind
{
    /// <summary>A dark, glossy oil stain with drips round it.</summary>
    Oil,
    /// <summary>Standing water: a mirror-smooth middle in a damp ring.</summary>
    Puddle,
    /// <summary>A broad patch of damp ground.</summary>
    Damp,
    /// <summary>Orange-brown rust run-off.</summary>
    Rust,
    /// <summary>Two tyre tracks with tread, fading at the ends (a long card).</summary>
    Tracks,
    /// <summary>A drift of dry leaves.</summary>
    Leaves,
    /// <summary>Scraps of paper, card and a crushed can (nothing printed on them).</summary>
    Litter,
    /// <summary>Broken bits of concrete and brick.</summary>
    Chips,
}

/// <summary>
/// Paints the ground-detail atlas for <see cref="GroundDetail"/>: a 4 × 4 grid of cells, two per
/// <see cref="GroundDetailKind"/>, each a colour with coverage, plus a surface map (red: roughness;
/// green: wetness) so oil and water shine. Clear pixels take their cell's average colour, so
/// mipmaps never pull a dark fringe in at a card's edge.
/// </summary>
public sealed class StainPainter
{
    public const int Columns = 4;

    public const int CellPx = 256;

    private readonly float[] _r = new float[CellPx * CellPx];
    private readonly float[] _g = new float[CellPx * CellPx];
    private readonly float[] _b = new float[CellPx * CellPx];
    private readonly float[] _a = new float[CellPx * CellPx];
    private readonly float[] _rough = new float[CellPx * CellPx];
    private readonly float[] _wet = new float[CellPx * CellPx];
    private readonly byte[] _albedo;
    private readonly byte[] _surface;
    private readonly int _size;
    private uint _seed;
    private Random _random = new(1);

    public StainPainter(uint seed)
    {
        _seed = seed;
        _size = CellPx * Columns;
        _albedo = new byte[_size * _size * 4];
        _surface = new byte[_size * _size * 4];
    }

    /// <summary>The atlas cell of a kind's variant (0 or 1).</summary>
    public static int Cell(GroundDetailKind kind, int variant) => (int)kind * 2 + (variant & 1);

    /// <summary>Paints every cell and returns the colour and surface atlases.</summary>
    public (ImageTexture Albedo, ImageTexture Surface) Paint()
    {
        foreach (GroundDetailKind kind in Enum.GetValues<GroundDetailKind>())
        {
            for (int variant = 0; variant < 2; variant++)
            {
                Clear();
                _seed = _seed * 747796405u + 2891336453u;
                _random = new Random((int)(_seed & 0x7fffffff));
                switch (kind)
                {
                    case GroundDetailKind.Oil: Oil(); break;
                    case GroundDetailKind.Puddle: Puddle(); break;
                    case GroundDetailKind.Damp: Damp(); break;
                    case GroundDetailKind.Rust: Rust(); break;
                    case GroundDetailKind.Tracks: Tracks(); break;
                    case GroundDetailKind.Leaves: Leaves(); break;
                    case GroundDetailKind.Litter: Litter(); break;
                    default: Chips(); break;
                }

                Store(Cell(kind, variant));
            }
        }

        Image albedo = Image.CreateFromData(_size, _size, false, Image.Format.Rgba8, _albedo);
        albedo.GenerateMipmaps();
        Image surface = Image.CreateFromData(_size, _size, false, Image.Format.Rgba8, _surface);
        surface.GenerateMipmaps();
        return (ImageTexture.CreateFromImage(albedo), ImageTexture.CreateFromImage(surface));
    }

    // ---------------------------------------------------------------- kinds

    private void Oil()
    {
        float squash = R(0.7f, 1f);
        Each((u, v) =>
        {
            float d = Warped(u / squash, v, 2.2f, 0.32f);
            float mask = 1f - Smooth(0.42f, 0.58f, d);
            float core = 1f - Smooth(0.05f, 0.42f, d);
            return (mask, Mix(new Color(0.06f, 0.05f, 0.045f), new Color(0.02f, 0.02f, 0.025f), core), mask * (0.65f + 0.3f * core), 0.15f + 0.4f * (1f - core), 0.6f);
        });
        // Drips and splashes round it.
        int drips = _random.Next(3, 8);
        for (int i = 0; i < drips; i++)
        {
            float a = R(0f, Mathf.Tau), r = R(0.5f, 0.8f), size = R(0.03f, 0.09f);
            Blob(Mathf.Cos(a) * r, Mathf.Sin(a) * r, size, new Color(0.04f, 0.035f, 0.03f), 0.8f, 0.2f, 0.5f);
        }
    }

    private void Puddle()
    {
        Each((u, v) =>
        {
            float d = Warped(u, v, 1.6f, 0.28f);
            float mask = 1f - Smooth(0.6f, 0.9f, d);
            float water = 1f - Smooth(0.48f, 0.53f, d);
            Color col = Mix(new Color(0.14f, 0.13f, 0.12f), new Color(0.05f, 0.055f, 0.06f), water);
            return (mask, col, mask * Mathf.Lerp(0.45f, 0.8f, water), Mathf.Lerp(0.42f, 0.03f, water), Mathf.Lerp(0.6f, 1f, water));
        });
    }

    private void Damp()
    {
        Each((u, v) =>
        {
            float mask = (1f - Smooth(0.15f, 0.95f, Warped(u, v, 1.3f, 0.4f))) * (0.6f + 0.4f * Fbm(u * 3f, v * 3f, 11));
            return (mask, new Color(0.1f, 0.095f, 0.085f), mask * 0.55f, 0.5f, 0.5f);
        });
    }

    private void Rust()
    {
        Each((u, v) =>
        {
            float mask = (1f - Smooth(0.35f, 0.75f, Warped(u, v, 2.6f, 0.38f))) * (0.55f + 0.45f * Fbm(u * 5f, v * 5f, 23));
            float t = Fbm(u * 7f, v * 7f, 29);
            return (mask, Mix(new Color(0.45f, 0.21f, 0.08f), new Color(0.27f, 0.12f, 0.05f), t), mask * 0.7f, 0.92f, 0f);
        });
    }

    private void Tracks()
    {
        // Two tyres 1.5 m apart on a 2 m wide card (u is across, v along), tread every few centimetres.
        float wander = R(-0.04f, 0.04f);
        Each((u, v) =>
        {
            float bend = wander * v * v;
            float band = MathF.Max(1f - Smooth(0.07f, 0.11f, MathF.Abs(u - 0.72f - bend)), 1f - Smooth(0.07f, 0.11f, MathF.Abs(u + 0.72f - bend)));
            float ends = 1f - Smooth(0.6f, 0.98f, MathF.Abs(v));
            float tread = 0.65f + 0.35f * MathF.Sin(v * 90f + MathF.Sin(u * 20f));
            float mask = band * ends * tread * (0.45f + 0.55f * Fbm(u * 2f, v * 6f, 31));
            return (mask, new Color(0.06f, 0.055f, 0.05f), mask * 0.6f, 0.8f, 0f);
        });
    }

    private void Leaves()
    {
        int count = _random.Next(30, 48);
        Color[] tones = { new(0.36f, 0.22f, 0.1f), new(0.52f, 0.37f, 0.14f), new(0.24f, 0.16f, 0.08f), new(0.45f, 0.4f, 0.18f) };
        for (int i = 0; i < count; i++)
        {
            float r = MathF.Sqrt(R(0f, 1f)) * 0.7f, a = R(0f, Mathf.Tau);
            Leaf(Mathf.Cos(a) * r, Mathf.Sin(a) * r, R(0.05f, 0.1f), R(0.022f, 0.045f), R(0f, Mathf.Tau), tones[_random.Next(tones.Length)] * R(0.8f, 1.15f));
        }
    }

    private void Litter()
    {
        int count = _random.Next(3, 7);
        for (int i = 0; i < count; i++)
        {
            float x = R(-0.6f, 0.6f), y = R(-0.6f, 0.6f), turn = R(0f, Mathf.Tau);
            switch (_random.Next(4))
            {
                case 0:
                case 1:
                    // A sheet of paper, crumpled into facets.
                    Sheet(x, y, R(0.12f, 0.24f), R(0.09f, 0.17f), turn, new Color(0.78f, 0.77f, 0.72f) * R(0.85f, 1.05f), 0.85f);
                    break;
                case 2:
                    Sheet(x, y, R(0.14f, 0.3f), R(0.1f, 0.2f), turn, new Color(0.48f, 0.37f, 0.24f), 0.9f);
                    break;
                default:
                    // A crushed can: a silver flattened oval with a coloured band, nothing printed.
                    Ellipse(x, y, R(0.07f, 0.09f), R(0.035f, 0.045f), turn, new Color(0.62f, 0.63f, 0.64f), 0.35f);
                    Ellipse(x, y, R(0.035f, 0.05f), R(0.03f, 0.042f), turn, new Color(0.5f, 0.1f, 0.08f) * R(0.6f, 1f), 0.5f);
                    break;
            }
        }
    }

    private void Chips()
    {
        int count = _random.Next(22, 40);
        for (int i = 0; i < count; i++)
        {
            float r = MathF.Sqrt(R(0f, 1f)) * 0.75f, a = R(0f, Mathf.Tau);
            Color tone = _random.NextDouble() < 0.3 ? new Color(0.45f, 0.22f, 0.15f) : new Color(0.55f, 0.54f, 0.5f);
            Chip(Mathf.Cos(a) * r, Mathf.Sin(a) * r, R(0.025f, 0.07f), tone * R(0.75f, 1.1f));
        }
    }

    // ---------------------------------------------------------------- strokes

    /// <summary>
    /// Paints every pixel from <paramref name="paint"/>: (coverage before the cell's edge fade, colour,
    /// alpha, roughness, wetness), so each pixel's noise is worked out once.
    /// </summary>
    private void Each(Func<float, float, (float Mask, Color Color, float Alpha, float Rough, float Wet)> paint)
    {
        for (int py = 0; py < CellPx; py++)
        {
            for (int px = 0; px < CellPx; px++)
            {
                (float u, float v) = Uv(px, py);
                float edge = Edge(u, v);
                if (edge <= 0f)
                {
                    continue;
                }

                (float mask, Color c, float alpha, float rough, float wet) = paint(u, v);
                if (mask * edge <= 0.002f)
                {
                    continue;
                }

                Over(py * CellPx + px, c, Mathf.Clamp(alpha * edge, 0f, 1f), rough, wet);
            }
        }
    }

    private void Blob(float cx, float cy, float radius, Color color, float alpha, float rough, float wet)
    {
        Stamp(cx, cy, radius * 1.6f, (u, v) => 1f - Smooth(radius * 0.7f, radius, Warped((u - cx) / radius, (v - cy) / radius, 3f, 0.25f) * radius),
            color, alpha, rough, wet);
    }

    private void Leaf(float cx, float cy, float length, float width, float turn, Color color)
    {
        float c = Mathf.Cos(turn), s = Mathf.Sin(turn);
        float curl = R(-0.3f, 0.3f);
        Stamp(cx, cy, length, (u, v) =>
        {
            float a = (u - cx) * c + (v - cy) * s, b = -(u - cx) * s + (v - cy) * c;
            float t = a / length;
            b -= curl * t * t * length;
            // Widest a third of the way along, pointed at the tip.
            float half = width * MathF.Max(0f, 1f - MathF.Abs(t + 0.15f) / 0.85f) * (t < -0.7f ? 0.4f : 1f);
            float inside = 1f - Smooth(half * 0.75f, half + 0.002f, MathF.Abs(b));
            return inside * (MathF.Abs(t) <= 1f ? 1f : 0f);
        }, color, 1f, 0.8f, 0f, (u, v) => 0.85f + 0.15f * Fbm(u * 40f, v * 40f, 3));
    }

    private void Sheet(float cx, float cy, float halfW, float halfH, float turn, Color color, float rough)
    {
        float c = Mathf.Cos(turn), s = Mathf.Sin(turn);
        Stamp(cx, cy, MathF.Max(halfW, halfH) * 1.5f, (u, v) =>
        {
            float a = (u - cx) * c + (v - cy) * s, b = -(u - cx) * s + (v - cy) * c;
            float ragged = 0.012f * (Fbm(a * 30f, b * 30f, 7) - 0.5f);
            return (1f - Smooth(halfW - 0.006f, halfW, MathF.Abs(a) + ragged)) * (1f - Smooth(halfH - 0.006f, halfH, MathF.Abs(b) + ragged));
        }, color, 1f, rough, 0f, (u, v) => 0.72f + 0.38f * Facets(u * 9f, v * 9f));
    }

    private void Ellipse(float cx, float cy, float rx, float ry, float turn, Color color, float rough)
    {
        float c = Mathf.Cos(turn), s = Mathf.Sin(turn);
        Stamp(cx, cy, MathF.Max(rx, ry) * 1.2f, (u, v) =>
        {
            float a = ((u - cx) * c + (v - cy) * s) / rx, b = (-(u - cx) * s + (v - cy) * c) / ry;
            return 1f - Smooth(0.85f, 1f, MathF.Sqrt(a * a + b * b));
        }, color, 1f, rough, 0f, (u, v) => 0.7f + 0.45f * Facets(u * 25f, v * 25f));
    }

    private void Chip(float cx, float cy, float size, Color color)
    {
        // A rough polygon: a circle with a jagged radius, shaded darker towards one side.
        int seed = _random.Next();
        float turn = R(0f, Mathf.Tau);
        Stamp(cx, cy, size * 1.4f, (u, v) =>
        {
            float a = MathF.Atan2(v - cy, u - cx);
            float r = MathF.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy));
            float edge = size * (0.75f + 0.35f * Fbm(MathF.Cos(a) * 1.6f + 5f, MathF.Sin(a) * 1.6f + 5f, (uint)seed));
            return 1f - Smooth(edge * 0.9f, edge, r);
        }, color, 1f, 0.9f, 0f, (u, v) => 0.75f + 0.35f * MathF.Cos(MathF.Atan2(v - cy, u - cx) - turn) * 0.5f + 0.15f * Fbm(u * 50f, v * 50f, 9));
    }

    /// <summary>Lays a shape over the cell within <paramref name="reach"/> of (cx, cy); <paramref name="shade"/> darkens or lightens it per pixel.</summary>
    private void Stamp(float cx, float cy, float reach, Func<float, float, float> mask, Color color, float alpha, float rough, float wet,
        Func<float, float, float>? shade = null)
    {
        int x0 = Math.Max(0, Px(cx - reach)), x1 = Math.Min(CellPx - 1, Px(cx + reach));
        int y0 = Math.Max(0, Px(cy - reach)), y1 = Math.Min(CellPx - 1, Px(cy + reach));
        for (int py = y0; py <= y1; py++)
        {
            for (int px = x0; px <= x1; px++)
            {
                (float u, float v) = Uv(px, py);
                float m = Mathf.Clamp(mask(u, v), 0f, 1f) * Edge(u, v);
                if (m <= 0.002f)
                {
                    continue;
                }

                float k = shade?.Invoke(u, v) ?? 1f;
                Over(py * CellPx + px, new Color(color.R * k, color.G * k, color.B * k), alpha * m, rough, wet);
            }
        }
    }

    /// <summary>Lays a colour over pixel <paramref name="i"/> with coverage <paramref name="alpha"/>.</summary>
    private void Over(int i, Color c, float alpha, float rough, float wet)
    {
        float a0 = _a[i];
        float a = alpha + a0 * (1f - alpha);
        if (a <= 0f)
        {
            return;
        }

        float w = alpha / a;
        _r[i] = Mathf.Lerp(_r[i], c.R, w);
        _g[i] = Mathf.Lerp(_g[i], c.G, w);
        _b[i] = Mathf.Lerp(_b[i], c.B, w);
        _rough[i] = Mathf.Lerp(_rough[i], rough, w);
        _wet[i] = Mathf.Lerp(_wet[i], wet, w);
        _a[i] = a;
    }

    private void Clear()
    {
        Array.Clear(_r);
        Array.Clear(_g);
        Array.Clear(_b);
        Array.Clear(_a);
        Array.Fill(_rough, 0.9f);
        Array.Clear(_wet);
    }

    /// <summary>Copies the cell into the atlases; clear pixels get the cell's average colour for clean mipmaps.</summary>
    private void Store(int cell)
    {
        double sr = 0, sg = 0, sb = 0, sa = 0;
        for (int i = 0; i < _a.Length; i++)
        {
            sr += _r[i] * _a[i];
            sg += _g[i] * _a[i];
            sb += _b[i] * _a[i];
            sa += _a[i];
        }

        var mean = sa > 0 ? new Color((float)(sr / sa), (float)(sg / sa), (float)(sb / sa)) : new Color(0.1f, 0.1f, 0.1f);
        int ox = cell % Columns * CellPx, oy = cell / Columns * CellPx;
        for (int py = 0; py < CellPx; py++)
        {
            for (int px = 0; px < CellPx; px++)
            {
                int i = py * CellPx + px;
                int o = ((oy + py) * _size + ox + px) * 4;
                float a = _a[i];
                // Colours are sRGB, like any other colour texture.
                Color c = a > 0.01f ? new Color(_r[i], _g[i], _b[i]) : mean;
                _albedo[o] = ToByte(c.R);
                _albedo[o + 1] = ToByte(c.G);
                _albedo[o + 2] = ToByte(c.B);
                _albedo[o + 3] = ToByte(a);
                _surface[o] = ToByte(_rough[i]);
                _surface[o + 1] = ToByte(_wet[i]);
                _surface[o + 2] = 0;
                _surface[o + 3] = 255;
            }
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Cell pixel → (u, v) in −1…1.</summary>
    private static (float U, float V) Uv(int px, int py) => ((px + 0.5f) / CellPx * 2f - 1f, (py + 0.5f) / CellPx * 2f - 1f);

    private static int Px(float u) => (int)((u + 1f) * 0.5f * CellPx);

    /// <summary>Fades everything out before the cell's border, so neighbouring cells never bleed in.</summary>
    private static float Edge(float u, float v) => 1f - Smooth(0.9f, 0.97f, MathF.Max(MathF.Abs(u), MathF.Abs(v)));

    /// <summary>Distance from the middle, pushed about by noise: an irregular blob's edge at about 0.5.</summary>
    private float Warped(float u, float v, float scale, float amount)
    {
        float wu = Fbm(u * scale + 3.1f, v * scale + 1.7f, _seed) - 0.5f;
        float wv = Fbm(u * scale + 8.3f, v * scale + 5.9f, _seed + 1) - 0.5f;
        float x = u + wu * amount * 2f, y = v + wv * amount * 2f;
        return MathF.Sqrt(x * x + y * y);
    }

    private float Fbm(float x, float y, uint salt)
    {
        float sum = 0f, amplitude = 0.5f;
        for (int i = 0; i < 3; i++)
        {
            sum += amplitude * Noise(x, y, _seed ^ (salt * 0x9E3779B9u) + (uint)i * 101u);
            x = x * 2.03f + 7.1f;
            y = y * 2.03f + 3.7f;
            amplitude *= 0.5f;
        }

        return sum / 0.875f;
    }

    /// <summary>Crumpled facets: flat shades in cells of a jittered grid.</summary>
    private float Facets(float x, float y)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float best = float.MaxValue;
        float shade = 0.5f;
        for (int j = -1; j <= 1; j++)
        {
            for (int i = -1; i <= 1; i++)
            {
                float fx = ix + i + Hash(ix + i, iy + j, _seed + 13), fy = iy + j + Hash(ix + i, iy + j, _seed + 17);
                float d = (fx - x) * (fx - x) + (fy - y) * (fy - y);
                if (d < best)
                {
                    best = d;
                    shade = Hash(ix + i, iy + j, _seed + 19);
                }
            }
        }

        return shade;
    }

    private static float Noise(float x, float y, uint seed)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float top = Mathf.Lerp(Hash(ix, iy, seed), Hash(ix + 1, iy, seed), fx);
        float bottom = Mathf.Lerp(Hash(ix, iy + 1, seed), Hash(ix + 1, iy + 1, seed), fx);
        return Mathf.Lerp(top, bottom, fy);
    }

    private static float Hash(int x, int y, uint seed)
    {
        unchecked
        {
            uint h = ((uint)x * 0x8da6b343u) ^ ((uint)y * 0xd8163841u) ^ (seed * 0xcb1ab31fu);
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return (h >> 8) * (1f / 16777216f);
        }
    }

    private static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static Color Mix(Color a, Color b, float t) => a.Lerp(b, Mathf.Clamp(t, 0f, 1f));

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    private float R(float a, float b) => a + (float)_random.NextDouble() * (b - a);
}
