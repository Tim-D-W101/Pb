using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Ballistics;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Collision;
using Pb.Sim.Level;
using Pb.Sim.Vehicles;

namespace Pb.Game.World;

/// <summary>
/// The vehicles as you see and walk into them (Phase 5; M5.7 their handling): each a buggy built in code — a tub of
/// panels on a tube frame, a roll cage, two seats, a steering wheel and four wheels on dampers — posed from the sim's
/// <see cref="VehicleSet"/> smoothly between ticks, leaning and pitching as the sim has it. Each wheel rides the ground
/// under it on its spring (found from the drawn pose, so they never float or sink), turns as far as the sim steers it and
/// rolls (or spins, or locks) as the sim has it; the steering wheel turns with the front wheels; the back tyres throw up
/// what they run on (dust, grass, mud, rubber smoke) as they roll and scrub. An <see cref="AnimatableBody3D"/> box moves
/// with each, for walking into. A buggy is drawn on a render layer of its own, so paint that hits it stays on it as it
/// drives off, and paint on the ground never lands on it.
/// </summary>
public partial class VehicleViews : Node3D
{
    /// <summary>The render layer vehicles are drawn on.</summary>
    public const uint RenderLayer = 1u << 5;

    private const float WheelWidth = 0.26f;

    /// <summary>The steering wheel's rim: its radius (m), and the hands' grips on it (rad round from its top, to the right).</summary>
    private const float RimRadius = 0.185f;

    private const float GripAngle = 1.05f;

    private readonly List<View> _views = new();
    private VehicleSet? _vehicles;
    private SimWorld? _sim;
    private VehicleViewDef _def = new();
    private Color _dust, _grass, _mud, _smoke;

    /// <summary>The level's vehicle views (one level at a time): the seat camera and the drivers' hands look here.</summary>
    public static VehicleViews? Current { get; private set; }

    /// <summary>On a joining copy driving one: how far off the vehicle it drives is drawn (the last correction, easing out).</summary>
    public Func<int, System.Numerics.Vector3>? DrawnOffset { get; set; }

    public int Count => _views.Count;

    public VehicleViewDef Def => _def;

    private sealed class View
    {
        public required MeshInstance3D Body;
        public required AnimatableBody3D Wall;
        public required Node3D Steering;
        public readonly Node3D[] Hubs = new Node3D[VehicleState.WheelCount];
        public readonly Node3D[] Wheels = new Node3D[VehicleState.WheelCount];
        public readonly MeshInstance3D[] Dampers = new MeshInstance3D[VehicleState.WheelCount];
        public readonly GpuParticles3D[] Dust = new GpuParticles3D[2];
        public readonly GpuParticles3D[] Bits = new GpuParticles3D[2];
        public readonly string[] Spray = { "-", "-" };
        public readonly float[] Spin = new float[VehicleState.WheelCount];
        public readonly float[] PreviousSpin = new float[VehicleState.WheelCount];
        public Transform3D Previous, Current;
    }

    public void Build(SimWorld sim, LevelLayout level, MaterialLibrary materials, VehicleViewDef? def = null)
    {
        Current = this;
        _sim = sim;
        _vehicles = sim.Vehicles;
        _def = def ?? new VehicleViewDef { SteeringRatio = 4.5f, SprayAmount = 32, SprayLifetime_s = 1f, DustFull_mps = 10f, ScrubFull_mps = 5f };
        _dust = Color.FromHtml(_def.DustColor);
        _grass = Color.FromHtml(_def.GrassColor);
        _mud = Color.FromHtml(_def.MudColor);
        _smoke = Color.FromHtml(_def.SmokeColor);
        int Material(string id, string fallback)
        {
            foreach (KitMaterial m in level.Materials)
            {
                if (m.Id == id)
                {
                    return m.Index;
                }
            }

            return id == fallback ? level.GroundMaterial.Index : Material(fallback, fallback);
        }

        int paint = Material("steel_yellow", "steel_painted"), frame = Material("steel_painted", "steel_painted");
        int rubber = Material("tyre_rubber", "steel_painted"), hub = Material("steel_galvanised", "steel_painted");
        int seat = Material("plastic_black", "steel_painted"), floor = Material("car_body", "steel_painted");
        var meshes = new Dictionary<string, (ArrayMesh Body, ArrayMesh Wheel, ArrayMesh Steering, ArrayMesh Damper)>(StringComparer.Ordinal);
        Texture2D soft = SoftDot();
        for (int i = 0; i < _vehicles.Count; i++)
        {
            VehicleParams p = _vehicles[i].Params;
            if (!meshes.TryGetValue(p.Id, out var mesh))
            {
                mesh = (BuildBody(p, materials, paint, frame, seat, floor), BuildWheel(p, materials, rubber, hub), BuildSteering(materials, frame, seat),
                    BuildDamper(materials, frame, hub));
                meshes[p.Id] = mesh;
            }

            var body = new MeshInstance3D { Name = $"Vehicle{i}", Mesh = mesh.Body, Layers = RenderLayer };
            AddChild(body);
            float bottom = 0.15f;
            var wall = new AnimatableBody3D { Name = $"VehicleBody{i}", SyncToPhysics = false, TopLevel = true };
            wall.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(p.Width, p.SideHeight - bottom, p.Length) },
                Position = new Vector3(0f, (p.SideHeight + bottom) * 0.5f, 0f),
            });
            AddChild(wall);
            var steering = new Node3D { Name = "Steering", Transform = SteeringFrame(p) };
            steering.AddChild(new MeshInstance3D { Name = "Wheel", Mesh = mesh.Steering, Layers = RenderLayer });
            body.AddChild(steering);
            var view = new View { Body = body, Wall = wall, Steering = steering };
            for (int w = 0; w < VehicleState.WheelCount; w++)
            {
                var hubNode = new Node3D { Name = $"Hub{w}", Position = WheelAt(p, w, p.StaticCompression) };
                var wheel = new MeshInstance3D { Name = "Wheel", Mesh = mesh.Wheel, Layers = RenderLayer };
                hubNode.AddChild(wheel);
                body.AddChild(hubNode);
                view.Hubs[w] = hubNode;
                view.Wheels[w] = wheel;
                var damper = new MeshInstance3D { Name = $"Damper{w}", Mesh = mesh.Damper, Layers = RenderLayer };
                body.AddChild(damper);
                view.Dampers[w] = damper;
            }

            for (int k = 0; k < 2; k++)
            {
                view.Dust[k] = Spray(soft, bits: false);
                view.Bits[k] = Spray(soft, bits: true);
                AddChild(view.Dust[k]);
                AddChild(view.Bits[k]);
            }

            _views.Add(view);
        }

        Capture();
        Capture();
        Place(1f);
    }

    public override void _ExitTree()
    {
        if (Current == this)
        {
            Current = null;
        }
    }

    /// <summary>After each sim step: the poses to draw between, the wheels' turn, and the walking boxes where the vehicles now are.</summary>
    public void Capture()
    {
        if (_vehicles is null || _sim is null)
        {
            return;
        }

        float dt = _sim.Dt;
        for (int i = 0; i < _views.Count; i++)
        {
            VehicleState v = _vehicles[i];
            View view = _views[i];
            view.Previous = view.Current;
            view.Current = Pose(v);
            bool remote = _sim.Role == SimRole.Client && _sim.PredictedVehicle != i;
            for (int w = 0; w < VehicleState.WheelCount; w++)
            {
                view.PreviousSpin[w] = view.Spin[w];
                // Where the sim steps it, its own wheels' turn; one drawn from the server's, rolling with the ground (and
                // the back ones spinning faster as the engine pushes and they slide).
                float rate = remote
                    ? v.Speed / v.Params.WheelRadius + (w >= 2 && v.Drive > 0.05f ? v.Slide[w] * MathF.Sign(v.Speed + 0.01f) / v.Params.WheelRadius : 0f)
                    : v.SpinRate[w];
                view.Spin[w] += rate * dt;
            }

            view.Wall.GlobalTransform = view.Current;
        }
    }

    /// <summary>The node a splat on this paint collider should stick to (a vehicle's body), or null for anything else.</summary>
    public SplatAnchor? AnchorOf(int colliderId)
    {
        if (_sim is null || colliderId < 0 || colliderId >= _sim.Collision.Colliders.Count)
        {
            return null;
        }

        string name = _sim.Collision.Colliders[colliderId].Name;
        if (!name.StartsWith("vehicle#", StringComparison.Ordinal))
        {
            return null;
        }

        int end = name.IndexOf(':');
        return int.TryParse(name.AsSpan(8, (end < 0 ? name.Length : end) - 8), out int i) && i >= 0 && i < _views.Count
            ? new SplatAnchor(_views[i].Body, CullMask: RenderLayer)
            : null;
    }

    /// <summary>How much vehicle <paramref name="i"/>'s tyres are throwing up this frame (the most of any spray, 0 … 1).</summary>
    public float Spraying(int i)
    {
        if (i < 0 || i >= _views.Count)
        {
            return 0f;
        }

        float most = 0f;
        foreach (GpuParticles3D spray in _views[i].Dust)
        {
            most = MathF.Max(most, spray.AmountRatio);
        }

        foreach (GpuParticles3D spray in _views[i].Bits)
        {
            most = MathF.Max(most, spray.AmountRatio);
        }

        return most;
    }

    /// <summary>Where vehicle <paramref name="i"/> is drawn this frame.</summary>
    public Transform3D Drawn(int i) => i >= 0 && i < _views.Count ? _views[i].Body.GlobalTransform : Transform3D.Identity;

    /// <summary>
    /// Where vehicle <paramref name="i"/> is drawn <paramref name="alpha"/> of the way from the last tick to this one: the
    /// same as <see cref="Drawn"/> once this frame's been placed, whichever of them is asked first (a seat's view, a
    /// driver's body).
    /// </summary>
    public Transform3D Drawn(int i, float alpha)
    {
        if (i < 0 || i >= _views.Count)
        {
            return Transform3D.Identity;
        }

        View view = _views[i];
        Transform3D drawn = view.Previous.IsEqualApprox(view.Current) ? view.Current : view.Previous.InterpolateWith(view.Current, alpha);
        if (DrawnOffset is { } offset)
        {
            drawn.Origin += offset(i).ToGodot();
        }

        return drawn;
    }

    /// <summary>
    /// Where the driver's hands hold vehicle <paramref name="i"/>'s steering wheel this frame (at ten to two, turning with
    /// it), and which way its column points (down and ahead, away from the driver).
    /// </summary>
    public bool Grips(int i, float alpha, out Vector3 left, out Vector3 right, out Vector3 column)
    {
        left = right = column = Vector3.Zero;
        if (i < 0 || i >= _views.Count || _vehicles is null)
        {
            return false;
        }

        VehicleState v = _vehicles[i];
        Transform3D wheel = Drawn(i, alpha) * SteeringFrame(v.Params) * new Transform3D(new Basis(Vector3.Up, v.Steer * _def.SteeringRatio), Vector3.Zero);
        // The rim lies in the wheel's x-z plane; its top is toward −z (up and ahead, the column tilted back).
        left = wheel * new Vector3(-MathF.Sin(GripAngle) * RimRadius, 0f, -MathF.Cos(GripAngle) * RimRadius);
        right = wheel * new Vector3(MathF.Sin(GripAngle) * RimRadius, 0f, -MathF.Cos(GripAngle) * RimRadius);
        column = -wheel.Basis.Y.Normalized();
        return true;
    }

    public override void _Process(double delta) => Place((float)Engine.GetPhysicsInterpolationFraction());

    private void Place(float alpha)
    {
        if (_vehicles is null || _sim is null)
        {
            return;
        }

        for (int i = 0; i < _views.Count; i++)
        {
            View view = _views[i];
            VehicleState v = _vehicles[i];
            VehicleParams p = v.Params;
            Transform3D drawn = Drawn(i, alpha);
            view.Body.GlobalTransform = drawn;
            Vector3 up = drawn.Basis.Y.Normalized();
            float reach = p.RestLength + p.WheelRadius, most = p.StaticCompression + p.BumpTravel;
            for (int w = 0; w < VehicleState.WheelCount; w++)
            {
                // The wheel on its spring, where the ground under it is as drawn (hanging, off the ground).
                Vector3 mount = drawn * new Vector3((w % 2 == 0 ? -0.5f : 0.5f) * p.Track, p.MountHeight, (w < 2 ? -0.5f : 0.5f) * p.Wheelbase);
                float pressed = 0f;
                SurfaceId surface = default;
                Vector3 contact = mount - up * reach;
                bool touching = _sim.Collision.SweepSphere(mount.ToSim(), (mount - up * reach).ToSim(), 0f, out SweepHit hit, includeDynamic: false);
                if (touching)
                {
                    pressed = Mathf.Clamp(p.RestLength - (hit.T * reach - p.WheelRadius), 0f, most);
                    surface = hit.Surface;
                    contact = hit.Point.ToGodot();
                }

                Vector3 hubAt = WheelAt(p, w, pressed);
                Node3D hub = view.Hubs[w];
                hub.Position = hubAt;
                hub.Rotation = new Vector3(0f, w < 2 ? v.Steer : 0f, 0f);
                // Rolling forward (−Z) turns the top of the wheel ahead: a negative turn about +X.
                float spin = Mathf.Lerp(view.PreviousSpin[w], view.Spin[w], alpha);
                view.Wheels[w].Rotation = new Vector3(-spin, 0f, 0f);
                PlaceDamper(view.Dampers[w], p, w, hubAt);
                if (w >= 2)
                {
                    Spray(view, w - 2, v, surface, contact, drawn.Basis, touching);
                }
            }

            // The steering wheel turns with the front wheels (positive steer, to the left: anticlockwise to the driver).
            view.Steering.Transform = SteeringFrame(p) * new Transform3D(new Basis(Vector3.Up, v.Steer * _def.SteeringRatio), Vector3.Zero);
        }
    }

    /// <summary>Wheel <paramref name="w"/>'s middle in the body's frame with its spring pressed <paramref name="pressed"/> m.</summary>
    private static Vector3 WheelAt(VehicleParams p, int w, float pressed)
    {
        float side = w % 2 == 0 ? -1f : 1f;
        return new Vector3(side * (p.Width * 0.5f - WheelWidth * 0.5f + 0.02f), p.MountHeight - (p.RestLength - pressed), (w < 2 ? -0.5f : 0.5f) * p.Wheelbase);
    }

    /// <summary>A damper from under the wheel's arch down to its hub (a strut, stretched as the wheel drops).</summary>
    private static void PlaceDamper(MeshInstance3D damper, VehicleParams p, int w, Vector3 hub)
    {
        float side = w % 2 == 0 ? -1f : 1f;
        var top = new Vector3(side * (p.Width * 0.5f - 0.36f), p.MountHeight + 0.24f, hub.Z + (w < 2 ? 0.06f : -0.06f));
        Vector3 bottom = hub + new Vector3(-side * 0.1f, 0f, 0f);
        Vector3 along = top - bottom;
        float length = along.Length();
        if (length < 1e-3f)
        {
            return;
        }

        Vector3 y = along / length;
        Vector3 x = Mathf.Abs(y.Dot(Vector3.Forward)) < 0.9f ? y.Cross(Vector3.Forward).Normalized() : y.Cross(Vector3.Right).Normalized();
        Vector3 z = x.Cross(y);
        damper.Transform = new Transform3D(new Basis(x, y * length, z), (top + bottom) * 0.5f);
    }

    /// <summary>
    /// What back tyre <paramref name="k"/> throws up this frame: dust off dry loose ground as it rolls, and whatever the
    /// ground is (dust, grass, mud, rubber smoke) as it scrubs.
    /// </summary>
    private void Spray(View view, int k, VehicleState v, SurfaceId surface, Vector3 contact, Basis basis, bool touching)
    {
        GpuParticles3D dust = view.Dust[k], bits = view.Bits[k];
        string name = touching && surface.Value < _sim!.Config.Surfaces.Count ? _sim.Config.Surfaces.Names[surface.Value] : "";
        string kind = _def.Spray.TryGetValue(name, out string? s) ? s : "";
        if (kind != view.Spray[k])
        {
            view.Spray[k] = kind;
            Color color = kind switch { "grass" => _grass, "mud" => _mud, "smoke" => _smoke, _ => _dust };
            ((ParticleProcessMaterial)dust.ProcessMaterial).Color = color with { A = kind == "smoke" ? 0.5f : 0.55f };
            ((ParticleProcessMaterial)bits.ProcessMaterial).Color = color;
        }

        float speed = MathF.Abs(v.Speed);
        float rolling = Mathf.SmoothStep(_def.DustFrom_mps, _def.DustFull_mps, speed);
        float scrub = Mathf.SmoothStep(_def.ScrubFrom_mps, _def.ScrubFull_mps, v.Slide[k + 2]);
        (float d, float b) = kind switch
        {
            "dust" => (0.6f * rolling + scrub, 0.6f * scrub),
            "grass" => (0f, scrub),
            "mud" => (0f, scrub + 0.3f * rolling),
            "smoke" => (scrub, 0f),
            _ => (0f, 0f),
        };

        var at = new Transform3D(basis.Orthonormalized(), contact + basis.Y.Normalized() * 0.1f);
        dust.GlobalTransform = at;
        bits.GlobalTransform = at;
        dust.AmountRatio = Mathf.Clamp(d, 0f, 1f);
        bits.AmountRatio = Mathf.Clamp(b, 0f, 1f);
    }

    /// <summary>A spray of soft puffs (dust, smoke) or small bits (stones, grass, mud) thrown up and back off a tyre.</summary>
    private GpuParticles3D Spray(Texture2D soft, bool bits)
    {
        var grow = new Curve();
        grow.AddPoint(new Vector2(0f, bits ? 1f : 0.35f));
        grow.AddPoint(new Vector2(1f, bits ? 0.8f : 1.6f));
        var fade = new Gradient();
        fade.SetColor(0, new Color(1f, 1f, 1f, 1f));
        fade.SetColor(1, new Color(1f, 1f, 1f, 0f));
        var process = new ParticleProcessMaterial
        {
            // Up and back off the tyre (the body's +Z is behind it).
            Direction = new Vector3(0f, bits ? 0.8f : 0.5f, 1f),
            Spread = bits ? 28f : 45f,
            InitialVelocityMin = bits ? 2.2f : 0.5f,
            InitialVelocityMax = bits ? 4.8f : 1.6f,
            Gravity = bits ? new Vector3(0f, -9.8f, 0f) : new Vector3(0f, 0.25f, 0f),
            DampingMin = bits ? 0.1f : 1.4f,
            DampingMax = bits ? 0.4f : 2.4f,
            ScaleMin = bits ? 0.5f : 0.7f,
            ScaleMax = bits ? 1.2f : 1.3f,
            ScaleCurve = new CurveTexture { Curve = grow },
            ColorRamp = new GradientTexture1D { Gradient = fade },
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.12f,
            Color = _dust,
        };
        var look = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = soft,
            ShadingMode = bits ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel,
            DisableReceiveShadows = true,
        };
        float size = bits ? 0.07f : 0.7f;
        return new GpuParticles3D
        {
            Name = bits ? "Bits" : "Dust",
            Amount = _def.SprayAmount,
            Lifetime = bits ? _def.SprayLifetime_s * 0.6f : _def.SprayLifetime_s,
            Randomness = 0.5f,
            LocalCoords = false,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = new Vector2(size, size), Material = look },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Godot.Aabb(new Vector3(-12f, -4f, -12f), new Vector3(24f, 10f, 24f)),
            AmountRatio = 0f,
            Emitting = true,
        };
    }

    /// <summary>A round, soft-edged dot for the spray.</summary>
    private static Texture2D SoftDot()
    {
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(1f, 1f, 1f, 1f));
        gradient.SetColor(1, new Color(1f, 1f, 1f, 0f));
        return new GradientTexture2D
        {
            Gradient = gradient, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
            Width = 64, Height = 64,
        };
    }

    private static Transform3D Pose(VehicleState v)
    {
        System.Numerics.Quaternion q = v.Orientation;
        return new Transform3D(new Basis(new Quaternion(q.X, q.Y, q.Z, q.W)), v.Position.ToGodot());
    }

    /// <summary>The steering wheel's frame in the body's: before the driver's seat, its column tilted back toward them.</summary>
    private static Transform3D SteeringFrame(VehicleParams p)
    {
        SeatParams driver = p.Seats[p.DriverSeat];
        var at = new Vector3(driver.At.X, driver.At.Y, -driver.At.Z) + new Vector3(0f, 0.42f, -0.48f);
        return new Transform3D(new Basis(Vector3.Right, Mathf.DegToRad(62f)), at);
    }

    /// <summary>
    /// The buggy in its own frame (x right, y up, ahead −Z, its point on the ground under its middle): a floor pan and a
    /// tub of panels up the sides, a nose and an engine deck, a tube frame and a roll cage, two seats where the sim
    /// has them and a column up to the steering wheel before the driver's.
    /// </summary>
    private static ArrayMesh BuildBody(VehicleParams p, MaterialLibrary materials, int paint, int frame, int seat, int floor)
    {
        var shape = new ShapeMesh();
        shape.Place(Transform3D.Identity, p.SideHeight + 0.7f);
        float w = p.Width, l = p.Length, h = p.SideHeight, f = p.Floor, r = p.WheelRadius;
        float halfW = w * 0.5f, halfL = l * 0.5f;
        // The tub: a floor pan, panels up each side between the wheels, a nose and a deck over the engine at the back.
        shape.Box(floor, new Vector3(0f, f, 0f), new Vector3(w - 0.32f, 0.05f, l - 0.4f));
        foreach (float side in new[] { -1f, 1f })
        {
            shape.Box(paint, new Vector3(side * (halfW - 0.2f), (f + h) * 0.5f + 0.02f, 0.05f), new Vector3(0.05f, h - f, p.Wheelbase - 0.85f));
            // Sills under the doors' gap and arches over the wheels (clear of them at full bump).
            shape.Box(paint, new Vector3(side * (halfW - 0.2f), f + 0.08f, 0f), new Vector3(0.12f, 0.16f, l - 0.6f));
            foreach (float end in new[] { -1f, 1f })
            {
                var arch = new Vector3(side * (halfW - WheelWidth * 0.5f + 0.02f), r * 2f + 0.06f + p.BumpTravel * 0.5f, end * p.Wheelbase * 0.5f);
                shape.Box(paint, arch, new Vector3(WheelWidth + 0.1f, 0.04f, r * 2.2f));
            }
        }

        shape.Box(paint, new Vector3(0f, f + 0.32f, -halfL + 0.36f), new Vector3(w - 0.5f, 0.5f, 0.72f));
        shape.Box(paint, new Vector3(0f, f + 0.42f, -halfL + 0.85f), new Vector3(w - 0.46f, 0.08f, 0.5f), new Basis(Vector3.Right, -0.35f));
        shape.Box(paint, new Vector3(0f, f + 0.4f, halfL - 0.42f), new Vector3(w - 0.42f, 0.62f, 0.84f));
        shape.Box(frame, new Vector3(0f, f + 0.74f, halfL - 0.42f), new Vector3(w - 0.6f, 0.06f, 0.7f));
        // Bumpers, front and back.
        shape.Bar(frame, new Vector3(-halfW + 0.25f, 0.42f, -halfL - 0.02f), new Vector3(halfW - 0.25f, 0.42f, -halfL - 0.02f), 0.08f, 0.08f);
        shape.Bar(frame, new Vector3(-halfW + 0.25f, 0.42f, halfL + 0.02f), new Vector3(halfW - 0.25f, 0.42f, halfL + 0.02f), 0.08f, 0.08f);

        // The roll cage: a hoop behind the seats, two bars up from the nose, rails over the top and along each side.
        float top = h + 0.68f, behind = 0.5f, ahead = -0.55f;
        foreach (float side in new[] { -1f, 1f })
        {
            float x = side * (halfW - 0.24f);
            shape.Rod(frame, new Vector3(x, f, behind), new Vector3(x * 0.92f, top, behind), 0.03f);
            shape.Rod(frame, new Vector3(x, f + 0.45f, ahead - 0.3f), new Vector3(x * 0.92f, top, ahead), 0.03f);
            shape.Rod(frame, new Vector3(x * 0.92f, top, ahead), new Vector3(x * 0.92f, top, behind), 0.03f);
            shape.Rod(frame, new Vector3(x, h - 0.05f, ahead - 0.2f), new Vector3(x, h - 0.05f, behind), 0.025f);
            // Bracing from the hoop down to the engine deck.
            shape.Rod(frame, new Vector3(x * 0.92f, top, behind), new Vector3(x * 0.8f, f + 0.7f, halfL - 0.2f), 0.025f);
        }

        shape.Rod(frame, new Vector3(-(halfW - 0.24f) * 0.92f, top, behind), new Vector3((halfW - 0.24f) * 0.92f, top, behind), 0.03f);
        shape.Rod(frame, new Vector3(-(halfW - 0.24f) * 0.92f, top, ahead), new Vector3((halfW - 0.24f) * 0.92f, top, ahead), 0.03f);

        // The seats, where the sim seats people (x right, z ahead → −Z here), and the column before the driver's.
        foreach (SeatParams s in p.Seats)
        {
            var at = new Vector3(s.At.X, s.At.Y, -s.At.Z);
            shape.Pillow(seat, at + new Vector3(0f, -0.04f, 0.02f), new Vector3(0.5f, 0.12f, 0.5f), Basis.Identity, 6f);
            shape.Pillow(seat, at + new Vector3(0f, 0.3f, 0.27f), new Vector3(0.5f, 0.62f, 0.12f), new Basis(Vector3.Right, -0.18f), 6f);
            if (s.Drives)
            {
                Vector3 hubAt = at + new Vector3(0f, 0.42f, -0.48f);
                shape.Rod(frame, hubAt + new Vector3(0f, -0.03f, -0.04f), hubAt + new Vector3(0f, -0.32f, -0.42f), 0.022f);
            }
        }

        var mesh = new ArrayMesh();
        shape.Commit(mesh, m => materials[m]);
        return mesh;
    }

    /// <summary>The steering wheel about its column (along y): a rim, a padded hub and three spokes.</summary>
    private static ArrayMesh BuildSteering(MaterialLibrary materials, int frame, int grip)
    {
        var shape = new ShapeMesh();
        shape.Place(Transform3D.Identity, 0.4f);
        shape.Lathe(grip, Vector3.Zero, Basis.Identity, new[]
        {
            new Vector2(RimRadius - 0.018f, -0.015f), new Vector2(RimRadius + 0.016f, 0f), new Vector2(RimRadius - 0.018f, 0.015f),
            new Vector2(RimRadius - 0.03f, 0f), new Vector2(RimRadius - 0.018f, -0.015f),
        }, 18);
        shape.Cylinder(grip, new Vector3(0f, 0.01f, 0f), Basis.Identity, 0.045f, 0.04f, 10);
        for (int k = 0; k < 3; k++)
        {
            float a = Mathf.Pi + k * Mathf.Tau / 3f; // one down, two up to the sides
            shape.Rod(frame, Vector3.Zero, new Vector3(Mathf.Sin(a) * (RimRadius - 0.02f), 0f, -Mathf.Cos(a) * (RimRadius - 0.02f)), 0.012f);
        }

        var mesh = new ArrayMesh();
        shape.Commit(mesh, m => materials[m]);
        return mesh;
    }

    /// <summary>A damper one metre long along y about its middle (stretched to fit): a fat body over a thin rod.</summary>
    private static ArrayMesh BuildDamper(MaterialLibrary materials, int body, int rod)
    {
        var shape = new ShapeMesh();
        shape.Place(Transform3D.Identity, 1f);
        shape.Cylinder(body, new Vector3(0f, 0.2f, 0f), Basis.Identity, 0.04f, 0.6f, 8);
        shape.Cylinder(rod, new Vector3(0f, -0.25f, 0f), Basis.Identity, 0.016f, 0.5f, 6);
        var mesh = new ArrayMesh();
        shape.Commit(mesh, m => materials[m]);
        return mesh;
    }

    /// <summary>A wheel about its own axle (along X): a tyre with a tread, a hub, at its middle.</summary>
    private static ArrayMesh BuildWheel(VehicleParams p, MaterialLibrary materials, int rubber, int hub)
    {
        var shape = new ShapeMesh();
        float r = p.WheelRadius, half = WheelWidth * 0.5f;
        shape.Place(Transform3D.Identity, r * 2f);
        var axle = new Basis(Vector3.Forward, Mathf.Pi * 0.5f);
        shape.Lathe(rubber, Vector3.Zero, axle, new[]
        {
            new Vector2(r * 0.55f, -half), new Vector2(r * 0.9f, -half), new Vector2(r, -half * 0.7f), new Vector2(r, half * 0.7f),
            new Vector2(r * 0.9f, half), new Vector2(r * 0.55f, half),
        }, 18);
        shape.Cylinder(hub, Vector3.Zero, axle, r * 0.56f, WheelWidth * 0.8f, 12);
        // Tread blocks across the tyre, so you see it roll.
        for (int k = 0; k < 12; k++)
        {
            float a = Mathf.Tau * k / 12f;
            var at = new Vector3(0f, Mathf.Sin(a) * r, Mathf.Cos(a) * r);
            shape.Box(rubber, at, new Vector3(WheelWidth * 0.9f, 0.035f, 0.07f), new Basis(Vector3.Right, -a));
        }

        var mesh = new ArrayMesh();
        shape.Commit(mesh, m => materials[m]);
        return mesh;
    }
}
