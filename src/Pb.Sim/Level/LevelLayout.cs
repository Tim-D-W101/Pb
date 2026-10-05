using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Data;

namespace Pb.Sim.Level;

/// <summary>A building in a level: its template, the owner of its primitives, and its plan frame (origin and yaw).</summary>
public sealed record PlacedBuilding(BuildingTemplate Template, int Owner, PlanFrame Frame);

/// <summary>A free-standing wall run in a level: its definition, the owner of its primitives, and its plan frame.</summary>
public sealed record PlacedWall(WallDef Def, int Owner, PlanFrame Frame);

public sealed class PropInstance
{
    public required PropType Type { get; init; }

    public required Vector3 Position { get; init; }

    public required float Yaw { get; init; }

    public required int FirstPrimitive { get; init; }

    public required int PrimitiveCount { get; init; }
}

/// <summary>A named region in world space (axis-aligned box).</summary>
public sealed class AreaSpec
{
    public required string Name { get; init; }

    public required Aabb Box { get; init; }

    public required bool Indoor { get; init; }

    /// <summary>0 = dark, 1 = daylight.</summary>
    public required float Light { get; init; }

    public float Volume => (Box.Max.X - Box.Min.X) * (Box.Max.Y - Box.Min.Y) * (Box.Max.Z - Box.Min.Z);
}

/// <summary>A place to start, facing <see cref="Yaw"/>.</summary>
public readonly record struct SpawnPoint(Vector3 Position, float Yaw);

public sealed class OpponentSpawn
{
    public required string Id { get; init; }

    public required Vector3 Position { get; init; }

    public required float Yaw { get; init; }

    public required IReadOnlyList<string> Roles { get; init; }

    public PatrolRoute? Patrol { get; init; }
}

public sealed class PatrolRoute
{
    public required string Id { get; init; }

    public required IReadOnlyList<Vector3> Points { get; init; }

    public required bool Loop { get; init; }

    public required float Pause { get; init; }
}

public readonly record struct Viewpoint(string Name, Vector3 Position, float Yaw, float Pitch);

public sealed class PickupSpec
{
    public required string Id { get; init; }

    public required PickupKind Kind { get; init; }

    public required Vector3 Position { get; init; }
}

/// <summary>
/// A resolved, playable level: every piece of geometry as analytic primitives in world space, plus
/// spawns, patrol routes, areas and pickups. Built by <see cref="LevelFactory"/> from a level file and
/// the kit; the Godot layer draws and walks on the same primitives the sim collides paint with.
/// </summary>
public sealed class LevelLayout
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    /// <summary>Paint leaving this box is removed; walking stops at its sides.</summary>
    public required Aabb Bounds { get; init; }

    public required KitMaterial GroundMaterial { get; init; }

    public required IReadOnlyList<KitMaterial> Materials { get; init; }

    public required IReadOnlyList<LevelPrimitive> Primitives { get; init; }

    /// <summary>Windows, doors, gaps and roof holes, in world space.</summary>
    public required IReadOnlyList<Aperture> Apertures { get; init; }

    public required IReadOnlyList<PropInstance> Props { get; init; }

    /// <summary>The buildings, each with its template and where it stands (presentation dresses them from their templates).</summary>
    public IReadOnlyList<PlacedBuilding> Buildings { get; init; } = Array.Empty<PlacedBuilding>();

    /// <summary>The free-standing wall runs (the perimeter, yard walls), each with its definition (presentation dresses them).</summary>
    public IReadOnlyList<PlacedWall> Walls { get; init; } = Array.Empty<PlacedWall>();

    /// <summary>What the game draws out beyond the level, if anything (presentation only).</summary>
    public SceneryDef? Scenery { get; init; }

    /// <summary>Paint on the open ground (the level's "markings"), passed through for the game to draw.</summary>
    public MarkingsDef? Markings { get; init; }

    /// <summary>Debug names for primitive owners ("warehouse#0", "prop:oil_drum#3", "wall#1").</summary>
    public required IReadOnlyList<string> Owners { get; init; }

    /// <summary>Where you can come in (one at random each round); the first is the default.</summary>
    public required IReadOnlyList<SpawnPoint> PlayerSpawns { get; init; }

    public Vector3 PlayerSpawn => PlayerSpawns[0].Position;

    public float PlayerSpawnYaw => PlayerSpawns[0].Yaw;

    /// <summary>Opponents dealt random starts begin inside this box.</summary>
    public required Aabb SpawnArea { get; init; }

    public required Vector3 DeadZone { get; init; }

    public required IReadOnlyList<AreaSpec> Areas { get; init; }

    public required IReadOnlyList<OpponentSpawn> OpponentSpawns { get; init; }

    public required IReadOnlyList<PatrolRoute> Patrols { get; init; }

    public required IReadOnlyList<PickupSpec> Pickups { get; init; }

    public required IReadOnlyList<Viewpoint> Viewpoints { get; init; }

    /// <summary>Adds the ground plane and every paint primitive to <paramref name="world"/>.</summary>
    public void BuildCollision(CollisionWorld world)
    {
        world.Clear();
        world.Add(new PlaneShape(Vector3.UnitY, 0f), GroundMaterial.Surface, "ground");
        for (int i = 0; i < Primitives.Count; i++)
        {
            LevelPrimitive p = Primitives[i];
            if (p.Has(PrimitiveFlags.Paint))
            {
                world.Add(p.CreateShape(), p.Surface, Owners[p.Owner]);
            }
        }

        world.Build();
    }

    /// <summary>The most specific (smallest) area containing <paramref name="point"/>, or null.</summary>
    public AreaSpec? AreaAt(Vector3 point)
    {
        // An indexed loop: bots ask this every tick, and an interface enumerator would allocate.
        AreaSpec? best = null;
        for (int i = 0; i < Areas.Count; i++)
        {
            AreaSpec area = Areas[i];
            if (area.Box.Contains(point) && (best is null || area.Volume < best.Volume))
            {
                best = area;
            }
        }

        return best;
    }
}
