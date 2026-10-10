using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.World;
using Pb.Sim.Gear;

namespace Pb.Game.Player;

/// <summary>
/// The loaders built in code, one per brand, in the loader's own frame: the middle of its feed's bottom at the origin
/// (where it sits on a marker's neck, <see cref="GearBuild.Feed"/>), +Y up, −Z forward. Kilnmark's Hod is a squared-off
/// opaque box with a ribbed lid and a steel band; Vellis's Halo a low, wide, see-through shell with a ring round its
/// middle and a bright speed lid; Quarrow's Satchel a loader in a cloth cover with a pouch, straps and webbing; Norrel's
/// Bowl the field's smoked loader (<see cref="MarkerShape"/>'s). The see-through ones hold paint, which shows.
/// </summary>
public static class LoaderRecipes
{
    public static readonly string[] Shapes = { "loader_hod", "loader_halo", "loader_satchel", "loader_bowl" };

    /// <param name="paint">Whether the paint in a see-through loader is built in (opponents' gear); first person draws its own, as full as the loader is.</param>
    public static GearBuild Build(string shape, ShapeMesh m, GearBrand brand, int seed, bool paint) => shape switch
    {
        "loader_hod" => Hod(m, brand),
        "loader_halo" => Halo(m, brand, seed, paint),
        "loader_satchel" => Satchel(m, brand),
        _ => Bowl(m, seed, paint),
    };

    /// <summary>The field's loader on its marker sits this high above the marker's centre: its frame's origin.</summary>
    private static readonly Vector3 FieldFeed = new(0f, 0.089f, 0f);

    private static GearBuild Hod(ShapeMesh m, GearBrand brand)
    {
        int main = GearZone.Of(GearFinish.Plastic, GearPaint.Main), second = GearZone.Of(GearFinish.Matt, GearPaint.Second);
        int accent = GearZone.Of(GearFinish.Metal, GearPaint.Accent), mark = GearZone.Of(GearFinish.Mark, GearPaint.Accent);
        m.Cylinder(GearZone.DarkSteel, new Vector3(0f, 0.008f, 0f), Basis.Identity, 0.021f, 0.016f, 16);
        m.Pillow(main, new Vector3(0f, 0.064f, 0.004f), new Vector3(0.112f, 0.104f, 0.185f), Basis.Identity, 5.5f, 10, 20);
        // A steel band round the waist, riveted.
        m.Pillow(accent, new Vector3(0f, 0.046f, 0.004f), new Vector3(0.1165f, 0.012f, 0.1895f), Basis.Identity, 5.5f, 4, 20);
        foreach (Vector3 at in new[] { new Vector3(0.0585f, 0.046f, -0.05f), new Vector3(0.0585f, 0.046f, 0.06f), new Vector3(-0.0585f, 0.046f, -0.05f), new Vector3(-0.0585f, 0.046f, 0.06f) })
        {
            m.Cylinder(GearZone.Steel, at, new Basis(Vector3.Back, Mathf.Pi / 2f), 0.0032f, 0.003f, 8);
        }

        // The lid, ribbed across, and its catch.
        m.Pillow(second, new Vector3(0f, 0.117f, 0.014f), new Vector3(0.082f, 0.014f, 0.112f), Basis.Identity, 4f, 4, 16);
        for (int i = 0; i < 4; i++)
        {
            m.Box(second, new Vector3(0f, 0.1245f, -0.02f + i * 0.022f), new Vector3(0.07f, 0.004f, 0.006f));
        }

        m.Box(accent, new Vector3(0f, 0.112f, -0.045f), new Vector3(0.022f, 0.012f, 0.008f));
        foreach (float side in new[] { -1f, 1f })
        {
            BrandMarks.Print(m, mark, brand, new Vector3(side * 0.0562f, 0.074f, 0.004f), side > 0f ? Vector3.Forward : Vector3.Back, Vector3.Up, 0.026f);
        }

        return new GearBuild();
    }

    private static GearBuild Halo(ShapeMesh m, GearBrand brand, int seed, bool paint)
    {
        int shell = GearZone.Of(GearFinish.Shell, GearPaint.Main), second = GearZone.Of(GearFinish.Gloss, GearPaint.Second);
        int accent = GearZone.Of(GearFinish.Gloss, GearPaint.Accent), mark = GearZone.Of(GearFinish.Mark, GearPaint.Accent);
        Vector3 centre = new(0f, 0.054f, 0.008f), size = new(0.124f, 0.096f, 0.2f);
        m.Cylinder(accent, new Vector3(0f, 0.007f, 0f), Basis.Identity, 0.0195f, 0.014f, 18);
        m.Pillow(shell, centre, size, Basis.Identity, 2.4f, 10, 20);
        // The halo: a thin ring round the shell, a little under its middle.
        const int segments = 28;
        float ringY = centre.Y - 0.008f;
        Vector3 Ring(int i)
        {
            // On the shell's superellipsoid (|x|ⁿ + |y|ⁿ + |z|ⁿ = 1, n its squareness) at the ring's height, a hair outside.
            const float n = 2.4f;
            float a = Mathf.Tau * i / segments;
            float squeeze = MathF.Pow(1f - MathF.Pow(MathF.Abs((ringY - centre.Y) / (size.Y * 0.5f)), n), 1f / n);
            float c = MathF.Cos(a), s = MathF.Sin(a);
            float r = MathF.Pow(MathF.Pow(MathF.Abs(c), n) + MathF.Pow(MathF.Abs(s), n), -1f / n) * squeeze;
            return new Vector3(c * r * (size.X * 0.5f + 0.002f), ringY, centre.Z + s * r * (size.Z * 0.5f + 0.002f));
        }

        for (int i = 0; i < segments; i++)
        {
            m.Bar(accent, Ring(i), Ring(i + 1), 0.0055f, 0.0045f);
        }

        // The speed lid on top, in the second colour, with its slot and the mark on it (read from behind, in first person).
        m.Pillow(second, new Vector3(0f, 0.1f, 0.02f), new Vector3(0.072f, 0.016f, 0.104f), Basis.Identity, 3f, 4, 16);
        m.Box(GearZone.DarkSteel, new Vector3(0f, 0.106f, -0.016f), new Vector3(0.032f, 0.006f, 0.008f));
        BrandMarks.Print(m, mark, brand, new Vector3(0f, 0.1082f, 0.03f), Vector3.Right, Vector3.Forward, 0.016f);

        var build = new GearBuild { Balls = Settle(centre, size, seed) };
        if (paint)
        {
            Balls(m, build.Balls, closeUp: false);
        }

        return build;
    }

    private static GearBuild Satchel(ShapeMesh m, GearBrand brand)
    {
        int main = GearZone.Of(GearFinish.Fabric, GearPaint.Main), second = GearZone.Of(GearFinish.Fabric, GearPaint.Second);
        int accent = GearZone.Of(GearFinish.Fabric, GearPaint.Accent), mark = GearZone.Of(GearFinish.Mark, GearPaint.Accent);
        m.Cylinder(GearZone.DarkSteel, new Vector3(0f, 0.008f, 0f), Basis.Identity, 0.021f, 0.016f, 16);
        m.Pillow(main, new Vector3(0f, 0.064f, 0.004f), new Vector3(0.114f, 0.108f, 0.19f), Basis.Identity, 3.2f, 10, 20);
        // A pouch on the front with its flap, straps round the sides and over the top, webbing on the sides.
        m.Pillow(second, new Vector3(0f, 0.05f, -0.096f), new Vector3(0.07f, 0.05f, 0.03f), Basis.Identity, 3f, 6, 12);
        m.Pillow(second, new Vector3(0f, 0.077f, -0.097f), new Vector3(0.074f, 0.012f, 0.036f), Basis.Identity, 3f, 4, 12);
        m.Box(GearZone.Of(GearFinish.Plastic, GearPaint.Accent), new Vector3(0f, 0.07f, -0.1125f), new Vector3(0.016f, 0.012f, 0.004f));
        foreach (float z in new[] { -0.045f, 0.055f })
        {
            m.Box(accent, new Vector3(0f, 0.1185f, z), new Vector3(0.09f, 0.003f, 0.02f));
            foreach (float side in new[] { -1f, 1f })
            {
                m.Box(accent, new Vector3(side * 0.0575f, 0.07f, z), new Vector3(0.003f, 0.08f, 0.02f));
            }
        }

        foreach (float side in new[] { -1f, 1f })
        {
            for (int row = 0; row < 3; row++)
            {
                m.Box(accent, new Vector3(side * 0.0572f, 0.035f + row * 0.016f, 0.005f), new Vector3(0.0025f, 0.006f, 0.08f));
            }

            // A patch with the mark, high on the side.
            m.Box(second, new Vector3(side * 0.0572f, 0.093f, 0.005f), new Vector3(0.0025f, 0.022f, 0.074f));
            BrandMarks.Print(m, mark, brand, new Vector3(side * 0.0586f, 0.093f, 0.005f), side > 0f ? Vector3.Forward : Vector3.Back, Vector3.Up, 0.019f);
        }

        m.Pillow(second, new Vector3(0f, 0.12f, 0.014f), new Vector3(0.08f, 0.012f, 0.1f), Basis.Identity, 3f, 4, 14);
        return new GearBuild();
    }

    private static GearBuild Bowl(ShapeMesh m, int seed, bool paint)
    {
        var shape = new ShapeMesh();
        MarkerShape.Build(shape, MarkerGroup.Loader, seed, closeUp: false, paint: paint);
        m.Absorb(shape, new Transform3D(Basis.Identity, -FieldFeed), part => (MarkerPart)part switch
        {
            MarkerPart.Shell => GearZone.Of(GearFinish.Shell, GearPaint.Main),
            MarkerPart.Paint => GearZone.Ball,
            _ => GearZone.Of(GearFinish.Gloss, GearPaint.Second),
        });
        List<Vector3> balls = MarkerShape.LoaderBalls(seed);
        for (int i = 0; i < balls.Count; i++)
        {
            balls[i] -= FieldFeed;
        }

        return new GearBuild { Balls = balls };
    }

    /// <summary>
    /// Where the paint lies in a see-through shell of <paramref name="size"/> round <paramref name="centre"/>: settled into
    /// its bottom two thirds, lowest first, as in the field's loader (<see cref="MarkerShape.LoaderBalls"/>).
    /// </summary>
    private static List<Vector3> Settle(Vector3 centre, Vector3 size, int seed)
    {
        var random = new Random(seed);
        float ball = MarkerShape.Ball;
        Vector3 half = size * 0.5f - new Vector3(ball, ball, ball) * 1.3f;
        var balls = new List<Vector3>(90);
        for (int tries = 0; tries < 1000 && balls.Count < 90; tries++)
        {
            var p = new Vector3((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 1.4f - 1f, (float)random.NextDouble() * 2f - 1f);
            if (p.X * p.X + p.Y * p.Y + p.Z * p.Z <= 1f)
            {
                balls.Add(centre + p * half);
            }
        }

        balls.Sort((a, b) => a.Y.CompareTo(b.Y));
        return balls;
    }

    /// <summary>The paintballs themselves, in the player's paint.</summary>
    public static void Balls(ShapeMesh m, IEnumerable<Vector3> balls, bool closeUp)
    {
        float ball = MarkerShape.Ball;
        foreach (Vector3 at in balls)
        {
            m.Pillow(GearZone.Ball, at, new Vector3(ball, ball, ball) * 2f, Basis.Identity, 2f, closeUp ? 6 : 3, closeUp ? 9 : 5);
        }
    }
}
