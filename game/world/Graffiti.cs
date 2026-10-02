using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Level;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.World;

/// <summary>
/// Graffiti on the outside walls (presentation.jsonc "graffiti"): throw-ups in fat bubble letters low
/// down, where someone crouching or standing could reach, and tags at arm's height, of invented words
/// (<see cref="GraffitiPainter"/>). Each lies flat on the outside face of a wall that stands on the
/// ground (a free-standing wall's face looking in; the buildings' walls get more than their share),
/// wholly on that face and clear of the other pieces on it, worn and bleached in the shader and drawn
/// under the rust streaks. Seeded by the level; one MultiMesh; looks only.
/// </summary>
public partial class Graffiti : Node3D
{
    private const float Lift = 0.0035f, Layer = 0.0002f;

    private static ImageTexture? _atlas;

    public int Count { get; private set; }

    private sealed record Face(Vector3 Centre, Vector3 Along, Vector3 Normal, float Half, float Foot, float Height, float Weight)
    {
        public List<(float From, float To)> Taken { get; } = new();
    }

    public void Build(LevelLayout level, GraffitiDef def, IReadOnlyList<(Vector3 At, float Half)>? piers = null)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        Count = 0;
        var faces = new List<Face>();
        float total = 0f;
        var buildings = new HashSet<int>();
        foreach (PlacedBuilding b in level.Buildings)
        {
            buildings.Add(b.Owner);
        }

        Pb.Sim.Collision.Aabb bounds = level.Bounds;
        var middle = new Vector3((bounds.Min.X + bounds.Max.X) * 0.5f, 0f, (bounds.Min.Z + bounds.Max.Z) * 0.5f);
        foreach (LevelPrimitive p in level.Primitives)
        {
            if (p.Role != PrimitiveRole.Wall || p.Kind != PrimitiveKind.Box || p.Bounds.Min.Y > 0.3f || p.Height < 1.2f)
            {
                continue;
            }

            Vector3 x = SVector3.Transform(SVector3.UnitX, p.Rotation).ToGodot(), z = SVector3.Transform(SVector3.UnitZ, p.Rotation).ToGodot();
            if (MathF.Abs(x.Y) > 0.02f || MathF.Abs(z.Y) > 0.02f)
            {
                continue;
            }

            bool longX = p.HalfExtents.X >= p.HalfExtents.Z;
            Vector3 along = longX ? x : z, normal = longX ? z : x;
            float half = longX ? p.HalfExtents.X : p.HalfExtents.Z, thick = longX ? p.HalfExtents.Z : p.HalfExtents.X;
            if (half < 0.6f)
            {
                continue;
            }

            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 n = normal * side;
                Vector3 face = p.Center.ToGodot() + n * thick;
                bool building = buildings.Contains(p.Owner);
                // A free-standing wall's outer face looks away from the place: nobody would see it.
                if (Indoor(level, new Vector3(face.X, p.Bounds.Min.Y + 1.2f, face.Z) + n * 0.4f) ||
                    !building && n.Dot(new Vector3(middle.X - face.X, 0f, middle.Z - face.Z)) <= 0f)
                {
                    continue;
                }

                // The buildings' walls, round the yard, get more than their share.
                float weight = half * (building ? 2f : 1f);
                var f = new Face(face, along, n, half, p.Bounds.Min.Y, p.Height, weight);
                // Piers standing proud of this face are taken: nothing is painted behind one.
                foreach ((Vector3 at, float pierHalf) in piers ?? Array.Empty<(Vector3, float)>())
                {
                    Vector3 off = at - face;
                    float across = MathF.Abs(off.Dot(n)), u = off.Dot(along);
                    if (across < thick + pierHalf + 0.1f && MathF.Abs(u) < half + pierHalf)
                    {
                        f.Taken.Add((u - pierHalf - 0.05f, u + pierHalf + 0.05f));
                    }
                }

                faces.Add(f);
                total += weight;
            }
        }

        if (faces.Count == 0 || def.Words.Length == 0)
        {
            return;
        }

        var random = new Random(LevelBuilder.StableHash(level.Id) ^ 0x6AFF);
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
        Face Pick()
        {
            float roll = R(0f, total);
            foreach (Face f in faces)
            {
                roll -= f.Weight;
                if (roll <= 0f)
                {
                    return f;
                }
            }

            return faces[^1];
        }

        var cards = new List<(Transform3D Transform, Color Data, Color Shade)>();
        void Place(bool throwUp, int count, float[] width_m)
        {
            for (int placed = 0, tries = 0; placed < count && tries < count * 30; tries++)
            {
                Face f = Pick();
                float width = R(width_m[0], width_m[1]);
                float height = width * GraffitiPainter.CellHeight / GraffitiPainter.CellWidth;
                // Throw-ups low down (the cell's own margin lifts the letters); tags round arm's height.
                float centreY = throwUp ? R(0.25f, 0.55f) + height * 0.5f : R(1.3f, 1.8f);
                float room = f.Half - width * 0.5f - 0.15f;
                if (room < 0f || centreY + height * 0.5f > f.Height - 0.1f)
                {
                    continue;
                }

                float at = R(-room, room);
                if (f.Taken.Exists(t => at + width * 0.5f > t.From && at - width * 0.5f < t.To))
                {
                    continue;
                }

                f.Taken.Add((at - width * 0.5f - 0.3f, at + width * 0.5f + 0.3f));
                Vector3 right = Vector3.Up.Cross(f.Normal);
                float tilt = R(-0.04f, 0.04f);
                var basis = new Basis(f.Normal, tilt) * new Basis(right * width, Vector3.Up * height, f.Normal);
                Vector3 origin = f.Centre + f.Along * at + Vector3.Up * (f.Foot + centreY - f.Centre.Y) + f.Normal * (Lift + cards.Count % 8 * Layer);
                int cell = throwUp ? random.Next(GraffitiPainter.Columns * 2) : GraffitiPainter.Columns * 2 + random.Next(GraffitiPainter.Columns * 2);
                float shade = R(0.85f, 1f);
                cards.Add((new Transform3D(basis, origin), new Color(cell, R(def.Opacity[0], def.Opacity[1]), R(def.Wear[0], def.Wear[1]), 0f), new Color(shade, shade, shade)));
                placed++;
            }
        }

        Place(throwUp: true, def.ThrowUps, def.ThrowUpWidth_m);
        Place(throwUp: false, def.Tags, def.TagWidth_m);
        Count = cards.Count;
        if (cards.Count == 0)
        {
            return;
        }

        _atlas ??= new GraffitiPainter(0x6AFF, def.Words, Colours(def.Fills), Colours(def.Outlines)).Paint();
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/graffiti.gdshader"), RenderPriority = -2 };
        material.SetShaderParameter("atlas", _atlas);
        material.SetShaderParameter("grid", new Vector2(GraffitiPainter.Columns, GraffitiPainter.Rows));
        material.SetShaderParameter("bleach", def.Bleach);
        material.SetShaderParameter("fade_start", def.FadeStart_m);
        material.SetShaderParameter("fade_end", def.FadeEnd_m);
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = CardMesh(),
        };
        multimesh.InstanceCount = cards.Count;
        for (int i = 0; i < cards.Count; i++)
        {
            multimesh.SetInstanceTransform(i, cards[i].Transform);
            multimesh.SetInstanceColor(i, cards[i].Shade);
            multimesh.SetInstanceCustomData(i, cards[i].Data);
        }

        AddChild(new MultiMeshInstance3D { Name = "Pieces", Multimesh = multimesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    private static Color[] Colours(string[] html)
    {
        var colours = new Color[html.Length];
        for (int i = 0; i < html.Length; i++)
        {
            colours[i] = Color.FromHtml(html[i]);
        }

        return colours;
    }

    private static bool Indoor(LevelLayout level, Vector3 at)
    {
        foreach (AreaSpec area in level.Areas)
        {
            Pb.Sim.Collision.Aabb b = area.Box;
            if (area.Indoor && at.X >= b.Min.X && at.X <= b.Max.X && at.Y >= b.Min.Y && at.Y <= b.Max.Y && at.Z >= b.Min.Z && at.Z <= b.Max.Z)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A unit card on the origin in the XY plane, facing +Z, UV across it (v down).</summary>
    private static ArrayMesh CardMesh()
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 a = new(-0.5f, 0.5f, 0f), b = new(0.5f, 0.5f, 0f), c = new(0.5f, -0.5f, 0f), d = new(-0.5f, -0.5f, 0f);
        foreach ((Vector3 p, Vector2 uv) in new[] { (a, Vector2.Zero), (b, Vector2.Right), (c, Vector2.One), (a, Vector2.Zero), (c, Vector2.One), (d, Vector2.Down) })
        {
            tool.SetNormal(Vector3.Back);
            tool.SetUV(uv);
            tool.AddVertex(p);
        }

        return tool.Commit();
    }
}
