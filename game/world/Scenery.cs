using System;
using System.Collections.Generic;
using Godot;
using Pb.Sim.Data;
using Pb.Sim.Level;

namespace Pb.Game.World;

/// <summary>
/// Things out beyond the level (level "scenery"), for the view only: power lines on steel lattice
/// pylons (legs tapering to a waist, X-braced faces, three crossarms a side with insulator strings,
/// an earth-wire peak, conductors sagging between them) and lines of wooden telegraph poles with a
/// crossarm and two wires. Each line runs along its plan points, a tower or pole every spacing;
/// they're varied by a seed from the level.
/// </summary>
public static class Scenery
{
    /// <summary>Conductors sag by this share of their span; telegraph wires by this.</summary>
    private const float PowerSag = 0.035f, PoleSag = 0.02f;

    /// <summary>
    /// Adds the level's scenery to <paramref name="meshFor"/> (the mesh for a world position);
    /// <paramref name="material"/> resolves a kit material id. Returns how many towers and poles were built.
    /// </summary>
    public static int Build(LevelLayout level, Func<string, int> material, Func<Vector3, ShapeMesh> meshFor)
    {
        if (level.Scenery is not { } scenery)
        {
            return 0;
        }

        var random = new Random(LevelBuilder.StableHash(level.Id) ^ 0x5CE7E);
        int built = 0;
        foreach (LineDef line in scenery.PowerLines ?? Array.Empty<LineDef>())
        {
            built += Line(line, material, meshFor, random, pylons: true);
        }

        foreach (LineDef line in scenery.PoleLines ?? Array.Empty<LineDef>())
        {
            built += Line(line, material, meshFor, random, pylons: false);
        }

        return built;
    }

    /// <summary>Towers or poles along the line, evenly spaced on each leg of it, and the wires between them.</summary>
    private static int Line(LineDef line, Func<string, int> material, Func<Vector3, ShapeMesh> meshFor, Random random, bool pylons)
    {
        int body = material(line.Material), wire = material(line.Wire);
        if (body < 0 || wire < 0)
        {
            return 0;
        }

        // Stations along the polyline: every point, and evenly between them at about the spacing.
        var stations = new List<(Vector3 At, Vector3 Along)>();
        for (int i = 0; i + 1 < line.Points_m.Length; i++)
        {
            var a = new Vector3(line.Points_m[i][0], 0f, line.Points_m[i][1]);
            var b = new Vector3(line.Points_m[i + 1][0], 0f, line.Points_m[i + 1][1]);
            int spans = Math.Max(1, (int)MathF.Round(a.DistanceTo(b) / line.Spacing_m));
            Vector3 along = (b - a).Normalized();
            for (int k = i == 0 ? 0 : 1; k <= spans; k++)
            {
                stations.Add((a.Lerp(b, (float)k / spans), along));
            }
        }

        var hooks = new List<Vector3[]>();
        foreach ((Vector3 at, Vector3 along) in stations)
        {
            ShapeMesh mesh = meshFor(at);
            mesh.Place(Transform3D.Identity, line.Height_m);
            // Across the line: the crossarms' way. Poles lean a little, each its own way.
            Vector3 across = Vector3.Up.Cross(along).Normalized();
            hooks.Add(pylons ? Pylon(mesh, body, wire, at, along, across, line.Height_m)
                : Pole(mesh, body, wire, at, along, across, line.Height_m, random));
        }

        // Wires between neighbours, hook to hook.
        float sag = pylons ? PowerSag : PoleSag;
        float radius = pylons ? 0.07f : 0.012f;
        for (int i = 0; i + 1 < hooks.Count; i++)
        {
            ShapeMesh mesh = meshFor((stations[i].At + stations[i + 1].At) * 0.5f);
            mesh.Place(Transform3D.Identity, line.Height_m);
            for (int h = 0; h < hooks[i].Length; h++)
            {
                Vector3 p = hooks[i][h], q = hooks[i + 1][h];
                Catenary(mesh, wire, p, q, p.DistanceTo(q) * sag, radius, pylons ? 16 : 8);
            }
        }

        return stations.Count;
    }

    /// <summary>
    /// A steel lattice pylon on its four legs, standing at <paramref name="at"/>, its arms across the
    /// line; returns where its wires hang (the six conductors' insulator ends, then the earth wire).
    /// </summary>
    private static Vector3[] Pylon(ShapeMesh mesh, int steel, int wire, Vector3 at, Vector3 along, Vector3 across, float height)
    {
        float waist = height * 0.66f;
        float baseHalf = height * 0.11f, waistHalf = height * 0.03f, topHalf = height * 0.022f;
        float Half(float y) => y <= waist ? Mathf.Lerp(baseHalf, waistHalf, y / waist) : Mathf.Lerp(waistHalf, topHalf, (y - waist) / (height - waist));
        Vector3 Corner(int c, float y)
        {
            float h = Half(y);
            float sa = c is 0 or 3 ? -1f : 1f, sb = c is 0 or 1 ? -1f : 1f;
            return at + across * (sa * h) + along * (sb * h) + Vector3.Up * y;
        }

        // Levels for the bracing: shorter panels as the tower narrows.
        var levels = new List<float> { 0f };
        for (float y = 0f, panel = height * 0.13f; y + panel < waist - 0.5f; panel *= 0.8f)
        {
            y += panel;
            levels.Add(y);
        }

        levels.Add(waist);
        float arm2 = waist + (height - waist) * 0.42f, arm3 = waist + (height - waist) * 0.84f;
        levels.Add(arm2);
        levels.Add(arm3);
        levels.Add(height);

        float leg = height * 0.0065f, brace = height * 0.0028f;
        for (int c = 0; c < 4; c++)
        {
            for (int l = 0; l + 1 < levels.Count; l++)
            {
                mesh.Bar(steel, Corner(c, levels[l]), Corner(c, levels[l + 1]), leg, leg);
            }
        }

        // Each face: a ring at every level and an X between levels.
        for (int c = 0; c < 4; c++)
        {
            int d = (c + 1) % 4;
            for (int l = 1; l < levels.Count; l++)
            {
                mesh.Bar(steel, Corner(c, levels[l]), Corner(d, levels[l]), brace, brace);
                mesh.Bar(steel, Corner(c, levels[l - 1]), Corner(d, levels[l]), brace, brace);
                mesh.Bar(steel, Corner(d, levels[l - 1]), Corner(c, levels[l]), brace, brace);
            }
        }

        // Three crossarms a side (the middle one longest), each a triangle of members, an insulator
        // string hanging from its tip.
        var hooks = new List<Vector3>();
        foreach ((float y, float reach) in new[] { (waist, height * 0.17f), (arm2, height * 0.2f), (arm3, height * 0.17f) })
        {
            foreach (float side in new[] { -1f, 1f })
            {
                float h = Half(y);
                Vector3 tip = at + across * (side * (h + reach)) + Vector3.Up * y;
                Vector3 nearA = at + across * (side * h) + along * h + Vector3.Up * y;
                Vector3 nearB = at + across * (side * h) - along * h + Vector3.Up * y;
                Vector3 high = at + across * (side * Half(y + 1.2f)) + Vector3.Up * (y + 1.2f);
                mesh.Bar(steel, nearA, tip, brace * 1.4f, brace * 1.4f);
                mesh.Bar(steel, nearB, tip, brace * 1.4f, brace * 1.4f);
                mesh.Bar(steel, high + along * Half(y + 1.2f), tip, brace, brace);
                mesh.Bar(steel, high - along * Half(y + 1.2f), tip, brace, brace);
                float drop = height * 0.075f;
                for (int disc = 0; disc < 7; disc++)
                {
                    float f = (disc + 0.5f) / 7f;
                    mesh.Cylinder(wire, tip + Vector3.Down * (drop * f), Basis.Identity, height * 0.0045f, drop * 0.06f, 8);
                }

                mesh.Rod(wire, tip, tip + Vector3.Down * drop, height * 0.0012f, 4, caps: false);
                hooks.Add(tip + Vector3.Down * drop);
            }
        }

        // The earth wire's peak.
        Vector3 peak = at + Vector3.Up * (height + height * 0.08f);
        for (int c = 0; c < 4; c++)
        {
            mesh.Bar(steel, Corner(c, height), peak, brace, brace);
        }

        hooks.Add(peak);
        return hooks.ToArray();
    }

    /// <summary>
    /// A wooden telegraph pole, leaning a little, with a crossarm near its top and two insulators;
    /// returns where its two wires hang.
    /// </summary>
    private static Vector3[] Pole(ShapeMesh mesh, int wood, int wire, Vector3 at, Vector3 along, Vector3 across, float height, Random random)
    {
        Vector3 lean = (along * (float)(random.NextDouble() - 0.5) + across * (float)(random.NextDouble() - 0.5)) * 0.07f;
        Vector3 up = (Vector3.Up + lean).Normalized();
        Vector3 top = at + up * height;
        mesh.Lathe(wood, at + up * (height * 0.5f), ShapeMesh.BasisAlong(up),
            new[] { new Vector2(0f, -height * 0.5f), new Vector2(0.15f, -height * 0.5f), new Vector2(0.15f, -height * 0.5f),
                    new Vector2(0.11f, height * 0.5f), new Vector2(0.11f, height * 0.5f), new Vector2(0f, height * 0.5f) }, 8);
        Vector3 arm = top - up * 0.45f;
        mesh.Bar(wood, arm - across * 0.85f, arm + across * 0.85f, 0.09f, 0.11f);
        var hooks = new Vector3[2];
        for (int i = 0; i < 2; i++)
        {
            Vector3 pin = arm + across * (i == 0 ? -0.65f : 0.65f) + up * 0.06f;
            mesh.Cylinder(wire, pin + up * 0.06f, ShapeMesh.BasisAlong(up), 0.035f, 0.12f, 8);
            hooks[i] = pin + up * 0.12f;
        }

        return hooks;
    }

    /// <summary>A wire from p to q hanging in a curve, <paramref name="sag"/> below the straight line at its middle.</summary>
    private static void Catenary(ShapeMesh mesh, int wire, Vector3 p, Vector3 q, float sag, float radius, int pieces)
    {
        Vector3 previous = p;
        for (int i = 1; i <= pieces; i++)
        {
            float f = (float)i / pieces;
            Vector3 next = p.Lerp(q, f) + Vector3.Down * (sag * 4f * f * (1f - f));
            mesh.Rod(wire, previous, next, radius, 4, caps: false);
            previous = next;
        }
    }
}
