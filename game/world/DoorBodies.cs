using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Level;

namespace Pb.Game.World;

/// <summary>
/// The dedicated server's doors: only the box each leaf is for walking into, moved after every sim step to where the
/// sim's <see cref="DoorSet"/> has the leaf (the game draws them with <see cref="DoorViews"/>).
/// </summary>
public partial class DoorBodies : Node3D
{
    private readonly List<AnimatableBody3D> _bodies = new();
    private DoorSet? _doors;

    public void Build(SimWorld sim)
    {
        _doors = sim.Doors;
        for (int i = 0; i < _doors.Count; i++)
        {
            DoorSpec spec = _doors[i];
            var body = new AnimatableBody3D { Name = $"DoorBody{i}", SyncToPhysics = false, TopLevel = true };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(spec.Width, spec.Height, spec.Kind.Thickness) } });
            AddChild(body);
            _bodies.Add(body);
        }

        Capture();
    }

    /// <summary>After each sim step: the boxes where the leaves now are.</summary>
    public void Capture()
    {
        if (_doors is null)
        {
            return;
        }

        for (int i = 0; i < _bodies.Count; i++)
        {
            DoorShape s = _doors.Shape(i);
            _bodies[i].GlobalTransform = new Transform3D(new Basis(s.AxisX.ToGodot(), s.AxisY.ToGodot(), s.AxisZ.ToGodot()), s.Center.ToGodot());
        }
    }
}
