using System;
using Godot;

namespace Pb.Game.World;

/// <summary>What hangs on the walls indoors (<see cref="WallHangings"/>).</summary>
public enum HangingKind
{
    Noticeboard,
    Whiteboard,
    Clock,
    Planner,
    ExitSign,
    Picture,
    HazardSign,
    SitePlan,
}

/// <summary>
/// Paints the wall hangings' atlas once at load, one kind to a cell of a 4 × 2 grid, each drawn in a
/// rectangle of its own shape inside its cell (<see cref="Rect"/> gives the UVs): a cork noticeboard
/// with papers pinned to it, a whiteboard with faded scribbles, a clock stopped at twenty to five, a
/// wall planner, an exit sign (a running figure, a door and an arrow), a framed landscape, a hazard
/// sign (a triangle with an exclamation mark) and a site plan. Lines stand in for writing: nothing can
/// be read, and nothing is anyone's mark.
/// </summary>
public sealed class HangingPainter
{
    public const int Columns = 4, Rows = 2, CellPx = 256;

    private const int Width = Columns * CellPx, Height = Rows * CellPx;

    private readonly Random _random;
    private readonly byte[] _rgba = new byte[Width * Height * 4];

    public HangingPainter(int seed)
    {
        _random = new Random(seed);
    }

    /// <summary>A kind's size on the wall (width, height; m).</summary>
    public static Vector2 Size(HangingKind kind) => kind switch
    {
        HangingKind.Noticeboard => new Vector2(1.2f, 0.9f),
        HangingKind.Whiteboard => new Vector2(1.5f, 1.0f),
        HangingKind.Clock => new Vector2(0.34f, 0.34f),
        HangingKind.Planner => new Vector2(1.0f, 0.7f),
        HangingKind.ExitSign => new Vector2(0.4f, 0.2f),
        HangingKind.Picture => new Vector2(0.6f, 0.45f),
        HangingKind.HazardSign => new Vector2(0.3f, 0.3f),
        _ => new Vector2(0.84f, 0.6f),
    };

    /// <summary>The UV rectangle (u0, v0, u1, v1) a kind is painted in: its own shape, centred in its cell.</summary>
    public static Color Rect(HangingKind kind)
    {
        (Rect2I r, _) = Pixels(kind);
        return new Color((float)r.Position.X / Width, (float)r.Position.Y / Height, (float)r.End.X / Width, (float)r.End.Y / Height);
    }

    public ImageTexture Paint()
    {
        foreach (HangingKind kind in Enum.GetValues<HangingKind>())
        {
            (Rect2I r, _) = Pixels(kind);
            switch (kind)
            {
                case HangingKind.Noticeboard:
                    Noticeboard(r);
                    break;
                case HangingKind.Whiteboard:
                    Whiteboard(r);
                    break;
                case HangingKind.Clock:
                    Clock(r);
                    break;
                case HangingKind.Planner:
                    Planner(r);
                    break;
                case HangingKind.ExitSign:
                    ExitSign(r);
                    break;
                case HangingKind.Picture:
                    Picture(r);
                    break;
                case HangingKind.HazardSign:
                    HazardSign(r);
                    break;
                case HangingKind.SitePlan:
                    SitePlan(r);
                    break;
            }
        }

        Image image = Image.CreateFromData(Width, Height, false, Image.Format.Rgba8, _rgba);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>The kind's rectangle in pixels, inset from its cell's edges, in its own proportions.</summary>
    private static (Rect2I Rect, int Cell) Pixels(HangingKind kind)
    {
        int cell = (int)kind;
        Vector2 size = Size(kind);
        float aspect = size.X / size.Y;
        const int room = CellPx - 8;
        int w = aspect >= 1f ? room : (int)(room * aspect), h = aspect >= 1f ? (int)(room / aspect) : room;
        int x = cell % Columns * CellPx + (CellPx - w) / 2, y = cell / Columns * CellPx + (CellPx - h) / 2;
        return (new Rect2I(x, y, w, h), cell);
    }

    private void Noticeboard(Rect2I r)
    {
        Fill(r, new Color(0.62f, 0.64f, 0.66f));
        Rect2I cork = Grow(r, -6);
        for (int y = cork.Position.Y; y < cork.End.Y; y++)
        {
            for (int x = cork.Position.X; x < cork.End.X; x++)
            {
                float n = (float)_random.NextDouble();
                Put(x, y, Opaque(new Color(0.58f, 0.42f, 0.26f) * (0.82f + 0.3f * n * n)));
            }
        }

        Color[] papers = { new(0.94f, 0.93f, 0.88f), new(0.95f, 0.9f, 0.62f), new(0.93f, 0.78f, 0.8f), new(0.78f, 0.86f, 0.93f), new(0.95f, 0.95f, 0.95f) };
        Color[] pins = { new(0.8f, 0.12f, 0.1f), new(0.12f, 0.3f, 0.8f), new(0.15f, 0.6f, 0.2f), new(0.9f, 0.75f, 0.1f) };
        int count = _random.Next(6, 10);
        for (int k = 0; k < count; k++)
        {
            int w = _random.Next(cork.Size.X / 6, cork.Size.X / 3), h = (int)(w * R(1.1f, 1.45f));
            int x = _random.Next(cork.Position.X + 4, Math.Max(cork.Position.X + 5, cork.End.X - w - 4));
            int y = _random.Next(cork.Position.Y + 4, Math.Max(cork.Position.Y + 5, cork.End.Y - h - 4));
            var paper = new Rect2I(x, y, w, Math.Min(h, cork.End.Y - y - 3));
            Fill(new Rect2I(paper.Position + new Vector2I(2, 3), paper.Size), new Color(0.3f, 0.22f, 0.14f));
            Fill(paper, Opaque(papers[_random.Next(papers.Length)] * R(0.9f, 1f)));
            Writing(Grow(paper, -Math.Max(3, w / 10)), new Color(0.45f, 0.45f, 0.48f), 4);
            Disc(new Vector2(paper.Position.X + w * 0.5f, paper.Position.Y + 4f), 2.6f, pins[_random.Next(pins.Length)]);
        }
    }

    private void Whiteboard(Rect2I r)
    {
        Fill(r, new Color(0.66f, 0.68f, 0.7f));
        Rect2I board = Grow(r, -5);
        Fill(board, new Color(0.92f, 0.93f, 0.94f));
        // Ghosts of what was wiped off: grey smears.
        for (int k = 0; k < 9; k++)
        {
            var at = new Vector2(R(board.Position.X, board.End.X), R(board.Position.Y, board.End.Y - 20));
            Blot(at, R(14f, 34f), new Color(0.72f, 0.74f, 0.76f), R(0.15f, 0.35f));
        }

        // A few faint scribbles left: loops and arrows, no words.
        Color[] inks = { new(0.15f, 0.25f, 0.6f), new(0.65f, 0.15f, 0.15f), new(0.15f, 0.15f, 0.15f), new(0.15f, 0.45f, 0.2f) };
        for (int k = 0; k < 5; k++)
        {
            Color ink = inks[_random.Next(inks.Length)] with { A = R(0.35f, 0.7f) };
            var p = new Vector2(R(board.Position.X + 15, board.End.X - 40), R(board.Position.Y + 12, board.End.Y - 40));
            for (int s = 0; s < 12; s++)
            {
                Vector2 q = p + new Vector2(R(-14f, 22f), R(-10f, 12f));
                Line(p, q, 1.4f, ink);
                p = q;
            }
        }

        // The tray along the bottom, with a pen in it.
        Fill(new Rect2I(r.Position.X, r.End.Y - 9, r.Size.X, 9), new Color(0.52f, 0.54f, 0.56f));
        Fill(new Rect2I(r.Position.X + r.Size.X / 3, r.End.Y - 8, 26, 4), new Color(0.12f, 0.2f, 0.55f));
    }

    private void Clock(Rect2I r)
    {
        var centre = new Vector2(r.Position.X + r.Size.X * 0.5f, r.Position.Y + r.Size.Y * 0.5f);
        float radius = r.Size.X * 0.5f - 1f;
        Disc(centre, radius, new Color(0.1f, 0.1f, 0.11f));
        Disc(centre, radius * 0.9f, new Color(0.92f, 0.91f, 0.86f));
        for (int i = 0; i < 60; i++)
        {
            float a = Mathf.Tau * i / 60f;
            var d = new Vector2(MathF.Sin(a), -MathF.Cos(a));
            bool hour = i % 5 == 0;
            Line(centre + d * radius * (hour ? 0.7f : 0.8f), centre + d * radius * 0.86f, hour ? (i % 15 == 0 ? 3.2f : 2.2f) : 0.8f, new Color(0.1f, 0.1f, 0.1f));
        }

        // Stopped at twenty to five.
        void Hand(float turns, float length, float width, Color colour) =>
            Line(centre, centre + new Vector2(MathF.Sin(Mathf.Tau * turns), -MathF.Cos(Mathf.Tau * turns)) * radius * length, width, colour);
        Hand((4f + 40f / 60f) / 12f, 0.48f, 4.5f, new Color(0.08f, 0.08f, 0.08f));
        Hand(40f / 60f, 0.72f, 3f, new Color(0.08f, 0.08f, 0.08f));
        Hand(13f / 60f, 0.78f, 1.2f, new Color(0.75f, 0.1f, 0.08f));
        Disc(centre, 4f, new Color(0.1f, 0.1f, 0.1f));
    }

    private void Planner(Rect2I r)
    {
        Fill(r, new Color(0.93f, 0.93f, 0.91f));
        Fill(new Rect2I(r.Position.X, r.Position.Y, r.Size.X, 14), new Color(0.2f, 0.36f, 0.6f));
        var grid = new Rect2I(r.Position.X + 16, r.Position.Y + 20, r.Size.X - 22, r.Size.Y - 26);
        const int columns = 12, rows = 31;
        for (int c = 0; c <= columns; c++)
        {
            int x = grid.Position.X + grid.Size.X * c / columns;
            Fill(new Rect2I(x, grid.Position.Y, 1, grid.Size.Y), new Color(0.55f, 0.6f, 0.68f));
        }

        for (int row = 0; row <= rows; row++)
        {
            int y = grid.Position.Y + grid.Size.Y * row / rows;
            Fill(new Rect2I(grid.Position.X, y, grid.Size.X, 1), new Color(0.75f, 0.78f, 0.82f));
        }

        // Blocks of time booked out, long faded.
        Color[] blocks = { new(0.85f, 0.45f, 0.35f), new(0.45f, 0.7f, 0.45f), new(0.95f, 0.8f, 0.35f), new(0.5f, 0.6f, 0.85f) };
        for (int k = 0; k < 14; k++)
        {
            int c = _random.Next(columns), row = _random.Next(rows - 4), length = _random.Next(1, 5);
            int x = grid.Position.X + grid.Size.X * c / columns + 2, y = grid.Position.Y + grid.Size.Y * row / rows + 1;
            Fill(new Rect2I(x, y, grid.Size.X / columns - 3, grid.Size.Y * length / rows - 1), Opaque(blocks[_random.Next(blocks.Length)] * 0.92f));
        }
    }

    private void ExitSign(Rect2I r)
    {
        var green = new Color(0.05f, 0.5f, 0.28f);
        var white = new Color(0.95f, 0.96f, 0.94f);
        Fill(r, green);
        float h = r.Size.Y, x0 = r.Position.X, y0 = r.Position.Y;
        // A doorway on the right, a figure running into it, an arrow on the left.
        Fill(new Rect2I((int)(x0 + r.Size.X * 0.72f), (int)(y0 + h * 0.14f), (int)(r.Size.X * 0.18f), (int)(h * 0.72f)), white);
        Fill(new Rect2I((int)(x0 + r.Size.X * 0.745f), (int)(y0 + h * 0.2f), (int)(r.Size.X * 0.13f), (int)(h * 0.66f)), green);
        var hip = new Vector2(x0 + r.Size.X * 0.56f, y0 + h * 0.55f);
        var neck = hip + new Vector2(h * 0.1f, -h * 0.26f);
        Disc(neck + new Vector2(h * 0.06f, -h * 0.1f), h * 0.075f, white);
        Line(hip, neck, h * 0.08f, white);
        Line(neck, neck + new Vector2(h * 0.16f, h * 0.06f), h * 0.05f, white);
        Line(neck, neck + new Vector2(-h * 0.12f, h * 0.12f), h * 0.05f, white);
        Line(hip, hip + new Vector2(h * 0.18f, h * 0.12f), h * 0.06f, white);
        Line(hip + new Vector2(h * 0.18f, h * 0.12f), hip + new Vector2(h * 0.15f, h * 0.3f), h * 0.055f, white);
        Line(hip, hip + new Vector2(-h * 0.14f, h * 0.2f), h * 0.06f, white);
        Line(hip + new Vector2(-h * 0.14f, h * 0.2f), hip + new Vector2(-h * 0.28f, h * 0.22f), h * 0.055f, white);
        var tail = new Vector2(x0 + r.Size.X * 0.08f, y0 + h * 0.5f);
        var tip = new Vector2(x0 + r.Size.X * 0.36f, y0 + h * 0.5f);
        Line(tail, tip - new Vector2(h * 0.12f, 0f), h * 0.12f, white);
        Triangle(tip, tip + new Vector2(-h * 0.22f, -h * 0.2f), tip + new Vector2(-h * 0.22f, h * 0.2f), white);
    }

    private void Picture(Rect2I r)
    {
        Fill(r, new Color(0.22f, 0.15f, 0.1f));
        Fill(Grow(r, -9), new Color(0.88f, 0.85f, 0.78f));
        Rect2I view = Grow(r, -22);
        float horizon = view.Position.Y + view.Size.Y * 0.55f;
        float p1 = R(0f, Mathf.Tau), p2 = R(0f, Mathf.Tau);
        for (int y = view.Position.Y; y < view.End.Y; y++)
        {
            for (int x = view.Position.X; x < view.End.X; x++)
            {
                float u = (float)(x - view.Position.X) / view.Size.X, v = (float)(y - view.Position.Y) / view.Size.Y;
                float hills = horizon - view.Size.Y * (0.12f + 0.08f * MathF.Sin(u * 5f + p1) + 0.04f * MathF.Sin(u * 13f + p2));
                Color c;
                if (y < hills)
                {
                    c = new Color(0.55f, 0.68f, 0.82f).Lerp(new Color(0.9f, 0.88f, 0.8f), v * 1.4f);
                }
                else if (y < horizon)
                {
                    c = new Color(0.45f, 0.52f, 0.58f);
                }
                else
                {
                    c = new Color(0.42f, 0.55f, 0.28f).Lerp(new Color(0.55f, 0.6f, 0.3f), (v - 0.55f) * 2f);
                }

                Put(x, y, c);
            }
        }

        // A tree in the field.
        var trunk = new Vector2(view.Position.X + view.Size.X * R(0.25f, 0.7f), horizon + view.Size.Y * 0.18f);
        Line(trunk, trunk - new Vector2(0f, view.Size.Y * 0.2f), 3f, new Color(0.3f, 0.22f, 0.15f));
        Disc(trunk - new Vector2(0f, view.Size.Y * 0.28f), view.Size.Y * 0.14f, new Color(0.25f, 0.38f, 0.18f));
    }

    private void HazardSign(Rect2I r)
    {
        Fill(r, new Color(0.93f, 0.93f, 0.9f));
        var top = new Vector2(r.Position.X + r.Size.X * 0.5f, r.Position.Y + r.Size.Y * 0.12f);
        var left = new Vector2(r.Position.X + r.Size.X * 0.1f, r.Position.Y + r.Size.Y * 0.86f);
        var right = new Vector2(r.Position.X + r.Size.X * 0.9f, r.Position.Y + r.Size.Y * 0.86f);
        Triangle(top, left, right, new Color(0.08f, 0.08f, 0.08f));
        Vector2 middle = (top + left + right) / 3f;
        Triangle(middle + (top - middle) * 0.8f, middle + (left - middle) * 0.8f, middle + (right - middle) * 0.8f, new Color(0.96f, 0.78f, 0.05f));
        float s = r.Size.Y;
        Line(new Vector2(top.X, top.Y + s * 0.2f), new Vector2(top.X, top.Y + s * 0.5f), s * 0.07f, new Color(0.08f, 0.08f, 0.08f));
        Disc(new Vector2(top.X, top.Y + s * 0.6f), s * 0.045f, new Color(0.08f, 0.08f, 0.08f));
    }

    private void SitePlan(Rect2I r)
    {
        var paper = new Color(0.82f, 0.88f, 0.94f);
        var ink = new Color(0.18f, 0.3f, 0.55f);
        Fill(r, paper);
        Outline(Grow(r, -6), ink, 2);
        // Buildings as boxes, a wall round them, a road in.
        var site = Grow(r, -18);
        Outline(site, ink, 2);
        for (int k = 0; k < 5; k++)
        {
            int w = _random.Next(site.Size.X / 8, site.Size.X / 3), h = _random.Next(site.Size.Y / 8, site.Size.Y / 3);
            var b = new Rect2I(_random.Next(site.Position.X + 6, site.End.X - w - 6), _random.Next(site.Position.Y + 6, site.End.Y - h - 30), w, h);
            Fill(b, Opaque(ink * 0.25f + paper * 0.75f));
            Outline(b, ink, 1);
        }

        for (int x = site.Position.X; x < site.End.X; x += 8)
        {
            Fill(new Rect2I(x, site.End.Y - 16, 4, 1), ink);
        }

        // A title block with lines in it, bottom right.
        var block = new Rect2I(r.End.X - 70, r.End.Y - 36, 62, 28);
        Fill(block, paper);
        Outline(block, ink, 1);
        Writing(Grow(block, -4), ink, 3);
    }

    /// <summary>Rows of grey dashes standing in for writing: nothing that can be read.</summary>
    private void Writing(Rect2I r, Color ink, int spacing)
    {
        for (int y = r.Position.Y + 2; y < r.End.Y - 1; y += spacing + 1)
        {
            int x = r.Position.X;
            int end = r.End.X - _random.Next(0, Math.Max(1, r.Size.X / 3));
            while (x < end)
            {
                int word = _random.Next(3, 10);
                Fill(new Rect2I(x, y, Math.Min(word, end - x), 1), Opaque(ink * R(0.8f, 1.1f)));
                x += word + _random.Next(2, 4);
            }
        }
    }

    private void Fill(Rect2I r, Color c)
    {
        for (int y = Math.Max(0, r.Position.Y); y < Math.Min(Height, r.End.Y); y++)
        {
            for (int x = Math.Max(0, r.Position.X); x < Math.Min(Width, r.End.X); x++)
            {
                Put(x, y, c);
            }
        }
    }

    private void Outline(Rect2I r, Color c, int width)
    {
        Fill(new Rect2I(r.Position.X, r.Position.Y, r.Size.X, width), c);
        Fill(new Rect2I(r.Position.X, r.End.Y - width, r.Size.X, width), c);
        Fill(new Rect2I(r.Position.X, r.Position.Y, width, r.Size.Y), c);
        Fill(new Rect2I(r.End.X - width, r.Position.Y, width, r.Size.Y), c);
    }

    private void Disc(Vector2 centre, float radius, Color c)
    {
        for (int y = (int)(centre.Y - radius - 1); y <= (int)(centre.Y + radius + 1); y++)
        {
            for (int x = (int)(centre.X - radius - 1); x <= (int)(centre.X + radius + 1); x++)
            {
                float cover = Math.Clamp(radius + 0.5f - centre.DistanceTo(new Vector2(x + 0.5f, y + 0.5f)), 0f, 1f);
                if (cover > 0f)
                {
                    Blend(x, y, c, cover);
                }
            }
        }
    }

    private void Blot(Vector2 centre, float radius, Color c, float strength)
    {
        for (int y = (int)(centre.Y - radius); y <= (int)(centre.Y + radius); y++)
        {
            for (int x = (int)(centre.X - radius); x <= (int)(centre.X + radius); x++)
            {
                float t = centre.DistanceTo(new Vector2(x, y)) / radius;
                if (t < 1f)
                {
                    Blend(x, y, c, strength * (1f - t * t));
                }
            }
        }
    }

    private void Line(Vector2 a, Vector2 b, float width, Color c)
    {
        float length = a.DistanceTo(b);
        int steps = Math.Max(1, (int)(length * 2f));
        float radius = width * 0.5f;
        for (int s = 0; s <= steps; s++)
        {
            Vector2 p = a.Lerp(b, (float)s / steps);
            for (int y = (int)(p.Y - radius - 1); y <= (int)(p.Y + radius + 1); y++)
            {
                for (int x = (int)(p.X - radius - 1); x <= (int)(p.X + radius + 1); x++)
                {
                    float cover = Math.Clamp(radius + 0.5f - p.DistanceTo(new Vector2(x + 0.5f, y + 0.5f)), 0f, 1f);
                    if (cover > 0f)
                    {
                        BlendMax(x, y, c, cover * c.A);
                    }
                }
            }
        }
    }

    private void Triangle(Vector2 a, Vector2 b, Vector2 c, Color colour)
    {
        int x0 = (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))), x1 = (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X)));
        int y0 = (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))), y1 = (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y)));
        float area = (b - a).Cross(c - a);
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float w0 = (b - a).Cross(p - a) / area, w1 = (c - b).Cross(p - b) / area, w2 = (a - c).Cross(p - c) / area;
                if (w0 >= 0f && w1 >= 0f && w2 >= 0f)
                {
                    Put(x, y, colour);
                }
            }
        }
    }

    private void Put(int x, int y, Color c) => Blend(x, y, c, 1f);

    private void Blend(int x, int y, Color c, float amount)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return;
        }

        int i = (y * Width + x) * 4;
        float keep = 1f - amount;
        _rgba[i] = (byte)(_rgba[i] * keep + Math.Clamp(c.R, 0f, 1f) * 255f * amount);
        _rgba[i + 1] = (byte)(_rgba[i + 1] * keep + Math.Clamp(c.G, 0f, 1f) * 255f * amount);
        _rgba[i + 2] = (byte)(_rgba[i + 2] * keep + Math.Clamp(c.B, 0f, 1f) * 255f * amount);
        _rgba[i + 3] = (byte)Math.Max(_rgba[i + 3], (int)(255f * amount));
    }

    /// <summary>Blends a line's colour in by its coverage without darkening where it overlaps itself.</summary>
    private void BlendMax(int x, int y, Color c, float amount) => Blend(x, y, c, amount);

    private static Color Opaque(Color c) => c with { A = 1f };

    private static Rect2I Grow(Rect2I r, int by) => new(r.Position.X - by, r.Position.Y - by, r.Size.X + 2 * by, r.Size.Y + 2 * by);

    private float R(float a, float b) => a + (float)_random.NextDouble() * (b - a);
}
