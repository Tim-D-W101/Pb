using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.World;

namespace Pb.Game.Player;

/// <summary>What each part of the marker is made of; the mesh has a surface for each.</summary>
public enum MarkerPart
{
    /// <summary>The anodised receiver, feed neck and trigger guard.</summary>
    Body,
    Barrel,
    /// <summary>The rubber of the grips.</summary>
    Rubber,
    /// <summary>Small steel parts: screws, back cap, trigger, regulator.</summary>
    Trim,
    /// <summary>The aluminium air bottle.</summary>
    Tank,
    /// <summary>The loader's smoke-tinted see-through shell.</summary>
    Shell,
    /// <summary>The loader's lid, darker and opaque.</summary>
    Lid,
    /// <summary>The paintballs in the loader, in the player's paint colour.</summary>
    Paint,
}

/// <summary>Which hitbox a part of the marker belongs to, so an opponent's gear can be fitted into its hitboxes.</summary>
public enum MarkerGroup
{
    Marker,
    Loader,
    Tank,
}

/// <summary>
/// The paintball marker built in code, after the reference picture (a generic black marker with a
/// smoke-grey loader and a silver bottle as the stock; no brand): an angular receiver, a tapered
/// barrel with a crowned muzzle, a pistol grip, foregrip and trigger guard, a loader with paint in it
/// on a feed neck, and the bottle behind on its regulator. Frame: +X right, +Y up, −Z forward (down
/// the barrel), the receiver's centre at the origin; the barrel's axis is 2 cm above it. The
/// first-person marker draws all of it; an opponent's gear fits each group into its hitbox.
/// Until a generated model replaces it, this is the marker everyone holds.
/// </summary>
public static class MarkerShape
{
    /// <summary>How high the barrel's axis sits above the receiver's centre (m).</summary>
    public const float BarrelHeight = 0.02f;

    /// <summary>Where the barrel ends, forward of the receiver's centre (m).</summary>
    public const float MuzzleForward = 0.43f;

    /// <summary>
    /// The box each group nominally fills in the marker's frame (centre and size): an opponent's gear
    /// maps it onto the group's hitbox, so the parts land where paint can hit them.
    /// </summary>
    public static (Vector3 Center, Vector3 Size) NominalBox(MarkerGroup group) => group switch
    {
        MarkerGroup.Marker => (new Vector3(0f, -0.02f, -0.15f), new Vector3(0.042f, 0.12f, 0.56f)),
        MarkerGroup.Loader => (new Vector3(0f, 0.149f, 0.01f), new Vector3(0.118f, 0.122f, 0.19f)),
        _ => (new Vector3(0f, -0.04f, 0.266f), new Vector3(0.064f, 0.064f, 0.272f)),
    };

    /// <summary>
    /// Adds the marker's parts (or one group's) to <paramref name="mesh"/>, already placed; the
    /// material ids are <see cref="MarkerPart"/> values. <paramref name="seed"/> settles the paint;
    /// <paramref name="closeUp"/> rounds the balls off for the first-person marker.
    /// </summary>
    /// <summary>The paintballs' radius (17.3 mm balls).</summary>
    public const float Ball = 0.00865f;

    /// <param name="paint">False leaves the balls out of the loader, for a caller that draws them itself (<see cref="LoaderBalls"/>).</param>
    public static void Build(ShapeMesh mesh, MarkerGroup? only = null, int seed = 1, bool closeUp = false, bool paint = true)
    {
        if (only is null or MarkerGroup.Marker)
        {
            Receiver(mesh);
            Barrel(mesh);
            Grips(mesh);
        }

        if (only is null or MarkerGroup.Loader)
        {
            Loader(mesh, seed, closeUp, paint);
        }

        if (only is null or MarkerGroup.Tank)
        {
            Bottle(mesh);
        }
    }

    private const int Body = (int)MarkerPart.Body, BarrelPart = (int)MarkerPart.Barrel, Rubber = (int)MarkerPart.Rubber,
        Trim = (int)MarkerPart.Trim, TankPart = (int)MarkerPart.Tank, Shell = (int)MarkerPart.Shell, Lid = (int)MarkerPart.Lid,
        Paint = (int)MarkerPart.Paint;

    /// <summary>Side profiles are drawn in (forward, up) and extruded across the marker.</summary>
    private static readonly Basis Side = new(Vector3.Forward, Vector3.Up, Vector3.Right);

    /// <summary>A rotation whose +Y runs forward (−Z), down the barrel, for parts turned on a lathe.</summary>
    private static readonly Basis AlongForward = new(Vector3.Right, -Mathf.Pi / 2f);

    /// <summary>A rotation whose +Y runs backward (+Z), to the stock.</summary>
    private static readonly Basis AlongBack = new(Vector3.Right, Mathf.Pi / 2f);

    private static Vector3 At(float forward, float up, float right = 0f) => new(right, up, -forward);

    private static void Receiver(ShapeMesh m)
    {
        var profile = new[]
        {
            new Vector2(-0.13f, -0.032f), new Vector2(0.115f, -0.032f), new Vector2(0.13f, -0.022f), new Vector2(0.13f, 0.03f),
            new Vector2(0.11f, 0.04f), new Vector2(-0.11f, 0.04f), new Vector2(-0.13f, 0.028f),
        };
        m.Extrude(Body, Vector3.Zero, Side, profile, 0.042f);
        // A raised strip along the top, the back cap, and screws on both sides.
        m.Box(Body, At(0.02f, 0.0425f), new Vector3(0.028f, 0.005f, 0.17f));
        m.Cylinder(Trim, At(-0.136f, 0.006f), AlongForward, 0.0135f, 0.012f, 14);
        foreach (float side in new[] { -1f, 1f })
        {
            foreach ((float f, float u) in new[] { (-0.09f, 0.02f), (0.085f, 0.02f), (-0.09f, -0.019f), (0.085f, -0.019f) })
            {
                m.Cylinder(Trim, At(f, u, side * 0.0215f), new Basis(Vector3.Back, Mathf.Pi / 2f), 0.0032f, 0.003f, 6);
            }
        }

        // The feed neck and its clamp collar, with a lever.
        m.Cylinder(Body, At(0f, 0.065f), Basis.Identity, 0.016f, 0.05f, 14);
        m.Cylinder(Body, At(0f, 0.083f), Basis.Identity, 0.0205f, 0.014f, 16);
        m.Box(Trim, At(0f, 0.083f, 0.024f), new Vector3(0.008f, 0.01f, 0.03f));
    }

    private static void Barrel(ShapeMesh m)
    {
        const float start = 0.13f;
        float length = MuzzleForward - start;
        // Walked from the back: the thread, a step down, the long taper, a muzzle lip and the
        // crown, then into the bore.
        var profile = new[]
        {
            new Vector2(0f, 0f), new Vector2(0.0165f, 0f), new Vector2(0.0165f, 0.024f), new Vector2(0.0145f, 0.027f),
            new Vector2(0.0145f, 0.045f), new Vector2(0.0127f, 0.052f), new Vector2(0.0121f, length - 0.03f),
            new Vector2(0.0134f, length - 0.027f), new Vector2(0.0134f, length - 0.002f), new Vector2(0.0122f, length),
            new Vector2(0.0091f, length), new Vector2(0.0091f, length - 0.03f), new Vector2(0f, length - 0.03f),
        };
        m.Lathe(BarrelPart, At(start, BarrelHeight), AlongForward, profile, 18);
        // A couple of rows of ports near the muzzle.
        for (int i = 0; i < 6; i++)
        {
            float f = MuzzleForward - 0.05f - (i / 2) * 0.022f;
            float a = (i % 2 == 0 ? 0.6f : -0.6f) + (i / 2) * 0.35f;
            Vector3 radial = new(Mathf.Sin(a), Mathf.Cos(a), 0f);
            m.Box(Trim, At(f, BarrelHeight) + radial * 0.0118f, new Vector3(0.006f, 0.0016f, 0.006f), new Basis(radial.Cross(Vector3.Forward), radial, Vector3.Forward));
        }
    }

    private static void Grips(ShapeMesh m)
    {
        // The frame under the receiver, the pistol grip raked back, and the foregrip under the front.
        m.Box(Body, At(-0.03f, -0.043f), new Vector3(0.03f, 0.022f, 0.11f));
        var grip = new[]
        {
            new Vector2(-0.118f, -0.152f), new Vector2(-0.076f, -0.15f), new Vector2(-0.058f, -0.112f), new Vector2(-0.052f, -0.086f),
            new Vector2(-0.046f, -0.054f), new Vector2(-0.092f, -0.054f), new Vector2(-0.102f, -0.092f), new Vector2(-0.112f, -0.13f),
        };
        m.Extrude(Rubber, Vector3.Zero, Side, grip, 0.031f);
        var foregrip = new[]
        {
            new Vector2(0.054f, -0.13f), new Vector2(0.086f, -0.128f), new Vector2(0.091f, -0.07f), new Vector2(0.096f, -0.032f),
            new Vector2(0.06f, -0.032f), new Vector2(0.055f, -0.07f),
        };
        m.Extrude(Rubber, Vector3.Zero, Side, foregrip, 0.028f);
        // Trigger guard and the trigger in it.
        m.Bar(Body, At(-0.045f, -0.054f), At(-0.04f, -0.097f), 0.009f, 0.006f);
        m.Bar(Body, At(-0.04f, -0.097f), At(0.016f, -0.097f), 0.009f, 0.006f);
        m.Bar(Body, At(0.016f, -0.097f), At(0.021f, -0.054f), 0.009f, 0.006f);
        m.Bar(Trim, At(-0.01f, -0.054f), At(-0.014f, -0.077f), 0.007f, 0.005f);
        m.Bar(Trim, At(-0.014f, -0.077f), At(-0.006f, -0.088f), 0.007f, 0.005f);
    }

    private static readonly Vector3 ShellCenter = At(-0.01f, 0.1465f), ShellSize = new(0.118f, 0.115f, 0.19f);

    private static void Loader(ShapeMesh m, int seed, bool closeUp, bool paint)
    {
        m.Pillow(Shell, ShellCenter, ShellSize, Basis.Identity, 2.3f, 10, 18);
        m.Pillow(Lid, At(-0.038f, 0.203f), new Vector3(0.078f, 0.012f, 0.1f), Basis.Identity, 3f, 4, 14);
        m.Box(Lid, At(0.024f, 0.2f), new Vector3(0.022f, 0.008f, 0.012f));
        if (!paint)
        {
            return;
        }

        foreach (Vector3 at in LoaderBalls(seed))
        {
            m.Pillow(Paint, at, new Vector3(Ball, Ball, Ball) * 2f, Basis.Identity, 2f, closeUp ? 6 : 3, closeUp ? 9 : 5);
        }
    }

    /// <summary>
    /// Where the paintballs lie in the loader (marker frame), settled into the bottom two thirds of the
    /// shell, lowest first, so drawing the first so many shows the loader that full.
    /// </summary>
    public static List<Vector3> LoaderBalls(int seed)
    {
        var random = new Random(seed);
        Vector3 half = ShellSize * 0.5f - new Vector3(Ball, Ball, Ball) * 1.3f;
        var balls = new List<Vector3>(80);
        for (int tries = 0; tries < 900 && balls.Count < 80; tries++)
        {
            var p = new Vector3((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 1.4f - 1f, (float)random.NextDouble() * 2f - 1f);
            if (p.X * p.X + p.Y * p.Y + p.Z * p.Z <= 1f)
            {
                balls.Add(ShellCenter + p * half);
            }
        }

        balls.Sort((a, b) => a.Y.CompareTo(b.Y));
        return balls;
    }

    private static void Bottle(ShapeMesh m)
    {
        // The regulator out of the back of the receiver, with a gauge, then the bottle as the stock.
        m.Cylinder(Trim, At(-0.15f, -0.04f), AlongBack, 0.0175f, 0.04f, 14);
        m.Cylinder(Trim, At(-0.15f, -0.04f, 0.019f), new Basis(Vector3.Back, Mathf.Pi / 2f), 0.009f, 0.007f, 12);
        var profile = new[]
        {
            new Vector2(0f, 0f), new Vector2(0.012f, 0f), new Vector2(0.013f, 0.006f), new Vector2(0.021f, 0.012f),
            new Vector2(0.028f, 0.021f), new Vector2(0.032f, 0.036f), new Vector2(0.032f, 0.205f), new Vector2(0.029f, 0.219f),
            new Vector2(0.02f, 0.228f), new Vector2(0f, 0.232f),
        };
        m.Lathe(TankPart, At(-0.17f, -0.04f), AlongBack, profile, 20);
    }
}
