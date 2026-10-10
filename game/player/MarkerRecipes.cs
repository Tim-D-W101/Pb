using Godot;
using Pb.Game.World;
using Pb.Sim.Gear;

namespace Pb.Game.Player;

/// <summary>
/// The markers built in code, one per brand, in the marker's frame (<see cref="MarkerShape"/>'s: +X right, +Y up,
/// −Z forward, the receiver's centre at the origin, the barrel's axis 2 cm above it, the muzzle 0.43 m forward). They
/// share the field marker's ergonomics, so the gloved hands (<see cref="HandShape"/>) close on every one's grips, a
/// loader sits on every feed neck and a bottle screws into every back: the pistol grip, trigger and guard stand where
/// the field's do, about the grip's top, and the foregrip where each recipe puts it. What differs is everything else:
/// Kilnmark's slab-sided block with bolted plates, fins and a ported brake; Vellis's sculpted body, swept stripe and
/// two-tone barrel; Quarrow's railed receiver and slotted handguard with the foregrip out on it. Each prints its
/// brand's mark on both sides.
/// </summary>
public static class MarkerRecipes
{
    public static readonly string[] Shapes = { "marker_forge", "marker_glide", "marker_ranger", "marker_field" };

    public static GearBuild Build(string shape, ShapeMesh m, GearBrand brand) => shape switch
    {
        "marker_forge" => Forge(m, brand),
        "marker_glide" => Glide(m, brand),
        "marker_ranger" => Ranger(m, brand),
        _ => Field(m),
    };

    /// <summary>Side profiles are drawn in (forward, up) and extruded across the marker.</summary>
    private static readonly Basis Side = new(Vector3.Forward, Vector3.Up, Vector3.Right);

    /// <summary>A rotation whose +Y runs forward (−Z), for parts turned on a lathe down the barrel.</summary>
    private static readonly Basis AlongForward = new(Vector3.Right, -Mathf.Pi / 2f);

    /// <summary>A rotation whose +Y runs back (+Z).</summary>
    private static readonly Basis AlongBack = new(Vector3.Right, Mathf.Pi / 2f);

    /// <summary>A rotation whose +Y runs across the marker, for screws and bolts in its sides.</summary>
    private static readonly Basis Across = new(Vector3.Back, Mathf.Pi / 2f);

    private static Vector3 At(float forward, float up, float right = 0f) => new(right, up, -forward);

    private static Vector2[] Outline(params float[] xy)
    {
        var points = new Vector2[xy.Length / 2];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
        }

        return points;
    }

    // Shared parts, about the pistol grip's top (g) and the foregrip's top (f).

    /// <summary>The pistol grip, raked back from its top (the field marker's outline).</summary>
    private static void PistolGrip(ShapeMesh m, int zone, Vector3 g, float width) =>
        m.Extrude(zone, g, Side, Outline(-0.049f, -0.094f, -0.007f, -0.092f, 0.011f, -0.054f, 0.017f, -0.028f, 0.023f, 0.004f, -0.023f, 0.004f, -0.033f, -0.034f, -0.043f, -0.072f), width);

    /// <summary>The foregrip, hanging from its top.</summary>
    private static void Foregrip(ShapeMesh m, int zone, Vector3 f, float width) =>
        m.Extrude(zone, f, Side, Outline(-0.024f, -0.094f, 0.008f, -0.092f, 0.013f, -0.034f, 0.018f, 0.004f, -0.018f, 0.004f, -0.023f, -0.034f), width);

    /// <summary>The trigger guard in front of the grip, as bars of <paramref name="width"/> by <paramref name="depth"/>.</summary>
    private static void Guard(ShapeMesh m, int zone, Vector3 g, float width, float depth, bool rounded = false)
    {
        if (rounded)
        {
            Vector3[] points = { At(0.024f, 0.004f), At(0.027f, -0.024f), At(0.036f, -0.037f), At(0.052f, -0.041f), At(0.072f, -0.04f), At(0.084f, -0.03f), At(0.09f, 0.004f) };
            for (int i = 0; i + 1 < points.Length; i++)
            {
                m.Bar(zone, g + points[i], g + points[i + 1], width, depth);
            }

            return;
        }

        m.Bar(zone, g + At(0.024f, 0.004f), g + At(0.029f, -0.039f), width, depth);
        m.Bar(zone, g + At(0.029f, -0.039f), g + At(0.085f, -0.039f), width, depth);
        m.Bar(zone, g + At(0.085f, -0.039f), g + At(0.09f, 0.004f), width, depth);
    }

    /// <summary>The trigger, where the trigger hand's index finger curls onto it.</summary>
    private static void Trigger(ShapeMesh m, int zone, Vector3 g, float width = 0.007f)
    {
        m.Bar(zone, g + At(0.059f, 0.004f), g + At(0.055f, -0.019f), width, 0.005f);
        m.Bar(zone, g + At(0.055f, -0.019f), g + At(0.063f, -0.03f), width, 0.005f);
    }

    /// <summary>The frame under the receiver, from behind the grip to the front of the guard, up to <paramref name="top"/>.</summary>
    private static void Frame(ShapeMesh m, int zone, Vector3 g, float width, float top)
    {
        float bottom = g.Y + 0.004f;
        m.Box(zone, new Vector3(0f, (bottom + top) * 0.5f, g.Z - 0.039f), new Vector3(width, top - bottom, 0.112f));
    }

    /// <summary>A hex bolt's head in a side of the marker, facing out along ±X.</summary>
    private static void Bolt(ShapeMesh m, int zone, Vector3 at, float radius) =>
        m.Cylinder(zone, at, Across, radius, 0.0018f, 6);

    /// <summary>The usual ergonomics: where the hands go, and where a loader and a bottle fit.</summary>
    private static GearBuild Ergonomics(Vector3 g, Vector3 f, Vector3 feed, Vector3 tankMount) => new()
    {
        PistolGrip = g,
        Foregrip = f,
        // As the generated marker's: the trigger wrist behind and below the grip's top, the support wrist under the foregrip.
        TriggerWrist = g + At(-0.055f, -0.045f),
        SupportWrist = f + At(0f, -0.095f),
        Feed = feed,
        TankMount = tankMount,
        Nominal = MarkerShape.NominalBox(MarkerGroup.Marker),
    };

    /// <summary>Barrel ports: slots of <paramref name="length"/> along the barrel at <paramref name="forward"/>, at each angle from the top.</summary>
    private static void Ports(ShapeMesh m, int zone, float forward, float radius, float length, params float[] angles)
    {
        foreach (float a in angles)
        {
            Vector3 radial = new(Mathf.Sin(a), Mathf.Cos(a), 0f);
            m.Box(zone, At(forward, MarkerShape.BarrelHeight) + radial * radius, new Vector3(0.0055f, 0.0016f, length),
                new Basis(radial.Cross(Vector3.Forward), radial, Vector3.Forward));
        }
    }

    /// <summary>
    /// Kilnmark's Forge: a slab-sided block, its sides bolted plates in the second colour with the mark stencilled on,
    /// cooling fins along the top, a hex back cap, a square feed tower, a heavy barrel ending in a ported brake, and
    /// knurled grips.
    /// </summary>
    private static GearBuild Forge(ShapeMesh m, GearBrand brand)
    {
        int main = GearZone.Of(GearFinish.Matt, GearPaint.Main), second = GearZone.Of(GearFinish.Matt, GearPaint.Second);
        int accent = GearZone.Of(GearFinish.Matt, GearPaint.Accent), mark = GearZone.Of(GearFinish.Mark, GearPaint.Accent);
        Vector3 g = HandShape.PistolGripTop, f = HandShape.ForegripTop;

        m.Extrude(main, Vector3.Zero, Side, Outline(-0.136f, -0.033f, 0.124f, -0.033f, 0.136f, -0.021f, 0.136f, 0.033f, 0.123f, 0.047f,
            -0.108f, 0.047f, -0.122f, 0.038f, -0.136f, 0.038f), 0.046f);
        foreach (float side in new[] { -1f, 1f })
        {
            m.Box(second, At(0.004f, 0.007f, side * 0.0236f), new Vector3(0.0016f, 0.05f, 0.19f));
            foreach ((float fw, float up) in new[] { (-0.083f, 0.026f), (0.091f, 0.026f), (-0.083f, -0.012f), (0.091f, -0.012f) })
            {
                Bolt(m, GearZone.Steel, At(fw, up, side * 0.0249f), 0.0042f);
            }

            // Read from either side: towards the muzzle on the right, towards the back on the left.
            BrandMarks.Print(m, mark, brand, At(0.004f, 0.007f, side * 0.0244f), side > 0f ? Vector3.Forward : Vector3.Back, Vector3.Up, 0.026f);
        }

        // Fins either side of the feed tower, the hex back cap, and a raised strap where the loader's tower stands.
        foreach (float fw in new[] { -0.098f, -0.078f, -0.058f, 0.058f, 0.078f, 0.098f })
        {
            m.Box(main, At(fw, 0.0515f), new Vector3(0.038f, 0.009f, 0.0065f));
        }

        m.Cylinder(accent, At(-0.142f, 0.007f), AlongForward, 0.019f, 0.012f, 6);
        m.Box(main, At(0f, 0.064f), new Vector3(0.034f, 0.038f, 0.034f));
        m.Box(accent, At(0f, 0.0855f), new Vector3(0.044f, 0.009f, 0.044f));
        m.Box(GearZone.Steel, At(-0.012f, 0.0855f, 0.026f), new Vector3(0.006f, 0.006f, 0.03f));
        Bolt(m, GearZone.Steel, At(0f, 0.0855f, 0.0226f), 0.0028f);

        // The heavy barrel and its brake: three raised rings, slotted on top and the upper sides.
        const float start = 0.136f;
        float length = MarkerShape.MuzzleForward - start;
        m.Lathe(second, At(start, MarkerShape.BarrelHeight), AlongForward, Outline(
            0f, 0f, 0.0185f, 0f, 0.0185f, 0.022f, 0.016f, 0.026f, 0.016f, length - 0.078f, 0.0198f, length - 0.074f,
            0.0198f, length - 0.058f, 0.0182f, length - 0.055f, 0.0182f, length - 0.049f, 0.0198f, length - 0.046f,
            0.0198f, length - 0.03f, 0.0182f, length - 0.027f, 0.0182f, length - 0.021f, 0.0198f, length - 0.018f,
            0.0198f, length - 0.002f, 0.0185f, length, 0.0095f, length, 0.0095f, length - 0.03f, 0f, length - 0.03f), 20);
        foreach (float along in new[] { length - 0.066f, length - 0.038f, length - 0.01f })
        {
            Ports(m, GearZone.DarkSteel, start + along, 0.0197f, 0.009f, -0.75f, 0f, 0.75f);
        }

        Frame(m, main, g, 0.034f, -0.031f);
        PistolGrip(m, GearZone.Knurl, g, 0.035f);
        foreach (float side in new[] { -1f, 1f })
        {
            Bolt(m, GearZone.Steel, g + At(-0.012f, -0.03f, side * 0.0177f), 0.0032f);
        }

        Guard(m, main, g, 0.011f, 0.009f);
        Trigger(m, GearZone.Steel, g);
        Foregrip(m, GearZone.Knurl, f, 0.031f);

        // The air's socket out of the back, under the receiver.
        m.Cylinder(GearZone.Steel, At(-0.141f, -0.04f), AlongBack, 0.0145f, 0.012f, 14);
        return Ergonomics(g, f, At(0f, 0.09f), At(-0.147f, -0.04f));
    }

    /// <summary>
    /// Vellis's Glide: a sculpted body with a raised spine, a swept stripe and eye covers in its colours, a slim neck
    /// and collar, a two-tone barrel with spiral ports, a long blade trigger and a rounded guard.
    /// </summary>
    private static GearBuild Glide(ShapeMesh m, GearBrand brand)
    {
        int main = GearZone.Of(GearFinish.Polished, GearPaint.Main), second = GearZone.Of(GearFinish.Polished, GearPaint.Second);
        int accent = GearZone.Of(GearFinish.Gloss, GearPaint.Accent), mark = GearZone.Of(GearFinish.Mark, GearPaint.Accent);
        Vector3 g = HandShape.PistolGripTop, f = HandShape.ForegripTop + At(0.006f, 0f);

        m.Extrude(main, Vector3.Zero, Side, Outline(-0.13f, -0.028f, 0.1f, -0.03f, 0.122f, -0.022f, 0.133f, -0.006f, 0.134f, 0.008f,
            0.126f, 0.02f, 0.108f, 0.026f, 0.03f, 0.028f, -0.06f, 0.026f, -0.11f, 0.02f, -0.128f, 0.008f, -0.134f, -0.01f), 0.034f);
        m.Extrude(main, Vector3.Zero, Side, Outline(-0.1f, 0.02f, 0.1f, 0.02f, 0.112f, 0.026f, 0.1f, 0.034f, -0.04f, 0.036f,
            -0.09f, 0.031f, -0.104f, 0.025f), 0.022f);
        foreach (float side in new[] { -1f, 1f })
        {
            // The stripe sweeps up from the back to the nose.
            m.Extrude(second, At(0f, 0f, side * 0.0176f), Side, Outline(-0.112f, -0.014f, -0.06f, -0.009f, 0f, 0.0f, 0.06f, 0.009f,
                0.108f, 0.016f, 0.118f, 0.021f, 0.106f, 0.024f, 0.06f, 0.018f, 0f, 0.01f, -0.06f, 0.0f, -0.116f, -0.006f), 0.0012f);
            m.Pillow(accent, At(0.045f, 0.004f, side * 0.0172f), new Vector3(0.006f, 0.013f, 0.026f), Basis.Identity, 2.6f, 4, 10);
            BrandMarks.Print(m, mark, brand, At(-0.05f, -0.017f, side * 0.0176f), side > 0f ? Vector3.Forward : Vector3.Back, Vector3.Up, 0.017f);
        }

        // A slim neck and its collar, with a small lever.
        m.Cylinder(main, At(0f, 0.052f), Basis.Identity, 0.0145f, 0.05f, 16);
        m.Cylinder(accent, At(0f, 0.0825f), Basis.Identity, 0.0185f, 0.015f, 18);
        m.Box(accent, At(0f, 0.0825f, 0.022f), new Vector3(0.007f, 0.008f, 0.024f));

        // The barrel's back in the main colour, the long front in the second, flared at the muzzle.
        const float start = 0.134f, joint = 0.2f;
        m.Lathe(main, At(start, MarkerShape.BarrelHeight), AlongForward, Outline(
            0f, 0f, 0.0158f, 0f, 0.0158f, 0.022f, 0.0146f, 0.025f, 0.0146f, joint - start, 0f, joint - start), 18);
        float front = MarkerShape.MuzzleForward - joint;
        m.Lathe(second, At(joint, MarkerShape.BarrelHeight), AlongForward, Outline(
            0f, 0f, 0.0128f, 0f, 0.0124f, 0.03f, 0.0118f, front - 0.024f, 0.0134f, front - 0.02f, 0.0134f, front - 0.003f,
            0.0124f, front, 0.0091f, front, 0.0091f, front - 0.03f, 0f, front - 0.03f), 18);
        for (int i = 0; i < 10; i++)
        {
            Ports(m, GearZone.DarkSteel, MarkerShape.MuzzleForward - 0.034f + i * 0.0026f, 0.0118f, 0.004f, i * 0.9f);
        }

        Frame(m, main, g, 0.03f, -0.025f);
        PistolGrip(m, GearZone.Rubber, g, 0.03f);
        // A strip down the back of the grip in the accent colour.
        m.Bar(accent, g + At(-0.025f, 0.002f), g + At(-0.035f, -0.034f), 0.005f, 0.031f);
        m.Bar(accent, g + At(-0.035f, -0.034f), g + At(-0.045f, -0.072f), 0.005f, 0.031f);
        Guard(m, main, g, 0.008f, 0.006f, rounded: true);
        Trigger(m, accent, g, 0.008f);
        m.Bar(accent, g + At(0.063f, -0.03f), g + At(0.068f, -0.034f), 0.008f, 0.005f);
        Foregrip(m, GearZone.Rubber, f, 0.028f);
        m.Bar(second, f + At(0.019f, 0.002f), f + At(0.014f, -0.034f), 0.004f, 0.026f);
        m.Bar(second, f + At(0.014f, -0.034f), f + At(0.009f, -0.09f), 0.004f, 0.026f);

        m.Cylinder(accent, At(-0.137f, -0.04f), AlongBack, 0.014f, 0.01f, 16);
        return Ergonomics(g, f, At(0f, 0.09f), At(-0.142f, -0.04f));
    }

    /// <summary>
    /// Quarrow's Ranger: a long railed receiver, a slotted handguard round the barrel's back with a rail under it and
    /// the foregrip out on that, a black barrel with a slotted hider, a stippled grip with a beavertail.
    /// </summary>
    private static GearBuild Ranger(ShapeMesh m, GearBrand brand)
    {
        int main = GearZone.Of(GearFinish.Matt, GearPaint.Main), second = GearZone.Of(GearFinish.Matt, GearPaint.Second);
        int accent = GearZone.Of(GearFinish.Matt, GearPaint.Accent), mark = GearZone.Of(GearFinish.Mark, GearPaint.Accent);
        Vector3 g = HandShape.PistolGripTop, f = At(0.2f, -0.012f);

        m.Extrude(main, Vector3.Zero, Side, Outline(-0.13f, -0.031f, 0.138f, -0.031f, 0.138f, 0.031f, 0.128f, 0.036f, -0.12f, 0.036f,
            -0.13f, 0.027f), 0.044f);

        // The top rail, either side of the feed, toothed.
        foreach ((float from, float to) in new[] { (-0.118f, -0.026f), (0.026f, 0.134f) })
        {
            m.Box(GearZone.DarkSteel, At((from + to) * 0.5f, 0.039f), new Vector3(0.022f, 0.006f, to - from));
            for (float fw = from + 0.005f; fw < to - 0.003f; fw += 0.01f)
            {
                m.Box(GearZone.DarkSteel, At(fw, 0.0435f), new Vector3(0.026f, 0.0035f, 0.005f));
            }
        }

        // The handguard, slotted along both upper sides, its rail underneath.
        const float guardFrom = 0.138f, guardTo = 0.33f;
        float guard = guardTo - guardFrom;
        m.Lathe(second, At(guardFrom, MarkerShape.BarrelHeight), AlongForward, Outline(
            0f, 0f, 0.0215f, 0f, 0.0235f, 0.004f, 0.0235f, guard - 0.004f, 0.0215f, guard, 0f, guard), 20);
        foreach (float along in new[] { 0.035f, 0.07f, 0.105f, 0.14f })
        {
            Ports(m, accent, guardFrom + along, 0.0234f, 0.024f, -1.15f, 1.15f);
        }

        m.Box(GearZone.DarkSteel, At(0.236f, -0.0065f), new Vector3(0.018f, 0.007f, 0.17f));
        foreach (float side in new[] { -1f, 1f })
        {
            BrandMarks.Print(m, mark, brand, At(0.232f, MarkerShape.BarrelHeight - 0.001f, side * 0.0236f), side > 0f ? Vector3.Forward : Vector3.Back, Vector3.Up, 0.015f);
        }

        // The barrel, black, and a slotted hider at the muzzle.
        m.Lathe(accent, At(guardTo, MarkerShape.BarrelHeight), AlongForward, Outline(0f, 0f, 0.0125f, 0f, 0.0125f, 0.072f, 0f, 0.072f), 16);
        const float hider = 0.4f;
        m.Lathe(GearZone.DarkSteel, At(hider, MarkerShape.BarrelHeight), AlongForward, Outline(
            0f, 0f, 0.0155f, 0f, 0.0155f, MarkerShape.MuzzleForward - hider, 0.0091f, MarkerShape.MuzzleForward - hider, 0.0091f, 0.005f, 0f, 0.005f), 16);
        Ports(m, GearZone.Rubber, hider + 0.017f, 0.0154f, 0.018f, 0f, 1.2f, -1.2f, 2.4f, -2.4f);

        // The feed through the gap in the rail, and a sling plate at the back.
        m.Box(GearZone.DarkSteel, At(0f, 0.062f), new Vector3(0.03f, 0.042f, 0.03f));
        m.Box(accent, At(0f, 0.0865f), new Vector3(0.038f, 0.007f, 0.038f));
        m.Box(GearZone.Steel, At(-0.124f, 0.012f, 0.0235f), new Vector3(0.003f, 0.016f, 0.012f));

        Frame(m, main, g, 0.034f, -0.03f);
        PistolGrip(m, GearZone.Knurl, g, 0.033f);
        m.Box(main, g + At(-0.03f, 0.012f), new Vector3(0.034f, 0.012f, 0.03f));
        Guard(m, accent, g, 0.01f, 0.008f);
        Trigger(m, GearZone.DarkSteel, g);
        // The foregrip on the handguard's rail, clamped.
        m.Box(GearZone.DarkSteel, f + At(0f, 0.004f), new Vector3(0.026f, 0.008f, 0.04f));
        Foregrip(m, GearZone.Knurl, f, 0.03f);

        m.Cylinder(GearZone.DarkSteel, At(-0.135f, -0.04f), AlongBack, 0.0145f, 0.01f, 14);
        return Ergonomics(g, f, At(0f, 0.09f), At(-0.14f, -0.04f));
    }

    /// <summary>The field's own (Norrel's), the marker built in code that stands in for the generated one: <see cref="MarkerShape"/>'s.</summary>
    private static GearBuild Field(ShapeMesh m)
    {
        var shape = new ShapeMesh();
        MarkerShape.Build(shape, MarkerGroup.Marker, closeUp: true);
        m.Absorb(shape, Transform3D.Identity, part => (MarkerPart)part switch
        {
            MarkerPart.Body => GearZone.Main,
            MarkerPart.Barrel => GearZone.Second,
            MarkerPart.Rubber => GearZone.Rubber,
            _ => GearZone.Of(GearFinish.Metal, GearPaint.Accent),
        });
        return Ergonomics(HandShape.PistolGripTop, HandShape.ForegripTop, At(0f, 0.09f), At(-0.13f, -0.04f));
    }
}
