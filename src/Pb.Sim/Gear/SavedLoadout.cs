namespace Pb.Sim.Gear;

/// <summary>
/// A loadout as your profile keeps it (Phase 5, profile format 3): your character and, for each slot, the item's id and
/// its three colours as <c>#rrggbb</c>, so it still reads after the catalogue changes (an item that's gone reads as its
/// slot's default, <see cref="GearCatalog.Read"/>).
/// </summary>
public sealed class SavedLoadout
{
    public int Character { get; set; }

    public SavedPick? Marker { get; set; }

    public SavedPick? Loader { get; set; }

    public SavedPick? Tank { get; set; }

    public SavedPick? Mask { get; set; }

    public SavedPick? Jersey { get; set; }

    public SavedPick? Pants { get; set; }

    public SavedPick? Get(GearSlot slot) => slot switch
    {
        GearSlot.Marker => Marker,
        GearSlot.Loader => Loader,
        GearSlot.Tank => Tank,
        GearSlot.Mask => Mask,
        GearSlot.Jersey => Jersey,
        _ => Pants,
    };

    public void Set(GearSlot slot, SavedPick? pick)
    {
        switch (slot)
        {
            case GearSlot.Marker:
                Marker = pick;
                break;
            case GearSlot.Loader:
                Loader = pick;
                break;
            case GearSlot.Tank:
                Tank = pick;
                break;
            case GearSlot.Mask:
                Mask = pick;
                break;
            case GearSlot.Jersey:
                Jersey = pick;
                break;
            default:
                Pants = pick;
                break;
        }
    }
}

/// <summary>One slot of a <see cref="SavedLoadout"/>: the item's id and its main, second and accent colours.</summary>
public sealed class SavedPick
{
    public string Item { get; set; } = "";

    public string[] Colours { get; set; } = System.Array.Empty<string>();
}
