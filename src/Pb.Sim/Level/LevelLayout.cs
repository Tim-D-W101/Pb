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

    /// <summary>What it sounds like inside (presentation only; empty for the default by size).</summary>
    public string Tone { get; init; } = "";

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
/// One place to play in a level: the whole of it (no <see cref="Bounds"/>), or the part inside them.
/// <see cref="LevelLayout.ForPlace"/> makes the level for a round there.
/// </summary>
public sealed class PlaceSpec
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    /// <summary>The play area (world; its height is the level's), or null for the whole level.</summary>
    public Aabb? Bounds { get; init; }

    /// <summary>Where you come in, or null for the level's own entries inside the place.</summary>
    public IReadOnlyList<SpawnPoint>? PlayerSpawns { get; init; }

    /// <summary>Where the eliminated walk off to, or null for the level's.</summary>
    public Vector3? DeadZone { get; init; }

    /// <summary>The starts' distances are scaled by this here.</summary>
    public float SpawnScale { get; init; } = 1f;

    public bool Whole => Bounds is null;

    /// <summary>Whether <paramref name="point"/> is in the place (on the plan; anywhere for the whole level).</summary>
    public bool Contains(Vector3 point) =>
        Bounds is not { } b || (point.X >= b.Min.X && point.X <= b.Max.X && point.Z >= b.Min.Z && point.Z <= b.Max.Z);
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

    /// <summary>The door leaves hung in its doorways (the sim's <see cref="DoorSet"/> moves them).</summary>
    public IReadOnlyList<DoorSpec> Doors { get; init; } = Array.Empty<DoorSpec>();

    private HashSet<int>? _hung;

    /// <summary>Whether a door leaf hangs in the aperture at <paramref name="aperture"/> (light and cobwebs leave those alone).</summary>
    public bool IsHung(int aperture)
    {
        _hung ??= Doors.Select(d => d.Aperture).ToHashSet();
        return _hung.Contains(aperture);
    }

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

    /// <summary>Where the objectives are played (none: the level offers only eliminate).</summary>
    public LevelObjectives Objectives { get; init; } = LevelObjectives.None;

    /// <summary>Railway tracks (their rails are among the primitives; the game draws them and their sleepers).</summary>
    public IReadOnlyList<TrackSpec> Tracks { get; init; } = Array.Empty<TrackSpec>();

    /// <summary>Where in the level you can choose to play, the menu's order (the whole level among them).</summary>
    public IReadOnlyList<PlaceSpec> Places { get; init; } = Array.Empty<PlaceSpec>();

    /// <summary>The place this layout is for: the whole level, unless <see cref="ForPlace"/> made it for a part.</summary>
    public PlaceSpec? Place { get; init; }

    /// <summary>The place called <paramref name="id"/> (null or unknown: the first).</summary>
    public PlaceSpec PlaceOf(string? id) => Places.FirstOrDefault(p => p.Id == id) ?? Places[0];

    /// <summary>
    /// The level for a round in <paramref name="place"/>: walled in by invisible walking walls round it (paint
    /// and sight pass), with its own entries and walk-off spot, and only the opponent spawns, patrols,
    /// pickups, viewpoints and objectives inside it. Every door and track stays (those outside are just out
    /// of reach). The whole level comes back as it is.
    /// </summary>
    public LevelLayout ForPlace(PlaceSpec place)
    {
        if (place.Bounds is not { } b)
        {
            return Place == place ? this : Copy(place, Primitives, Owners, PlayerSpawns, SpawnArea, DeadZone);
        }

        var owners = new List<string>(Owners) { $"place:{place.Id}" };
        var sink = new PrimitiveSink();
        sink.Items.AddRange(Primitives);
        int owner = owners.Count - 1;
        const float thickness = 0.3f;
        float height = Bounds.Max.Y - Bounds.Min.Y;
        float cy = (Bounds.Max.Y + Bounds.Min.Y) * 0.5f;
        MaterialRef material = GroundMaterial.Ref;
        void Wall(Vector3 center, Vector3 half) =>
            sink.AddBox(PlanFrame.Identity, center, Quaternion.Identity, half, material, PrimitiveFlags.Walk, PrimitiveRole.Boundary, owner);

        float sx = b.Max.X - b.Min.X, sz = b.Max.Z - b.Min.Z, h = thickness * 0.5f;
        Wall(new Vector3(b.Min.X - h, cy, (b.Min.Z + b.Max.Z) * 0.5f), new Vector3(h, height * 0.5f, sz * 0.5f + thickness));
        Wall(new Vector3(b.Max.X + h, cy, (b.Min.Z + b.Max.Z) * 0.5f), new Vector3(h, height * 0.5f, sz * 0.5f + thickness));
        Wall(new Vector3((b.Min.X + b.Max.X) * 0.5f, cy, b.Min.Z - h), new Vector3(sx * 0.5f + thickness, height * 0.5f, h));
        Wall(new Vector3((b.Min.X + b.Max.X) * 0.5f, cy, b.Max.Z + h), new Vector3(sx * 0.5f + thickness, height * 0.5f, h));

        IReadOnlyList<SpawnPoint> entries = place.PlayerSpawns ?? PlayerSpawns.Where(s => place.Contains(s.Position)).ToArray();
        // Random starts in the part of the level's spawn ground inside the place (a place reaching out past the
        // perimeter for its way in doesn't start anyone out there); all of the place if none of it is.
        float x0 = MathF.Max(b.Min.X, SpawnArea.Min.X), x1 = MathF.Min(b.Max.X, SpawnArea.Max.X);
        float z0 = MathF.Max(b.Min.Z, SpawnArea.Min.Z), z1 = MathF.Min(b.Max.Z, SpawnArea.Max.Z);
        var area = x1 - x0 > 4f && z1 - z0 > 4f
            ? new Aabb(new Vector3(x0, SpawnArea.Min.Y, z0), new Vector3(x1, SpawnArea.Max.Y, z1))
            : new Aabb(new Vector3(b.Min.X, SpawnArea.Min.Y, b.Min.Z), new Vector3(b.Max.X, SpawnArea.Max.Y, b.Max.Z));
        return Copy(place, sink.Items, owners, entries, area, place.DeadZone ?? DeadZone);
    }

    /// <summary>
    /// The objectives a round in <paramref name="place"/> can play: the case spots and rooms to hold inside it (a
    /// room only if all of it is), and the ways out inside it, else its entries (out the way you came in).
    /// </summary>
    private LevelObjectives ObjectivesIn(PlaceSpec place, IReadOnlyList<SpawnPoint> entries)
    {
        if (place.Whole)
        {
            return Objectives;
        }

        ExitSpec[] exits = Objectives.Exits.Where(e => place.Contains(e.Position)).ToArray();
        if (exits.Length == 0)
        {
            exits = entries.Select(s => new ExitSpec("where you came in", s.Position)).ToArray();
        }

        return new LevelObjectives
        {
            CaseSpots = Objectives.CaseSpots.Where(c => place.Contains(c.Position)).ToArray(),
            Exits = exits,
            Rooms = Objectives.Rooms.Where(r => r.Boxes.All(box => place.Contains((box.Min + box.Max) * 0.5f))).ToArray(),
        };
    }

    private LevelLayout Copy(PlaceSpec place, IReadOnlyList<LevelPrimitive> primitives, IReadOnlyList<string> owners, IReadOnlyList<SpawnPoint> entries,
        Aabb spawnArea, Vector3 deadZone)
    {
        IReadOnlyList<Viewpoint> views = Viewpoints.Where(v => place.Contains(v.Position)).ToArray();
        return new LevelLayout
        {
            Id = Id,
            DisplayName = DisplayName,
            Description = Description,
            Bounds = Bounds,
            GroundMaterial = GroundMaterial,
            Materials = Materials,
            Primitives = primitives,
            Apertures = Apertures,
            Doors = Doors,
            Props = Props,
            Buildings = Buildings,
            Walls = Walls,
            Scenery = Scenery,
            Markings = Markings,
            Owners = owners,
            PlayerSpawns = entries,
            SpawnArea = spawnArea,
            DeadZone = deadZone,
            Areas = Areas,
            OpponentSpawns = OpponentSpawns.Where(s => place.Contains(s.Position)).ToArray(),
            Patrols = Patrols.Where(r => r.Points.All(place.Contains)).ToArray(),
            Pickups = Pickups.Where(p => place.Contains(p.Position)).ToArray(),
            Viewpoints = views.Count > 0 ? views : Viewpoints,
            Objectives = ObjectivesIn(place, entries),
            Tracks = Tracks,
            Places = Places,
            Place = place,
        };
    }

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
