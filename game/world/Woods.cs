using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Data;

namespace Pb.Game.World;

/// <summary>
/// Trees out beyond the level (presentation.jsonc "woods"), for the view only: copses of broadleaf trees
/// in the scrubland round the walls, so the view over them has something between the wall and the tree
/// lines on the horizon. Each tree has a trunk that tapers and leans, main branches forking out and up
/// with smaller ones off them, and clumps of leaves over its crown: crossed cards from an atlas painted
/// at load (<see cref="Paint"/>), alpha-cut, lit as if the crown were round, light coming through them
/// from behind, swaying in the wind (leaves.gdshader). Wood is merged into one mesh, leaves into one
/// MultiMesh; the wood casts shadows, the leaves don't. Kept clear of the place itself and of its power and pole lines, and
/// seeded by the place, so they're the same every time.
/// </summary>
public partial class Woods : Node3D
{
    public const int Variants = 4;

    private const int CellPx = 256;

    private static ImageTexture? _atlas;

    public int TreeCount { get; private set; }

    /// <summary>
    /// Grows the copses round <paramref name="place"/> (a plan rectangle, x and z), keeping
    /// <paramref name="def"/>'s clearance from it and from the lines of <paramref name="scenery"/>.
    /// </summary>
    public void Build(Rect2 place, int seed, WoodsDef def, SceneryDef? scenery)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        var random = new Random(seed ^ 0x7EE5);
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
        var lines = new List<(Vector2 A, Vector2 B)>();
        foreach (LineDef line in (scenery?.PowerLines ?? Array.Empty<LineDef>()).Concat(scenery?.PoleLines ?? Array.Empty<LineDef>()))
        {
            for (int i = 0; i + 1 < line.Points_m.Length; i++)
            {
                lines.Add((new Vector2(line.Points_m[i][0], line.Points_m[i][1]), new Vector2(line.Points_m[i + 1][0], line.Points_m[i + 1][1])));
            }
        }

        Rect2 clear = place.Grow(def.Clearance_m);
        bool Free(Vector2 p, List<Vector2> trees)
        {
            if (clear.HasPoint(p))
            {
                return false;
            }

            foreach ((Vector2 a, Vector2 b) in lines)
            {
                if (p.DistanceTo(Geometry2D.GetClosestPointToSegment(p, a, b)) < def.LineClearance_m)
                {
                    return false;
                }
            }

            foreach (Vector2 t in trees)
            {
                if (p.DistanceSquaredTo(t) < 9f)
                {
                    return false;
                }
            }

            return true;
        }

        var wood = new ShapeMesh();
        wood.Place(Transform3D.Identity, 10f);
        var clumps = new List<(Transform3D Transform, Color Tint, Color Custom)>();
        var trees = new List<Vector2>();
        Color[] leaves = Array.ConvertAll(def.LeafColors, c => Color.FromHtml(c).SrgbToLinear());
        float perimeter = 2f * (clear.Size.X + clear.Size.Y);
        for (int c = 0; c < def.Copses; c++)
        {
            // A point round the cleared rectangle, then out from it.
            Vector2 centre = Vector2.Zero;
            bool found = false;
            for (int attempt = 0; attempt < 20 && !found; attempt++)
            {
                float along = R(0f, perimeter), out_ = MathF.Pow(R(0f, 1f), 1.4f) * def.Reach_m + 6f;
                centre = Around(clear, along, out_);
                found = Free(centre, trees);
            }

            if (!found)
            {
                continue;
            }

            int count = random.Next(def.TreesPerCopse[0], def.TreesPerCopse[1] + 1);
            float spread = R(def.Spread_m[0], def.Spread_m[1]);
            Color leaf = leaves[random.Next(leaves.Length)];
            for (int k = 0, attempt = 0; k < count && attempt < count * 8; attempt++)
            {
                float a = R(0f, Mathf.Tau), r = spread * MathF.Sqrt(R(0f, 1f));
                var at = new Vector2(centre.X + MathF.Cos(a) * r, centre.Y + MathF.Sin(a) * r);
                if (!Free(at, trees))
                {
                    continue;
                }

                trees.Add(at);
                // Most trees in a copse share its colour; a few are another kind.
                Color tint = random.NextDouble() < 0.75 ? leaf : leaves[random.Next(leaves.Length)];
                // A few young ones among them.
                float height = R(def.Height_m[0], def.Height_m[1]) * (random.NextDouble() < 0.25 ? R(0.4f, 0.65f) : 1f);
                Tree(wood, clumps, new Vector3(at.X, 0f, at.Y), height, tint, random);
                k++;
            }
        }

        TreeCount = trees.Count;
        if (trees.Count == 0)
        {
            return;
        }

        var bark = new StandardMaterial3D { AlbedoColor = Color.FromHtml(def.BarkColor), Roughness = 0.95f };
        var mesh = new ArrayMesh();
        wood.Commit(mesh, _ => bark);
        AddChild(new MeshInstance3D { Name = "Wood", Mesh = ShapeMesh.WithLods(mesh) });

        _atlas ??= Paint(new Random(0x7EE5));
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/leaves.gdshader") };
        material.SetShaderParameter("atlas", _atlas);
        material.SetShaderParameter("cells", (float)Variants);
        material.SetShaderParameter("sway", def.Sway_m);
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = ClumpMesh(),
        };
        multimesh.InstanceCount = clumps.Count;
        for (int i = 0; i < clumps.Count; i++)
        {
            multimesh.SetInstanceTransform(i, clumps[i].Transform);
            multimesh.SetInstanceColor(i, clumps[i].Tint);
            multimesh.SetInstanceCustomData(i, clumps[i].Custom);
        }

        // The leaves' shadows would mostly fall outside the walls, and alpha-cut cards are dear to cast.
        AddChild(new MultiMeshInstance3D { Name = "Leaves", Multimesh = multimesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    /// <summary>The point <paramref name="along"/> metres round the edge of <paramref name="rect"/>, then <paramref name="out_"/> metres out from it.</summary>
    private static Vector2 Around(Rect2 rect, float along, float out_)
    {
        float w = rect.Size.X, h = rect.Size.Y;
        if (along < w)
        {
            return new Vector2(rect.Position.X + along, rect.Position.Y - out_);
        }

        along -= w;
        if (along < h)
        {
            return new Vector2(rect.End.X + out_, rect.Position.Y + along);
        }

        along -= h;
        if (along < w)
        {
            return new Vector2(rect.End.X - along, rect.End.Y + out_);
        }

        along -= w;
        return new Vector2(rect.Position.X - out_, rect.End.Y - along);
    }

    /// <summary>
    /// One tree at <paramref name="root"/>: a leaning, tapering trunk with a flare at its foot, main
    /// branches from the crown's base reaching out and up towards its edge with two smaller ones off
    /// each, and leaf clumps scattered through the outer part of the crown (an ellipsoid).
    /// </summary>
    private static void Tree(ShapeMesh wood, List<(Transform3D, Color, Color)> clumps, Vector3 root, float height, Color tint, Random random)
    {
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
        float leanAngle = R(0f, Mathf.Tau);
        var lean = new Vector3(MathF.Cos(leanAngle), 0f, MathF.Sin(leanAngle)) * R(0f, 0.07f);
        float radius = height * R(0.017f, 0.023f);
        float crownBase = height * R(0.24f, 0.36f);
        Vector3 Trunk(float y) => root + Vector3.Up * y + lean * y;

        // The trunk in a few tapering lengths, from a flared foot to a thin leader.
        float[] stops = { 0f, 0.5f, crownBase, height * 0.62f, height * 0.85f };
        float[] radii = { radius * 1.35f, radius, radius * 0.85f, radius * 0.55f, radius * 0.2f };
        for (int i = 0; i + 1 < stops.Length; i++)
        {
            Taper(wood, Trunk(stops[i]), Trunk(stops[i + 1]), radii[i], radii[i + 1], 8);
        }

        Vector3 centre = Trunk(Mathf.Lerp(crownBase, height, 0.52f));
        var size = new Vector3(height * R(0.3f, 0.42f), (height - crownBase) * 0.55f, height * R(0.3f, 0.42f));
        Vector3 OnCrown(Vector3 direction, float reach) => centre + direction * size * reach;

        int mains = random.Next(5, 9);
        float turn = R(0f, Mathf.Tau);
        for (int i = 0; i < mains; i++)
        {
            float a = turn + Mathf.Tau * (i + R(-0.3f, 0.3f)) / mains;
            float rise = R(0.05f, 0.75f);
            var direction = new Vector3(MathF.Cos(a), rise, MathF.Sin(a)).Normalized();
            float from = R(crownBase * 0.95f, height * 0.62f);
            Vector3 start = Trunk(from), end = OnCrown(direction, R(0.7f, 0.92f));
            float r0 = Mathf.Lerp(radius * 0.55f, radius * 0.3f, (from - crownBase) / MathF.Max(height * 0.62f - crownBase, 0.1f));
            Vector3 elbow = start.Lerp(end, 0.5f) + Vector3.Up * R(0f, 0.08f) * height;
            Taper(wood, start, elbow, r0, r0 * 0.65f, 6);
            Taper(wood, elbow, end, r0 * 0.65f, r0 * 0.15f, 5);
            for (int s = 0; s < 2; s++)
            {
                Vector3 fork = start.Lerp(elbow, R(0.6f, 1f));
                Vector3 side = OnCrown((direction + new Vector3(R(-0.7f, 0.7f), R(0f, 0.6f), R(-0.7f, 0.7f))).Normalized(), R(0.75f, 0.95f));
                Taper(wood, fork, side, r0 * 0.4f, r0 * 0.08f, 4);
            }
        }

        // Leaf clumps through the outer crown, more of them on top; lit as if the crown were round.
        int count = (int)(58f * MathF.Pow(height / 12f, 1.5f)) + random.Next(0, 12);
        for (int i = 0; i < count; i++)
        {
            var direction = new Vector3(R(-1f, 1f), R(-0.55f, 1f), R(-1f, 1f));
            if (direction.LengthSquared() < 0.05f)
            {
                continue;
            }

            direction = direction.Normalized();
            Vector3 at = OnCrown(direction, MathF.Pow(R(0.1f, 1f), 0.35f));
            float scale = height * R(0.17f, 0.26f);
            var basis = new Basis(Vector3.Up, R(0f, Mathf.Tau)) * new Basis(Vector3.Right, R(-0.5f, 0.5f)) * Basis.FromScale(Vector3.One * scale);
            Vector3 normal = (direction / size).Normalized();
            Color shade = tint * R(0.82f, 1.12f);
            clumps.Add((new Transform3D(basis, at), shade with { A = 1f }, new Color(normal.X, normal.Y, normal.Z, random.Next(Variants))));
        }
    }

    /// <summary>A tapering length of trunk or branch from <paramref name="a"/> (radius <paramref name="ra"/>) to <paramref name="b"/>.</summary>
    private static void Taper(ShapeMesh mesh, Vector3 a, Vector3 b, float ra, float rb, int segments)
    {
        Vector3 axis = b - a;
        float length = axis.Length();
        if (length < 1e-3f)
        {
            return;
        }

        var profile = new[] { new Vector2(ra, -length * 0.5f), new Vector2(rb, length * 0.5f) };
        mesh.Lathe(0, (a + b) * 0.5f, ShapeMesh.BasisAlong(axis / length), profile, segments);
    }

    /// <summary>Two unit cards crossed at right angles about the vertical, centred on the origin, UV across each (v down).</summary>
    private static ArrayMesh ClumpMesh()
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        foreach (Vector3 across in new[] { Vector3.Right, Vector3.Back })
        {
            Vector3 a = -across * 0.5f + Vector3.Up * 0.5f, b = across * 0.5f + Vector3.Up * 0.5f;
            Vector3 c = across * 0.5f - Vector3.Up * 0.5f, d = -across * 0.5f - Vector3.Up * 0.5f;
            Vector3 normal = across.Cross(Vector3.Up);
            foreach ((Vector3 p, Vector2 uv) in new[] { (a, Vector2.Zero), (b, Vector2.Right), (c, Vector2.One), (a, Vector2.Zero), (c, Vector2.One), (d, Vector2.Down) })
            {
                tool.SetNormal(normal);
                tool.SetUV(uv);
                tool.AddVertex(p);
            }
        }

        return tool.Commit();
    }

    /// <summary>
    /// Four clumps of leaves side by side: a hundred or so small pointed leaves each, scattered through
    /// a lumpy round blob, thicker in the middle, lighter towards the top and darker underneath, with
    /// thin twigs among them. Luminance (tinted per clump) and alpha.
    /// </summary>
    private static ImageTexture Paint(Random random)
    {
        int width = CellPx * Variants;
        var cover = new float[width * CellPx];
        var shade = new float[width * CellPx];
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
        for (int v = 0; v < Variants; v++)
        {
            var middle = new Vector2(v * CellPx + CellPx * 0.5f, CellPx * 0.5f);
            // Twigs first, from the bottom of the clump outwards.
            for (int t = 0; t < 7; t++)
            {
                Vector2 from = middle + new Vector2(R(-20f, 20f), R(30f, 70f));
                Vector2 to = middle + new Vector2(R(-95f, 95f), R(-95f, 40f));
                for (float s = 0f; s <= 1f; s += 0.004f)
                {
                    Vector2 p = from.Lerp(to, s);
                    Stamp(cover, shade, width, v, p, 1.4f - s * 0.8f, 0.35f);
                }
            }

            float p1 = R(0f, Mathf.Tau), p2 = R(0f, Mathf.Tau);
            int leaves = random.Next(130, 180);
            for (int k = 0; k < leaves; k++)
            {
                // A spot in the lumpy blob, more of them towards its middle.
                float a = R(0f, Mathf.Tau);
                float edge = 112f * (1f + 0.14f * MathF.Sin(3f * a + p1) + 0.08f * MathF.Sin(5f * a + p2));
                float r = edge * MathF.Pow(R(0f, 1f), 0.6f);
                Vector2 at = middle + new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r * 0.9f);
                float length = R(12f, 20f), half = length * R(0.22f, 0.32f), angle = R(0f, Mathf.Tau);
                // Lighter towards the top, a little darker towards the rim (the clump's own shade).
                float tone = R(0.55f, 1f) * (1.08f - 0.3f * (at.Y / CellPx)) * (1f - 0.18f * (r / edge));
                Leaf(cover, shade, width, v, at, length, half, angle, Math.Clamp(tone, 0.3f, 1f));
            }
        }

        var bytes = new byte[width * CellPx * 2];
        for (int i = 0; i < width * CellPx; i++)
        {
            bytes[i * 2] = (byte)(Math.Clamp(shade[i], 0f, 1f) * 255f);
            bytes[i * 2 + 1] = (byte)(Math.Clamp(cover[i], 0f, 1f) * 255f);
        }

        Image image = Image.CreateFromData(width, CellPx, false, Image.Format.La8, bytes);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>A pointed leaf: widest a third of the way along, a little darker along its midrib.</summary>
    private static void Leaf(float[] cover, float[] shade, int width, int cell, Vector2 at, float length, float half, float angle, float tone)
    {
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        int x0 = Math.Max(cell * CellPx + 1, (int)(at.X - length)), x1 = Math.Min((cell + 1) * CellPx - 2, (int)(at.X + length));
        int y0 = Math.Max(1, (int)(at.Y - length)), y1 = Math.Min(CellPx - 2, (int)(at.Y + length));
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                Vector2 d = new Vector2(x + 0.5f, y + 0.5f) - at;
                float u = (d.X * c + d.Y * s) / length + 0.5f, w = -d.X * s + d.Y * c;
                if (u < 0f || u > 1f)
                {
                    continue;
                }

                float bladeHalf = half * MathF.Sin(MathF.PI * MathF.Pow(u, 0.7f));
                float inside = Math.Clamp(bladeHalf - MathF.Abs(w) + 0.5f, 0f, 1f);
                if (inside <= 0f)
                {
                    continue;
                }

                int i = y * width + x;
                float t = tone * (MathF.Abs(w) < 0.7f ? 0.82f : 1f) * (w > 0f ? 0.9f : 1f);
                shade[i] = Mathf.Lerp(shade[i], t, inside);
                cover[i] = MathF.Max(cover[i], inside);
            }
        }
    }

    private static void Stamp(float[] cover, float[] shade, int width, int cell, Vector2 p, float radius, float tone)
    {
        int x0 = Math.Max(cell * CellPx + 1, (int)(p.X - radius - 1f)), x1 = Math.Min((cell + 1) * CellPx - 2, (int)(p.X + radius + 1f));
        int y0 = Math.Max(1, (int)(p.Y - radius - 1f)), y1 = Math.Min(CellPx - 2, (int)(p.Y + radius + 1f));
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float inside = Math.Clamp(radius + 0.5f - p.DistanceTo(new Vector2(x + 0.5f, y + 0.5f)), 0f, 1f);
                if (inside > 0f)
                {
                    int i = y * width + x;
                    shade[i] = Mathf.Lerp(shade[i], tone, inside);
                    cover[i] = MathF.Max(cover[i], inside);
                }
            }
        }
    }
}
