using System;
using System.Collections.Generic;
using Godot;

namespace Pb.Game.World;

/// <summary>
/// Collects the triangles of procedural prop shapes (<see cref="PropShapes"/>) per material, in
/// world space, and turns each material's triangles into one mesh surface. It follows the kit
/// shader's mesh contract (weathered.gdshader): UV in metres along each face (v up on walls),
/// UV2 = (height above the prop's base, prop height), so photographs tile at their real size and
/// grime gathers at the foot of each prop. Parts are given in the prop's frame and placed with
/// <see cref="Place"/>; triangles are wound clockwise seen from outside, as Godot wants.
/// </summary>
public sealed class ShapeMesh
{
    private readonly Dictionary<int, Surface> _surfaces = new();
    private Transform3D _place = Transform3D.Identity;
    private float _baseY;
    private float _height = 1f;

    public int TriangleCount { get; private set; }

    public IEnumerable<int> Materials => _surfaces.Keys;

    /// <summary>Where the next parts go: the prop's transform, and its height for the weathering ramp.</summary>
    public void Place(Transform3D prop, float height)
    {
        _place = prop;
        _baseY = 0f;
        _height = MathF.Max(height, 0.05f);
    }

    /// <summary>A box of <paramref name="size"/> centred on <paramref name="center"/>, turned by <paramref name="rotation"/>.</summary>
    public void Box(int material, Vector3 center, Vector3 size, Basis rotation, Vector2 uvOffset = default)
    {
        Vector3 h = size * 0.5f;
        Vector3 ax = rotation.X, ay = rotation.Y, az = rotation.Z;
        // (normal, A, B) with A × B = normal; B runs up the sides, as on the level's walls.
        Face(material, center, ax, -az, ay, h.X, h.Z, h.Y, uvOffset);
        Face(material, center, -ax, az, ay, h.X, h.Z, h.Y, uvOffset);
        Face(material, center, az, ax, ay, h.Z, h.X, h.Y, uvOffset);
        Face(material, center, -az, -ax, ay, h.Z, h.X, h.Y, uvOffset);
        Face(material, center, ay, ax, -az, h.Y, h.X, h.Z, uvOffset);
        Face(material, center, -ay, ax, az, h.Y, h.X, h.Z, uvOffset);
    }

    public void Box(int material, Vector3 center, Vector3 size) => Box(material, center, size, Basis.Identity);

    /// <summary>
    /// A solid of revolution about the +Y axis of <paramref name="rotation"/> through <paramref name="center"/>:
    /// <paramref name="profile"/> holds (radius, height) points, walked so the surface faces outwards
    /// (bottom to top for an outer wall). Normals are smooth around the axis and across the profile
    /// unless a point repeats, which makes a crease.
    /// </summary>
    public void Lathe(int material, Vector3 center, Basis rotation, IReadOnlyList<Vector2> profile, int segments, float arcStart = 0f, float arcEnd = Mathf.Tau)
    {
        int n = profile.Count;
        if (n < 2)
        {
            return;
        }

        // Profile normals: perpendicular to the neighbouring segments, averaged except at creases.
        var normals = new Vector2[n];
        var lengths = new float[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 prev = i > 0 ? profile[i] - profile[i - 1] : Vector2.Zero;
            Vector2 next = i < n - 1 ? profile[i + 1] - profile[i] : Vector2.Zero;
            Vector2 np = prev.LengthSquared() > 1e-10f ? new Vector2(prev.Y, -prev.X).Normalized() : Vector2.Zero;
            Vector2 nn = next.LengthSquared() > 1e-10f ? new Vector2(next.Y, -next.X).Normalized() : Vector2.Zero;
            Vector2 sum = np + nn;
            normals[i] = sum.LengthSquared() > 1e-10f ? sum.Normalized() : np.LengthSquared() > 0f ? np : nn;
            lengths[i] = i == 0 ? 0f : lengths[i - 1] + prev.Length();
        }

        for (int s = 0; s < segments; s++)
        {
            float t0 = Mathf.Lerp(arcStart, arcEnd, (float)s / segments);
            float t1 = Mathf.Lerp(arcStart, arcEnd, (float)(s + 1) / segments);
            Vector3 d0 = new(Mathf.Cos(t0), 0f, Mathf.Sin(t0));
            Vector3 d1 = new(Mathf.Cos(t1), 0f, Mathf.Sin(t1));
            for (int i = 0; i < n - 1; i++)
            {
                Vector2 a = profile[i], b = profile[i + 1];
                if ((b - a).LengthSquared() < 1e-10f)
                {
                    continue; // a crease
                }

                // Normals at the two ends of this segment: the crease-aware ones unless the
                // neighbour is a crease, in which case this segment's own.
                Vector2 own = new Vector2(b.Y - a.Y, -(b.X - a.X)).Normalized();
                Vector2 na = i > 0 && (a - profile[i - 1]).LengthSquared() < 1e-10f ? own : normals[i];
                Vector2 nb = i + 1 < n - 1 && (profile[i + 2] - b).LengthSquared() < 1e-10f ? own : normals[i + 1];
                float r = (a.X + b.X) * 0.5f;
                float u0 = t0 * MathF.Max(r, 0.05f), u1 = t1 * MathF.Max(r, 0.05f);
                LatheVertex lv00 = new(d0, a, na, new Vector2(u0, lengths[i]));
                LatheVertex lv01 = new(d1, a, na, new Vector2(u1, lengths[i]));
                LatheVertex lv10 = new(d0, b, nb, new Vector2(u0, lengths[i + 1]));
                LatheVertex lv11 = new(d1, b, nb, new Vector2(u1, lengths[i + 1]));
                LatheQuad(material, center, rotation, lv00, lv01, lv11, lv10);
            }
        }
    }

    /// <summary>A capped cylinder along the +Y axis of <paramref name="rotation"/>.</summary>
    public void Cylinder(int material, Vector3 center, Basis rotation, float radius, float height, int segments, bool caps = true)
    {
        float h = height * 0.5f;
        var profile = caps
            ? new[] { new Vector2(0f, -h), new Vector2(radius, -h), new Vector2(radius, -h), new Vector2(radius, h), new Vector2(radius, h), new Vector2(0f, h) }
            : new[] { new Vector2(radius, -h), new Vector2(radius, h) };
        Lathe(material, center, rotation, profile, segments);
    }

    /// <summary>A rod from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public void Rod(int material, Vector3 from, Vector3 to, float radius, int segments = 6, bool caps = true)
    {
        Vector3 axis = to - from;
        float length = axis.Length();
        if (length < 1e-4f)
        {
            return;
        }

        Cylinder(material, (from + to) * 0.5f, BasisAlong(axis / length), radius, length, segments, caps);
    }

    /// <summary>A beam of square section from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public void Bar(int material, Vector3 from, Vector3 to, float width, float depth)
    {
        Vector3 axis = to - from;
        float length = axis.Length();
        if (length < 1e-4f)
        {
            return;
        }

        Box(material, (from + to) * 0.5f, new Vector3(width, length, depth), BasisAlong(axis / length));
    }

    /// <summary>
    /// A prism: <paramref name="outline"/> (x, y) in the plane of the basis' X and Y, extruded along
    /// its Z by <paramref name="length"/>, centred on <paramref name="center"/>. The outline is a
    /// simple polygon running counter-clockwise; it may be concave (a car's side with its wheel arches).
    /// </summary>
    public void Extrude(int material, Vector3 center, Basis rotation, IReadOnlyList<Vector2> outline, float length)
    {
        int n = outline.Count;
        float hz = length * 0.5f;
        Vector3 Local(Vector2 p, float z) => center + rotation * new Vector3(p.X, p.Y, z);

        // Sides: one quad per edge, flat-shaded; u runs along the extrusion, v around the outline.
        float run = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = outline[i], b = outline[(i + 1) % n];
            Vector2 e = b - a;
            float len = e.Length();
            if (len < 1e-6f)
            {
                continue;
            }

            Vector3 normal = rotation * new Vector3(e.Y, -e.X, 0f).Normalized();
            Vector3 pa0 = Local(a, -hz), pb0 = Local(b, -hz), pb1 = Local(b, hz), pa1 = Local(a, hz);
            // Counter-clockwise outline: the outward side faces right of a → b; wind clockwise from outside.
            Quad(material, pa0, pa1, pb1, pb0, normal,
                new Vector2(-hz, run), new Vector2(hz, run), new Vector2(hz, run + len), new Vector2(-hz, run + len));
            run += len;
        }

        // Caps, triangulated by ear clipping; the outline's own coordinates are their UVs.
        Vector3 back = rotation * Vector3.Back, front = rotation * Vector3.Forward;
        List<int> triangles = Triangulate(outline);
        for (int i = 0; i < triangles.Count; i += 3)
        {
            Vector2 a = outline[triangles[i]], b = outline[triangles[i + 1]], c = outline[triangles[i + 2]];
            Tri(material, Local(a, hz), Local(c, hz), Local(b, hz), back, a, c, b);
            Tri(material, Local(a, -hz), Local(b, -hz), Local(c, -hz), front, new Vector2(-a.X, a.Y), new Vector2(-b.X, b.Y), new Vector2(-c.X, c.Y));
        }
    }

    /// <summary>Ear clipping: counter-clockwise triangles (index triples) covering a simple counter-clockwise polygon.</summary>
    public static List<int> Triangulate(IReadOnlyList<Vector2> polygon)
    {
        var result = new List<int>();
        var remaining = new List<int>();
        for (int i = 0; i < polygon.Count; i++)
        {
            remaining.Add(i);
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        int guard = polygon.Count * polygon.Count;
        while (remaining.Count > 3 && guard-- > 0)
        {
            bool clipped = false;
            for (int k = 0; k < remaining.Count; k++)
            {
                int ia = remaining[(k + remaining.Count - 1) % remaining.Count], ib = remaining[k], ic = remaining[(k + 1) % remaining.Count];
                Vector2 a = polygon[ia], b = polygon[ib], c = polygon[ic];
                if (Cross(a, b, c) <= 1e-9f)
                {
                    continue; // a reflex (or flat) corner can't be an ear
                }

                bool inside = false;
                foreach (int j in remaining)
                {
                    if (j == ia || j == ib || j == ic)
                    {
                        continue;
                    }

                    Vector2 p = polygon[j];
                    if (Cross(a, b, p) >= 0f && Cross(b, c, p) >= 0f && Cross(c, a, p) >= 0f)
                    {
                        inside = true;
                        break;
                    }
                }

                if (!inside)
                {
                    result.Add(ia);
                    result.Add(ib);
                    result.Add(ic);
                    remaining.RemoveAt(k);
                    clipped = true;
                    break;
                }
            }

            if (!clipped)
            {
                break; // not a simple polygon: give up on the rest rather than loop forever
            }
        }

        if (remaining.Count == 3)
        {
            result.Add(remaining[0]);
            result.Add(remaining[1]);
            result.Add(remaining[2]);
        }

        return result;
    }

    /// <summary>
    /// A rounded box (a superellipsoid): <paramref name="size"/> across, corners rounded by
    /// <paramref name="squareness"/> (2 is an ellipsoid, higher is boxier). Sandbags, cushions.
    /// </summary>
    public void Pillow(int material, Vector3 center, Vector3 size, Basis rotation, float squareness, int rings = 6, int segments = 10)
    {
        Vector3 h = size * 0.5f;
        float e = 2f / squareness;
        Vector3 Point(float lat, float lon, out Vector3 normal)
        {
            float cl = Mathf.Cos(lat), sl = Mathf.Sin(lat), co = Mathf.Cos(lon), so = Mathf.Sin(lon);
            float SPow(float v, float p) => MathF.Sign(v) * MathF.Pow(MathF.Abs(v), p);
            var unit = new Vector3(SPow(cl, e) * SPow(co, e), SPow(sl, e), SPow(cl, e) * SPow(so, e));
            // The surface's normal: the superellipsoid's gradient, scaled back by its size.
            float g = 2f - e;
            var grad = new Vector3(SPow(cl, g) * SPow(co, g) / h.X, SPow(sl, g) / h.Y, SPow(cl, g) * SPow(so, g) / h.Z);
            normal = rotation * (grad.LengthSquared() > 1e-12f ? grad.Normalized() : Vector3.Up);
            return center + rotation * (unit * h);
        }

        for (int r = 0; r < rings; r++)
        {
            float lat0 = Mathf.Lerp(-Mathf.Pi / 2f, Mathf.Pi / 2f, (float)r / rings);
            float lat1 = Mathf.Lerp(-Mathf.Pi / 2f, Mathf.Pi / 2f, (float)(r + 1) / rings);
            for (int s = 0; s < segments; s++)
            {
                float lon0 = Mathf.Tau * s / segments, lon1 = Mathf.Tau * (s + 1) / segments;
                Vector3 p00 = Point(lat0, lon0, out Vector3 n00), p01 = Point(lat0, lon1, out Vector3 n01);
                Vector3 p10 = Point(lat1, lon0, out Vector3 n10), p11 = Point(lat1, lon1, out Vector3 n11);
                float circ = (h.X + h.Z) * 2f;
                Vector2 uv(float lat, float lon) => new(lon / Mathf.Tau * circ, (lat / Mathf.Pi + 0.5f) * size.Y * 1.6f);
                // As on a lathe: longitude grows from +X towards +Z, latitude bottom to top, so
                // (lon0, lat0) → (lon1, lat0) → (lon1, lat1) is clockwise seen from outside.
                AddWorld(material, p00, n00, uv(lat0, lon0));
                AddWorld(material, p01, n01, uv(lat0, lon1));
                AddWorld(material, p11, n11, uv(lat1, lon1));
                AddWorld(material, p00, n00, uv(lat0, lon0));
                AddWorld(material, p11, n11, uv(lat1, lon1));
                AddWorld(material, p10, n10, uv(lat1, lon0));
                TriangleCount += 2;
            }
        }
    }

    /// <summary>A flat quad; corners clockwise seen from the side the normal points to.</summary>
    public void Quad(int material, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
    {
        Tri(material, a, b, c, normal, ua, ub, uc);
        Tri(material, a, c, d, normal, ua, uc, ud);
    }

    /// <summary>One triangle in the prop's frame, clockwise seen from the side the normal points to.</summary>
    public void Tri(int material, Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc)
    {
        Add(material, a, normal, ua);
        Add(material, b, normal, ub);
        Add(material, c, normal, uc);
        TriangleCount++;
    }

    /// <summary>One triangle with a normal per corner (smooth shading), clockwise seen from outside.</summary>
    public void Smooth(int material, Vector3 a, Vector3 na, Vector2 ua, Vector3 b, Vector3 nb, Vector2 ub, Vector3 c, Vector3 nc, Vector2 uc)
    {
        Add(material, a, na, ua);
        Add(material, b, nb, ub);
        Add(material, c, nc, uc);
        TriangleCount++;
    }

    /// <summary>A basis whose +Y runs along <paramref name="axis"/> (a unit vector).</summary>
    public static Basis BasisAlong(Vector3 axis)
    {
        Vector3 helper = MathF.Abs(axis.Y) < 0.95f ? Vector3.Up : Vector3.Right;
        Vector3 x = helper.Cross(axis).Normalized();
        Vector3 z = x.Cross(axis).Normalized();
        return new Basis(x, axis, z);
    }

    /// <summary>Adds every surface to <paramref name="mesh"/> with the given materials, tangents generated.</summary>
    public void Commit(ArrayMesh mesh, Func<int, Material> materials)
    {
        foreach ((int material, Surface s) in _surfaces)
        {
            if (s.Positions.Count == 0)
            {
                continue;
            }

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = s.Positions.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = s.Normals.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = s.Uvs.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV2] = s.Uv2s.ToArray();
            var tool = new SurfaceTool();
            tool.CreateFromArrays(arrays);
            tool.GenerateTangents();
            tool.SetMaterial(materials(material));
            tool.Commit(mesh);
        }
    }

    public void Clear()
    {
        _surfaces.Clear();
        TriangleCount = 0;
    }

    private readonly record struct LatheVertex(Vector3 Direction, Vector2 Point, Vector2 Normal, Vector2 Uv);

    private void LatheQuad(int material, Vector3 center, Basis rotation, LatheVertex a, LatheVertex b, LatheVertex c, LatheVertex d)
    {
        // a = (θ0, p0), b = (θ1, p0), c = (θ1, p1), d = (θ0, p1): with the profile walked bottom to top
        // and θ growing from +X towards +Z, a → b → c is clockwise seen from outside.
        void V(LatheVertex v)
        {
            Vector3 local = new(v.Direction.X * v.Point.X, v.Point.Y, v.Direction.Z * v.Point.X);
            Vector3 n = new(v.Direction.X * v.Normal.X, v.Normal.Y, v.Direction.Z * v.Normal.X);
            Add(material, center + rotation * local, (rotation * n).Normalized(), v.Uv);
        }

        V(a);
        V(b);
        V(c);
        V(a);
        V(c);
        V(d);
        TriangleCount += 2;
    }

    private void Face(int material, Vector3 center, Vector3 normal, Vector3 a, Vector3 b, float extentN, float extentA, float extentB, Vector2 uvOffset)
    {
        Vector3 fc = center + normal * extentN;
        Vector3 p0 = fc - a * extentA - b * extentB;
        Vector3 p1 = fc + a * extentA - b * extentB;
        Vector3 p2 = fc + a * extentA + b * extentB;
        Vector3 p3 = fc - a * extentA + b * extentB;
        // UVs in metres from the face's own corner, plus an offset (to pick a plank on a texture).
        var u0 = new Vector2(-extentA, -extentB) + uvOffset;
        var u1 = new Vector2(extentA, -extentB) + uvOffset;
        var u2 = new Vector2(extentA, extentB) + uvOffset;
        var u3 = new Vector2(-extentA, extentB) + uvOffset;
        // With A × B = normal, p0 → p1 → p2 is counter-clockwise from outside; Godot wants clockwise.
        Tri(material, p0, p2, p1, normal, u0, u2, u1);
        Tri(material, p0, p3, p2, normal, u0, u3, u2);
    }

    private void Add(int material, Vector3 local, Vector3 normal, Vector2 uv) =>
        AddWorld(material, local, normal, uv);

    private void AddWorld(int material, Vector3 local, Vector3 normal, Vector2 uv)
    {
        if (!_surfaces.TryGetValue(material, out Surface? s))
        {
            s = new Surface();
            _surfaces[material] = s;
        }

        s.Positions.Add(_place * local);
        s.Normals.Add((_place.Basis * normal).Normalized());
        s.Uvs.Add(uv);
        s.Uv2s.Add(new Vector2(MathF.Max(local.Y - _baseY, 0f), _height));
    }

    private sealed class Surface
    {
        public readonly List<Vector3> Positions = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Vector2> Uvs = new();
        public readonly List<Vector2> Uv2s = new();
    }
}
