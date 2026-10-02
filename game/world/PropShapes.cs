using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Level;

namespace Pb.Game.World;

/// <summary>
/// Detail models for props built in code (kit/props.jsonc "shape"), from each prop's own colliders
/// and the kit's materials: pallets made of boards, crates with battens, stacked tyres, ribbed
/// drums, sandbag walls, a burnt-out car, the water tower and so on. They're presentation only: the
/// colliders stay what paint and walking hit, so every shape keeps inside its colliders, give or
/// take a handle or a lip. A generated model (the prop's "model") is drawn instead when it loads;
/// with neither, the colliders are drawn as greybox. Each prop is varied by a seed from where it
/// stands, so two pallet stacks never look the same but a level always looks the same.
/// </summary>
public static class PropShapes
{
    private static readonly Dictionary<string, Action<Ctx>> Recipes = new(StringComparer.Ordinal)
    {
        ["pallets"] = Pallets,
        ["crate"] = Crate,
        ["tyres"] = Tyres,
        ["drum"] = Drum,
        ["sandbags"] = Sandbags,
        ["barrier"] = Barrier,
        ["desk"] = Desk,
        ["table_on_edge"] = TableOnEdge,
        ["filing_cabinet"] = FilingCabinet,
        ["racking"] = Racking,
        ["rubble"] = Rubble,
        ["scrap"] = Scrap,
        ["car_wreck"] = CarWreck,
        ["water_tower"] = WaterTower,
        ["cable_reel"] = CableReel,
        ["generator"] = Generator,
        ["pump"] = Pump,
        ["skip"] = Skip,
        ["forklift"] = Forklift,
        ["roller_door"] = RollerDoor,
        ["container"] = Container,
    };

    public static IEnumerable<string> Kinds => Recipes.Keys;

    public static bool Has(string kind) => Recipes.ContainsKey(kind);

    /// <summary>
    /// Adds the shape <paramref name="kind"/> for a prop of <paramref name="type"/> to <paramref name="mesh"/>,
    /// which is already placed at the prop. <paramref name="material"/> gives a kit material's index by id
    /// (−1 if the kit has none), for parts that aren't the colliders' own material.
    /// </summary>
    public static void Build(string kind, ShapeMesh mesh, PropType type, int seed, Func<string, int> material) =>
        Recipes[kind](new Ctx(mesh, type, seed, material));

    // ---------------------------------------------------------------- wood

    /// <summary>A stack of block pallets filling the collider, each a little askew; now and then a board is missing.</summary>
    private static void Pallets(Ctx c)
    {
        Vector3 size = c.Size(), center = c.Center();
        float baseY = center.Y - size.Y * 0.5f;
        int count = Math.Max(1, (int)MathF.Round(size.Y / 0.144f));
        float step = size.Y / count;
        for (int i = 0; i < count; i++)
        {
            var offset = new Vector3(center.X + c.R(-0.025f, 0.025f), baseY + i * step, center.Z + c.R(-0.025f, 0.025f));
            Pallet(c, c.Default, size.X - 0.05f, size.Z - 0.05f, step / 0.144f, offset, new Basis(Vector3.Up, c.R(-0.035f, 0.035f)));
        }
    }

    /// <summary>
    /// One pallet, lengthwise along X: three bottom boards, nine blocks, three stringer boards and
    /// seven deck boards across them, 0.144 m tall times <paramref name="scale"/>.
    /// </summary>
    private static void Pallet(Ctx c, int wood, float length, float width, float scale, Vector3 at, Basis turn)
    {
        float t = 0.022f * scale, block = 0.078f * scale;
        float zs = width * 0.5f - 0.05f, xs = length * 0.5f - 0.05f;
        Vector3 P(float x, float y, float z) => at + turn * new Vector3(x, y, z);

        for (int r = -1; r <= 1; r++)
        {
            Board(c, wood, P(0f, t * 0.5f, r * zs), new Vector3(length, t, 0.1f), turn);
            Board(c, wood, P(0f, t * 1.5f + block, r * zs), new Vector3(length, t, 0.1f), turn);
            for (int k = -1; k <= 1; k++)
            {
                Board(c, wood, P(k * xs, t + block * 0.5f, r * zs), new Vector3(0.1f, block, 0.12f), turn);
            }
        }

        Basis across = turn * new Basis(Vector3.Up, Mathf.Pi / 2f);
        for (int i = 0; i < 7; i++)
        {
            if (c.Chance(0.05f))
            {
                continue;
            }

            float x = Mathf.Lerp(-length * 0.5f + 0.07f, length * 0.5f - 0.07f, i / 6f);
            Board(c, wood, P(x, t * 2.5f + block, 0f), new Vector3(width, t, c.R(0.09f, 0.14f)), across);
        }
    }

    /// <summary>A crate of planks with battens round the edge of each side and the lid, and a diagonal brace.</summary>
    private static void Crate(Ctx c)
    {
        Vector3 size = c.Size(), center = c.Center();
        const float t = 0.025f, w = 0.09f;
        int wood = c.Default;
        c.M.Box(wood, center, size - new Vector3(2f * t, 2f * t, 2f * t));

        void Side(Vector3 normal, Vector3 right, Vector3 up)
        {
            float halfN = Mathf.Abs(normal.Dot(size)) * 0.5f;
            float halfW = Mathf.Abs(right.Dot(size)) * 0.5f;
            float halfH = Mathf.Abs(up.Dot(size)) * 0.5f;
            Vector3 o = center + normal * (halfN - t * 0.5f);

            void Batten(Vector3 from, Vector3 to)
            {
                Vector3 along = (to - from).Normalized();
                Board(c, wood, (from + to) * 0.5f, new Vector3((to - from).Length(), t, w), new Basis(along, normal, along.Cross(normal)));
            }

            float hh = halfH - w * 0.5f, hw = halfW - w * 0.5f;
            Batten(o - right * halfW + up * hh, o + right * halfW + up * hh);
            Batten(o - right * halfW - up * hh, o + right * halfW - up * hh);
            Batten(o - right * hw - up * (halfH - w), o - right * hw + up * (halfH - w));
            Batten(o + right * hw - up * (halfH - w), o + right * hw + up * (halfH - w));
            float flip = c.Chance(0.5f) ? 1f : -1f;
            Batten(o - right * flip * (halfW - w) - up * (halfH - w), o + right * flip * (halfW - w) + up * (halfH - w));
        }

        Side(Vector3.Forward, Vector3.Right, Vector3.Up);
        Side(Vector3.Back, Vector3.Left, Vector3.Up);
        Side(Vector3.Left, Vector3.Forward, Vector3.Up);
        Side(Vector3.Right, Vector3.Back, Vector3.Up);
        Side(Vector3.Up, Vector3.Right, Vector3.Back);
    }

    /// <summary>
    /// A wooden cable drum on its rim, in the collider's frame (+Y along the axle): two flanges cut
    /// from planks, a core with a few turns of old cable, and bolts through the flanges.
    /// </summary>
    private static void CableReel(Ctx c)
    {
        float r = c.Radius(), h = c.Height();
        Vector3 center = c.Center();
        Basis rot = c.Rot();
        int wood = c.Default, steel = c.Mat("steel_rust"), cable = c.Mat("tyre_rubber");
        const float ft = 0.05f;
        // Outline in (rot.Z, rot.X), extruded along the axle, so the planks run straight across the face.
        var flangeBasis = new Basis(rot.Z, rot.X, rot.Y);
        Vector2[] disc = Circle(r, 28);
        foreach (float s in new[] { -1f, 1f })
        {
            Vector3 fc = center + rot.Y * (s * (h * 0.5f - ft * 0.5f));
            c.M.Extrude(wood, fc, flangeBasis, disc, ft);
            c.M.Cylinder(steel, fc + rot.Y * (s * ft * 0.5f), rot, 0.07f, 0.012f, 12);
            for (int b = 0; b < 6; b++)
            {
                float a = b * Mathf.Tau / 6f + 0.3f;
                Vector3 radial = rot.X * Mathf.Cos(a) + rot.Z * Mathf.Sin(a);
                c.M.Cylinder(steel, fc + radial * (r * 0.62f), rot, 0.018f, ft + 0.035f, 6);
            }
        }

        float core = r * 0.36f;
        c.M.Cylinder(wood, center, rot, core, h - 2f * ft, 18, caps: false);
        int turns = c.Rng.Next(2, 6);
        for (int i = 0; i < turns; i++)
        {
            Vector3 at = center + rot.Y * (h * 0.5f - ft - 0.03f - i * 0.045f);
            Ring(c, cable, at, rot.X, rot.Z, core + 0.022f, 0.021f, 0f, Mathf.Tau, 20, 6);
        }
    }

    // ---------------------------------------------------------------- stacks and walls

    /// <summary>Old tyres lying flat on each other, each a little off centre and tilted.</summary>
    private static void Tyres(Ctx c)
    {
        float radius = c.Radius(), height = c.Height();
        Vector3 center = c.Center();
        float baseY = center.Y - height * 0.5f;
        int count = Math.Max(1, (int)MathF.Round(height / 0.21f));
        float w = height / count;
        for (int i = 0; i < count; i++)
        {
            float outer = radius - c.R(0f, 0.04f);
            var at = new Vector3(center.X + c.R(-0.03f, 0.03f), baseY + w * (i + 0.5f), center.Z + c.R(-0.03f, 0.03f));
            Basis tilt = new Basis(Vector3.Right, c.R(-0.03f, 0.03f)) * new Basis(Vector3.Forward, c.R(-0.03f, 0.03f)) * new Basis(Vector3.Up, c.R(0f, Mathf.Tau));
            c.M.Lathe(c.Default, at, tilt, TyreProfile(outer, outer * 0.56f, w * 0.97f), 18);
        }
    }

    /// <summary>
    /// A tyre's cross-section as a closed loop in (radius, height), walked counter-clockwise so the
    /// lathe's surface faces out: up the tread, over the shoulder, in along the sidewall, round the
    /// bead, down the bore and back out underneath.
    /// </summary>
    private static List<Vector2> TyreProfile(float outer, float inner, float width)
    {
        float hw = width * 0.5f, rs = MathF.Min(0.05f, hw * 0.6f);
        const float rb = 0.015f;
        var p = new List<Vector2> { new(outer, -hw + rs) };
        Arc(p, new Vector2(outer - rs, hw - rs), rs, 0f, Mathf.Pi / 2f, 3);
        Arc(p, new Vector2(inner + rb, hw - rb), rb, Mathf.Pi / 2f, Mathf.Pi, 2);
        Arc(p, new Vector2(inner + rb, -hw + rb), rb, Mathf.Pi, 1.5f * Mathf.Pi, 2);
        Arc(p, new Vector2(outer - rs, -hw + rs), rs, 1.5f * Mathf.Pi, Mathf.Tau, 3);
        return p;
    }

    /// <summary>A steel drum with rolling hoops, a rim round its lid and two bungs; in the collider's frame, so a tipped one lies down.</summary>
    private static void Drum(Ctx c)
    {
        float r = c.Radius(), h = c.Height(), hh = h * 0.5f;
        Vector3 center = c.Center();
        Basis rot = c.Rot();
        var p = new List<Vector2> { new(0f, -hh), new(r - 0.012f, -hh), new(r, -hh + 0.012f) };
        foreach (float f in new[] { 1f / 3f, 2f / 3f })
        {
            float y = -hh + h * f;
            p.Add(new Vector2(r, y - 0.022f));
            p.Add(new Vector2(r + 0.009f, y - 0.008f));
            p.Add(new Vector2(r + 0.009f, y + 0.008f));
            p.Add(new Vector2(r, y + 0.022f));
        }

        p.Add(new Vector2(r, hh - 0.012f));
        p.Add(new Vector2(r + 0.004f, hh));
        p.Add(new Vector2(r - 0.014f, hh));
        p.Add(new Vector2(r - 0.018f, hh - 0.014f));
        p.Add(new Vector2(0f, hh - 0.014f));
        c.M.Lathe(c.Default, center, rot, p, 22);
        c.M.Cylinder(c.Default, center + rot * new Vector3(r * 0.55f, hh - 0.008f, 0f), rot, 0.032f, 0.014f, 8);
        c.M.Cylinder(c.Default, center + rot * new Vector3(-r * 0.6f, hh - 0.009f, 0.05f), rot, 0.022f, 0.012f, 8);
    }

    /// <summary>
    /// A sandbag wall: courses of bags two rows deep; every other course is shifted half a bag, with
    /// a bag laid across both rows at each end to close the bond.
    /// </summary>
    private static void Sandbags(Ctx c)
    {
        Vector3 size = c.Size(), center = c.Center();
        float baseY = center.Y - size.Y * 0.5f;
        int courses = Math.Max(1, (int)MathF.Round(size.Y / 0.155f));
        float ch = size.Y / courses;
        int perRow = Math.Max(1, (int)MathF.Round(size.X / 0.6f));
        float bagL = size.X / perRow, bagW = size.Z * 0.5f;
        for (int k = 0; k < courses; k++)
        {
            float y = baseY + ch * (k + 0.5f);
            bool shifted = k % 2 == 1 && perRow > 1;
            for (int row = 0; row < 2; row++)
            {
                float z = center.Z + (row - 0.5f) * bagW;
                int n = shifted ? perRow - 1 : perRow;
                for (int i = 0; i < n; i++)
                {
                    float x = center.X - size.X * 0.5f + bagL * (i + (shifted ? 1f : 0.5f));
                    Bag(c, new Vector3(x, y, z), new Vector3(bagL * 0.98f, ch * 1.12f, bagW * 0.97f), 0f);
                }
            }

            if (shifted)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    Bag(c, new Vector3(center.X + side * (size.X * 0.5f - bagL * 0.25f), y, center.Z),
                        new Vector3(size.Z * 0.97f, ch * 1.12f, bagL * 0.48f), Mathf.Pi / 2f);
                }
            }
        }
    }

    private static void Bag(Ctx c, Vector3 at, Vector3 size, float yaw)
    {
        Basis turn = new Basis(Vector3.Up, yaw + c.R(-0.06f, 0.06f)) * new Basis(Vector3.Right, c.R(-0.04f, 0.04f)) * new Basis(Vector3.Forward, c.R(-0.05f, 0.05f));
        var jitter = new Vector3(c.R(-0.015f, 0.015f), c.R(-0.006f, 0.006f), c.R(-0.015f, 0.015f));
        c.M.Pillow(c.Default, at + jitter, size * c.R(0.95f, 1.02f), turn, 3.2f, 5, 9);
    }

    /// <summary>A precast safety barrier: a foot, the steep toe slope, a near-vertical face and a narrow top, with two lifting loops.</summary>
    private static void Barrier(Ctx c)
    {
        Vector3 size = c.Size(), center = c.Center();
        float baseY = center.Y - size.Y * 0.5f;
        float sx = size.Z / 0.6f, sy = size.Y / 0.82f;
        var outline = new List<Vector2>();
        foreach ((float x, float y) in new[] { (-0.3f, 0f), (0.3f, 0f), (0.3f, 0.075f), (0.205f, 0.33f), (0.09f, 0.8f), (0.075f, 0.82f), (-0.075f, 0.82f), (-0.09f, 0.8f), (-0.205f, 0.33f), (-0.3f, 0.075f) })
        {
            outline.Add(new Vector2(x * sx, y * sy));
        }

        // Outline across the barrier (Z, up), extruded along its length (X).
        var basis = new Basis(Vector3.Back, Vector3.Up, Vector3.Left);
        c.M.Extrude(c.Default, new Vector3(center.X, baseY, center.Z), basis, outline, size.X - 0.01f);
        int steel = c.Mat("steel_rust");
        foreach (float s in new[] { -0.3f, 0.3f })
        {
            Ring(c, steel, new Vector3(center.X + s * size.X, baseY + size.Y, center.Z), Vector3.Right, Vector3.Up, 0.05f, 0.009f, 0f, Mathf.Pi, 8, 5);
        }
    }

    // ---------------------------------------------------------------- office

    /// <summary>A steel office desk: a top, a drawer pedestal on the right, a side panel on the left and a modesty panel at the front.</summary>
    private static void Desk(Ctx c)
    {
        Vector3 size = c.Size(), center = c.Center();
        float baseY = center.Y - size.Y * 0.5f;
        int steel = c.Mat("steel_painted"), wood = c.Default;
        float hx = size.X * 0.5f, hz = size.Z * 0.5f;
        Board(c, wood, new Vector3(center.X, baseY + size.Y - 0.015f, center.Z), new Vector3(size.X, 0.03f, size.Z), Basis.Identity);

        float pw = 0.42f, ph = size.Y - 0.03f;
        var pc = new Vector3(center.X + hx - pw * 0.5f - 0.01f, baseY + ph * 0.5f, center.Z);
        c.M.Box(steel, pc, new Vector3(pw, ph, size.Z - 0.04f));
        float dh = (ph - 0.06f) / 3f;
        for (int i = 0; i < 3; i++)
        {
            var front = new Vector3(pc.X, baseY + 0.05f + dh * (i + 0.5f), center.Z + hz - 0.012f);
            c.M.Box(steel, front, new Vector3(pw - 0.04f, dh - 0.015f, 0.016f));
            c.M.Box(steel, front + new Vector3(0f, dh * 0.22f, 0.012f), new Vector3(0.12f, 0.018f, 0.012f));
        }

        c.M.Box(steel, new Vector3(center.X - hx + 0.025f, baseY + ph * 0.5f, center.Z), new Vector3(0.03f, ph, size.Z - 0.04f));
        c.M.Box(steel, new Vector3(center.X - pw * 0.5f, baseY + ph - 0.24f, center.Z - hz + 0.04f), new Vector3(size.X - pw - 0.08f, 0.42f, 0.015f));
    }

    /// <summary>A big table tipped on its edge as a barricade: the top faces the front (−Z), its legs stick out behind.</summary>
    private static void TableOnEdge(Ctx c)
    {
        Vector3 size = c.Size(), center = c.Center();
        float baseY = center.Y - size.Y * 0.5f;
        int wood = c.Default, steel = c.Mat("steel_painted");
        const float t = 0.04f;
        float back = center.Z - size.Z * 0.5f + t;
        Board(c, wood, new Vector3(center.X, center.Y, back - t * 0.5f), new Vector3(size.X, size.Y, t), Basis.Identity);
        // The apron under the top, then the legs, now pointing back.
        float inset = 0.09f;
        float x0 = center.X - size.X * 0.5f + inset, x1 = center.X + size.X * 0.5f - inset;
        float y0 = baseY + inset, y1 = baseY + size.Y - inset;
        float apron = back + 0.05f;
        c.M.Bar(steel, new Vector3(x0, y0, apron), new Vector3(x1, y0, apron), 0.03f, 0.1f);
        c.M.Bar(steel, new Vector3(x0, y1, apron), new Vector3(x1, y1, apron), 0.03f, 0.1f);
        c.M.Bar(steel, new Vector3(x0, y0, apron), new Vector3(x0, y1, apron), 0.03f, 0.1f);
        c.M.Bar(steel, new Vector3(x1, y0, apron), new Vector3(x1, y1, apron), 0.03f, 0.1f);
        float leg = c.R(0.38f, 0.5f);
        foreach (float x in new[] { x0, x1 })
        {
            foreach (float y in new[] { y0, y1 })
            {
                c.M.Bar(steel, new Vector3(x, y, back), new Vector3(x, y, back + leg), 0.045f, 0.045f);
            }
        }
    }

    /// <summary>A four-drawer steel filing cabinet, drawers to the front (−Z); one is often left open.</summary>
    private static void FilingCabinet(Ctx c)
    {
        Vector3 size = c.Size(), center = c.Center();
        float baseY = center.Y - size.Y * 0.5f, hz = size.Z * 0.5f;
        int steel = c.Default;
        c.M.Box(steel, new Vector3(center.X, center.Y, center.Z + 0.01f), new Vector3(size.X, size.Y, size.Z - 0.02f));
        const int drawers = 4;
        float dh = (size.Y - 0.08f) / drawers;
        int open = c.Chance(0.6f) ? c.Rng.Next(drawers) : -1;
        for (int i = 0; i < drawers; i++)
        {
            float y = baseY + 0.05f + dh * (i + 0.5f);
            float pulled = i == open ? c.R(0.06f, 0.22f) : 0f;
            float fz = center.Z - hz + 0.012f - pulled;
            if (pulled > 0f)
            {
                c.M.Box(steel, new Vector3(center.X, y - 0.01f, fz + pulled * 0.5f + 0.01f), new Vector3(size.X - 0.06f, dh - 0.05f, pulled + 0.02f));
            }

            c.M.Box(steel, new Vector3(center.X, y, fz), new Vector3(size.X - 0.03f, dh - 0.012f, 0.016f));
            c.M.Box(steel, new Vector3(center.X, y + dh * 0.18f, fz - 0.013f), new Vector3(0.13f, 0.02f, 0.012f));
            c.M.Box(steel, new Vector3(center.X, y + dh * 0.32f, fz - 0.009f), new Vector3(0.08f, 0.035f, 0.004f));
        }
    }

    // ---------------------------------------------------------------- warehouse

    /// <summary>A bay of pallet racking: two laced upright frames, three beam levels with decks, and whatever was left on them.</summary>
    private static void Racking(Ctx c)
    {
        Vector3 size = c.Size(), center = c.Center();
        float baseY = center.Y - size.Y * 0.5f, height = size.Y;
        int posts = c.Default, beams = c.Mat("steel_yellow"), wood = c.Mat("wood_planks");
        int sacks = c.Mat("sandbag"), drums = c.Mat("drum_rust");
        float hx = size.X * 0.5f - 0.05f, hz = size.Z * 0.5f - 0.04f;
        foreach (float sx in new[] { -1f, 1f })
        {
            float x = center.X + sx * hx;
            foreach (float sz in new[] { -1f, 1f })
            {
                c.M.Box(posts, new Vector3(x, baseY + height * 0.5f, center.Z + sz * hz), new Vector3(0.08f, height, 0.07f));
            }

            const int bays = 4;
            for (int b = 0; b <= bays; b++)
            {
                float y = baseY + 0.1f + (height - 0.2f) * b / bays;
                c.M.Bar(posts, new Vector3(x, y, center.Z - hz), new Vector3(x, y, center.Z + hz), 0.035f, 0.035f);
                if (b < bays)
                {
                    float y2 = baseY + 0.1f + (height - 0.2f) * (b + 1) / bays;
                    float z0 = b % 2 == 0 ? -hz : hz;
                    c.M.Bar(posts, new Vector3(x, y, center.Z + z0), new Vector3(x, y2, center.Z - z0), 0.03f, 0.03f);
                }
            }
        }

        foreach (float level in new[] { 0.12f, 0.95f, 1.8f })
        {
            float y = baseY + level * height / 2.5f;
            foreach (float sz in new[] { -1f, 1f })
            {
                c.M.Box(beams, new Vector3(center.X, y, center.Z + sz * hz), new Vector3(2f * hx - 0.08f, 0.1f, 0.05f));
            }

            Board(c, wood, new Vector3(center.X, y + 0.06f, center.Z), new Vector3(2f * hx - 0.1f, 0.02f, 2f * hz), Basis.Identity);
            float x = center.X - hx + 0.12f;
            float room = baseY + height - (y + 0.07f) - 0.04f;
            while (true)
            {
                float w = c.R(0.3f, 0.7f);
                if (x + w > center.X + hx - 0.1f)
                {
                    break;
                }

                if (c.Chance(0.7f))
                {
                    float h = MathF.Min(c.R(0.2f, 0.6f), room);
                    float d = c.R(0.4f, 2f * hz - 0.06f);
                    var at = new Vector3(x + w * 0.5f, y + 0.07f + h * 0.5f, center.Z + c.R(-0.05f, 0.05f));
                    switch (c.Rng.Next(3))
                    {
                        case 0:
                            c.M.Box(wood, at, new Vector3(w, h, d), new Basis(Vector3.Up, c.R(-0.1f, 0.1f)), PlankUv(c));
                            break;
                        case 1:
                            c.M.Pillow(sacks, at - new Vector3(0f, h * 0.2f, 0f), new Vector3(w, h * 0.6f, d), new Basis(Vector3.Up, c.R(-0.2f, 0.2f)), 2.8f, 4, 8);
                            break;
                        default:
                            float r = MathF.Min(w, d) * 0.45f;
                            c.M.Cylinder(drums, at, Basis.Identity, r, h, 14);
                            break;
                    }
                }

                x += w + c.R(0.03f, 0.2f);
            }
        }
    }

    /// <summary>
    /// An old forklift facing −Z: chassis, rounded counterweight, seat under an overhead guard, the
    /// mast in the mast collider with the forks lowered in front, and four solid tyres.
    /// </summary>
    private static void Forklift(Ctx c)
    {
        int yellow = c.Default, dark = c.Mat("steel_painted"), rubber = c.Mat("tyre_rubber"), rust = c.Mat("steel_rust");
        Vector3 bodySize = c.Size(0), body = c.Center(0);
        float baseY = body.Y - bodySize.Y * 0.5f;
        float zFront = body.Z - bodySize.Z * 0.5f, zBack = body.Z + bodySize.Z * 0.5f;
        float hx = bodySize.X * 0.5f;

        // Chassis and counterweight.
        c.M.Box(yellow, new Vector3(body.X, baseY + 0.42f, body.Z - 0.1f), new Vector3(bodySize.X - 0.14f, 0.5f, bodySize.Z - 0.6f));
        c.M.Pillow(yellow, new Vector3(body.X, baseY + 0.62f, zBack - 0.3f), new Vector3(bodySize.X - 0.04f, 0.78f, 0.58f), Basis.Identity, 6f, 6, 14);
        // Engine cover with the seat on it, and the dash with its steering wheel.
        c.M.Box(yellow, new Vector3(body.X, baseY + 0.78f, body.Z + 0.35f), new Vector3(bodySize.X - 0.3f, 0.22f, 0.8f));
        c.M.Pillow(rubber, new Vector3(body.X, baseY + 0.95f, body.Z + 0.42f), new Vector3(0.5f, 0.12f, 0.45f), Basis.Identity, 4f, 4, 10);
        c.M.Pillow(rubber, new Vector3(body.X, baseY + 1.18f, body.Z + 0.66f), new Vector3(0.5f, 0.42f, 0.1f), new Basis(Vector3.Right, -0.2f), 4f, 4, 10);
        c.M.Box(yellow, new Vector3(body.X, baseY + 0.86f, zFront + 0.35f), new Vector3(bodySize.X - 0.2f, 0.3f, 0.3f));
        c.M.Rod(dark, new Vector3(body.X, baseY + 0.95f, zFront + 0.42f), new Vector3(body.X, baseY + 1.2f, zFront + 0.62f), 0.025f);
        Ring(c, rubber, new Vector3(body.X, baseY + 1.21f, zFront + 0.63f), Vector3.Right, (Vector3.Up * 0.62f + Vector3.Back * 0.78f).Normalized(), 0.17f, 0.014f, 0f, Mathf.Tau, 16, 5);

        // Overhead guard: four posts and a roof grid.
        float gy = baseY + bodySize.Y + 0.5f;
        var front = new Vector3(hx - 0.06f, 0f, zFront + 0.22f);
        var rear = new Vector3(hx - 0.06f, 0f, zBack - 0.42f);
        foreach (float s in new[] { -1f, 1f })
        {
            c.M.Bar(dark, new Vector3(body.X + s * front.X, baseY + 0.9f, front.Z), new Vector3(body.X + s * front.X, gy, front.Z + 0.12f), 0.06f, 0.06f);
            c.M.Bar(dark, new Vector3(body.X + s * rear.X, baseY + 1.0f, rear.Z), new Vector3(body.X + s * rear.X, gy, rear.Z), 0.06f, 0.06f);
            c.M.Bar(dark, new Vector3(body.X + s * front.X, gy, front.Z + 0.12f), new Vector3(body.X + s * rear.X, gy, rear.Z), 0.05f, 0.05f);
        }

        for (int i = 0; i <= 4; i++)
        {
            float z = Mathf.Lerp(front.Z + 0.12f, rear.Z, i / 4f);
            c.M.Bar(dark, new Vector3(body.X - front.X, gy, z), new Vector3(body.X + front.X, gy, z), 0.04f, 0.03f);
        }

        // Tyres: big ones at the front, smaller ones under the counterweight.
        foreach (float s in new[] { -1f, 1f })
        {
            Wheel(c, rubber, rust, new Vector3(body.X + s * (hx - 0.11f), baseY + 0.3f, zFront + 0.42f), 0.3f, 0.2f);
            Wheel(c, rubber, rust, new Vector3(body.X + s * (hx - 0.1f), baseY + 0.24f, zBack - 0.4f), 0.24f, 0.17f);
        }

        if (c.Count < 2)
        {
            return;
        }

        // Mast: two channels, cross members and a lift cylinder; the carriage and forks lowered in front.
        Vector3 mastSize = c.Size(1), mast = c.Center(1);
        float mBase = mast.Y - mastSize.Y * 0.5f, mTop = mast.Y + mastSize.Y * 0.5f;
        float mx = mastSize.X * 0.5f - 0.06f;
        foreach (float s in new[] { -1f, 1f })
        {
            c.M.Box(dark, new Vector3(mast.X + s * mx, mast.Y, mast.Z), new Vector3(0.08f, mastSize.Y - 0.02f, 0.13f));
        }

        foreach (float f in new[] { 0.12f, 0.55f, 0.97f })
        {
            c.M.Box(dark, new Vector3(mast.X, Mathf.Lerp(mBase, mTop, f), mast.Z), new Vector3(2f * mx, 0.08f, 0.08f));
        }

        c.M.Rod(rust, new Vector3(mast.X, mBase + 0.1f, mast.Z + 0.06f), new Vector3(mast.X, mTop - 0.2f, mast.Z + 0.06f), 0.045f, 10);
        float cz = mast.Z - mastSize.Z * 0.5f - 0.03f;
        c.M.Box(dark, new Vector3(mast.X, mBase + 0.25f, cz), new Vector3(2f * mx + 0.06f, 0.4f, 0.05f));
        foreach (float s in new[] { -0.26f, 0.26f })
        {
            c.M.Box(dark, new Vector3(mast.X + s, mBase + 0.22f, cz - 0.04f), new Vector3(0.1f, 0.44f, 0.04f));
            c.M.Box(dark, new Vector3(mast.X + s, mBase + 0.02f, cz - 0.56f), new Vector3(0.1f, 0.04f, 1.04f));
        }
    }

    private static void Wheel(Ctx c, int rubber, int hub, Vector3 at, float radius, float width)
    {
        Basis axle = new Basis(Vector3.Back, -Mathf.Pi / 2f); // +Y along +X
        c.M.Lathe(rubber, at, axle, TyreProfile(radius, radius * 0.62f, width), 16);
        c.M.Cylinder(hub, at, axle, radius * 0.64f, width * 0.7f, 12);
    }

    /// <summary>A half-open roller door: slats with grooves between them and a bottom bar.</summary>
    private static void RollerDoor(Ctx c)
    {
        Vector3 size = c.Size(), center = c.Center();
        float bottom = center.Y - size.Y * 0.5f;
        const float slat = 0.1f;
        int n = Math.Max(1, (int)(size.Y / slat));
        float pitch = (size.Y - 0.06f) / n;
        for (int i = 0; i < n; i++)
        {
            float y = bottom + 0.06f + pitch * (i + 0.5f);
            c.M.Box(c.Default, new Vector3(center.X, y, center.Z), new Vector3(size.X - 0.02f, pitch - 0.016f, size.Z * 0.6f));
            c.M.Box(c.Default, new Vector3(center.X, y + pitch * 0.5f, center.Z), new Vector3(size.X - 0.02f, 0.02f, size.Z * 0.3f));
        }

        c.M.Box(c.Default, new Vector3(center.X, bottom + 0.03f, center.Z), new Vector3(size.X, 0.06f, size.Z));
    }

    // ---------------------------------------------------------------- yard

    /// <summary>
    /// A shipping container: corrugated walls and roof set inside a frame of corner posts and rails,
    /// cast corner fittings, and doors with locking bars on the +X end.
    /// </summary>
    private static void Container(Ctx c)
    {
        Vector3 size = c.Size(), center = c.Center();
        float baseY = center.Y - size.Y * 0.5f;
        int mat = c.Default, dark = c.Mat("steel_rust");
        float hx = size.X * 0.5f, hz = size.Z * 0.5f;
        const float post = 0.16f, rail = 0.14f;
        c.M.Box(mat, center, new Vector3(size.X - 0.04f, size.Y - 0.06f, size.Z - 0.05f));
        foreach (float sx in new[] { -1f, 1f })
        {
            foreach (float sz in new[] { -1f, 1f })
            {
                c.M.Box(mat, new Vector3(center.X + sx * (hx - post * 0.5f), center.Y, center.Z + sz * (hz - post * 0.5f)), new Vector3(post, size.Y, post));
            }
        }

        foreach (float y in new[] { baseY + rail * 0.5f, baseY + size.Y - rail * 0.5f })
        {
            foreach (float sz in new[] { -1f, 1f })
            {
                c.M.Box(mat, new Vector3(center.X, y, center.Z + sz * (hz - rail * 0.5f)), new Vector3(size.X, rail, rail));
            }

            foreach (float sx in new[] { -1f, 1f })
            {
                c.M.Box(mat, new Vector3(center.X + sx * (hx - rail * 0.5f), y, center.Z), new Vector3(rail, rail, size.Z));
            }

            foreach (float sx in new[] { -1f, 1f })
            {
                foreach (float sz in new[] { -1f, 1f })
                {
                    c.M.Box(dark, new Vector3(center.X + sx * (hx - 0.085f), y, center.Z + sz * (hz - 0.08f)), new Vector3(0.18f, rail + 0.01f, 0.17f));
                }
            }
        }

        // Doors on the +X end: two leaves, four locking bars with handles and keepers.
        float ex = center.X + hx;
        float leafH = size.Y - 2f * rail;
        foreach (float sz in new[] { -1f, 1f })
        {
            c.M.Box(mat, new Vector3(ex + 0.006f, center.Y, center.Z + sz * (hz - post) * 0.5f), new Vector3(0.012f, leafH, hz - post - 0.01f));
        }

        foreach (float f in new[] { -0.72f, -0.3f, 0.3f, 0.72f })
        {
            float z = center.Z + f * (hz - post);
            c.M.Rod(dark, new Vector3(ex + 0.035f, baseY + rail, z), new Vector3(ex + 0.035f, baseY + size.Y - rail, z), 0.017f, 6);
            float side = f < 0f ? 1f : -1f;
            c.M.Bar(dark, new Vector3(ex + 0.05f, baseY + 1.1f, z), new Vector3(ex + 0.05f, baseY + 1.1f, z + side * 0.28f), 0.03f, 0.025f);
            foreach (float y in new[] { baseY + rail + 0.05f, baseY + size.Y - rail - 0.05f })
            {
                c.M.Box(dark, new Vector3(ex + 0.03f, y, z), new Vector3(0.05f, 0.07f, 0.07f));
            }
        }
    }

    /// <summary>
    /// A burnt-out saloon facing −Z, sitting low on its hubs: the body is one side profile (wheel
    /// arches cut out) extruded across, then pillars and a roof, the gutted interior seen through the
    /// empty windows, bumpers, lamps and mirrors.
    /// </summary>
    private static void CarWreck(Ctx c)
    {
        int body = c.Default, rust = c.Mat("steel_rust"), soot = c.Mat("tyre_rubber");
        Vector3 lowerSize = c.Size(0), lower = c.Center(0);
        float hx = lowerSize.X * 0.5f, hl = lowerSize.Z * 0.5f;
        float belt = lower.Y + lowerSize.Y * 0.5f;
        float sill = lower.Y - lowerSize.Y * 0.5f + 0.07f;
        // Lengthwise positions below are relative to the body's centre.
        float cabinFront = -0.85f, cabinBack = 1.25f, roofY = belt + 0.5f;
        if (c.Count > 1)
        {
            Vector3 cabinSize = c.Size(1), cabin = c.Center(1);
            cabinFront = cabin.Z - cabinSize.Z * 0.5f - lower.Z;
            cabinBack = cabin.Z + cabinSize.Z * 0.5f - lower.Z;
            roofY = cabin.Y + cabinSize.Y * 0.5f - 0.03f;
        }

        // The side profile (z, y), front at −Z, walked counter-clockwise: along the bottom (arches
        // cut out), up the tail, forward over the boot, the beltline and the bonnet, down the nose.
        float archR = 0.36f, archY = sill - 0.02f, frontAxle = lower.Z - hl * 0.63f, rearAxle = lower.Z + hl * 0.65f;
        var side = new List<Vector2> { new(lower.Z - hl + 0.1f, sill) };
        void Arch(float z)
        {
            for (int i = 0; i <= 8; i++)
            {
                float a = Mathf.Pi - Mathf.Pi * i / 8f;
                side.Add(new Vector2(z + Mathf.Cos(a) * archR, archY + MathF.Max(Mathf.Sin(a), 0.06f) * archR));
            }
        }

        Arch(frontAxle);
        Arch(rearAxle);
        foreach ((float z, float y) in new[]
        {
            (hl - 0.1f, sill), (hl, sill + 0.13f), (hl, sill + 0.42f), (hl - 0.05f, belt - 0.07f), (hl - 0.15f, belt - 0.02f),
            (cabinBack, belt), (cabinFront, belt), (-hl + 0.2f, belt - 0.11f), (-hl + 0.03f, belt - 0.17f), (-hl, belt - 0.33f), (-hl, sill + 0.16f),
        })
        {
            side.Add(new Vector2(lower.Z + z, y));
        }

        // Profile in (Z, up), extruded across the car (X).
        var across = new Basis(Vector3.Back, Vector3.Up, Vector3.Left);
        c.M.Extrude(body, new Vector3(lower.X, 0f, 0f), across, side, 2f * hx - 0.04f);

        // Wheel wells closed off on the inside, and the bare hubs it rests on.
        foreach (float z in new[] { frontAxle, rearAxle })
        {
            foreach (float s in new[] { -1f, 1f })
            {
                c.M.Box(soot, new Vector3(lower.X + s * (hx - 0.32f), archY + archR * 0.5f, z), new Vector3(0.02f, archR, 2f * archR));
                c.M.Cylinder(rust, new Vector3(lower.X + s * (hx - 0.12f), 0.19f, z + c.R(-0.03f, 0.03f)), new Basis(Vector3.Back, Mathf.Pi / 2f), 0.19f, 0.1f, 14);
                c.M.Cylinder(rust, new Vector3(lower.X + s * (hx - 0.2f), 0.19f, z), new Basis(Vector3.Back, Mathf.Pi / 2f), 0.08f, 0.14f, 8);
            }
        }

        // Pillars and roof.
        float roofFront = cabinFront + 0.55f, roofBack = cabinBack - 0.42f;
        float px = hx - 0.07f, rx = hx - 0.15f;
        foreach (float s in new[] { -1f, 1f })
        {
            c.M.Bar(body, new Vector3(lower.X + s * px, belt, lower.Z + cabinFront + 0.04f), new Vector3(lower.X + s * rx, roofY, lower.Z + roofFront), 0.07f, 0.06f);
            c.M.Bar(body, new Vector3(lower.X + s * px, belt, lower.Z + (cabinFront + cabinBack) * 0.5f + 0.1f), new Vector3(lower.X + s * rx, roofY, lower.Z + (cabinFront + cabinBack) * 0.5f + 0.1f), 0.08f, 0.06f);
            c.M.Bar(body, new Vector3(lower.X + s * px, belt, lower.Z + cabinBack - 0.04f), new Vector3(lower.X + s * rx, roofY, lower.Z + roofBack), 0.1f, 0.06f);
        }

        c.M.Pillow(body, new Vector3(lower.X, roofY, lower.Z + (roofFront + roofBack) * 0.5f), new Vector3(2f * rx + 0.08f, 0.07f, roofBack - roofFront + 0.1f), Basis.Identity, 7f, 4, 16);

        // The gutted interior: soot over the floor, seat frames, the dash and steering wheel.
        c.M.Box(soot, new Vector3(lower.X, belt + 0.003f, lower.Z + (cabinFront + cabinBack) * 0.5f), new Vector3(2f * hx - 0.12f, 0.006f, cabinBack - cabinFront - 0.1f));
        float seatZ = lower.Z + (cabinFront + cabinBack) * 0.5f + 0.05f;
        foreach (float s in new[] { -1f, 1f })
        {
            c.M.Box(rust, new Vector3(lower.X + s * 0.36f, belt + 0.18f, seatZ + 0.05f), new Vector3(0.44f, 0.36f, 0.06f), new Basis(Vector3.Right, -0.2f));
        }

        c.M.Box(rust, new Vector3(lower.X, belt + 0.14f, lower.Z + cabinBack - 0.25f), new Vector3(2f * hx - 0.3f, 0.28f, 0.06f), new Basis(Vector3.Right, -0.25f));
        c.M.Box(soot, new Vector3(lower.X, belt + 0.06f, lower.Z + cabinFront + 0.18f), new Vector3(2f * hx - 0.16f, 0.12f, 0.24f));
        Ring(c, rust, new Vector3(lower.X - 0.36f, belt + 0.2f, lower.Z + cabinFront + 0.36f), Vector3.Right, (Vector3.Up * 0.8f + Vector3.Back * 0.6f).Normalized(), 0.17f, 0.013f, 0f, Mathf.Tau, 14, 4);

        // Bumpers, lamps, grille, door handles and mirrors.
        c.M.Box(rust, new Vector3(lower.X, sill + 0.2f, lower.Z - hl - 0.03f), new Vector3(2f * hx - 0.1f, 0.12f, 0.08f));
        c.M.Box(rust, new Vector3(lower.X, sill + 0.24f, lower.Z + hl + 0.03f), new Vector3(2f * hx - 0.1f, 0.12f, 0.08f));
        foreach (float s in new[] { -1f, 1f })
        {
            c.M.Box(soot, new Vector3(lower.X + s * (hx - 0.3f), belt - 0.25f, lower.Z - hl - 0.002f), new Vector3(0.26f, 0.12f, 0.02f));
            c.M.Box(soot, new Vector3(lower.X + s * (hx - 0.28f), belt - 0.2f, lower.Z + hl + 0.002f), new Vector3(0.3f, 0.1f, 0.02f));
            foreach (float dz in new[] { -0.3f, 0.6f })
            {
                c.M.Box(rust, new Vector3(lower.X + s * (hx + 0.008f), belt - 0.12f, lower.Z + dz), new Vector3(0.016f, 0.03f, 0.12f));
            }

            c.M.Box(body, new Vector3(lower.X + s * (hx + 0.08f), belt + 0.08f, lower.Z + cabinFront + 0.12f), new Vector3(0.14f, 0.09f, 0.05f));
        }

        c.M.Box(soot, new Vector3(lower.X, belt - 0.25f, lower.Z - hl - 0.002f), new Vector3(0.62f, 0.12f, 0.02f));
    }

    /// <summary>
    /// A heap of rusty scrap: corrugated sheets, pipes, girders, broken machine parts and a wheel rim
    /// lying on a low mound, mostly hiding it.
    /// </summary>
    private static void Scrap(Ctx c)
    {
        Vector3 size = c.Size(0), center = c.Center(0);
        int rust = c.Default, sheet = c.Mat("corrugated_rust"), dark = c.Mat("steel_painted"), drum = c.Mat("drum_rust");
        var mound = new Mound(c, center, size, 0.8f);
        for (int i = 0; i < 10; i++)
        {
            mound.Lay(sheet, new Vector3(c.R(0.6f, 1.3f), 0.012f, c.R(0.4f, 0.9f)), 0f);
        }

        for (int i = 0; i < 9; i++)
        {
            (Vector3 at, Vector3 normal) = mound.Spot(0.04f);
            Vector3 along = normal.Cross(new Basis(Vector3.Up, c.R(0f, Mathf.Tau)) * Vector3.Right).Normalized();
            float half = c.R(0.35f, 0.8f), radius = c.R(0.025f, 0.06f);
            c.M.Rod(i % 3 == 0 ? dark : rust, at + normal * radius - along * half, at + normal * radius + along * half, radius, 10);
        }

        for (int i = 0; i < 8; i++)
        {
            mound.Lay(i % 2 == 0 ? rust : dark, new Vector3(c.R(0.15f, 0.45f), c.R(0.1f, 0.3f), c.R(0.15f, 0.4f)), 0.15f);
        }

        var ibeam = new List<Vector2>
        {
            new(-0.06f, -0.08f), new(0.06f, -0.08f), new(0.06f, -0.07f), new(0.005f, -0.07f), new(0.005f, 0.07f), new(0.06f, 0.07f),
            new(0.06f, 0.08f), new(-0.06f, 0.08f), new(-0.06f, 0.07f), new(-0.005f, 0.07f), new(-0.005f, -0.07f), new(-0.06f, -0.07f),
        };
        for (int i = 0; i < 2; i++)
        {
            (Vector3 at, Vector3 normal) = mound.Spot(0.08f);
            Basis lie = Conv.BasisFromUp(normal, c.R(0f, Mathf.Tau)) * new Basis(Vector3.Right, Mathf.Pi / 2f);
            c.M.Extrude(rust, at, lie, ibeam, c.R(1.2f, 1.9f));
        }

        (Vector3 rimAt, Vector3 rimUp) = mound.Spot(0.05f);
        Vector3 rimU = rimUp.Cross(Vector3.Forward).Normalized();
        Ring(c, dark, rimAt + rimUp * 0.05f, rimU, rimU.Cross(rimUp).Normalized(), 0.25f, 0.04f, 0f, Mathf.Tau, 14, 6);
        if (c.Chance(0.6f))
        {
            (Vector3 drumAt, Vector3 drumUp) = mound.Spot(0.2f);
            c.M.Cylinder(drum, drumAt + drumUp * 0.2f, Conv.BasisFromUp(drumUp.Cross(Vector3.Right).Normalized(), 0f), 0.28f, 0.85f, 16);
        }

        if (c.Count > 1)
        {
            Vector3 s1 = c.Size(1);
            c.M.Box(sheet, c.Center(1), new Vector3(s1.X, 0.012f, s1.Z), c.Rot(1));
        }
    }

    /// <summary>A mound of broken concrete: chunks lying all over it, a slab or two leaning on it, bricks and bent rebar.</summary>
    private static void Rubble(Ctx c)
    {
        Vector3 size = c.Size(0), center = c.Center(0);
        int concrete = c.Default, brick = c.Mat("brick_red"), rebar = c.Mat("steel_rust");
        var mound = new Mound(c, center, size, 0.9f);
        int chunks = (int)(size.X * size.Z * 6f) + 10;
        for (int i = 0; i < chunks; i++)
        {
            float s = c.R(0.14f, 0.42f);
            mound.Lay(concrete, new Vector3(s * c.R(0.9f, 1.7f), s * c.R(0.35f, 0.75f), s * c.R(0.8f, 1.4f)), 0.3f);
        }

        for (int i = 1; i < c.Count; i++)
        {
            c.M.Box(concrete, c.Center(i), c.Size(i) * 0.96f, c.Rot(i));
        }

        for (int i = 0; i < 14; i++)
        {
            mound.Lay(brick, new Vector3(0.22f, 0.065f, 0.105f), 0.25f);
        }

        for (int i = 0; i < 6; i++)
        {
            (Vector3 root, Vector3 normal) = mound.Spot(-0.05f);
            Vector3 dir = (normal + new Basis(Vector3.Up, c.R(0f, Mathf.Tau)) * new Vector3(c.R(0.3f, 0.8f), 0f, 0f)).Normalized();
            Vector3 bend = root + dir * c.R(0.3f, 0.6f);
            Vector3 tip = bend + (new Basis(Vector3.Up, c.R(-1f, 1f)) * dir + Vector3.Down * c.R(0.2f, 0.6f)).Normalized() * c.R(0.15f, 0.4f);
            c.M.Rod(rebar, root, bend, 0.008f, 4);
            c.M.Rod(rebar, bend, tip, 0.008f, 4);
        }
    }

    /// <summary>
    /// A low half-ellipsoid heap filling a box collider, with things laid on its surface: each sits
    /// where the heap is, tilted to its slope, sunk in by a share of its own height.
    /// </summary>
    private sealed class Mound
    {
        private readonly Ctx _c;
        private readonly Vector3 _base;
        private readonly Vector3 _half;

        public Mound(Ctx c, Vector3 center, Vector3 size, float fill)
        {
            _c = c;
            _base = new Vector3(center.X, center.Y - size.Y * 0.5f, center.Z);
            _half = new Vector3(size.X * 0.47f, size.Y * fill, size.Z * 0.47f);
            // The heap itself, an exact ellipsoid so its surface is known; it's mostly hidden.
            c.M.Pillow(c.Default, _base, _half * 2f, Basis.Identity, 2f, 6, 14);
        }

        /// <summary>A point on the heap (inside its outer rim) and the surface normal there, lifted by <paramref name="lift"/>.</summary>
        public (Vector3 At, Vector3 Normal) Spot(float lift)
        {
            float a = _c.R(0f, Mathf.Tau), r = MathF.Sqrt(_c.R(0f, 0.85f));
            float u = Mathf.Cos(a) * r, v = Mathf.Sin(a) * r;
            float h = MathF.Sqrt(MathF.Max(1f - u * u - v * v, 0f));
            var normal = new Vector3(u / _half.X, h / _half.Y, v / _half.Z).Normalized();
            Vector3 at = _base + new Vector3(u * _half.X, h * _half.Y, v * _half.Z);
            return (at + normal * lift, normal);
        }

        /// <summary>Lays a box on the heap, tilted to its slope, sunk in by <paramref name="sink"/> × its own height.</summary>
        public void Lay(int material, Vector3 size, float sink)
        {
            (Vector3 at, Vector3 normal) = Spot(0f);
            Basis lie = Conv.BasisFromUp(normal, _c.R(0f, Mathf.Tau)) * new Basis(Vector3.Right, _c.R(-0.25f, 0.25f));
            _c.M.Box(material, at + normal * (size.Y * (0.5f - sink)), size, lie);
        }
    }

    /// <summary>A builder's skip: sloping ends, ribbed sides in old yellow paint, a rim round the top, on two skids.</summary>
    private static void Skip(Ctx c)
    {
        Vector3 size = c.Size(), center = c.Center();
        float baseY = center.Y - size.Y * 0.5f, height = size.Y;
        int rust = c.Default, paint = c.Mat("steel_yellow");
        float topHalf = size.X * 0.5f, botHalf = topHalf - 0.5f, hz = size.Z * 0.5f;
        const float t = 0.035f, floor = 0.12f;
        foreach (float sz in new[] { -0.6f, 0.6f })
        {
            c.M.Box(rust, new Vector3(center.X, baseY + floor * 0.5f, center.Z + sz * hz), new Vector3(2f * botHalf - 0.1f, floor, 0.1f));
        }

        c.M.Box(rust, new Vector3(center.X, baseY + floor + t * 0.5f, center.Z), new Vector3(2f * botHalf, t, size.Z - 2f * t));
        var trapezoid = new[] { new Vector2(-botHalf, floor), new Vector2(botHalf, floor), new Vector2(topHalf, height), new Vector2(-topHalf, height) };
        foreach (float sz in new[] { -1f, 1f })
        {
            c.M.Extrude(paint, new Vector3(center.X, baseY, center.Z + sz * (hz - t * 0.5f)), Basis.Identity, trapezoid, t);
            for (int i = -2; i <= 2; i++)
            {
                float x = center.X + i * botHalf * 0.42f;
                c.M.Box(rust, new Vector3(x, baseY + (floor + height) * 0.5f, center.Z + sz * (hz + 0.012f)), new Vector3(0.07f, height - floor - 0.08f, 0.03f));
            }

            c.M.Box(rust, new Vector3(center.X, baseY + height - 0.035f, center.Z + sz * (hz - 0.02f)), new Vector3(2f * topHalf, 0.07f, 0.07f));
        }

        foreach (float sx in new[] { -1f, 1f })
        {
            c.M.Bar(paint, new Vector3(center.X + sx * botHalf, baseY + floor, center.Z), new Vector3(center.X + sx * topHalf, baseY + height, center.Z), size.Z - 2f * t, t);
            c.M.Box(rust, new Vector3(center.X + sx * (topHalf - 0.02f), baseY + height - 0.035f, center.Z), new Vector3(0.07f, 0.07f, size.Z));
        }
    }

    // ---------------------------------------------------------------- machinery

    /// <summary>A diesel generator: a skid, a canopy with louvres and doors on both sides, an exhaust stack and a control box.</summary>
    private static void Generator(Ctx c)
    {
        Vector3 size = c.Size(), center = c.Center();
        float baseY = center.Y - size.Y * 0.5f;
        int body = c.Default, rust = c.Mat("steel_rust");
        float hx = size.X * 0.5f, hz = size.Z * 0.5f;
        foreach (float sz in new[] { -1f, 1f })
        {
            c.M.Box(rust, new Vector3(center.X, baseY + 0.05f, center.Z + sz * (hz - 0.06f)), new Vector3(size.X, 0.1f, 0.08f));
        }

        for (int i = -1; i <= 1; i++)
        {
            c.M.Box(rust, new Vector3(center.X + i * (hx - 0.1f), baseY + 0.04f, center.Z), new Vector3(0.08f, 0.08f, size.Z - 0.1f));
        }

        float cy0 = baseY + 0.1f, ch = size.Y - 0.16f;
        c.M.Box(body, new Vector3(center.X, cy0 + ch * 0.5f, center.Z), new Vector3(size.X - 0.08f, ch, size.Z - 0.06f));
        c.M.Box(body, new Vector3(center.X, cy0 + ch + 0.02f, center.Z), new Vector3(size.X - 0.02f, 0.04f, size.Z - 0.02f));
        foreach (float sz in new[] { -1f, 1f })
        {
            float z = center.Z + sz * (hz - 0.03f);
            for (int i = 0; i < 7; i++)
            {
                c.M.Box(body, new Vector3(center.X - hx * 0.45f, cy0 + 0.25f + i * 0.1f, z + sz * 0.012f), new Vector3(0.62f, 0.012f, 0.06f), new Basis(Vector3.Right, sz * 0.6f));
            }

            c.M.Box(body, new Vector3(center.X + hx * 0.42f, cy0 + ch * 0.5f, z + sz * 0.006f), new Vector3(0.66f, ch - 0.22f, 0.012f));
            c.M.Box(rust, new Vector3(center.X + hx * 0.42f + 0.26f, cy0 + ch * 0.55f, z + sz * 0.02f), new Vector3(0.03f, 0.12f, 0.025f));
        }

        float ex = center.X + hx * 0.62f, ez = center.Z + 0.18f;
        c.M.Rod(rust, new Vector3(ex, cy0 + ch, ez), new Vector3(ex, baseY + size.Y + 0.24f, ez), 0.05f, 12);
        c.M.Cylinder(rust, new Vector3(ex, baseY + size.Y + 0.26f, ez), Basis.Identity, 0.075f, 0.03f, 12);
        c.M.Box(body, new Vector3(center.X + hx - 0.02f, cy0 + ch * 0.6f, center.Z), new Vector3(0.06f, 0.36f, 0.46f));
        Ring(c, rust, new Vector3(center.X, cy0 + ch + 0.04f, center.Z), Vector3.Right, Vector3.Up, 0.06f, 0.012f, 0f, Mathf.Pi, 8, 5);
    }

    /// <summary>
    /// A pump set on a concrete plinth: a steel bed, the pump casing with its pipes at one end, and
    /// the motor (the second collider) on a stand, finned along its length, coupled to the pump.
    /// </summary>
    private static void Pump(Ctx c)
    {
        Vector3 size = c.Size(0), center = c.Center(0);
        float baseY = center.Y - size.Y * 0.5f;
        int machine = c.Default, rust = c.Mat("steel_rust"), concrete = c.Mat("concrete_wall");
        float hx = size.X * 0.5f;
        c.M.Box(concrete, new Vector3(center.X, baseY + 0.15f, center.Z), new Vector3(size.X, 0.3f, size.Z));
        c.M.Box(rust, new Vector3(center.X, baseY + 0.34f, center.Z), new Vector3(size.X - 0.1f, 0.08f, size.Z - 0.2f));

        // The casing: a squat drum on its side, with the suction pipe going out the end and the
        // delivery pipe straight up and over, into the ground.
        float px = center.X - hx + 0.42f, py = baseY + 0.7f;
        Basis alongZ = new(Vector3.Right, Mathf.Pi / 2f);
        c.M.Cylinder(machine, new Vector3(px, py, center.Z), alongZ, 0.31f, 0.34f, 18);
        c.M.Box(machine, new Vector3(px, baseY + 0.46f, center.Z), new Vector3(0.4f, 0.16f, 0.3f));
        Basis alongX = new(Vector3.Back, Mathf.Pi / 2f);
        c.M.Cylinder(rust, new Vector3(center.X - hx + 0.05f, py, center.Z), alongX, 0.12f, 0.12f, 12);
        c.M.Cylinder(rust, new Vector3(center.X - hx + 0.13f, py, center.Z), alongX, 0.17f, 0.03f, 14);
        c.M.Rod(rust, new Vector3(px, py + 0.25f, center.Z), new Vector3(px, baseY + size.Y + 0.25f, center.Z), 0.1f, 12);
        c.M.Cylinder(rust, new Vector3(px, py + 0.33f, center.Z), Basis.Identity, 0.15f, 0.03f, 14);

        if (c.Count < 2)
        {
            return;
        }

        float mr = c.Radius(1) * 0.9f, mh = c.Height(1);
        Vector3 mc = c.Center(1);
        Basis mrot = c.Rot(1);
        c.M.Cylinder(machine, mc, mrot, mr, mh * 0.78f, 18);
        c.M.Cylinder(machine, mc + mrot.Y * (mh * 0.43f), mrot, mr * 0.9f, mh * 0.1f, 18);
        for (int f = 0; f < 14; f++)
        {
            float a = f * Mathf.Tau / 14f;
            Vector3 radial = mrot.X * Mathf.Cos(a) + mrot.Z * Mathf.Sin(a);
            c.M.Box(machine, mc + radial * (mr + 0.012f), new Vector3(0.028f, mh * 0.66f, 0.01f), new Basis(radial, mrot.Y, radial.Cross(mrot.Y)));
        }

        float standTop = mc.Y - mr;
        c.M.Box(machine, new Vector3(mc.X, (baseY + 0.38f + standTop) * 0.5f, mc.Z), new Vector3(mh * 0.6f, standTop - baseY - 0.38f, mr * 1.6f));
        Vector3 coupling = mc - mrot.Y * (mh * 0.5f + 0.08f);
        c.M.Box(machine, new Vector3(coupling.X, coupling.Y, coupling.Z), new Vector3(0.22f, mr * 1.2f, mr * 1.4f));
        c.M.Box(rust, new Vector3((coupling.X + px) * 0.5f, py, center.Z), new Vector3(MathF.Max(MathF.Abs(coupling.X - px) - 0.3f, 0.05f), 0.08f, 0.08f));
    }

    /// <summary>
    /// The water tower: four legs braced with struts and crossed tie rods, a railed platform, a
    /// riveted tank with hoops and a conical roof, and a ladder up a leg and the tank.
    /// </summary>
    private static void WaterTower(Ctx c)
    {
        int steel = c.Default, tankMat = c.Mat("steel_painted");
        var legs = new List<Vector3>();
        float legHeight = 0f, legWidth = 0.3f;
        int platform = -1, tank = -1;
        for (int i = 0; i < c.Count; i++)
        {
            if (c.IsCylinder(i))
            {
                tank = i;
            }
            else if (c.Size(i).Y > 4f)
            {
                legs.Add(c.Center(i));
                legHeight = c.Size(i).Y;
                legWidth = c.Size(i).X;
            }
            else
            {
                platform = i;
            }
        }

        foreach (Vector3 leg in legs)
        {
            c.M.Box(steel, leg, new Vector3(legWidth * 0.8f, legHeight, legWidth * 0.8f));
        }

        // Struts and tie rods round the outside, between neighbouring legs.
        if (legs.Count >= 3)
        {
            Vector3 mid = Vector3.Zero;
            foreach (Vector3 leg in legs)
            {
                mid += leg;
            }

            mid /= legs.Count;
            legs.Sort((a, b) => Mathf.Atan2(a.Z - mid.Z, a.X - mid.X).CompareTo(Mathf.Atan2(b.Z - mid.Z, b.X - mid.X)));
            float bottom = legs[0].Y - legHeight * 0.5f;
            const int bays = 4;
            for (int i = 0; i < legs.Count; i++)
            {
                Vector3 a = legs[i], b = legs[(i + 1) % legs.Count];
                for (int k = 0; k <= bays; k++)
                {
                    float y = bottom + 0.3f + (legHeight - 0.5f) * k / bays;
                    c.M.Bar(steel, new Vector3(a.X, y, a.Z), new Vector3(b.X, y, b.Z), 0.1f, 0.1f);
                    if (k < bays)
                    {
                        float y2 = bottom + 0.3f + (legHeight - 0.5f) * (k + 1) / bays;
                        c.M.Rod(steel, new Vector3(a.X, y, a.Z), new Vector3(b.X, y2, b.Z), 0.02f, 4);
                        c.M.Rod(steel, new Vector3(b.X, y, b.Z), new Vector3(a.X, y2, a.Z), 0.02f, 4);
                    }
                }
            }

            // A ladder up the outside of the first leg.
            Vector3 l = legs[0];
            Vector3 outward = new Vector3(l.X - mid.X, 0f, l.Z - mid.Z).Normalized();
            Vector3 sideways = outward.Cross(Vector3.Up);
            Vector3 foot = l + outward * (legWidth * 0.5f + 0.18f);
            Ladder(c, steel, new Vector3(foot.X, bottom, foot.Z), sideways, legHeight);
        }

        if (platform >= 0)
        {
            Vector3 ps = c.Size(platform), pc = c.Center(platform);
            c.M.Box(steel, pc, new Vector3(ps.X, ps.Y * 0.7f, ps.Z));
            float top = pc.Y + ps.Y * 0.5f, ex = ps.X * 0.5f - 0.04f, ez = ps.Z * 0.5f - 0.04f;
            var corners = new[] { new Vector3(-ex, 0f, -ez), new Vector3(ex, 0f, -ez), new Vector3(ex, 0f, ez), new Vector3(-ex, 0f, ez) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = pc + corners[i], b = pc + corners[(i + 1) % 4];
                for (int k = 0; k < 4; k++)
                {
                    Vector3 p = a.Lerp(b, k / 4f);
                    c.M.Rod(steel, new Vector3(p.X, top, p.Z), new Vector3(p.X, top + 1f, p.Z), 0.025f, 5);
                }

                c.M.Bar(steel, new Vector3(a.X, top + 1f, a.Z), new Vector3(b.X, top + 1f, b.Z), 0.04f, 0.04f);
                c.M.Bar(steel, new Vector3(a.X, top + 0.5f, a.Z), new Vector3(b.X, top + 0.5f, b.Z), 0.03f, 0.03f);
            }
        }

        if (tank >= 0)
        {
            float r = c.Radius(tank), hh = c.Height(tank) * 0.5f;
            var p = new List<Vector2> { new(0f, -hh), new(r - 0.1f, -hh), new(r, -hh + 0.1f) };
            foreach (float f in new[] { 0.25f, 0.55f, 0.85f })
            {
                float y = -hh + 2f * hh * f;
                p.Add(new Vector2(r, y - 0.05f));
                p.Add(new Vector2(r + 0.025f, y - 0.025f));
                p.Add(new Vector2(r + 0.025f, y + 0.025f));
                p.Add(new Vector2(r, y + 0.05f));
            }

            p.Add(new Vector2(r, hh - 0.1f));
            p.Add(new Vector2(r + 0.06f, hh - 0.08f));
            p.Add(new Vector2(r + 0.06f, hh - 0.04f));
            p.Add(new Vector2(r * 0.12f, hh + 0.55f));
            p.Add(new Vector2(r * 0.06f, hh + 0.62f));
            p.Add(new Vector2(0f, hh + 0.62f));
            c.M.Lathe(tankMat, c.Center(tank), c.Rot(tank), p, 32);
            Vector3 tc = c.Center(tank);
            Ladder(c, steel, new Vector3(tc.X + r + 0.15f, tc.Y - hh, tc.Z), Vector3.Back, 2f * hh - 0.1f);
        }
    }

    private static void Ladder(Ctx c, int material, Vector3 foot, Vector3 sideways, float height)
    {
        const float half = 0.2f, rung = 0.3f;
        foreach (float s in new[] { -half, half })
        {
            c.M.Rod(material, foot + sideways * s, foot + sideways * s + Vector3.Up * height, 0.02f, 5);
        }

        for (float y = rung; y < height - 0.05f; y += rung)
        {
            c.M.Rod(material, foot + sideways * -half + Vector3.Up * y, foot + sideways * half + Vector3.Up * y, 0.012f, 4, caps: false);
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A board of planks: a box whose faces show a single plank of the wood texture.</summary>
    private static void Board(Ctx c, int material, Vector3 center, Vector3 size, Basis basis) =>
        c.M.Box(material, center, size, basis, PlankUv(c));

    /// <summary>
    /// A UV offset that lands a board (up to 17 cm wide) on one plank of wood_planks (a 0.8 m tile
    /// with four planks across it), so no joint between planks runs along the board.
    /// </summary>
    private static Vector2 PlankUv(Ctx c) => new(c.R(0f, 3f), -(0.2f * c.Rng.Next(4) + 0.1f));

    /// <summary>
    /// A torus arc: the ring runs from <paramref name="a0"/> to <paramref name="a1"/> in the plane of
    /// <paramref name="u"/> (angle 0) and <paramref name="v"/> (angle π/2), round <paramref name="center"/>.
    /// </summary>
    private static void Ring(Ctx c, int material, Vector3 center, Vector3 u, Vector3 v, float radius, float tube, float a0, float a1, int segments, int sides)
    {
        Vector3 n = u.Cross(v).Normalized();
        Vector3 At(float a, float b, out Vector3 normal)
        {
            Vector3 radial = u * Mathf.Cos(a) + v * Mathf.Sin(a);
            normal = radial * Mathf.Cos(b) + n * Mathf.Sin(b);
            return center + radial * radius + normal * tube;
        }

        for (int i = 0; i < segments; i++)
        {
            float s0 = Mathf.Lerp(a0, a1, (float)i / segments), s1 = Mathf.Lerp(a0, a1, (float)(i + 1) / segments);
            for (int j = 0; j < sides; j++)
            {
                float b0 = Mathf.Tau * j / sides, b1 = Mathf.Tau * (j + 1) / sides;
                Vector3 p00 = At(s0, b0, out Vector3 n00), p10 = At(s1, b0, out Vector3 n10);
                Vector3 p11 = At(s1, b1, out Vector3 n11), p01 = At(s0, b1, out Vector3 n01);
                var uv00 = new Vector2(s0 * radius, b0 * tube);
                var uv10 = new Vector2(s1 * radius, b0 * tube);
                var uv11 = new Vector2(s1 * radius, b1 * tube);
                var uv01 = new Vector2(s0 * radius, b1 * tube);
                // Wind each triangle clockwise seen from outside, whichever way u and v turn.
                if ((p10 - p00).Cross(p11 - p00).Dot(n00 + n11) > 0f)
                {
                    c.M.Smooth(material, p00, n00, uv00, p11, n11, uv11, p10, n10, uv10);
                    c.M.Smooth(material, p00, n00, uv00, p01, n01, uv01, p11, n11, uv11);
                }
                else
                {
                    c.M.Smooth(material, p00, n00, uv00, p10, n10, uv10, p11, n11, uv11);
                    c.M.Smooth(material, p00, n00, uv00, p11, n11, uv11, p01, n01, uv01);
                }
            }
        }
    }

    private static void Arc(List<Vector2> points, Vector2 center, float radius, float a0, float a1, int steps)
    {
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.Lerp(a0, a1, (float)i / steps);
            points.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
        }
    }

    private static Vector2[] Circle(float radius, int segments)
    {
        var points = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float a = Mathf.Tau * i / segments;
            points[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        return points;
    }

    private static Basis RandomTilt(Ctx c, float tilt) =>
        new Basis(Vector3.Up, c.R(0f, Mathf.Tau)) * new Basis(Vector3.Right, c.R(-tilt, tilt)) * new Basis(Vector3.Forward, c.R(-tilt, tilt));

    /// <summary>What a recipe works from: the mesh, the prop type's colliders, a seeded random stream and the kit's materials.</summary>
    private sealed class Ctx
    {
        private readonly Func<string, int> _material;

        public Ctx(ShapeMesh mesh, PropType type, int seed, Func<string, int> material)
        {
            M = mesh;
            Type = type;
            Rng = new Random(seed);
            _material = material;
        }

        public ShapeMesh M { get; }

        public PropType Type { get; }

        public Random Rng { get; }

        public int Count => Type.Colliders.Count;

        /// <summary>The first collider's material: what the prop is mostly made of.</summary>
        public int Default => Type.Colliders[0].Material.Index;

        /// <summary>A kit material by id, or <see cref="Default"/> if the kit has none by that name.</summary>
        public int Mat(string id)
        {
            int index = _material(id);
            return index >= 0 ? index : Default;
        }

        public bool IsCylinder(int i) => Type.Colliders[i].Kind == PrimitiveKind.Cylinder;

        public Vector3 Center(int i = 0) => Type.Colliders[i].Center.ToGodot();

        public Basis Rot(int i = 0)
        {
            System.Numerics.Quaternion q = Type.Colliders[i].Rotation;
            return new Basis(new Quaternion(q.X, q.Y, q.Z, q.W));
        }

        /// <summary>A box collider's size, or a cylinder's (diameter, height, diameter) along its own axes.</summary>
        public Vector3 Size(int i = 0)
        {
            Vector3 h = Type.Colliders[i].HalfExtents.ToGodot();
            return IsCylinder(i) ? new Vector3(h.X * 2f, h.Y * 2f, h.X * 2f) : h * 2f;
        }

        public float Radius(int i = 0) => Type.Colliders[i].HalfExtents.X;

        public float Height(int i = 0) => Type.Colliders[i].HalfExtents.Y * 2f;

        public float R(float a, float b) => a + (float)Rng.NextDouble() * (b - a);

        public bool Chance(float p) => Rng.NextDouble() < p;
    }
}
