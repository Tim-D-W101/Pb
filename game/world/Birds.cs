using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;

namespace Pb.Game.World;

/// <summary>
/// Crows wheeling over a place (presentation.jsonc "birds"): each flock circles round its point, each
/// bird on a circle and at a height of its own, banked into the turn, gliding with its wings a little
/// raised and now and then flapping in a burst (birds.gdshader beats the wings). Dark silhouettes in one
/// MultiMesh, moved every frame. Presentation only; seeded, so it's the same every run.
/// </summary>
public partial class Birds : Node3D
{
    /// <summary>How far the birds bank into the turn (radians), and how far they rise and fall (m).</summary>
    private const float Bank = 0.42f, Bob = 1.4f;

    private MultiMesh? _multimesh;
    private Bird[] _birds = Array.Empty<Bird>();
    private BirdsDef _def = null!;
    private Random _random = new(1);

    public int Count => _birds.Length;

    /// <summary>Flocks round points given from <paramref name="origin"/>, varied by <paramref name="seed"/>.</summary>
    public void Build(Vector3 origin, int seed, BirdsDef def)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        _def = def;
        _random = new Random(seed ^ 0xB12D5);
        var birds = new List<Bird>();
        foreach (FlockDef flock in def.Flocks)
        {
            Vector3 center = origin + new Vector3(flock.Center_m[0], 0f, flock.Center_m[1]);
            // A flock wheels one way together.
            float direction = _random.Next(2) == 0 ? 1f : -1f;
            for (int i = 0; i < flock.Count; i++)
            {
                birds.Add(new Bird
                {
                    Center = center + new Vector3(R(-4f, 4f), 0f, R(-4f, 4f)),
                    Radius = R(flock.Radius_m[0], flock.Radius_m[1]),
                    Height = R(flock.Height_m[0], flock.Height_m[1]),
                    Angle = R(0f, Mathf.Tau),
                    Direction = direction,
                    Speed = def.Speed_mps * R(0.85f, 1.15f),
                    Phase = R(0f, Mathf.Tau),
                    Rise = R(0f, Mathf.Tau),
                    NextFlap = R(0f, def.FlapEvery_s[1]),
                });
            }
        }

        _birds = birds.ToArray();
        _multimesh = null;
        if (_birds.Length == 0)
        {
            return;
        }

        float root = def.Wingspan_m * 0.04f;
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/birds.gdshader") };
        material.SetShaderParameter("color", Color.FromHtml(def.Color));
        material.SetShaderParameter("root", root);
        _multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = BirdMesh(def.Wingspan_m, root),
        };
        _multimesh.InstanceCount = _birds.Length;
        AddChild(new MultiMeshInstance3D
        {
            Name = "Birds",
            Multimesh = _multimesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        Update(0f);
    }

    public override void _Process(double delta) => Update((float)delta);

    private void Update(float dt)
    {
        if (_multimesh is null)
        {
            return;
        }

        for (int i = 0; i < _birds.Length; i++)
        {
            ref Bird b = ref _birds[i];
            b.Angle += b.Direction * b.Speed / b.Radius * dt;
            b.Rise += dt * 0.35f;
            // A burst of wingbeats every few seconds, eased in and out.
            b.NextFlap -= dt;
            if (b.NextFlap <= 0f)
            {
                b.FlapLeft = R(_def.FlapFor_s[0], _def.FlapFor_s[1]);
                b.NextFlap = b.FlapLeft + R(_def.FlapEvery_s[0], _def.FlapEvery_s[1]);
            }

            b.FlapLeft -= dt;
            b.Flap = Mathf.MoveToward(b.Flap, b.FlapLeft > 0f ? 1f : 0f, dt * 3f);

            var outward = new Vector3(Mathf.Cos(b.Angle), 0f, Mathf.Sin(b.Angle));
            Vector3 position = b.Center + outward * b.Radius + Vector3.Up * (b.Height + Mathf.Sin(b.Rise) * Bob);
            // Along the circle, climbing or sinking a little with the bob, banked towards the middle.
            Vector3 forward = new Vector3(-outward.Z, 0f, outward.X) * b.Direction + Vector3.Up * (Mathf.Cos(b.Rise) * Bob * 0.35f / b.Radius);
            Vector3 up = (Vector3.Up - outward * Mathf.Tan(Bank * (1f - 0.5f * b.Flap))).Normalized();
            Basis basis = Basis.LookingAt(forward.Normalized(), up);
            _multimesh.SetInstanceTransform(i, new Transform3D(basis, position));
            _multimesh.SetInstanceCustomData(i, new Color(b.Phase, b.Flap, 0f, 0f));
        }
    }

    /// <summary>
    /// A bird seen from below, flat: a narrow body with its head forward (−Z), a tail behind, and two
    /// wings swept a little back, from their roots out to the tips in three spans so the tips can lag.
    /// </summary>
    private static ArrayMesh BirdMesh(float span, float root)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            foreach (Vector3 v in new[] { a, b, c })
            {
                tool.SetNormal(Vector3.Up);
                tool.AddVertex(v);
            }
        }

        // Body: head, shoulders, waist, tail fan, all within the wing roots.
        Vector3 nose = new(0f, 0f, -0.24f * span), shoulderL = new(-root, 0f, -0.06f * span), shoulderR = new(root, 0f, -0.06f * span);
        Vector3 hipL = new(-root, 0f, 0.13f * span), hipR = new(root, 0f, 0.13f * span);
        Vector3 tailL = new(-root * 0.95f, 0f, 0.33f * span), tailR = new(root * 0.95f, 0f, 0.33f * span), tailMid = new(0f, 0f, 0.31f * span);
        Tri(nose, shoulderR, shoulderL);
        Tri(shoulderL, shoulderR, hipR);
        Tri(shoulderL, hipR, hipL);
        Tri(hipL, hipR, tailR);
        Tri(hipL, tailR, tailMid);
        Tri(hipL, tailMid, tailL);

        // Wings: the leading edge sweeps back to the tip, the trailing edge comes forward to it.
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 lead0 = new(side * root, 0f, -0.06f * span), trail0 = new(side * root, 0f, 0.13f * span);
            for (int k = 1; k <= 3; k++)
            {
                float t = k / 3f;
                float x = Mathf.Lerp(root, span * 0.5f, t);
                Vector3 lead1 = new(side * x, 0f, Mathf.Lerp(-0.06f, -0.01f, t) * span);
                Vector3 trail1 = new(side * x, 0f, Mathf.Lerp(0.13f, k == 3 ? 0.03f : 0.09f, t) * span);
                Tri(lead0, lead1, trail1);
                Tri(lead0, trail1, trail0);
                lead0 = lead1;
                trail0 = trail1;
            }
        }

        return tool.Commit();
    }

    private float R(float a, float b) => a + (float)_random.NextDouble() * (b - a);

    private struct Bird
    {
        public Vector3 Center;
        public float Radius, Height, Angle, Direction, Speed, Phase, Rise, NextFlap, FlapLeft, Flap;
    }
}
