using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Range;

namespace Pb.Game.World;

/// <summary>
/// Builds the Phase 1 range from its layout data: ground, distance markers, boundary nets,
/// backstop, props and targets. Each prop's mesh and walking collision come from the same
/// <see cref="PropSpec"/> that builds its ball collider in the sim, so they always match.
/// </summary>
public partial class RangeBuilder : Node3D
{
    private readonly List<TargetView> _targets = new();

    public IReadOnlyList<TargetView> Targets => _targets;

    public Node3D? TargetNode(int index) => index >= 0 && index < _targets.Count ? _targets[index] : null;

    public void Build(RangeLayout layout)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        _targets.Clear();
        BuildGround(layout);
        BuildMarkers(layout);
        BuildBoundary(layout);
        foreach (PropSpec prop in layout.Props)
        {
            BuildProp(prop);
        }

        for (int i = 0; i < layout.Targets.Count; i++)
        {
            var view = new TargetView { Name = "Target_" + layout.Targets[i].Id };
            AddChild(view);
            view.Build(layout.Targets[i]);
            _targets.Add(view);
        }
    }

    public void UpdateTargets(double time)
    {
        foreach (TargetView view in _targets)
        {
            view.SetTime(time);
        }
    }

    private void BuildGround(RangeLayout layout)
    {
        float length = layout.Length + layout.BackMargin;
        var noise = new FastNoiseLite { Frequency = 0.04f, FractalOctaves = 3, Seed = 7 };
        var turf = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.33f, 0.52f, 0.25f),
            AlbedoTexture = new NoiseTexture2D { Width = 256, Height = 256, Seamless = true, Noise = noise, ColorRamp = TurfRamp() },
            Uv1Scale = new Vector3(layout.Width / 8f, length / 8f, 1f),
            Roughness = 0.95f,
        };
        AddChild(new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new PlaneMesh { Size = new Vector2(layout.Width, length) },
            MaterialOverride = turf,
            Position = new Vector3(0, 0, (layout.BackMargin - layout.Length) * 0.5f),
        });

        // Surrounding grass so the horizon isn't a void.
        AddChild(new MeshInstance3D
        {
            Name = "Surroundings",
            Mesh = new PlaneMesh { Size = new Vector2(600, 600) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.27f, 0.4f, 0.2f), Roughness = 1f },
            Position = new Vector3(0, -0.02f, -layout.Length * 0.5f),
        });

        var body = new StaticBody3D { Name = "GroundBody" };
        body.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D() });
        AddChild(body);
    }

    private void BuildMarkers(RangeLayout layout)
    {
        var paint = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.95f, 0.92f), Roughness = 0.8f };
        var firingLine = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.8f, 0.15f), Roughness = 0.8f };
        AddChild(new MeshInstance3D
        {
            Name = "FiringLine",
            Mesh = new BoxMesh { Size = new Vector3(layout.Width, 0.01f, 0.12f) },
            MaterialOverride = firingLine,
            Position = new Vector3(0, 0.005f, 0.4f),
        });

        for (float d = layout.MarkerSpacing; d <= layout.MarkerMax + 0.01f; d += layout.MarkerSpacing)
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(layout.Width, 0.01f, 0.08f) },
                MaterialOverride = paint,
                Position = new Vector3(0, 0.005f, -d),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        foreach (float d in layout.MarkerLabels)
        {
            AddChild(new Label3D
            {
                Text = $"{d:0} m",
                Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
                PixelSize = 0.0035f + 0.00009f * d,
                FontSize = 80,
                OutlineSize = 16,
                Position = new Vector3(-(layout.Width * 0.5f - 1.2f), 0.6f + 0.02f * d, -d),
            });
        }
    }

    private void BuildBoundary(RangeLayout layout)
    {
        var net = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.08f, 0.12f, 0.1f, 0.35f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Roughness = 1f,
        };
        float length = layout.Length + layout.BackMargin;
        float z = (layout.BackMargin - layout.Length) * 0.5f;
        foreach (float side in new[] { -1f, 1f })
        {
            AddChild(new MeshInstance3D
            {
                Name = side < 0 ? "NetLeft" : "NetRight",
                Mesh = new QuadMesh { Size = new Vector2(length, 5f) },
                MaterialOverride = net,
                Position = new Vector3(side * layout.Width * 0.5f, 2.5f, z),
                RotationDegrees = new Vector3(0, 90, 0),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        AddChild(new MeshInstance3D
        {
            Name = "Backstop",
            Mesh = new BoxMesh { Size = new Vector3(layout.Width + 10f, 8f, 2f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.28f, 0.18f), Roughness = 1f },
            Position = new Vector3(0, 4f, -layout.Length - 2f),
        });
    }

    private void BuildProp(PropSpec prop)
    {
        Vector3 size = prop.Size.ToGodot();
        Color fallback = prop.SurfaceName == "panel" ? new Color(0.78f, 0.7f, 0.54f) : new Color(0.85f, 0.85f, 0.85f);
        var material = new StandardMaterial3D { AlbedoColor = Conv.ParseColor(prop.Color, fallback), Roughness = 0.45f };

        Mesh mesh;
        Shape3D shape;
        switch (prop.Kind)
        {
            case PropShapeKind.Box:
                mesh = new BoxMesh { Size = size };
                shape = new BoxShape3D { Size = size };
                break;
            case PropShapeKind.Cylinder:
                mesh = new CylinderMesh { TopRadius = size.X * 0.5f, BottomRadius = size.X * 0.5f, Height = size.Y, RadialSegments = 32 };
                shape = new CylinderShape3D { Radius = size.X * 0.5f, Height = size.Y };
                break;
            case PropShapeKind.Wedge:
                mesh = new PrismMesh { Size = size, LeftToRight = 0.5f };
                shape = new ConvexPolygonShape3D { Points = WedgePoints(size) };
                break;
            default:
                throw new InvalidOperationException($"Unknown prop shape {prop.Kind}");
        }

        var root = new StaticBody3D
        {
            Name = "Prop_" + prop.Id,
            Position = prop.BasePosition.ToGodot() + new Vector3(0, size.Y * 0.5f, 0),
            Rotation = new Vector3(0, prop.Yaw, 0),
        };
        root.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material });
        root.AddChild(new CollisionShape3D { Shape = shape });
        AddChild(root);
    }

    /// <summary>Wedge corners centred on the prism's bounding box (Godot's PrismMesh origin).</summary>
    private static Vector3[] WedgePoints(Vector3 size)
    {
        float hw = size.X * 0.5f, hh = size.Y * 0.5f, hl = size.Z * 0.5f;
        return new[]
        {
            new Vector3(-hw, -hh, -hl), new Vector3(hw, -hh, -hl), new Vector3(0, hh, -hl),
            new Vector3(-hw, -hh, hl), new Vector3(hw, -hh, hl), new Vector3(0, hh, hl),
        };
    }

    private static Gradient TurfRamp()
    {
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(0.78f, 0.86f, 0.72f));
        ramp.SetColor(1, new Color(1f, 1f, 1f));
        return ramp;
    }
}
