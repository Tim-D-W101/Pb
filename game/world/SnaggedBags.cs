using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Level;

namespace Pb.Game.World;

/// <summary>
/// A strand of the walls' barbed wire, between two brackets (<see cref="WallDressing"/>): its ends,
/// how far it sags in the middle, and the height of the top of the wall under it.
/// </summary>
public readonly record struct WireStrand(Vector3 From, Vector3 To, float Sag_m, float Floor_m);

/// <summary>
/// Plastic bags caught on the barbed wire (presentation.jsonc "snaggedBags"): each hangs from a point
/// on a strand (<see cref="LevelBuilder.Strands"/>), no lower than the top of the wall under it,
/// crumpled, held out by the ground wind and flapping (snagged_bags.gdshader). Seeded by the level; one
/// MultiMesh; looks only.
/// </summary>
public partial class SnaggedBags : Node3D
{
    public int Count { get; private set; }

    public void Build(LevelLayout level, IReadOnlyList<WireStrand> strands, SnaggedBagsDef def, Vector2 wind)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        Count = 0;
        if (strands.Count == 0 || def.Count == 0)
        {
            return;
        }

        var random = new Random(LevelBuilder.StableHash(level.Id) ^ 0xBA65);
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
        var taken = new HashSet<int>();
        var bags = new List<(Transform3D Transform, Color Color, float Phase)>();
        for (int tries = 0; bags.Count < def.Count && tries < def.Count * 10; tries++)
        {
            int i = random.Next(strands.Count);
            if (!taken.Add(i))
            {
                continue;
            }

            WireStrand strand = strands[i];
            float f = R(0.2f, 0.8f);
            Vector3 at = strand.From.Lerp(strand.To, f) + Vector3.Down * (strand.Sag_m * 4f * f * (1f - f));
            Vector3 along = strand.To - strand.From;
            along = new Vector3(along.X, 0f, along.Z).Normalized();
            // A strand low over the wall has no room under it for a bag, even held out at the least angle.
            float most = MathF.Min(def.Size_m[1], (at.Y - strand.Floor_m - 0.05f) / MathF.Cos(def.Lean_rad[0]));
            if (most < def.Size_m[0] || along.LengthSquared() < 0.5f)
            {
                continue;
            }

            float size = R(def.Size_m[0], most);
            Vector3 across = along.Cross(Vector3.Up);
            // Hanging below the strand, a little askew, its width along the wire.
            var basis = new Basis(along * size * R(0.6f, 0.8f), Vector3.Up * size, across * size * 0.6f) * new Basis(Vector3.Back, R(-0.25f, 0.25f));
            Color color = Color.FromHtml(def.Colors[random.Next(def.Colors.Length)]) * R(0.8f, 1f);
            color.A = 1f;
            bags.Add((new Transform3D(basis, at), color, R(0f, Mathf.Tau)));
        }

        Count = bags.Count;
        if (bags.Count == 0)
        {
            return;
        }

        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/snagged_bags.gdshader") };
        material.SetShaderParameter("wind", new Vector3(wind.X, 0f, wind.Y).Normalized());
        material.SetShaderParameter("lean_least", def.Lean_rad[0]);
        material.SetShaderParameter("lean_most", def.Lean_rad[1]);
        material.SetShaderParameter("flap", def.Flap_m);
        material.SetShaderParameter("flutter", def.Flutter_hz * Mathf.Tau);
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = BagMesh(new Random(0xBA66)),
        };
        multimesh.InstanceCount = bags.Count;
        for (int i = 0; i < bags.Count; i++)
        {
            multimesh.SetInstanceTransform(i, bags[i].Transform);
            multimesh.SetInstanceColor(i, bags[i].Color);
            multimesh.SetInstanceCustomData(i, new Color(bags[i].Phase, 0f, 0f, 0f));
        }

        AddChild(new MultiMeshInstance3D { Name = "Bags", Multimesh = multimesh, MaterialOverride = material });
    }

    /// <summary>
    /// A crumpled bag hanging from the origin: a sheet a unit across (x) and a unit down (−y), gathered at
    /// the top where it's caught, puffed out a little and creased below, its open end split into the two
    /// handles. UV.y runs 0 at the top to 1 at the bottom, so the shader keeps the caught top still.
    /// </summary>
    private static ArrayMesh BagMesh(Random random)
    {
        const int across = 6, down = 6;
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        var points = new Vector3[across + 1, down + 1];
        for (int j = 0; j <= down; j++)
        {
            float v = (float)j / down;
            // Gathered to a point at the top, full width from halfway down.
            float width = 0.12f + 0.88f * MathF.Sin(MathF.PI * 0.5f * MathF.Min(1f, v * 2f));
            for (int i = 0; i <= across; i++)
            {
                float u = (float)i / across - 0.5f;
                // The open end dips up between the handles.
                float notch = j == down ? 0.3f * MathF.Max(0f, 1f - MathF.Abs(u) * 4f) : 0f;
                float crease = (float)(random.NextDouble() - 0.5) * 0.08f * v;
                float puff = MathF.Cos(u * MathF.PI) * 0.15f * MathF.Sin(MathF.PI * MathF.Min(1f, v * 1.3f));
                points[i, j] = new Vector3(u * width + crease, -v + notch, puff + (float)(random.NextDouble() - 0.5) * 0.12f * v);
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
