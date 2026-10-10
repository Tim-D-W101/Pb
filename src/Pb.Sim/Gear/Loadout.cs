using System.Globalization;

namespace Pb.Sim.Gear;

/// <summary>The six things a player wears and carries, each picked in the gear locker.</summary>
public enum GearSlot
{
    Marker,
    Loader,
    Tank,
    Mask,
    Jersey,
    Pants,
}

/// <summary>An item's three colours (0xRRGGBB each): what each one paints is up to the item.</summary>
public readonly record struct GearColours(uint Main, uint Second, uint Accent)
{
    /// <summary>"#rrggbb" (or "rrggbb") → 0xRRGGBB.</summary>
    public static bool TryParse(string? text, out uint colour)
    {
        colour = 0;
        if (text is null)
        {
            return false;
        }

        string hex = text.StartsWith('#') ? text[1..] : text;
        return hex.Length == 6 && uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out colour);
    }

    public static string Format(uint colour) => "#" + (colour & 0xFFFFFF).ToString("x6", CultureInfo.InvariantCulture);

    public uint this[int index] => index switch
    {
        0 => Main,
        1 => Second,
        _ => Accent,
    };

    public GearColours With(int index, uint colour) => index switch
    {
        0 => this with { Main = colour },
        1 => this with { Second = colour },
        _ => this with { Accent = colour },
    };
}

/// <summary>One slot's pick: the item (its index in <see cref="GearCatalog.Items"/>) and its colours.</summary>
public readonly record struct GearChoice(int Item, GearColours Colours);

/// <summary>
/// What a player wears: their character (one of the three, by index) and a pick for each of the six slots. Looks only:
/// the sim never reads it. Indices are into the catalogue every copy of the game loads from the same data.
/// </summary>
public sealed class Loadout
{
    public static readonly int SlotCount = Enum.GetValues<GearSlot>().Length;

    private readonly GearChoice[] _slots = new GearChoice[SlotCount];

    public int Character { get; set; }

    public GearChoice this[GearSlot slot]
    {
        get => _slots[(int)slot];
        set => _slots[(int)slot] = value;
    }

    public Loadout Copy()
    {
        var copy = new Loadout { Character = Character };
        Array.Copy(_slots, copy._slots, SlotCount);
        return copy;
    }

    public bool SameAs(Loadout other)
    {
        if (other.Character != Character)
        {
            return false;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (_slots[i] != other._slots[i])
            {
                return false;
            }
        }

        return true;
    }
}
