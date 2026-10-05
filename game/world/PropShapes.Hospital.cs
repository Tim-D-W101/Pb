using System;
using Godot;

namespace Pb.Game.World;

/// <summary>
/// The hospital's props, built in code from their colliders (kit/props.jsonc): ward beds and their curtains, lockers,
/// trolleys and wheelchairs, benches, the operating table, an ambulance wreck, the courtyard's fountain, and the boiler
/// house's boilers and chimney. Where paint flies under something (a bed, a bench, the ambulance), a walking-only
/// collider fills the gap and isn't drawn.
/// </summary>
public static partial class PropShapes
{
    /// <summary>A ward bed: tubular frame on castors, a sagging mattress, a stained pillow, head and foot rails.</summary>
    private static void HospitalBed(Ctx c)
    {
        Vector3 mattress = c.Center(0), size = c.Size(0);
        int cloth = c.Default, frame = c.Mat("steel_painted"), chrome = c.Mat("steel_galvanised"), rubber = c.Mat("rubber_plain");
        int sheet = c.Mat("nylon");
        float bottom = mattress.Y - size.Y * 0.5f, hx = size.X * 0.5f, hz = size.Z * 0.5f;
        c.M.Pillow(cloth, mattress, size, Basis.Identity, 8f, 6, 14);
        if (c.Chance(0.6f))
        {
            // A sheet half pulled off, hanging over one side.
            float sx = c.Chance(0.5f) ? 1f : -1f;
            c.M.Box(sheet, mattress + new Vector3(0f, size.Y * 0.5f + 0.01f, c.R(0f, 0.3f)), new Vector3(size.X + 0.02f, 0.015f, size.Z * c.R(0.5f, 0.8f)));
            c.M.Box(sheet, mattress + new Vector3(sx * (hx + 0.01f), -0.1f, c.R(0f, 0.3f)), new Vector3(0.015f, 0.35f, size.Z * 0.5f));
        }

        c.M.Pillow(sheet, mattress + new Vector3(0f, size.Y * 0.5f + 0.06f, -hz + 0.3f), new Vector3(0.6f, 0.12f, 0.4f), Basis.Identity, 10f, 4, 10);
        // The frame: side rails, a cross bar under the mattress, legs on castors.
        foreach (float sx in new[] { -1f, 1f })
        {
            c.M.Rod(frame, new Vector3(sx * (hx + 0.02f), bottom - 0.03f, -hz), new Vector3(sx * (hx + 0.02f), bottom - 0.03f, hz), 0.025f, 6);
            foreach (float sz in new[] { -1f, 1f })
            {
                var leg = new Vector3(sx * (hx - 0.02f), 0f, mattress.Z + sz * (hz - 0.05f));
                c.M.Rod(frame, leg + new Vector3(0f, 0.1f, 0f), leg + new Vector3(0f, bottom, 0f), 0.022f, 6);
                c.M.Cylinder(rubber, leg + new Vector3(0f, 0.05f, 0f), new Basis(Vector3.Back, Mathf.Pi / 2f), 0.05f, 0.03f, 8);
            }
        }

        for (float z = -hz + 0.3f; z < hz; z += 0.45f)
        {
            c.M.Rod(frame, new Vector3(-hx, bottom - 0.03f, mattress.Z + z), new Vector3(hx, bottom - 0.03f, mattress.Z + z), 0.015f, 4);
        }

        // Head and foot: a tube frame round a panel.
        for (int i = 1; i <= 2; i++)
        {
            Vector3 board = c.Center(i), bs = c.Size(i);
            float top = board.Y + bs.Y * 0.5f, low = board.Y - bs.Y * 0.5f, half = bs.X * 0.5f;
            c.M.Box(frame, board + new Vector3(0f, -0.04f, 0f), new Vector3(bs.X - 0.12f, bs.Y - 0.2f, 0.02f));
            foreach (float sx in new[] { -1f, 1f })
            {
                c.M.Rod(chrome, new Vector3(sx * half, low, board.Z), new Vector3(sx * half, top, board.Z), 0.025f, 8);
            }

            c.M.Rod(chrome, new Vector3(-half, top, board.Z), new Vector3(half, top, board.Z), 0.025f, 8);
        }
    }

    /// <summary>A curtain on its ceiling rail: folds hanging from rings, faded and torn at the hem.</summary>
    private static void CurtainScreen(Ctx c)
    {
        Vector3 center = c.Center(), size = c.Size();
        int cloth = c.Default, rail = c.Mat("steel_galvanised");
        float top = center.Y + size.Y * 0.5f, bottom = center.Y - size.Y * 0.5f, half = size.X * 0.5f;
        c.M.Rod(rail, new Vector3(center.X - half - 0.1f, top + 0.08f, center.Z), new Vector3(center.X + half + 0.1f, top + 0.08f, center.Z), 0.015f, 6);
        // Folds: slim boxes zigzagging either side of the curtain's line, each a little longer or shorter at the hem.
        int folds = Math.Max(4, (int)(size.X / 0.2f));
        float pitch = size.X / folds;
        for (int k = 0; k < folds; k++)
        {
            float x = center.X - half + pitch * (k + 0.5f);
            float hem = bottom + c.R(-0.04f, 0.08f);
            float z = center.Z + (k % 2 == 0 ? -0.03f : 0.03f);
            c.M.Box(cloth, new Vector3(x, (top + hem) * 0.5f, z), new Vector3(pitch * 1.08f, top - hem, 0.012f), new Basis(Vector3.Up, k % 2 == 0 ? 0.35f : -0.35f));
            c.M.Box(rail, new Vector3(x, top + 0.05f, center.Z), new Vector3(0.03f, 0.05f, 0.03f));
        }
    }

    /// <summary>A bank of four lockers: doors with louvres and handles, some hanging open on a dark inside.</summary>
    private static void LockerBank(Ctx c)
    {
        Vector3 center = c.Center(), size = c.Size();
        int steel = c.Default, dark = c.Mat("plastic_black"), chrome = c.Mat("steel_galvanised");
        float bottom = center.Y - size.Y * 0.5f, front = center.Z - size.Z * 0.5f;
        c.M.Box(steel, center + new Vector3(0f, 0f, 0.02f), size - new Vector3(0f, 0f, 0.04f));
        const int doors = 4;
        float w = size.X / doors;
        for (int k = 0; k < doors; k++)
        {
            float x = center.X - size.X * 0.5f + w * (k + 0.5f);
            if (c.Chance(0.3f))
            {
                // Open: the dark inside, and the door swung out on its hinge.
                c.M.Box(dark, new Vector3(x, center.Y + 0.05f, front + 0.01f), new Vector3(w - 0.06f, size.Y - 0.2f, 0.02f));
                float hingeX = x - w * 0.5f + 0.02f;
                float a = c.R(0.6f, 1.6f);
                Vector3 mid = new Vector3(hingeX, center.Y + 0.05f, front) + new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a)) * (w * 0.5f - 0.02f);
                c.M.Box(steel, mid, new Vector3(w - 0.05f, size.Y - 0.2f, 0.015f), new Basis(Vector3.Up, a));
                continue;
            }

            c.M.Box(steel, new Vector3(x, center.Y + 0.05f, front - 0.008f), new Vector3(w - 0.03f, size.Y - 0.2f, 0.015f));
            for (int l = 0; l < 4; l++)
            {
                c.M.Box(dark, new Vector3(x, center.Y + size.Y * 0.35f - l * 0.05f, front - 0.018f), new Vector3(w * 0.5f, 0.012f, 0.008f));
            }

            c.M.Box(chrome, new Vector3(x + w * 0.32f, center.Y, front - 0.03f), new Vector3(0.02f, 0.12f, 0.03f));
        }

        c.M.Box(dark, new Vector3(center.X, bottom + 0.05f, front - 0.005f), new Vector3(size.X, 0.1f, 0.02f));
    }

    /// <summary>A steel trolley: two shelves on corner posts and castors, a few things left on the top.</summary>
    private static void MedicalTrolley(Ctx c)
    {
        Vector3 center = c.Center(), size = c.Size();
        int steel = c.Default, rubber = c.Mat("rubber_plain"), card = c.Mat("hardboard");
        float bottom = center.Y - size.Y * 0.5f, top = center.Y + size.Y * 0.5f, hx = size.X * 0.5f - 0.02f, hz = size.Z * 0.5f - 0.02f;
        foreach (float y in new[] { bottom + 0.2f, top - 0.02f })
        {
            c.M.Box(steel, new Vector3(center.X, y, center.Z), new Vector3(size.X, 0.03f, size.Z));
        }

        foreach (float sx in new[] { -1f, 1f })
        {
            foreach (float sz in new[] { -1f, 1f })
            {
                var post = new Vector3(center.X + sx * hx, 0f, center.Z + sz * hz);
                c.M.Rod(steel, post + new Vector3(0f, 0.09f, 0f), post + new Vector3(0f, top, 0f), 0.012f, 6);
                c.M.Cylinder(rubber, post + new Vector3(0f, 0.045f, 0f), new Basis(Vector3.Back, Mathf.Pi / 2f), 0.04f, 0.025f, 8);
            }
        }

        if (c.Chance(0.6f))
        {
            c.M.Box(card, new Vector3(center.X + c.R(-0.1f, 0.1f), top + 0.06f, center.Z), new Vector3(0.25f, 0.1f, 0.18f), new Basis(Vector3.Up, c.R(0f, 1f)));
        }
    }

    /// <summary>A wheelchair: big wheels with hand rims, small castors in front, the sling seat and back, footrests.</summary>
    private static void Wheelchair(Ctx c)
    {
        Vector3 center = c.Center(), size = c.Size();
        int frame = c.Default, rubber = c.Mat("tyre_rubber"), chrome = c.Mat("steel_galvanised"), cloth = c.Mat("plastic_black");
        float hx = size.X * 0.5f, hz = size.Z * 0.5f;
        Basis axle = new Basis(Vector3.Back, -Mathf.Pi / 2f);
        foreach (float sx in new[] { -1f, 1f })
        {
            var hub = new Vector3(center.X + sx * (hx - 0.03f), 0.3f, center.Z + hz * 0.25f);
            c.M.Lathe(rubber, hub, axle, TyreProfile(0.3f, 0.26f, 0.03f), 16);
            c.M.Cylinder(chrome, hub, axle, 0.27f, 0.006f, 16);
            c.M.Cylinder(chrome, hub, axle, 0.03f, 0.05f, 8);
            c.M.Cylinder(rubber, new Vector3(center.X + sx * (hx - 0.1f), 0.07f, center.Z - hz + 0.08f), axle, 0.07f, 0.03f, 10);
            c.M.Rod(frame, new Vector3(center.X + sx * (hx - 0.1f), 0.12f, center.Z - hz + 0.08f), new Vector3(center.X + sx * (hx - 0.1f), 0.5f, center.Z - hz + 0.15f), 0.012f, 6);
            c.M.Rod(frame, new Vector3(center.X + sx * (hx - 0.1f), 0.5f, center.Z - hz + 0.15f), new Vector3(center.X + sx * (hx - 0.1f), 0.5f, center.Z + hz - 0.1f), 0.012f, 6);
            c.M.Rod(frame, new Vector3(center.X + sx * (hx - 0.1f), 0.5f, center.Z + hz - 0.1f), new Vector3(center.X + sx * (hx - 0.1f), 0.95f, center.Z + hz - 0.05f), 0.012f, 6);
            c.M.Box(cloth, new Vector3(center.X + sx * (hx - 0.1f), 0.12f, center.Z - hz + 0.02f), new Vector3(0.14f, 0.015f, 0.12f));
        }

        c.M.Box(cloth, new Vector3(center.X, 0.5f, center.Z + 0.05f), new Vector3(size.X - 0.22f, 0.02f, size.Z * 0.55f));
        c.M.Box(cloth, new Vector3(center.X, 0.72f, center.Z + hz - 0.08f), new Vector3(size.X - 0.22f, 0.4f, 0.02f), new Basis(Vector3.Right, -0.12f));
    }

    /// <summary>A bench: slats on a seat and a back, on cast iron ends.</summary>
    private static void Bench(Ctx c)
    {
        int wood = c.Default, iron = c.Mat("cast_iron");
        Vector3 seat = c.Center(0), seatSize = c.Size(0), back = c.Center(1), backSize = c.Size(1);
        for (int k = 0; k < 4; k++)
        {
            float z = seat.Z - seatSize.Z * 0.5f + seatSize.Z * (k + 0.5f) / 4f;
            Board(c, wood, new Vector3(seat.X, seat.Y, z), new Vector3(seatSize.X, seatSize.Y, seatSize.Z / 4f - 0.015f), Basis.Identity);
        }

        for (int k = 0; k < 3; k++)
        {
            float y = back.Y - backSize.Y * 0.5f + backSize.Y * (k + 0.5f) / 3f;
            Board(c, wood, new Vector3(back.X, y, back.Z), new Vector3(backSize.X, backSize.Y / 3f - 0.02f, backSize.Z), new Basis(Vector3.Right, -0.1f));
        }

        foreach (float sx in new[] { -0.42f, 0.42f })
        {
            float x = seat.X + sx * seatSize.X;
            c.M.Box(iron, new Vector3(x, seat.Y * 0.5f, seat.Z), new Vector3(0.05f, seat.Y, seatSize.Z * 0.9f));
            c.M.Box(iron, new Vector3(x, back.Y, back.Z + 0.03f), new Vector3(0.05f, backSize.Y + 0.1f, 0.06f), new Basis(Vector3.Right, -0.1f));
            c.M.Box(iron, new Vector3(x, seat.Y + 0.2f, seat.Z), new Vector3(0.05f, 0.04f, seatSize.Z));
        }
    }

    /// <summary>The operating table: a padded top on its pedestal and base, and the round theatre lamp on its arm above.</summary>
    private static void OperatingTable(Ctx c)
    {
        int steel = c.Default, chrome = c.Mat("steel_galvanised"), pad = c.Mat("plastic_black"), enamel = c.Mat("enamel_white");
        Vector3 top = c.Center(0), topSize = c.Size(0);
        c.M.Box(steel, top, topSize);
        c.M.Pillow(pad, top + new Vector3(0f, topSize.Y * 0.5f + 0.03f, 0f), new Vector3(topSize.X - 0.04f, 0.06f, topSize.Z - 0.06f), Basis.Identity, 8f, 4, 12);
        Vector3 post = c.Center(1), postSize = c.Size(1);
        c.M.Box(chrome, post, postSize);
        c.M.Box(steel, new Vector3(post.X, 0.03f, post.Z), new Vector3(0.6f, 0.06f, 0.9f));
        if (c.Count > 2)
        {
            Vector3 lamp = c.Center(2);
            c.M.Cylinder(enamel, lamp, Basis.Identity, c.Radius(2), c.Height(2), 20);
            c.M.Cylinder(chrome, lamp - new Vector3(0f, c.Height(2) * 0.5f + 0.01f, 0f), Basis.Identity, c.Radius(2) * 0.7f, 0.02f, 20);
            c.M.Rod(chrome, lamp + new Vector3(0f, c.Height(2) * 0.5f, 0f), lamp + new Vector3(0.6f, 0.8f, 0f), 0.03f, 8);
            c.M.Rod(chrome, lamp + new Vector3(0.6f, 0.8f, 0f), lamp + new Vector3(0.6f, 1.2f, 0f), 0.04f, 8);
        }
    }

    /// <summary>
    /// A burnt-out ambulance: the box body with its rear doors and the cab in front, scorched and streaked, the chequered
    /// band along its sides blistered, the windows gone, sitting on its rims.
    /// </summary>
    private static void AmbulanceWreck(Ctx c)
    {
        int white = c.Default, soot = c.Mat("plastic_black"), rust = c.Mat("steel_rust"), green = c.Mat("container_green");
        int yellow = c.Mat("steel_yellow"), rubber = c.Mat("tyre_rubber"), steel = c.Mat("steel_painted");
        Vector3 body = c.Center(0), bodySize = c.Size(0), cab = c.Center(1), cabSize = c.Size(1);
        c.M.Box(white, body, bodySize);
        c.M.Box(white, cab, cabSize);
        float bodyBottom = body.Y - bodySize.Y * 0.5f, hx = bodySize.X * 0.5f;
        // The chequered band along the sides and across the back.
        foreach (float sx in new[] { -1f, 1f })
        {
            float z0 = cab.Z - cabSize.Z * 0.5f + 0.1f, z1 = body.Z + bodySize.Z * 0.5f - 0.1f;
            int squares = (int)((z1 - z0) / 0.3f);
            for (int k = 0; k < squares; k++)
            {
                float z = z0 + (k + 0.5f) * (z1 - z0) / squares;
                c.M.Box(k % 2 == 0 ? green : yellow, new Vector3(sx * (hx + 0.006f), bodyBottom + 0.55f, z), new Vector3(0.01f, 0.28f, (z1 - z0) / squares));
            }

            // Scorch across the upper body, the side windows gone dark.
            c.M.Box(soot, new Vector3(sx * (hx + 0.008f), body.Y + bodySize.Y * 0.25f, body.Z + c.R(-0.5f, 0.5f)), new Vector3(0.01f, bodySize.Y * c.R(0.3f, 0.5f), bodySize.Z * c.R(0.4f, 0.8f)));
            c.M.Box(soot, new Vector3(sx * (cabSize.X * 0.5f + 0.008f), cab.Y + cabSize.Y * 0.2f, cab.Z + 0.1f), new Vector3(0.01f, cabSize.Y * 0.35f, cabSize.Z * 0.5f));
        }

        float back = body.Z + bodySize.Z * 0.5f;
        c.M.Box(soot, new Vector3(0f, body.Y + 0.2f, back + 0.008f), new Vector3(0.02f, bodySize.Y - 0.3f, 0.01f));
        c.M.Box(soot, new Vector3(0f, cab.Y + cabSize.Y * 0.2f, cab.Z - cabSize.Z * 0.5f - 0.01f), new Vector3(cabSize.X - 0.2f, cabSize.Y * 0.35f, 0.02f));
        c.M.Box(steel, new Vector3(0f, cab.Y - cabSize.Y * 0.35f, cab.Z - cabSize.Z * 0.5f - 0.05f), new Vector3(cabSize.X, 0.2f, 0.1f));
        c.M.Box(rust, new Vector3(0f, bodyBottom - 0.12f, body.Z - 0.5f), new Vector3(1.0f, 0.24f, bodySize.Z + cabSize.Z));
        for (int i = 3; i < c.Count; i++)
        {
            if (c.IsCylinder(i))
            {
                Wheel(c, rubber, rust, c.Center(i), c.Radius(i), c.Height(i));
            }
        }
    }

    /// <summary>The dry fountain: the basin's rim and its bed of leaves, the column, the bowl on top.</summary>
    private static void Fountain(Ctx c)
    {
        int stone = c.Default, leaves = c.Mat("dirt");
        Vector3 basin = c.Center(0);
        float r = c.Radius(0), h = c.Height(0), top = basin.Y + h * 0.5f;
        // Up the outside, across the top and down the inside (walked so each face looks out of the stone).
        var rim = new System.Collections.Generic.List<Vector2>
        {
            new(r, basin.Y - h * 0.5f), new(r, top), new(r - 0.25f, top), new(r - 0.25f, top - 0.25f),
        };
        c.M.Lathe(stone, new Vector3(basin.X, 0f, basin.Z), Basis.Identity, rim, 28);
        c.M.Cylinder(leaves, new Vector3(basin.X, top - 0.24f, basin.Z), Basis.Identity, r - 0.25f, 0.02f, 28);
        for (int i = 1; i < c.Count; i++)
        {
            c.M.Cylinder(stone, c.Center(i), Basis.Identity, c.Radius(i), c.Height(i), 20);
        }
    }

    /// <summary>A boiler: the riveted shell with its bands, the burner and its door at the front, the flue at the back.</summary>
    private static void Boiler(Ctx c)
    {
        int shell = c.Default, brick = c.Mat("brick_red"), machine = c.Mat("machinery"), rust = c.Mat("steel_rust");
        Vector3 vessel = c.Center(0);
        float r = c.Radius(0), length = c.Height(0);
        Basis along = new Basis(Vector3.Right, Mathf.Pi * 0.5f);
        c.M.Cylinder(shell, vessel, along, r, length, 24);
        for (float t = -0.4f; t <= 0.41f; t += 0.2f)
        {
            c.M.Cylinder(rust, vessel + new Vector3(0f, 0f, t * length), along, r + 0.025f, 0.08f, 24, caps: false);
        }

        c.M.Box(brick, c.Center(1), c.Size(1));
        Vector3 burner = c.Center(2), bs = c.Size(2);
        c.M.Box(machine, burner, bs);
        c.M.Cylinder(rust, burner + new Vector3(0f, 0f, -bs.Z * 0.5f - 0.02f), along, bs.X * 0.3f, 0.04f, 16);
        if (c.Count > 3)
        {
            c.M.Cylinder(rust, c.Center(3), Basis.Identity, c.Radius(3), c.Height(3), 14);
        }
    }

    /// <summary>The chimney: a brick stack tapering a little, with bands and a corbelled top, on its square base.</summary>
    private static void Chimney(Ctx c)
    {
        int brick = c.Default, band = c.Mat("concrete_wall");
        Vector3 stack = c.Center(0);
        float r = c.Radius(0), h = c.Height(0), bottom = stack.Y - h * 0.5f;
        var profile = new System.Collections.Generic.List<Vector2>
        {
            new(r, bottom), new(r * 0.82f, bottom + h - 0.8f), new(r * 0.95f, bottom + h - 0.6f), new(r * 0.95f, bottom + h), new(r * 0.7f, bottom + h),
        };
        c.M.Lathe(brick, new Vector3(stack.X, 0f, stack.Z), Basis.Identity, profile, 20);
        foreach (float f in new[] { 0.3f, 0.6f, 0.85f })
        {
            float y = bottom + h * f;
            float rr = Mathf.Lerp(r, r * 0.82f, f) + 0.04f;
            c.M.Cylinder(band, new Vector3(stack.X, y, stack.Z), Basis.Identity, rr, 0.25f, 20, caps: false);
        }

        if (c.Count > 1)
        {
            c.M.Box(brick, c.Center(1), c.Size(1));
            c.M.Box(band, c.Center(1) + new Vector3(0f, c.Size(1).Y * 0.5f, 0f), c.Size(1) + new Vector3(0.1f, -c.Size(1).Y + 0.12f, 0.1f));
        }
    }
}
