using System.Numerics;

namespace Pb.Sim.Collision;

public struct SweepHit
{
    /// <summary>Fraction of the swept segment at first contact.</summary>
    public float T;
    /// <summary>Sphere centre at contact (one radius off the surface).</summary>
    public Vector3 Point;
    public Vector3 Normal;
    public SurfaceId Surface;
    public int ColliderId;
}

public interface ICollisionWorld
{
    bool SweepSphere(Vector3 from, Vector3 to, float radius, out SweepHit hit);
}

public sealed class Collider
{
    internal Collider(int id, Shape shape, SurfaceId surface, string name, bool dynamic)
    {
        Id = id;
        Shape = shape;
        Surface = surface;
        Name = name;
        Dynamic = dynamic;
    }

    public int Id { get; }

    public Shape Shape { get; }

    public SurfaceId Surface { get; }

    public string Name { get; }

    /// <summary>Moves during the round (a door leaf): filed under the bounds of everywhere it can be.</summary>
    public bool Dynamic { get; }
}

/// <summary>
/// Static world geometry for ball collision. Infinite planes are always tested; everything else
/// sits in a uniform XZ grid (compressed-row storage) so a ball's per-tick segment only looks at
/// the handful of colliders near it. Queries never allocate.
/// </summary>
public sealed class CollisionWorld : ICollisionWorld
{
    private readonly List<Collider> _colliders = new();
    private readonly List<int> _planes = new();
    private readonly List<int> _gridded = new();
    private readonly float _cellSize;

    private Aabb[] _bounds = Array.Empty<Aabb>();
    private int[] _cellStart = Array.Empty<int>();
    private int[] _cellItems = Array.Empty<int>();
    private int[] _stamp = Array.Empty<int>();
    private int _query;
    private Vector2 _origin;
    private int _cellsX;
    private int _cellsZ;
    private bool _dirty = true;

    public CollisionWorld(float cellSize = 2f)
    {
        _cellSize = cellSize;
    }

    public IReadOnlyList<Collider> Colliders => _colliders;

    /// <summary>
    /// Queries ignore dynamic colliders (doors) while set: for things built once from the level as it stands, which
    /// mustn't stick to a door that will move (old paint, weeds, cover, light through doorways).
    /// </summary>
    public bool SkipDynamic { get; set; }

    public Collider Add(Shape shape, SurfaceId surface, string name, bool dynamic = false)
    {
        var collider = new Collider(_colliders.Count, shape, surface, name, dynamic);
        _colliders.Add(collider);
        (shape is PlaneShape ? _planes : _gridded).Add(collider.Id);
        _dirty = true;
        return collider;
    }

    public void Clear()
    {
        _colliders.Clear();
        _planes.Clear();
        _gridded.Clear();
        _dirty = true;
    }

    /// <summary>Rebuilds the broadphase. Called automatically by the first query after changes.</summary>
    public void Build()
    {
        _bounds = new Aabb[_colliders.Count];
        _stamp = new int[_colliders.Count];
        _query = 0;
        _dirty = false;

        if (_gridded.Count == 0)
        {
            _cellsX = _cellsZ = 0;
            _cellStart = new int[1];
            _cellItems = Array.Empty<int>();
            return;
        }

        Aabb all = _colliders[_gridded[0]].Shape.Bounds;
        foreach (int id in _gridded)
        {
            _bounds[id] = _colliders[id].Shape.Bounds;
            all = all.Union(_bounds[id]);
        }

        _origin = new Vector2(all.Min.X, all.Min.Z);
        _cellsX = Math.Max(1, (int)MathF.Ceiling((all.Max.X - all.Min.X) / _cellSize) + 1);
        _cellsZ = Math.Max(1, (int)MathF.Ceiling((all.Max.Z - all.Min.Z) / _cellSize) + 1);

        var counts = new int[_cellsX * _cellsZ];
        foreach (int id in _gridded)
        {
            CellRange(_bounds[id], out int x0, out int z0, out int x1, out int z1);
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    counts[z * _cellsX + x]++;
                }
            }
        }

        _cellStart = new int[counts.Length + 1];
        for (int i = 0; i < counts.Length; i++)
        {
            _cellStart[i + 1] = _cellStart[i] + counts[i];
        }

        _cellItems = new int[_cellStart[^1]];
        var fill = (int[])_cellStart.Clone();
        foreach (int id in _gridded)
        {
            CellRange(_bounds[id], out int x0, out int z0, out int x1, out int z1);
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    _cellItems[fill[z * _cellsX + x]++] = id;
                }
            }
        }
    }

    public bool SweepSphere(Vector3 from, Vector3 to, float radius, out SweepHit hit) =>
        SweepSphere(from, to, radius, out hit, !SkipDynamic);

    /// <summary>The first hit along the sweep; <paramref name="includeDynamic"/> false leaves out doors.</summary>
    public bool SweepSphere(Vector3 from, Vector3 to, float radius, out SweepHit hit, bool includeDynamic)
    {
        if (_dirty)
        {
            Build();
        }

        hit = default;
        float best = float.MaxValue;
        Vector3 d = to - from;

        foreach (int id in _planes)
        {
            Collider c = _colliders[id];
            if (c.Shape.Sweep(from, d, radius, out float t, out Vector3 n) && t < best)
            {
                best = t;
                hit.Normal = n;
                hit.Surface = c.Surface;
                hit.ColliderId = id;
            }
        }

        if (_cellsX > 0)
        {
            // Long segments (aim and sight lines) are walked in chunks of two cells, so only the cells
            // along the line are visited and the walk stops at the first chunk that ends past a hit.
            // A contact at parameter T always lies in the chunk containing T, and every collider near
            // that chunk is tested against the whole segment, so the result equals one big query.
            float lengthXz = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
            int chunks = lengthXz > 4f * _cellSize ? (int)MathF.Ceiling(lengthXz / (2f * _cellSize)) : 1;
            int query = NextQuery();
            for (int c = 0; c < chunks; c++)
            {
                float ta = c / (float)chunks;
                float tb = (c + 1) / (float)chunks;
                TestChunk(from, d, radius, ta, tb, query, includeDynamic, ref best, ref hit);
                if (best <= tb)
                {
                    break;
                }
            }
        }

        if (best == float.MaxValue)
        {
            return false;
        }

        hit.T = best;
        hit.Point = from + d * best;
        return true;
    }

    /// <summary>Tests every collider with no broadphase. Reference implementation for tests.</summary>
    internal bool SweepSphereBruteForce(Vector3 from, Vector3 to, float radius, out SweepHit hit)
    {
        hit = default;
        float best = float.MaxValue;
        Vector3 d = to - from;
        foreach (Collider c in _colliders)
        {
            if (c.Dynamic && SkipDynamic)
            {
                continue;
            }

            if (c.Shape.Sweep(from, d, radius, out float t, out Vector3 n) && t < best)
            {
                best = t;
                hit.Normal = n;
                hit.Surface = c.Surface;
                hit.ColliderId = c.Id;
            }
        }

        if (best == float.MaxValue)
        {
            return false;
        }

        hit.T = best;
        hit.Point = from + d * best;
        return true;
    }

    private void TestChunk(Vector3 from, Vector3 d, float radius, float ta, float tb, int query, bool includeDynamic, ref float best, ref SweepHit hit)
    {
        // Twice the radius: boxes and tilted cylinders inflated per-axis can reach up to √3·r past
        // their tight bounds at the corners.
        Aabb chunk = Aabb.FromSegment(from + d * ta, from + d * tb, 2f * radius + 1e-4f);
        if (!TryCellRange(chunk, out int x0, out int z0, out int x1, out int z1))
        {
            return;
        }

        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                int cell = z * _cellsX + x;
                for (int k = _cellStart[cell]; k < _cellStart[cell + 1]; k++)
                {
                    int id = _cellItems[k];
                    if (_stamp[id] == query)
                    {
                        continue;
                    }

                    if (!_bounds[id].Overlaps(chunk))
                    {
                        continue; // may still overlap a later chunk, so leave it unstamped
                    }

                    _stamp[id] = query;
                    Collider c = _colliders[id];
                    if (!includeDynamic && c.Dynamic)
                    {
                        continue;
                    }

                    if (c.Shape.Sweep(from, d, radius, out float t, out Vector3 n) && t < best)
                    {
                        best = t;
                        hit.Normal = n;
                        hit.Surface = c.Surface;
                        hit.ColliderId = id;
                    }
                }
            }
        }
    }

    private int NextQuery()
    {
        if (++_query == int.MaxValue)
        {
            Array.Clear(_stamp);
            _query = 1;
        }

        return _query;
    }

    private void CellRange(in Aabb box, out int x0, out int z0, out int x1, out int z1)
    {
        x0 = Math.Clamp((int)MathF.Floor((box.Min.X - _origin.X) / _cellSize), 0, _cellsX - 1);
        z0 = Math.Clamp((int)MathF.Floor((box.Min.Z - _origin.Y) / _cellSize), 0, _cellsZ - 1);
        x1 = Math.Clamp((int)MathF.Floor((box.Max.X - _origin.X) / _cellSize), 0, _cellsX - 1);
        z1 = Math.Clamp((int)MathF.Floor((box.Max.Z - _origin.Y) / _cellSize), 0, _cellsZ - 1);
    }

    private bool TryCellRange(in Aabb box, out int x0, out int z0, out int x1, out int z1)
    {
        float maxX = _origin.X + _cellsX * _cellSize;
        float maxZ = _origin.Y + _cellsZ * _cellSize;
        if (box.Max.X < _origin.X || box.Max.Z < _origin.Y || box.Min.X > maxX || box.Min.Z > maxZ ||
            float.IsNaN(box.Min.X) || float.IsNaN(box.Min.Z))
        {
            x0 = z0 = x1 = z1 = 0;
            return false;
        }

        CellRange(box, out x0, out z0, out x1, out z1);
        return true;
    }
}
