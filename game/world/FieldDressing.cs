using System;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Data;
using Pb.Sim.Level;

namespace Pb.Game.World;

/// <summary>
/// A speedball field's looks (the level's <c>field.dressing</c>): nets all round on the level's bounds, on posts, with a
/// skirt along their foot and a wire along their top; each side's buzzer station on its back line; and the banners
/// hung on the nets (their words are stencilled by <see cref="Markings"/>). Presentation only: the nets stop nothing
/// themselves, the bounds do (a ball leaving them is gone, and nobody walks out).
/// </summary>
public static class FieldDressing
{
    /// <summary>Banners hang this far in front of the nets, on the field's side.</summary>
    public const float BannerStandoff = 0.04f;

    /// <summary>
    /// Builds it into the meshes <paramref name="meshFor"/> gives for each spot, and the netting into the ones
    /// <paramref name="sheetFor"/> gives (drawn without simpler levels of detail, which would stretch the cords or fold
    /// the sheets away); how many pieces it built.
    /// </summary>
    public static int Build(LevelLayout level, Func<string, int> material, Func<Vector3, ShapeMesh> meshFor, Func<Vector3, ShapeMesh> sheetFor)
    {
        if (level.Field is not { } field || level.FieldDressing is not { } d)
        {
            return 0;
        }

        int pieces = 0;
        int net = material(d.Net), posts = material(d.Posts), skirt = material(d.Skirt);
        Pb.Sim.Collision.Aabb b = level.Bounds;
        var corners = new[]
        {
            new Vector3(b.Min.X, 0f, b.Min.Z), new Vector3(b.Max.X, 0f, b.Min.Z), new Vector3(b.Max.X, 0f, b.Max.Z), new Vector3(b.Min.X, 0f, b.Max.Z),
        };
        for (int side = 0; side < 4; side++)
        {
            Vector3 from = corners[side], to = corners[(side + 1) % 4];
            float length = from.DistanceTo(to);
            Vector3 along = (to - from) / length;
            // The corners go round clockwise seen from above (north up), so this points out of the field.
            Vector3 outward = Vector3.Up.Cross(along);
            int bays = Math.Max(1, Mathf.RoundToInt(length / d.PostSpacing_m));
            for (int k = 0; k < bays; k++)
            {
                Vector3 a = from + along * (length * k / bays), c = from + along * (length * (k + 1) / bays);
                Vector3 middle = (a + c) * 0.5f;
                // The post at the bay's start (the corners' are the next side's first), the skirt and the top wire; and the
                // netting, one sheet seen from both sides.
                ShapeMesh m = meshFor(middle);
                m.Place(Transform3D.Identity, d.NetHeight_m);
                m.Cylinder(posts, a + Vector3.Up * (d.NetHeight_m * 0.5f), Basis.Identity, 0.05f, d.NetHeight_m, 8);
                Panel(m, skirt, a + outward * 0.01f, c + outward * 0.01f, 0f, d.SkirtHeight_m, outward, true);
                m.Rod(posts, a + Vector3.Up * d.NetHeight_m, c + Vector3.Up * d.NetHeight_m, 0.008f, 4);
                ShapeMesh sheet = sheetFor(middle);
                sheet.Place(Transform3D.Identity, d.NetHeight_m);
                Panel(sheet, net, a, c, 0f, d.NetHeight_m, outward, false);
                pieces++;
            }
        }

        // Each side's buzzer: a post at its back line with a box on top, a big button and a horn.
        int box = material(d.BuzzerBox), button = material(d.BuzzerButton);
        for (int side = 0; side < field.Buzzers.Count; side++)
        {
            Vector3 at = field.Buzzers[side].ToGodot();
            // It faces up the field.
            var facing = new Basis(Vector3.Up, side == 0 ? 0f : Mathf.Pi);
            ShapeMesh m = meshFor(at);
            m.Place(Transform3D.Identity, 1.6f);
            m.Cylinder(posts, at + Vector3.Up * 0.55f, Basis.Identity, 0.04f, 1.1f, 8);
            m.Box(posts, at + Vector3.Up * 0.02f, new Vector3(0.5f, 0.04f, 0.5f));
            m.Box(box, at + Vector3.Up * 1.25f, new Vector3(0.36f, 0.3f, 0.22f), facing);
            m.Cylinder(button, at + Vector3.Up * 1.25f + facing * new Vector3(0f, 0f, -0.12f), facing * new Basis(Vector3.Right, Mathf.Pi / 2f), 0.09f, 0.04f, 16);
            m.Lathe(box, at + Vector3.Up * 1.4f, Basis.Identity, new[] { new Vector2(0.02f, 0f), new Vector2(0.03f, 0.12f), new Vector2(0.11f, 0.24f) }, 16);
            pieces++;
        }

        // The banners' backing (their words are stencilled on them by Markings), on the field's side of the nets.
        foreach (FieldBannerDef banner in d.Banners)
        {
            var at = new Vector3(banner.At_m[0], banner.At_m[1], banner.At_m[2]);
            var turn = new Basis(Vector3.Up, Mathf.DegToRad(banner.Yaw_deg));
            Vector3 normal = turn * Vector3.Back, right = turn * Vector3.Right;
            float hw = banner.Size_m[0] * 0.5f, hh = banner.Size_m[1] * 0.5f;
            ShapeMesh m = meshFor(at);
            m.Place(Transform3D.Identity, at.Y + hh);
            Vector3 face = at + normal * BannerStandoff;
            m.Box(material(banner.Material), face - normal * 0.005f, new Vector3(hw * 2f, hh * 2f, 0.01f), turn);
            // Grommets and ties at its corners.
            foreach ((float x, float y) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            {
                m.Rod(posts, face + right * (x * (hw - 0.05f)) + Vector3.Up * (y * (hh - 0.05f)),
                    face - normal * BannerStandoff + right * (x * (hw + 0.03f)) + Vector3.Up * (y * (hh + 0.03f)), 0.006f, 4);
            }

            pieces++;
        }

        return pieces;
    }

    /// <summary>
    /// A flat sheet from <paramref name="a"/> to <paramref name="c"/> along the ground, <paramref name="y0"/> to
    /// <paramref name="y1"/> up, facing <paramref name="normal"/>, and the other way too if <paramref name="twoFaced"/>
    /// (an opaque sheet; a see-through one is drawn from both sides by its material).
    /// </summary>
    private static void Panel(ShapeMesh m, int material, Vector3 a, Vector3 c, float y0, float y1, Vector3 normal, bool twoFaced)
    {
        if (y1 - y0 < 0.001f)
        {
            return;
        }

        Vector3 p0 = a + Vector3.Up * y0, p1 = c + Vector3.Up * y0, p2 = c + Vector3.Up * y1, p3 = a + Vector3.Up * y1;
        float u = a.DistanceTo(c);
        // Clockwise seen from the side the normal points to: a, its top, c's top, c.
        bool towards = (c - a).Cross(Vector3.Up).Dot(normal) > 0f;
        if (towards)
        {
            m.Quad(material, p0, p3, p2, p1, normal, new Vector2(0f, y0), new Vector2(0f, y1), new Vector2(u, y1), new Vector2(u, y0));
        }
        else
        {
            m.Quad(material, p0, p1, p2, p3, normal, new Vector2(0f, y0), new Vector2(u, y0), new Vector2(u, y1), new Vector2(0f, y1));
        }

        if (twoFaced)
        {
            Vector3 back = -normal * 0.002f;
            p0 += back;
            p1 += back;
            p2 += back;
            p3 += back;
            if (towards)
            {
                m.Quad(material, p0, p1, p2, p3, -normal, new Vector2(0f, y0), new Vector2(u, y0), new Vector2(u, y1), new Vector2(0f, y1));
            }
            else
            {
                m.Quad(material, p0, p3, p2, p1, -normal, new Vector2(0f, y0), new Vector2(0f, y1), new Vector2(u, y1), new Vector2(u, y0));
            }
        }
    }
}
