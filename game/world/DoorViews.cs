using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Ballistics;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Level;

namespace Pb.Game.World;

/// <summary>
/// The doors as you see and walk into them: each leaf a mesh built in code (<see cref="DoorShapes"/>) posed from the
/// sim's <see cref="DoorSet"/>, smoothly between ticks, and an <see cref="AnimatableBody3D"/> box that moves with it
/// for walking. Door meshes sit on a render layer of their own, so paint that hits a door paints only doors and moves
/// with the leaf, and paint on the walls round a doorway never paints a door that then swings away from it.
/// </summary>
public partial class DoorViews : Node3D
{
    /// <summary>The render layer door leaves are drawn on.</summary>
    public const uint RenderLayer = 1u << 4;

    private readonly List<MeshInstance3D> _leaves = new();
    private readonly List<AnimatableBody3D> _bodies = new();
    private Transform3D[] _previous = Array.Empty<Transform3D>();
    private Transform3D[] _current = Array.Empty<Transform3D>();
    private DoorSet? _doors;

    public int Count => _leaves.Count;

    public void Build(SimWorld sim, MaterialLibrary materials)
    {
        _doors = sim.Doors;
        var meshes = new Dictionary<(string, float, float, float, int), ArrayMesh>();
        _previous = new Transform3D[_doors.Count];
        _current = new Transform3D[_doors.Count];
        for (int i = 0; i < _doors.Count; i++)
        {
            DoorSpec spec = _doors[i];
            var key = (spec.Kind.Style, spec.Width, spec.Height, spec.Kind.Thickness, spec.Kind.Material.Index);
            if (!meshes.TryGetValue(key, out ArrayMesh? mesh))
            {
                mesh = DoorShapes.Build(spec, materials);
                meshes[key] = mesh;
            }

            var leaf = new MeshInstance3D { Name = $"Door{i}", Mesh = mesh, Layers = RenderLayer };
            // Its pattern and stains drawn in its own frame, from where it hangs shut, so they swing with it.
            leaf.SetInstanceShaderParameter("local_pattern", true);
            leaf.SetInstanceShaderParameter("local_origin", spec.ShutCenter.ToGodot() + new Vector3(0f, spec.Height * 0.5f, 0f));
            AddChild(leaf);
            _leaves.Add(leaf);

            var body = new AnimatableBody3D { Name = $"DoorBody{i}", SyncToPhysics = false, TopLevel = true };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(spec.Width, spec.Height, spec.Kind.Thickness) } });
            AddChild(body);
            _bodies.Add(body);
        }

        Capture();
        Capture();
        Place(1f);
    }

    /// <summary>After each sim step: the poses to draw between, and the walking boxes where the leaves now are.</summary>
    public void Capture()
    {
        if (_doors is null)
        {
            return;
        }

        for (int i = 0; i < _leaves.Count; i++)
        {
            _previous[i] = _current[i];
            _current[i] = Pose(i);
            _bodies[i].GlobalTransform = _current[i];
        }
    }

    /// <summary>The node a splat on this paint collider should stick to (a door leaf), or null for anything else.</summary>
    public SplatAnchor? AnchorOf(int colliderId)
    {
        int leaf = _doors?.LeafOfCollider(colliderId) ?? -1;
        return leaf >= 0 ? new SplatAnchor(_leaves[leaf], CullMask: RenderLayer) : null;
    }

    public override void _Process(double delta) => Place((float)Engine.GetPhysicsInterpolationFraction());

    private void Place(float alpha)
    {
        for (int i = 0; i < _leaves.Count; i++)
        {
            Transform3D a = _previous[i], b = _current[i];
            _leaves[i].GlobalTransform = a.Origin.IsEqualApprox(b.Origin) && a.Basis.IsEqualApprox(b.Basis) ? b : a.InterpolateWith(b, alpha);
        }
    }

    private Transform3D Pose(int leaf)
    {
        DoorShape s = _doors!.Shape(leaf);
        return new Transform3D(new Basis(s.AxisX.ToGodot(), s.AxisY.ToGodot(), s.AxisZ.ToGodot()), s.Center.ToGodot());
    }
}
