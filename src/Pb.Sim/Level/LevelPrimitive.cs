using System.Numerics;
using Pb.Sim.Collision;

namespace Pb.Sim.Level;

public enum PrimitiveKind : byte
{
    Box,
    Cylinder,
}

/// <summary>What a level primitive is used for. One primitive can feed several systems.</summary>
[Flags]
public enum PrimitiveFlags : byte
{
    None = 0,
    /// <summary>Paint collides with it (sim collision world).</summary>
    Paint = 1 << 0,
    /// <summary>Blocks walking (Godot collision) and is navigation-mesh source geometry.</summary>
    Walk = 1 << 1,
    /// <summary>Drawn as greybox geometry (props with a model are drawn by the model instead).</summary>
    Render = 1 << 2,
    /// <summary>Large enough to hide what's behind it (occlusion culling).</summary>
    Occluder = 1 << 3,
    /// <summary>Tall enough for bots to take cover behind.</summary>
    Cover = 1 << 4,
}

/// <summary>What part of a level a primitive came from (debug names, cover rules).</summary>
public enum PrimitiveRole : byte
{
    Wall,
    Floor,
    Roof,
    Stair,
    Ramp,
    Column,
    Prop,
    GroundPatch,
    Boundary,
}

/// <summary>
/// One analytic piece of level geometry in world space. Paint collision, walking collision,
/// occluders and the greybox mesh are all generated from the same primitive, so they can't drift
/// apart. Cylinders store (radius, half height, radius) in <see cref="HalfExtents"/> and use the
/// rotated +Y axis.
/// </summary>
public sealed class LevelPrimitive
{
    public required PrimitiveKind Kind { get; init; }

    public required Vector3 Center { get; init; }

    public required Quaternion Rotation { get; init; }

    public required Vector3 HalfExtents { get; init; }

    public required int Material { get; init; }

    public required SurfaceId Surface { get; init; }

    public required PrimitiveFlags Flags { get; init; }

    public required PrimitiveRole Role { get; init; }

    /// <summary>Index into <see cref="LevelLayout.Owners"/>: which building, wall run or prop made it.</summary>
    public required int Owner { get; init; }

    public bool Has(PrimitiveFlags flag) => (Flags & flag) == flag;

    public float Height => Kind == PrimitiveKind.Cylinder ? HalfExtents.Y * 2f : Bounds.Max.Y - Bounds.Min.Y;

    public Aabb Bounds => CreateShape().Bounds;

    public Shape CreateShape() => Kind switch
    {
        PrimitiveKind.Box => new BoxShape(Center, Rotation, HalfExtents),
        PrimitiveKind.Cylinder => new CylinderShape(Center, Vector3.Transform(Vector3.UnitY, Rotation), HalfExtents.Y, HalfExtents.X),
        _ => throw new InvalidOperationException($"Unknown primitive kind {Kind}."),
    };
}
