using System;
using Godot;

namespace Pb.Game.World;

/// <summary>
/// The rail yard's rolling stock and track-side props, built in code from their colliders (kit/props.jsonc): wagons and
/// a shunter stand on wheels on rails 0.25 m up; collider 0 is the body (or deck, or tank), then the underframe, then a
/// walking-only box filling the gap under it, then the wheels (paint only), so paint and sight pass under a wagon between
/// its wheels while feet can't.
/// </summary>
public static partial class PropShapes
{
    /// <summary>Wheel tread radius and how far the wheels sit from the middle (on standard-gauge rails).</summary>
    private const float WheelRadius = 0.45f;
    private const float WheelX = 0.75f;

    /// <summary>
    /// A covered van: a planked body with ribs, sliding doors in the middle of each side, a curved roof, the underframe
    /// with its axle boxes and springs, wheelsets, buffers and couplings at the ends, and steps.
    /// </summary>
    private static void BoxVan(Ctx c)
    {
        Vector3 body = c.Center(0), size = c.Size(0);
        int planks = c.Default, steel = c.Mat("steel_rust"), dark = c.Mat("steel_painted");
        float floor = body.Y - size.Y * 0.5f, top = body.Y + size.Y * 0.5f;
        float hx = size.X * 0.5f, hz = size.Z * 0.5f;
        float eaves = top - 0.32f;

        // The planked box and its frame: corner posts, ribs and a waist rail.
        c.M.Box(planks, new Vector3(body.X, (floor + eaves) * 0.5f, body.Z), new Vector3(size.X - 0.08f, eaves - floor, size.Z - 0.08f));
        foreach (float sx in new[] { -1f, 1f })
        {
            foreach (float sz in new[] { -1f, 1f })
            {
                c.M.Box(steel, new Vector3(body.X + sx * (hx - 0.05f), (floor + eaves) * 0.5f, body.Z + sz * (hz - 0.05f)), new Vector3(0.1f, eaves - floor, 0.1f));
            }

            for (int i = 1; i < 8; i++)
            {
                float z = body.Z - hz + size.Z * i / 8f;
                if (MathF.Abs(z - body.Z) < 0.9f)
                {
                    continue; // the doorway
                }

                c.M.Box(steel, new Vector3(body.X + sx * (hx - 0.02f), (floor + eaves) * 0.5f, z), new Vector3(0.05f, eaves - floor - 0.05f, 0.08f));
            }

            c.M.Box(steel, new Vector3(body.X + sx * (hx - 0.02f), floor + 0.9f, body.Z), new Vector3(0.05f, 0.08f, size.Z - 0.1f));
            // The sliding door, a little open on one side, on its top rail.
            float slide = sx > 0f ? c.R(0f, 0.7f) : 0f;
            c.M.Box(planks, new Vector3(body.X + sx * (hx + 0.03f), (floor + eaves) * 0.5f - 0.05f, body.Z + slide), new Vector3(0.05f, eaves - floor - 0.25f, 1.7f));
            c.M.Box(steel, new Vector3(body.X + sx * (hx + 0.06f), eaves - 0.06f, body.Z), new Vector3(0.04f, 0.06f, 3.6f));
            c.M.Box(dark, new Vector3(body.X + sx * (hx + 0.07f), floor + 1.0f, body.Z + slide + 0.7f), new Vector3(0.03f, 0.25f, 0.05f));
        }

        // A curved roof over the eaves: a circular segment from eave to eave, rising to the top of the body's box.
        float half = hx + 0.03f, rise = top - eaves;
        float radius = (half * half + rise * rise) / (2f * rise);
        var centre = new Vector2(0f, top - radius);
        float a0 = MathF.Atan2(eaves - centre.Y, half);
        var outline = new System.Collections.Generic.List<Vector2> { new(half, eaves - 0.03f) };
        Arc(outline, centre, radius, a0, Mathf.Pi - a0, 12);
        outline.Add(new Vector2(-half, eaves - 0.03f));
        c.M.Extrude(c.Mat("roof_felt"), new Vector3(body.X, 0f, body.Z), Basis.Identity, outline, size.Z + 0.06f);

        Underframe(c, body.Z, size.Z, floor);
    }

    /// <summary>An open wagon: a steel floor and low sides with stanchions and drop doors, open to the sky (and to paint).</summary>
    private static void OpenWagon(Ctx c)
    {
        Vector3 deck = c.Center(0), deckSize = c.Size(0);
        int steel = c.Default, rust = c.Mat("steel_rust");
        float floor = deck.Y - deckSize.Y * 0.5f;
        c.M.Box(steel, deck, deckSize);
        for (int i = 1; i <= 4 && i < c.Count; i++)
        {
            if (c.IsCylinder(i))
            {
                break;
            }

            Vector3 side = c.Center(i), s = c.Size(i);
            if (side.Y - s.Y * 0.5f < floor - 0.05f)
            {
                break; // the underframe
            }

            c.M.Box(steel, side, s);
            // Stanchions along the long sides, a top rail and a few dents.
            bool lengthwise = s.Z > s.X;
            float run = lengthwise ? s.Z : s.X;
            int posts = Math.Max(2, (int)(run / 1.1f));
            for (int k = 0; k <= posts; k++)
            {
                float f = -0.5f + (float)k / posts;
                Vector3 at = side + (lengthwise ? new Vector3(MathF.Sign(side.X) * (s.X * 0.5f + 0.03f), 0f, f * run) : new Vector3(f * run, 0f, MathF.Sign(side.Z) * (s.Z * 0.5f + 0.03f)));
                c.M.Box(rust, at, new Vector3(lengthwise ? 0.06f : 0.08f, s.Y, lengthwise ? 0.08f : 0.06f));
            }

            c.M.Box(rust, side + new Vector3(0f, s.Y * 0.5f, 0f), s + new Vector3(0.06f, -s.Y + 0.06f, 0.06f));
        }

        Underframe(c, deck.Z, deckSize.Z, floor);
    }

    /// <summary>A flat wagon with its load of big concrete pipes, chocked and chained down.</summary>
    private static void FlatWagon(Ctx c)
    {
        Vector3 deck = c.Center(0), deckSize = c.Size(0);
        int steel = c.Default, wood = c.Mat("wood_planks"), chain = c.Mat("steel_rust");
        float floor = deck.Y - deckSize.Y * 0.5f;
        c.M.Box(wood, deck + new Vector3(0f, 0.02f, 0f), deckSize - new Vector3(0.1f, 0.04f, 0.1f));
        c.M.Box(steel, deck - new Vector3(0f, 0.05f, 0f), new Vector3(deckSize.X, deckSize.Y * 0.5f, deckSize.Z));
        for (int i = 1; i < c.Count; i++)
        {
            if (!c.IsCylinder(i) || c.Center(i).Y < deck.Y + 0.2f)
            {
                continue;
            }

            // A pipe: a thick concrete ring lying along the wagon, hollow, with a socket at one end.
            Vector3 at = c.Center(i);
            float r = c.Radius(i), length = c.Height(i);
            int concrete = c.Mat("concrete_wall");
            Basis along = new Basis(Vector3.Right, Mathf.Pi * 0.5f);
            c.M.Lathe(concrete, at, along, new[]
            {
                new Vector2(r * 0.78f, -length * 0.5f), new Vector2(r, -length * 0.5f), new Vector2(r, length * 0.5f - 0.3f),
                new Vector2(r + 0.06f, length * 0.5f - 0.25f), new Vector2(r + 0.06f, length * 0.5f), new Vector2(r * 0.78f, length * 0.5f),
                new Vector2(r * 0.78f, -length * 0.5f),
            }, 20);
            if (at.Y - r < floor + deckSize.Y + 0.1f)
            {
                foreach (float z in new[] { at.Z - length * 0.3f, at.Z + length * 0.3f })
                {
                    c.M.Box(wood, new Vector3(at.X, floor + deckSize.Y + 0.08f, z), new Vector3(r * 1.6f, 0.16f, 0.2f));
                }
            }
        }

        foreach (float z in new[] { deck.Z - deckSize.Z * 0.3f, deck.Z + deckSize.Z * 0.3f })
        {
            c.M.Bar(chain, new Vector3(deck.X - deckSize.X * 0.5f, floor + deckSize.Y, z), new Vector3(deck.X, floor + deckSize.Y + 1.9f, z), 0.03f, 0.03f);
            c.M.Bar(chain, new Vector3(deck.X + deckSize.X * 0.5f, floor + deckSize.Y, z), new Vector3(deck.X, floor + deckSize.Y + 1.9f, z), 0.03f, 0.03f);
        }

        Underframe(c, deck.Z, deckSize.Z, floor);
    }

    /// <summary>A tank wagon: the barrel on its cradles, bands round it, the dome with its lid, a ladder and a walkway.</summary>
    private static void TankWagon(Ctx c)
    {
        Vector3 tank = c.Center(0);
        float r = c.Radius(0), length = c.Height(0);
        int black = c.Default, steel = c.Mat("steel_rust"), dark = c.Mat("steel_painted");
        Basis along = new Basis(Vector3.Right, Mathf.Pi * 0.5f);
        c.M.Lathe(black, tank, along, new[]
        {
            new Vector2(0f, -length * 0.5f), new Vector2(r * 0.7f, -length * 0.5f), new Vector2(r * 0.95f, -length * 0.5f + 0.15f),
            new Vector2(r, -length * 0.5f + 0.35f), new Vector2(r, length * 0.5f - 0.35f), new Vector2(r * 0.95f, length * 0.5f - 0.15f),
            new Vector2(r * 0.7f, length * 0.5f), new Vector2(0f, length * 0.5f),
        }, 24);
        foreach (float f in new[] { -0.36f, 0f, 0.36f })
        {
            c.M.Cylinder(steel, tank + new Vector3(0f, 0f, f * length), along, r + 0.02f, 0.1f, 24, caps: false);
        }

        float floor = tank.Y - r;
        foreach (float f in new[] { -0.3f, 0.3f })
        {
            c.M.Box(steel, new Vector3(tank.X, floor + 0.1f, tank.Z + f * length), new Vector3(r * 1.6f, 0.4f, 0.3f));
        }

        if (c.Count > 1 && c.IsCylinder(1))
        {
            Vector3 dome = c.Center(1);
            c.M.Cylinder(black, dome, Basis.Identity, c.Radius(1), c.Height(1), 16);
            c.M.Cylinder(dark, dome + new Vector3(0f, c.Height(1) * 0.5f + 0.03f, 0f), Basis.Identity, c.Radius(1) * 0.8f, 0.06f, 16);
        }

        // The ladder up the side (from the data, as it's climbed) to the walkway along the top.
        Ladders(c, dark);
        c.M.Box(dark, new Vector3(tank.X, tank.Y + r + 0.04f, tank.Z), new Vector3(0.5f, 0.04f, length * 0.4f));
        Underframe(c, tank.Z, length, floor - 0.05f);
    }

    /// <summary>
    /// A diesel shunter facing −Z: the long bonnet with louvres and handrails, the cab at the back with its windows, the
    /// exhaust stack, the footplate with steps, three axles with coupling rods, and buffers.
    /// </summary>
    private static void Shunter(Ctx c)
    {
        Vector3 bonnet = c.Center(0), bonnetSize = c.Size(0);
        Vector3 cab = c.Center(1), cabSize = c.Size(1);
        int paint = c.Default, dark = c.Mat("steel_painted"), rust = c.Mat("steel_rust"), glass = c.Mat("glass_dirty"), yellow = c.Mat("steel_yellow");
        float floor = bonnet.Y - bonnetSize.Y * 0.5f;

        c.M.Box(paint, bonnet, bonnetSize - new Vector3(0.06f, 0.06f, 0.06f));
        for (int i = 0; i < 6; i++)
        {
            float z = bonnet.Z - bonnetSize.Z * 0.42f + bonnetSize.Z * 0.84f * i / 5f;
            foreach (float sx in new[] { -1f, 1f })
            {
                c.M.Box(dark, new Vector3(bonnet.X + sx * (bonnetSize.X * 0.5f), bonnet.Y + 0.2f, z), new Vector3(0.03f, bonnetSize.Y * 0.45f, 0.5f));
            }
        }

        c.M.Box(dark, new Vector3(bonnet.X, bonnet.Y + bonnetSize.Y * 0.5f, bonnet.Z), new Vector3(bonnetSize.X * 0.6f, 0.05f, bonnetSize.Z * 0.9f));
        c.M.Cylinder(rust, new Vector3(bonnet.X + 0.3f, bonnet.Y + bonnetSize.Y * 0.5f + 0.25f, bonnet.Z + bonnetSize.Z * 0.2f), Basis.Identity, 0.1f, 0.5f, 10);
        foreach (float sx in new[] { -1f, 1f })
        {
            float x = bonnet.X + sx * (bonnetSize.X * 0.5f + 0.2f);
            c.M.Rod(yellow, new Vector3(x, floor + 1.0f, bonnet.Z - bonnetSize.Z * 0.5f), new Vector3(x, floor + 1.0f, cab.Z - cabSize.Z * 0.5f), 0.02f);
        }

        // The cab: walls with windows all round, a roof with a lip.
        c.M.Box(paint, new Vector3(cab.X, floor + 0.6f, cab.Z), new Vector3(cabSize.X - 0.06f, 1.2f, cabSize.Z - 0.06f));
        float winTop = floor + cabSize.Y - 0.35f, winBottom = floor + 1.25f;
        foreach (float sx in new[] { -1f, 1f })
        {
            c.M.Box(glass, new Vector3(cab.X + sx * (cabSize.X * 0.5f - 0.03f), (winTop + winBottom) * 0.5f, cab.Z), new Vector3(0.02f, winTop - winBottom, cabSize.Z - 0.4f));
            c.M.Box(paint, new Vector3(cab.X + sx * (cabSize.X * 0.5f - 0.05f), (winTop + winBottom) * 0.5f, cab.Z - cabSize.Z * 0.5f + 0.1f), new Vector3(0.1f, winTop - winBottom, 0.2f));
            c.M.Box(paint, new Vector3(cab.X + sx * (cabSize.X * 0.5f - 0.05f), (winTop + winBottom) * 0.5f, cab.Z + cabSize.Z * 0.5f - 0.1f), new Vector3(0.1f, winTop - winBottom, 0.2f));
        }

        foreach (float sz in new[] { -1f, 1f })
        {
            c.M.Box(glass, new Vector3(cab.X, (winTop + winBottom) * 0.5f, cab.Z + sz * (cabSize.Z * 0.5f - 0.03f)), new Vector3(cabSize.X - 0.5f, winTop - winBottom, 0.02f));
        }

        c.M.Box(paint, new Vector3(cab.X, (winTop + floor + cabSize.Y) * 0.5f, cab.Z), new Vector3(cabSize.X - 0.06f, floor + cabSize.Y - winTop, cabSize.Z - 0.06f));
        c.M.Box(dark, new Vector3(cab.X, floor + cabSize.Y + 0.02f, cab.Z), new Vector3(cabSize.X + 0.1f, 0.05f, cabSize.Z + 0.1f));

        // Three axles with coupling rods (no separate underframe drawing: the footplate is collider 2).
        if (c.Count > 2)
        {
            Vector3 plate = c.Center(2), plateSize = c.Size(2);
            c.M.Box(dark, plate, plateSize - new Vector3(0.02f, 0.02f, 0.02f));
            c.M.Box(yellow, new Vector3(plate.X, plate.Y, plate.Z - plateSize.Z * 0.5f - 0.01f), new Vector3(plateSize.X, plateSize.Y * 0.8f, 0.02f));
            c.M.Box(yellow, new Vector3(plate.X, plate.Y, plate.Z + plateSize.Z * 0.5f + 0.01f), new Vector3(plateSize.X, plateSize.Y * 0.8f, 0.02f));
            Buffers(c, plate.Z - plateSize.Z * 0.5f, plate.Z + plateSize.Z * 0.5f, plate.Y);
        }

        float? lastZ = null;
        for (int i = 3; i < c.Count; i++)
        {
            if (!c.IsCylinder(i) || c.Center(i).X < 0f)
            {
                continue;
            }

            Vector3 w = c.Center(i);
            Wheelset(c, w.Z, w.Y, c.Radius(i), rust, dark);
            if (lastZ is { } z0)
            {
                foreach (float sx in new[] { -1f, 1f })
                {
                    c.M.Bar(rust, new Vector3(sx * (WheelX + 0.12f), w.Y - 0.15f, z0), new Vector3(sx * (WheelX + 0.12f), w.Y - 0.15f, w.Z), 0.04f, 0.08f);
                }
            }

            lastZ = w.Z;
        }
    }

    /// <summary>A buffer stop: two rails bent up into struts, a heavy beam across with two buffer heads, on its sleepers.</summary>
    private static void BufferStop(Ctx c)
    {
        Vector3 box = c.Center(0), size = c.Size(0);
        int rust = c.Default, wood = c.Mat("sleeper_timber"), red = c.Mat("container_red");
        float bottom = box.Y - size.Y * 0.5f, top = box.Y + size.Y * 0.5f;
        float front = box.Z - size.Z * 0.5f, back = box.Z + size.Z * 0.5f;
        foreach (float x in new[] { -WheelX, WheelX })
        {
            c.M.Bar(rust, new Vector3(x, bottom + 0.1f, back), new Vector3(x, top - 0.2f, front + 0.2f), 0.07f, 0.14f);
            c.M.Bar(rust, new Vector3(x, bottom + 0.1f, front + 0.1f), new Vector3(x, top - 0.25f, front + 0.25f), 0.07f, 0.14f);
        }

        c.M.Box(red, new Vector3(box.X, top - 0.2f, front + 0.2f), new Vector3(size.X, 0.4f, 0.3f));
        foreach (float x in new[] { -0.85f, 0.85f })
        {
            c.M.Rod(rust, new Vector3(x, top - 0.2f, front + 0.1f), new Vector3(x, top - 0.2f, front - 0.15f), 0.08f, 10);
            c.M.Cylinder(rust, new Vector3(x, top - 0.2f, front - 0.17f), new Basis(Vector3.Right, Mathf.Pi * 0.5f), 0.2f, 0.04f, 14);
        }

        for (int i = 0; i < 3; i++)
        {
            c.M.Box(wood, new Vector3(box.X, bottom + 0.05f, front + 0.3f + i * 0.55f), new Vector3(2.6f, 0.1f, 0.25f));
        }
    }

    /// <summary>A crib of old sleepers, each layer across the last, a few askew.</summary>
    private static void SleeperStack(Ctx c)
    {
        Vector3 box = c.Center(0), size = c.Size(0);
        int wood = c.Default;
        float bottom = box.Y - size.Y * 0.5f;
        const float thick = 0.13f, wide = 0.25f;
        int layers = Math.Max(1, (int)(size.Y / thick));
        for (int l = 0; l < layers; l++)
        {
            bool across = l % 2 == 0;
            float run = across ? size.Z : size.X;
            float length = across ? size.X : size.Z;
            int n = Math.Max(2, (int)(run / (wide + 0.04f)));
            for (int k = 0; k < n; k++)
            {
                if (l == layers - 1 && c.Chance(0.3f))
                {
                    continue; // the top layer half taken
                }

                float f = -run * 0.5f + wide * 0.5f + (run - wide) * k / (n - 1);
                var at = new Vector3(box.X + (across ? 0f : f), bottom + thick * (l + 0.5f), box.Z + (across ? f : 0f));
                var s = new Vector3(across ? length : wide, thick - 0.005f, across ? wide : length);
                c.M.Box(wood, at, s * new Vector3(c.R(0.97f, 1f), 1f, c.R(0.97f, 1f)), new Basis(Vector3.Up, c.R(-0.03f, 0.03f)));
            }
        }
    }

    /// <summary>Lengths of rail lying on timber bearers, in a couple of layers.</summary>
    private static void RailStack(Ctx c)
    {
        Vector3 box = c.Center(0), size = c.Size(0);
        int rust = c.Default, wood = c.Mat("sleeper_timber");
        float bottom = box.Y - size.Y * 0.5f;
        bool alongZ = size.Z >= size.X;
        float length = alongZ ? size.Z : size.X, width = alongZ ? size.X : size.Z;
        Vector3 axis = alongZ ? Vector3.Back : Vector3.Right, side = alongZ ? Vector3.Right : Vector3.Back;
        foreach (float f in new[] { -0.4f, 0f, 0.4f })
        {
            c.M.Box(wood, new Vector3(box.X, bottom + 0.07f, box.Z) + axis * (f * length), alongZ ? new Vector3(width, 0.14f, 0.2f) : new Vector3(0.2f, 0.14f, width));
        }

        int perLayer = Math.Max(2, (int)(width / 0.18f));
        int layers = Math.Max(1, (int)((size.Y - 0.14f) / 0.16f));
        for (int l = 0; l < layers; l++)
        {
            for (int k = 0; k < perLayer; k++)
            {
                float s = -width * 0.5f + 0.09f + (width - 0.18f) * k / Math.Max(1, perLayer - 1);
                Vector3 at = new Vector3(box.X, bottom + 0.14f + 0.08f + l * 0.16f, box.Z) + side * s + axis * c.R(-0.15f, 0.15f);
                Vector3 extent = alongZ ? new Vector3(0.07f, 0.15f, length * c.R(0.92f, 1f)) : new Vector3(length * c.R(0.92f, 1f), 0.15f, 0.07f);
                c.M.Box(rust, at, extent);
            }
        }
    }

    /// <summary>The underframe of a wagon <paramref name="length"/> long, floor at <paramref name="floor"/>: solebars, headstocks, wheelsets, buffers.</summary>
    private static void Underframe(Ctx c, float midZ, float length, float floor)
    {
        int rust = c.Mat("steel_rust"), dark = c.Mat("steel_painted");
        float y = floor - 0.15f;
        foreach (float sx in new[] { -1f, 1f })
        {
            c.M.Box(rust, new Vector3(sx * 1.05f, y, midZ), new Vector3(0.1f, 0.3f, length + 0.1f));
        }

        foreach (float sz in new[] { -1f, 1f })
        {
            c.M.Box(rust, new Vector3(0f, y, midZ + sz * (length * 0.5f + 0.05f)), new Vector3(2.3f, 0.32f, 0.12f));
        }

        Buffers(c, midZ - length * 0.5f - 0.1f, midZ + length * 0.5f + 0.1f, y);
        for (int i = 0; i < c.Count; i++)
        {
            if (c.IsCylinder(i) && c.Center(i).X > 0f && c.Center(i).Y < 1.2f && MathF.Abs(c.Radius(i) - WheelRadius) < 0.15f)
            {
                Wheelset(c, c.Center(i).Z, c.Center(i).Y, c.Radius(i), rust, dark);
            }
        }
    }

    /// <summary>A pair of wheels on their axle at <paramref name="z"/>, with axle boxes and leaf springs up to the solebars.</summary>
    private static void Wheelset(Ctx c, float z, float axleY, float radius, int rust, int dark)
    {
        Basis axle = new Basis(Vector3.Back, -Mathf.Pi / 2f);
        c.M.Rod(dark, new Vector3(-WheelX - 0.1f, axleY, z), new Vector3(WheelX + 0.1f, axleY, z), 0.07f, 8);
        foreach (float sx in new[] { -1f, 1f })
        {
            float x = sx * WheelX;
            c.M.Cylinder(rust, new Vector3(x, axleY, z), axle, radius, 0.12f, 18);
            c.M.Cylinder(rust, new Vector3(x - sx * 0.07f, axleY, z), axle, radius + 0.03f, 0.025f, 18); // the flange, inside
            c.M.Box(dark, new Vector3(x + sx * 0.16f, axleY, z), new Vector3(0.18f, 0.24f, 0.26f));
            c.M.Box(rust, new Vector3(x + sx * 0.16f, axleY + 0.22f, z), new Vector3(0.1f, 0.06f, 1.1f));
        }
    }

    /// <summary>Spring buffers and a screw coupling at both ends, at height <paramref name="y"/>.</summary>
    private static void Buffers(Ctx c, float front, float back, float y)
    {
        int rust = c.Mat("steel_rust"), dark = c.Mat("steel_painted");
        foreach ((float z, float dir) in new[] { (front, -1f), (back, 1f) })
        {
            foreach (float x in new[] { -0.87f, 0.87f })
            {
                c.M.Rod(dark, new Vector3(x, y, z), new Vector3(x, y, z + dir * 0.4f), 0.07f, 10);
                c.M.Cylinder(rust, new Vector3(x, y, z + dir * 0.42f), new Basis(Vector3.Right, Mathf.Pi * 0.5f), 0.19f, 0.04f, 14);
            }

            c.M.Bar(rust, new Vector3(0f, y, z), new Vector3(0f, y - 0.25f, z + dir * 0.45f), 0.04f, 0.04f);
        }
    }
}
