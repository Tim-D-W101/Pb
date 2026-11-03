using System.Numerics;
using Pb.Sim.Collision;

namespace Pb.Sim.Level;

/// <summary>
/// The rolling ground (<see cref="Terrain"/>) as a collider (M5.8): paint breaks on it, sight lines meet it, buggies'
/// wheels stand on it. A sweep walks the grid squares its path crosses, nearest first, testing each square's two
/// triangles (moved out by the sphere's radius along their faces), and skips squares whose block lies wholly below the
/// path. A sphere that starts below the ground (or within its radius of it) reports t = 0 with the ground's normal there.
/// Outside the grid the ground is level at height 0. Like the ground plane it stands in for, it's everywhere, so the
/// collision world tests it for every sweep.
/// </summary>
public sealed class HeightFieldShape : Shape
{
    private readonly Terrain _terrain;

    public HeightFieldShape(Terrain terrain)
    {
        _terrain = terrain;
    }

    public Terrain Terrain => _terrain;

    public override Aabb Bounds => new(new Vector3(float.MinValue), new Vector3(float.MaxValue));

    public override bool Everywhere => true;

    public override SurfaceId SurfaceAt(Vector3 point, SurfaceId own) => _terrain.SurfaceAt(point.X, point.Z);

    public override bool Sweep(Vector3 p0, Vector3 d, float r, out float t, out Vector3 normal)
    {
        Terrain g = _terrain;
        t = 0f;
        normal = default;
        float lowest = MathF.Min(p0.Y, p0.Y + d.Y) - r;
        if (lowest > g.MaxHeight)
        {
            return false; // above everything
        }

        // Above the highest ground in the blocks under its path (most balls in flight): nothing to meet.
        Vector3 p1 = p0 + d;
        if (lowest > g.HighestUnder(MathF.Min(p0.X, p1.X) - r, MathF.Min(p0.Z, p1.Z) - r, MathF.Max(p0.X, p1.X) + r, MathF.Max(p0.Z, p1.Z) + r) + 1e-3f)
        {
            return false;
        }

        bool inside = Inside(p0.X, p0.Z);
        float h0 = inside ? g.HeightAt(p0.X, p0.Z) : 0f;
        // Within its radius of the ground (along the ground's normal; never more than twice it straight up on any slope it has).
        if (p0.Y - h0 < 2f * r + 1e-5f)
        {
            Vector3 n0 = inside ? g.NormalAt(p0.X, p0.Z) : Vector3.UnitY;
            if ((p0.Y - h0) * n0.Y < r - 1e-5f)
            {
                normal = n0;
                return true; // starting in the ground
            }
        }

        float cell = g.Cell;
        float gx = (p0.X - g.MinX) / cell, gz = (p0.Z - g.MinZ) / cell;
        float dx = d.X / cell, dz = d.Z / cell;
        int c = (int)MathF.Floor(gx), row = (int)MathF.Floor(gz);
        int stepC = dx > 0f ? 1 : -1, stepR = dz > 0f ? 1 : -1;
        float tDeltaX = MathF.Abs(dx) > 1e-9f ? 1f / MathF.Abs(dx) : float.MaxValue;
        float tDeltaZ = MathF.Abs(dz) > 1e-9f ? 1f / MathF.Abs(dz) : float.MaxValue;
        float tMaxX = MathF.Abs(dx) > 1e-9f ? (dx > 0f ? c + 1 - gx : gx - c) * tDeltaX : float.MaxValue;
        float tMaxZ = MathF.Abs(dz) > 1e-9f ? (dz > 0f ? row + 1 - gz : gz - row) * tDeltaZ : float.MaxValue;
        float enter = 0f, best = float.MaxValue;
        Vector3 bestNormal = default;
        int squares = g.Cols - 1, rows = g.Rows - 1;
        for (int guard = 0; guard < 4096; guard++)
        {
            float exit = MathF.Min(MathF.Min(tMaxX, tMaxZ), 1f);
            if (c >= 0 && row >= 0 && c < squares && row < rows)
            {
                // The path over this square, against the highest point of its block.
                float low = MathF.Min(p0.Y + d.Y * enter, p0.Y + d.Y * exit) - r;
                if (low <= g.BlockMax(c, row) + 1e-3f)
                {
                    Square(c, row, p0, d, r, ref best, ref bestNormal);
                }

                if (best <= exit + 1e-4f)
                {
                    break;
                }
            }
            else if (Leaving(c, row, stepC, stepR, squares, rows))
            {
                break;
            }

            if (exit >= 1f)
            {
                break;
            }

            if (tMaxX < tMaxZ)
            {
                c += stepC;
                enter = tMaxX;
                tMaxX += tDeltaX;
            }
            else
            {
                row += stepR;
                enter = tMaxZ;
                tMaxZ += tDeltaZ;
            }
        }

        if (best <= 1f)
        {
            t = best;
            normal = bestNormal;
            return true;
        }

        // Off the grid: level ground at 0.
        if (d.Y < 0f)
        {
            float tp = (p0.Y - r) / -d.Y;
            Vector3 at = p0 + d * tp;
            if (tp >= 0f && tp <= 1f && !Inside(at.X, at.Z))
            {
                t = tp;
                normal = Vector3.UnitY;
                return true;
            }
        }

        return false;
    }

    private bool Inside(float x, float z) =>
        x >= _terrain.MinX && z >= _terrain.MinZ && x <= _terrain.MinX + (_terrain.Cols - 1) * _terrain.Cell &&
        z <= _terrain.MinZ + (_terrain.Rows - 1) * _terrain.Cell;

    /// <summary>Whether the walk has left the grid for good (outside it and heading further out on an axis).</summary>
    private static bool Leaving(int c, int r, int stepC, int stepR, int cols, int rows) =>
        (c < 0 && stepC < 0) || (c >= cols && stepC > 0) || (r < 0 && stepR < 0) || (r >= rows && stepR > 0);

    /// <summary>The square's two triangles against the sphere's path: the earliest touch on either, if before <paramref name="best"/>.</summary>
    private void Square(int c, int r, Vector3 p0, Vector3 d, float radius, ref float best, ref Vector3 bestNormal)
    {
        Terrain g = _terrain;
        float cell = g.Cell;
        float x0 = g.MinX + c * cell, z0 = g.MinZ + r * cell;
        float h00 = g.PointHeight(c, r), h10 = g.PointHeight(c + 1, r), h01 = g.PointHeight(c, r + 1), h11 = g.PointHeight(c + 1, r + 1);
        float slack = 0.01f + 0.5f * radius / cell;
        // Triangle (0,0) (1,0) (0,1): u + v ≤ 1.
        var nA = Vector3.Normalize(new Vector3(-(h10 - h00) / cell, 1f, -(h01 - h00) / cell));
        if (Triangle(nA, new Vector3(x0, h00, z0), true, p0, d, radius, x0, z0, cell, slack, best, out float tA))
        {
            best = tA;
            bestNormal = nA;
        }

        // Triangle (1,0) (1,1) (0,1): u + v ≥ 1.
        var nB = Vector3.Normalize(new Vector3(-(h11 - h01) / cell, 1f, -(h11 - h10) / cell));
        if (Triangle(nB, new Vector3(x0 + cell, h10, z0), false, p0, d, radius, x0, z0, cell, slack, best, out float tB))
        {
            best = tB;
            bestNormal = nB;
        }
    }

    /// <summary>
    /// When the sphere's path first touches the triangle with upward normal <paramref name="n"/> through <paramref name="on"/>
    /// (the first or second of its square), if before <paramref name="best"/>.
    /// </summary>
    private static bool Triangle(Vector3 n, Vector3 on, bool first, Vector3 p0, Vector3 d, float radius, float x0, float z0, float cell,
        float slack, float best, out float t)
    {
        t = 0f;
        float toward = Vector3.Dot(n, d);
        if (toward >= 0f)
        {
            return false;
        }

        t = -(Vector3.Dot(n, p0 - on) - radius) / toward;
        if (t < 0f || t > 1f || t >= best)
        {
            return false;
        }

        Vector3 q = p0 + d * t - n * radius;
        float u = (q.X - x0) / cell, v = (q.Z - z0) / cell;
        return first
            ? u >= -slack && v >= -slack && u + v <= 1f + slack
            : u <= 1f + slack && v <= 1f + slack && u + v >= 1f - slack;
    }
}
