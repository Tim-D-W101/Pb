using System.Collections.Generic;
using System.Linq;
using Pb.Game.World;
using Pb.Sim.Gear;

namespace Pb.Game.Player;

/// <summary>
/// Every gear item's recipe, by its slot and the catalogue's <c>shape</c> (gear/catalog.jsonc): the markers, loaders,
/// tanks and mask shells built in code. Jerseys and pants are patterns (<see cref="ClothesPatterns"/>), drawn by the
/// characters' shader rather than built.
/// </summary>
public static class GearRecipes
{
    /// <summary>The shapes each slot knows.</summary>
    public static IReadOnlyList<string> Shapes(GearSlot slot) => slot switch
    {
        GearSlot.Marker => MarkerRecipes.Shapes,
        GearSlot.Loader => LoaderRecipes.Shapes,
        GearSlot.Tank => TankRecipes.Shapes,
        GearSlot.Mask => MaskRecipes.Shapes,
        _ => ClothesPatterns.Names,
    };

    public static bool Has(GearSlot slot, string shape) => Shapes(slot).Contains(shape);

    /// <summary>
    /// The items whose shape no recipe knows, as "id (slot 'shape')", for the smoke test and the log (an item without
    /// one draws as its slot's default).
    /// </summary>
    public static List<string> Unknown(GearCatalog catalog) =>
        catalog.Items.Where(i => !Has(i.Slot, i.Shape)).Select(i => $"{i.Id} ({i.Slot.ToString().ToLowerInvariant()} '{i.Shape}')").ToList();

    /// <summary>
    /// Builds a marker, loader or tank into <paramref name="mesh"/>, unplaced, in its own frame. <paramref name="paint"/>
    /// builds the paint into a see-through loader.
    /// </summary>
    public static GearBuild Build(GearItem item, ShapeMesh mesh, int seed, bool paint) => item.Slot switch
    {
        GearSlot.Marker => MarkerRecipes.Build(item.Shape, mesh, item.Brand),
        GearSlot.Loader => LoaderRecipes.Build(item.Shape, mesh, item.Brand, seed, paint),
        _ => TankRecipes.Build(item.Shape, mesh, item.Brand),
    };
}
