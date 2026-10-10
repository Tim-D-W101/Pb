using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.World;
using Pb.Sim.Gear;

namespace Pb.Game.Player;

/// <summary>
/// The brands' marks printed on their kit: each brand's name (<see cref="GearBrand.Mark"/>) painted once, when the game
/// starts, into a row of an atlas in the stencil hand of the yard's markings (<see cref="MarkingPainter"/>'s letters),
/// bolder and unworn. A mark is a quad laid a hair off an item's surface, cut out of its row by gear.gdshader, in one
/// of the item's colours.
/// </summary>
public static class BrandMarks
{
    private const int Width = 1024, RowPx = 128;

    /// <summary>The letters' height and their boxes' width, as shares of a row; the gap between letters, of a letter's width.</summary>
    private const float CapHeight = 0.72f, LetterAspect = 0.62f, Spacing = 0.2f, Stroke = 0.075f;

    private static ImageTexture? _atlas;

    /// <summary>Each mark's rectangle in the atlas (UVs) and its width over its height.</summary>
    private static readonly Dictionary<string, (Rect2 Uv, float Aspect)> Rows = new(StringComparer.Ordinal);

    /// <summary>The atlas of every brand's mark (painted the first time it's asked for).</summary>
    public static ImageTexture Atlas(IReadOnlyList<GearBrand> brands)
    {
        if (_atlas is not null)
        {
            return _atlas;
        }

        int height = RowPx;
        while (height < RowPx * brands.Count)
        {
            height *= 2;
        }

        // White everywhere, so the letters' edges stay white as the mipmaps blend them out.
        var pixels = new byte[Width * height * 2];
        for (int i = 0; i < pixels.Length; i += 2)
        {
            pixels[i] = 255;
        }

        for (int b = 0; b < brands.Count; b++)
        {
            Rows[brands[b].Mark] = Paint(pixels, brands[b].Mark, b * RowPx, height);
        }

        Image image = Image.CreateFromData(Width, height, false, Image.Format.La8, pixels);
        image.GenerateMipmaps();
        _atlas = ImageTexture.CreateFromImage(image);
        return _atlas;
    }

    /// <summary>
    /// Prints <paramref name="brand"/>'s mark on <paramref name="mesh"/> as surface <paramref name="zone"/>: centred on
    /// <paramref name="centre"/>, reading along <paramref name="right"/> with <paramref name="up"/> up and facing out of
    /// the surface towards right × up, <paramref name="height"/> tall (its width follows from its letters). The atlas
    /// must be painted first (<see cref="Atlas"/>).
    /// </summary>
    public static void Print(ShapeMesh mesh, int zone, GearBrand brand, Vector3 centre, Vector3 right, Vector3 up, float height)
    {
        if (!Rows.TryGetValue(brand.Mark, out (Rect2 Uv, float Aspect) found))
        {
            return;
        }

        Rect2 row = found.Uv;
        float width = height * found.Aspect;
        Vector3 r = right.Normalized() * (width * 0.5f), u = up.Normalized() * (height * 0.5f);
        Vector3 normal = right.Cross(up).Normalized();
        Vector3 at = centre + normal * 0.0007f;
        // Clockwise seen from the front: top left, top right, bottom right, bottom left.
        mesh.Quad(zone, at - r + u, at + r + u, at + r - u, at - r - u, normal,
            new Vector2(row.Position.X, row.Position.Y), new Vector2(row.End.X, row.Position.Y),
            new Vector2(row.End.X, row.End.Y), new Vector2(row.Position.X, row.End.Y));
    }

    /// <summary>Paints <paramref name="mark"/> into the row starting <paramref name="top"/> pixels down; its rectangle in UVs, and its aspect.</summary>
    private static (Rect2 Uv, float Aspect) Paint(byte[] pixels, string mark, int top, int height)
    {
        float cap = RowPx * CapHeight, letter = cap * LetterAspect, advance = letter * (1f + Spacing), half = cap * Stroke;
        float textWidth = mark.Length * advance - letter * Spacing;
        float left = MathF.Round((Width - textWidth) * 0.5f), baseline = top + (RowPx + cap) * 0.5f;
        for (int i = 0; i < mark.Length; i++)
        {
            if (mark[i] is < 'A' or > 'Z')
            {
                continue;
            }

            List<Vector2[]> strokes = MarkingPainter.LetterStrokes(mark[i]);
            float x0 = left + i * advance;
            int px0 = Math.Max(0, (int)(x0 - half - 2f)), px1 = Math.Min(Width - 1, (int)(x0 + letter + half + 2f));
            int py0 = Math.Max(top, (int)(baseline - cap - half - 2f)), py1 = Math.Min(top + RowPx - 1, (int)(baseline + half + 2f));
            for (int py = py0; py <= py1; py++)
            {
                for (int px = px0; px <= px1; px++)
                {
                    // In pixels from the letter box's bottom left, y up.
                    var p = new Vector2(px + 0.5f - x0, baseline - (py + 0.5f));
                    float distance = float.MaxValue;
                    foreach (Vector2[] stroke in strokes)
                    {
                        for (int s = 0; s + 1 < stroke.Length; s++)
                        {
                            distance = MathF.Min(distance, Segment(p, stroke[s] * new Vector2(letter, cap), stroke[s + 1] * new Vector2(letter, cap)));
                        }
                    }

                    float cover = Mathf.Clamp(half - distance + 0.5f, 0f, 1f);
                    int index = (py * Width + px) * 2;
                    pixels[index + 1] = Math.Max(pixels[index + 1], (byte)MathF.Round(cover * 255f));
                }
            }
        }

        float pad = half + 2f;
        return (new Rect2((left - pad) / Width, (float)top / height, (textWidth + pad * 2f) / Width, (float)RowPx / height), (textWidth + pad * 2f) / RowPx);
    }

    private static float Segment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp((p - a).Dot(ab) / MathF.Max(ab.LengthSquared(), 1e-6f), 0f, 1f);
        return p.DistanceTo(a + ab * t);
    }
}
