using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Level;

namespace Pb.Sim.AI;

/// <summary>Path queries for bots. The game and the tests both use <see cref="NavGrid"/>.</summary>
public interface IBotNavigation
{
    /// <summary>
    /// A walkable path from <paramref name="from"/> to <paramref name="to"/>: waypoints after the start,
    /// the goal last. False (and an empty path) when there's none.
    /// </summary>
    bool FindPath(Vector3 from, Vector3 to, List<Vector3> path);

    /// <summary>The nearest place a bot can stand to <paramref name="position"/>, on about the same floor.</summary>
    bool TrySnap(Vector3 position, out Vector3 snapped);

    /// <summary>True when a bot can walk in a straight line from one point to the other.</summary>
    bool CanWalkStraight(Vector3 from, Vector3 to);
}

/// <summary>
/// Where bots can walk, built from a level's walkable geometry when it loads. The level is divided
/// into square columns; down each column every surface with headroom above it and clear of walls by
/// the agent radius becomes a "span", a place a bot can stand. Spans in neighbouring columns join when
/// the step between them is small enough, so stairs (ramps), floors and mezzanines all connect, and
/// paths are A* searches over spans, straightened afterwards. It's deterministic and engine-free, so
/// the game and the sim tests use the same paths. Searches don't allocate.
/// </summary>
public sealed class NavGrid : IBotNavigation
{
    private const int None = -1;

    /// <summary>Neighbour offsets: the four sides first, then the diagonals.</summary>
    private static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };

    private static readonly int[] Dz = { 0, 0, 1, -1, 1, -1, 1, -1 };

    private static readonly float Diagonal = MathF.Sqrt(2f);

    private readonly NavParams _p;
    private readonly float _minX;
    private readonly float _minZ;
    private readonly int _cols;
    private readonly int _rows;
    private readonly int[] _columnStart;
    private readonly float[] _spanX;
    private readonly float[] _spanY;
    private readonly float[] _spanZ;
    private readonly int[] _spanColumn;
    private readonly int[] _links;

    // Which connected piece of the grid each span is in: a goal in another piece fails without a search.
    private readonly int[] _component;

    // Landmarks: walking distances from a few spans spread over the biggest piece to every span in it
    // ([span * count + landmark]), so the search's estimate of what's left knows about the long way round.
    private readonly int _landmarkCount;
    private readonly int _landmarkComponent = None;
    private readonly float[] _landmarkDistance;
    private readonly float[] _goalLandmark;
    private bool _landmarksOn;

    // Search scratch, reused by every query.
    private readonly float[] _g;
    private readonly int[] _parent;
    private readonly int[] _seen;
    private readonly int[] _closed;
    private readonly SpanHeap _open;
    private readonly List<int> _spans = new();
    private int _generation;

    private NavGrid(NavParams p, float minX, float minZ, int cols, int rows, int[] columnStart, float[] spanY)
    {
        _p = p;
        _minX = minX;
        _minZ = minZ;
        _cols = cols;
        _rows = rows;
        _columnStart = columnStart;
        _spanY = spanY;
        _spanColumn = new int[spanY.Length];
        _spanX = new float[spanY.Length];
        _spanZ = new float[spanY.Length];
        for (int col = 0; col < cols * rows; col++)
        {
            for (int s = columnStart[col]; s < columnStart[col + 1]; s++)
            {
                _spanColumn[s] = col;
                _spanX[s] = minX + (col % cols + 0.5f) * p.CellSize;
                _spanZ[s] = minZ + (col / cols + 0.5f) * p.CellSize;
            }
        }

        _links = new int[spanY.Length * 8];
        _g = new float[spanY.Length];
        _parent = new int[spanY.Length];
        _seen = new int[spanY.Length];
        _closed = new int[spanY.Length];
        _open = new SpanHeap(Math.Min(spanY.Length, 65536) + 16);
        Link();
        _component = new int[spanY.Length];
        int biggest = LabelComponents();
        _landmarkCount = biggest == None ? 0 : p.Landmarks;
        _landmarkDistance = new float[spanY.Length * _landmarkCount];
        _goalLandmark = new float[_landmarkCount];
        if (_landmarkCount > 0)
        {
            _landmarkComponent = _component[biggest];
            PlaceLandmarks(biggest);
        }
    }

    public NavParams Params => _p;

    public int SpanCount => _spanY.Length;

    public int Columns => _cols;

    public int Rows => _rows;

    /// <summary>Places joined to at least one neighbour (for stats and tests).</summary>
    public int LinkedSpanCount { get; private set; }

    /// <summary>Spans expanded by the last search (for tests and the benchmark).</summary>
    public int LastSearchExpanded { get; private set; }

    /// <summary>Builds the grid for <paramref name="level"/>: every surface flagged for walking, plus the ground.</summary>
    public static NavGrid Build(LevelLayout level, NavParams p)
    {
        Aabb bounds = level.Bounds;
        float cell = p.CellSize;
        int cols = Math.Max(1, (int)MathF.Ceiling((bounds.Max.X - bounds.Min.X) / cell));
        int rows = Math.Max(1, (int)MathF.Ceiling((bounds.Max.Z - bounds.Min.Z) / cell));

        // Walkable geometry: what feet stand on and what bodies bump into. The drawn stair steps count
        // too (people walk on the smooth ramp laid over them), so nobody paths underneath a flight.
        var shapes = new List<Shape>();
        var walk = new CollisionWorld(2f);
        foreach (LevelPrimitive prim in level.Primitives)
        {
            if (prim.Has(PrimitiveFlags.Walk) || prim.Role == PrimitiveRole.Stair)
            {
                Shape shape = prim.CreateShape();
                shapes.Add(shape);
                walk.Add(shape, prim.Surface, "walk");
            }
        }

        walk.Build();
        var index = new ColumnIndex(bounds.Min.X, bounds.Min.Z, bounds.Max.X, bounds.Max.Z, 2f, shapes);

        var columnStart = new int[cols * rows + 1];
        var spanY = new List<float>(cols * rows + cols * rows / 4);
        var intervals = new List<(float Bottom, float Top)>(16);
        var floors = new List<float>(8);
        for (int r = 0; r < rows; r++)
        {
            float z = bounds.Min.Z + (r + 0.5f) * cell;
            for (int c = 0; c < cols; c++)
            {
                float x = bounds.Min.X + (c + 0.5f) * cell;
                columnStart[r * cols + c] = spanY.Count;
                SolidIntervals(index, shapes, x, z, intervals);
                FloorsWithHeadroom(intervals, p.AgentHeight, bounds.Max.Y, floors);
                foreach (float y in floors)
                {
                    // Clear of walls: a body-height capsule of the agent radius, starting a step above the
                    // floor (a step's worth of kerb or slope is fine), touches nothing.
                    var low = new Vector3(x, y + p.StepHeight + p.AgentRadius, z);
                    var high = new Vector3(x, y + p.AgentHeight - p.AgentRadius, z);
                    if (!walk.SweepSphere(low, high, p.AgentRadius, out _))
                    {
                        spanY.Add(y);
                    }
                }
            }
        }

        columnStart[cols * rows] = spanY.Count;
        return new NavGrid(p, bounds.Min.X, bounds.Min.Z, cols, rows, columnStart, spanY.ToArray());
    }

    public Vector3 PositionOf(int span) => new(_spanX[span], _spanY[span], _spanZ[span]);

    /// <summary>The span neighbouring <paramref name="span"/> in direction <paramref name="direction"/> (0–7), or −1.</summary>
    public int Neighbour(int span, int direction) => _links[span * 8 + direction];

    /// <summary>Whether <paramref name="b"/> is <paramref name="a"/> or one of its neighbours.</summary>
    public bool AreLinked(int a, int b)
    {
        if (a == b)
        {
            return true;
        }

        for (int d = 0; d < 8; d++)
        {
            if (_links[a * 8 + d] == b)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The span under <paramref name="position"/> (its own column, the floor nearest the feet), or −1.</summary>
    public int SpanAt(Vector3 position)
    {
        int col = ColumnOf(position.X, position.Z);
        return col < 0 ? None : NearestInColumn(col, position.Y, 1.0f, 0.75f);
    }

    /// <summary>The nearest span to <paramref name="position"/> within <paramref name="radius"/> on about the same floor, or −1.</summary>
    public int NearestSpan(Vector3 position, float radius = 2f)
    {
        int here = SpanAt(position);
        if (here != None)
        {
            return here;
        }

        int c0 = (int)MathF.Floor((position.X - _minX) / _p.CellSize);
        int r0 = (int)MathF.Floor((position.Z - _minZ) / _p.CellSize);
        int reach = (int)MathF.Ceiling(radius / _p.CellSize);
        int best = None;
        float bestDistance = float.MaxValue;
        for (int ring = 1; ring <= reach; ring++)
        {
            for (int dr = -ring; dr <= ring; dr++)
            {
                for (int dc = -ring; dc <= ring; dc++)
                {
                    if (Math.Max(Math.Abs(dr), Math.Abs(dc)) != ring)
                    {
                        continue; // only the ring's edge; inner rings were done
                    }

                    int c = c0 + dc;
                    int r = r0 + dr;
                    if (c < 0 || r < 0 || c >= _cols || r >= _rows)
                    {
                        continue;
                    }

                    int s = NearestInColumn(r * _cols + c, position.Y, 1.0f, 0.75f);
                    if (s == None)
                    {
                        continue;
                    }

                    Vector3 at = PositionOf(s);
                    float d = (at.X - position.X) * (at.X - position.X) + (at.Z - position.Z) * (at.Z - position.Z);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = s;
                    }
                }
            }

            if (best != None)
            {
                return best; // the nearest ring with anything wins (close enough to nearest)
            }
        }

        return None;
    }

    public bool TrySnap(Vector3 position, out Vector3 snapped)
    {
        int s = NearestSpan(position);
        snapped = s == None ? position : PositionOf(s);
        return s != None;
    }

    /// <summary>
    /// Where something that moves stands in the way (a door standing open): spans at whose floor point it says yes are
    /// walked round, by searches and by the straightening of their paths. Null: nothing moves.
    /// </summary>
    public Func<Vector3, bool>? Blocked { get; set; }

    public bool FindPath(Vector3 from, Vector3 to, List<Vector3> path)
    {
        path.Clear();
        int start = NearestSpan(from);
        int goal = NearestSpan(to);
        if (start == None || goal == None || _component[start] != _component[goal] || !Search(start, goal))
        {
            return false;
        }

        // _spans holds the span path goal → start; walk it start → goal, keeping only the corners a
        // straight walk can't cut. From each corner, look ahead in doubling strides for a span still in a
        // straight line, then narrow down between the last that was and the first that wasn't: a few
        // straight-line checks per corner, not one per span (which made long straight runs cost milliseconds).
        int anchor = _spans.Count - 1;
        while (anchor > 0)
        {
            int good = anchor - 1;
            int bad = None;
            for (int stride = 1; good > 0; stride *= 2)
            {
                int probe = Math.Max(good - stride, 0);
                if (!StraightBetween(_spans[anchor], _spans[probe]))
                {
                    bad = probe;
                    break;
                }

                good = probe;
            }

            while (bad != None && good - bad > 1)
            {
                int middle = (good + bad) / 2;
                if (StraightBetween(_spans[anchor], _spans[middle]))
                {
                    good = middle;
                }
                else
                {
                    bad = middle;
                }
            }

            path.Add(PositionOf(_spans[good]));
            anchor = good;
        }

        if (path.Count == 0)
        {
            path.Add(PositionOf(goal));
        }

        return true;
    }

    public bool CanWalkStraight(Vector3 from, Vector3 to)
    {
        int a = NearestSpan(from, 0.5f);
        int b = NearestSpan(to, 0.5f);
        return a != None && b != None && StraightBetween(a, b);
    }

    /// <summary>A* from <paramref name="start"/> to <paramref name="goal"/>; on success <see cref="_spans"/> holds the path backwards.</summary>
    private bool Search(int start, int goal)
    {
        _spans.Clear();
        if (start == goal)
        {
            _spans.Add(goal);
            _spans.Add(start);
            return true;
        }

        int generation = ++_generation;
        _open.Clear();
        _g[start] = 0f;
        _parent[start] = None;
        _seen[start] = generation;
        Vector3 goalAt = PositionOf(goal);
        _landmarksOn = _landmarkCount > 0 && _component[goal] == _landmarkComponent;
        if (_landmarksOn)
        {
            Array.Copy(_landmarkDistance, goal * _landmarkCount, _goalLandmark, 0, _landmarkCount);
        }

        _open.Push(start, Heuristic(start, goalAt));
        int expanded = 0;
        bool found = false;
        while (_open.Count > 0)
        {
            int s = _open.Pop();
            if (_closed[s] == generation)
            {
                continue; // a stale duplicate
            }

            _closed[s] = generation;
            if (s == goal)
            {
                found = true;
                break;
            }

            if (++expanded > _p.MaxSearchNodes)
            {
                break;
            }

            float gs = _g[s];
            for (int d = 0; d < 8; d++)
            {
                int n = _links[s * 8 + d];
                if (n == None || _closed[n] == generation || (n != goal && Blocked is { } blocked && blocked(PositionOf(n))))
                {
                    continue;
                }

                float g = gs + StepCost(s, n, d);
                if (_seen[n] != generation || g < _g[n])
                {
                    _seen[n] = generation;
                    _g[n] = g;
                    _parent[n] = s;
                    _open.Push(n, g + Heuristic(n, goalAt));
                }
            }
        }

        LastSearchExpanded = expanded;
        if (!found)
        {
            return false;
        }

        for (int s = goal; s != None; s = _parent[s])
        {
            _spans.Add(s);
        }

        return true;
    }

    /// <summary>
    /// How far the goal still is at least: octile distance plus the climb, or more where a landmark knows better (the
    /// difference of the two places' walking distances from it can't be more than the walk between them). Weighted
    /// (navigation.jsonc): above 1 the search heads for the goal much more eagerly, at the price of paths slightly
    /// longer than the shortest (straightening hides most of it).
    /// </summary>
    private float Heuristic(int span, Vector3 goal)
    {
        float dx = MathF.Abs(_spanX[span] - goal.X);
        float dz = MathF.Abs(_spanZ[span] - goal.Z);
        float h = MathF.Max(dx, dz) + (Diagonal - 1f) * MathF.Min(dx, dz) + MathF.Abs(_spanY[span] - goal.Y);
        if (_landmarksOn)
        {
            int row = span * _landmarkCount;
            for (int k = 0; k < _landmarkCount; k++)
            {
                h = MathF.Max(h, MathF.Abs(_goalLandmark[k] - _landmarkDistance[row + k]));
            }
        }

        return _p.HeuristicWeight * h;
    }

    /// <summary>The cost of the step from <paramref name="s"/> to its neighbour <paramref name="n"/> in direction <paramref name="d"/>.</summary>
    private float StepCost(int s, int n, int d) => (d < 4 ? 1f : Diagonal) * _p.CellSize + MathF.Abs(_spanY[n] - _spanY[s]);

    /// <summary>Labels the connected pieces of the grid; returns a span in the biggest (−1 for an empty grid).</summary>
    private int LabelComponents()
    {
        Array.Fill(_component, None);
        var stack = new Stack<int>();
        int label = 0;
        int biggest = None;
        int biggestSize = 0;
        for (int first = 0; first < _component.Length; first++)
        {
            if (_component[first] != None)
            {
                continue;
            }

            int size = 0;
            _component[first] = label;
            stack.Push(first);
            while (stack.Count > 0)
            {
                int s = stack.Pop();
                size++;
                for (int d = 0; d < 8; d++)
                {
                    int n = _links[s * 8 + d];
                    if (n != None && _component[n] == None)
                    {
                        _component[n] = label;
                        stack.Push(n);
                    }
                }
            }

            if (size > biggestSize)
            {
                biggestSize = size;
                biggest = first;
            }

            label++;
        }

        return biggest;
    }

    /// <summary>
    /// Spreads the landmarks over the biggest piece: each one as far as possible from those before it (the first as far as
    /// possible from <paramref name="seed"/>), and works out every span's walking distance from each.
    /// </summary>
    private void PlaceLandmarks(int seed)
    {
        var distance = new float[_spanY.Length];
        var nearest = new float[_spanY.Length];
        Array.Fill(nearest, float.PositiveInfinity);
        Distances(seed, distance);
        int next = Farthest(distance);
        for (int k = 0; k < _landmarkCount; k++)
        {
            Distances(next, distance);
            for (int s = 0; s < distance.Length; s++)
            {
                _landmarkDistance[s * _landmarkCount + k] = distance[s];
                nearest[s] = MathF.Min(nearest[s], distance[s]);
            }

            next = Farthest(nearest);
        }
    }

    /// <summary>The span with the largest finite value (the first of equals).</summary>
    private static int Farthest(float[] distance)
    {
        int best = 0;
        float bestDistance = -1f;
        for (int s = 0; s < distance.Length; s++)
        {
            if (distance[s] > bestDistance && float.IsFinite(distance[s]))
            {
                bestDistance = distance[s];
                best = s;
            }
        }

        return best;
    }

    /// <summary>Walking distance from <paramref name="source"/> to every span (infinite where it can't walk), by Dijkstra.</summary>
    private void Distances(int source, float[] distance)
    {
        Array.Fill(distance, float.PositiveInfinity);
        int generation = ++_generation;
        _open.Clear();
        distance[source] = 0f;
        _open.Push(source, 0f);
        while (_open.Count > 0)
        {
            int s = _open.Pop();
            if (_closed[s] == generation)
            {
                continue; // a stale duplicate
            }

            _closed[s] = generation;
            float ds = distance[s];
            for (int d = 0; d < 8; d++)
            {
                int n = _links[s * 8 + d];
                if (n == None || _closed[n] == generation)
                {
                    continue;
                }

                float g = ds + StepCost(s, n, d);
                if (g < distance[n])
                {
                    distance[n] = g;
                    _open.Push(n, g);
                }
            }
        }
    }

    /// <summary>
    /// Whether a straight walk from span <paramref name="a"/> to span <paramref name="b"/> stays on the grid:
    /// sampled every half cell, each sample must have a span within a step of the previous one.
    /// </summary>
    private bool StraightBetween(int a, int b)
    {
        Vector3 pa = PositionOf(a);
        Vector3 pb = PositionOf(b);
        float dx = pb.X - pa.X;
        float dz = pb.Z - pa.Z;
        float length = MathF.Sqrt(dx * dx + dz * dz);
        int samples = (int)MathF.Ceiling(length / (_p.CellSize * 0.5f));
        int current = a;
        int currentCol = _spanColumn[a];
        for (int i = 1; i <= samples; i++)
        {
            float t = i / (float)samples;
            int col = ColumnOf(pa.X + dx * t, pa.Z + dz * t);
            if (col < 0)
            {
                return false;
            }

            if (col == currentCol)
            {
                continue;
            }

            // Moving diagonally between columns, both side columns must be walkable too (no corner cutting).
            int fromC = currentCol % _cols;
            int fromR = currentCol / _cols;
            int toC = col % _cols;
            int toR = col / _cols;
            if (Math.Abs(toC - fromC) > 1 || Math.Abs(toR - fromR) > 1)
            {
                return false; // skipped a column: sampling too coarse for this direction (shouldn't happen)
            }

            int next = None;
            for (int d = 0; d < 8; d++)
            {
                if (fromC + Dx[d] == toC && fromR + Dz[d] == toR)
                {
                    next = _links[current * 8 + d];
                    break;
                }
            }

            if (next == None || (next != b && Blocked is { } blocked && blocked(PositionOf(next))))
            {
                return false;
            }

            current = next;
            currentCol = col;
        }

        return current == b || MathF.Abs(_spanY[current] - _spanY[b]) <= _p.StepHeight;
    }

    private int ColumnOf(float x, float z)
    {
        int c = (int)MathF.Floor((x - _minX) / _p.CellSize);
        int r = (int)MathF.Floor((z - _minZ) / _p.CellSize);
        return c < 0 || r < 0 || c >= _cols || r >= _rows ? None : r * _cols + c;
    }

    /// <summary>The span in column <paramref name="col"/> whose floor is nearest <paramref name="y"/>, at most <paramref name="below"/> under or <paramref name="above"/> over it.</summary>
    private int NearestInColumn(int col, float y, float below, float above)
    {
        int best = None;
        float bestGap = float.MaxValue;
        for (int s = _columnStart[col]; s < _columnStart[col + 1]; s++)
        {
            float gap = _spanY[s] - y;
            if (gap < -below || gap > above)
            {
                continue;
            }

            if (MathF.Abs(gap) < bestGap)
            {
                bestGap = MathF.Abs(gap);
                best = s;
            }
        }

        return best;
    }

    /// <summary>
    /// Joins each span to the span in each neighbouring column within a step of it (the nearest in
    /// height). Diagonal moves also need both side columns joined, so paths never cut a wall corner.
    /// </summary>
    private void Link()
    {
        Array.Fill(_links, None);
        int linked = 0;
        for (int s = 0; s < _spanY.Length; s++)
        {
            int col = _spanColumn[s];
            int c = col % _cols;
            int r = col / _cols;
            bool any = false;
            for (int d = 0; d < 8; d++)
            {
                int nc = c + Dx[d];
                int nr = r + Dz[d];
                if (nc < 0 || nr < 0 || nc >= _cols || nr >= _rows)
                {
                    continue;
                }

                int n = NearestInColumn(nr * _cols + nc, _spanY[s], _p.StepHeight, _p.StepHeight);
                if (n == None)
                {
                    continue;
                }

                if (d >= 4 && (_links[s * 8 + SideIndex(Dx[d], 0)] == None || _links[s * 8 + SideIndex(0, Dz[d])] == None))
                {
                    continue;
                }

                _links[s * 8 + d] = n;
                any = true;
            }

            if (any)
            {
                linked++;
            }
        }

        LinkedSpanCount = linked;
    }

    private static int SideIndex(int dx, int dz) => dx switch
    {
        1 => 0,
        -1 => 1,
        _ => dz == 1 ? 2 : 3,
    };

    /// <summary>Solid stretches down the vertical line through (x, z), merged and sorted bottom up.</summary>
    private static void SolidIntervals(ColumnIndex index, List<Shape> shapes, float x, float z, List<(float Bottom, float Top)> intervals)
    {
        intervals.Clear();
        IReadOnlyList<int> near = index.At(x, z);
        for (int k = 0; k < near.Count; k++)
        {
            Shape shape = shapes[near[k]];
            Aabb box = shape.Bounds;
            if (x < box.Min.X || x > box.Max.X || z < box.Min.Z || z > box.Max.Z)
            {
                continue;
            }

            float top = box.Max.Y + 1f;
            float bottom = box.Min.Y - 1f;
            float length = top - bottom;
            if (shape.Sweep(new Vector3(x, top, z), new Vector3(0f, -length, 0f), 0f, out float down, out _) &&
                shape.Sweep(new Vector3(x, bottom, z), new Vector3(0f, length, 0f), 0f, out float up, out _))
            {
                intervals.Add((bottom + up * length, top - down * length));
            }
        }

        intervals.Sort((a, b) => a.Bottom.CompareTo(b.Bottom));
        int w = 0;
        for (int i = 0; i < intervals.Count; i++)
        {
            if (w > 0 && intervals[i].Bottom <= intervals[w - 1].Top + 0.02f)
            {
                intervals[w - 1] = (intervals[w - 1].Bottom, MathF.Max(intervals[w - 1].Top, intervals[i].Top));
            }
            else
            {
                intervals[w++] = intervals[i];
            }
        }

        intervals.RemoveRange(w, intervals.Count - w);
    }

    /// <summary>Floors in a column (the ground, then the top of each solid stretch) with enough headroom above.</summary>
    private static void FloorsWithHeadroom(List<(float Bottom, float Top)> intervals, float height, float ceilingLimit, List<float> floors)
    {
        floors.Clear();
        bool groundCovered = intervals.Count > 0 && intervals[0].Bottom <= 0.02f;
        if (!groundCovered)
        {
            AddIfRoom(0f, intervals, 0, height, ceilingLimit, floors);
        }

        for (int i = 0; i < intervals.Count; i++)
        {
            AddIfRoom(MathF.Max(0f, intervals[i].Top), intervals, i + 1, height, ceilingLimit, floors);
        }
    }

    private static void AddIfRoom(float floor, List<(float Bottom, float Top)> intervals, int next, float height, float ceilingLimit, List<float> floors)
    {
        float ceiling = next < intervals.Count ? intervals[next].Bottom : float.MaxValue;
        if (ceiling - floor >= height && floor + height <= ceilingLimit)
        {
            floors.Add(floor);
        }
    }

    /// <summary>Which shapes overlap each square bucket of the level (for column queries during the build).</summary>
    private sealed class ColumnIndex
    {
        private readonly float _minX;
        private readonly float _minZ;
        private readonly float _size;
        private readonly int _cols;
        private readonly int _rows;
        private readonly List<int>?[] _buckets;

        public ColumnIndex(float minX, float minZ, float maxX, float maxZ, float size, List<Shape> shapes)
        {
            _minX = minX;
            _minZ = minZ;
            _size = size;
            _cols = Math.Max(1, (int)MathF.Ceiling((maxX - minX) / size));
            _rows = Math.Max(1, (int)MathF.Ceiling((maxZ - minZ) / size));
            _buckets = new List<int>?[_cols * _rows];
            for (int i = 0; i < shapes.Count; i++)
            {
                Aabb b = shapes[i].Bounds;
                int c0 = Math.Clamp((int)MathF.Floor((b.Min.X - minX) / size), 0, _cols - 1);
                int c1 = Math.Clamp((int)MathF.Floor((b.Max.X - minX) / size), 0, _cols - 1);
                int r0 = Math.Clamp((int)MathF.Floor((b.Min.Z - minZ) / size), 0, _rows - 1);
                int r1 = Math.Clamp((int)MathF.Floor((b.Max.Z - minZ) / size), 0, _rows - 1);
                for (int r = r0; r <= r1; r++)
                {
                    for (int c = c0; c <= c1; c++)
                    {
                        (_buckets[r * _cols + c] ??= new List<int>()).Add(i);
                    }
                }
            }
        }

        public IReadOnlyList<int> At(float x, float z)
        {
            int c = Math.Clamp((int)MathF.Floor((x - _minX) / _size), 0, _cols - 1);
            int r = Math.Clamp((int)MathF.Floor((z - _minZ) / _size), 0, _rows - 1);
            return (IReadOnlyList<int>?)_buckets[r * _cols + c] ?? Array.Empty<int>();
        }
    }
}

/// <summary>A binary min-heap of span indices keyed by priority, with duplicates allowed (lazy deletion).</summary>
internal sealed class SpanHeap
{
    private int[] _items;
    private float[] _keys;

    public SpanHeap(int capacity)
    {
        _items = new int[capacity];
        _keys = new float[capacity];
    }

    public int Count { get; private set; }

    public void Clear() => Count = 0;

    public void Push(int item, float key)
    {
        if (Count == _items.Length)
        {
            Array.Resize(ref _items, _items.Length * 2);
            Array.Resize(ref _keys, _keys.Length * 2);
        }

        int i = Count++;
        while (i > 0)
        {
            int parent = (i - 1) >> 1;
            if (_keys[parent] <= key)
            {
                break;
            }

            _items[i] = _items[parent];
            _keys[i] = _keys[parent];
            i = parent;
        }

        _items[i] = item;
        _keys[i] = key;
    }

    public int Pop()
    {
        int top = _items[0];
        int lastItem = _items[--Count];
        float lastKey = _keys[Count];
        int i = 0;
        while (true)
        {
            int child = 2 * i + 1;
            if (child >= Count)
            {
                break;
            }

            if (child + 1 < Count && _keys[child + 1] < _keys[child])
            {
                child++;
            }

            if (_keys[child] >= lastKey)
            {
                break;
            }

            _items[i] = _items[child];
            _keys[i] = _keys[child];
            i = child;
        }

        if (Count > 0)
        {
            _items[i] = lastItem;
            _keys[i] = lastKey;
        }

        return top;
    }
}
