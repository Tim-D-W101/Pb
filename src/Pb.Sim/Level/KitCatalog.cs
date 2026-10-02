using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Data;

namespace Pb.Sim.Level;

public sealed class KitMaterial
{
    public required int Index { get; init; }

    public required string Id { get; init; }

    public required SurfaceId Surface { get; init; }

    public required MaterialDef Def { get; init; }

    public MaterialRef Ref => new(Index, Surface);
}

/// <summary>A prop collider in the prop's frame, with its material resolved.</summary>
public sealed class PropColliderTemplate
{
    public required PrimitiveKind Kind { get; init; }

    public required Vector3 Center { get; init; }

    public required Quaternion Rotation { get; init; }

    /// <summary>Box half extents, or (radius, half height, radius) for cylinders.</summary>
    public required Vector3 HalfExtents { get; init; }

    public required MaterialRef Material { get; init; }

    public required bool Walk { get; init; }
}

public sealed class PropType
{
    public required string Id { get; init; }

    public required PropTypeDef Def { get; init; }

    public required IReadOnlyList<PropColliderTemplate> Colliders { get; init; }

    public bool HasModel => !string.IsNullOrWhiteSpace(Def.Model);

    public bool HasShape => !string.IsNullOrWhiteSpace(Def.Shape);

    /// <summary>Drawn by a model or a shape rather than by its colliders.</summary>
    public bool HasVisual => HasModel || HasShape;
}

public sealed class BuildingTemplate
{
    public required string Id { get; init; }

    public required string File { get; init; }

    public required BuildingDef Def { get; init; }
}

/// <summary>
/// The level kit: materials, prop types and building templates, with every cross-reference
/// checked at load. Errors name the file and key, like every other data file.
/// </summary>
public sealed class KitCatalog
{
    private readonly Dictionary<string, KitMaterial> _materialsById;

    private KitCatalog(IReadOnlyList<KitMaterial> materials, IReadOnlyDictionary<string, PropType> props,
        IReadOnlyDictionary<string, BuildingTemplate> buildings)
    {
        Materials = materials;
        _materialsById = materials.ToDictionary(m => m.Id, StringComparer.Ordinal);
        Props = props;
        Buildings = buildings;
    }

    public IReadOnlyList<KitMaterial> Materials { get; }

    public IReadOnlyDictionary<string, PropType> Props { get; }

    public IReadOnlyDictionary<string, BuildingTemplate> Buildings { get; }

    public bool TryGetMaterial(string id, out KitMaterial material) => _materialsById.TryGetValue(id, out material!);

    public static KitCatalog Load(IDataSource source, string indexFile, SurfaceRegistry surfaces)
    {
        KitIndexDef index = Jsonc.Load<KitIndexDef>(source, indexFile);

        MaterialsDef materialsDef = Jsonc.Load<MaterialsDef>(source, index.Materials);
        var materialErrors = new Validator(index.Materials);
        var materials = new List<KitMaterial>();
        for (int i = 0; i < materialsDef.Materials.Length; i++)
        {
            MaterialDef m = materialsDef.Materials[i];
            if (!surfaces.TryGet(m.Surface, out SurfaceId surface))
            {
                materialErrors.Item(nameof(MaterialsDef.Materials), i).Error(nameof(MaterialDef.Surface),
                    $"unknown surface '{m.Surface}' (known: {string.Join(", ", surfaces.Names)})");
            }

            materials.Add(new KitMaterial { Index = i, Id = m.Id, Surface = surface, Def = m });
        }

        materialErrors.ThrowIfErrors();
        var byId = materials.ToDictionary(m => m.Id, StringComparer.Ordinal);

        PropsDef propsDef = Jsonc.Load<PropsDef>(source, index.Props);
        var propErrors = new Validator(index.Props);
        var props = new Dictionary<string, PropType>(StringComparer.Ordinal);
        for (int i = 0; i < propsDef.Props.Length; i++)
        {
            PropTypeDef p = propsDef.Props[i];
            Validator item = propErrors.Item(nameof(PropsDef.Props), i);
            var colliders = new List<PropColliderTemplate>();
            for (int c = 0; c < p.Colliders.Length; c++)
            {
                ColliderDef cd = p.Colliders[c];
                MaterialRef material = ResolveMaterial(byId, item.Item(nameof(PropTypeDef.Colliders), c), nameof(ColliderDef.Material), cd.Material);
                colliders.Add(new PropColliderTemplate
                {
                    Kind = cd.Shape == ColliderShape.Cylinder ? PrimitiveKind.Cylinder : PrimitiveKind.Box,
                    Center = Validator.ToVector3(cd.Center_m),
                    Rotation = EulerDegrees(cd.Rotation_deg),
                    HalfExtents = cd.Shape == ColliderShape.Cylinder
                        ? new Vector3(cd.Radius_m, cd.Height_m * 0.5f, cd.Radius_m)
                        : Validator.ToVector3(cd.Size_m) * 0.5f,
                    Material = material,
                    Walk = cd.Walk,
                });
            }

            props[p.Id] = new PropType { Id = p.Id, Def = p, Colliders = colliders };
        }

        propErrors.ThrowIfErrors();

        var buildings = new Dictionary<string, BuildingTemplate>(StringComparer.Ordinal);
        foreach (string file in index.Buildings)
        {
            BuildingDef b = Jsonc.Load<BuildingDef>(source, file);
            var errors = new Validator(file);
            CheckBuilding(b, byId, props, errors);
            errors.ThrowIfErrors();
            if (!buildings.TryAdd(b.Id, new BuildingTemplate { Id = b.Id, File = file, Def = b }))
            {
                throw new DataException(file, $"id: duplicate building id '{b.Id}' (also in {buildings[b.Id].File})");
            }
        }

        return new KitCatalog(materials, props, buildings);
    }

    /// <summary>[about X, about Y, about Z] in degrees, applied Z, then X, then Y (Godot's default YXZ order).</summary>
    public static Quaternion EulerDegrees(float[]? degrees)
    {
        if (degrees is not { Length: 3 })
        {
            return Quaternion.Identity;
        }

        return Quaternion.CreateFromYawPitchRoll(
            degrees[1] * Units.DegreesToRadians, degrees[0] * Units.DegreesToRadians, degrees[2] * Units.DegreesToRadians);
    }

    internal static MaterialRef ResolveMaterial(IReadOnlyDictionary<string, KitMaterial> materials, Validator scope, string property, string? id)
    {
        if (id is not null && materials.TryGetValue(id, out KitMaterial? material))
        {
            return material.Ref;
        }

        scope.Error(property, $"unknown material '{id}'");
        return default;
    }

    private static void CheckBuilding(BuildingDef b, Dictionary<string, KitMaterial> materials, Dictionary<string, PropType> props, Validator v)
    {
        ResolveMaterial(materials, v, nameof(BuildingDef.FloorMaterial), b.FloorMaterial);
        if (b.Frames is not null)
        {
            ResolveMaterial(materials, v, nameof(BuildingDef.Frames), b.Frames);
        }

        if (b.Gutters is not null)
        {
            ResolveMaterial(materials, v, nameof(BuildingDef.Gutters), b.Gutters);
        }

        if (b.Fittings is not null)
        {
            ResolveMaterial(materials, v, nameof(BuildingDef.Fittings), b.Fittings);
        }

        if (b.CeilingLights is not null)
        {
            ResolveMaterial(materials, v, nameof(BuildingDef.CeilingLights), b.CeilingLights);
        }

        if (b.Trusses is not null)
        {
            ResolveMaterial(materials, v.Scope(nameof(BuildingDef.Trusses)), nameof(TrussesDef.Material), b.Trusses.Material);
        }
        for (int i = 0; i < b.Walls.Length; i++)
        {
            WallDef w = b.Walls[i];
            Validator item = v.Item(nameof(BuildingDef.Walls), i);
            ResolveMaterial(materials, item, nameof(WallDef.Material), w.Material);
            float height = float.IsNaN(w.Height_m) && w.Storey >= 0 && w.Storey < b.Storeys_m.Length ? b.Storeys_m[w.Storey] : w.Height_m;
            KitGeometry.CheckWall(w, height, item);
        }

        for (int i = 0; b.Floors is not null && i < b.Floors.Length; i++)
        {
            if (b.Floors[i].Material is { } floorMaterial)
            {
                ResolveMaterial(materials, v.Item(nameof(BuildingDef.Floors), i), nameof(SlabDef.Material), floorMaterial);
            }
        }

        if (b.Roof is { } roof)
        {
            Validator scope = v.Scope(nameof(BuildingDef.Roof));
            ResolveMaterial(materials, scope, nameof(RoofDef.Material), roof.Material);
            if (roof.CeilingMaterial is not null)
            {
                ResolveMaterial(materials, scope, nameof(RoofDef.CeilingMaterial), roof.CeilingMaterial);
                if ((float.IsNaN(roof.Thickness_m) ? b.SlabThickness_m : roof.Thickness_m) < 0.05f)
                {
                    scope.Error(nameof(RoofDef.CeilingMaterial), "a roof with a ceiling needs to be at least 5 cm thick");
                }
            }
            if (roof.Parapet_m > 0f)
            {
                ResolveMaterial(materials, scope, nameof(RoofDef.ParapetMaterial), roof.ParapetMaterial ?? roof.Material);
            }
        }

        for (int i = 0; b.Stairs is not null && i < b.Stairs.Length; i++)
        {
            ResolveMaterial(materials, v.Item(nameof(BuildingDef.Stairs), i), nameof(StairDef.Material), b.Stairs[i].Material);
        }

        for (int i = 0; b.Columns is not null && i < b.Columns.Length; i++)
        {
            ResolveMaterial(materials, v.Item(nameof(BuildingDef.Columns), i), nameof(ColumnDef.Material), b.Columns[i].Material);
        }

        for (int i = 0; b.Props is not null && i < b.Props.Length; i++)
        {
            if (!props.ContainsKey(b.Props[i].Prop))
            {
                v.Item(nameof(BuildingDef.Props), i).Error(nameof(PropPlacementDef.Prop), $"unknown prop '{b.Props[i].Prop}'");
            }
        }
    }
}
