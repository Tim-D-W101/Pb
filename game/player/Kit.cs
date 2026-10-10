using Godot;
using Pb.Sim.Gear;

namespace Pb.Game.Player;

/// <summary>
/// What a player wears, as drawn: their loadout from the catalogue, and the colour of the side their jersey and pants
/// take as their main colour (in rounds with sides, and on bots always; null keeps their own). Looks only.
/// </summary>
public sealed class Kit
{
    public Kit(GearCatalog gear, Loadout loadout, Color? side = null)
    {
        Gear = gear;
        Loadout = gear.Normalised(loadout);
        Side = side;
    }

    public GearCatalog Gear { get; }

    public Loadout Loadout { get; }

    public Color? Side { get; }

    /// <summary>The item worn in <paramref name="slot"/> (the slot's default if its shape has no recipe).</summary>
    public GearItem Item(GearSlot slot) => GearModels.ItemFor(Gear, Loadout, slot);

    /// <summary>The colours <paramref name="slot"/> is drawn in: the loadout's, with the side's as the jersey's and pants' main.</summary>
    public GearColours Colours(GearSlot slot)
    {
        GearColours colours = Loadout[slot].Colours;
        return slot is GearSlot.Jersey or GearSlot.Pants && Side is { } side ? colours with { Main = ToUint(side) } : colours;
    }

    /// <summary>
    /// Your kit as the locker saved it in your profile (<paramref name="saved"/>; the field's own before you've been in),
    /// on <paramref name="character"/>.
    /// </summary>
    public static Kit Saved(GearCatalog gear, SavedLoadout? saved, int character, Color? side = null)
    {
        Loadout loadout = gear.Read(saved, character);
        loadout.Character = System.Math.Max(0, character);
        return new Kit(gear, loadout, side);
    }

    /// <summary>The field's own kit on character <paramref name="character"/>.</summary>
    public static Kit Default(GearCatalog gear, int character, Color? side = null) => new(gear, gear.Default(character), side);

    /// <summary>Every slot from <paramref name="brand"/>'s range in its own colours (a dev check, <c>--kit=BRAND</c>), or null for a brand there isn't.</summary>
    public static Loadout? Brand(GearCatalog gear, string brand, int character)
    {
        Loadout loadout = gear.Default(character);
        bool any = false;
        foreach (GearSlot slot in System.Enum.GetValues<GearSlot>())
        {
            foreach (int item in gear.ItemsIn(slot))
            {
                if (gear.Items[item].Brand.Id == brand)
                {
                    loadout[slot] = new GearChoice(item, gear.Items[item].Colours);
                    any = true;
                    break;
                }
            }
        }

        return any ? loadout : null;
    }

    public static uint ToUint(Color colour) =>
        ((uint)Mathf.RoundToInt(Mathf.Clamp(colour.R, 0f, 1f) * 255f) << 16) | ((uint)Mathf.RoundToInt(Mathf.Clamp(colour.G, 0f, 1f) * 255f) << 8) |
        (uint)Mathf.RoundToInt(Mathf.Clamp(colour.B, 0f, 1f) * 255f);
}
