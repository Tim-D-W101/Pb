using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Collision;
using Pb.Sim.Level;
using Pb.Sim.Range;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.World;

/// <summary>
/// Builds the training ground (the Phase 1 range) from its layout data, dressed with the level kit
/// (presentation.jsonc "trainingGround"):
/// <list type="bullet">
/// <item>a grass lane with painted lines, and gravel behind the firing line;</item>
/// <item>netting on posts down both sides;</item>
/// <item>a timber backstop banked with earth, netting above;</item>
/// <item>distance boards;</item>
/// <item>inflatable bunkers and target dummies built in code (<see cref="RangeShapes"/>);</item>
/// <item>old paint, scenery and tree lines beyond, and crows overhead.</item>
/// </list>
/// Each prop's walking collision comes from the same <see cref="PropSpec"/> that builds its ball
/// collider in the sim, so they always match. The nets, backstop and boards only stop you walking
/// through them; paint isn't stopped by anything the sim doesn't have.
/// </summary>
public partial class RangeBuilder : Node3D
{
    /// <summary>Static dressing is merged into meshes per chunk of this size (m), as on the levels.</summary>
    private const float ChunkSize = 24f;

    /// <summary>Material ids from here up are recoloured kit materials (<see cref="MaterialLibrary.Recoloured"/>).</summary>
    private const int Recoloured = 100_000;

    /// <summary>Where the firing line is painted, behind the spawn (m, +Z).</summary>
    private const float FiringLineZ = 0.4f;

    private readonly List<TargetView> _targets = new();
    private readonly List<Material> _recoloured = new();
    private readonly Dictionary<(int Index, Color Color), int> _recolouredIds = new();
    private MaterialLibrary _materials = null!;
    private TrainingGroundDef _def = null!;

    public IReadOnlyList<TargetView> Targets => _targets;

    private readonly List<OldPaint> _oldPaint = new();

    /// <summary>The feet of everything standing on the ground (weeds grow along them, not inside), and where a roof keeps rain off.</summary>
    private readonly List<Vector2[]> _feet = new();
    private readonly List<Rect2> _covered = new();

    /// <summary>The old paint on the backstop, the bunkers, the ground and the dummies, so a graphics preset can hide it.</summary>
    public IReadOnlyList<OldPaint> OldPaint => _oldPaint;

    /// <summary>Grass and weeds outside the lane and along the foot of everything, so a graphics preset can thin them.</summary>
    public WeedField? Weeds { get; private set; }

    public Node3D? TargetNode(int index) => index >= 0 && index < _targets.Count ? _targets[index] : null;

    public void Build(RangeLayout layout, IReadOnlyList<KitMaterial> kit, PresentationDef view)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        _targets.Clear();
        _oldPaint.Clear();
        _feet.Clear();
        _covered.Clear();
        _recoloured.Clear();
        _recolouredIds.Clear();
        _materials = new MaterialLibrary(kit);
        _def = view.TrainingGround;
        var shapes = new Dictionary<(int Cx, int Cz), ShapeMesh>();
        var walls = new StaticBody3D { Name = "DressingBody" };
        AddChild(walls);

        BuildGround(layout);
        BuildLines(layout);
        BuildBoards(layout, shapes, walls);
        float backstopZ = -layout.Length - 1f;
        BuildNets(layout, backstopZ, shapes, walls);
        BuildBackstop(layout, backstopZ, shapes, walls);
        BuildHut(shapes, walls);
        foreach (PropSpec prop in layout.Props)
        {
            BuildProp(prop, ChunkMesh(shapes, prop.BasePosition.X, prop.BasePosition.Z));
        }

        BuildTargets(layout, shapes);
        Scenery.Build(_def.Scenery, layout.Id, id => Kit(id, "scenery"), at => ChunkMesh(shapes, at.X, at.Z));
        foreach (((int cx, int cz), ShapeMesh shape) in shapes)
        {
            var mesh = new ArrayMesh();
            shape.Commit(mesh, MaterialFor);
            AddChild(new MeshInstance3D { Name = $"Dressing_{cx}_{cz}", Mesh = ShapeMesh.WithLods(mesh) });
        }

        AddChild(Horizon.Build(new Vector3(0f, 0f, -layout.Length * 0.5f), LevelBuilder.StableHash(layout.Id), view.Horizon));
        var birds = new Birds { Name = "Birds" };
        AddChild(birds);
        birds.Build(new Vector3(0f, 0f, -layout.Length * 0.5f), LevelBuilder.StableHash(layout.Id), view.Birds);
        BuildOldPaint(layout, backstopZ);
        Weeds = new WeedField { Name = "Weeds" };
        AddChild(Weeds);
        Weeds.Build(new RangeGround(layout, _def, backstopZ, _feet, _covered), (uint)LevelBuilder.StableHash(layout.Id), view.Weeds);
    }

    public void UpdateTargets(double time)
    {
        foreach (TargetView view in _targets)
        {
            view.SetTime(time);
        }
    }

    /// <summary>The lane's grass from the firing line to the backstop, gravel behind the line, and scrub all round.</summary>
    private void BuildGround(RangeLayout layout)
    {
        float lane = layout.Width;
        float near = FiringLineZ + 0.06f, far = -layout.Length - 1.5f;
        AddChild(new MeshInstance3D
        {
            Name = "Lane",
            Mesh = LevelBuilder.GroundMesh(new Vector2(lane, near - far), new Vector3(0f, 0f, (near + far) * 0.5f)),
            MaterialOverride = _materials.Recoloured(Kit(_def.Field, "field"), Color.FromHtml(_def.FieldTint)),
        });

        RangeHutDef hut = _def.Hut;
        float back = MathF.Max(layout.BackMargin + 2f, hut.Position_m[1] + hut.Depth_m * 0.5f + 1.5f);
        AddChild(new MeshInstance3D
        {
            Name = "FiringPoint",
            Mesh = LevelBuilder.GroundMesh(new Vector2(lane + 4f, back - near), new Vector3(0f, 0f, (near + back) * 0.5f)),
            MaterialOverride = _materials[Kit(_def.FiringPoint, "firingPoint")],
        });

        AddChild(new MeshInstance3D
        {
            Name = "Surroundings",
            Mesh = LevelBuilder.GroundMesh(new Vector2(4000f, 4000f), new Vector3(0f, 0f, -layout.Length * 0.5f)),
            MaterialOverride = _materials[Kit(LevelBuilder.SurroundingsMaterial, "field")],
            Position = new Vector3(0f, -0.03f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        var body = new StaticBody3D { Name = "GroundBody" };
        body.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D() });
        AddChild(body);
    }

    /// <summary>The firing line, and a line across the lane every marker spacing.</summary>
    private void BuildLines(RangeLayout layout)
    {
        var paint = new StandardMaterial3D { AlbedoColor = Color.FromHtml(_def.LineColor), Roughness = 0.9f };
        var firingLine = new StandardMaterial3D { AlbedoColor = Color.FromHtml(_def.FiringLineColor), Roughness = 0.9f };
        AddChild(new MeshInstance3D
        {
            Name = "FiringLine",
            Mesh = new BoxMesh { Size = new Vector3(layout.Width, 0.01f, 0.12f) },
            MaterialOverride = firingLine,
            Position = new Vector3(0, 0.005f, FiringLineZ),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        for (float d = layout.MarkerSpacing; d <= layout.MarkerMax + 0.01f; d += layout.MarkerSpacing)
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(layout.Width, 0.01f, 0.08f) },
                MaterialOverride = paint,
                Position = new Vector3(0, 0.005f, -d),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
    }

    /// <summary>
    /// A board on two posts at each labelled distance, on the left of the lane facing the firing line,
    /// growing with distance (and standing taller) so the far ones read; the distance in big letters.
    /// </summary>
    private void BuildBoards(RangeLayout layout, Dictionary<(int, int), ShapeMesh> shapes, StaticBody3D walls)
    {
        DistanceBoardsDef boards = _def.Boards;
        int board = Kit(boards.Board, "boards.board"), posts = Kit(boards.Posts, "boards.posts");
        float min = float.MaxValue, max = float.MinValue;
        foreach (float d in layout.MarkerLabels)
        {
            min = MathF.Min(min, d);
            max = MathF.Max(max, d);
        }

        float x = -(layout.Width * 0.5f - 1.2f);
        foreach (float d in layout.MarkerLabels)
        {
            float t = max > min ? (d - min) / (max - min) : 0f;
            float width = Mathf.Lerp(boards.Width_m[0], boards.Width_m[1], t), height = width * 0.42f;
            float bottom = 0.5f + 0.01f * d, top = bottom + height;
            ShapeMesh m = ChunkMesh(shapes, x, -d);
            m.Place(Transform3D.Identity, top);
            m.Box(board, new Vector3(x, bottom + height * 0.5f, -d), new Vector3(width, height, 0.03f));
            foreach (float side in new[] { -0.36f, 0.36f })
            {
                m.Box(posts, new Vector3(x + side * width, (top - 0.04f) * 0.5f, -d - 0.055f), new Vector3(0.075f, top - 0.04f, 0.075f));
            }

            AddChild(new Label3D
            {
                Name = $"Board_{d:0}",
                Text = $"{d:0} m",
                FontSize = 128,
                PixelSize = height * 0.72f / 128f,
                Modulate = Color.FromHtml(boards.TextColor),
                OutlineSize = 0,
                Shaded = true,
                DoubleSided = false,
                AlphaCut = Label3D.AlphaCutMode.Discard,
                Position = new Vector3(x, bottom + height * 0.5f, -d + 0.017f),
            });
            Wall(walls, new Vector3(x, top * 0.5f, -d - 0.03f), new Vector3(width, top, 0.15f));
        }
    }

    /// <summary>
    /// Netting down both sides of the lane, from behind the firing point to the backstop: posts at even
    /// spacing, a cable sagging between their tops, the net hung from it. You can't walk through it.
    /// </summary>
    private void BuildNets(RangeLayout layout, float backstopZ, Dictionary<(int, int), ShapeMesh> shapes, StaticBody3D walls)
    {
        RangeNetsDef nets = _def.Nets;
        int post = Kit(nets.Posts, "nets.posts");
        float near = layout.BackMargin, far = backstopZ;
        int spans = Math.Max(1, Mathf.CeilToInt((near - far) / nets.PostSpacing_m));
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        foreach (float side in new[] { -1f, 1f })
        {
            float x = side * layout.Width * 0.5f;
            for (int i = 0; i <= spans; i++)
            {
                float z = Mathf.Lerp(near, far, (float)i / spans);
                Post(ChunkMesh(shapes, x, z), post, new Vector3(x, 0f, z), nets.Height_m, nets.PostRadius_m);
                if (i < spans)
                {
                    float z1 = Mathf.Lerp(near, far, (float)(i + 1) / spans);
                    Span(ChunkMesh(shapes, x, z), post, tool, new Vector3(x, 0f, z), new Vector3(x, 0f, z1), 0.03f, nets.Height_m, nets);
                }
            }

            Wall(walls, new Vector3(x, nets.Height_m * 0.5f, (near + far) * 0.5f), new Vector3(0.1f, nets.Height_m, near - far));
        }

        AddChild(new MeshInstance3D { Name = "Nets", Mesh = tool.Commit(), MaterialOverride = NetMaterial(nets), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    /// <summary>
    /// The backstop: a wall of boards across the end of the lane and past it on each side, posts behind it
    /// poking above its capping, an earth bank behind rising over it with grass on top, and netting on tall
    /// poles above. Its face takes the old paint.
    /// </summary>
    private void BuildBackstop(RangeLayout layout, float z, Dictionary<(int, int), ShapeMesh> shapes, StaticBody3D walls)
    {
        BackstopDef b = _def.Backstop;
        int boards = Kit(b.Boards, "backstop.boards"), posts = Kit(b.Posts, "backstop.posts");
        int bank = Kit(b.Bank, "backstop.bank"), grass = Kit(b.BankTop, "backstop.bankTop"), poles = Kit(_def.Nets.Posts, "nets.posts");
        float half = layout.Width * 0.5f + b.Overhang_m, h = b.Height_m;
        int bays = Math.Max(1, Mathf.CeilToInt(half * 2f / 2.4f));
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < bays; i++)
        {
            float x0 = Mathf.Lerp(-half, half, (float)i / bays), x1 = Mathf.Lerp(-half, half, (float)(i + 1) / bays);
            ShapeMesh m = ChunkMesh(shapes, (x0 + x1) * 0.5f, z);
            m.Place(Transform3D.Identity, h);
            // Boards, a bay at a time so each takes its own run of the photo.
            m.Box(boards, new Vector3((x0 + x1) * 0.5f, h * 0.5f, z - 0.025f), new Vector3(x1 - x0, h, 0.05f), Basis.Identity, new Vector2(i * 0.83f, 0f));
            m.Box(boards, new Vector3((x0 + x1) * 0.5f, h + 0.025f, z - 0.06f), new Vector3(x1 - x0, 0.05f, 0.2f));
            m.Box(posts, new Vector3(x0, (h + 0.2f) * 0.5f, z - 0.13f), new Vector3(0.15f, h + 0.2f, 0.15f));
            if (i % 3 == 0)
            {
                Vector3 foot = new(x0, 0f, z - 0.32f);
                Post(m, poles, foot, b.NetHeight_m, _def.Nets.PostRadius_m * 1.4f);
                float xs = Mathf.Lerp(-half, half, Math.Min(bays, i + 3) / (float)bays);
                Span(m, poles, tool, foot, new Vector3(xs, 0f, z - 0.32f), h + 0.1f, b.NetHeight_m, _def.Nets);
            }
        }

        // The last post and pole, at the far end.
        ShapeMesh end = ChunkMesh(shapes, half, z);
        end.Place(Transform3D.Identity, h);
        end.Box(posts, new Vector3(half, (h + 0.2f) * 0.5f, z - 0.13f), new Vector3(0.15f, h + 0.2f, 0.15f));
        Post(end, poles, new Vector3(half, 0f, z - 0.32f), b.NetHeight_m, _def.Nets.PostRadius_m * 1.4f);

        // The bank, in the plane across the lane: from the wall's back to its far foot, its crest a little over the wall.
        var across = new Basis(new Vector3(0f, 0f, -1f), Vector3.Up, new Vector3(1f, 0f, 0f));
        float crest = h + b.BankRise_m, depth = b.BankDepth_m;
        // Its top: up from just under the wall's capping to the crest, along it, and down to the far foot.
        var top = new[] { new Vector2(0f, h - 0.1f), new Vector2(0.8f, crest), new Vector2(1.8f, crest), new Vector2(depth, 0f) };
        var body = new[] { new Vector2(0f, 0f), top[3], top[2], top[1], top[0] };
        // Grass over the top, a few centimetres thick: along the top front to back, then back along its upper side.
        var skin = new List<Vector2>(top);
        for (int i = top.Length - 1; i >= 0; i--)
        {
            skin.Add(top[i] + new Vector2(i == top.Length - 1 ? 0.05f : 0f, 0.04f));
        }

        for (int i = 0; i < 4; i++)
        {
            // In four lengths, so each sits in its own chunk.
            float x0 = Mathf.Lerp(-half, half, i / 4f), x1 = Mathf.Lerp(-half, half, (i + 1) / 4f);
            ShapeMesh m = ChunkMesh(shapes, (x0 + x1) * 0.5f, z);
            m.Place(Transform3D.Identity, crest);
            m.Extrude(bank, new Vector3((x0 + x1) * 0.5f, 0f, z - 0.05f), across, body, x1 - x0);
            m.Extrude(grass, new Vector3((x0 + x1) * 0.5f, 0f, z - 0.05f), across, skin, x1 - x0 + 0.04f);
        }

        AddChild(new MeshInstance3D { Name = "BackstopNet", Mesh = tool.Commit(), MaterialOverride = NetMaterial(_def.Nets), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        Wall(walls, new Vector3(0f, crest * 0.5f, z - depth * 0.5f), new Vector3(half * 2f, crest, depth));
    }

    /// <summary>
    /// The shelter behind the firing point (<see cref="RangeHutDef"/>), in its own frame: its open front
    /// to the lane (−Z), the roof falling to the back.
    /// </summary>
    private void BuildHut(Dictionary<(int, int), ShapeMesh> shapes, StaticBody3D walls)
    {
        RangeHutDef hut = _def.Hut;
        int frame = Kit(hut.Frame, "hut.frame"), roof = Kit(hut.Roof, "hut.roof"), boards = Kit(hut.Boards, "hut.boards");
        int pods = Kit(_def.Dummy.Mask, "dummy.mask"), tank = Kit(_def.Nets.Posts, "nets.posts");
        var at = new Vector3(hut.Position_m[0], 0f, hut.Position_m[1]);
        float w = hut.Width_m, hw = w * 0.5f, d = hut.Depth_m, front = -d * 0.5f, back = d * 0.5f;
        float high = hut.Height_m, low = high - 0.5f;
        ShapeMesh m = ChunkMesh(shapes, at.X, at.Z);
        m.Place(new Transform3D(Basis.Identity, at), high);
        void Solid(Vector3 center, Vector3 size) => Wall(walls, at + center, size);
        // No rain under the roof, so nothing grows there.
        _covered.Add(new Rect2(at.X - hw - 0.3f, at.Z + front - 0.3f, w + 0.6f, d + 0.6f));

        // Posts front and back, beams along their tops, rafters between them.
        int bays = Math.Max(2, Mathf.RoundToInt(w / 2.5f));
        for (int i = 0; i <= bays; i++)
        {
            float x = Mathf.Lerp(-hw, hw, (float)i / bays);
            m.Box(frame, new Vector3(x, high * 0.5f, front), new Vector3(0.12f, high, 0.12f));
            m.Box(frame, new Vector3(x, low * 0.5f, back), new Vector3(0.12f, low, 0.12f));
            m.Bar(frame, new Vector3(x, high + 0.02f, front - 0.3f), new Vector3(x, low + 0.02f, back + 0.3f), 0.06f, 0.12f);
            Solid(new Vector3(x, high * 0.5f, front), new Vector3(0.14f, high, 0.14f));
        }

        m.Box(frame, new Vector3(0f, high - 0.08f, front), new Vector3(w + 0.24f, 0.16f, 0.1f));
        m.Box(frame, new Vector3(0f, low - 0.08f, back), new Vector3(w + 0.24f, 0.16f, 0.1f));

        // The roof: one sheet over the rafters, falling to the back, overhanging all round.
        float slope = MathF.Atan2(high - low, d + 0.6f), run = MathF.Sqrt((d + 0.6f) * (d + 0.6f) + (high - low) * (high - low));
        m.Box(roof, new Vector3(0f, (high + low) * 0.5f + 0.1f, 0f), new Vector3(w + 0.6f, 0.03f, run + 0.3f), new Basis(Vector3.Right, slope));

        // The boarded back, a bench along it, and a table by the open end.
        m.Box(boards, new Vector3(0f, low * 0.5f, back + 0.08f), new Vector3(w + 0.1f, low, 0.03f));
        Solid(new Vector3(0f, low * 0.5f, back + 0.08f), new Vector3(w + 0.1f, low, 0.1f));
        m.Box(boards, new Vector3(0f, 0.46f, back - 0.28f), new Vector3(w - 0.8f, 0.05f, 0.36f));
        for (float x = -hw + 0.6f; x <= hw - 0.59f; x += (w - 1.2f) / 3f)
        {
            m.Box(frame, new Vector3(x, 0.22f, back - 0.28f), new Vector3(0.07f, 0.44f, 0.3f));
        }

        Solid(new Vector3(0f, 0.25f, back - 0.28f), new Vector3(w - 0.8f, 0.5f, 0.4f));
        var table = new Vector3(hw - 1.1f, 0f, front + 1.0f);
        m.Box(boards, table + new Vector3(0f, 0.76f, 0f), new Vector3(1.6f, 0.05f, 0.8f));
        foreach (float sx in new[] { -0.72f, 0.72f })
        {
            foreach (float sz in new[] { -0.32f, 0.32f })
            {
                m.Box(frame, table + new Vector3(sx, 0.37f, sz), new Vector3(0.06f, 0.74f, 0.06f));
            }
        }

        Solid(table + new Vector3(0f, 0.39f, 0f), new Vector3(1.6f, 0.78f, 0.8f));
        // On it: a row of paint pods and an air tank lying down.
        for (int i = 0; i < 6; i++)
        {
            m.Cylinder(pods, table + new Vector3(-0.6f + 0.08f * i, 0.885f, -0.18f + 0.03f * (i % 2)), Basis.Identity, 0.034f, 0.2f, 10);
        }

        m.Cylinder(tank, table + new Vector3(0.3f, 0.845f, 0.1f), ShapeMesh.BasisAlong(new Vector3(0.9f, 0f, 0.44f).Normalized()), 0.055f, 0.3f, 12);
    }

    /// <summary>Walking collision for a box of the dressing, which weeds also grow round the foot of.</summary>
    private void Wall(StaticBody3D walls, Vector3 center, Vector3 size)
    {
        walls.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = center });
        Foot(new Vector2(center.X, center.Z), new Vector2(size.X, size.Z) * 0.5f, 0f);
    }

    /// <summary>Records a rectangle standing on the ground, turned by <paramref name="yaw"/>.</summary>
    private void Foot(Vector2 centre, Vector2 half, float yaw)
    {
        var x = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw)) * half.X;
        var z = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)) * half.Y;
        _feet.Add(new[] { centre - x - z, centre + x - z, centre + x + z, centre - x + z });
    }

    /// <summary>Records a round foot.</summary>
    private void Foot(Vector2 centre, float radius)
    {
        int segments = Math.Clamp((int)(radius * 12f), 8, 24);
        var outline = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float a = Mathf.Tau * i / segments;
            outline[i] = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        _feet.Add(outline);
    }

    /// <summary>A post standing at <paramref name="foot"/>, capped.</summary>
    private static void Post(ShapeMesh m, int material, Vector3 foot, float height, float radius)
    {
        m.Place(Transform3D.Identity, height);
        m.Cylinder(material, foot + Vector3.Up * (height * 0.5f), Basis.Identity, radius, height, 10);
        m.Pillow(material, foot + Vector3.Up * height, new Vector3(radius, radius * 0.6f, radius) * 2.6f, Basis.Identity, 3f, 4, 10);
    }

    /// <summary>
    /// The cable sagging between two posts' tops and the net hanging from it to <paramref name="bottom"/>,
    /// in columns so its top follows the cable.
    /// </summary>
    private static void Span(ShapeMesh m, int cable, SurfaceTool net, Vector3 a, Vector3 b, float bottom, float top, RangeNetsDef def)
    {
        const int Columns = 8;
        m.Place(Transform3D.Identity, top);
        float repeat = def.Mesh_m * NetCells;
        float length = a.DistanceTo(b);
        Vector3 Cable(float t) => a.Lerp(b, t) + Vector3.Up * (top - 0.06f - def.Sag_m * 4f * t * (1f - t));
        for (int i = 0; i < Columns; i++)
        {
            float t0 = (float)i / Columns, t1 = (float)(i + 1) / Columns;
            Vector3 c0 = Cable(t0), c1 = Cable(t1);
            m.Rod(cable, c0, c1, 0.006f, 4, caps: false);
            Vector3 f0 = a.Lerp(b, t0) + Vector3.Up * bottom, f1 = a.Lerp(b, t1) + Vector3.Up * bottom;
            float u0 = t0 * length / repeat, u1 = t1 * length / repeat;
            // Two-sided, so either winding.
            Vector3 n0 = c0 - Vector3.Up * 0.02f, n1 = c1 - Vector3.Up * 0.02f;
            NetVertex(net, f0, new Vector2(u0, f0.Y / repeat));
            NetVertex(net, n0, new Vector2(u0, n0.Y / repeat));
            NetVertex(net, n1, new Vector2(u1, n1.Y / repeat));
            NetVertex(net, f0, new Vector2(u0, f0.Y / repeat));
            NetVertex(net, n1, new Vector2(u1, n1.Y / repeat));
            NetVertex(net, f1, new Vector2(u1, f1.Y / repeat));
        }
    }

    private static void NetVertex(SurfaceTool tool, Vector3 at, Vector2 uv)
    {
        tool.SetNormal(Vector3.Up);
        tool.SetUV(uv);
        tool.AddVertex(at);
    }

    /// <summary>Squares of net across one repeat of its texture.</summary>
    private const int NetCells = 4;

    /// <summary>
    /// Knotted square netting in the twine's colour, painted at load: see-through, so far off it thins to
    /// a haze. Lit from above whichever way it faces, like the tree lines, so it doesn't flicker as it turns.
    /// </summary>
    private static StandardMaterial3D NetMaterial(RangeNetsDef def)
    {
        const int Cell = 16, Size = Cell * NetCells;
        Color twine = Color.FromHtml(def.Color);
        float half = MathF.Max(0.6f, def.Twine * Cell * 0.5f);
        var bytes = new byte[Size * Size * 4];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float dx = MathF.Abs((x + 0.5f) % Cell - Cell * 0.5f), dy = MathF.Abs((y + 0.5f) % Cell - Cell * 0.5f);
                // Distance to the nearest strand: the grid lines run through the middle of each cell.
                float line = MathF.Min(dx, dy);
                float knot = MathF.Sqrt(dx * dx + dy * dy);
                float cover = Mathf.Clamp(half - line + 0.5f, 0f, 1f);
                cover = MathF.Max(cover, Mathf.Clamp(half * 1.7f - knot + 0.5f, 0f, 1f));
                int i = (y * Size + x) * 4;
                bytes[i] = (byte)(twine.R8);
                bytes[i + 1] = (byte)(twine.G8);
                bytes[i + 2] = (byte)(twine.B8);
                bytes[i + 3] = (byte)Math.Clamp((int)MathF.Round(cover * 255f), 0, 255);
            }
        }

        Image image = Image.CreateFromData(Size, Size, false, Image.Format.Rgba8, bytes);
        image.GenerateMipmaps();
        return new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(image),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            // No shine: twine this thin doesn't catch the sky, it shows dark against it.
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            Roughness = 1f,
        };
    }

    /// <summary>A bunker or board drawn in code over its collider, which also stops you walking through it.</summary>
    private void BuildProp(PropSpec prop, ShapeMesh m)
    {
        Vector3 size = prop.Size.ToGodot();
        m.Place(new Transform3D(new Basis(Vector3.Up, prop.Yaw), prop.BasePosition.ToGodot()), size.Y);
        Color color = Conv.ParseColor(prop.Color, new Color(0.85f, 0.85f, 0.85f));
        if (prop.SurfaceName == "panel")
        {
            RangeShapes.Panel(m, prop, Paint(Kit(_def.Panel, "panel"), color), Kit(_def.Stakes, "stakes"));
        }
        else
        {
            int fabric = Kit(_def.Inflatable, "inflatable");
            RangeShapes.Inflatable(m, prop, new RangeShapes.InflatableMaterials(Paint(fabric, color), Paint(fabric, color * 0.72f), Kit(_def.Tethers, "tethers")));
        }

        Shape3D shape = prop.Kind switch
        {
            PropShapeKind.Box => new BoxShape3D { Size = size },
            PropShapeKind.Cylinder => new CylinderShape3D { Radius = size.X * 0.5f, Height = size.Y },
            PropShapeKind.Wedge => new ConvexPolygonShape3D { Points = WedgePoints(size) },
            _ => throw new InvalidOperationException($"Unknown prop shape {prop.Kind}"),
        };
        var body = new StaticBody3D
        {
            Name = "Prop_" + prop.Id,
            Position = prop.BasePosition.ToGodot() + new Vector3(0, size.Y * 0.5f, 0),
            Rotation = new Vector3(0, prop.Yaw, 0),
        };
        body.AddChild(new CollisionShape3D { Shape = shape });
        AddChild(body);
        var at = new Vector2(prop.BasePosition.X, prop.BasePosition.Z);
        if (prop.Kind == PropShapeKind.Cylinder)
        {
            Foot(at, size.X * 0.5f);
        }
        else
        {
            Foot(at, new Vector2(size.X, size.Z) * 0.5f, prop.Yaw);
        }
    }

    /// <summary>Each target's dummy, and the track under any that moves.</summary>
    private void BuildTargets(RangeLayout layout, Dictionary<(int, int), ShapeMesh> shapes)
    {
        DummyViewDef d = _def.Dummy;
        int body = Kit(d.Body, "dummy.body"), head = Kit(d.Head, "dummy.head");
        int rails = Kit(_def.Rails, "rails"), sleepers = Kit(_def.Sleepers, "sleepers");
        var materials = new RangeShapes.DummyMaterials(
            Paint(body, Color.FromHtml(d.BodyColor)), Kit(d.Straps, "dummy.straps"), Paint(head, Color.FromHtml(d.HeadColor)),
            Kit(d.Mask, "dummy.mask"), Kit(d.Lens, "dummy.lens"), Kit(d.Base, "dummy.base"), Kit(d.Fill, "dummy.fill"), rails);
        for (int i = 0; i < layout.Targets.Count; i++)
        {
            TargetSpec spec = layout.Targets[i];
            // The track runs along the motion axis in the world; the trolley is built in the dummy's own frame.
            Vector3 along = spec.Motion is { } motion ? new Basis(Vector3.Up, -spec.Yaw) * motion.Axis.ToGodot() : Vector3.Right;
            var shape = new ShapeMesh();
            shape.Place(Transform3D.Identity, 1.7f);
            RangeShapes.Dummy(shape, spec.Kind, spec.Motion is not null, along, materials);
            var mesh = new ArrayMesh();
            shape.Commit(mesh, MaterialFor);

            var view = new TargetView { Name = "Target_" + spec.Id };
            AddChild(view);
            view.Build(spec, mesh, Color.FromHtml(d.FlashColor));
            _targets.Add(view);
            PaintDummy(layout, spec, view);

            Vector3 at = spec.BasePosition.ToGodot();
            if (spec.Motion is { } run)
            {
                ShapeMesh track = ChunkMesh(shapes, at.X, at.Z);
                track.Place(Transform3D.Identity, 0.2f);
                RangeShapes.Track(track, spec, rails, sleepers);
                // Weeds along the rails: the track's foot, along its axis.
                Vector3 axis = run.Axis.ToGodot();
                Foot(new Vector2(at.X, at.Z), new Vector2(run.Amplitude + 0.75f, 0.35f), Mathf.Atan2(-axis.Z, axis.X));
            }
            else
            {
                Foot(new Vector2(at.X, at.Z), 0.31f);
            }
        }
    }

    /// <summary>
    /// Old paint from practice: shots from the firing line at the targets (passing through them, as the
    /// sim's targets aren't in this world, to land behind), at the bunkers and at the backstop.
    /// </summary>
    private void BuildOldPaint(RangeLayout layout, float backstopZ)
    {
        var world = new CollisionWorld();
        layout.BuildCollision(world);
        float half = layout.Width * 0.5f + _def.Backstop.Overhang_m, h = _def.Backstop.Height_m;
        world.Add(new BoxShape(new SVector3(0f, h * 0.5f, backstopZ - 0.025f), System.Numerics.Quaternion.Identity, new SVector3(half, h * 0.5f, 0.025f)),
            layout.GroundSurface, "backstop");
        world.Build();

        float lane = layout.Width * 0.5f;
        var paint = new OldPaint { Name = "OldPaint" };
        AddChild(paint);
        _oldPaint.Add(paint);
        paint.Build(layout.Id, world, _def.OldPaint, random =>
        {
            float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
            SVector3 eye = FiringPosition(random, lane);
            double pick = random.NextDouble();
            if (pick < 0.45 && layout.Targets.Count > 0)
            {
                TargetSpec target = layout.Targets[random.Next(layout.Targets.Count)];
                SVector3 at = target.BasePosition + (target.Motion is { } m ? m.Axis * (m.Amplitude * R(-1f, 1f)) : SVector3.Zero);
                return (eye, at + new SVector3(0f, R(0.3f, 1.65f), 0f));
            }

            if (pick < 0.75 && layout.Props.Count > 0)
            {
                PropSpec prop = layout.Props[random.Next(layout.Props.Count)];
                return (eye, prop.BasePosition + new SVector3(R(-0.4f, 0.4f) * prop.Size.X, R(0.15f, 0.95f) * prop.Size.Y, 0f));
            }

            // At the backstop behind the targets: mostly low and in the middle.
            float x = R(-1f, 1f) * R(0.2f, 1f) * lane;
            return (eye, new SVector3(x, R(0.15f, h - 0.15f) * R(0.6f, 1f), backstopZ));
        });
    }

    /// <summary>
    /// Old paint on a dummy, in its own frame (so a moving one carries it): shots from the firing line at
    /// its body and head, landing on its hitbox's parts, which its padding and head sit just inside.
    /// </summary>
    private void PaintDummy(RangeLayout layout, TargetSpec spec, TargetView view)
    {
        var world = new CollisionWorld();
        foreach (TargetPartSpec part in spec.Kind.Parts)
        {
            world.Add(part.CreateLocalShape(), spec.Kind.Surface, "dummy");
        }

        world.Build();
        var toLocal = System.Numerics.Quaternion.CreateFromAxisAngle(SVector3.UnitY, -spec.Yaw);
        float lane = layout.Width * 0.5f, top = 0f;
        foreach (TargetPartSpec part in spec.Kind.Parts)
        {
            top = MathF.Max(top, part.Kind == PartShapeKind.Capsule ? MathF.Max(part.From.Y, part.To.Y) + part.Radius : part.Center.Y + part.Radius);
        }

        var paint = new OldPaint { Name = "OldPaint" };
        view.AddChild(paint);
        _oldPaint.Add(paint);
        paint.Build($"{layout.Id}/{spec.Id}", world, _def.DummyPaint, random =>
        {
            SVector3 eye = SVector3.Transform(FiringPosition(random, lane) - spec.BasePosition, toLocal);
            var aim = new SVector3(((float)random.NextDouble() - 0.5f) * 0.25f, 0.2f + (float)random.NextDouble() * (top - 0.2f), 0f);
            return (eye, aim);
        }, atlas: layout.Id);
    }

    /// <summary>Where someone stands to shoot: along the firing line, at standing or kneeling eye height.</summary>
    private static SVector3 FiringPosition(Random random, float lane)
    {
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
        return new SVector3(R(-lane * 0.8f, lane * 0.8f), R(1.1f, 1.65f), R(-0.3f, 1.2f));
    }

    /// <summary>The index of a kit material, or a plain stand-in (with an error naming the key) if there's none by that id.</summary>
    private int Kit(string id, string key)
    {
        int index = _materials.Find(id);
        if (index < 0)
        {
            GD.PushError($"presentation.jsonc trainingGround.{key}: no material '{id}' in kit/materials.jsonc; drawn with the first one");
            return 0;
        }

        return index;
    }

    /// <summary>A material id for a kit material in another colour.</summary>
    private int Paint(int index, Color color)
    {
        if (!_recolouredIds.TryGetValue((index, color), out int id))
        {
            id = Recoloured + _recoloured.Count;
            _recoloured.Add(_materials.Recoloured(index, color));
            _recolouredIds[(index, color)] = id;
        }

        return id;
    }

    private Material MaterialFor(int id) => id >= Recoloured ? _recoloured[id - Recoloured] : _materials[id];

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

    /// <summary>
    /// The training ground's ground for the weeds: the lane is mown (nothing scattered on it, only along
    /// the feet of what stands there), the firing point is the kit's gravel and everywhere round it the
    /// field's grass; nothing grows inside anything standing on the ground or under the shelter's roof.
    /// </summary>
    private sealed class RangeGround : WeedField.IWeedGround
    {
        /// <summary>Not a kit material: the weeds' densities don't list it, so the lane stays mown.</summary>
        private const string Mown = "(mown)";

        private readonly Rect2 _lane, _firingPoint;
        private readonly string _field, _gravel;
        private readonly List<Vector2[]> _feet;
        private readonly List<Rect2> _covered;
        private readonly List<Vector2> _outline = new();

        public RangeGround(RangeLayout layout, TrainingGroundDef def, float backstopZ, List<Vector2[]> feet, List<Rect2> covered)
        {
            float half = layout.Width * 0.5f;
            _lane = new Rect2(-half, backstopZ, layout.Width, FiringLineZ - backstopZ);
            float back = MathF.Max(layout.BackMargin + 2f, def.Hut.Position_m[1] + def.Hut.Depth_m * 0.5f + 1.5f);
            _firingPoint = new Rect2(-half - 2f, FiringLineZ, layout.Width + 4f, back - FiringLineZ);
            _field = def.Field;
            _gravel = def.FiringPoint;
            _feet = feet;
            _covered = covered;
            Bounds = new Pb.Sim.Collision.Aabb(new System.Numerics.Vector3(-half - 18f, -1f, backstopZ - def.Backstop.BankDepth_m - 8f),
                new System.Numerics.Vector3(half + 18f, 10f, back + 8f));
        }

        public Pb.Sim.Collision.Aabb Bounds { get; }

        public string MaterialAt(float x, float z) =>
            _lane.HasPoint(new Vector2(x, z)) ? Mown : _firingPoint.HasPoint(new Vector2(x, z)) ? _gravel : _field;

        public bool OpenGround(float x, float z, float clearance, out float y)
        {
            y = 0f;
            var p = new Vector2(x, z);
            foreach (Rect2 r in _covered)
            {
                if (r.HasPoint(p))
                {
                    return false;
                }
            }

            foreach (Vector2[] foot in _feet)
            {
                if (Inside(foot, p, clearance))
                {
                    return false;
                }
            }

            return true;
        }

        public IEnumerable<(List<Vector2> Outline, Vector2 Centre)> Feet()
        {
            foreach (Vector2[] foot in _feet)
            {
                _outline.Clear();
                _outline.AddRange(foot);
                Vector2 centre = Vector2.Zero;
                foreach (Vector2 q in foot)
                {
                    centre += q / foot.Length;
                }

                yield return (_outline, centre);
            }
        }

        public bool KeepClear(float x, float z, float radius) => false;

        /// <summary>Whether <paramref name="p"/> is inside a convex outline (either winding), grown by <paramref name="margin"/>.</summary>
        private static bool Inside(Vector2[] outline, Vector2 p, float margin)
        {
            float area = 0f;
            for (int i = 0; i < outline.Length; i++)
            {
                area += outline[i].Cross(outline[(i + 1) % outline.Length]);
            }

            // The inside is to the left of every edge of an outline with positive area, to the right otherwise.
            float sign = MathF.Sign(area);
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 a = outline[i], e = outline[(i + 1) % outline.Length] - a;
                float length = e.Length();
                if (length > 1e-5f && e.Cross(p - a) / length * sign < -margin)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Wedge corners centred on the prism's bounding box (Godot's PrismMesh origin).</summary>
    private static Vector3[] WedgePoints(Vector3 size)
    {
        float hw = size.X * 0.5f, hh = size.Y * 0.5f, hl = size.Z * 0.5f;
        return new[]
        {
            new Vector3(-hw, -hh, -hl), new Vector3(hw, -hh, -hl), new Vector3(0, hh, -hl),
            new Vector3(-hw, -hh, hl), new Vector3(hw, -hh, hl), new Vector3(0, hh, hl),
        };
    }
}
