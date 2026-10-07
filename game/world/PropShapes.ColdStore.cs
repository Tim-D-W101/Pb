using System;
using Godot;

namespace Pb.Game.World;

/// <summary>
/// The cold store's props, built in code from their colliders (kit/props.jsonc): the lorry trailers you run through,
/// pallet racking, roll cages, the plant room's compressors and ammonia tanks, and a lorry cab. Where paint flies under
/// something (a trailer, a tank), a walking-only collider fills the gap and isn't drawn.
/// </summary>
public static partial class PropShapes
{
    /// <summary>
    /// A box trailer: white panel sides and roof on rails and corner posts, the rear frame with its doors swung back flat
    /// against the sides, a plank floor, the chassis with its landing legs, tandem axles on twin wheels under mudguards, a
    /// bumper bar and lamps at the back, and the ribbed steel ramp up into the nose. Colliders: 0–1 sides, 2 roof,
    /// 3 floor, 4 the gap under it (walking only), 5–6 rear doors, 7 ramp, then wheels and legs.
    /// </summary>
    private static void LorryTrailer(Ctx c)
    {
        Vector3 roof = c.Center(2), roofSize = c.Size(2);
        Vector3 floor = c.Center(3), floorSize = c.Size(3);
        int panel = c.Default, rail = c.Mat("steel_painted"), rust = c.Mat("steel_rust"), wood = c.Mat("wood_planks");
        int rubber = c.Mat("tyre_rubber"), red = c.Mat("container_red"), dark = c.Mat("plastic_black");
        float hx = roofSize.X * 0.5f, hz = roofSize.Z * 0.5f;
        float floorTop = floor.Y + floorSize.Y * 0.5f, under = floor.Y - floorSize.Y * 0.5f, top = roof.Y + roofSize.Y * 0.5f;

        // The skins, the rails along their top and bottom edges, and the corner posts; ribs inside the sides.
        for (int i = 0; i <= 1; i++)
        {
            Vector3 side = c.Center(i), size = c.Size(i);
            float sx = MathF.Sign(side.X);
            c.M.Box(panel, side, size);
            c.M.Box(rail, new Vector3(sx * (hx + 0.015f), floorTop + 0.07f, 0f), new Vector3(0.05f, 0.14f, 2f * hz));
            c.M.Box(rail, new Vector3(sx * (hx + 0.015f), top - 0.07f, 0f), new Vector3(0.05f, 0.14f, 2f * hz));
            foreach (float sz in new[] { -1f, 1f })
            {
                c.M.Box(rail, new Vector3(sx * (hx - 0.05f), (floorTop + top) * 0.5f, sz * (hz - 0.06f)), new Vector3(0.12f, top - floorTop, 0.12f));
            }

            for (float z = -hz + 0.6f; z < hz - 0.4f; z += 0.6f)
            {
                c.M.Box(rail, new Vector3(sx * (hx - size.X - 0.015f), (floorTop + top) * 0.5f, z), new Vector3(0.03f, top - floorTop - 0.3f, 0.06f));
            }
        }

        c.M.Box(panel, roof, roofSize);
        // The floor's planks, worn through here and there to the steel beneath.
        for (float x = -hx + 0.15f; x < hx - 0.1f; x += 0.3f)
        {
            Board(c, wood, new Vector3(x, floor.Y + 0.005f, 0f), new Vector3(0.28f, floorSize.Y - 0.01f, 2f * hz - 0.04f), Basis.Identity);
        }

        c.M.Box(rust, new Vector3(0f, floor.Y - 0.02f, 0f), new Vector3(2f * hx, floorSize.Y - 0.06f, 2f * hz));

        // The rear frame round the doorway: the header, the sill, and the posts the doors hang on.
        c.M.Box(rail, new Vector3(0f, top - 0.2f, -hz + 0.07f), new Vector3(2f * hx, 0.4f, 0.14f));
        c.M.Box(rail, new Vector3(0f, under + 0.05f, -hz + 0.07f), new Vector3(2f * hx, 0.3f, 0.14f));
        c.M.Box(rail, new Vector3(0f, top - 0.2f, hz - 0.07f), new Vector3(2f * hx, 0.4f, 0.14f));

        // The doors, swung right round onto the sides: a panel, a frame, two locking bars with their handles, hinges.
        for (int i = 5; i <= 6; i++)
        {
            Vector3 door = c.Center(i), size = c.Size(i);
            float sx = MathF.Sign(door.X);
            c.M.Box(panel, door, size);
            float x = door.X + sx * (size.X * 0.5f + 0.02f);
            c.M.Box(rail, new Vector3(x, door.Y, door.Z - size.Z * 0.5f + 0.04f), new Vector3(0.03f, size.Y, 0.08f));
            c.M.Box(rail, new Vector3(x, door.Y, door.Z + size.Z * 0.5f - 0.04f), new Vector3(0.03f, size.Y, 0.08f));
            foreach (float f in new[] { -0.22f, 0.22f })
            {
                float z = door.Z + f * size.Z;
                c.M.Rod(rail, new Vector3(x + sx * 0.03f, door.Y - size.Y * 0.48f, z), new Vector3(x + sx * 0.03f, door.Y + size.Y * 0.48f, z), 0.016f, 6);
                c.M.Bar(dark, new Vector3(x + sx * 0.05f, door.Y - 0.25f, z), new Vector3(x + sx * 0.05f, door.Y - 0.25f, z + 0.25f), 0.03f, 0.03f);
            }

            foreach (float y in new[] { door.Y - size.Y * 0.38f, door.Y, door.Y + size.Y * 0.38f })
            {
                c.M.Box(dark, new Vector3(door.X + sx * 0.02f, y, -hz + 0.02f), new Vector3(0.1f, 0.1f, 0.12f));
            }
        }

        // The chassis: two beams the length of the trailer, cross members, side guards between the legs and the wheels.
        foreach (float sx in new[] { -0.5f, 0.5f })
        {
            c.M.Box(rust, new Vector3(sx, under - 0.22f, 0f), new Vector3(0.12f, 0.44f, 2f * hz - 0.3f));
        }

        for (float z = -hz + 0.5f; z < hz; z += 1.2f)
        {
            c.M.Box(rust, new Vector3(0f, under - 0.06f, z), new Vector3(2f * hx - 0.2f, 0.12f, 0.08f));
        }

        foreach (float sx in new[] { -1f, 1f })
        {
            c.M.Box(rail, new Vector3(sx * (hx - 0.06f), under - 0.35f, -0.2f), new Vector3(0.04f, 0.06f, 7.0f));
            c.M.Box(rail, new Vector3(sx * (hx - 0.06f), under - 0.6f, -0.2f), new Vector3(0.04f, 0.06f, 7.0f));
        }

        // Wheels, axles, mudguards; the landing legs with their feet and crank.
        float axleZ0 = float.MaxValue, axleZ1 = float.MinValue;
        for (int i = 8; i < c.Count; i++)
        {
            Vector3 at = c.Center(i);
            if (c.IsCylinder(i))
            {
                float width = c.Height(i);
                Wheel(c, rubber, rail, at + new Vector3(-width * 0.25f, 0f, 0f), c.Radius(i), width * 0.48f);
                Wheel(c, rubber, rail, at + new Vector3(width * 0.25f, 0f, 0f), c.Radius(i), width * 0.48f);
                if (at.X > 0f)
                {
                    c.M.Rod(rust, new Vector3(-at.X, at.Y, at.Z), at, 0.07f, 8);
                }

                axleZ0 = MathF.Min(axleZ0, at.Z);
                axleZ1 = MathF.Max(axleZ1, at.Z);
            }
            else
            {
                Vector3 size = c.Size(i);
                c.M.Box(rail, at, size);
                c.M.Box(rail, new Vector3(at.X, at.Y - size.Y * 0.5f + 0.03f, at.Z), new Vector3(0.3f, 0.06f, 0.3f));
                c.M.Bar(rust, new Vector3(at.X, at.Y + size.Y * 0.3f, at.Z), new Vector3(-at.X, at.Y + size.Y * 0.3f, at.Z), 0.05f, 0.05f);
            }
        }

        if (axleZ0 < axleZ1)
        {
            foreach (float sx in new[] { -1f, 1f })
            {
                c.M.Box(dark, new Vector3(sx * (hx - 0.3f), under - 0.02f, (axleZ0 + axleZ1) * 0.5f), new Vector3(0.62f, 0.04f, axleZ1 - axleZ0 + 1.3f));
            }
        }

        // The bumper bar and lamp clusters under the back.
        c.M.Box(rust, new Vector3(0f, 0.55f, -hz + 0.25f), new Vector3(2f * hx - 0.3f, 0.12f, 0.12f));
        foreach (float sx in new[] { -1f, 1f })
        {
            c.M.Bar(rust, new Vector3(sx * 0.5f, under, -hz + 0.25f), new Vector3(sx * 0.5f, 0.55f, -hz + 0.25f), 0.08f, 0.08f);
            c.M.Box(red, new Vector3(sx * (hx - 0.3f), under - 0.12f, -hz + 0.03f), new Vector3(0.4f, 0.12f, 0.05f));
        }

        // The ramp: a steel plate with ribs across it and angles along its edges.
        Vector3 ramp = c.Center(7), rampSize = c.Size(7);
        Basis tilt = c.Rot(7);
        c.M.Box(rail, ramp, rampSize, tilt);
        Vector3 up = tilt * Vector3.Up, along = tilt * Vector3.Back;
        for (float t = -rampSize.Z * 0.5f + 0.15f; t < rampSize.Z * 0.5f; t += 0.3f)
        {
            c.M.Box(rust, ramp + along * t + up * (rampSize.Y * 0.5f + 0.01f), new Vector3(rampSize.X - 0.1f, 0.02f, 0.03f), tilt);
        }

        foreach (float sx in new[] { -1f, 1f })
        {
            c.M.Box(rust, ramp + tilt * new Vector3(sx * rampSize.X * 0.5f, 0.04f, 0f), new Vector3(0.05f, 0.12f, rampSize.Z), tilt);
        }
    }

    /// <summary>
    /// A bay of pallet racking: perforated uprights braced front to back, orange beams, and on each loaded level two
    /// pallets of cartons in torn shrink-wrap. Colliders: 0–3 uprights, 4–7 beams, then the loads.
    /// </summary>
    private static void PalletRacking(Ctx c)
    {
        int upright = c.Default, beam = c.Mat("steel_yellow"), card = c.Mat("hardboard"), pallet = c.Mat("wood_pallet");
        int wrap = c.Mat("nylon");
        for (int i = 0; i < 4 && i < c.Count; i++)
        {
            c.M.Box(upright, c.Center(i), c.Size(i));
        }

        // The bracing between each pair of uprights (front and back), zigzagging up the frame.
        foreach ((int front, int back) in new[] { (0, 1), (2, 3) })
        {
            Vector3 a = c.Center(front), b = c.Center(back);
            float height = c.Size(front).Y, baseY = a.Y - height * 0.5f;
            const int braces = 6;
            for (int k = 0; k <= braces; k++)
            {
                float y = baseY + 0.15f + (height - 0.3f) * k / braces;
                c.M.Bar(upright, new Vector3(a.X, y, a.Z), new Vector3(b.X, y, b.Z), 0.03f, 0.03f);
                if (k < braces)
                {
                    float y2 = baseY + 0.15f + (height - 0.3f) * (k + 1) / braces;
                    (Vector3 from, Vector3 to) = k % 2 == 0 ? (a, b) : (b, a);
                    c.M.Bar(upright, new Vector3(from.X, y, from.Z), new Vector3(to.X, y2, to.Z), 0.025f, 0.025f);
                }
            }
        }

        for (int i = 4; i < 8 && i < c.Count; i++)
        {
            c.M.Box(beam, c.Center(i), c.Size(i));
        }

        for (int i = 8; i < c.Count; i++)
        {
            Vector3 load = c.Center(i), size = c.Size(i);
            float bottom = load.Y - size.Y * 0.5f, half = size.X * 0.5f;
            foreach (float sx in new[] { -0.5f, 0.5f })
            {
                float x = load.X + sx * half;
                c.M.Box(pallet, new Vector3(x, bottom + 0.07f, load.Z), new Vector3(half - 0.04f, 0.14f, size.Z));
                // The cartons: layers of boxes, a little uneven, inside the wrap.
                float y = bottom + 0.14f;
                float stackTop = bottom + size.Y - 0.02f;
                while (y < stackTop - 0.1f)
                {
                    float h = MathF.Min(c.R(0.25f, 0.4f), stackTop - y);
                    float inset = c.R(0f, 0.04f);
                    c.M.Box(card, new Vector3(x + c.R(-0.02f, 0.02f), y + h * 0.5f, load.Z + c.R(-0.02f, 0.02f)), new Vector3(half - 0.08f - inset, h - 0.01f, size.Z - 0.04f - inset));
                    y += h;
                }

                if (c.Chance(0.7f))
                {
                    float wrapTop = bottom + 0.14f + (size.Y - 0.14f) * c.R(0.5f, 0.95f);
                    c.M.Box(wrap, new Vector3(x, (bottom + 0.16f + wrapTop) * 0.5f, load.Z), new Vector3(half - 0.05f, wrapTop - bottom - 0.16f, size.Z - 0.01f));
                }
            }
        }
    }

    /// <summary>A roll cage: a galvanised frame with mesh sides and an open front, castors, and cartons left in it.</summary>
    private static void RollCage(Ctx c)
    {
        Vector3 center = c.Center(), size = c.Size();
        int steel = c.Default, card = c.Mat("hardboard"), rubber = c.Mat("rubber_plain");
        float bottom = center.Y - size.Y * 0.5f, top = center.Y + size.Y * 0.5f;
        float hx = size.X * 0.5f - 0.02f, hz = size.Z * 0.5f - 0.02f;
        c.M.Box(steel, new Vector3(center.X, bottom + 0.03f, center.Z), new Vector3(size.X, 0.06f, size.Z));
        foreach (float sx in new[] { -1f, 1f })
        {
            foreach (float sz in new[] { -1f, 1f })
            {
                c.M.Rod(steel, new Vector3(center.X + sx * hx, bottom, center.Z + sz * hz), new Vector3(center.X + sx * hx, top, center.Z + sz * hz), 0.015f, 6);
                c.M.Cylinder(rubber, new Vector3(center.X + sx * (hx - 0.05f), 0.05f, center.Z + sz * (hz - 0.05f)), new Basis(Vector3.Back, Mathf.Pi / 2f), 0.05f, 0.03f, 8);
            }
        }

        // Mesh on the back and the two sides: a grid of thin rods.
        void Mesh(Vector3 a, Vector3 b)
        {
            for (float t = 0.1f; t < 0.95f; t += 0.12f)
            {
                Vector3 p = a.Lerp(b, t);
                c.M.Rod(steel, new Vector3(p.X, bottom + 0.06f, p.Z), new Vector3(p.X, top, p.Z), 0.004f, 3, caps: false);
            }

            for (float y = bottom + 0.15f; y < top; y += 0.15f)
            {
                c.M.Rod(steel, new Vector3(a.X, y, a.Z), new Vector3(b.X, y, b.Z), 0.004f, 3, caps: false);
            }
        }

        Mesh(new Vector3(center.X - hx, 0f, center.Z + hz), new Vector3(center.X + hx, 0f, center.Z + hz));
        Mesh(new Vector3(center.X - hx, 0f, center.Z - hz), new Vector3(center.X - hx, 0f, center.Z + hz));
        Mesh(new Vector3(center.X + hx, 0f, center.Z - hz), new Vector3(center.X + hx, 0f, center.Z + hz));
        float y0 = bottom + 0.06f;
        while (y0 < top - 0.3f)
        {
            float h = c.R(0.25f, 0.45f);
            c.M.Box(card, new Vector3(center.X + c.R(-0.05f, 0.05f), y0 + h * 0.5f, center.Z + c.R(-0.03f, 0.05f)), new Vector3(size.X - c.R(0.1f, 0.25f), h - 0.01f, size.Z - c.R(0.08f, 0.2f)));
            y0 += h;
            if (c.Chance(0.25f))
            {
                break;
            }
        }
    }

    /// <summary>
    /// A compressor set: the skid, the compressor block with its ribbed casing, the motor and its coupling guard, the oil
    /// separator on legs across the top, pipes between them, and the starter box with its gauges.
    /// </summary>
    private static void Compressor(Ctx c)
    {
        int frame = c.Default, machine = c.Mat("machinery"), pipe = c.Mat("steel_galvanised"), dark = c.Mat("plastic_black");
        int yellow = c.Mat("steel_yellow");
        Vector3 skid = c.Center(0), skidSize = c.Size(0);
        c.M.Box(frame, skid, skidSize);
        foreach (float sz in new[] { -1f, 1f })
        {
            c.M.Box(yellow, new Vector3(skid.X, skid.Y + 0.02f, skid.Z + sz * (skidSize.Z * 0.5f + 0.01f)), new Vector3(skidSize.X, 0.06f, 0.02f));
        }

        Vector3 block = c.Center(1), blockSize = c.Size(1);
        c.M.Box(machine, block, blockSize - new Vector3(0.06f, 0f, 0.06f));
        for (float y = block.Y - blockSize.Y * 0.4f; y < block.Y + blockSize.Y * 0.45f; y += 0.12f)
        {
            c.M.Box(machine, new Vector3(block.X, y, block.Z), new Vector3(blockSize.X, 0.03f, blockSize.Z));
        }

        Vector3 motor = c.Center(2);
        Basis axis = new Basis(Vector3.Back, -Mathf.Pi / 2f);
        c.M.Cylinder(machine, motor, axis, c.Radius(2), c.Height(2), 18);
        for (float t = -0.4f; t <= 0.4f; t += 0.08f)
        {
            c.M.Cylinder(machine, motor + new Vector3(t * c.Height(2), 0f, 0f), axis, c.Radius(2) + 0.02f, 0.02f, 18, caps: false);
        }

        c.M.Box(yellow, new Vector3(motor.X - c.Height(2) * 0.5f - 0.12f, motor.Y, motor.Z), new Vector3(0.24f, c.Radius(2) * 1.6f, c.Radius(2) * 1.6f));
        c.M.Box(machine, new Vector3(motor.X, motor.Y + c.Radius(2) + 0.08f, motor.Z), new Vector3(0.36f, 0.16f, 0.3f));

        Vector3 vessel = c.Center(3);
        c.M.Cylinder(frame, vessel, axis, c.Radius(3), c.Height(3) - 0.1f, 18);
        foreach (float sx in new[] { -1f, 1f })
        {
            Vector3 end = vessel + new Vector3(sx * (c.Height(3) * 0.5f - 0.05f), 0f, 0f);
            c.M.Cylinder(frame, end, axis, c.Radius(3) * 0.75f, 0.1f, 18);
            Vector3 leg = vessel + new Vector3(sx * c.Height(3) * 0.35f, 0f, 0f);
            c.M.Bar(frame, leg, new Vector3(leg.X, skid.Y + skidSize.Y * 0.5f, leg.Z), 0.07f, 0.07f);
        }

        // Pipes from the block up to the separator and away, with flanges.
        Vector3 outlet = new(block.X + 0.3f, block.Y + blockSize.Y * 0.5f, block.Z + 0.2f);
        Vector3 inlet = new(outlet.X, vessel.Y - c.Radius(3), vessel.Z);
        c.M.Rod(pipe, outlet, inlet, 0.06f, 8);
        c.M.Rod(pipe, new Vector3(vessel.X - c.Height(3) * 0.5f, vessel.Y, vessel.Z), new Vector3(vessel.X - c.Height(3) * 0.5f - 0.3f, vessel.Y, vessel.Z), 0.05f, 8);
        c.M.Rod(pipe, new Vector3(block.X - 0.5f, block.Y + blockSize.Y * 0.5f, block.Z - 0.3f), new Vector3(block.X - 0.5f, vessel.Y + 0.4f, block.Z - 0.3f), 0.07f, 8);

        // The starter box on a post, with three gauges.
        var box = new Vector3(skid.X + skidSize.X * 0.5f - 0.15f, 1.35f, skid.Z - skidSize.Z * 0.5f + 0.12f);
        c.M.Bar(frame, new Vector3(box.X, skid.Y, box.Z), new Vector3(box.X, box.Y - 0.3f, box.Z), 0.06f, 0.06f);
        c.M.Box(frame, box, new Vector3(0.3f, 0.6f, 0.2f));
        for (int g = 0; g < 3; g++)
        {
            c.M.Cylinder(dark, new Vector3(block.X - 0.4f + g * 0.25f, block.Y + 0.25f, block.Z - blockSize.Z * 0.5f - 0.02f), new Basis(Vector3.Right, Mathf.Pi * 0.5f), 0.06f, 0.04f, 12);
        }
    }

    /// <summary>An ammonia receiver: a white vessel with dished ends on two concrete saddles, a valve on top and pipe stubs.</summary>
    private static void AmmoniaTank(Ctx c)
    {
        int white = c.Default, concrete = c.Mat("concrete_wall"), steel = c.Mat("steel_painted"), yellow = c.Mat("steel_yellow");
        Vector3 vessel = c.Center(0);
        float radius = c.Radius(0), length = c.Height(0);
        Basis along = new Basis(Vector3.Right, Mathf.Pi * 0.5f);
        c.M.Cylinder(white, vessel, along, radius, length - radius * 0.6f, 24, caps: false);
        foreach (float sz in new[] { -1f, 1f })
        {
            // A dished end: a shallow dome out from each end of the shell.
            var profile = new System.Collections.Generic.List<Vector2>();
            for (int k = 0; k <= 6; k++)
            {
                float a = Mathf.Pi * 0.5f * k / 6f;
                profile.Add(new Vector2(radius * Mathf.Cos(a), radius * 0.3f * Mathf.Sin(a)));
            }

            Basis end = sz > 0f ? along : new Basis(Vector3.Right, -Mathf.Pi * 0.5f);
            c.M.Lathe(white, vessel + new Vector3(0f, 0f, sz * (length * 0.5f - radius * 0.3f)), end, profile, 24);
            c.M.Box(yellow, vessel + new Vector3(0f, 0f, sz * (length * 0.5f - radius * 0.3f - 0.2f)), new Vector3(2f * radius + 0.02f, 0.08f, 0.04f), Basis.Identity);
        }

        for (int i = 1; i <= 2; i++)
        {
            c.M.Box(concrete, c.Center(i), c.Size(i));
            c.M.Box(steel, c.Center(i) + new Vector3(0f, c.Size(i).Y * 0.5f + 0.04f, 0f), new Vector3(c.Size(i).X * 0.8f, 0.08f, c.Size(i).Z + 0.04f));
        }

        if (c.Count > 4)
        {
            Vector3 valve = c.Center(4);
            c.M.Cylinder(steel, valve, Basis.Identity, c.Radius(4), c.Height(4), 12);
            c.M.Cylinder(yellow, valve + new Vector3(0f, c.Height(4) * 0.5f + 0.03f, 0f), Basis.Identity, c.Radius(4) * 1.6f, 0.04f, 12);
        }

        c.M.Rod(steel, vessel + new Vector3(0f, radius, 1.2f), vessel + new Vector3(0f, radius + 0.5f, 1.2f), 0.05f, 8);
        c.M.Rod(steel, vessel + new Vector3(0f, radius + 0.5f, 1.2f), vessel + new Vector3(0f, radius + 0.5f, length * 0.5f + 0.4f), 0.05f, 8);
    }

    /// <summary>
    /// A tractor unit: the cab with its windscreen, side windows, doors, grille, bumper, lamps and mirrors, a roof
    /// fairing, the chassis behind with the fifth-wheel plate, fuel tanks and an exhaust stack, and its wheels.
    /// </summary>
    private static void LorryCab(Ctx c)
    {
        int paint = c.Default, glass = c.Mat("glass_dirty"), dark = c.Mat("plastic_black"), steel = c.Mat("steel_painted");
        int rust = c.Mat("steel_rust"), rubber = c.Mat("tyre_rubber"), chrome = c.Mat("steel_galvanised");
        Vector3 cab = c.Center(0), cabSize = c.Size(0);
        float hx = cabSize.X * 0.5f, front = cab.Z - cabSize.Z * 0.5f, back = cab.Z + cabSize.Z * 0.5f;
        float bottom = cab.Y - cabSize.Y * 0.5f, top = cab.Y + cabSize.Y * 0.5f;

        c.M.Box(paint, cab + new Vector3(0f, -0.05f, 0.05f), cabSize - new Vector3(0.04f, 0.1f, 0.1f));
        c.M.Pillow(paint, new Vector3(cab.X, top - 0.05f, cab.Z + 0.1f), new Vector3(cabSize.X - 0.1f, 0.3f, cabSize.Z - 0.3f), Basis.Identity, 6f, 6, 12);
        // The windscreen (two panes) and the side windows, dark and grimy; the door outlines and handles.
        float glassY = bottom + cabSize.Y * 0.68f;
        foreach (float sx in new[] { -1f, 1f })
        {
            c.M.Box(glass, new Vector3(sx * hx * 0.48f, glassY, front - 0.01f), new Vector3(hx * 0.9f, cabSize.Y * 0.32f, 0.03f));
            c.M.Box(glass, new Vector3(sx * (hx + 0.005f), glassY, front + 0.6f), new Vector3(0.03f, cabSize.Y * 0.28f, 0.8f));
            c.M.Box(dark, new Vector3(sx * (hx + 0.01f), bottom + cabSize.Y * 0.45f, front + 0.6f), new Vector3(0.02f, cabSize.Y * 0.85f, 0.02f));
            c.M.Box(dark, new Vector3(sx * (hx + 0.01f), bottom + cabSize.Y * 0.45f, front + 1.25f), new Vector3(0.02f, cabSize.Y * 0.85f, 0.02f));
            c.M.Box(chrome, new Vector3(sx * (hx + 0.02f), bottom + cabSize.Y * 0.5f, front + 1.1f), new Vector3(0.03f, 0.04f, 0.18f));
            // Mirror on its arm.
            c.M.Bar(dark, new Vector3(sx * hx, glassY + 0.2f, front + 0.15f), new Vector3(sx * (hx + 0.3f), glassY + 0.2f, front + 0.1f), 0.03f, 0.03f);
            c.M.Box(dark, new Vector3(sx * (hx + 0.32f), glassY, front + 0.1f), new Vector3(0.05f, 0.45f, 0.22f));
            // Steps up to the door.
            c.M.Box(steel, new Vector3(sx * (hx - 0.1f), bottom - 0.25f, front + 0.9f), new Vector3(0.28f, 0.04f, 0.5f));
            // Headlamps.
            c.M.Box(chrome, new Vector3(sx * (hx - 0.35f), bottom + 0.35f, front - 0.03f), new Vector3(0.4f, 0.18f, 0.04f));
        }

        // The grille and the bumper.
        for (float y = bottom + 0.5f; y < bottom + cabSize.Y * 0.48f; y += 0.08f)
        {
            c.M.Box(dark, new Vector3(0f, y, front - 0.02f), new Vector3(cabSize.X * 0.55f, 0.03f, 0.03f));
        }

        c.M.Box(steel, new Vector3(0f, bottom + 0.05f, front - 0.08f), new Vector3(cabSize.X, 0.35f, 0.2f));

        // Chassis and fifth wheel, the gap under it, fuel tanks, the exhaust stack behind the cab.
        Vector3 rails = c.Center(1), railSize = c.Size(1);
        foreach (float sx in new[] { -1f, 1f })
        {
            c.M.Box(rust, new Vector3(rails.X + sx * (railSize.X * 0.5f - 0.06f), rails.Y, rails.Z), new Vector3(0.12f, railSize.Y, railSize.Z));
        }

        for (float z = rails.Z - railSize.Z * 0.5f + 0.3f; z < rails.Z + railSize.Z * 0.5f; z += 0.9f)
        {
            c.M.Box(rust, new Vector3(rails.X, rails.Y, z), new Vector3(railSize.X, 0.1f, 0.08f));
        }

        Vector3 plate = c.Center(2), plateSize = c.Size(2);
        c.M.Box(steel, plate, plateSize);
        c.M.Box(dark, plate + new Vector3(0f, plateSize.Y * 0.5f + 0.005f, 0.3f), new Vector3(0.12f, 0.01f, 0.6f));
        Basis across = new Basis(Vector3.Back, -Mathf.Pi / 2f);
        c.M.Cylinder(chrome, new Vector3(-0.85f, rails.Y - 0.2f, back + 0.6f), new Basis(Vector3.Right, Mathf.Pi * 0.5f), 0.3f, 1.2f, 16);
        c.M.Box(dark, new Vector3(0.85f, rails.Y - 0.15f, back + 0.5f), new Vector3(0.5f, 0.5f, 0.8f));
        c.M.Rod(chrome, new Vector3(hx - 0.2f, rails.Y, back + 0.1f), new Vector3(hx - 0.2f, top + 0.4f, back + 0.1f), 0.07f, 10);

        for (int i = 4; i < c.Count; i++)
        {
            if (!c.IsCylinder(i))
            {
                continue;
            }

            Vector3 at = c.Center(i);
            float width = c.Height(i);
            if (width > 0.45f)
            {
                Wheel(c, rubber, steel, at + new Vector3(-width * 0.25f, 0f, 0f), c.Radius(i), width * 0.48f);
                Wheel(c, rubber, steel, at + new Vector3(width * 0.25f, 0f, 0f), c.Radius(i), width * 0.48f);
            }
            else
            {
                Wheel(c, rubber, steel, at, c.Radius(i), width);
            }

            c.M.Box(dark, new Vector3(at.X, at.Y + c.Radius(i) + 0.06f, at.Z), new Vector3(width + 0.1f, 0.04f, c.Radius(i) * 2.2f));
            if (at.X > 0f)
            {
                c.M.Cylinder(rust, new Vector3(0f, at.Y, at.Z), across, 0.07f, 2f * at.X, 8);
            }
        }
    }
}
