using System;
using Godot;

namespace Pb.Game.World;

/// <summary>What lies on the floors indoors (<see cref="FloorDebris"/>), each painted in two variants.</summary>
public enum DebrisKind
{
    Papers,
    Plaster,
    Glass,
    Dust,
    Leaves,
}

/// <summary>
/// Paints the floor debris atlas once at load: for each <see cref="DebrisKind"/> two cells of a 4-column
/// grid, white on clear (luminance for the detail, alpha for coverage), tinted per card. Papers are
/// sheets with a fold and faint ruled lines, a corner curled up, or crumpled into a ball; plaster is
/// broken chunks with darker edges in a scatter of powder; glass is slivers, clear in the middle and
/// bright along their edges; dust is a soft drift with specks in it; leaves are dry leaves with their
/// midribs. Nothing printed, nothing anyone would recognise.
/// </summary>
public sealed class DebrisPainter
{
    public const int Columns = 4, Rows = 3, CellPx = 256;

    public const int Variants = 2;

    private readonly Random _random;
    private readonly float[] _cover = new float[Columns * CellPx * Rows * CellPx];
    private readonly float[] _shade = new float[Columns * CellPx * Rows * CellPx];

    public DebrisPainter(uint seed)
    {
        _random = new Random((int)(seed & 0x7fffffff));
    }

    /// <summary>The atlas cell for a kind's variant.</summary>
    public static int Cell(DebrisKind kind, int variant) => (int)kind * Variants + (variant & 1);

    public ImageTexture Paint()
    {
        Array.Fill(_shade, 1f);
        foreach (DebrisKind kind in Enum.GetValues<DebrisKind>())
        {
            for (int v = 0; v < Variants; v++)
            {
                int cell = Cell(kind, v);
                var origin = new Vector2(cell % Columns * CellPx, cell / Columns * CellPx);
                switch (kind)
                {
                    case DebrisKind.Papers:
                        Papers(origin, v);
                        break;
                    case DebrisKind.Plaster:
                        Plaster(origin, v);
                        break;
                    case DebrisKind.Glass:
                        Glass(origin);
                        break;
                    case DebrisKind.Dust:
                        Dust(origin);
                        break;
                    case DebrisKind.Leaves:
                        Leaves(origin);
                        break;
                }
            }
        }

        int width = Columns * CellPx, height = Rows * CellPx;
        var bytes = new byte[width * height * 2];
        for (int i = 0; i < width * height; i++)
        {
            bytes[i * 2] = (byte)(Math.Clamp(_shade[i], 0f, 1f) * 255f);
            bytes[i * 2 + 1] = (byte)(Math.Clamp(_cover[i], 0f, 1f) * 255f);
        }

        Image image = Image.CreateFromData(width, height, false, Image.Format.La8, bytes);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>A sheet lying flat (fold, ruled lines, a curled corner) or crumpled into a ball beside a torn half sheet.</summary>
    private void Papers(Vector2 origin, int variant)
    {
        Vector2 centre = origin + new Vector2(CellPx, CellPx) * 0.5f;
        if (variant == 0)
        {
            Sheet(centre, new Vector2(70f, 99f), R(-0.5f, 0.5f), curl: true);
            return;
        }

        Sheet(centre + new Vector2(30f, 25f), new Vector2(60f, 42f), R(-0.6f, 0.6f), curl: false);
        // The ball: facets of light and shade inside a lumpy outline.
        Vector2 ball = centre + new Vector2(-38f, -36f);
        var facets = new Vector2[14];
        var tone = new float[facets.Length];
        for (int i = 0; i < facets.Length; i++)
        {
            facets[i] = ball + new Vector2(R(-38f, 38f), R(-38f, 38f));
            tone[i] = R(0.6f, 1f);
        }

        float p1 = R(0f, Mathf.Tau), p2 = R(0f, Mathf.Tau);
        Fill(ball, 46f, p =>
        {
            Vector2 d = p - ball;
            float a = MathF.Atan2(d.Y, d.X);
            float edge = 34f * (1f + 0.12f * MathF.Sin(5f * a + p1) + 0.08f * MathF.Sin(8f * a + p2));
            float inside = Math.Clamp(edge - d.Length(), 0f, 1.5f) / 1.5f;
            if (inside <= 0f)
            {
                return (0f, 1f);
            }

            // Nearest and second-nearest facet: darker along the creases between them.
            float best = float.MaxValue, second = float.MaxValue;
            int at = 0;
            for (int i = 0; i < facets.Length; i++)
            {
                float f = p.DistanceSquaredTo(facets[i]);
                if (f < best)
                {
                    second = best;
                    best = f;
                    at = i;
                }
                else if (f < second)
                {
                    second = f;
                }
            }

            float crease = Math.Clamp((MathF.Sqrt(second) - MathF.Sqrt(best)) / 3f, 0f, 1f);
            float rim = Math.Clamp((edge - d.Length()) / 10f, 0f, 1f);
            return (inside, tone[at] * (0.7f + 0.3f * crease) * (0.75f + 0.25f * rim));
        });
    }

    private void Sheet(Vector2 centre, Vector2 half, float angle, bool curl)
    {
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        float fold = R(-0.3f, 0.3f);
        Fill(centre, half.Length() + 2f, p =>
        {
            Vector2 d = p - centre;
            var q = new Vector2(d.X * c + d.Y * s, -d.X * s + d.Y * c);
            float inside = Math.Clamp(MathF.Min(half.X - MathF.Abs(q.X), half.Y - MathF.Abs(q.Y)) + 0.5f, 0f, 1f);
            if (inside <= 0f)
            {
                return (0f, 1f);
            }

            float shade = 0.97f;
            // Ruled lines across, a margin down the side, nothing written.
            if (MathF.Abs((q.Y + half.Y) % 9f - 4.5f) < 0.45f && q.Y > -half.Y + 14f)
            {
                shade = 0.86f;
            }

            if (MathF.Abs(q.X + half.X - 12f) < 0.5f)
            {
                shade = 0.84f;
            }

            // A fold across it: one side a little darker.
            if (q.Y > fold * half.Y)
            {
                shade *= 0.92f;
            }

            if (MathF.Abs(q.Y - fold * half.Y) < 0.8f)
            {
                shade *= 0.8f;
            }

            // A corner curled up, shaded on its underside.
            if (curl && q.X + q.Y > half.X + half.Y - 28f)
            {
                shade *= 0.72f;
            }

            // Grubby towards the edges.
            float edge = MathF.Min(half.X - MathF.Abs(q.X), half.Y - MathF.Abs(q.Y));
            shade *= 0.88f + 0.12f * Math.Clamp(edge / 8f, 0f, 1f);
            return (inside, shade);
        });
    }

    /// <summary>Broken chunks of plaster with darker broken edges, in a scatter of powder and crumbs.</summary>
    private void Plaster(Vector2 origin, int variant)
    {
        Vector2 centre = origin + new Vector2(CellPx, CellPx) * 0.5f;
        // Powder: a soft patch under the pieces.
        float q1 = R(0f, Mathf.Tau), q2 = R(0f, Mathf.Tau);
        Fill(centre, 120f, p =>
        {
            Vector2 d = p - centre;
            float a = MathF.Atan2(d.Y, d.X);
            float r = 92f * (1f + 0.18f * MathF.Sin(3f * a + q1) + 0.1f * MathF.Sin(7f * a + q2));
            float t = d.Length() / r;
            return (0.32f * Math.Clamp(1f - t * t, 0f, 1f), 0.95f);
        });

        int pieces = variant == 0 ? _random.Next(9, 15) : _random.Next(4, 7);
        for (int k = 0; k < pieces; k++)
        {
            float size = variant == 0 ? R(6f, 22f) : R(14f, 36f);
            Vector2 at = centre + new Vector2(R(-80f, 80f), R(-80f, 80f)) * (1f - size / 60f);
            Chunk(at, size, R(0.82f, 1f));
        }

        // Crumbs.
        for (int k = 0; k < 140; k++)
        {
            Vector2 at = centre + new Vector2(R(-95f, 95f), R(-95f, 95f));
            if (at.DistanceTo(centre) < 100f)
            {
                Chunk(at, R(1.2f, 3.5f), R(0.75f, 1f));
            }
        }
    }

    private void Chunk(Vector2 at, float size, float tone)
    {
        int sides = _random.Next(5, 9);
        var reach = new float[sides];
        for (int i = 0; i < sides; i++)
        {
            reach[i] = size * R(0.6f, 1.1f);
        }

        float turn = R(0f, Mathf.Tau);
        Fill(at, size * 1.2f, p =>
        {
            Vector2 d = p - at;
            float a = (MathF.Atan2(d.Y, d.X) - turn + Mathf.Tau * 2f) % Mathf.Tau / Mathf.Tau * sides;
            int i = (int)a % sides;
            float edge = Mathf.Lerp(reach[i], reach[(i + 1) % sides], a - MathF.Floor(a));
            float inside = Math.Clamp(edge - d.Length() + 0.5f, 0f, 1f);
            float rim = Math.Clamp((edge - d.Length()) / MathF.Max(2f, size * 0.25f), 0f, 1f);
            return (inside, tone * (0.68f + 0.32f * rim));
        });
    }

    /// <summary>Slivers of glass: clear in the middle, catching the light along their edges.</summary>
    private void Glass(Vector2 origin)
    {
        Vector2 centre = origin + new Vector2(CellPx, CellPx) * 0.5f;
        int shards = _random.Next(8, 15);
        for (int k = 0; k < shards; k++)
        {
            Vector2 at = centre + new Vector2(R(-85f, 85f), R(-85f, 85f));
            float length = R(10f, 46f), width = length * R(0.18f, 0.5f), angle = R(0f, Mathf.Tau);
            Vector2 u = new(MathF.Cos(angle), MathF.Sin(angle)), w = new(-u.Y, u.X);
            // A triangle: point a, b along it, c off to one side.
            Vector2 a = at - u * length * 0.5f, b = at + u * length * 0.5f, c = at + u * R(-0.3f, 0.3f) * length + w * width;
            Fill(at, length, p =>
            {
                float e = MathF.Min(Edge(p, a, b, c), MathF.Min(Edge(p, b, c, a), Edge(p, c, a, b)));
                if (e < -0.5f)
                {
                    return (0f, 1f);
                }

                float inside = Math.Clamp(e + 0.5f, 0f, 1f);
                float bright = 1f - Math.Clamp((e - 0.5f) / 1.5f, 0f, 1f);
                return (inside * (0.3f + 0.6f * bright), 0.75f + 0.25f * bright);
            });
        }
    }

    /// <summary>Signed distance inside edge a→b of triangle (a, b, c): positive on c's side.</summary>
    private static float Edge(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        Vector2 ab = b - a;
        float side = ab.Cross(c - a) >= 0f ? 1f : -1f;
        return ab.Cross(p - a) * side / ab.Length();
    }

    /// <summary>A soft drift of dust and grit, thicker in the middle, with specks in it.</summary>
    private void Dust(Vector2 origin)
    {
        Vector2 centre = origin + new Vector2(CellPx, CellPx) * 0.5f;
        float s1 = R(0f, 100f), s2 = R(0f, 100f);
        Fill(centre, 125f, p =>
        {
            Vector2 d = (p - centre) / 118f;
            float n = Noise(p.X * 0.03f + s1, p.Y * 0.03f + s2) * 0.6f + Noise(p.X * 0.09f + s2, p.Y * 0.09f + s1) * 0.4f;
            float t = Math.Clamp(1f - d.LengthSquared() * (1.3f - 0.6f * n), 0f, 1f);
            return (0.55f * t * t * (0.6f + 0.4f * n), 0.85f + 0.15f * n);
        });

        for (int k = 0; k < 90; k++)
        {
            Vector2 at = centre + new Vector2(R(-80f, 80f), R(-80f, 80f));
            Chunk(at, R(0.8f, 2.4f), R(0.45f, 0.8f));
        }
    }

    /// <summary>A few dry leaves: pointed ovals with a midrib, curled a little (darker towards one side).</summary>
    private void Leaves(Vector2 origin)
    {
        Vector2 centre = origin + new Vector2(CellPx, CellPx) * 0.5f;
        int leaves = _random.Next(4, 8);
        for (int k = 0; k < leaves; k++)
        {
            Vector2 at = centre + new Vector2(R(-70f, 70f), R(-70f, 70f));
            float length = R(26f, 44f), half = length * R(0.28f, 0.4f), angle = R(0f, Mathf.Tau), tone = R(0.6f, 1f);
            float c = MathF.Cos(angle), s = MathF.Sin(angle);
            Fill(at, length, p =>
            {
                Vector2 d = p - at;
                var q = new Vector2(d.X * c + d.Y * s, -d.X * s + d.Y * c);
                float t = q.X / length + 0.5f;
                if (t < 0f || t > 1.08f)
                {
                    return (0f, 1f);
                }

                // A stalk past the tip end, then the blade: widest a little below the middle, pointed at the tip.
                if (t > 1f)
                {
                    return (MathF.Abs(q.Y) < 0.9f ? 1f : 0f, tone * 0.6f);
                }

                float w = half * MathF.Sin(MathF.PI * MathF.Pow(t, 0.8f));
                float inside = Math.Clamp(w - MathF.Abs(q.Y) + 0.5f, 0f, 1f);
                float shade = tone * (q.Y > 0f ? 0.85f : 1f) * (MathF.Abs(q.Y) < 0.8f ? 0.7f : 1f);
                return (inside, shade);
            });
        }
    }

    /// <summary>Paints coverage and shade from <paramref name="at"/> out to <paramref name="radius"/>, over what's there.</summary>
    private void Fill(Vector2 at, float radius, Func<Vector2, (float Cover, float Shade)> paint)
    {
        int width = Columns * CellPx;
        int cellX = (int)(at.X / CellPx) * CellPx, cellY = (int)(at.Y / CellPx) * CellPx;
        int x0 = Math.Max(cellX + 2, (int)(at.X - radius)), x1 = Math.Min(cellX + CellPx - 3, (int)(at.X + radius));
        int y0 = Math.Max(cellY + 2, (int)(at.Y - radius)), y1 = Math.Min(cellY + CellPx - 3, (int)(at.Y + radius));
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                (float cover, float shade) = paint(new Vector2(x + 0.5f, y + 0.5f));
                if (cover <= 0f)
                {
                    continue;
                }

                int i = y * width + x;
                // Over what's already there: the new paint's shade where it covers.
                _shade[i] = Mathf.Lerp(_cover[i] > 0f ? _shade[i] : shade, shade, cover);
                _cover[i] = 1f - (1f - _cover[i]) * (1f - Math.Clamp(cover, 0f, 1f));
            }
        }
    }

    private float R(float a, float b) => a + (float)_random.NextDouble() * (b - a);

    /// <summary>Smooth value noise in 0–1.</summary>
    private static float Noise(float x, float y)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        float ux = fx * fx * (3f - 2f * fx), uy = fy * fy * (3f - 2f * fy);
        float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
    }

    private static float Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }
    }
}
