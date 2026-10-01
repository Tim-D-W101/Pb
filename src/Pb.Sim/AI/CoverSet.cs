using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Level;

namespace Pb.Sim.AI;

public enum CoverHeight : byte
{
    /// <summary>Hides a crouching body; you shoot over the top standing up.</summary>
    Half,

    /// <summary>Hides a standing body; you shoot round its edge.</summary>
    Full,
}

/// <summary>
/// One place to take cover, generated from the level geometry: just behind a wall end, a door or
/// window frame, a low wall or a prop. Threats on the far side of the cover (toward −<see cref="Normal"/>)
/// can't see whoever stands here; <see cref="PeekDirection"/> is the way to step to shoot round the edge.
/// </summary>
public readonly struct CoverPoint
{
    public CoverPoint(Vector3 position, Vector3 normal, Vector3 peekDirection, CoverHeight height, bool twoSided)
    {
        Position = position;
        Normal = normal;
        PeekDirection = peekDirection;
        Height = height;
        TwoSided = twoSided;
    }

    /// <summary>Where to stand (on the navigation grid, at floor height).</summary>
    public Vector3 Position { get; }

    /// <summary>Horizontal unit vector from the cover's face out toward the spot.</summary>
    public Vector3 Normal { get; }

    /// <summary>Horizontal unit vector to step along to see past the edge; zero when there's no edge to use.</summary>
    public Vector3 PeekDirection { get; }

    public CoverHeight Height { get; }

    /// <summary>Narrow cover (columns, drums) that can be peeked round either side.</summary>
    public bool TwoSided { get; }

    public bool HasEdge => PeekDirection != Vector3.Zero;

    /// <summary>Somewhere to shoot from: over low cover, or round an edge.</summary>
    public bool CanShoot => Height == CoverHeight.Half || HasEdge;
}

/// <summary>
/// Every cover point in a level, generated once at load from the primitives flagged as cover, kept
/// only where a bot can actually stand. Bots query the points near them and claim one at a time, so
/// two don't pile into the same spot.
/// </summary>
public sealed class CoverSet
{
    /// <summary>How far in from a corner to stand, so the body is behind the edge rather than past it.</summary>
    private const float CornerInset = 0.35f;

    /// <summary>Gap between the face and the bot's body.</summary>
    private const float Standoff = 0.12f;

    /// <summary>Spacing of over-the-top points along low walls and hide points along tall ones.</summary>
    private const float AlongSpacing = 2.5f;

    /// <summary>How far to step out sideways to shoot round an edge.</summary>
    public const float PeekStep = 0.7f;

    /// <summary>Extra height over the eyes that cover needs to hide the head (the mask's top).</summary>
    private const float HeadMargin = 0.1f;

    private const float BucketSize = 4f;

    private readonly List<CoverPoint> _points = new();
    private float _standHead;
    private float _crouchHead;
    private readonly Dictionary<long, List<int>> _buckets = new();
    private readonly Dictionary<int, int> _claims = new();

    public IReadOnlyList<CoverPoint> Points => _points;

    /// <summary>
    /// Generates the points. Cover counts as full where it hides a standing head
    /// (<paramref name="standEyeHeight"/>), as half where it hides a crouched one, and not at all lower
    /// than that: a waist-high sill shields a crouching body but leaves the mask showing.
    /// </summary>
    public static CoverSet Build(LevelLayout level, NavGrid grid, float standEyeHeight, float crouchEyeHeight)
    {
        var set = new CoverSet { _standHead = standEyeHeight + HeadMargin, _crouchHead = crouchEyeHeight + HeadMargin };
        float radius = grid.Params.AgentRadius + Standoff;
        foreach (LevelPrimitive prim in level.Primitives)
        {
            if (!prim.Has(PrimitiveFlags.Cover))
            {
                continue;
            }

            Aabb bounds = prim.Bounds;
            if (prim.Kind == PrimitiveKind.Cylinder)
            {
                set.AddCylinder(grid, prim, bounds, radius);
            }
            else
            {
                set.AddBox(grid, prim, bounds, radius);
            }
        }

        set.Index();
        return set;
    }

    /// <summary>Indices of the points within <paramref name="radius"/> of <paramref name="position"/> (horizontally, within 3 m of height).</summary>
    public void Near(Vector3 position, float radius, List<int> found)
    {
        found.Clear();
        int b0x = (int)MathF.Floor((position.X - radius) / BucketSize);
        int b1x = (int)MathF.Floor((position.X + radius) / BucketSize);
        int b0z = (int)MathF.Floor((position.Z - radius) / BucketSize);
        int b1z = (int)MathF.Floor((position.Z + radius) / BucketSize);
        float r2 = radius * radius;
        for (int bz = b0z; bz <= b1z; bz++)
        {
            for (int bx = b0x; bx <= b1x; bx++)
            {
                if (!_buckets.TryGetValue(Key(bx, bz), out List<int>? items))
                {
                    continue;
                }

                foreach (int i in items)
                {
                    Vector3 d = _points[i].Position - position;
                    if (d.X * d.X + d.Z * d.Z <= r2 && MathF.Abs(d.Y) <= 3f)
                    {
                        found.Add(i);
                    }
                }
            }
        }
    }

    /// <summary>Who holds point <paramref name="index"/> (−1 = nobody).</summary>
    public int ClaimedBy(int index) => _claims.TryGetValue(index, out int who) ? who : -1;

    /// <summary>Takes point <paramref name="index"/> for <paramref name="who"/>, releasing their previous one. False if someone else holds it.</summary>
    public bool Claim(int index, int who)
    {
        int holder = ClaimedBy(index);
        if (holder != -1 && holder != who)
        {
            return false;
        }

        Release(who);
        _claims[index] = who;
        return true;
    }

    public void Release(int who)
    {
        foreach ((int index, int holder) in _claims)
        {
            if (holder == who)
            {
                _claims.Remove(index);
                return;
            }
        }
    }

    public void ReleaseAll() => _claims.Clear();

    private void AddBox(NavGrid grid, LevelPrimitive prim, Aabb bounds, float radius)
    {
        Vector3 axisX = Vector3.Transform(Vector3.UnitX, prim.Rotation);
        Vector3 axisY = Vector3.Transform(Vector3.UnitY, prim.Rotation);
        Vector3 axisZ = Vector3.Transform(Vector3.UnitZ, prim.Rotation);
        if (MathF.Abs(axisY.Y) < 0.95f)
        {
            return; // tilted: not something to stand behind
        }

        Vector3 flatX = Flat(axisX);
        Vector3 flatZ = Flat(axisZ);
        var centre = new Vector3(prim.Center.X, bounds.Min.Y, prim.Center.Z);
        Vector3 half = prim.HalfExtents;
        // Faces ±Z run along X, faces ±X run along Z.
        float top = bounds.Max.Y;
        AddFace(grid, centre + flatZ * half.Z, flatZ, flatX, half.X, top, radius);
        AddFace(grid, centre - flatZ * half.Z, -flatZ, flatX, half.X, top, radius);
        AddFace(grid, centre + flatX * half.X, flatX, flatZ, half.Z, top, radius);
        AddFace(grid, centre - flatX * half.X, -flatX, flatZ, half.Z, top, radius);
    }

    /// <summary>
    /// Points along one vertical face: a corner point at each end (peek round the edge if there's room
    /// to step out), and points along the face (over the top for low cover, hiding for tall).
    /// </summary>
    private void AddFace(NavGrid grid, Vector3 faceCentre, Vector3 normal, Vector3 tangent, float halfLength, float top, float radius)
    {
        if (halfLength * 2f < 0.6f)
        {
            return; // an end face (a wall's thickness): its corners come from the long faces
        }

        Vector3 standLine = faceCentre + normal * radius;
        foreach (float side in new[] { 1f, -1f })
        {
            float along = MathF.Max(0f, halfLength - CornerInset);
            Vector3 at = standLine + tangent * (side * along);
            Vector3 peek = tangent * side;
            // Room to step out past the edge? (A corner against another wall has none.)
            Vector3 stepOut = standLine + tangent * (side * (halfLength + PeekStep * 0.6f));
            bool canStep = grid.SpanAt(stepOut) >= 0;
            AddPoint(grid, at, normal, canStep ? peek : Vector3.Zero, top, false);
        }

        int inner = (int)MathF.Floor((halfLength * 2f - 2f * CornerInset) / AlongSpacing);
        for (int i = 1; i <= inner; i++)
        {
            float t = -halfLength + CornerInset + i * ((halfLength * 2f - 2f * CornerInset) / (inner + 1));
            AddPoint(grid, standLine + tangent * t, normal, Vector3.Zero, top, false);
        }
    }

    private void AddCylinder(NavGrid grid, LevelPrimitive prim, Aabb bounds, float radius)
    {
        Vector3 axis = Vector3.Transform(Vector3.UnitY, prim.Rotation);
        float r = prim.HalfExtents.X;
        if (MathF.Abs(axis.Y) < 0.95f || r < 0.15f)
        {
            return; // lying down, or too thin to hide behind
        }

        var centre = new Vector3(prim.Center.X, bounds.Min.Y, prim.Center.Z);
        for (int k = 0; k < 4; k++)
        {
            float angle = k * MathF.PI * 0.5f;
            var normal = new Vector3(MathF.Sin(angle), 0f, MathF.Cos(angle));
            var tangent = new Vector3(normal.Z, 0f, -normal.X);
            AddPoint(grid, centre + normal * (r + radius), normal, tangent, bounds.Max.Y, true);
        }
    }

    private void AddPoint(NavGrid grid, Vector3 at, Vector3 normal, Vector3 peek, float top, bool twoSided)
    {
        int span = grid.SpanAt(at);
        if (span < 0)
        {
            return;
        }

        Vector3 standing = grid.PositionOf(span);
        if (MathF.Abs(standing.Y - at.Y) > 0.4f)
        {
            return; // a different floor
        }

        float above = top - standing.Y;
        if (above < _crouchHead)
        {
            return; // too low to hide a crouched head
        }

        CoverHeight kind = above >= _standHead ? CoverHeight.Full : CoverHeight.Half;

        // One point per spot: a near twin facing the same way is the same cover.
        foreach (CoverPoint p in _points)
        {
            if (Vector3.DistanceSquared(p.Position, standing) < 0.16f && Vector3.Dot(p.Normal, normal) > 0.5f)
            {
                return;
            }
        }

        _points.Add(new CoverPoint(standing, normal, peek, kind, twoSided));
    }

    private void Index()
    {
        for (int i = 0; i < _points.Count; i++)
        {
            long key = Key((int)MathF.Floor(_points[i].Position.X / BucketSize), (int)MathF.Floor(_points[i].Position.Z / BucketSize));
            if (!_buckets.TryGetValue(key, out List<int>? list))
            {
                _buckets[key] = list = new List<int>();
            }

            list.Add(i);
        }
    }

    private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

    private static Vector3 Flat(Vector3 v)
    {
        v.Y = 0f;
        float length = v.Length();
        return length > 1e-5f ? v / length : Vector3.UnitX;
    }
}
