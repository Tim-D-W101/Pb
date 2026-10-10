using Godot;

namespace Pb.Game.Player;

/// <summary>Which of the wearer's colours paints a part of an item, or none (its finish has a colour of its own).</summary>
public enum GearPaint
{
    Main,
    Second,
    Accent,
    /// <summary>The finish's own colour: black rubber, steel, the smoked lens.</summary>
    None,
    /// <summary>The paint in the loader, in the player's paint colour.</summary>
    Ball,
}

/// <summary>What a part of an item is made of: how rough, how metallic, and any fine pattern on it.</summary>
public enum GearFinish
{
    /// <summary>Anodised aluminium in a colour: receivers, barrels.</summary>
    Anodised,
    /// <summary>Bead-blasted, matt anodising: workshop and woodland kit.</summary>
    Matt,
    /// <summary>Polished anodising, nearly a mirror: tournament kit.</summary>
    Polished,
    /// <summary>Matt moulded plastic: loader shells, masks.</summary>
    Plastic,
    /// <summary>Glossy plastic or paint: trim, lids, a lens frame.</summary>
    Gloss,
    /// <summary>Woven cloth: covers, sleeves, straps.</summary>
    Fabric,
    /// <summary>Black rubber: grips, feet.</summary>
    Rubber,
    /// <summary>Black rubber with a diamond knurl pressed into it.</summary>
    Knurl,
    /// <summary>Bare metal in a colour: bottles, regulators.</summary>
    Metal,
    /// <summary>Blued, nearly black steel: ports, screws, rails.</summary>
    DarkSteel,
    /// <summary>Bright steel: bolts, triggers, levers.</summary>
    Steel,
    /// <summary>Carbon fibre in a colour, its twill showing.</summary>
    Carbon,
    /// <summary>A mask's smoked lens.</summary>
    Lens,
    /// <summary>A see-through loader shell, tinted by its colour.</summary>
    Shell,
    /// <summary>A brand's mark, printed: its letters from the marks' atlas (<see cref="BrandMarks"/>), in a colour.</summary>
    Mark,
}

/// <summary>
/// The surfaces of a gear item's mesh (<see cref="GearModels"/>): each is a finish painted by one of the
/// wearer's colours (or its own), so a mesh is built once per item and coloured per wearer. The id is
/// what the recipes hand <see cref="World.ShapeMesh"/> as a material.
/// </summary>
public static class GearZone
{
    private const int Paints = 8;

    public static int Of(GearFinish finish, GearPaint paint = GearPaint.None) => (int)finish * Paints + (int)paint;

    public static GearFinish FinishOf(int zone) => (GearFinish)(zone / Paints);

    public static GearPaint PaintOf(int zone) => (GearPaint)(zone % Paints);

    // The parts most recipes are made of.
    public static readonly int Main = Of(GearFinish.Anodised, GearPaint.Main), Second = Of(GearFinish.Anodised, GearPaint.Second),
        Accent = Of(GearFinish.Anodised, GearPaint.Accent), Rubber = Of(GearFinish.Rubber), Knurl = Of(GearFinish.Knurl),
        DarkSteel = Of(GearFinish.DarkSteel), Steel = Of(GearFinish.Steel), Lens = Of(GearFinish.Lens), Ball = Of(GearFinish.Plastic, GearPaint.Ball);

    /// <summary>How a finish looks: its own colour (when it paints none of the wearer's), roughness, metallic, specular, pattern.</summary>
    public static (Color Own, float Roughness, float Metallic, float Specular, int Pattern) Look(GearFinish finish) => finish switch
    {
        GearFinish.Anodised => (Colors.White, 0.42f, 0.45f, 0.5f, 0),
        GearFinish.Matt => (Colors.White, 0.62f, 0.35f, 0.45f, 0),
        GearFinish.Polished => (Colors.White, 0.24f, 0.55f, 0.55f, 0),
        GearFinish.Plastic => (Colors.White, 0.55f, 0f, 0.5f, 0),
        GearFinish.Gloss => (Colors.White, 0.2f, 0f, 0.6f, 0),
        GearFinish.Fabric => (Colors.White, 0.95f, 0f, 0.3f, 2),
        GearFinish.Rubber => (new Color(0.094f, 0.098f, 0.106f), 0.86f, 0f, 0.4f, 0),
        GearFinish.Knurl => (new Color(0.094f, 0.098f, 0.106f), 0.8f, 0f, 0.4f, 1),
        GearFinish.Metal => (Colors.White, 0.28f, 0.9f, 0.5f, 0),
        GearFinish.DarkSteel => (new Color(0.165f, 0.169f, 0.18f), 0.4f, 0.75f, 0.5f, 0),
        GearFinish.Steel => (new Color(0.66f, 0.68f, 0.7f), 0.3f, 0.9f, 0.5f, 0),
        GearFinish.Carbon => (Colors.White, 0.25f, 0.1f, 0.6f, 3),
        GearFinish.Lens => (new Color(0.043f, 0.051f, 0.063f), 0.05f, 0.2f, 0.8f, 0),
        GearFinish.Shell => (Colors.White, 0.1f, 0f, 0.6f, 0),
        _ => (Colors.White, 0.6f, 0f, 0.5f, 0),
    };
}
