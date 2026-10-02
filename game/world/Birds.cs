using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;

namespace Pb.Game.World;

/// <summary>
/// Crows wheeling over a place (presentation.jsonc "birds"): each flock circles round its point, each
/// bird on a circle and at a height of its own, banked into the turn, gliding with its wings a little
/// raised and now and then flapping in a burst (birds.gdshader beats the wings). Dark silhouettes in one
/// MultiMesh, moved every frame. On a level, a few more sit on the tops of its walls under the open sky,
/// turning now and then, until the camera comes close: then they flap up and away and join the wheeling.
/// Presentation only; seeded, so it's the same every run.
/// </summary>
public partial class Birds : Node3D
{
    /// <summary>How far the birds bank into the turn (radians), and how far they rise and fall (m).</summary>
    private const float Bank = 0.42f, Bob = 1.4f;

    /// <summary>A bird takes this long to climb from its perch onto its circle (s).</summary>
    private const float TakeOff = 2.5f;

    /// <summary>A feeding bird's peck at the ground and its hop take this long (s).</summary>
    private const float Peck = 0.35f, Hop = 0.25f;

    private MultiMesh? _multimesh;
    private MultiMesh? _perched;
    private Bird[] _birds = Array.Empty<Bird>();
    private BirdsDef _def = null!;
    private Random _random = new(1);

    public int Count => _birds.Length;

    /// <summary>
    /// Flocks round points given from <paramref name="origin"/>, varied by <paramref name="seed"/>, and,
    /// given a level, birds perched on its walls.
    /// </summary>
    public void Build(Vector3 origin, int seed, BirdsDef def, Pb.Sim.Level.LevelLayout? level = null, Pb.Sim.Collision.ICollisionWorld? world = null)
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

        // Perched: each on its spot until flushed, then onto a circle of its own above and nearby.
        int firstPerched = birds.Count;
        if (level is not null)
        {
            foreach ((Vector3 at, float yaw) in Perches(level, def.Perched))
            {
                birds.Add(new Bird
                {
                    State = Perch.Sitting,
                    Perch = at,
                    PerchYaw = yaw,
                    Yaw = yaw,
                    TurnIn = R(1f, 6f),
                    Center = at + new Vector3(R(-10f, 10f), 0f, R(-10f, 10f)),
                    Radius = R(10f, 20f),
                    Height = at.Y + R(12f, 22f),
                    Direction = _random.Next(2) == 0 ? 1f : -1f,
                    Speed = def.Speed_mps * R(0.85f, 1.15f),
                    Phase = R(0f, Mathf.Tau),
                    Rise = R(0f, Mathf.Tau),
                    NextFlap = 99f,
                });
            }
        }

        // Feeding: on the ground in a loose group or two, pecking and hopping about until flushed.
        if (level is not null && world is not null)
        {
            foreach ((Vector3 at, float yaw) in Feeding(level, world, def))
            {
                birds.Add(new Bird
                {
                    State = Perch.Sitting,
                    Ground = true,
                    Perch = at,
                    PerchYaw = yaw,
                    Yaw = yaw,
                    TurnIn = R(1f, 4f),
                    PeckIn = R(0.3f, 2f),
                    HopIn = R(2f, 7f),
                    Center = at + new Vector3(R(-10f, 10f), 0f, R(-10f, 10f)),
                    Radius = R(10f, 20f),
                    Height = at.Y + R(10f, 20f),
                    Direction = _random.Next(2) == 0 ? 1f : -1f,
                    Speed = def.Speed_mps * R(0.85f, 1.15f),
                    Phase = R(0f, Mathf.Tau),
                    Rise = R(0f, Mathf.Tau),
                    NextFlap = 99f,
                });
            }
        }

        _birds = birds.ToArray();
        _multimesh = null;
        _perched = null;
        if (_birds.Length == 0)
        {
            return;
        }

        if (_birds.Length > firstPerched)
        {
            _perched = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = PerchedMesh(def.Wingspan_m) };
            _perched.InstanceCount = _birds.Length;
            AddChild(new MultiMeshInstance3D
            {
                Name = "PerchedBirds",
                Multimesh = _perched,
                MaterialOverride = new StandardMaterial3D { AlbedoColor = Color.FromHtml(def.Color), Roughness = 0.42f },
            });
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

        Vector3? camera = IsInsideTree() ? GetViewport()?.GetCamera3D()?.GlobalPosition : null;
        var hidden = new Transform3D(Basis.FromScale(Vector3.Zero), Vector3.Zero);
        for (int i = 0; i < _birds.Length; i++)
        {
            ref Bird b = ref _birds[i];
            if (b.State == Perch.Sitting)
            {
                // Turning now and then; off when the camera comes close.
                if ((b.TurnIn -= dt) <= 0f)
                {
                    b.TurnTo = b.PerchYaw + R(-1.2f, 1.2f);
                    b.TurnIn = R(2f, 7f);
                }

                b.Yaw = Mathf.MoveToward(b.Yaw, b.TurnTo, dt * 3f);
                var pose = new Basis(Vector3.Up, b.Yaw);
                Vector3 at = b.Perch;
                if (b.Ground)
                {
                    // Pecking at the ground now and then, and hopping on a little way.
                    if ((b.PeckIn -= dt) <= 0f)
                    {
                        b.PeckT = 1f;
                        b.PeckIn = R(0.6f, 2.4f);
                    }

                    b.PeckT = MathF.Max(0f, b.PeckT - dt / Peck);
                    pose *= new Basis(Vector3.Right, -0.65f * MathF.Sin(MathF.PI * b.PeckT));
                    if ((b.HopIn -= dt) <= 0f)
                    {
                        b.HopT = 1f;
                        b.HopFrom = b.Perch;
                        b.Perch += new Basis(Vector3.Up, b.Yaw) * Vector3.Forward * R(0.15f, 0.35f);
                        b.HopIn = R(2.5f, 8f);
                    }

                    if (b.HopT > 0f)
                    {
                        b.HopT = MathF.Max(0f, b.HopT - dt / Hop);
                        at = b.Perch.Lerp(b.HopFrom, b.HopT) + Vector3.Up * (0.07f * MathF.Sin(MathF.PI * b.HopT));
                    }
                }

                _perched!.SetInstanceTransform(i, new Transform3D(pose, at));
                _multimesh.SetInstanceTransform(i, hidden);
                if (camera is { } eye && eye.DistanceTo(b.Perch) < _def.FlushDistance_m)
                {
                    b.State = Perch.Leaving;
                    b.Angle = Mathf.Atan2(b.Perch.Z - b.Center.Z, b.Perch.X - b.Center.X);
                }

                continue;
            }

            if (b.State == Perch.Leaving)
            {
                _perched!.SetInstanceTransform(i, hidden);
                b.Left += dt / TakeOff;
                if (b.Left >= 1f)
                {
                    b.State = Perch.Flying;
                    b.NextFlap = R(_def.FlapEvery_s[0], _def.FlapEvery_s[1]);
                }
            }
            else if (_perched is not null && i < _perched.InstanceCount)
            {
                _perched.SetInstanceTransform(i, hidden);
            }

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
            if (b.State == Perch.Leaving)
            {
                // Up off the perch, flapping hard, easing onto the circle.
                float t = b.Left * b.Left * (3f - 2f * b.Left);
                Vector3 from = b.Perch + Vector3.Up * 0.2f;
                forward = (position - from).LengthSquared() > 0.01f ? (position - from).Normalized() : forward;
                position = from.Lerp(position, t) + Vector3.Up * (Mathf.Sin(Mathf.Pi * b.Left) * 2f);
                up = Vector3.Up;
                b.Flap = 1f;
            }

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

    /// <summary>
    /// Spots for perched birds: along the tops of upright wall pieces at least 2.4 m up with nothing over
    /// them (the perimeter wall's coping, the buildings' parapets), a bird to a piece, facing off either side
    /// or along it.
    /// </summary>
    private List<(Vector3 At, float Yaw)> Perches(Pb.Sim.Level.LevelLayout level, int count)
    {
        var coping = new Dictionary<int, float>();
        foreach (Pb.Sim.Level.PlacedWall w in level.Walls)
        {
            coping[w.Owner] = w.Def.Dressing?.Coping is not null ? 0.07f : 0f;
        }

        var tops = new List<(Vector3 Mid, Vector3 Along, float Half)>();
        foreach (Pb.Sim.Level.LevelPrimitive p in level.Primitives)
        {
            if (p.Role != Pb.Sim.Level.PrimitiveRole.Wall || p.Kind != Pb.Sim.Level.PrimitiveKind.Box)
            {
                continue;
            }

            Pb.Sim.Collision.Aabb b = p.Bounds;
            Vector3 x = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitX, p.Rotation).ToGodot();
            Vector3 z = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ, p.Rotation).ToGodot();
            bool longX = p.HalfExtents.X >= p.HalfExtents.Z;
            float half = longX ? p.HalfExtents.X : p.HalfExtents.Z;
            if (b.Max.Y < 2.4f || half < 0.8f || MathF.Min(p.HalfExtents.X, p.HalfExtents.Z) > 0.4f || Covered(level, p, b))
            {
                continue;
            }

            float lift = coping.TryGetValue(p.Owner, out float c) ? c : 0f;
            tops.Add((new Vector3(p.Center.X, b.Max.Y + lift, p.Center.Z), longX ? x : z, half));
        }

        var spots = new List<(Vector3, float)>();
        for (int i = 0; i < count && tops.Count > 0; i++)
        {
            int k = _random.Next(tops.Count);
            (Vector3 mid, Vector3 along, float half) = tops[k];
            tops.RemoveAt(k);
            float yaw = Mathf.Atan2(-along.X, -along.Z) + (_random.Next(4) * Mathf.Pi * 0.5f) + R(-0.3f, 0.3f);
            spots.Add((mid + along * R(-half + 0.3f, half - 0.3f), yaw));
        }

        return spots;
    }

    /// <summary>
    /// Spots for feeding birds: one or two loose groups on the listed ground materials under the open sky,
    /// a metre or more clear of anything standing, each bird a few metres from the last, facing any way.
    /// </summary>
    private List<(Vector3 At, float Yaw)> Feeding(Pb.Sim.Level.LevelLayout level, Pb.Sim.Collision.ICollisionWorld world, BirdsDef def)
    {
        var spots = new List<(Vector3, float)>();
        if (def.Feeding <= 0)
        {
            return spots;
        }

        var survey = new GroundSurvey(level, world);
        var on = new HashSet<string>(def.FeedOn, StringComparer.Ordinal);
        Pb.Sim.Collision.Aabb b = level.Bounds;
        Vector3 last = Vector3.Zero;
        for (int attempt = 0; attempt < def.Feeding * 80 && spots.Count < def.Feeding; attempt++)
        {
            // A new group now and then, otherwise near the last bird down.
            bool fresh = spots.Count == 0 || _random.NextDouble() < 0.15;
            float x = fresh ? R(b.Min.X, b.Max.X) : last.X + R(-3.5f, 3.5f), z = fresh ? R(b.Min.Z, b.Max.Z) : last.Z + R(-3.5f, 3.5f);
            if (!on.Contains(survey.SurfaceAt(x, z, out float y)) || y >= GroundSurvey.GroundTop || !survey.OpenGround(x, z, 0.05f, out _)
                || world.SweepSphere(new System.Numerics.Vector3(x, 1.6f, z), new System.Numerics.Vector3(x, 0.9f, z), 0.8f, out _))
            {
                continue;
            }

            last = new Vector3(x, y, z);
            spots.Add((last, R(0f, Mathf.Tau)));
        }

        return spots;
    }

    /// <summary>Whether anything else of the level is over this wall piece's top.</summary>
    private static bool Covered(Pb.Sim.Level.LevelLayout level, Pb.Sim.Level.LevelPrimitive wall, Pb.Sim.Collision.Aabb top)
    {
        foreach (Pb.Sim.Level.LevelPrimitive p in level.Primitives)
        {
            Pb.Sim.Collision.Aabb b = p.Bounds;
            if (p != wall && b.Min.Y >= top.Max.Y - 0.05f && b.Min.X < top.Max.X && b.Max.X > top.Min.X && b.Min.Z < top.Max.Z && b.Max.Z > top.Min.Z)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A crow sitting, about a wingspan's half long, facing −Z: a body tilted head-up, a round head with a
    /// beak, a tail angled down behind and two legs to stand on.
    /// </summary>
    private static ArrayMesh PerchedMesh(float span)
    {
        float s = span / 0.9f;
        var shape = new ShapeMesh();
        shape.Place(Transform3D.Identity, 0.3f * s);
        foreach (float side in new[] { -1f, 1f })
        {
            shape.Rod(0, new Vector3(side * 0.025f, 0f, 0.01f) * s, new Vector3(side * 0.025f, 0.07f, 0.015f) * s, 0.006f * s, 4);
        }

        shape.Pillow(0, new Vector3(0f, 0.14f, 0.02f) * s, new Vector3(0.12f, 0.13f, 0.27f) * s, new Basis(Vector3.Right, 0.42f), 2.4f, 6, 10);
        shape.Pillow(0, new Vector3(0f, 0.235f, -0.105f) * s, new Vector3(0.075f, 0.075f, 0.085f) * s, Basis.Identity, 2f, 5, 8);
        shape.Lathe(0, new Vector3(0f, 0.228f, -0.14f) * s, ShapeMesh.BasisAlong(Vector3.Forward),
            new[] { new Vector2(0f, 0f), new Vector2(0.014f * s, 0f), new Vector2(0f, 0.055f * s) }, 6);
        shape.Box(0, new Vector3(0f, 0.105f, 0.19f) * s, new Vector3(0.055f, 0.012f, 0.15f) * s, new Basis(Vector3.Right, -0.35f));
        var mesh = new ArrayMesh();
        shape.Commit(mesh, _ => new StandardMaterial3D());
        return mesh;
    }

    private float R(float a, float b) => a + (float)_random.NextDouble() * (b - a);

    private enum Perch : byte
    {
        Flying,
        Sitting,
        Leaving,
    }

    private struct Bird
    {
        public Vector3 Center, Perch, HopFrom;
        public Perch State;
        public bool Ground;
        public float Radius, Height, Angle, Direction, Speed, Phase, Rise, NextFlap, FlapLeft, Flap;
        public float PerchYaw, Yaw, TurnTo, TurnIn, Left;
        public float PeckIn, PeckT, HopIn, HopT;
    }
}
