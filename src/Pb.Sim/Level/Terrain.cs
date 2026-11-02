using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Data;

namespace Pb.Sim.Level;

/// <summary>
/// A level's rolling ground (M5.8): heights on a square grid over the level, made at load from its
/// <see cref="TerrainDef"/> the same on every copy, and the surface under each point (the ground's own, or a stream bed's).
/// Each grid square is two triangles split from its (+x, −z) corner to its (−x, +z) corner, so the ground between the
/// grid points is flat in each, and everything that meets it (paint, sight, wheels, the bots' grid, walking, what's
/// drawn) meets the same ground. Outside the grid it's level with the grid's edge.
/// </summary>
public sealed class Terrain
{
    private readonly float[] _heights;
    private readonly byte[] _kind;
    private readonly SurfaceId[] _surfaces;
    private readonly int[] _materials;
    private readonly int _firstBed;
    private readonly float[] _blockMax;
    private readonly int _blockCols;

    /// <summary>Grid squares per side of a block whose highest point is kept, to turn away sweeps passing over it.</summary>
    public const int Block = 8;

    private Terrain(float minX, float minZ, float cell, int cols, int rows, float[] heights, byte[] kind, SurfaceId[] surfaces, int[] materials,
        int firstBed, IReadOnlyList<StreamBed> streams)
    {
        Streams = streams;
        MinX = minX;
        MinZ = minZ;
        Cell = cell;
        Cols = cols;
        Rows = rows;
        _heights = heights;
        _kind = kind;
        _surfaces = surfaces;
        _materials = materials;
        _firstBed = firstBed;
        MinHeight = heights.Min();
        MaxHeight = heights.Max();
        _blockCols = (cols - 1 + Block - 1) / Block;
        int blockRows = (rows - 1 + Block - 1) / Block;
        _blockMax = new float[_blockCols * blockRows];
        Array.Fill(_blockMax, float.MinValue);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                // A point bounds the squares on both sides of it.
                for (int dr = -1; dr <= 0; dr++)
                {
                    for (int dc = -1; dc <= 0; dc++)
                    {
                        int sc = c + dc, sr = r + dr;
                        if (sc >= 0 && sr >= 0 && sc < cols - 1 && sr < rows - 1)
                        {
                            int b = sr / Block * _blockCols + sc / Block;
                            _blockMax[b] = MathF.Max(_blockMax[b], heights[r * cols + c]);
                        }
                    }
                }
            }
        }
    }

    /// <summary>A stream's bed: its middle line on the plan (x, z), and how wide and deep it's cut.</summary>
    public sealed record StreamBed(string Name, Vector2[] Line, float Width, float Depth);

    /// <summary>The stream beds cut into the ground (for what's drawn in them: the water).</summary>
    public IReadOnlyList<StreamBed> Streams { get; }

    /// <summary>The grid's first point (its least x and z), its spacing, and how many points across (x) and down (z).</summary>
    public float MinX { get; }

    public float MinZ { get; }

    public float Cell { get; }

    public int Cols { get; }

    public int Rows { get; }

    public float MinHeight { get; }

    public float MaxHeight { get; }

    /// <summary>
    /// What covers grid point (<paramref name="c"/>, <paramref name="r"/>): 0 the ground itself, then the level's ground
    /// patches in order (leaf litter, a yard), then its stream beds.
    /// </summary>
    public int KindOf(int c, int r) => _kind[r * Cols + c];

    /// <summary>How many kinds of cover there are, and each one's material (an index into the level's materials) and surface.</summary>
    public int Kinds => _surfaces.Length;

    public int MaterialOf(int kind) => _materials[kind];

    public SurfaceId SurfaceOfKind(int kind) => _surfaces[kind];

    /// <summary>The surface of the grid point (<paramref name="c"/>, <paramref name="r"/>).</summary>
    public SurfaceId SurfaceOf(int c, int r) => _surfaces[_kind[r * Cols + c]];

    /// <summary>Whether the grid point (<paramref name="c"/>, <paramref name="r"/>) is in a stream bed.</summary>
    public bool InBed(int c, int r) => _kind[r * Cols + c] >= _firstBed;

    /// <summary>The height of grid point (<paramref name="c"/>, <paramref name="r"/>) (clamped to the grid).</summary>
    public float PointHeight(int c, int r) => _heights[Math.Clamp(r, 0, Rows - 1) * Cols + Math.Clamp(c, 0, Cols - 1)];

    /// <summary>The ground's height at (<paramref name="x"/>, <paramref name="z"/>), on the grid's triangles.</summary>
    public float HeightAt(float x, float z)
    {
        float gx = (x - MinX) / Cell, gz = (z - MinZ) / Cell;
        int c = Math.Clamp((int)MathF.Floor(gx), 0, Cols - 2), r = Math.Clamp((int)MathF.Floor(gz), 0, Rows - 2);
        float u = Math.Clamp(gx - c, 0f, 1f), v = Math.Clamp(gz - r, 0f, 1f);
        float h00 = _heights[r * Cols + c], h10 = _heights[r * Cols + c + 1];
        float h01 = _heights[(r + 1) * Cols + c], h11 = _heights[(r + 1) * Cols + c + 1];
        return u + v <= 1f ? h00 + (h10 - h00) * u + (h01 - h00) * v : h11 + (h01 - h11) * (1f - u) + (h10 - h11) * (1f - v);
    }

    /// <summary>The ground's upward normal at (<paramref name="x"/>, <paramref name="z"/>): its triangle's.</summary>
    public Vector3 NormalAt(float x, float z)
    {
        float gx = (x - MinX) / Cell, gz = (z - MinZ) / Cell;
        int c = Math.Clamp((int)MathF.Floor(gx), 0, Cols - 2), r = Math.Clamp((int)MathF.Floor(gz), 0, Rows - 2);
        float u = Math.Clamp(gx - c, 0f, 1f), v = Math.Clamp(gz - r, 0f, 1f);
        float h00 = _heights[r * Cols + c], h10 = _heights[r * Cols + c + 1];
        float h01 = _heights[(r + 1) * Cols + c], h11 = _heights[(r + 1) * Cols + c + 1];
        // The slope along x and z on the triangle under the point.
        float sx = u + v <= 1f ? (h10 - h00) / Cell : (h11 - h01) / Cell;
        float sz = u + v <= 1f ? (h01 - h00) / Cell : (h11 - h10) / Cell;
        return Vector3.Normalize(new Vector3(-sx, 1f, -sz));
    }

    /// <summary>The surface under (<paramref name="x"/>, <paramref name="z"/>): its nearest grid point's.</summary>
    public SurfaceId SurfaceAt(float x, float z)
    {
        int c = Math.Clamp((int)MathF.Round((x - MinX) / Cell), 0, Cols - 1), r = Math.Clamp((int)MathF.Round((z - MinZ) / Cell), 0, Rows - 1);
        return _surfaces[_kind[r * Cols + c]];
    }

    /// <summary>A point stood on the ground: its height above the level datum (y = 0) made height above the ground there.</summary>
    public Vector3 Lift(Vector3 p) => p with { Y = p.Y + HeightAt(p.X, p.Z) };

    /// <summary>The lowest the ground comes within <paramref name="radius"/> of (<paramref name="x"/>, <paramref name="z"/>) (its middle and eight points round it).</summary>
    public float LowestUnder(float x, float z, float radius)
    {
        float lowest = HeightAt(x, z);
        for (int k = 0; k < 8; k++)
        {
            float a = k * MathF.PI * 0.25f;
            lowest = MathF.Min(lowest, HeightAt(x + MathF.Cos(a) * radius, z + MathF.Sin(a) * radius));
        }

        return lowest;
    }

    /// <summary>The least and most height of the ground over a rectangle.</summary>
    public (float Min, float Max) RangeIn(float x0, float z0, float x1, float z1)
    {
        int c0 = Math.Clamp((int)MathF.Floor((MathF.Min(x0, x1) - MinX) / Cell), 0, Cols - 1);
        int c1 = Math.Clamp((int)MathF.Ceiling((MathF.Max(x0, x1) - MinX) / Cell), 0, Cols - 1);
        int r0 = Math.Clamp((int)MathF.Floor((MathF.Min(z0, z1) - MinZ) / Cell), 0, Rows - 1);
        int r1 = Math.Clamp((int)MathF.Ceiling((MathF.Max(z0, z1) - MinZ) / Cell), 0, Rows - 1);
        float min = float.MaxValue, max = float.MinValue;
        for (int r = r0; r <= r1; r++)
        {
            for (int c = c0; c <= c1; c++)
            {
                float h = _heights[r * Cols + c];
                min = MathF.Min(min, h);
                max = MathF.Max(max, h);
            }
        }

        return (min, max);
    }

    /// <summary>The highest point of the block holding grid square (<paramref name="c"/>, <paramref name="r"/>).</summary>
    internal float BlockMax(int c, int r) => _blockMax[r / Block * _blockCols + c / Block];

    /// <summary>
    /// The highest the ground goes in the blocks under a rectangle on the plan (the level ground off the grid counts as 0):
    /// a path that stays above it meets nothing.
    /// </summary>
    internal float HighestUnder(float x0, float z0, float x1, float z1)
    {
        float gx0 = (x0 - MinX) / Cell, gx1 = (x1 - MinX) / Cell, gz0 = (z0 - MinZ) / Cell, gz1 = (z1 - MinZ) / Cell;
        int squaresX = Cols - 1, squaresZ = Rows - 1;
        float high = gx0 < 0f || gz0 < 0f || gx1 > squaresX || gz1 > squaresZ ? 0f : float.MinValue;
        int blockRows = _blockMax.Length / _blockCols;
        int bx0 = Math.Clamp((int)MathF.Floor(gx0) / Block, 0, _blockCols - 1), bx1 = Math.Clamp((int)MathF.Floor(gx1) / Block, 0, _blockCols - 1);
        int bz0 = Math.Clamp((int)MathF.Floor(gz0) / Block, 0, blockRows - 1), bz1 = Math.Clamp((int)MathF.Floor(gz1) / Block, 0, blockRows - 1);
        for (int bz = bz0; bz <= bz1; bz++)
        {
            for (int bx = bx0; bx <= bx1; bx++)
            {
                high = MathF.Max(high, _blockMax[bz * _blockCols + bx]);
            }
        }

        return high;
    }

    /// <summary>
    /// The ground of a level from its <paramref name="def"/> over <paramref name="bounds"/>: the gentle unevenness, the
    /// hills and dips, the banks; held level on the flats and toward the edge; level across the tracks (each at the
    /// height its middle line has); then the stream beds cut in, their bottoms of their own surface.
    /// </summary>
    public static Terrain Build(TerrainDef def, Aabb bounds, IReadOnlyList<RoadDef> roads, float roadMargin, (SurfaceId Surface, int Material) ground,
        IReadOnlyList<(Vector4 Rect, SurfaceId Surface, int Material)> patches, IReadOnlyList<(SurfaceId Surface, int Material)> streams)
    {
        float cell = def.Cell_m;
        int cols = Math.Max(2, (int)MathF.Ceiling((bounds.Max.X - bounds.Min.X) / cell) + 1);
        int rows = Math.Max(2, (int)MathF.Ceiling((bounds.Max.Z - bounds.Min.Z) / cell) + 1);
        var heights = new float[cols * rows];
        var kind = new byte[cols * rows];
        int firstBed = 1 + patches.Count;
        var shape = new Shaper(def, bounds);
        var lines = roads.Select(r => r.Points_m.Select(p => new Vector2(p[0], p[1])).ToArray()).ToArray();
        var beds = (def.Streams ?? Array.Empty<TerrainStreamDef>()).Select(st => st.Points_m.Select(p => new Vector2(p[0], p[1])).ToArray()).ToArray();
        Parallel.For(0, rows, r =>
        {
            float z = bounds.Min.Z + r * cell;
            for (int c = 0; c < cols; c++)
            {
                float x = bounds.Min.X + c * cell;
                float h = shape.Free(x, z);

                // Level across a track: the height its middle line has where it's nearest, blending back beside it.
                float weight = 0f, level = h;
                for (int k = 0; k < lines.Length; k++)
                {
                    (float d, Vector2 nearest) = Nearest(lines[k], new Vector2(x, z));
                    float half = roads[k].Width_m * 0.5f + 0.5f;
                    float w = d <= half ? 1f : 1f - SmoothStep(half, half + roadMargin, d);
                    if (w > weight)
                    {
                        weight = w;
                        level = shape.Free(nearest.X, nearest.Y);
                    }
                }

                h += (level - h) * weight;

                // What covers it: the last ground patch over it, else the ground; a stream bed over all.
                byte cover = 0;
                for (int k = 0; k < patches.Count; k++)
                {
                    Vector4 rect = patches[k].Rect;
                    if (x >= rect.X && x <= rect.Z && z >= rect.Y && z <= rect.W)
                    {
                        cover = (byte)(1 + k);
                    }
                }

                // The stream beds, cut in last (a track crossing one dips through it: a ford).
                for (int s = 0; def.Streams is not null && s < def.Streams.Length; s++)
                {
                    TerrainStreamDef stream = def.Streams[s];
                    (float d, _) = Nearest(beds[s], new Vector2(x, z));
                    float half = stream.Width_m * 0.5f;
                    float cut = d <= half ? 1f : 1f - SmoothStep(half, half + stream.Banks_m, d);
                    h -= stream.Depth_m * cut;
                    if (d <= half + stream.Banks_m * 0.25f)
                    {
                        cover = (byte)(firstBed + s);
                    }
                }

                heights[r * cols + c] = h;
                kind[r * cols + c] = cover;
            }
        });

        var surfaces = new List<SurfaceId> { ground.Surface };
        var materials = new List<int> { ground.Material };
        surfaces.AddRange(patches.Select(p => p.Surface));
        materials.AddRange(patches.Select(p => p.Material));
        surfaces.AddRange(streams.Select(p => p.Surface));
        materials.AddRange(streams.Select(p => p.Material));
        var streamBeds = (def.Streams ?? Array.Empty<TerrainStreamDef>()).Select((st, i) => new StreamBed(st.Name, beds[i], st.Width_m, st.Depth_m)).ToArray();
        return new Terrain(bounds.Min.X, bounds.Min.Z, cell, cols, rows, heights, kind, surfaces.ToArray(), materials.ToArray(), firstBed, streamBeds);
    }

    /// <summary>The ground before the tracks are levelled and the streams cut: what a point's height follows from.</summary>
    private sealed class Shaper
    {
        private readonly TerrainDef _def;
        private readonly Aabb _bounds;
        private readonly Vector2[][] _ridges;

        public Shaper(TerrainDef def, Aabb bounds)
        {
            _def = def;
            _bounds = bounds;
            _ridges = (def.Ridges ?? Array.Empty<TerrainRidgeDef>()).Select(r => r.Points_m.Select(p => new Vector2(p[0], p[1])).ToArray()).ToArray();
        }

        public float Free(float x, float z)
        {
            float h = 0f;
            foreach (TerrainNoiseDef noise in _def.Noise ?? Array.Empty<TerrainNoiseDef>())
            {
                h += noise.Height_m * (0.65f * ValueNoise(x / noise.Size_m, z / noise.Size_m, noise.Seed) +
                                       0.35f * ValueNoise(x / (noise.Size_m * 0.43f), z / (noise.Size_m * 0.43f), noise.Seed + 101u));
            }

            foreach (TerrainHillDef hill in _def.Hills ?? Array.Empty<TerrainHillDef>())
            {
                float d = Vector2.Distance(new Vector2(x, z), new Vector2(hill.At_m[0], hill.At_m[1]));
                if (d < hill.Radius_m)
                {
                    h += hill.Height_m * 0.5f * (1f + MathF.Cos(MathF.PI * d / hill.Radius_m));
                }
            }

            for (int k = 0; k < _ridges.Length; k++)
            {
                TerrainRidgeDef ridge = _def.Ridges![k];
                (float d, _) = Nearest(_ridges[k], new Vector2(x, z));
                float half = ridge.Width_m * 0.5f;
                if (d < half)
                {
                    h += ridge.Height_m * 0.5f * (1f + MathF.Cos(MathF.PI * d / half));
                }
            }

            // Held level on the flats, and coming back to level toward the edge. Where the margins of flats at different
            // heights meet, their heights blend (each by how near it is, a flat's own rect all its own), so no step opens.
            float flatWeight = 0f, heightSum = 0f, shareSum = 0f;
            foreach (TerrainFlatDef flat in _def.Flats ?? Array.Empty<TerrainFlatDef>())
            {
                float x0 = MathF.Min(flat.Rect_m[0], flat.Rect_m[2]), x1 = MathF.Max(flat.Rect_m[0], flat.Rect_m[2]);
                float z0 = MathF.Min(flat.Rect_m[1], flat.Rect_m[3]), z1 = MathF.Max(flat.Rect_m[1], flat.Rect_m[3]);
                float dx = MathF.Max(0f, MathF.Max(x0 - x, x - x1)), dz = MathF.Max(0f, MathF.Max(z0 - z, z - z1));
                float outside = MathF.Sqrt(dx * dx + dz * dz);
                float w = outside <= 0f ? 1f : 1f - SmoothStep(0f, MathF.Max(_def.FlatMargin_m, 1e-3f), outside);
                if (w <= 0f)
                {
                    continue;
                }

                float share = w / (1f - w + 1e-5f);
                heightSum += share * flat.Height_m;
                shareSum += share;
                flatWeight = MathF.Max(flatWeight, w);
            }

            if (shareSum > 0f)
            {
                h += (heightSum / shareSum - h) * flatWeight;
            }

            float edge = MathF.Min(MathF.Min(x - _bounds.Min.X, _bounds.Max.X - x), MathF.Min(z - _bounds.Min.Z, _bounds.Max.Z - z));
            float level = 1f - SmoothStep(0f, MathF.Max(_def.Edge_m, 1e-3f), edge);
            return h * (1f - level);
        }
    }

    /// <summary>How far <paramref name="p"/> is from a line of points, and the nearest point on it.</summary>
    private static (float Distance, Vector2 Nearest) Nearest(Vector2[] line, Vector2 p)
    {
        float best = float.MaxValue;
        Vector2 nearest = line.Length > 0 ? line[0] : p;
        for (int i = 1; i < line.Length; i++)
        {
            Vector2 a = line[i - 1], ab = line[i] - a;
            float t = Math.Clamp(Vector2.Dot(p - a, ab) / MathF.Max(ab.LengthSquared(), 1e-6f), 0f, 1f);
            Vector2 q = a + ab * t;
            float d = Vector2.DistanceSquared(p, q);
            if (d < best)
            {
                best = d;
                nearest = q;
            }
        }

        return (MathF.Sqrt(best), nearest);
    }

    private static float SmoothStep(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / MathF.Max(to - from, 1e-6f), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Smooth value noise in −1 … 1 over a unit lattice: a random value at each lattice point, eased between.</summary>
    private static float ValueNoise(float x, float z, uint seed)
    {
        int ix = (int)MathF.Floor(x), iz = (int)MathF.Floor(z);
        float fx = x - ix, fz = z - iz;
        float u = fx * fx * (3f - 2f * fx), v = fz * fz * (3f - 2f * fz);
        float a = Lattice(ix, iz, seed), b = Lattice(ix + 1, iz, seed), c = Lattice(ix, iz + 1, seed), d = Lattice(ix + 1, iz + 1, seed);
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }

    private static float Lattice(int x, int z, uint seed)
    {
        uint h = (uint)x * 0x8DA6B343u ^ (uint)z * 0xD8163841u ^ seed * 0xCB1AB31Fu;
        h ^= h >> 13;
        h *= 0x5BD1E995u;
        h ^= h >> 15;
        return (h & 0xFFFFFF) / (float)0xFFFFFF * 2f - 1f;
    }
}
