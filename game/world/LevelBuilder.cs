using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Level;
using SVector3 = System.Numerics.Vector3;
using SQuaternion = System.Numerics.Quaternion;

namespace Pb.Game.World;

/// <summary>
/// Builds a compound level from its <see cref="LevelLayout"/>: greybox meshes merged per area chunk
/// and material, walking collision, occluders, per-room ambient light and prop models. Every piece
/// comes from the same primitives the sim collides paint with, so what you see is what you hit.
/// </summary>
public partial class LevelBuilder : Node3D
{
    /// <summary>Meshes are merged per chunk of this size (m) so frustum and occlusion culling still work.</summary>
    private const float ChunkSize = 24f;

    /// <summary>The scrubland around every level is this material.</summary>
    public const string SurroundingsMaterial = "grass_dry";

    /// <summary>What's left of the glass in window frames is this material.</summary>
    public const string GlassMaterial = "glass_dirty";

    /// <summary>Side of the scrubland square around the level (m).</summary>
    private const float SurroundingsSize = 4000f;

    private MaterialLibrary _materials = null!;

    public int MeshCount { get; private set; }

    public int ColliderCount { get; private set; }

    /// <summary>How many props are drawn by shapes built in code (<see cref="PropShapes"/>), and their triangles.</summary>
    public int ShapeCount { get; private set; }

    public int ShapeTriangles { get; private set; }

    /// <summary>How many windows and doors got frames (<see cref="OpeningFrames"/>).</summary>
    public int FramedOpenings { get; private set; }

    /// <summary>How many buildings got gutters, downpipes or roof trusses (<see cref="BuildingDetails"/>).</summary>
    public int DressedBuildings { get; private set; }

    /// <summary>How many wall runs got piers, coping, wire or rubble (<see cref="WallDressing"/>).</summary>
    public int DressedWalls { get; private set; }

    /// <summary>How many inside wall faces got a skirting board (<see cref="Skirting"/>).</summary>
    public int SkirtedFaces { get; private set; }

    /// <summary>How many pylons and poles stand out beyond the level (<see cref="Scenery"/>).</summary>
    public int SceneryCount { get; private set; }

    /// <summary>How many trees stand out beyond the level (<see cref="Woods"/>).</summary>
    public int TreeCount { get; private set; }

    /// <summary>Where rain runs off the buildings' and walls' details down their faces, for <see cref="RunOff"/>.</summary>
    public List<Drip> Drips { get; } = new();

    /// <summary>
    /// Builds the level: its primitives, the props, and the buildings' frames and details from their
    /// templates (<see cref="LevelLayout.Buildings"/>).
    /// </summary>
    public void Build(LevelLayout level, MaterialLibrary materials, bool ambientProbes = true, HorizonDef? horizon = null, WoodsDef? woods = null)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        _materials = materials;
        Drips.Clear();
        MeshCount = 0;
        ColliderCount = 0;
        ShapeTriangles = 0;

        // Props: a generated model if it loads, else the shape built in code, else the colliders as greybox.
        var render = new List<LevelPrimitive>();
        var missingModels = new HashSet<int>();
        var shapes = new Dictionary<(int Cx, int Cz), ShapeMesh>();
        var materialIds = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (KitMaterial m in level.Materials)
        {
            materialIds[m.Id] = m.Index;
        }

        ShapeCount = 0;
        foreach (PropInstance prop in level.Props)
        {
            bool drawn = prop.Type.HasModel && TryPlaceModel(prop);
            if (!drawn && prop.Type.HasShape)
            {
                drawn = TryBuildShape(level, prop, shapes, id => materialIds.TryGetValue(id, out int index) ? index : -1);
            }

            if (!drawn && prop.Type.HasVisual)
            {
                for (int i = prop.FirstPrimitive; i < prop.FirstPrimitive + prop.PrimitiveCount; i++)
                {
                    missingModels.Add(i);
                }
            }
        }

        // The buildings' frames in their openings, gutters and trusses.
        var frames = new Dictionary<int, int>();
        foreach (PlacedBuilding b in level.Buildings)
        {
            if (b.Template.Def.Frames is { } id && materialIds.TryGetValue(id, out int index))
            {
                frames[b.Owner] = index;
            }
        }

        int glass = materialIds.TryGetValue(GlassMaterial, out int g) ? g : -1;
        FramedOpenings = OpeningFrames.Build(level, owner => frames.TryGetValue(owner, out int m) ? m : -1, glass, at => ChunkMesh(shapes, at.X, at.Z));
        DressedBuildings = BuildingDetails.Build(level, id => materialIds.TryGetValue(id, out int index) ? index : -1, at => ChunkMesh(shapes, at.X, at.Z), Drips);
        DressedWalls = WallDressing.Build(level, id => materialIds.TryGetValue(id, out int index) ? index : -1, at => ChunkMesh(shapes, at.X, at.Z), Drips);
        SkirtedFaces = Skirting.Build(level, id => materialIds.TryGetValue(id, out int index) ? index : -1, at => ChunkMesh(shapes, at.X, at.Z));
        SceneryCount = Scenery.Build(level, id => materialIds.TryGetValue(id, out int index) ? index : -1, at => ChunkMesh(shapes, at.X, at.Z));

        foreach (((int cx, int cz), ShapeMesh shape) in shapes)
        {
            var mesh = new ArrayMesh();
            shape.Commit(mesh, m => _materials[m]);
            AddChild(new MeshInstance3D { Name = $"Props_{cx}_{cz}", Mesh = ShapeMesh.WithLods(mesh) });
            ShapeTriangles += shape.TriangleCount;
            MeshCount++;
        }

        for (int i = 0; i < level.Primitives.Count; i++)
        {
            LevelPrimitive p = level.Primitives[i];
            if (p.Has(PrimitiveFlags.Render) || missingModels.Contains(i))
            {
                render.Add(p);
            }
        }

        BuildGround(level);
        if (horizon is not null)
        {
            Pb.Sim.Collision.Aabb b = level.Bounds;
            AddChild(Horizon.Build(new Vector3((b.Min.X + b.Max.X) * 0.5f, 0f, (b.Min.Z + b.Max.Z) * 0.5f), StableHash(level.Id), horizon));
        }

        TreeCount = 0;
        if (woods is not null)
        {
            Pb.Sim.Collision.Aabb b = level.Bounds;
            var trees = new Woods { Name = "Woods" };
            AddChild(trees);
            trees.Build(new Rect2(b.Min.X, b.Min.Z, b.Max.X - b.Min.X, b.Max.Z - b.Min.Z), StableHash(level.Id), woods, level.Scenery);
            TreeCount = trees.TreeCount;
        }

        BuildMeshes(render);
        BuildCollision(level);
        BuildOccluders(level);
        if (ambientProbes)
        {
            BuildAmbientProbes(level);
        }
    }

    private void BuildGround(LevelLayout level)
    {
        Pb.Sim.Collision.Aabb b = level.Bounds;
        var size = new Vector2(b.Max.X - b.Min.X, b.Max.Z - b.Min.Z);
        var center = new Vector3((b.Min.X + b.Max.X) * 0.5f, 0f, (b.Min.Z + b.Max.Z) * 0.5f);

        // The playable ground, plus scrubland reaching well past where fog swallows it (the sky below
        // the horizon is fog-coloured, so there's no visible edge).
        AddChild(new MeshInstance3D
        {
            Name = "Ground",
            Mesh = GroundMesh(size, center),
            MaterialOverride = _materials[level.GroundMaterial.Index],
        });

        int grass = FindMaterial(level, SurroundingsMaterial, level.GroundMaterial.Index);
        AddChild(new MeshInstance3D
        {
            Name = "Surroundings",
            Mesh = GroundMesh(new Vector2(SurroundingsSize, SurroundingsSize), center),
            MaterialOverride = _materials[grass],
            Position = new Vector3(0f, -0.03f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        var body = new StaticBody3D { Name = "GroundBody" };
        body.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D() });
        AddChild(body);
    }

    /// <summary>FNV-1a: a hash of the level id that's the same on every run (string.GetHashCode isn't).</summary>
    internal static int StableHash(string s)
    {
        uint h = 2166136261;
        foreach (char c in s)
        {
            h = (h ^ c) * 16777619;
        }

        return (int)(h & 0x7fffffff);
    }

    private void BuildMeshes(List<LevelPrimitive> primitives)
    {
        var groups = new Dictionary<(int Cx, int Cz, int Material), SurfaceTool>();
        foreach (LevelPrimitive p in primitives)
        {
            var key = ((int)MathF.Floor(p.Center.X / ChunkSize), (int)MathF.Floor(p.Center.Z / ChunkSize), p.Material);
            if (!groups.TryGetValue(key, out SurfaceTool? tool))
            {
                tool = new SurfaceTool();
                tool.Begin(Mesh.PrimitiveType.Triangles);
                groups[key] = tool;
            }

            if (p.Kind == PrimitiveKind.Cylinder)
            {
                AddCylinder(tool, p);
            }
            else
            {
                AddBox(tool, p);
            }
        }

        foreach (((int cx, int cz, int material), SurfaceTool tool) in groups)
        {
            tool.GenerateTangents();
            AddChild(new MeshInstance3D
            {
                Name = $"Mesh_{cx}_{cz}_{material}",
                Mesh = tool.Commit(),
                MaterialOverride = _materials[material],
                CastShadow = _materials.IsTransparent(material)
                    ? GeometryInstance3D.ShadowCastingSetting.Off
                    : GeometryInstance3D.ShadowCastingSetting.On,
            });
            MeshCount++;
        }
    }

    private void BuildCollision(LevelLayout level)
    {
        var body = new StaticBody3D { Name = "LevelBody" };
        foreach (LevelPrimitive p in level.Primitives)
        {
            if (!p.Has(PrimitiveFlags.Walk))
            {
                continue;
            }

            Shape3D shape = p.Kind == PrimitiveKind.Cylinder
                ? new CylinderShape3D { Radius = p.HalfExtents.X, Height = p.HalfExtents.Y * 2f }
                : new BoxShape3D { Size = p.HalfExtents.ToGodot() * 2f };
            body.AddChild(new CollisionShape3D { Shape = shape, Transform = TransformOf(p) });
            ColliderCount++;
        }

        AddChild(body);
    }

    private void BuildOccluders(LevelLayout level)
    {
        foreach (LevelPrimitive p in level.Primitives)
        {
            if (p.Has(PrimitiveFlags.Occluder) && p.Kind == PrimitiveKind.Box)
            {
                AddChild(new OccluderInstance3D
                {
                    Occluder = new BoxOccluder3D { Size = p.HalfExtents.ToGodot() * 2f },
                    Transform = TransformOf(p),
                });
            }
        }
    }

    /// <summary>
    /// Indoor areas get an interior reflection probe whose ambient light follows the area's light
    /// level, so rooms are dim and shafts of sun through windows stand out even without GI.
    /// </summary>
    private void BuildAmbientProbes(LevelLayout level)
    {
        foreach (AreaSpec area in level.Areas)
        {
            if (!area.Indoor)
            {
                continue;
            }

            Pb.Sim.Collision.Aabb b = area.Box;
            Vector3 size = (b.Max - b.Min).ToGodot();
            AddChild(new ReflectionProbe
            {
                Name = "Probe_" + area.Name.Replace(' ', '_').Replace(',', '_'),
                Position = ((b.Min + b.Max) * 0.5f).ToGodot(),
                Size = size + new Vector3(0.6f, 0.4f, 0.6f),
                Interior = true,
                BoxProjection = true,
                UpdateMode = ReflectionProbe.UpdateModeEnum.Once,
                AmbientMode = ReflectionProbe.AmbientModeEnum.Color,
                AmbientColor = new Color(0.62f, 0.6f, 0.57f),
                AmbientColorEnergy = 0.18f + 0.55f * area.Light,
                Intensity = 0.6f,
            });
        }
    }

    private bool TryPlaceModel(PropInstance prop)
    {
        string? path = prop.Type.Def.Model;
        if (ArtFiles.Load<PackedScene>(path) is not { } scene)
        {
            return false;
        }

        Node3D model = scene.Instantiate<Node3D>();
        float[]? offset = prop.Type.Def.ModelOffset_m;
        model.Position = prop.Position.ToGodot() + (offset is { Length: 3 }
            ? new Basis(Vector3.Up, prop.Yaw) * new Vector3(offset[0], offset[1], offset[2])
            : Vector3.Zero);
        model.Rotation = new Vector3(0f, prop.Yaw + Mathf.DegToRad(prop.Type.Def.ModelYaw_deg), 0f);
        model.Scale = Vector3.One * prop.Type.Def.ModelScale;
        AddChild(model);
        return true;
    }

    /// <summary>
    /// Builds a prop's shape into the mesh of the chunk it stands in. An unknown shape is reported and
    /// the prop falls back to greybox.
    /// </summary>
    private bool TryBuildShape(LevelLayout level, PropInstance prop, Dictionary<(int Cx, int Cz), ShapeMesh> shapes, Func<string, int> material)
    {
        string kind = prop.Type.Def.Shape!;
        if (!PropShapes.Has(kind))
        {
            GD.PushError($"kit/props.jsonc: prop '{prop.Type.Id}' has an unknown shape '{kind}' (known: {string.Join(", ", PropShapes.Kinds)}); drawn as greybox");
            return false;
        }

        ShapeMesh mesh = ChunkMesh(shapes, prop.Position.X, prop.Position.Z);
        float top = prop.Position.Y;
        for (int i = prop.FirstPrimitive; i < prop.FirstPrimitive + prop.PrimitiveCount; i++)
        {
            top = MathF.Max(top, level.Primitives[i].Bounds.Max.Y);
        }

        mesh.Place(new Transform3D(new Basis(Vector3.Up, prop.Yaw), prop.Position.ToGodot()), top - prop.Position.Y);
        // The seed comes from where the prop stands, so each one differs but a level looks the same every time.
        int seed = StableHash(prop.Type.Id)
            ^ (int)MathF.Round(prop.Position.X * 10f) * 73856093
            ^ (int)MathF.Round(prop.Position.Y * 10f) * 19349663
            ^ (int)MathF.Round(prop.Position.Z * 10f) * 83492791;
        PropShapes.Build(kind, mesh, prop.Type, seed, material);
        ShapeCount++;
        return true;
    }

    /// <summary>The shape mesh of the chunk holding (<paramref name="x"/>, <paramref name="z"/>).</summary>
    private static ShapeMesh ChunkMesh(Dictionary<(int Cx, int Cz), ShapeMesh> shapes, float x, float z)
    {
        var key = ((int)MathF.Floor(x / ChunkSize), (int)MathF.Floor(z / ChunkSize));
        if (!shapes.TryGetValue(key, out ShapeMesh? mesh))
        {
            mesh = new ShapeMesh();
            shapes[key] = mesh;
        }

        return mesh;
    }

    private static int FindMaterial(LevelLayout level, string id, int fallback)
    {
        foreach (KitMaterial m in level.Materials)
        {
            if (m.Id == id)
            {
                return m.Index;
            }
        }

        return fallback;
    }

    private static Transform3D TransformOf(LevelPrimitive p) => new(new Basis(ToGodot(p.Rotation)), p.Center.ToGodot());

    private static Quaternion ToGodot(SQuaternion q) => new(q.X, q.Y, q.Z, q.W);

    internal static ArrayMesh GroundMesh(Vector2 size, Vector3 center)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        float hx = size.X * 0.5f, hz = size.Y * 0.5f;
        Vector3 a = center + new Vector3(-hx, 0f, -hz);
        Vector3 b = center + new Vector3(hx, 0f, -hz);
        Vector3 c = center + new Vector3(hx, 0f, hz);
        Vector3 d = center + new Vector3(-hx, 0f, hz);
        // Clockwise when seen from above (Godot's front-face winding).
        foreach (Vector3 v in new[] { a, b, c, a, c, d })
        {
            tool.SetNormal(Vector3.Up);
            tool.SetUV(new Vector2(v.X, v.Z));
            tool.SetUV2(new Vector2(0f, 0f));
            tool.AddVertex(v);
        }

        tool.GenerateTangents();
        return tool.Commit();
    }

    /// <summary>
    /// Adds a box with world-space UVs in metres along each face's own axes (so patterns stay
    /// continuous across wall pieces and follow rotated props) and UV2 = (height above the box's
    /// base, box height) for the weathering shader.
    /// </summary>
    private static void AddBox(SurfaceTool tool, LevelPrimitive p)
    {
        Quaternion q = ToGodot(p.Rotation);
        var basis = new Basis(q);
        Vector3 ax = basis.X, ay = basis.Y, az = basis.Z;
        Vector3 c = p.Center.ToGodot();
        Vector3 h = p.HalfExtents.ToGodot();
        Pb.Sim.Collision.Aabb bounds = p.Bounds;
        float baseY = bounds.Min.Y;
        float height = bounds.Max.Y - bounds.Min.Y;

        // (normal, A, B, extent along normal, extent along A, extent along B) with A × B = normal.
        Face(tool, c, ax, -az, ay, h.X, h.Z, h.Y, baseY, height);
        Face(tool, c, -ax, az, ay, h.X, h.Z, h.Y, baseY, height);
        Face(tool, c, az, ax, ay, h.Z, h.X, h.Y, baseY, height);
        Face(tool, c, -az, -ax, ay, h.Z, h.X, h.Y, baseY, height);
        Face(tool, c, ay, ax, -az, h.Y, h.X, h.Z, baseY, height);
        Face(tool, c, -ay, ax, az, h.Y, h.X, h.Z, baseY, height);
    }

    private static void Face(SurfaceTool tool, Vector3 center, Vector3 normal, Vector3 a, Vector3 b,
        float extentN, float extentA, float extentB, float baseY, float height)
    {
        Vector3 fc = center + normal * extentN;
        Vector3 p0 = fc - a * extentA - b * extentB;
        Vector3 p1 = fc + a * extentA - b * extentB;
        Vector3 p2 = fc + a * extentA + b * extentB;
        Vector3 p3 = fc - a * extentA + b * extentB;
        // With A × B = normal, p0→p1→p2 is counter-clockwise seen from outside; Godot wants clockwise.
        Vertex(tool, p0, normal, a, b, baseY, height);
        Vertex(tool, p2, normal, a, b, baseY, height);
        Vertex(tool, p1, normal, a, b, baseY, height);
        Vertex(tool, p0, normal, a, b, baseY, height);
        Vertex(tool, p3, normal, a, b, baseY, height);
        Vertex(tool, p2, normal, a, b, baseY, height);
    }

    private static void Vertex(SurfaceTool tool, Vector3 position, Vector3 normal, Vector3 a, Vector3 b, float baseY, float height)
    {
        tool.SetNormal(normal);
        tool.SetUV(new Vector2(position.Dot(a), position.Dot(b)));
        tool.SetUV2(new Vector2(position.Y - baseY, height));
        tool.AddVertex(position);
    }

    private static void AddCylinder(SurfaceTool tool, LevelPrimitive p)
    {
        var basis = new Basis(ToGodot(p.Rotation));
        Vector3 axis = basis.Y;
        Vector3 u = basis.X, w = basis.Z;
        Vector3 c = p.Center.ToGodot();
        float r = p.HalfExtents.X;
        float hh = p.HalfExtents.Y;
        int segments = Math.Clamp((int)(r * 40f), 12, 32);
        Pb.Sim.Collision.Aabb bounds = p.Bounds;
        float baseY = bounds.Min.Y;
        float height = bounds.Max.Y - bounds.Min.Y;

        void V(Vector3 pos, Vector3 n, Vector2 uv)
        {
            tool.SetNormal(n);
            tool.SetUV(uv);
            tool.SetUV2(new Vector2(pos.Y - baseY, height));
            tool.AddVertex(pos);
        }

        for (int i = 0; i < segments; i++)
        {
            float t0 = Mathf.Tau * i / segments, t1 = Mathf.Tau * (i + 1) / segments;
            Vector3 d0 = u * Mathf.Cos(t0) + w * Mathf.Sin(t0);
            Vector3 d1 = u * Mathf.Cos(t1) + w * Mathf.Sin(t1);
            Vector3 b0 = c + d0 * r - axis * hh, b1 = c + d1 * r - axis * hh;
            Vector3 top0 = c + d0 * r + axis * hh, top1 = c + d1 * r + axis * hh;
            float s0 = t0 * r, s1 = t1 * r;
            // Side quad (clockwise from outside), then the two cap triangles.
            V(b0, d0, new Vector2(s0, -hh)); V(b1, d1, new Vector2(s1, -hh)); V(top1, d1, new Vector2(s1, hh));
            V(b0, d0, new Vector2(s0, -hh)); V(top1, d1, new Vector2(s1, hh)); V(top0, d0, new Vector2(s0, hh));

            Vector3 tc = c + axis * hh, bc = c - axis * hh;
            V(tc, axis, Vector2.Zero); V(top0, axis, new Vector2(d0.Dot(u), d0.Dot(w)) * r); V(top1, axis, new Vector2(d1.Dot(u), d1.Dot(w)) * r);
            V(bc, -axis, Vector2.Zero); V(b1, -axis, new Vector2(d1.Dot(u), d1.Dot(w)) * r); V(b0, -axis, new Vector2(d0.Dot(u), d0.Dot(w)) * r);
        }
    }
}
