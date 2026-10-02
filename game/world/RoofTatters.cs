using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Level;

namespace Pb.Game.World;

/// <summary>
/// Tatters hanging from the edges of the holes where a roof has fallen in (presentation.jsonc
/// "roofTatters"): torn strips of roofing felt and insulation along each edge, swaying and fluttering in
/// the draught (snagged_bags.gdshader, as the bags on the wire, with the ground wind). Up under the roof,
/// clear of anyone's head; seeded by the level; one MultiMesh; looks only.
/// </summary>
public partial class RoofTatters : Node3D
{
    public int Count { get; private set; }

    public void Build(LevelLayout level, RoofTattersDef def, Vector2 wind)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        Count = 0;
        var random = new Random(LevelBuilder.StableHash(level.Id) ^ 0x7A77);
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
        var strips = new List<(Transform3D Transform, Color Color, float Phase)>();
        foreach (Aperture hole in level.Apertures)
        {
            if (hole.Kind != ApertureKind.RoofHole)
            {
                continue;
            }

            Vector3 u = hole.U.ToGodot(), v = hole.V.ToGodot();
            // The four edges: where each starts, which way it runs, how long it is, and which way is out of the hole.
            var edges = new (Vector3 From, Vector3 Along, float Length, Vector3 Out)[]
            {
                (hole.At(-1f, 1f).ToGodot(), u, hole.HalfWidth * 2f, v),
                (hole.At(-1f, -1f).ToGodot(), u, hole.HalfWidth * 2f, -v),
                (hole.At(1f, -1f).ToGodot(), v, hole.HalfHeight * 2f, u),
                (hole.At(-1f, -1f).ToGodot(), v, hole.HalfHeight * 2f, -u),
            };
            foreach ((Vector3 from, Vector3 along, float length, Vector3 outward) in edges)
            {
                for (float t = R(0.1f, def.Every_m); t < length - 0.1f; t += def.Every_m * R(0.7f, 1.3f))
                {
                    if (random.NextDouble() > def.Share)
                    {
                        continue;
                    }

                    // Mostly short, now and then a long one.
                    float hang = Mathf.Lerp(def.Length_m[0], def.Length_m[1], MathF.Pow(R(0f, 1f), 2f)), width = R(def.Width_m[0], def.Width_m[1]);
                    // Hanging from just under the edge of what's left of the roof, its width along the edge, a little askew.
                    var basis = new Basis(along * width, Vector3.Up * hang, outward * width * 0.4f) * new Basis(Vector3.Back, R(-0.2f, 0.2f));
                    Color color = Color.FromHtml(def.Colors[random.Next(def.Colors.Length)]) * R(0.8f, 1f);
                    color.A = 1f;
                    strips.Add((new Transform3D(basis, from + along * t + outward * 0.03f), color, R(0f, Mathf.Tau)));
                }
            }
        }

        Count = strips.Count;
        if (strips.Count == 0)
        {
            return;
        }

        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/snagged_bags.gdshader") };
        material.SetShaderParameter("wind", new Vector3(wind.X, 0f, wind.Y).Normalized());
        material.SetShaderParameter("lean_least", def.Lean_rad[0]);
        material.SetShaderParameter("lean_most", def.Lean_rad[1]);
        material.SetShaderParameter("flap", def.Flap_m);
        material.SetShaderParameter("flutter", def.Flutter_hz * Mathf.Tau);
        // Felt and old insulation: matte, and nothing shows through.
        material.SetShaderParameter("roughness", 0.95f);
        material.SetShaderParameter("specular", 0.1f);
        material.SetShaderParameter("backlight", 0.05f);
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = StripMesh(new Random(0x7A78)),
        };
        multimesh.InstanceCount = strips.Count;
        for (int i = 0; i < strips.Count; i++)
        {
            multimesh.SetInstanceTransform(i, strips[i].Transform);
            multimesh.SetInstanceColor(i, strips[i].Color);
            multimesh.SetInstanceCustomData(i, new Color(strips[i].Phase, 0f, 0f, 0f));
        }

        AddChild(new MultiMeshInstance3D { Name = "Strips", Multimesh = multimesh, MaterialOverride = material });
    }

    /// <summary>
    /// A torn strip hanging from the origin: a unit across (x) and a unit down (−y), narrowing to a ragged
    /// end and curling a little. UV.y runs 0 at the top to 1 at the bottom, so the shader keeps the top still.
    /// </summary>
    private static ArrayMesh StripMesh(Random random)
    {
        const int across = 2, down = 6;
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        var points = new Vector3[across + 1, down + 1];
        for (int j = 0; j <= down; j++)
        {
            float v = (float)j / down;
            float width = 1f - 0.3f * v;
            for (int i = 0; i <= across; i++)
            {
                float x = ((float)i / across - 0.5f) * width;
                // The torn end: ragged, each corner torn off at its own height.
                float y = j == down ? -v + (float)random.NextDouble() * 0.3f : -v;
                float curl = 0.12f * v * v * (i == 1 ? 1f : 0.6f) + (float)(random.NextDouble() - 0.5) * 0.06f * v;
                points[i, j] = new Vector3(x + (float)(random.NextDouble() - 0.5) * 0.06f * v, y, curl);
            }
        }

        void Vertex(int i, int j)
        {
            tool.SetUV(new Vector2((float)i / across, (float)j / down));
            tool.AddVertex(points[i, j]);
        }

        for (int j = 0; j < down; j++)
        {
            for (int i = 0; i < across; i++)
            {
                Vertex(i, j);
                Vertex(i + 1, j);
                Vertex(i + 1, j + 1);
                Vertex(i, j);
                Vertex(i + 1, j + 1);
                Vertex(i, j + 1);
            }
        }

        tool.GenerateNormals();
        return tool.Commit();
    }
}
