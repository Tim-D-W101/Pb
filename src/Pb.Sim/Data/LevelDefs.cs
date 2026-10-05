using System.Globalization;

namespace Pb.Sim.Data;

// Data-file definitions for the level kit (kit/*.jsonc), buildings (kit/buildings/*.jsonc), levels
// (levels/*.jsonc) and the level ladder. Plan coordinates are [x, z] pairs in metres: x east, z south
// (north is −Z, the direction yaw 0 faces). Rectangles are [x0, z0, x1, z1].
#pragma warning disable CA1707 // Identifiers should not contain underscores: the suffix is the unit.

/// <summary>kit/kit.jsonc: which files hold the materials, prop types and building templates.</summary>
public sealed class KitIndexDef : IValidatable
{
    public string Materials { get; set; } = "";

    public string Props { get; set; } = "";

    /// <summary>The door leaves that door openings can carry (kit/doors.jsonc).</summary>
    [Optional]
    public string? Doors { get; set; }

    public string[] Buildings { get; set; } = Array.Empty<string>();

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Materials), Materials);
        v.NotEmpty(nameof(Props), Props);
        for (int i = 0; i < Buildings.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(Buildings[i]))
            {
                v.Item(nameof(Buildings), i).Error("path", "must not be empty");
            }
        }
    }
}

/// <summary>How a material is drawn while it has no texture: picks the procedural pattern in the shader.</summary>
public enum MaterialPattern
{
    Plain,
    Concrete,
    Block,
    Brick,
    Plaster,
    Corrugated,
    Metal,
    Rust,
    Asphalt,
    Dirt,
    Gravel,
    Wood,
    Tarp,
    Rubber,
    Glass,
    Grass,
}

public sealed class MaterialsDef : IValidatable
{
    public MaterialDef[] Materials { get; set; } = Array.Empty<MaterialDef>();

    public void Validate(Validator v) => LevelDefChecks.UniqueIds(v, nameof(Materials), Materials, m => m.Id);
}

/// <summary>
/// One surface material. <c>surface</c> picks the paint physics row in break_model.jsonc; the rest is
/// presentation (the sim ignores it). Texture paths are optional: without them the shader draws a
/// procedural <c>pattern</c> in <c>color</c>.
/// </summary>
public sealed class MaterialDef : IValidatable
{
    public string Id { get; set; } = "";

    public string Surface { get; set; } = "";

    public string Color { get; set; } = "";

    public MaterialPattern Pattern { get; set; }

    public float Tile_m { get; set; }

    public float Roughness { get; set; }

    public float Weathering { get; set; }

    [Optional]
    public float Metallic { get; set; }

    [Optional]
    public float Alpha { get; set; } = 1f;

    [Optional]
    public string? Albedo { get; set; }

    [Optional]
    public string? Normal { get; set; }

    [Optional]
    public string? RoughnessMap { get; set; }

    /// <summary>
    /// Shuffles the texture in broad patches so its repeat doesn't show as a grid over a large area.
    /// Only for surfaces without a regular pattern: bricks, planks or corrugations would come apart.
    /// </summary>
    [Optional]
    public bool BreakUpRepeat { get; set; }

    /// <summary>
    /// An sRGB colour multiplied with the texture, so one photo serves several materials (a burnt
    /// car's duller rust from the rusted-steel photo). White leaves it as it is.
    /// </summary>
    [Optional]
    public string? Tint { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.NotEmpty(nameof(Surface), Surface);
        LevelDefChecks.HexColor(v, nameof(Color), Color);
        if (Tint is not null)
        {
            LevelDefChecks.HexColor(v, nameof(Tint), Tint);
        }
        v.InRange(nameof(Tile_m), Tile_m, 0.05, 100);
        v.InRange(nameof(Roughness), Roughness, 0, 1);
        v.InRange(nameof(Weathering), Weathering, 0, 1);
        v.InRange(nameof(Metallic), Metallic, 0, 1);
        v.InRange(nameof(Alpha), Alpha, 0.05, 1);
    }
}

public sealed class PropsDef : IValidatable
{
    public PropTypeDef[] Props { get; set; } = Array.Empty<PropTypeDef>();

    public void Validate(Validator v) => LevelDefChecks.UniqueIds(v, nameof(Props), Props, p => p.Id);
}

/// <summary>
/// A placeable prop. Its <c>colliders</c> are what paint and walking actually hit, so a generated 3D
/// model or a shape built in code can be swapped in without changing gameplay. With neither, the
/// colliders are drawn.
/// </summary>
public sealed class PropTypeDef : IValidatable
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    [Optional]
    public string? Model { get; set; }

    /// <summary>
    /// A detail model the game builds in code from the colliders (pallets, crate, tyres…; the game
    /// lists them). Drawn when there's no <see cref="Model"/>, or it doesn't load.
    /// </summary>
    [Optional]
    public string? Shape { get; set; }

    [Optional]
    public float ModelScale { get; set; } = 1f;

    [Optional]
    public float[]? ModelOffset_m { get; set; }

    [Optional]
    public float ModelYaw_deg { get; set; }

    public ColliderDef[] Colliders { get; set; } = Array.Empty<ColliderDef>();

    /// <summary>Whether bots may use this prop as cover.</summary>
    [Optional]
    public bool Cover { get; set; } = true;

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.NotEmpty(nameof(DisplayName), DisplayName);
        v.InRange(nameof(ModelScale), ModelScale, 0.01, 100);
        if (ModelOffset_m is not null)
        {
            v.Vector(nameof(ModelOffset_m), ModelOffset_m);
        }

        if (Colliders.Length == 0)
        {
            v.Error(nameof(Colliders), "must list at least one collider");
        }

        for (int i = 0; i < Colliders.Length; i++)
        {
            Colliders[i].Validate(v.Item(nameof(Colliders), i));
        }
    }
}

public enum ColliderShape
{
    Box,
    Cylinder,
}

/// <summary>A prop collider in the prop's frame (origin on the ground, facing −Z).</summary>
public sealed class ColliderDef : IValidatable
{
    public ColliderShape Shape { get; set; }

    public float[] Center_m { get; set; } = Array.Empty<float>();

    [Optional]
    public float[]? Size_m { get; set; }

    [Optional]
    public float Radius_m { get; set; }

    [Optional]
    public float Height_m { get; set; }

    /// <summary>[about X, about Y, about Z] in degrees, applied Z, then X, then Y (Godot's default order).</summary>
    [Optional]
    public float[]? Rotation_deg { get; set; }

    public string Material { get; set; } = "";

    /// <summary>Whether it blocks walking as well as paint.</summary>
    [Optional]
    public bool Walk { get; set; } = true;

    public void Validate(Validator v)
    {
        v.Vector(nameof(Center_m), Center_m);
        v.NotEmpty(nameof(Material), Material);
        if (Rotation_deg is not null)
        {
            v.Vector(nameof(Rotation_deg), Rotation_deg);
        }

        switch (Shape)
        {
            case ColliderShape.Box:
                LevelDefChecks.PositiveVector(v, nameof(Size_m), Size_m, 3);
                break;
            case ColliderShape.Cylinder:
                v.InRange(nameof(Radius_m), Radius_m, 0.005, 50);
                v.InRange(nameof(Height_m), Height_m, 0.005, 100);
                break;
        }
    }
}

/// <summary>
/// A building template in its own frame: walls by storey, upper floors and roof slabs with holes,
/// stairs, columns and props. Levels place it with a position and yaw.
/// </summary>
public sealed class BuildingDef : IValidatable
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    /// <summary>Floor-to-floor height of each storey, bottom up.</summary>
    public float[] Storeys_m { get; set; } = Array.Empty<float>();

    public float SlabThickness_m { get; set; }

    /// <summary>Outline used for the ground-floor finish: [x0, z0, x1, z1].</summary>
    public float[] Footprint_m { get; set; } = Array.Empty<float>();

    public string FloorMaterial { get; set; } = "";

    /// <summary>
    /// Material of the frames the game draws in this building's windows and doors (presentation
    /// only: the openings stay open to paint). Omit for bare openings.
    /// </summary>
    [Optional]
    public string? Frames { get; set; }

    /// <summary>
    /// Material of the gutters along the roof's eaves and the downpipes from them (presentation only;
    /// a parapet roof gets just the downpipes). Omit for none.
    /// </summary>
    [Optional]
    public string? Gutters { get; set; }

    /// <summary>Steel trusses under the roof (presentation only). Omit for none.</summary>
    [Optional]
    public TrussesDef? Trusses { get; set; }

    /// <summary>
    /// Material of the fittings on the outside walls: lamps over the doors, junction boxes, vents and
    /// pipes (presentation only). Omit for none.
    /// </summary>
    [Optional]
    public string? Fittings { get; set; }

    /// <summary>
    /// Material of the fluorescent fittings on every storey's ceiling, some hanging askew and some gone
    /// (presentation only). Omit for none.
    /// </summary>
    [Optional]
    public string? CeilingLights { get; set; }

    /// <summary>Material of the skirting boards along the foot of the inside walls (presentation only). Omit for none.</summary>
    [Optional]
    public string? Skirting { get; set; }

    /// <summary>Paint on the floors and walls: lines, hatched areas, stencilled numbers, striped columns (presentation only).</summary>
    [Optional]
    public MarkingsDef? Markings { get; set; }

    public WallDef[] Walls { get; set; } = Array.Empty<WallDef>();

    [Optional]
    public SlabDef[]? Floors { get; set; }

    [Optional]
    public RoofDef? Roof { get; set; }

    [Optional]
    public StairDef[]? Stairs { get; set; }

    [Optional]
    public ColumnDef[]? Columns { get; set; }

    [Optional]
    public PropPlacementDef[]? Props { get; set; }

    [Optional]
    public AreaDef[]? Areas { get; set; }

    public float TotalHeight
    {
        get
        {
            float sum = 0f;
            foreach (float h in Storeys_m)
            {
                sum += h;
            }

            return sum;
        }
    }

    public float StoreyBase(int storey)
    {
        float sum = 0f;
        for (int i = 0; i < storey && i < Storeys_m.Length; i++)
        {
            sum += Storeys_m[i];
        }

        return sum;
    }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.NotEmpty(nameof(DisplayName), DisplayName);
        if (Storeys_m.Length == 0)
        {
            v.Error(nameof(Storeys_m), "must list at least one storey");
        }

        for (int i = 0; i < Storeys_m.Length; i++)
        {
            if (!(Storeys_m[i] >= 1.5f && Storeys_m[i] <= 30f))
            {
                v.Error(nameof(Storeys_m), $"storey {i} height must be in [1.5, 30] (got {Storeys_m[i].ToString("0.###", CultureInfo.InvariantCulture)})");
            }
        }

        v.InRange(nameof(SlabThickness_m), SlabThickness_m, 0.02, 2);
        LevelDefChecks.Rect(v, nameof(Footprint_m), Footprint_m);
        v.NotEmpty(nameof(FloorMaterial), FloorMaterial);
        for (int i = 0; i < Walls.Length; i++)
        {
            Validator item = v.Item(nameof(Walls), i);
            Walls[i].Validate(item);
            if (Walls[i].Storey < 0 || Walls[i].Storey >= Math.Max(1, Storeys_m.Length))
            {
                item.Error(nameof(WallDef.Storey), $"must be a storey index in [0, {Storeys_m.Length - 1}]");
            }
        }

        LevelDefChecks.Items(v, nameof(Floors), Floors);
        Roof?.Validate(v.Scope(nameof(Roof)));
        Trusses?.Validate(v.Scope(nameof(Trusses)));
        Markings?.Validate(v.Scope(nameof(Markings)));
        LevelDefChecks.Items(v, nameof(Stairs), Stairs);
        LevelDefChecks.Items(v, nameof(Columns), Columns);
        LevelDefChecks.Items(v, nameof(Props), Props);
        LevelDefChecks.Items(v, nameof(Areas), Areas);
    }
}

public enum OpeningKind
{
    Door,
    Window,
    Gap,
}

public enum WallStyle
{
    /// <summary>A solid wall: piers, sills and lintels around its openings.</summary>
    Solid,

    /// <summary>
    /// An open railing: posts, a top rail, a mid rail and a toe board, all <c>thickness_m</c> thick.
    /// Paint and sight pass between the members; only gap openings are allowed.
    /// </summary>
    Railing,
}

/// <summary>
/// A wall run: a polyline in plan, extruded from its base elevation, with door and window openings
/// cut into its segments. Segment k runs from point k to point k+1 (and the last back to the first
/// when <c>closed</c>). Corners must turn by 90° or less.
/// </summary>
public sealed class WallDef : IValidatable
{
    /// <summary>Building walls only: the storey whose floor is the wall's base and whose height is its default height.</summary>
    [Optional]
    public int Storey { get; set; }

    /// <summary>Overrides the base elevation (required for level walls).</summary>
    [Optional]
    public float BaseElevation_m { get; set; } = float.NaN;

    /// <summary>Overrides the height (required for level walls).</summary>
    [Optional]
    public float Height_m { get; set; } = float.NaN;

    public float[][] Points_m { get; set; } = Array.Empty<float[]>();

    [Optional]
    public bool Closed { get; set; }

    public float Thickness_m { get; set; }

    public string Material { get; set; } = "";

    [Optional]
    public OpeningDef[]? Openings { get; set; }

    [Optional]
    public WallStyle Style { get; set; }

    /// <summary>Railings only: the largest gap between posts.</summary>
    [Optional]
    public float PostSpacing_m { get; set; } = 1.5f;

    /// <summary>Piers, coping, barbed wire and rubble the game draws on the wall (presentation only).</summary>
    [Optional]
    public WallDressingDef? Dressing { get; set; }

    public int SegmentCount => Points_m.Length < 2 ? 0 : Closed ? Points_m.Length : Points_m.Length - 1;

    public void Validate(Validator v)
    {
        if (Points_m.Length < 2)
        {
            v.Error(nameof(Points_m), "needs at least two points");
        }

        for (int i = 0; i < Points_m.Length; i++)
        {
            v.Vector(nameof(Points_m) + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", Points_m[i], 2);
        }

        if (Closed && Points_m.Length < 3)
        {
            v.Error(nameof(Closed), "a closed wall needs at least three points");
        }

        v.InRange(nameof(Thickness_m), Thickness_m, 0.01, 5);
        v.InRange(nameof(PostSpacing_m), PostSpacing_m, 0.3, 10);
        v.NotEmpty(nameof(Material), Material);
        if (!float.IsNaN(BaseElevation_m))
        {
            v.InRange(nameof(BaseElevation_m), BaseElevation_m, -10, 200);
        }

        if (!float.IsNaN(Height_m))
        {
            v.InRange(nameof(Height_m), Height_m, 0.05, 100);
        }

        LevelDefChecks.Items(v, nameof(Openings), Openings);
        Dressing?.Validate(v.Scope(nameof(Dressing)));
        foreach (int gate in Dressing?.FallenGates ?? System.Array.Empty<int>())
        {
            if (Openings is null || gate < 0 || gate >= Openings.Length || Openings[gate].Kind != OpeningKind.Gap)
            {
                v.Scope(nameof(Dressing)).Error(nameof(WallDressingDef.FallenGates), $"{gate} must be the index of one of the wall's gap openings");
            }
        }

        for (int i = 0; Openings is not null && i < Openings.Length; i++)
        {
            if (Openings[i].Segment < 0 || Openings[i].Segment >= SegmentCount)
            {
                v.Item(nameof(Openings), i).Error(nameof(OpeningDef.Segment), $"must be a segment index in [0, {SegmentCount - 1}]");
            }
        }
    }
}

/// <summary>
/// What the game draws on a wall run besides the wall (presentation only; paint and walking see just
/// the wall): piers this far apart (0 for none), this wide, standing a little proud of the wall, a
/// coping course along its top, barbed wire on brackets leaning out (away from the middle of a closed
/// run), hanging loose at the breaks, broken blocks on the ground either side of each break, and gates
/// lying off their hinges outside the gaps listed in fallenGates.
/// </summary>
public sealed class WallDressingDef : IValidatable
{
    [Optional]
    public float PierSpacing_m { get; set; }

    [Optional]
    public float PierSize_m { get; set; } = 0.45f;

    /// <summary>The piers' material; the wall's when left out.</summary>
    [Optional]
    public string? PierMaterial { get; set; }

    [Optional]
    public string? Coping { get; set; }

    [Optional]
    public string? Wire { get; set; }

    [Optional]
    public bool Rubble { get; set; }

    /// <summary>Openings (indices into the wall's openings, each a gap) whose gate lies off its hinges on the ground outside.</summary>
    [Optional]
    public int[]? FallenGates { get; set; }

    public void Validate(Validator v)
    {
        if (PierSpacing_m != 0f)
        {
            v.InRange(nameof(PierSpacing_m), PierSpacing_m, 1, 50);
        }

        v.InRange(nameof(PierSize_m), PierSize_m, 0.1, 2);
    }
}

/// <summary>An opening in one wall segment, centred <c>at_m</c> metres from the segment's start point.</summary>
public sealed class OpeningDef : IValidatable
{
    public int Segment { get; set; }

    public float At_m { get; set; }

    public float Width_m { get; set; }

    public OpeningKind Kind { get; set; }

    /// <summary>Height of the opening's bottom above the wall base.</summary>
    [Optional]
    public float Sill_m { get; set; }

    /// <summary>Opening height; omitted means up to the top of the wall (no lintel).</summary>
    [Optional]
    public float Height_m { get; set; } = float.NaN;

    /// <summary>Door openings only: the door hung in it (omit for an empty doorway).</summary>
    [Optional]
    public DoorLeafDef? Leaf { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(At_m), At_m, 0, 1000);
        v.InRange(nameof(Width_m), Width_m, 0.2, 100);
        v.InRange(nameof(Sill_m), Sill_m, 0, 50);
        if (!float.IsNaN(Height_m))
        {
            v.InRange(nameof(Height_m), Height_m, 0.2, 100);
        }

        if (Leaf is not null)
        {
            Leaf.Validate(v.Scope(nameof(Leaf)));
            if (Kind != OpeningKind.Door)
            {
                v.Error(nameof(Leaf), "only door openings take a leaf");
            }

            if (float.IsNaN(Height_m))
            {
                v.Error(nameof(Leaf), "a door with a leaf needs height_m");
            }

            if (Width_m / (Leaf.Double ? 2f : 1f) < 0.3f)
            {
                v.Error(nameof(Leaf), "each leaf must be at least 0.3 m wide");
            }
        }
    }
}

public enum DoorHinge
{
    /// <summary>On the edge of the opening nearer the segment's start point.</summary>
    Start,

    /// <summary>On the edge nearer the segment's end point.</summary>
    End,
}

public enum DoorSwing
{
    /// <summary>Into the side on your left, looking along the segment from its start to its end.</summary>
    Left,

    Right,

    /// <summary>Either way: away from whoever opens it (a swing door).</summary>
    Both,
}

public enum DoorStart
{
    /// <summary>Shut, open or ajar by chance each round (rules.jsonc "doors.randomStart").</summary>
    Random,

    Shut,

    Open,

    /// <summary>Partly open (rules.jsonc "doors.ajar").</summary>
    Ajar,
}

/// <summary>
/// A door hung in a door opening: which kind of leaf (kit/doors.jsonc), its hinge edge and the side it swings to (or,
/// sliding, the edge it slides towards and the face it slides on), one leaf or a pair, and how it starts the round.
/// </summary>
public sealed class DoorLeafDef : IValidatable
{
    public string Door { get; set; } = "";

    [Optional]
    public DoorHinge Hinge { get; set; }

    [Optional]
    public DoorSwing Swing { get; set; }

    /// <summary>Slides along the wall instead of swinging (towards its hinge edge, on its swing side).</summary>
    [Optional]
    public bool Sliding { get; set; }

    /// <summary>Two leaves, hinged at both edges and opened together.</summary>
    [Optional]
    public bool Double { get; set; }

    [Optional]
    public DoorStart Start { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Door), Door);
        if (Sliding && Swing == DoorSwing.Both)
        {
            v.Error(nameof(Swing), "a sliding door slides on one face: left or right, not both");
        }

        if (Sliding && Double)
        {
            v.Error(nameof(Double), "sliding doors are single leaves");
        }
    }
}

/// <summary>kit/doors.jsonc: the kinds of door leaf.</summary>
public sealed class DoorsDef : IValidatable
{
    public DoorKindDef[] Doors { get; set; } = Array.Empty<DoorKindDef>();

    public void Validate(Validator v)
    {
        LevelDefChecks.UniqueIds(v, nameof(Doors), Doors, d => d.Id);
        LevelDefChecks.Items(v, nameof(Doors), Doors);
    }
}

/// <summary>One kind of door leaf: what it's made of, how thick, how fast it moves, how far it swings and how loud it is.</summary>
public sealed class DoorKindDef : IValidatable
{
    public string Id { get; set; } = "";

    public string Material { get; set; } = "";

    public float Thickness_m { get; set; }

    public float OpenTime_s { get; set; }

    public float CloseTime_s { get; set; }

    public float Swing_deg { get; set; }

    /// <summary>How far opening or shutting it carries (bots hear it within this, halved through walls).</summary>
    public float Noise_m { get; set; }

    /// <summary>How the game draws it (presentation only): panel, flush, steel, cold_room or swing.</summary>
    public string Style { get; set; } = "";

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.NotEmpty(nameof(Material), Material);
        v.NotEmpty(nameof(Style), Style);
        v.InRange(nameof(Thickness_m), Thickness_m, 0.01, 0.4);
        v.InRange(nameof(OpenTime_s), OpenTime_s, 0.1, 10);
        v.InRange(nameof(CloseTime_s), CloseTime_s, 0.1, 10);
        v.InRange(nameof(Swing_deg), Swing_deg, 30, 180);
        v.InRange(nameof(Noise_m), Noise_m, 0, 100);
    }
}

/// <summary>A horizontal slab (upper floor, mezzanine, landing) with optional rectangular holes.</summary>
public sealed class SlabDef : IValidatable
{
    /// <summary>Elevation of the slab's top surface.</summary>
    public float Elevation_m { get; set; }

    public float[] Rect_m { get; set; } = Array.Empty<float>();

    [Optional]
    public float[][]? Holes_m { get; set; }

    [Optional]
    public float Thickness_m { get; set; } = float.NaN;

    [Optional]
    public string? Material { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Elevation_m), Elevation_m, -10, 200);
        LevelDefChecks.Rect(v, nameof(Rect_m), Rect_m);
        LevelDefChecks.Rects(v, nameof(Holes_m), Holes_m);
        if (!float.IsNaN(Thickness_m))
        {
            v.InRange(nameof(Thickness_m), Thickness_m, 0.02, 2);
        }
    }
}

/// <summary>Flat roof on top of the last storey, with holes (collapsed sections) and an optional parapet.</summary>
/// <summary>
/// Trusses spanning the roof's short way, this far apart, this deep, with purlins along the long way
/// under the roof. Over a hole in the roof they're broken off and hang down.
/// </summary>
public sealed class TrussesDef : IValidatable
{
    public string Material { get; set; } = "";

    public float Spacing_m { get; set; }

    public float Depth_m { get; set; }

    /// <summary>Pendant lamps hanging from each truss on cables (0 for none).</summary>
    [Optional]
    public int LampsPerTruss { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Material), Material);
        v.InRange(nameof(Spacing_m), Spacing_m, 1, 20);
        v.InRange(nameof(Depth_m), Depth_m, 0.2, 3);
        v.InRange(nameof(LampsPerTruss), LampsPerTruss, 0, 20);
    }
}

/// <summary>
/// Worn paint on a building (presentation only), in plan metres in its frame: lines and hatched areas
/// on the ground floor, numbers stencilled on a floor or a wall, and stripes round the foot of the
/// ground floor's columns, all in this colour unless a stencil gives its own.
/// </summary>
public sealed class MarkingsDef : IValidatable
{
    /// <summary>The paint's colour, as #rrggbb.</summary>
    public string Color { get; set; } = "";

    public float LineWidth_m { get; set; }

    /// <summary>Lines on the ground floor: [x0, z0, x1, z1] each.</summary>
    [Optional]
    public float[][]? Lines_m { get; set; }

    /// <summary>Areas on the ground floor hatched with diagonal stripes: [x0, z0, x1, z1] each.</summary>
    [Optional]
    public float[][]? Hatches_m { get; set; }

    [Optional]
    public StencilDef[]? Stencils { get; set; }

    /// <summary>How high the stripes go round the foot of the ground floor's columns (0 for none).</summary>
    [Optional]
    public float ColumnStripes_m { get; set; }

    public void Validate(Validator v)
    {
        LevelDefChecks.Colour(v, nameof(Color), Color);
        v.InRange(nameof(LineWidth_m), LineWidth_m, 0.02, 1);
        for (int i = 0; Lines_m is not null && i < Lines_m.Length; i++)
        {
            v.Vector(nameof(Lines_m) + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", Lines_m[i], 4);
        }

        LevelDefChecks.Rects(v, nameof(Hatches_m), Hatches_m);
        LevelDefChecks.Items(v, nameof(Stencils), Stencils);
        v.InRange(nameof(ColumnStripes_m), ColumnStripes_m, 0, 5);
    }
}

/// <summary>
/// Capital letters, digits, spaces and dashes stencilled on a floor or, upright, on a wall: their middle at
/// [x, y, z] (y is the height of the floor or of the middle on the wall), each this tall. Yaw turns them as anything in the plan: at
/// 0 they read from the +Z side, a wall's at 0 facing +Z.
/// </summary>
public sealed class StencilDef : IValidatable
{
    public string Text { get; set; } = "";

    public float[] At_m { get; set; } = Array.Empty<float>();

    public float Size_m { get; set; }

    [Optional]
    public float Yaw_deg { get; set; }

    /// <summary>On a wall rather than the floor.</summary>
    [Optional]
    public bool Wall { get; set; }

    /// <summary>A colour of its own, as #rrggbb.</summary>
    [Optional]
    public string? Color { get; set; }

    public void Validate(Validator v)
    {
        if (string.IsNullOrWhiteSpace(Text) || !Text.All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'Z' or ' ' or '-'))
        {
            v.Error(nameof(Text), $"'{Text}' must be capital letters, digits, spaces and dashes");
        }

        v.Vector(nameof(At_m), At_m);
        v.InRange(nameof(Size_m), Size_m, 0.05, 5);
        v.InRange(nameof(Yaw_deg), Yaw_deg, -360, 360);
        if (Color is not null)
        {
            LevelDefChecks.Colour(v, nameof(Color), Color);
        }
    }
}

public sealed class RoofDef : IValidatable
{
    public float[] Rect_m { get; set; } = Array.Empty<float>();

    [Optional]
    public float[][]? Holes_m { get; set; }

    public string Material { get; set; } = "";

    [Optional]
    public float Thickness_m { get; set; } = float.NaN;

    [Optional]
    public float Parapet_m { get; set; }

    [Optional]
    public string? ParapetMaterial { get; set; }

    /// <summary>What the roof looks like from inside; <c>material</c> is then just a skin on top.</summary>
    [Optional]
    public string? CeilingMaterial { get; set; }

    public void Validate(Validator v)
    {
        LevelDefChecks.Rect(v, nameof(Rect_m), Rect_m);
        LevelDefChecks.Rects(v, nameof(Holes_m), Holes_m);
        v.NotEmpty(nameof(Material), Material);
        v.InRange(nameof(Parapet_m), Parapet_m, 0, 5);
        if (!float.IsNaN(Thickness_m))
        {
            v.InRange(nameof(Thickness_m), Thickness_m, 0.02, 2);
        }
    }
}

/// <summary>
/// A straight flight of stairs. <c>start_m</c> is the centre of the bottom step's front edge and the
/// flight climbs toward <c>yaw_deg</c> (0 = north, −Z; positive turns left).
/// </summary>
public sealed class StairDef : IValidatable
{
    public float[] Start_m { get; set; } = Array.Empty<float>();

    public float Yaw_deg { get; set; }

    public float Width_m { get; set; }

    public float Run_m { get; set; }

    public float Rise_m { get; set; }

    public int Steps { get; set; }

    [Optional]
    public float BaseElevation_m { get; set; }

    public string Material { get; set; } = "";

    public void Validate(Validator v)
    {
        v.Vector(nameof(Start_m), Start_m, 2);
        v.InRange(nameof(Yaw_deg), Yaw_deg, -360, 360);
        v.InRange(nameof(Width_m), Width_m, 0.5, 10);
        v.InRange(nameof(Run_m), Run_m, 0.5, 30);
        v.InRange(nameof(Rise_m), Rise_m, 0.2, 20);
        v.InRange(nameof(Steps), Steps, 2, 100);
        v.InRange(nameof(BaseElevation_m), BaseElevation_m, -10, 200);
        v.NotEmpty(nameof(Material), Material);
        if (Run_m > 0 && Rise_m / Run_m > 1.0f)
        {
            v.Error(nameof(Rise_m), "stairs steeper than 45° can't be walked");
        }
    }
}

public sealed class ColumnDef : IValidatable
{
    public float[] At_m { get; set; } = Array.Empty<float>();

    public float[] Size_m { get; set; } = Array.Empty<float>();

    [Optional]
    public float BaseElevation_m { get; set; }

    public float Height_m { get; set; }

    public string Material { get; set; } = "";

    public void Validate(Validator v)
    {
        v.Vector(nameof(At_m), At_m, 2);
        LevelDefChecks.PositiveVector(v, nameof(Size_m), Size_m, 2);
        v.InRange(nameof(BaseElevation_m), BaseElevation_m, -10, 200);
        v.InRange(nameof(Height_m), Height_m, 0.05, 100);
        v.NotEmpty(nameof(Material), Material);
    }
}

public sealed class PropPlacementDef : IValidatable
{
    public string Prop { get; set; } = "";

    /// <summary>[x, y, z]: y lifts the prop (e.g. onto a mezzanine or another prop).</summary>
    public float[] Position_m { get; set; } = Array.Empty<float>();

    [Optional]
    public float Yaw_deg { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Prop), Prop);
        v.Vector(nameof(Position_m), Position_m);
        v.InRange(nameof(Yaw_deg), Yaw_deg, -360, 360);
    }
}

/// <summary>A named region: callouts ("office, upstairs"), bot searches, and how well lit it is.</summary>
public sealed class AreaDef : IValidatable
{
    public string Name { get; set; } = "";

    public float[] Rect_m { get; set; } = Array.Empty<float>();

    [Optional]
    public float MinY_m { get; set; } = -1f;

    [Optional]
    public float MaxY_m { get; set; } = 30f;

    [Optional]
    public bool Indoor { get; set; }

    /// <summary>0 = dark, 1 = open daylight. Darker areas take bots longer to spot you in.</summary>
    [Optional]
    public float Light { get; set; } = 1f;

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Name), Name);
        LevelDefChecks.Rect(v, nameof(Rect_m), Rect_m);
        v.InRange(nameof(MaxY_m), MaxY_m, MinY_m, 500);
        v.InRange(nameof(Light), Light, 0, 1);
    }
}

/// <summary>levels/*.jsonc: one explorable level.</summary>
/// <summary>
/// Things out beyond a level, drawn by the game for the view (presentation only; nothing out there
/// can be reached or hit): power lines on lattice pylons and lines of wooden telegraph poles.
/// </summary>
public sealed class SceneryDef : IValidatable
{
    [Optional]
    public LineDef[]? PowerLines { get; set; }

    [Optional]
    public LineDef[]? PoleLines { get; set; }

    public void Validate(Validator v)
    {
        LevelDefChecks.Items(v, nameof(PowerLines), PowerLines);
        LevelDefChecks.Items(v, nameof(PoleLines), PoleLines);
    }
}

/// <summary>A line of towers or poles along plan points [x, z], this far apart, this tall, with wires between them.</summary>
public sealed class LineDef : IValidatable
{
    public float[][] Points_m { get; set; } = Array.Empty<float[]>();

    public float Spacing_m { get; set; }

    public float Height_m { get; set; }

    /// <summary>The towers' or poles' material, and the wires'.</summary>
    public string Material { get; set; } = "";

    public string Wire { get; set; } = "";

    public void Validate(Validator v)
    {
        if (Points_m.Length < 2)
        {
            v.Error(nameof(Points_m), "needs at least two points");
        }

        for (int i = 0; i < Points_m.Length; i++)
        {
            v.Vector(nameof(Points_m) + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", Points_m[i], 2);
        }

        v.InRange(nameof(Spacing_m), Spacing_m, 5, 1000);
        v.InRange(nameof(Height_m), Height_m, 2, 200);
        v.NotEmpty(nameof(Material), Material);
        v.NotEmpty(nameof(Wire), Wire);
    }
}

public sealed class LevelDef : IValidatable
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>Paint leaving this box is removed, and walking stops at its sides.</summary>
    public float[] BoundsMin_m { get; set; } = Array.Empty<float>();

    public float[] BoundsMax_m { get; set; } = Array.Empty<float>();

    public string GroundMaterial { get; set; } = "";

    [Optional]
    public GroundPatchDef[]? GroundPatches { get; set; }

    /// <summary>Where you come in: one at random each round (scripted runs use the first).</summary>
    public SpawnDef[] PlayerSpawns { get; set; } = Array.Empty<SpawnDef>();

    /// <summary>The area (by name) inside which opponents dealt random starts begin.</summary>
    public string SpawnArea { get; set; } = "";

    /// <summary>Where eliminated opponents walk to before leaving the level.</summary>
    public float[] DeadZone_m { get; set; } = Array.Empty<float>();

    public BuildingPlacementDef[] Buildings { get; set; } = Array.Empty<BuildingPlacementDef>();

    [Optional]
    public WallDef[]? Walls { get; set; }

    [Optional]
    public PropPlacementDef[]? Props { get; set; }

    [Optional]
    public AreaDef[]? Areas { get; set; }

    public OpponentSpawnDef[] OpponentSpawns { get; set; } = Array.Empty<OpponentSpawnDef>();

    [Optional]
    public PatrolDef[]? Patrols { get; set; }

    [Optional]
    public PickupDef[]? Pickups { get; set; }

    /// <summary>Named camera positions: the briefing fly-over and screenshots.</summary>
    [Optional]
    public ViewpointDef[]? Viewpoints { get; set; }

    /// <summary>What the game draws out beyond the level (presentation only).</summary>
    [Optional]
    public SceneryDef? Scenery { get; set; }

    /// <summary>Paint on the open ground, in world [x, z]: lines, hatched areas, stencilled numbers (presentation only).</summary>
    [Optional]
    public MarkingsDef? Markings { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.NotEmpty(nameof(DisplayName), DisplayName);
        LevelDefChecks.Items(v, nameof(Viewpoints), Viewpoints);
        Scenery?.Validate(v.Scope(nameof(Scenery)));
        Markings?.Validate(v.Scope(nameof(Markings)));
        v.Vector(nameof(BoundsMin_m), BoundsMin_m);
        v.Vector(nameof(BoundsMax_m), BoundsMax_m);
        if (BoundsMin_m is { Length: 3 } && BoundsMax_m is { Length: 3 } &&
            (BoundsMax_m[0] <= BoundsMin_m[0] || BoundsMax_m[1] <= BoundsMin_m[1] || BoundsMax_m[2] <= BoundsMin_m[2]))
        {
            v.Error(nameof(BoundsMax_m), "must be greater than boundsMin_m on every axis");
        }

        v.NotEmpty(nameof(GroundMaterial), GroundMaterial);
        LevelDefChecks.Items(v, nameof(GroundPatches), GroundPatches);
        if (PlayerSpawns.Length == 0)
        {
            v.Error(nameof(PlayerSpawns), "needs at least one spawn");
        }

        for (int i = 0; i < PlayerSpawns.Length; i++)
        {
            PlayerSpawns[i].Validate(v.Item(nameof(PlayerSpawns), i));
        }

        v.NotEmpty(nameof(SpawnArea), SpawnArea);
        if (SpawnArea.Length > 0 && Areas?.Any(a => a.Name == SpawnArea) != true)
        {
            v.Error(nameof(SpawnArea), $"names no area of this level ('{SpawnArea}')");
        }
        v.Vector(nameof(DeadZone_m), DeadZone_m);
        for (int i = 0; i < Buildings.Length; i++)
        {
            Buildings[i].Validate(v.Item(nameof(Buildings), i));
        }

        for (int i = 0; Walls is not null && i < Walls.Length; i++)
        {
            Validator item = v.Item(nameof(Walls), i);
            Walls[i].Validate(item);
            if (float.IsNaN(Walls[i].BaseElevation_m))
            {
                item.Error(nameof(WallDef.BaseElevation_m), "is required for level walls");
            }

            if (float.IsNaN(Walls[i].Height_m))
            {
                item.Error(nameof(WallDef.Height_m), "is required for level walls");
            }
        }

        LevelDefChecks.Items(v, nameof(Props), Props);
        LevelDefChecks.Items(v, nameof(Areas), Areas);
        LevelDefChecks.UniqueIds(v, nameof(OpponentSpawns), OpponentSpawns, s => s.Id);
        if (Patrols is not null)
        {
            LevelDefChecks.UniqueIds(v, nameof(Patrols), Patrols, p => p.Id);
        }

        if (Pickups is not null)
        {
            LevelDefChecks.UniqueIds(v, nameof(Pickups), Pickups, p => p.Id);
        }
    }
}

/// <summary>A patch of different ground (dirt, gravel, a concrete pad): drawn and hit by paint, not walked on separately.</summary>
public sealed class GroundPatchDef : IValidatable
{
    public float[] Rect_m { get; set; } = Array.Empty<float>();

    public string Material { get; set; } = "";

    public void Validate(Validator v)
    {
        LevelDefChecks.Rect(v, nameof(Rect_m), Rect_m);
        v.NotEmpty(nameof(Material), Material);
    }
}

public sealed class BuildingPlacementDef : IValidatable
{
    public string Building { get; set; } = "";

    /// <summary>World position of the building's local origin [x, y, z].</summary>
    public float[] Position_m { get; set; } = Array.Empty<float>();

    [Optional]
    public float Yaw_deg { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Building), Building);
        v.Vector(nameof(Position_m), Position_m);
        v.InRange(nameof(Yaw_deg), Yaw_deg, -360, 360);
    }
}

/// <summary>A place an opponent can start. The ladder decides how many spawns are used per difficulty.</summary>
public sealed class OpponentSpawnDef : IValidatable
{
    public string Id { get; set; } = "";

    public float[] Position_m { get; set; } = Array.Empty<float>();

    [Optional]
    public float Yaw_deg { get; set; }

    /// <summary>Behaviours that may start here (sentry, patroller, rusher).</summary>
    public string[] Roles { get; set; } = Array.Empty<string>();

    [Optional]
    public string? Patrol { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.Vector(nameof(Position_m), Position_m);
        v.InRange(nameof(Yaw_deg), Yaw_deg, -360, 360);
        if (Roles.Length == 0)
        {
            v.Error(nameof(Roles), "must list at least one role");
        }
    }
}

public sealed class PatrolDef : IValidatable
{
    public string Id { get; set; } = "";

    /// <summary>Waypoints [x, y, z]; y is the floor the point is on.</summary>
    public float[][] Points_m { get; set; } = Array.Empty<float[]>();

    [Optional]
    public bool Loop { get; set; } = true;

    /// <summary>How long a patroller lingers at each waypoint.</summary>
    [Optional]
    public float Pause_s { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        if (Points_m.Length < 2)
        {
            v.Error(nameof(Points_m), "needs at least two points");
        }

        for (int i = 0; i < Points_m.Length; i++)
        {
            v.Vector(nameof(Points_m) + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", Points_m[i]);
        }

        v.InRange(nameof(Pause_s), Pause_s, 0, 120);
    }
}

public sealed class ViewpointDef : IValidatable
{
    public string Name { get; set; } = "";

    public float[] Position_m { get; set; } = Array.Empty<float>();

    public float Yaw_deg { get; set; }

    public float Pitch_deg { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Name), Name);
        v.Vector(nameof(Position_m), Position_m);
        v.InRange(nameof(Yaw_deg), Yaw_deg, -360, 360);
        v.InRange(nameof(Pitch_deg), Pitch_deg, -89, 89);
    }
}

public enum PickupKind
{
    Pod,
    Air,
}

public sealed class PickupDef : IValidatable
{
    public string Id { get; set; } = "";

    public PickupKind Kind { get; set; }

    public float[] Position_m { get; set; } = Array.Empty<float>();

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.Vector(nameof(Position_m), Position_m);
    }
}

/// <summary>levels/ladder.jsonc: the levels in play order. Locked entries are announced but not playable yet.</summary>
public sealed class LadderDef : IValidatable
{
    public LadderLevelDef[] Levels { get; set; } = Array.Empty<LadderLevelDef>();

    /// <summary>How later levels open (the first is always open).</summary>
    [Optional]
    public UnlockDef Unlock { get; set; } = new();

    /// <summary>What level select's records count.</summary>
    [Optional]
    public RecordsDef Records { get; set; } = new();

    public void Validate(Validator v)
    {
        if (Levels.Length == 0)
        {
            v.Error(nameof(Levels), "must list at least one level");
        }

        LevelDefChecks.UniqueIds(v, nameof(Levels), Levels, l => l.Id);
        Unlock.Validate(v.Scope(nameof(Unlock)));
        Records.Validate(v.Scope(nameof(Records)));
        for (int i = 0; i < Levels.Length && Unlock.MinTier.Length > 0; i++)
        {
            LadderLevelDef level = Levels[i];
            if (level.Tiers is { Length: > 0 } tiers && Array.FindIndex(tiers, t => t.Id == Unlock.MinTier) < 0)
            {
                v.Scope(nameof(Unlock)).Error(nameof(UnlockDef.MinTier),
                    $"'{Unlock.MinTier}' is not one of {level.Id}'s difficulty tiers ({string.Join(", ", tiers.Select(t => t.Id))})");
            }
        }
    }
}

/// <summary>
/// How the ladder opens: each level after the first opens when a round on the level before it is won (in any
/// mode) on <see cref="MinTier"/> or a harder tier (later in that level's list). Empty: any tier counts.
/// </summary>
public sealed class UnlockDef : IValidatable
{
    [Optional]
    public string MinTier { get; set; } = "";

    public void Validate(Validator v)
    {
    }
}

/// <summary>What level select's records count: accuracy only from rounds with at least <see cref="AccuracyMinShots"/> shots.</summary>
public sealed class RecordsDef : IValidatable
{
    [Optional]
    public int AccuracyMinShots { get; set; } = 10;

    public void Validate(Validator v) => v.InRange(nameof(AccuracyMinShots), AccuracyMinShots, 1, 1000);
}

public sealed class LadderLevelDef : IValidatable
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    /// <summary>The level file; omitted for levels that aren't built yet (shown locked).</summary>
    [Optional]
    public string? File { get; set; }

    [Optional]
    public string? Note { get; set; }

    /// <summary>Difficulty tiers, easiest first (playable levels need at least one).</summary>
    [Optional]
    public LadderTierDef[]? Tiers { get; set; }

    /// <summary>
    /// Opponent spawn ids that scripted solo runs (smoke tests, demos) fill in this order, so they play out
    /// the same every time; real rounds deal random starts instead.
    /// </summary>
    [Optional]
    public string[]? Roster { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.NotEmpty(nameof(DisplayName), DisplayName);
        if (!string.IsNullOrWhiteSpace(File) && (Tiers is null || Tiers.Length == 0))
        {
            v.Error(nameof(Tiers), "a playable level needs at least one difficulty tier");
        }

        if (!string.IsNullOrWhiteSpace(File) && (Roster is null || Roster.Length == 0))
        {
            v.Error(nameof(Roster), "a playable level needs a roster for scripted runs");
        }

        if (Roster is not null && Roster.Distinct(StringComparer.Ordinal).Count() != Roster.Length)
        {
            v.Error(nameof(Roster), "lists a spawn twice");
        }

        if (Tiers is not null)
        {
            LevelDefChecks.UniqueIds(v, nameof(Tiers), Tiers, t => t.Id);
        }
    }
}

/// <summary>
/// One difficulty tier of a level: how good the bots are, how long the round lasts and what everyone
/// carries. Never how many there are: the mode and size are picked separately.
/// </summary>
public sealed class LadderTierDef : IValidatable
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public float TimeLimit_s { get; set; }

    public int StartPods { get; set; }

    /// <summary>Spare pods every bot starts with, teammates and opponents alike.</summary>
    public int BotPods { get; set; }

    public bool Pickups { get; set; }

    /// <summary>Which bot difficulty tier (bots/difficulty.jsonc) every bot plays at.</summary>
    public string Bots { get; set; } = "";

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.NotEmpty(nameof(DisplayName), DisplayName);
        v.NotEmpty(nameof(Bots), Bots);
        v.InRange(nameof(TimeLimit_s), TimeLimit_s, 30, 7200);
        v.InRange(nameof(StartPods), StartPods, 0, 10);
        v.InRange(nameof(BotPods), BotPods, 0, 10);
    }
}

internal static class LevelDefChecks
{
    public static void UniqueIds<T>(Validator v, string property, T[] items, Func<T, string> id)
        where T : IValidatable
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < items.Length; i++)
        {
            Validator item = v.Item(property, i);
            items[i].Validate(item);
            string key = id(items[i]);
            if (!string.IsNullOrEmpty(key) && !seen.Add(key))
            {
                item.Error("id", $"duplicate id '{key}'");
            }
        }
    }

    public static void Items<T>(Validator v, string property, T[]? items)
        where T : IValidatable
    {
        for (int i = 0; items is not null && i < items.Length; i++)
        {
            items[i].Validate(v.Item(property, i));
        }
    }

    /// <summary>A colour written #rrggbb.</summary>
    public static void Colour(Validator v, string property, string? value)
    {
        if (value is not { Length: 7 } || value[0] != '#' || !value.Skip(1).All(char.IsAsciiHexDigit))
        {
            v.Error(property, $"'{value}' must be a colour written #rrggbb");
        }
    }

    public static void Rect(Validator v, string property, float[]? rect)
    {
        v.Vector(property, rect, 4);
        if (rect is { Length: 4 } && (rect[2] <= rect[0] || rect[3] <= rect[1]))
        {
            v.Error(property, "must be [x0, z0, x1, z1] with x1 > x0 and z1 > z0");
        }
    }

    public static void Rects(Validator v, string property, float[][]? rects)
    {
        for (int i = 0; rects is not null && i < rects.Length; i++)
        {
            Rect(v, property + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", rects[i]);
        }
    }

    public static void PositiveVector(Validator v, string property, float[]? value, int length)
    {
        v.Vector(property, value, length);
        if (value is not null && value.Length == length && value.Any(f => f <= 0f))
        {
            v.Error(property, "all sizes must be > 0");
        }
    }

    public static void HexColor(Validator v, string property, string value)
    {
        bool ok = value.Length is 7 or 9 && value[0] == '#' &&
                  value.AsSpan(1).ToString().All(Uri.IsHexDigit);
        if (!ok)
        {
            v.Error(property, $"must be a colour like \"#8a8a85\" (got \"{value}\")");
        }
    }
}

#pragma warning restore CA1707
