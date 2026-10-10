using Pb.Net.Packing;
using Pb.Sim.Gear;

namespace Pb.Net.Protocol;

/// <summary>
/// A loadout on the wire (protocol 4): whether there is one, the character, and each slot's item (its index in the
/// catalogue, which every copy loads from the same data, the data hash makes sure) and its three colours: 50 to 70
/// bytes. What's read isn't trusted: the host puts a slot's default in for an item that isn't one of the slot's
/// (<see cref="GearCatalog.Normalised"/>).
/// </summary>
public static class KitCodec
{
    /// <summary>The largest item index read (a catalogue never holds more).</summary>
    public const int MostItems = 1024;

    public static void Write(BitWriter w, Loadout? loadout)
    {
        w.WriteBool(loadout is not null);
        if (loadout is null)
        {
            return;
        }

        w.WriteByte((byte)Math.Clamp(loadout.Character, 0, 255));
        for (int s = 0; s < Loadout.SlotCount; s++)
        {
            GearChoice choice = loadout[(GearSlot)s];
            w.WriteVarUInt((uint)Math.Clamp(choice.Item, 0, MostItems));
            w.WriteBits(choice.Colours.Main & 0xFFFFFF, 24);
            w.WriteBits(choice.Colours.Second & 0xFFFFFF, 24);
            w.WriteBits(choice.Colours.Accent & 0xFFFFFF, 24);
        }
    }

    public static Loadout? Read(ref BitReader r)
    {
        if (!r.ReadBool())
        {
            return null;
        }

        var loadout = new Loadout { Character = r.ReadByte() };
        for (int s = 0; s < Loadout.SlotCount; s++)
        {
            int item = (int)Math.Min(r.ReadVarUInt(), MostItems);
            uint main = r.ReadBits(24), second = r.ReadBits(24), accent = r.ReadBits(24);
            loadout[(GearSlot)s] = new GearChoice(item, new GearColours(main, second, accent));
        }

        return loadout;
    }
}
