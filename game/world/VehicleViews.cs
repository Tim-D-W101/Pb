using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Ballistics;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Level;
using Pb.Sim.Vehicles;

namespace Pb.Game.World;

/// <summary>
/// The vehicles as you see and walk into them (Phase 5): each a buggy built in code — a tub of panels on a tube frame,
/// a roll cage, two seats, a wheel to steer by and four wheels — posed from the sim's <see cref="VehicleSet"/> smoothly
/// between ticks, its front wheels turned as far as the sim has them and every wheel rolling with the ground it covers;
/// and an <see cref="AnimatableBody3D"/> box that moves with it, for walking into. A buggy is drawn on a render layer of
/// its own, so paint that hits it stays on it as it drives off, and paint on the ground never lands on it.
/// </summary>
public partial class VehicleViews : Node3D
{
    /// <summary>The render layer vehicles are drawn on.</summary>
    public const uint RenderLayer = 1u << 5;

    private const float WheelRadius = 0.34f;
    private const float WheelWidth = 0.26f;

    private readonly List<Node3D> _bodies = new();
    private readonly List<AnimatableBody3D> _walls = new();
    private readonly List<(Node3D Hub, Node3D Wheel, bool Front)[]> _wheels = new();
    private Transform3D[] _previous = Array.Empty<Transform3D>();
    private Transform3D[] _current = Array.Empty<Transform3D>();
    private float[] _roll = Array.Empty<float>(), _previousRoll = Array.Empty<float>();
    private VehicleSet? _vehicles;
    private SimWorld? _sim;

    public int Count => _bodies.Count;

    public void Build(SimWorld sim, LevelLayout level, MaterialLibrary materials)
    {
        _sim = sim;
        _vehicles = sim.Vehicles;
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
        var meshes = new Dictionary<string, (ArrayMesh Body, ArrayMesh Wheel)>(StringComparer.Ordinal);
        _previous = new Transform3D[_vehicles.Count];
        _current = new Transform3D[_vehicles.Count];
        _roll = new float[_vehicles.Count];
        _previousRoll = new float[_vehicles.Count];
        for (int i = 0; i < _vehicles.Count; i++)
        {
            VehicleParams p = _vehicles[i].Params;
            if (!meshes.TryGetValue(p.Id, out var mesh))
            {
                mesh = (BuildBody(p, materials, paint, frame, seat, floor), BuildWheel(materials, rubber, hub));
                meshes[p.Id] = mesh;
            }

            var body = new MeshInstance3D { Name = $"Vehicle{i}", Mesh = mesh.Body, Layers = RenderLayer };
            AddChild(body);
            _bodies.Add(body);
            var wheels = new (Node3D, Node3D, bool)[4];
            for (int w = 0; w < 4; w++)
            {
                bool front = w < 2;
                float side = w % 2 == 0 ? -1f : 1f;
                var at = new Vector3(side * (p.Width * 0.5f - WheelWidth * 0.5f + 0.02f), WheelRadius, (front ? -1f : 1f) * p.Wheelbase * 0.5f);
                var steer = new Node3D { Name = $"Hub{w}", Position = at };
                var wheel = new MeshInstance3D { Name = "Wheel", Mesh = mesh.Wheel, Layers = RenderLayer };
                steer.AddChild(wheel);
                body.AddChild(steer);
                wheels[w] = (steer, wheel, front);
            }

            _wheels.Add(wheels);
            float bottom = 0.15f;
            var wall = new AnimatableBody3D { Name = $"VehicleBody{i}", SyncToPhysics = false, TopLevel = true };
            wall.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(p.Width, p.SideHeight - bottom, p.Length) },
                Position = new Vector3(0f, (p.SideHeight + bottom) * 0.5f, 0f),
            });
            AddChild(wall);
            _walls.Add(wall);
        }

        Capture();
        Capture();
        Place(1f);
    }

    /// <summary>After each sim step: the poses to draw between, the wheels' roll, and the walking boxes where the vehicles now are.</summary>
    public void Capture()
    {
        if (_vehicles is null || _sim is null)
        {
            return;
        }

        for (int i = 0; i < _bodies.Count; i++)
        {
            VehicleState v = _vehicles[i];
            _previous[i] = _current[i];
            _current[i] = Pose(v.Position, v.Yaw);
            _previousRoll[i] = _roll[i];
            _roll[i] += v.Speed * _sim.Dt / WheelRadius;
            _walls[i].GlobalTransform = _current[i];
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
        return int.TryParse(name.AsSpan(8, (end < 0 ? name.Length : end) - 8), out int i) && i >= 0 && i < _bodies.Count
            ? new SplatAnchor(_bodies[i], CullMask: RenderLayer)
            : null;
    }

    /// <summary>Where vehicle <paramref name="i"/> is drawn this frame.</summary>
    public Transform3D Drawn(int i) => i >= 0 && i < _bodies.Count ? _bodies[i].GlobalTransform : Transform3D.Identity;

    public override void _Process(double delta) => Place((float)Engine.GetPhysicsInterpolationFraction());

    private void Place(float alpha)
    {
        if (_vehicles is null)
        {
            return;
        }

        for (int i = 0; i < _bodies.Count; i++)
        {
            Transform3D a = _previous[i], b = _current[i];
            _bodies[i].GlobalTransform = a.IsEqualApprox(b) ? b : a.InterpolateWith(b, alpha);
            float roll = Mathf.Lerp(_previousRoll[i], _roll[i], alpha);
            float steer = _vehicles[i].Steer;
            foreach ((Node3D hub, Node3D wheel, bool front) in _wheels[i])
            {
                hub.Rotation = new Vector3(0f, front ? steer : 0f, 0f);
                // Rolling forward (−Z) turns the top of the wheel ahead: a negative turn about +X.
                wheel.Rotation = new Vector3(-roll, 0f, 0f);
            }
        }
    }

    private static Transform3D Pose(System.Numerics.Vector3 position, float yaw) => new(new Basis(Vector3.Up, yaw), position.ToGodot());

    /// <summary>
    /// The buggy in its own frame (x right, y up, ahead −Z, its point on the ground under its middle): a floor pan and a
    /// tub of panels up the sides, a nose and an engine deck, a tube frame and a roll cage, two seats where the sim
    /// has them and a wheel before the driver's.
    /// </summary>
    private static ArrayMesh BuildBody(VehicleParams p, MaterialLibrary materials, int paint, int frame, int seat, int floor)
    {
        var shape = new ShapeMesh();
        shape.Place(Transform3D.Identity, p.SideHeight + 0.7f);
        float w = p.Width, l = p.Length, h = p.SideHeight, f = p.Floor;
        float halfW = w * 0.5f, halfL = l * 0.5f;
        // The tub: a floor pan, panels up each side between the wheels, a nose and a deck over the engine at the back.
        shape.Box(floor, new Vector3(0f, f, 0f), new Vector3(w - 0.32f, 0.05f, l - 0.4f));
        foreach (float side in new[] { -1f, 1f })
        {
            shape.Box(paint, new Vector3(side * (halfW - 0.2f), (f + h) * 0.5f + 0.02f, 0.05f), new Vector3(0.05f, h - f, p.Wheelbase - 0.85f));
            // Sills under the doors' gap and arches over the wheels.
            shape.Box(paint, new Vector3(side * (halfW - 0.2f), f + 0.08f, 0f), new Vector3(0.12f, 0.16f, l - 0.6f));
            foreach (float end in new[] { -1f, 1f })
            {
                var arch = new Vector3(side * (halfW - WheelWidth * 0.5f + 0.02f), WheelRadius * 2f + 0.06f, end * p.Wheelbase * 0.5f);
                shape.Box(paint, arch, new Vector3(WheelWidth + 0.1f, 0.04f, WheelRadius * 2.2f));
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

        // The seats, where the sim seats people (x right, z ahead → −Z here), and the wheel before the driver's.
        foreach (SeatParams s in p.Seats)
        {
            var at = new Vector3(s.At.X, s.At.Y, -s.At.Z);
            shape.Pillow(seat, at + new Vector3(0f, -0.04f, 0.02f), new Vector3(0.5f, 0.12f, 0.5f), Basis.Identity, 6f);
            shape.Pillow(seat, at + new Vector3(0f, 0.3f, 0.27f), new Vector3(0.5f, 0.62f, 0.12f), new Basis(Vector3.Right, -0.18f), 6f);
            if (s.Drives)
            {
                Vector3 hubAt = at + new Vector3(0f, 0.42f, -0.48f);
                var tilt = new Basis(Vector3.Right, Mathf.DegToRad(62f));
                shape.Lathe(frame, hubAt, tilt, new[] { new Vector2(0.17f, -0.015f), new Vector2(0.2f, 0f), new Vector2(0.17f, 0.015f), new Vector2(0.15f, 0f), new Vector2(0.17f, -0.015f) }, 14);
                shape.Rod(frame, hubAt, hubAt + new Vector3(0f, -0.32f, -0.42f), 0.022f);
            }
        }

        var mesh = new ArrayMesh();
        shape.Commit(mesh, m => materials[m]);
        return mesh;
    }

    /// <summary>A wheel about its own axle (along X): a tyre with a tread, a hub, at its middle.</summary>
    private static ArrayMesh BuildWheel(MaterialLibrary materials, int rubber, int hub)
    {
        var shape = new ShapeMesh();
        shape.Place(Transform3D.Identity, WheelRadius * 2f);
        var axle = new Basis(Vector3.Forward, Mathf.Pi * 0.5f);
        float r = WheelRadius, half = WheelWidth * 0.5f;
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
