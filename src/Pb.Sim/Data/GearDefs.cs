using Pb.Sim.Gear;

namespace Pb.Sim.Data;

#pragma warning disable CA1707 // Identifiers should not contain underscores: the suffix is the unit.

/// <summary>
/// gear/catalog.jsonc: the brands and every item they make for the six slots (Phase 5). Looks only: nothing here
/// changes how anyone plays, so it holds no rules, only what each item is, who makes it and how it's built.
/// </summary>
public sealed class GearCatalogDef : IValidatable
{
    public GearBrandDef[] Brands { get; set; } = Array.Empty<GearBrandDef>();

    public GearItemDef[] Items { get; set; } = Array.Empty<GearItemDef>();

    /// <summary>The item each slot starts with, and falls back to when the one picked is gone.</summary>
    public GearDefaultsDef Defaults { get; set; } = new();

    public void Validate(Validator v)
    {
        if (Brands.Length == 0)
        {
            v.Error(nameof(Brands), "must list at least one brand");
        }

        LevelDefChecks.UniqueIds(v, nameof(Brands), Brands, b => b.Id);
        LevelDefChecks.UniqueIds(v, nameof(Items), Items, i => i.Id);
        for (int i = 0; i < Brands.Length; i++)
        {
            Brands[i].Validate(v.Item(nameof(Brands), i));
        }

        var brands = new HashSet<string>(Brands.Select(b => b.Id), StringComparer.Ordinal);
        for (int i = 0; i < Items.Length; i++)
        {
            Validator item = v.Item(nameof(Items), i);
            Items[i].Validate(item);
            if (!brands.Contains(Items[i].Brand))
            {
                item.Error(nameof(GearItemDef.Brand), $"unknown brand '{Items[i].Brand}' (known: {string.Join(", ", brands)})");
            }
        }

        // Every brand makes something for every slot, so the locker can show a brand's whole kit.
        foreach (GearBrandDef brand in Brands)
        {
            foreach (GearSlot slot in Enum.GetValues<GearSlot>())
            {
                if (!Items.Any(i => i.Brand == brand.Id && i.Slot == slot))
                {
                    v.Error(nameof(Items), $"brand '{brand.Id}' has no {Jsonc.KeyOf(slot.ToString())}");
                }
            }
        }

        Validator defaults = v.Scope(nameof(Defaults));
        foreach (GearSlot slot in Enum.GetValues<GearSlot>())
        {
            string id = Defaults.For(slot);
            GearItemDef? item = Items.FirstOrDefault(i => i.Id == id);
            if (item is null)
            {
                defaults.Error(slot.ToString(), $"unknown item '{id}'");
            }
            else if (item.Slot != slot)
            {
                defaults.Error(slot.ToString(), $"'{id}' is a {Jsonc.KeyOf(item.Slot.ToString())}, not a {Jsonc.KeyOf(slot.ToString())}");
            }
        }
    }
}

/// <summary>A brand of our own: its name, a line about it for the locker, and its name as printed on its kit.</summary>
public sealed class GearBrandDef : IValidatable
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Line { get; set; } = "";

    /// <summary>The name as printed on the brand's kit, in the stencil hand of the yard's markings: A–Z, spaces and dashes.</summary>
    public string Mark { get; set; } = "";

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.NotEmpty(nameof(DisplayName), DisplayName);
        v.NotEmpty(nameof(Mark), Mark);
        if (Mark.Any(c => c is not (>= 'A' and <= 'Z' or ' ' or '-')))
        {
            v.Error(nameof(Mark), $"may only hold A–Z, spaces and dashes (got '{Mark}')");
        }
    }
}

/// <summary>
/// One item: which slot it fills, who makes it, how it's built (<see cref="Shape"/>: the recipe the game builds it from;
/// a jersey's or pants' is its pattern), and the three colours it comes in. An item may instead be a generated
/// <see cref="Model"/>, which can have other slots' items built in (<see cref="Includes"/>).
/// </summary>
public sealed class GearItemDef : IValidatable
{
    public string Id { get; set; } = "";

    public GearSlot Slot { get; set; }

    public string Brand { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Shape { get; set; } = "";

    /// <summary>Main, second and accent colours (#rrggbb): what each paints is up to the item's shape.</summary>
    public string[] Colours { get; set; } = Array.Empty<string>();

    [Optional]
    public string? Model { get; set; }

    /// <summary>Other slots the model has built in: they're drawn with it, whatever's picked for them.</summary>
    [Optional]
    public GearSlot[] Includes { get; set; } = Array.Empty<GearSlot>();

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.NotEmpty(nameof(DisplayName), DisplayName);
        v.NotEmpty(nameof(Shape), Shape);
        if (Colours.Length != 3)
        {
            v.Error(nameof(Colours), "must list three colours: main, second and accent");
        }

        foreach (string colour in Colours)
        {
            if (!GearColours.TryParse(colour, out _))
            {
                v.Error(nameof(Colours), $"'{colour}' isn't a colour (#rrggbb)");
            }
        }

        if (Includes.Contains(Slot))
        {
            v.Error(nameof(Includes), "can't include the item's own slot");
        }

        if (Includes.Length > 0 && Model is null)
        {
            v.Error(nameof(Includes), "only a generated model can have other slots built in");
        }
    }
}

public sealed class GearDefaultsDef
{
    public string Marker { get; set; } = "";

    public string Loader { get; set; } = "";

    public string Tank { get; set; } = "";

    public string Mask { get; set; } = "";

    public string Jersey { get; set; } = "";

    public string Pants { get; set; } = "";

    public string For(GearSlot slot) => slot switch
    {
        GearSlot.Marker => Marker,
        GearSlot.Loader => Loader,
        GearSlot.Tank => Tank,
        GearSlot.Mask => Mask,
        GearSlot.Jersey => Jersey,
        _ => Pants,
    };
}
