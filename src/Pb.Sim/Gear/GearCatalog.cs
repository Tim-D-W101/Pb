using Pb.Sim.Core;
using Pb.Sim.Data;

namespace Pb.Sim.Gear;

/// <summary>A brand of our own (gear/catalog.jsonc).</summary>
public sealed class GearBrand
{
    public required int Index { get; init; }

    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string Line { get; init; }

    /// <summary>Its name as printed on its kit (A–Z, spaces, dashes).</summary>
    public required string Mark { get; init; }
}

/// <summary>One item in the catalogue: its slot, brand, how it's built and the colours it comes in.</summary>
public sealed class GearItem
{
    public required int Index { get; init; }

    public required string Id { get; init; }

    public required GearSlot Slot { get; init; }

    public required GearBrand Brand { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>The recipe the game builds it from (for a jersey or pants, its pattern).</summary>
    public required string Shape { get; init; }

    /// <summary>A generated model drawn instead of the shape when it loads.</summary>
    public string? Model { get; init; }

    public required GearColours Colours { get; init; }

    /// <summary>Other slots the model has built in.</summary>
    public required IReadOnlyList<GearSlot> Includes { get; init; }
}

/// <summary>
/// The gear locker's catalogue: the brands and every item they make, with the default kit. Looks only: the sim never
/// reads it; GameData loads it with everything else so it's checked like everything else, and so the game, the locker
/// and the network share one list (copies only play together with the same data, so an item's index means the same on
/// every copy).
/// </summary>
public sealed class GearCatalog
{
    private readonly List<int>[] _bySlot;
    private readonly int[] _defaults;
    private readonly Dictionary<string, int> _byId;

    private GearCatalog(IReadOnlyList<GearBrand> brands, IReadOnlyList<GearItem> items, int[] defaults)
    {
        Brands = brands;
        Items = items;
        _defaults = defaults;
        _byId = items.ToDictionary(i => i.Id, i => i.Index, StringComparer.Ordinal);
        _bySlot = new List<int>[Loadout.SlotCount];
        for (int s = 0; s < _bySlot.Length; s++)
        {
            _bySlot[s] = new List<int>();
        }

        foreach (GearItem item in items)
        {
            _bySlot[(int)item.Slot].Add(item.Index);
        }
    }

    public IReadOnlyList<GearBrand> Brands { get; }

    public IReadOnlyList<GearItem> Items { get; }

    /// <summary>The items for <paramref name="slot"/>, in the catalogue's order (indices into <see cref="Items"/>).</summary>
    public IReadOnlyList<int> ItemsIn(GearSlot slot) => _bySlot[(int)slot];

    public int DefaultItem(GearSlot slot) => _defaults[(int)slot];

    /// <summary>The item with <paramref name="id"/>, or −1.</summary>
    public int IndexOf(string id) => _byId.TryGetValue(id, out int index) ? index : -1;

    /// <summary>Whether <paramref name="item"/> is one of <paramref name="slot"/>'s items.</summary>
    public bool Fits(GearSlot slot, int item) => item >= 0 && item < Items.Count && Items[item].Slot == slot;

    /// <summary>The default kit on <paramref name="character"/>: each slot's default item in its own colours.</summary>
    public Loadout Default(int character)
    {
        var loadout = new Loadout { Character = character };
        foreach (GearSlot slot in Enum.GetValues<GearSlot>())
        {
            int item = DefaultItem(slot);
            loadout[slot] = new GearChoice(item, Items[item].Colours);
        }

        return loadout;
    }

    /// <summary>A copy of <paramref name="loadout"/> with any pick that isn't one of its slot's items replaced by the slot's default.</summary>
    public Loadout Normalised(Loadout loadout)
    {
        Loadout copy = loadout.Copy();
        foreach (GearSlot slot in Enum.GetValues<GearSlot>())
        {
            if (!Fits(slot, copy[slot].Item))
            {
                int item = DefaultItem(slot);
                copy[slot] = new GearChoice(item, Items[item].Colours);
            }
        }

        return copy;
    }

    /// <summary>Whether <paramref name="slot"/> comes built into another slot's item in <paramref name="loadout"/> (a generated marker's loader and tank).</summary>
    public bool Included(Loadout loadout, GearSlot slot)
    {
        foreach (GearSlot other in Enum.GetValues<GearSlot>())
        {
            if (other != slot && Fits(other, loadout[other].Item) && Items[loadout[other].Item].Includes.Contains(slot))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A bot's kit, the same on every copy: an item of each slot drawn from the match seed and the player's id, in the
    /// item's own colours. It has a random stream of its own, so dealing kit moves no other.
    /// </summary>
    public Loadout Deal(ulong matchSeed, int playerId, int character)
    {
        var random = new Pcg32(matchSeed ^ 0x9E3779B97F4A7C15UL, 0x67656172UL + (ulong)playerId * 2UL);
        var loadout = new Loadout { Character = character };
        foreach (GearSlot slot in Enum.GetValues<GearSlot>())
        {
            IReadOnlyList<int> items = ItemsIn(slot);
            int item = items[(int)(random.NextUInt() % (uint)items.Count)];
            loadout[slot] = new GearChoice(item, Items[item].Colours);
        }

        return loadout;
    }

    public static GearCatalog From(GearCatalogDef def)
    {
        var brands = def.Brands.Select((b, i) => new GearBrand { Index = i, Id = b.Id, DisplayName = b.DisplayName, Line = b.Line, Mark = b.Mark }).ToList();
        var brandById = brands.ToDictionary(b => b.Id, StringComparer.Ordinal);
        var items = new List<GearItem>(def.Items.Length);
        foreach (GearItemDef d in def.Items)
        {
            GearColours.TryParse(d.Colours[0], out uint main);
            GearColours.TryParse(d.Colours[1], out uint second);
            GearColours.TryParse(d.Colours[2], out uint accent);
            items.Add(new GearItem
            {
                Index = items.Count,
                Id = d.Id,
                Slot = d.Slot,
                Brand = brandById[d.Brand],
                DisplayName = d.DisplayName,
                Shape = d.Shape,
                Model = d.Model,
                Colours = new GearColours(main, second, accent),
                Includes = d.Includes,
            });
        }

        var defaults = new int[Loadout.SlotCount];
        foreach (GearSlot slot in Enum.GetValues<GearSlot>())
        {
            defaults[(int)slot] = items.First(i => i.Id == def.Defaults.For(slot)).Index;
        }

        return new GearCatalog(brands, items, defaults);
    }
}
