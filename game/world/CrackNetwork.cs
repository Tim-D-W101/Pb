using System;
using System.Collections.Generic;
using Godot;

namespace Pb.Game.World;

/// <summary>
/// The network of cracks in a level's asphalt and concrete: the edges between the cells of a jittered
/// grid (a Voronoi diagram) about <c>spacing</c> across, seeded by the level. The weeds grow along it
/// (<see cref="WeedField"/>) and <see cref="Cracks"/> draws it, so the tufts come up out of the cracks.
/// </summary>
public sealed class CrackNetwork
{
    /// <summary>The tag of a cell polygon's edge that is still part of the box it was cut from.</summary>
    private const int Box = -1;

    private readonly float _spacing;
    private readonly uint _seed;

    public CrackNetwork(float spacing, uint seed)
    {
        _spacing = spacing;
        _seed = seed;
    }

    /// <summary>Distance in metres from (x, z) to the nearest crack: the bisector of the two nearest cells' sites.</summary>
    public float Distance(float x, float z)
    {
        float s = _spacing;
        float px = x / s, pz = z / s;
        int ix = (int)MathF.Floor(px), iz = (int)MathF.Floor(pz);
        var p = new Vector2(px, pz);
        Vector2 f1 = default, f2 = default;
        float d1 = float.MaxValue, d2 = float.MaxValue;
        for (int j = -1; j <= 1; j++)
        {
            for (int i = -1; i <= 1; i++)
            {
                int cx = ix + i, cz = iz + j;
                var f = new Vector2(cx + Hash01(cx, cz, _seed + 7), cz + Hash01(cx, cz, _seed + 11));
                float d = (f - p).LengthSquared();
                if (d < d1)
                {
                    (d2, f2) = (d1, f1);
                    (d1, f1) = (d, f);
                }
                else if (d < d2)
                {
                    (d2, f2) = (d, f);
                }
            }
        }

        return MathF.Abs((p - (f1 + f2) * 0.5f).Dot((f2 - f1).Normalized())) * s;
    }

    /// <summary>
    /// Every crack (the edge between two neighbouring cells, end to end in metres, X and Z) of the cells
    /// over <paramref name="area"/> and a cell beyond, each once. A cell's edges are cut from a box round
    /// its site by the bisectors with its neighbours two cells round, which is as far as a jittered grid's
    /// neighbours can reach.
    /// </summary>
    public List<(Vector2 A, Vector2 B)> Edges(Rect2 area)
    {
        var edges = new List<(Vector2 A, Vector2 B)>();
        int x0 = (int)MathF.Floor(area.Position.X / _spacing) - 1, x1 = (int)MathF.Floor(area.End.X / _spacing) + 1;
        int z0 = (int)MathF.Floor(area.Position.Y / _spacing) - 1, z1 = (int)MathF.Floor(area.End.Y / _spacing) + 1;
        var polygon = new List<(Vector2 At, int Tag)>();
        var cut = new List<(Vector2 At, int Tag)>();
        for (int cz = z0; cz <= z1; cz++)
        {
            for (int cx = x0; cx <= x1; cx++)
            {
                Vector2 site = Site(cx, cz);
                float r = _spacing * 3f;
                polygon.Clear();
                polygon.Add((site + new Vector2(-r, -r), Box));
                polygon.Add((site + new Vector2(r, -r), Box));
                polygon.Add((site + new Vector2(r, r), Box));
                polygon.Add((site + new Vector2(-r, r), Box));
                for (int dz = -2; dz <= 2; dz++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        if (dx == 0 && dz == 0)
                        {
                            continue;
                        }

                        Vector2 other = Site(cx + dx, cz + dz);
                        Clip(polygon, cut, other - site, (site + other) * 0.5f, (dx + 2) + (dz + 2) * 5);
                        (polygon, cut) = (cut, polygon);
                    }
                }

                // Each edge once: kept by the cell whose neighbour across it comes later in the scan.
                for (int i = 0; i < polygon.Count; i++)
                {
                    int tag = polygon[i].Tag;
                    if (tag == Box)
                    {
                        continue;
                    }

                    int dx = tag % 5 - 2, dz = tag / 5 - 2;
                    if (dz > 0 || (dz == 0 && dx > 0))
                    {
                        edges.Add((polygon[i].At, polygon[(i + 1) % polygon.Count].At));
                    }
                }
            }
        }

        return edges;
    }

    /// <summary>A cell's site, in metres.</summary>
    private Vector2 Site(int cx, int cz) => new Vector2(cx + Hash01(cx, cz, _seed + 7), cz + Hash01(cx, cz, _seed + 11)) * _spacing;

    /// <summary>
    /// Cuts a convex polygon to the side of a line its <paramref name="normal"/> points away from. Each
    /// vertex carries the tag of the edge leaving it; the new edge along the line takes <paramref name="tag"/>.
    /// </summary>
    private static void Clip(List<(Vector2 At, int Tag)> polygon, List<(Vector2 At, int Tag)> result, Vector2 normal, Vector2 through, int tag)
    {
        result.Clear();
        int n = polygon.Count;
        for (int i = 0; i < n; i++)
        {
            (Vector2 p, int edge) = polygon[i];
            Vector2 q = polygon[(i + 1) % n].At;
            float dp = (p - through).Dot(normal), dq = (q - through).Dot(normal);
            if (dp <= 0f)
            {
                result.Add((p, edge));
                if (dq > 0f)
                {
                    result.Add((p + (q - p) * (dp / (dp - dq)), tag));
                }
            }
            else if (dq <= 0f)
            {
                result.Add((p + (q - p) * (dp / (dp - dq)), edge));
            }
        }
    }

    internal static float Hash01(int x, int y, uint seed)
    {
        unchecked
        {
            uint h = ((uint)x * 0x8da6b343u) ^ ((uint)y * 0xd8163841u) ^ (seed * 0xcb1ab31fu);
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return (h >> 8) * (1f / 16777216f);
        }
    }
}
