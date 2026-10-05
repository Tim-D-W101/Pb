using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Level;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.World;

/// <summary>
/// Soft contact shadows on the floor round the foot of walls, columns and props (presentation.jsonc
/// "contactShadows"), so they sit on the ground on presets without ambient occlusion (hidden where the
/// preset has SSAO): a card round each upright piece standing on a floor or the ground, a wall's pieces
/// joined into straight runs first, darkest at its footprint and fading out over a reach
/// (contact_shadows.gdshader multiplies what's under it). One MultiMesh; presentation only.
/// </summary>
public partial class ContactShadows : Node3D
{
    /// <summary>Cards sit this far off the floor.</summary>
    private const float Lift = 0.003f;

    public int Count { get; private set; }

    public void Build(LevelLayout level, ContactShadowsDef def)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        var floors = new List<Pb.Sim.Collision.Aabb>();
        foreach (LevelPrimitive p in level.Primitives)
        {
            if (p.Role == PrimitiveRole.Floor && p.Kind == PrimitiveKind.Box)
            {
                floors.Add(p.Bounds);
            }
        }

        var cards = new List<(Transform3D Transform, Color Foot)>();
        void Card(Vector3 at, Vector3 x, Vector3 z, float hx, float hz, float corner, float reach, float strength)
        {
            // Right along x, up along −z, so right × up is the floor's normal.
            var basis = new Basis(x * (2f * (hx + reach)), -z * (2f * (hz + reach)), Vector3.Up);
            cards.Add((new Transform3D(basis, at + Vector3.Up * (Lift + (cards.Count % 4) * 0.0003f)), new Color(hx, hz, corner, strength)));
        }

        var runs = new List<Run>();
        foreach (LevelPrimitive p in level.Primitives)
        {
            if (p.Role is not (PrimitiveRole.Wall or PrimitiveRole.Column or PrimitiveRole.Prop) || p.Height < def.MinHeight_m)
            {
                continue;
            }

            Vector3 up = SVector3.Transform(SVector3.UnitY, p.Rotation).ToGodot();
            Pb.Sim.Collision.Aabb b = p.Bounds;
            float footY = b.Min.Y;
            var at = new Vector3(p.Center.X, footY, p.Center.Z);
            if (!OnFloor(floors, at, footY))
            {
                continue;
            }

            if (up.Y > 0.95f)
            {
                Vector3 x = SVector3.Transform(SVector3.UnitX, p.Rotation).ToGodot();
                Vector3 z = SVector3.Transform(SVector3.UnitZ, p.Rotation).ToGodot();
                x = new Vector3(x.X, 0f, x.Z).Normalized();
                z = new Vector3(z.X, 0f, z.Z).Normalized();
                if (p.Role == PrimitiveRole.Wall && p.Kind == PrimitiveKind.Box)
                {
                    bool longX = p.HalfExtents.X >= p.HalfExtents.Z;
                    runs.Add(new Run(at, longX ? x : z, longX ? p.HalfExtents.X : p.HalfExtents.Z, longX ? p.HalfExtents.Z : p.HalfExtents.X));
                    continue;
                }

                bool round = p.Kind == PrimitiveKind.Cylinder;
                Card(at, x, z, p.HalfExtents.X, round ? p.HalfExtents.X : p.HalfExtents.Z, round ? p.HalfExtents.X : 0.03f,
                    p.Role == PrimitiveRole.Prop ? def.PropReach_m : def.WallReach_m, p.Role == PrimitiveRole.Prop ? def.PropStrength : def.WallStrength);
            }
            else
            {
                // Tipped over: its bounds, square on.
                float hx = (b.Max.X - b.Min.X) * 0.5f, hz = (b.Max.Z - b.Min.Z) * 0.5f;
                Card(new Vector3((b.Min.X + b.Max.X) * 0.5f, footY, (b.Min.Z + b.Max.Z) * 0.5f), Vector3.Right, Vector3.Back, hx, hz, MathF.Min(hx, hz),
                    p.Role == PrimitiveRole.Prop ? def.PropReach_m : def.WallReach_m, p.Role == PrimitiveRole.Prop ? def.PropStrength : def.WallStrength);
            }
        }

        // A wall is in pieces round its doors and windows (and under them, to the sill): one card per run of
        // pieces that carry on from each other, so the shadows don't double up where they meet.
        Merge(runs);
        foreach (Run r in runs)
        {
            if (!r.Gone)
            {
                Card(r.Centre, r.Along, r.Along.Cross(Vector3.Up), r.Half, r.Thick, 0.03f, def.WallReach_m, def.WallStrength);
            }
        }

        Count = cards.Count;
        if (cards.Count == 0)
        {
            return;
        }

        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/contact_shadows.gdshader") };
        material.SetShaderParameter("fade_start", def.FadeStart_m);
        material.SetShaderParameter("fade_end", def.FadeEnd_m);
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = CardMesh(),
        };
        multimesh.InstanceCount = cards.Count;
        for (int i = 0; i < cards.Count; i++)
        {
            multimesh.SetInstanceTransform(i, cards[i].Transform);
            multimesh.SetInstanceCustomData(i, cards[i].Foot);
        }

        AddChild(new MultiMeshInstance3D
        {
            Name = "ContactShadowCards",
            Multimesh = multimesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <summary>A straight run of wall along the floor: its foot's centre, direction, half length and half thickness.</summary>
    private sealed class Run(Vector3 centre, Vector3 along, float half, float thick)
    {
        public Vector3 Centre = centre;
        public readonly Vector3 Along = along;
        public float Half = half;
        public readonly float Thick = thick;
        public bool Gone;
    }

    /// <summary>Joins runs that lie on one line at one height and thickness and touch or overlap, end to end.</summary>
    private static void Merge(List<Run> runs)
    {
        const float Slack = 0.02f;
        foreach (Run a in runs)
        {
            for (bool grew = !a.Gone; grew;)
            {
                grew = false;
                foreach (Run b in runs)
                {
                    if (b == a || b.Gone || MathF.Abs(a.Along.Dot(b.Along)) < 0.999f || MathF.Abs(a.Thick - b.Thick) > Slack)
                    {
                        continue;
                    }

                    Vector3 d = b.Centre - a.Centre;
                    float s = d.Dot(a.Along);
                    if (MathF.Abs(d.Y) > Slack || (d - a.Along * s).Length() > Slack || MathF.Abs(s) > a.Half + b.Half + Slack)
                    {
                        continue;
                    }

                    float lo = MathF.Min(-a.Half, s - b.Half), hi = MathF.Max(a.Half, s + b.Half);
                    a.Centre += a.Along * ((lo + hi) * 0.5f);
                    a.Half = (hi - lo) * 0.5f;
                    b.Gone = true;
                    grew = true;
                }
            }
        }
    }

    /// <summary>Whether this foot stands on the ground or on a floor's top.</summary>
    private static bool OnFloor(List<Pb.Sim.Collision.Aabb> floors, Vector3 at, float footY)
    {
        if (MathF.Abs(footY) < 0.06f)
        {
            return true;
        }

        foreach (Pb.Sim.Collision.Aabb b in floors)
        {
            if (at.X >= b.Min.X && at.X <= b.Max.X && at.Z >= b.Min.Z && at.Z <= b.Max.Z && MathF.Abs(b.Max.Y - footY) < 0.06f)
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
