using System.Numerics;
using Pb.Sim.Level;

namespace Pb.Sim.Vehicles;

/// <summary>
/// A level's roads as a graph to drive on (Phase 5): every road's points are its nodes (a point two roads share joins
/// them), each stretch between neighbouring points an edge both ways. <see cref="Route"/> finds the way along the roads
/// from the road nearest one place to the road nearest another, by Dijkstra over arrays kept for the purpose, so a route
/// costs nothing but the caller's list.
/// </summary>
public sealed class RoadGraph
{
    /// <summary>Points of two roads closer than this are one junction.</summary>
    private const float Join = 1.0f;

    private readonly List<Vector3> _nodes = new();
    private readonly List<(int A, int B)> _segments = new();
    private readonly List<int>[] _next;
    private readonly float[] _distance;
    private readonly int[] _previous;
    private readonly bool[] _done;
    private readonly int[] _stack;

    private RoadGraph(IReadOnlyList<RoadSpec> roads)
    {
        var next = new List<List<int>>();
        foreach (RoadSpec road in roads)
        {
            int last = -1;
            foreach (Vector3 point in road.Points)
            {
                int node = NodeAt(point);
                if (node < 0)
                {
                    node = _nodes.Count;
                    _nodes.Add(point with { Y = 0f });
                    next.Add(new List<int>(4));
                }

                if (last >= 0 && last != node && !next[last].Contains(node))
                {
                    next[last].Add(node);
                    next[node].Add(last);
                    _segments.Add((last, node));
                }

                last = node;
            }
        }

        // A road that ends on another's side (a T-junction) joins it there.
        for (int e = 0; e < _nodes.Count; e++)
        {
            if (next[e].Count != 1)
            {
                continue;
            }

            for (int k = 0; k < _segments.Count; k++)
            {
                (int a, int b) = _segments[k];
                Vector3 ab = (_nodes[b] - _nodes[a]) with { Y = 0f };
                float length = ab.Length();
                if (a == e || b == e || length < 3f * Join)
                {
                    continue;
                }

                float along = Vector3.Dot((_nodes[e] - _nodes[a]) with { Y = 0f }, ab / length);
                if (along > Join && along < length - Join && Flat(_nodes[a] + ab / length * along - _nodes[e]) < Join)
                {
                    _segments[k] = (a, e);
                    _segments.Add((e, b));
                    next[a].Remove(b);
                    next[b].Remove(a);
                    next[a].Add(e);
                    next[e].Add(a);
                    next[e].Add(b);
                    next[b].Add(e);
                    break;
                }
            }
        }

        _next = next.ToArray();
        _distance = new float[_nodes.Count];
        _previous = new int[_nodes.Count];
        _done = new bool[_nodes.Count];
        _stack = new int[_nodes.Count];
    }

    public int NodeCount => _nodes.Count;

    public int SegmentCount => _segments.Count;

    public static RoadGraph Build(IReadOnlyList<RoadSpec> roads) => new(roads);

    /// <summary>The point on a road nearest <paramref name="p"/> (on the plan), and how far it is; false with no roads.</summary>
    public bool Nearest(Vector3 p, out Vector3 point, out float distance)
    {
        point = default;
        distance = float.MaxValue;
        for (int s = 0; s < _segments.Count; s++)
        {
            Vector3 q = OnSegment(s, p, out _);
            float d = Flat(q - p);
            if (d < distance)
            {
                distance = d;
                point = q;
            }
        }

        return _segments.Count > 0;
    }

    /// <summary>
    /// The way along the roads from the road point nearest <paramref name="from"/> to the one nearest
    /// <paramref name="to"/>, into <paramref name="route"/> (from the first to the last), and its length along the roads;
    /// false if there are no roads or they don't join.
    /// </summary>
    public bool Route(Vector3 from, Vector3 to, List<Vector3> route, out float length)
    {
        route.Clear();
        length = 0f;
        int startSegment = NearestSegment(from, out Vector3 start, out float startAlong);
        int endSegment = NearestSegment(to, out Vector3 end, out float endAlong);
        if (startSegment < 0 || endSegment < 0)
        {
            return false;
        }

        if (startSegment == endSegment)
        {
            route.Add(start);
            route.Add(end);
            length = Flat(end - start);
            return true;
        }

        // Dijkstra from both ends of the start's segment (each at its distance along it) to either end of the end's.
        Array.Fill(_distance, float.MaxValue);
        Array.Fill(_previous, -1);
        Array.Fill(_done, false);
        (int sa, int sb) = _segments[startSegment];
        (int ea, int eb) = _segments[endSegment];
        float segmentLength = Flat(_nodes[sb] - _nodes[sa]);
        _distance[sa] = startAlong;
        _distance[sb] = segmentLength - startAlong;
        for (int round = 0; round < _nodes.Count; round++)
        {
            int best = -1;
            for (int n = 0; n < _nodes.Count; n++)
            {
                if (!_done[n] && _distance[n] < float.MaxValue && (best < 0 || _distance[n] < _distance[best]))
                {
                    best = n;
                }
            }

            if (best < 0)
            {
                break;
            }

            _done[best] = true;
            foreach (int n in _next[best])
            {
                float d = _distance[best] + Flat(_nodes[n] - _nodes[best]);
                if (d < _distance[n])
                {
                    _distance[n] = d;
                    _previous[n] = best;
                }
            }
        }

        float endLength = Flat(_nodes[eb] - _nodes[ea]);
        float viaA = _distance[ea] < float.MaxValue ? _distance[ea] + endAlong : float.MaxValue;
        float viaB = _distance[eb] < float.MaxValue ? _distance[eb] + endLength - endAlong : float.MaxValue;
        if (viaA == float.MaxValue && viaB == float.MaxValue)
        {
            return false;
        }

        int last = viaA <= viaB ? ea : eb;
        length = MathF.Min(viaA, viaB);
        int count = 0;
        for (int n = last; n >= 0; n = _previous[n])
        {
            _stack[count++] = n;
        }

        route.Add(start);
        for (int i = count - 1; i >= 0; i--)
        {
            route.Add(_nodes[_stack[i]]);
        }

        route.Add(end);
        return true;
    }

    /// <summary>
    /// Following <paramref name="route"/> from <paramref name="at"/>: the leg it's on (<paramref name="leg"/>, from point
    /// <c>leg</c> to the next, moved on as it passes each point), the point <paramref name="ahead"/> metres on along the
    /// route from where it is on that leg, and how far that is from the route's end (<paramref name="left"/>). Steering for
    /// that point keeps to the route round corners instead of cutting across them.
    /// </summary>
    public static Vector3 Ahead(List<Vector3> route, ref int leg, Vector3 at, float ahead, out float left)
    {
        left = 0f;
        if (route.Count < 2)
        {
            return route.Count == 1 ? route[0] : at;
        }

        leg = Math.Clamp(leg, 0, route.Count - 2);
        float t;
        while (true)
        {
            Vector3 a = route[leg], ab = (route[leg + 1] - a) with { Y = 0f };
            t = Vector3.Dot((at - a) with { Y = 0f }, ab) / MathF.Max(ab.LengthSquared(), 1e-6f);
            if (t < 1f || leg >= route.Count - 2)
            {
                break;
            }

            leg++;
        }

        Vector3 from = route[leg] + (route[leg + 1] - route[leg]) * Math.Clamp(t, 0f, 1f);
        Vector3 found = route[^1];
        bool have = false;
        Vector3 point = from;
        for (int k = leg + 1; k < route.Count; k++)
        {
            float step = Flat(route[k] - point);
            if (!have && step >= ahead)
            {
                found = point + (route[k] - point) * (ahead / MathF.Max(step, 1e-4f));
                have = true;
            }
            else if (!have)
            {
                ahead -= step;
            }

            left += step;
            point = route[k];
        }

        return found with { Y = 0f };
    }

    private int NearestSegment(Vector3 p, out Vector3 point, out float along)
    {
        int best = -1;
        point = default;
        along = 0f;
        float bestDistance = float.MaxValue;
        for (int s = 0; s < _segments.Count; s++)
        {
            Vector3 q = OnSegment(s, p, out float t);
            float d = Flat(q - p);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = s;
                point = q;
                along = t;
            }
        }

        return best;
    }

    /// <summary>The point of segment <paramref name="s"/> nearest <paramref name="p"/>, and how far along it that is (m).</summary>
    private Vector3 OnSegment(int s, Vector3 p, out float along)
    {
        Vector3 a = _nodes[_segments[s].A], b = _nodes[_segments[s].B];
        Vector3 ab = (b - a) with { Y = 0f };
        float length = ab.Length();
        if (length < 1e-4f)
        {
            along = 0f;
            return a;
        }

        along = Math.Clamp(Vector3.Dot((p - a) with { Y = 0f }, ab / length), 0f, length);
        return a + ab / length * along;
    }

    private int NodeAt(Vector3 point)
    {
        for (int i = 0; i < _nodes.Count; i++)
        {
            if (Flat(_nodes[i] - point) < Join)
            {
                return i;
            }
        }

        return -1;
    }

    private static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);
}
