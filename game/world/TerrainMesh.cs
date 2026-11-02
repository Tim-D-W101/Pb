using System;
using System.Collections.Generic;
using Godot;
using Pb.Sim.Level;

namespace Pb.Game.World;

/// <summary>
/// The rolling ground drawn and walked on (M5.8): the sim's <see cref="Terrain"/> as meshes, in square chunks so each
/// is culled on its own, every triangle exactly the sim's; and as a height map for the engine's collide-and-slide.
/// The level's ground is drawn everywhere; each other kind of cover (a wood's leaf litter, a yard, the brook's mud) is
/// drawn over it on the triangles that touch it, solid where all its corners are its own and ragged toward the others'
/// (<see cref="MaterialLibrary.EdgeVariant"/>), so its edges wander like real ones instead of following the grid.
/// </summary>
public static class TerrainMesh
{
    /// <summary>Grid squares along each side of a chunk.</summary>
    private const int Chunk = 32;

    /// <summary>Builds the chunks under <paramref name="parent"/>; the number of triangles drawn.</summary>
    /// <param name="material">The kit's material at an index.</param>
    /// <param name="edge">Its ragged-edged variant, drawn in front by a step for its order (1 and up) among the covers.</param>
    public static int Build(Node3D parent, Terrain t, Func<int, Material> material, Func<int, int, Material> edge)
    {
        int triangles = 0;
        int squaresX = t.Cols - 1, squaresZ = t.Rows - 1;
        var normals = new Vector3[t.Cols * t.Rows];
        for (int r = 0; r < t.Rows; r++)
        {
            for (int c = 0; c < t.Cols; c++)
            {
                // Smooth shading across the triangles: the slope from the points either side.
                float sx = (t.PointHeight(c + 1, r) - t.PointHeight(c - 1, r)) / (2f * t.Cell);
                float sz = (t.PointHeight(c, r + 1) - t.PointHeight(c, r - 1)) / (2f * t.Cell);
                normals[r * t.Cols + c] = new Vector3(-sx, 1f, -sz).Normalized();
            }
        }

        // One surface for the ground (kind 0), and one for each other kind of cover.
        var surfaces = new Surface[t.Kinds];
        for (int k = 0; k < surfaces.Length; k++)
        {
            surfaces[k] = new Surface(k);
        }

        for (int cz = 0; cz < squaresZ; cz += Chunk)
        {
            for (int cx = 0; cx < squaresX; cx += Chunk)
            {
                foreach (Surface s in surfaces)
                {
                    s.Clear();
                }

                for (int r = cz; r < Math.Min(cz + Chunk, squaresZ); r++)
                {
                    for (int c = cx; c < Math.Min(cx + Chunk, squaresX); c++)
                    {
                        // (c, r) (c+1, r) (c, r+1), then (c+1, r) (c+1, r+1) (c, r+1): split as the sim splits it.
                        Triangle(t, surfaces, normals, c, r, c + 1, r, c, r + 1);
                        Triangle(t, surfaces, normals, c + 1, r, c + 1, r + 1, c, r + 1);
                    }
                }

                var mesh = new ArrayMesh();
                foreach (Surface s in surfaces)
                {
                    if (s.Indices.Count == 0)
                    {
                        continue;
                    }

                    mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, s.Arrays());
                    mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1,
                        s.Kind == 0 ? material(t.MaterialOf(0)) : edge(t.MaterialOf(s.Kind), s.Kind));
                    triangles += s.Indices.Count / 3;
                }

                if (mesh.GetSurfaceCount() > 0)
                {
                    // The ground's light and shade come from its slopes; casting shadows on itself it only striped them.
                    parent.AddChild(new MeshInstance3D
                    {
                        Name = $"Ground_{cx / Chunk}_{cz / Chunk}", Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    });
                }
            }
        }

        return triangles;
    }

    /// <summary>
    /// The water in each stream bed: a strip along its middle line, <see cref="Pb.Game.Core.TerrainViewDef.WaterDepth_m"/>
    /// over the bed's bottom there and in from the foot of its banks, a point a metre along it. Looks only.
    /// </summary>
    public static int Water(Node3D parent, Terrain t, Pb.Game.Core.TerrainViewDef def)
    {
        int strips = 0;
        var material = new StandardMaterial3D
        {
            AlbedoColor = Pb.Game.Core.Conv.ParseColor(def.WaterColor, new Color(0.17f, 0.18f, 0.14f)),
            Roughness = def.WaterRoughness, MetallicSpecular = def.WaterSpecular,
        };
        foreach (Terrain.StreamBed bed in t.Streams)
        {
            float half = MathF.Max(0.1f, bed.Width * 0.5f - def.WaterInset_m);
            var points = new List<Vector2>();
            for (int i = 1; i < bed.Line.Length; i++)
            {
                var a = new Vector2(bed.Line[i - 1].X, bed.Line[i - 1].Y);
                var b = new Vector2(bed.Line[i].X, bed.Line[i].Y);
                int steps = Math.Max(1, (int)MathF.Ceiling(a.DistanceTo(b)));
                for (int k = i == 1 ? 0 : 1; k <= steps; k++)
                {
                    points.Add(a.Lerp(b, k / (float)steps));
                }
            }

            if (points.Count < 2)
            {
                continue;
            }

            var tool = new SurfaceTool();
            tool.Begin(Mesh.PrimitiveType.Triangles);
            Vector3 Edge(int i, float side)
            {
                // Across the line at the point: square to the way it runs there (both ways averaged at a bend).
                Vector2 along = (points[Math.Min(i + 1, points.Count - 1)] - points[Math.Max(i - 1, 0)]).Normalized();
                var across = new Vector2(-along.Y, along.X);
                Vector2 p = points[i] + across * (side * half);
                return new Vector3(p.X, t.HeightAt(points[i].X, points[i].Y) + def.WaterDepth_m, p.Y);
            }

            for (int i = 1; i < points.Count; i++)
            {
                Vector3 a0 = Edge(i - 1, -1f), a1 = Edge(i - 1, 1f), b0 = Edge(i, -1f), b1 = Edge(i, 1f);
                // Both windings' worth of check: the strip's faces point up.
                foreach (Vector3 v in Vector3.Up.Dot((b0 - a0).Cross(a1 - a0)) < 0f ? new[] { a0, b0, a1, a1, b0, b1 } : new[] { a0, a1, b0, a1, b1, b0 })
                {
                    tool.SetNormal(Vector3.Up);
                    tool.AddVertex(v);
                }
            }

            parent.AddChild(new MeshInstance3D
            {
                Name = $"Water_{strips}", Mesh = tool.Commit(), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            strips++;
        }

        return strips;
    }

    /// <summary>The ground as a height map for walking, and where its collision shape goes.</summary>
    public static (HeightMapShape3D Shape, Transform3D At) Collider(Terrain t)
    {
        var heights = new float[t.Cols * t.Rows];
        for (int r = 0; r < t.Rows; r++)
        {
            for (int c = 0; c < t.Cols; c++)
            {
                heights[r * t.Cols + c] = t.PointHeight(c, r);
            }
        }

        var shape = new HeightMapShape3D { MapWidth = t.Cols, MapDepth = t.Rows, MapData = heights };
        // The map is centred on its shape's origin, its points a unit apart.
        var at = new Transform3D(Basis.Identity.Scaled(new Vector3(t.Cell, 1f, t.Cell)),
            new Vector3(t.MinX + (t.Cols - 1) * t.Cell * 0.5f, 0f, t.MinZ + (t.Rows - 1) * t.Cell * 0.5f));
        return (shape, at);
    }

    /// <summary>
    /// A triangle: on the ground's surface unless another cover has it whole, and on each other cover's that has a
    /// corner of it.
    /// </summary>
    private static void Triangle(Terrain t, Surface[] surfaces, Vector3[] normals, int c0, int r0, int c1, int r1, int c2, int r2)
    {
        int k0 = t.KindOf(c0, r0), k1 = t.KindOf(c1, r1), k2 = t.KindOf(c2, r2);
        if (k0 == 0 || k0 != k1 || k0 != k2)
        {
            surfaces[0].Add(t, normals, c0, r0, c1, r1, c2, r2);
        }

        if (k0 != 0)
        {
            surfaces[k0].Add(t, normals, c0, r0, c1, r1, c2, r2);
        }

        if (k1 != 0 && k1 != k0)
        {
            surfaces[k1].Add(t, normals, c0, r0, c1, r1, c2, r2);
        }

        if (k2 != 0 && k2 != k0 && k2 != k1)
        {
            surfaces[k2].Add(t, normals, c0, r0, c1, r1, c2, r2);
        }
    }

    /// <summary>One kind of cover's triangles in a chunk, its grid points shared between them.</summary>
    private sealed class Surface
    {
        private readonly Dictionary<int, int> _index = new();
        private readonly List<Vector3> _vertices = new();
        private readonly List<Vector3> _normals = new();
        private readonly List<Vector2> _uvs = new();
        private readonly List<Color> _colors = new();

        public Surface(int kind)
        {
            Kind = kind;
        }

        public int Kind { get; }

        public List<int> Indices { get; } = new();

        public void Clear()
        {
            _index.Clear();
            _vertices.Clear();
            _normals.Clear();
            _uvs.Clear();
            _colors.Clear();
            Indices.Clear();
        }

        /// <summary>The triangle, clockwise seen from above (Godot's front faces).</summary>
        public void Add(Terrain t, Vector3[] normals, int c0, int r0, int c1, int r1, int c2, int r2)
        {
            Indices.Add(Vertex(t, normals, c0, r0));
            Indices.Add(Vertex(t, normals, c1, r1));
            Indices.Add(Vertex(t, normals, c2, r2));
        }

        private int Vertex(Terrain t, Vector3[] normals, int c, int r)
        {
            int key = r * t.Cols + c;
            if (_index.TryGetValue(key, out int i))
            {
                return i;
            }

            float x = t.MinX + c * t.Cell, z = t.MinZ + r * t.Cell;
            i = _vertices.Count;
            _vertices.Add(new Vector3(x, t.PointHeight(c, r), z));
            _normals.Add(normals[key]);
            _uvs.Add(new Vector2(x, z));
            // A cover's share at its own points is whole, at the others' none (the edge shader's threshold between).
            _colors.Add(new Color(1f, 1f, 1f, Kind == 0 || t.KindOf(c, r) == Kind ? 1f : 0f));
            _index[key] = i;
            return i;
        }

        public Godot.Collections.Array Arrays()
        {
            // Tangents along +x (the way u runs), as the engine's own generator gives them for UV = (x, z).
            var tangents = new float[_vertices.Count * 4];
            for (int i = 0; i < _vertices.Count; i++)
            {
                Vector3 n = _normals[i];
                Vector3 along = (new Vector3(1f, 0f, 0f) - n * n.X).Normalized();
                tangents[i * 4] = along.X;
                tangents[i * 4 + 1] = along.Y;
                tangents[i * 4 + 2] = along.Z;
                tangents[i * 4 + 3] = 1f;
            }

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
            arrays[(int)Mesh.ArrayType.Tangent] = tangents;
            arrays[(int)Mesh.ArrayType.Color] = _colors.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = _uvs.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV2] = new Vector2[_vertices.Count];
            arrays[(int)Mesh.ArrayType.Index] = Indices.ToArray();
            return arrays;
        }
    }
}
