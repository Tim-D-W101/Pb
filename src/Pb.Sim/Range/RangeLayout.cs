using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Level;

namespace Pb.Sim.Range;

public enum PropShapeKind : byte
{
    Box,
    Cylinder,
    Wedge,
}

public enum PartShapeKind : byte
{
    Capsule,
    Sphere,
    Box,
}

/// <summary>
/// A static prop (inflatable, panel…). One spec drives the ball collider (sim), the walking
/// collision and the visible mesh (Godot), so the three always match.
/// </summary>
public sealed class PropSpec
{
    public required string Id { get; init; }

    public required PropShapeKind Kind { get; init; }

    /// <summary>Centre of the footprint on the ground.</summary>
    public required Vector3 BasePosition { get; init; }

    public required float Yaw { get; init; }

    /// <summary>Box: full size (x, y, z). Wedge: (width, height, length). Cylinder: (2r, height, 2r).</summary>
    public required Vector3 Size { get; init; }

    public required string SurfaceName { get; init; }

    public required SurfaceId Surface { get; init; }

    public string? Color { get; init; }

    public Quaternion Rotation => Quaternion.CreateFromAxisAngle(Vector3.UnitY, Yaw);

    public Shape CreateShape() => Kind switch
    {
        PropShapeKind.Box => new BoxShape(BasePosition + new Vector3(0f, Size.Y * 0.5f, 0f), Rotation, Size * 0.5f),
        PropShapeKind.Cylinder => new CylinderShape(BasePosition + new Vector3(0f, Size.Y * 0.5f, 0f), Vector3.UnitY, Size.Y * 0.5f, Size.X * 0.5f),
        PropShapeKind.Wedge => ConvexShape.Wedge(BasePosition, Yaw, Size.X, Size.Y, Size.Z),
        _ => throw new InvalidOperationException($"Unknown prop shape {Kind}."),
    };
}

/// <summary>One hitbox primitive of a target kind, in the target's local frame (feet at origin, facing −Z).</summary>
public sealed class TargetPartSpec
{
    public required HitboxPart Part { get; init; }

    public required PartShapeKind Kind { get; init; }

    public Vector3 From { get; init; }

    public Vector3 To { get; init; }

    public Vector3 Center { get; init; }

    public float Radius { get; init; }

    public Vector3 Size { get; init; }

    public Shape CreateLocalShape() => Kind switch
    {
        PartShapeKind.Capsule => new CapsuleShape(From, To, Radius),
        PartShapeKind.Sphere => new SphereShape(Center, Radius),
        PartShapeKind.Box => new BoxShape(Center, Quaternion.Identity, Size * 0.5f),
        _ => throw new InvalidOperationException($"Unknown part shape {Kind}."),
    };
}

public sealed class TargetKindSpec
{
    public required string Id { get; init; }

    public required SurfaceId Surface { get; init; }

    public required IReadOnlyList<TargetPartSpec> Parts { get; init; }
}

/// <summary>Side-to-side motion at constant speed (triangle wave) along <see cref="Axis"/>.</summary>
public sealed class TargetMotion
{
    public required Vector3 Axis { get; init; }

    public required float Amplitude { get; init; }

    public required float Speed { get; init; }

    public Vector3 OffsetAt(double time)
    {
        if (Amplitude <= 0f || Speed <= 0f)
        {
            return Vector3.Zero;
        }

        double a = Amplitude;
        double phase = time * Speed % (4.0 * a);
        double offset = phase < 2.0 * a ? phase - a : 3.0 * a - phase;
        return Axis * (float)offset;
    }
}

public sealed class TargetSpec
{
    public required string Id { get; init; }

    public required string Label { get; init; }

    public required TargetKindSpec Kind { get; init; }

    public required Vector3 BasePosition { get; init; }

    public required float Yaw { get; init; }

    public TargetMotion? Motion { get; init; }

    public Vector3 PositionAt(double time) => Motion is null ? BasePosition : BasePosition + Motion.OffsetAt(time);
}

public sealed class CannonSpec
{
    public required Vector3 Position { get; init; }

    public required float Yaw { get; init; }

    public required float YawSpread { get; init; }

    public required float PitchMin { get; init; }

    public required float PitchMax { get; init; }
}

public sealed class StressSettings
{
    public required int TargetLiveBalls { get; init; }

    public required int MaxSpawnPerTick { get; init; }

    public required IReadOnlyList<CannonSpec> Cannons { get; init; }
}

/// <summary>Resolved Phase 1 range: ground, props, targets and the despawn boundary.</summary>
public sealed class RangeLayout
{
    public required string Id { get; init; }

    /// <summary>Ground width across X (m), centred on X = 0.</summary>
    public required float Width { get; init; }

    /// <summary>Distance downrange (−Z) from the firing line (m).</summary>
    public required float Length { get; init; }

    /// <summary>Ground behind the firing line (+Z) (m).</summary>
    public required float BackMargin { get; init; }

    public required SurfaceId GroundSurface { get; init; }

    public required string GroundSurfaceName { get; init; }

    public required float BoundaryHeight { get; init; }

    public required float BoundaryMargin { get; init; }

    public required Vector3 SpawnPosition { get; init; }

    public required float SpawnYaw { get; init; }

    public required float MarkerSpacing { get; init; }

    public required float MarkerMax { get; init; }

    /// <summary>Distances that get a sign.</summary>
    public required IReadOnlyList<float> MarkerLabels { get; init; }

    public required IReadOnlyList<PropSpec> Props { get; init; }

    public required IReadOnlyList<TargetSpec> Targets { get; init; }

    /// <summary>Camera viewpoints for the game's screenshot tour (looks only).</summary>
    public IReadOnlyList<Viewpoint> Viewpoints { get; init; } = Array.Empty<Viewpoint>();

    /// <summary>Balls leaving this box are removed (netted boundary).</summary>
    public Aabb Bounds => new(
        new Vector3(-Width * 0.5f - BoundaryMargin, -1f, -Length - BoundaryMargin),
        new Vector3(Width * 0.5f + BoundaryMargin, BoundaryHeight, BackMargin + BoundaryMargin));

    /// <summary>Adds the ground plane and every prop to <paramref name="world"/>.</summary>
    public void BuildCollision(CollisionWorld world)
    {
        world.Clear();
        world.Add(new PlaneShape(Vector3.UnitY, 0f), GroundSurface, "ground");
        foreach (PropSpec prop in Props)
        {
            world.Add(prop.CreateShape(), prop.Surface, prop.Id);
        }

        world.Build();
    }
}
