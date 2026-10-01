using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Match;

namespace Pb.Game.World;

/// <summary>
/// Draws the level's pickups (greybox until the art pass): a cardboard tray of three paint pods,
/// or an air tank lying on the floor. A pickup disappears when the sim reports it taken; who gets
/// it, and whether they have room for it, is the sim's call.
/// </summary>
public partial class PickupVisuals : Node3D, ISimEventListener
{
    private readonly List<Node3D> _items = new();

    public void Build(PickupSet pickups)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        _items.Clear();
        for (int i = 0; i < pickups.Items.Count; i++)
        {
            PickupSpec spec = pickups.Items[i];
            Node3D item = spec.Kind == PickupKind.Air ? AirTank() : PodTray();
            item.Name = spec.Id;
            item.Position = spec.Position.ToGodot();
            // A fixed but varied angle per pickup, so they don't all line up with the grid.
            item.RotationDegrees = new Vector3(0, (i * 67) % 360, 0);
            item.Visible = pickups.Active && !pickups.IsTaken(i);
            AddChild(item);
            _items.Add(item);
        }
    }

    public void OnSimEvent(in SimEvent e)
    {
        if (e.Type == SimEventType.PickupTaken && e.TargetId >= 0 && e.TargetId < _items.Count)
        {
            _items[e.TargetId].Visible = false;
        }
    }

    private static Node3D PodTray()
    {
        var root = new Node3D();
        root.AddChild(Box(new Vector3(0.30f, 0.04f, 0.22f), new Vector3(0, 0.02f, 0), Cardboard));
        for (int i = 0; i < 3; i++)
        {
            // Pods lie across the tray: smoky plastic tubes full of paint, with a bright lid at one end.
            float z = (i - 1) * 0.068f;
            var pod = new Node3D { Position = new Vector3(0, 0.07f, z), RotationDegrees = new Vector3(0, 0, 90) };
            pod.AddChild(Cylinder(0.031f, 0.17f, Vector3.Zero, PodShell));
            pod.AddChild(Cylinder(0.026f, 0.15f, new Vector3(0, -0.005f, 0), PodFill));
            pod.AddChild(Cylinder(0.034f, 0.025f, new Vector3(0, 0.095f, 0), PodLid));
            root.AddChild(pod);
        }

        return root;
    }

    private static Node3D AirTank()
    {
        var root = new Node3D();
        // Lying on its side, raised by its own radius, with the regulator and a guard ring at one end.
        var tank = new Node3D { Position = new Vector3(0, 0.056f, 0), RotationDegrees = new Vector3(0, 0, 90) };
        tank.AddChild(Cylinder(0.056f, 0.30f, Vector3.Zero, TankShell));
        tank.AddChild(Sphere(0.056f, new Vector3(0, -0.15f, 0), TankShell));
        tank.AddChild(Cylinder(0.030f, 0.07f, new Vector3(0, 0.185f, 0), Regulator));
        tank.AddChild(Cylinder(0.042f, 0.018f, new Vector3(0, 0.165f, 0), PodLid));
        root.AddChild(tank);
        return root;
    }

    private static MeshInstance3D Box(Vector3 size, Vector3 position, Material material) =>
        new() { Mesh = new BoxMesh { Size = size, Material = material }, Position = position };

    private static MeshInstance3D Cylinder(float radius, float height, Vector3 position, Material material) =>
        new()
        {
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height, RadialSegments = 16, Rings = 1, Material = material },
            Position = position,
        };

    private static MeshInstance3D Sphere(float radius, Vector3 position, Material material) =>
        new() { Mesh = new SphereMesh { Radius = radius, Height = radius * 2f, RadialSegments = 16, Rings = 8, Material = material }, Position = position };

    private static readonly Material Cardboard = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.36f, 0.25f), Roughness = 0.95f };

    private static readonly Material PodShell = new StandardMaterial3D
    {
        AlbedoColor = new Color(0.75f, 0.78f, 0.8f, 0.45f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.25f,
    };

    private static readonly Material PodFill = new StandardMaterial3D { AlbedoColor = new Color(0.82f, 0.68f, 0.16f), Roughness = 0.4f };

    private static readonly Material PodLid = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.42f, 0.08f), Roughness = 0.5f };

    private static readonly Material TankShell = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.64f, 0.66f), Metallic = 0.8f, Roughness = 0.35f };

    private static readonly Material Regulator = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.2f, 0.22f), Metallic = 0.6f, Roughness = 0.45f };
}
