using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Level;

namespace Pb.Game.World;

/// <summary>
/// Frames in the buildings' windows and doors (kit/buildings "frames"): a frame round each window
/// with mullions and a transom dividing it into panes, jagged shards of glass left in some panes,
/// and a frame round each doorway. The openings stay open to paint and light, as the level kit
/// records them (<see cref="LevelLayout.Apertures"/>); the shards are only at the edges of a pane,
/// so a window still reads as open. Each opening is varied by a seed from where it is.
/// </summary>
public static class OpeningFrames
{
    /// <summary>Width of a window frame's members in the wall's plane, and their depth into it (m).</summary>
    private const float FrameWidth = 0.06f, FrameDepth = 0.06f;

    private const float MullionWidth = 0.035f, MullionDepth = 0.045f;

    private const float DoorFrameWidth = 0.075f, DoorFrameDepth = 0.1f;

    /// <summary>Panes are about this size; a window is divided into as many as fit.</summary>
    private const float PaneWidth = 0.55f, PaneHeight = 0.65f;

    /// <summary>The share of panes that keep shards of their glass.</summary>
    private const float ShardChance = 0.55f;

    /// <summary>
    /// Adds the frames to <paramref name="meshFor"/> (the mesh for a world position). <paramref name="frameMaterial"/>
    /// gives the frame material for an aperture's owner (−1 for none); <paramref name="glass"/> is the
    /// material for the shards (−1 for none). Returns how many openings got frames.
    /// </summary>
    public static int Build(LevelLayout level, Func<int, int> frameMaterial, int glass, Func<Vector3, ShapeMesh> meshFor)
    {
        int framed = 0;
        foreach (Aperture a in level.Apertures)
        {
            if (a.Kind is not (ApertureKind.Window or ApertureKind.Door))
            {
                continue;
            }

            int material = frameMaterial(a.Owner);
            if (material < 0)
            {
                continue;
            }

            Vector3 center = a.Center.ToGodot();
            var u = a.U.ToGodot();
            var v = a.V.ToGodot();
            ShapeMesh mesh = meshFor(center);
            mesh.Place(Transform3D.Identity, 3f);
            var random = new Random(LevelBuilder.StableHash(level.Id) ^ (int)MathF.Round(center.X * 10f) * 73856093
                ^ (int)MathF.Round(center.Y * 10f) * 19349663 ^ (int)MathF.Round(center.Z * 10f) * 83492791);
            if (a.Kind == ApertureKind.Window)
            {
                Window(mesh, material, glass, random, center, u, v, a.HalfWidth, a.HalfHeight);
            }
            else
            {
                Door(mesh, material, center, u, v, a.HalfWidth, a.HalfHeight);
            }

            framed++;
        }

        return framed;
    }

    private static void Window(ShapeMesh mesh, int material, int glass, Random random, Vector3 center, Vector3 u, Vector3 v, float hw, float hh)
    {
        Vector3 n = u.Cross(v).Normalized();
        var frame = new Basis(u, v, n);
        Vector3 At(float x, float y) => center + u * x + v * y;

        // The outer frame: sides full height, head and sill between them.
        mesh.Box(material, At(-hw + FrameWidth * 0.5f, 0f), new Vector3(FrameWidth, 2f * hh, FrameDepth), frame);
        mesh.Box(material, At(hw - FrameWidth * 0.5f, 0f), new Vector3(FrameWidth, 2f * hh, FrameDepth), frame);
        mesh.Box(material, At(0f, hh - FrameWidth * 0.5f), new Vector3(2f * hw - 2f * FrameWidth, FrameWidth, FrameDepth), frame);
        mesh.Box(material, At(0f, -hh + FrameWidth * 0.5f), new Vector3(2f * hw - 2f * FrameWidth, FrameWidth, FrameDepth * 1.3f), frame);

        // Mullions and transoms divide the rest into panes.
        float x0 = -hw + FrameWidth, x1 = hw - FrameWidth, y0 = -hh + FrameWidth, y1 = hh - FrameWidth;
        int across = Math.Max(1, (int)MathF.Round((x1 - x0) / PaneWidth));
        int down = Math.Max(1, (int)MathF.Round((y1 - y0) / PaneHeight));
        for (int i = 1; i < across; i++)
        {
            float x = Mathf.Lerp(x0, x1, (float)i / across);
            mesh.Box(material, At(x, 0f), new Vector3(MullionWidth, y1 - y0, MullionDepth), frame);
        }

        for (int j = 1; j < down; j++)
        {
            float y = Mathf.Lerp(y0, y1, (float)j / down);
            mesh.Box(material, At(0f, y), new Vector3(x1 - x0, MullionWidth, MullionDepth), frame);
        }

        if (glass < 0)
        {
            return;
        }

        // What's left of the glass: jagged shards along the edges of some panes.
        float half = MullionWidth * 0.5f;
        for (int i = 0; i < across; i++)
        {
            for (int j = 0; j < down; j++)
            {
                if (random.NextDouble() >= ShardChance)
                {
                    continue;
                }

                float px0 = Mathf.Lerp(x0, x1, (float)i / across) + (i > 0 ? half : 0f);
                float px1 = Mathf.Lerp(x0, x1, (float)(i + 1) / across) - (i < across - 1 ? half : 0f);
                float py0 = Mathf.Lerp(y0, y1, (float)j / down) + (j > 0 ? half : 0f);
                float py1 = Mathf.Lerp(y0, y1, (float)(j + 1) / down) - (j < down - 1 ? half : 0f);
                Shards(mesh, glass, random, center, u, v, n, px0, px1, py0, py1);
            }
        }
    }

    /// <summary>A few jagged triangles of glass, each standing on an edge of the pane (x0…x1, y0…y1) and pointing inwards.</summary>
    private static void Shards(ShapeMesh mesh, int glass, Random random, Vector3 center, Vector3 u, Vector3 v, Vector3 n,
        float x0, float x1, float y0, float y1)
    {
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
        Vector3 At(float x, float y) => center + u * x + v * y;
        int count = random.Next(2, 6);
        for (int k = 0; k < count; k++)
        {
            int edge = random.Next(4);
            // The edge as a start, a direction along it and the way inwards.
            (Vector2 start, Vector2 along, Vector2 inward, float length) = edge switch
            {
                0 => (new Vector2(x0, y0), Vector2.Right, Vector2.Up, x1 - x0),
                1 => (new Vector2(x1, y0), Vector2.Up, Vector2.Left, y1 - y0),
                2 => (new Vector2(x1, y1), Vector2.Left, Vector2.Down, x1 - x0),
                _ => (new Vector2(x0, y1), Vector2.Down, Vector2.Right, y1 - y0),
            };
            float span = MathF.Min(R(0.06f, 0.26f), length * 0.6f);
            float t = R(0f, MathF.Max(length - span, 0f));
            float depth = MathF.Min(R(0.04f, 0.22f), (edge % 2 == 0 ? y1 - y0 : x1 - x0) * 0.45f);
            Vector2 a = start + along * t, b = start + along * (t + span);
            Vector2 tip = a + along * (span * R(0.2f, 0.8f)) + inward * depth;
            // A second, shorter point on most shards makes them jagged.
            Vector2 notch = a + along * (span * R(0.55f, 0.95f)) + inward * (depth * R(0.2f, 0.6f));
            Vector3 pa = At(a.X, a.Y), pb = At(b.X, b.Y), pt = At(tip.X, tip.Y), pn = At(notch.X, notch.Y);
            // Glass is drawn from both sides, so the winding doesn't matter.
            mesh.Tri(glass, pa, pt, pn, n, a, tip, notch);
            mesh.Tri(glass, pa, pn, pb, n, a, notch, b);
        }
    }

    private static void Door(ShapeMesh mesh, int material, Vector3 center, Vector3 u, Vector3 v, float hw, float hh)
    {
        Vector3 n = u.Cross(v).Normalized();
        var frame = new Basis(u, v, n);
        Vector3 At(float x, float y) => center + u * x + v * y;
        mesh.Box(material, At(-hw + DoorFrameWidth * 0.5f, 0f), new Vector3(DoorFrameWidth, 2f * hh, DoorFrameDepth), frame);
        mesh.Box(material, At(hw - DoorFrameWidth * 0.5f, 0f), new Vector3(DoorFrameWidth, 2f * hh, DoorFrameDepth), frame);
        mesh.Box(material, At(0f, hh - DoorFrameWidth * 0.5f), new Vector3(2f * hw - 2f * DoorFrameWidth, DoorFrameWidth, DoorFrameDepth), frame);
    }
}
