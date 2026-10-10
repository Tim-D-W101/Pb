using System;
using Godot;
using Pb.Game.Core;

namespace Pb.Game.Ballistics;

/// <summary>
/// The paint splats as they land (Phase 5, M5.4), painted once when the game starts. Each shape has a thick middle
/// standing proud of the surface, fingers thrown out round it, drops beyond, and flecks of the ball's shell lying in
/// it. For each there's its colour's coverage and shade (white, tinted by the team colour), a normal map from its
/// thickness, and its roughness wet (glossy, thickest where the paint is) and dry (satin): world decals use those, the
/// splats painted in players' and gear's shaders read <see cref="Atlas"/> (coverage, thickness and flecks in one
/// texture, the shapes in a 4 × 2 grid). Shapes 0–4 are round splats, 5 and 6 glancing ones sprayed one way, 7 a
/// spatter of small drops (what lands on you from a break close by).
/// </summary>
public sealed class WetSplats
{
    public const int Shapes = 8;

    public const int Columns = 4;

    public const int Rows = 2;

    /// <summary>The spatter of small drops.</summary>
    public const int Spatter = 7;

    private const int Size = 128;

    private static WetSplats? _painted;
    private readonly float[] _cover = new float[Size * Size];
    private readonly float[] _height = new float[Size * Size];
    private readonly float[] _fleck = new float[Size * Size];
    private Random _random = new(1);

    private WetSplats(float wetRoughness, float dryRoughness, float bump)
    {
        var atlas = new byte[Size * Columns * Size * Rows * 4];
        for (int shape = 0; shape < Shapes; shape++)
        {
            Array.Clear(_cover);
            Array.Clear(_height);
            Array.Clear(_fleck);
            _random = new Random(9173 + shape * 7919);
            switch (shape)
            {
                case 5:
                case 6:
                    Glancing(shape == 5 ? 0.35f : 3.6f);
                    break;
                case Spatter:
                    for (int i = _random.Next(5, 9); i > 0; i--)
                    {
                        Drop(R(-0.6f, 0.6f), R(-0.6f, 0.6f), R(0.05f, 0.16f));
                    }

                    break;
                default:
                    Splat(R(-0.04f, 0.04f), R(-0.04f, 0.04f), R(0.4f, 0.47f));
                    break;
            }

            Flecks(shape == Spatter ? 0 : _random.Next(5, 10));
            Albedo[shape] = Texture(Image.Format.Rgba8, 4, (i, o, px) =>
            {
                // White, tinted by the team colour: the thick middle a shade richer, the shell's flecks lighter.
                float shade = 0.9f - 0.12f * _height[i] + 0.22f * _fleck[i];
                byte s = Byte(_cover[i] > 0.01f ? shade : 0.85f);
                px[o] = s;
                px[o + 1] = s;
                px[o + 2] = s;
                px[o + 3] = Byte(_cover[i]);
            });
            Normal[shape] = Texture(Image.Format.Rgb8, 3, (i, o, px) =>
            {
                int x = i % Size, y = i / Size;
                float dx = Height(x + 1, y) - Height(x - 1, y), dy = Height(x, y + 1) - Height(x, y - 1);
                var n = new Vector3(-dx * bump * 6f, -dy * bump * 6f, 1f).Normalized();
                px[o] = Byte(n.X * 0.5f + 0.5f);
                px[o + 1] = Byte(n.Y * 0.5f + 0.5f);
                px[o + 2] = Byte(n.Z * 0.5f + 0.5f);
            });
            WetOrm[shape] = Orm(wetRoughness, 0.18f);
            DryOrm[shape] = Orm(dryRoughness, 0.42f);

            // The shaders' atlas: coverage, thickness and flecks.
            int ox = shape % Columns * Size, oy = shape / Columns * Size;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x;
                    int o = ((oy + y) * Size * Columns + ox + x) * 4;
                    atlas[o] = Byte(_cover[i]);
                    atlas[o + 1] = Byte(_height[i]);
                    atlas[o + 2] = Byte(_fleck[i]);
                    atlas[o + 3] = 255;
                }
            }
        }

        Image image = Image.CreateFromData(Size * Columns, Size * Rows, false, Image.Format.Rgba8, atlas);
        image.GenerateMipmaps();
        Atlas = ImageTexture.CreateFromImage(image);
    }

    /// <summary>Each shape's coverage (alpha) and shade, for the world's decals.</summary>
    public ImageTexture[] Albedo { get; } = new ImageTexture[Shapes];

    /// <summary>Each shape's normal map, from the paint's thickness.</summary>
    public ImageTexture[] Normal { get; } = new ImageTexture[Shapes];

    /// <summary>Each shape's occlusion, roughness and metal wet, and dry.</summary>
    public ImageTexture[] WetOrm { get; } = new ImageTexture[Shapes];

    public ImageTexture[] DryOrm { get; } = new ImageTexture[Shapes];

    /// <summary>Every shape for the shaders: red its coverage, green its thickness, blue the shell's flecks.</summary>
    public ImageTexture Atlas { get; }

    /// <summary>The shapes, painted the first time they're asked for (the first view's roughness and bump hold after).</summary>
    public static WetSplats Get(SplatDef def) => _painted ??= new WetSplats(def.WetRoughness, def.DryRoughness, def.Bump);

    private ImageTexture Orm(float roughness, float rim)
    {
        return Texture(Image.Format.Rgb8, 3, (i, o, px) =>
        {
            // Glossiest where the paint lies thickest; the shell's flecks a little less so.
            float r = Mathf.Lerp(rim, roughness, Mathf.Clamp(_height[i] * 1.6f, 0f, 1f)) + 0.12f * _fleck[i];
            px[o] = 255;
            px[o + 1] = Byte(r);
            px[o + 2] = 0;
        });
    }

    private ImageTexture Texture(Image.Format format, int channels, Action<int, int, byte[]> pixel)
    {
        var pixels = new byte[Size * Size * channels];
        for (int i = 0; i < Size * Size; i++)
        {
            pixel(i, i * channels, pixels);
        }

        Image image = Image.CreateFromData(Size, Size, false, format, pixels);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    // ---------------------------------------------------------------- shapes (u right, v down, −1 to 1 across the cell)

    /// <summary>A ball's splat: a ragged, thick middle, fingers thrown out from it, drops beyond.</summary>
    private void Splat(float cx, float cy, float radius)
    {
        int fingers = _random.Next(8, 15);
        var angles = new float[fingers];
        var lengths = new float[fingers];
        var widths = new float[fingers];
        for (int i = 0; i < fingers; i++)
        {
            angles[i] = Mathf.Tau * (i + R(-0.35f, 0.35f)) / fingers;
            lengths[i] = radius * R(0.35f, 0.95f) * (R(0f, 1f) < 0.25f ? 1.35f : 1f);
            widths[i] = R(0.12f, 0.26f);
        }

        float wobbleSeed = R(0f, 50f);
        Stamp(cx, cy, radius * 2.6f, (x, y) =>
        {
            float d = MathF.Sqrt(x * x + y * y);
            float a = MathF.Atan2(y, x);
            float wobble = 1f + 0.16f * (Fbm(MathF.Cos(a) * 2.2f + wobbleSeed, MathF.Sin(a) * 2.2f) - 0.5f) * 2f;
            float body = 1f - Smooth(radius * 0.9f, radius * 1.02f, d / wobble);
            float finger = 0f;
            for (int i = 0; i < fingers; i++)
            {
                float da = MathF.Abs(Mathf.Wrap(a - angles[i], -Mathf.Pi, Mathf.Pi));
                float t = (d - radius * 0.8f) / lengths[i];
                if (t < 0f || t > 1f)
                {
                    continue;
                }

                float halfWidth = widths[i] * (1f - t) * radius / MathF.Max(d, 1e-3f);
                finger = MathF.Max(finger, 1f - Smooth(halfWidth * 0.75f, halfWidth, da));
            }

            // The middle stands thickest, thinning to the rim; the fingers are thin.
            float thick = body * (0.35f + 0.65f * (1f - Smooth(0f, radius * 0.95f, d))) + finger * (1f - body) * 0.3f;
            return (MathF.Max(body, finger), thick + 0.06f * (Fbm(x * 8f, y * 8f) - 0.5f));
        });

        for (int i = _random.Next(8, 20); i > 0; i--)
        {
            float a = R(0f, 1f) < 0.6f ? angles[_random.Next(fingers)] + R(-0.15f, 0.15f) : R(0f, Mathf.Tau);
            float r = radius * R(1.15f, 1.9f);
            Drop(cx + MathF.Cos(a) * r, cy + MathF.Sin(a) * r, radius * R(0.03f, 0.09f));
        }
    }

    /// <summary>A ball that hit at an angle: a long splat sprayed out one way (<paramref name="turn"/>), thickest where it struck.</summary>
    private void Glancing(float turn)
    {
        float c = MathF.Cos(turn), s = MathF.Sin(turn);
        float radius = R(0.2f, 0.25f);
        Stamp(-c * radius, -s * radius, 1.4f, (x, y) =>
        {
            float a = x * c + y * s, b = -x * s + y * c;
            float along = a < 0f ? a / (radius * 1.1f) : a / (radius * 3.1f);
            float across = b / (radius * (1f - 0.55f * Mathf.Clamp(a / (radius * 3.1f), 0f, 1f)));
            float d = MathF.Sqrt(along * along + across * across) * (1f + 0.25f * (Fbm(x * 5f, y * 5f) - 0.5f) * 2f);
            float coverage = 1f - Smooth(0.85f, 1f, d);
            float thick = coverage * (0.3f + 0.7f * (1f - Smooth(-0.2f, 0.9f, along)));
            return (coverage, thick);
        });

        for (int i = _random.Next(14, 30); i > 0; i--)
        {
            float a = radius * R(2.1f, 3.6f), b = radius * R(-1.3f, 1.3f) * a / (radius * 3.6f);
            Drop(a * c - b * s - c * radius, a * s + b * c - s * radius, radius * R(0.04f, 0.11f));
        }
    }

    /// <summary>A round drop of paint, domed.</summary>
    private void Drop(float cx, float cy, float radius) =>
        Stamp(cx, cy, radius * 1.4f, (x, y) =>
        {
            float d = MathF.Sqrt(x * x + y * y) / radius;
            return (1f - Smooth(0.7f, 1f, d), 0.65f * MathF.Sqrt(MathF.Max(0f, 1f - d * d)));
        });

    /// <summary>Bits of the ball's shell: small angular flakes where the paint lies, standing a little proud of it.</summary>
    private void Flecks(int count)
    {
        for (int i = 0; i < count; i++)
        {
            float a = R(0f, Mathf.Tau), r = R(0.02f, 0.4f);
            float cx = MathF.Cos(a) * r, cy = MathF.Sin(a) * r;
            if (Cover(cx, cy) < 0.5f)
            {
                continue;
            }

            float size = R(0.025f, 0.06f), turn = R(0f, Mathf.Pi), stretch = R(1.3f, 2.4f);
            float ct = MathF.Cos(turn), st = MathF.Sin(turn);
            int x0 = Px(cx - size * 2f), x1 = Px(cx + size * 2f), y0 = Px(cy - size * 2f), y1 = Px(cy + size * 2f);
            for (int py = Math.Max(0, y0); py <= Math.Min(Size - 1, y1); py++)
            {
                for (int px = Math.Max(0, x0); px <= Math.Min(Size - 1, x1); px++)
                {
                    (float u, float v) = Uv(px, py);
                    float du = u - cx, dv = v - cy;
                    // A rough quadrilateral: a stretched diamond, its corners clipped.
                    float p = MathF.Abs(du * ct + dv * st) / (size * stretch), q = MathF.Abs(-du * st + dv * ct) / size;
                    float flake = 1f - Smooth(0.8f, 1f, MathF.Max(p + q * 0.6f, q));
                    int k = py * Size + px;
                    _fleck[k] = MathF.Max(_fleck[k], flake * _cover[k]);
                    _height[k] = MathF.Min(1f, _height[k] + 0.25f * flake * _cover[k]);
                }
            }
        }
    }

    private float Cover(float u, float v)
    {
        int px = Math.Clamp(Px(u), 0, Size - 1), py = Math.Clamp(Px(v), 0, Size - 1);
        return _cover[py * Size + px];
    }

    private float Height(int x, int y) => _height[Math.Clamp(y, 0, Size - 1) * Size + Math.Clamp(x, 0, Size - 1)];

    /// <summary>Lays a shape over the cell: <paramref name="shape"/> gives coverage and thickness at an offset from (cx, cy).</summary>
    private void Stamp(float cx, float cy, float reach, Func<float, float, (float Coverage, float Thick)> shape)
    {
        int x0 = Px(cx - reach), x1 = Px(cx + reach), y0 = Px(cy - reach), y1 = Px(cy + reach);
        for (int py = Math.Max(0, y0); py <= Math.Min(Size - 1, y1); py++)
        {
            for (int px = Math.Max(0, x0); px <= Math.Min(Size - 1, x1); px++)
            {
                (float u, float v) = Uv(px, py);
                // Nothing reaches the cell's edge, so mipmaps and the atlas's neighbours stay clean.
                float edge = 1f - Smooth(0.86f, 0.96f, MathF.Max(MathF.Abs(u), MathF.Abs(v)));
                (float coverage, float thick) = shape(u - cx, v - cy);
                float a = Mathf.Clamp(coverage * edge, 0f, 1f);
                if (a <= 0.002f)
                {
                    continue;
                }

                int i = py * Size + px;
                _cover[i] = _cover[i] + a * (1f - _cover[i]);
                _height[i] = MathF.Max(_height[i], Mathf.Clamp(thick, 0f, 1f) * a);
            }
        }
    }

    private static (float U, float V) Uv(int px, int py) => ((px + 0.5f) / Size * 2f - 1f, (py + 0.5f) / Size * 2f - 1f);

    private static int Px(float u) => (int)((u + 1f) * 0.5f * Size);

    private static float Fbm(float x, float y)
    {
        float sum = 0f, amplitude = 0.5f;
        for (int i = 0; i < 3; i++)
        {
            sum += amplitude * Noise(x, y, (uint)i * 101u + 7u);
            x = x * 2.03f + 7.1f;
            y = y * 2.03f + 3.7f;
            amplitude *= 0.5f;
        }

        return sum / 0.875f;
    }

    private static float Noise(float x, float y, uint seed)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        float ux = fx * fx * (3f - 2f * fx), uy = fy * fy * (3f - 2f * fy);
        float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed), c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
    }

    private static float Hash(int x, int y, uint seed)
    {
        uint h = (uint)x * 374761393u + (uint)y * 668265263u + seed * 2246822519u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return (h ^ (h >> 16)) / (float)uint.MaxValue;
    }

    private static float Smooth(float a, float b, float x)
    {
        float t = Mathf.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static byte Byte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    private float R(float a, float b) => a + (float)_random.NextDouble() * (b - a);
}
