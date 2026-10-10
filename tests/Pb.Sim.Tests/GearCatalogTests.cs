using Pb.Sim.Data;
using Pb.Sim.Gear;

namespace Pb.Sim.Tests;

/// <summary>Phase 5 (M5.1): the gear locker's catalogue, the default kit, and the kit dealt to bots.</summary>
public class GearCatalogTests
{
    private static GearCatalog Gear => TestData.Data.Gear;

    [Fact]
    public void Every_brand_makes_something_for_every_slot()
    {
        Assert.Equal(new[] { "kilnmark", "vellis", "quarrow", "norrel" }, Gear.Brands.Select(b => b.Id));
        foreach (GearBrand brand in Gear.Brands)
        {
            foreach (GearSlot slot in Enum.GetValues<GearSlot>())
            {
                Assert.Contains(Gear.ItemsIn(slot), i => Gear.Items[i].Brand == brand);
            }
        }

        // Each slot's list holds only that slot's items, in the catalogue's order.
        foreach (GearSlot slot in Enum.GetValues<GearSlot>())
        {
            Assert.All(Gear.ItemsIn(slot), i => Assert.Equal(slot, Gear.Items[i].Slot));
            Assert.Equal(Gear.ItemsIn(slot).OrderBy(i => i), Gear.ItemsIn(slot));
        }
    }

    [Fact]
    public void The_default_kit_is_the_fields_own_with_the_generated_markers_loader_and_tank_built_in()
    {
        Loadout kit = Gear.Default(character: 2);
        Assert.Equal(2, kit.Character);
        foreach (GearSlot slot in Enum.GetValues<GearSlot>())
        {
            Assert.Equal("norrel", Gear.Items[kit[slot].Item].Brand.Id);
            Assert.Equal(Gear.Items[kit[slot].Item].Colours, kit[slot].Colours);
        }

        Assert.True(Gear.Included(kit, GearSlot.Loader));
        Assert.True(Gear.Included(kit, GearSlot.Tank));
        Assert.False(Gear.Included(kit, GearSlot.Marker));

        // Another brand's marker has nothing built in, so the loader and tank picked are the ones drawn.
        int forge = Gear.IndexOf("kilnmark_forge");
        kit[GearSlot.Marker] = new GearChoice(forge, Gear.Items[forge].Colours);
        Assert.False(Gear.Included(kit, GearSlot.Loader));
        Assert.False(Gear.Included(kit, GearSlot.Tank));
    }

    [Fact]
    public void A_bots_kit_is_dealt_the_same_from_the_same_seed_and_mixes_the_brands()
    {
        var brands = new HashSet<string>();
        for (int id = 0; id < 10; id++)
        {
            Loadout a = Gear.Deal(20261010, id, character: id % 3);
            Loadout b = Gear.Deal(20261010, id, character: id % 3);
            Assert.True(a.SameAs(b));
            foreach (GearSlot slot in Enum.GetValues<GearSlot>())
            {
                Assert.True(Gear.Fits(slot, a[slot].Item));
                Assert.Equal(Gear.Items[a[slot].Item].Colours, a[slot].Colours);
                brands.Add(Gear.Items[a[slot].Item].Brand.Id);
            }
        }

        Assert.Equal(4, brands.Count);
        Assert.False(Gear.Deal(1, 3, 0).SameAs(Gear.Deal(2, 3, 0)) && Gear.Deal(1, 4, 0).SameAs(Gear.Deal(2, 4, 0)));
    }

    [Fact]
    public void A_pick_that_isnt_its_slots_falls_back_to_the_default()
    {
        Loadout kit = Gear.Default(0);
        int glide = Gear.IndexOf("vellis_glide");
        kit[GearSlot.Marker] = new GearChoice(glide, new GearColours(0x112233, 0x445566, 0x778899));
        kit[GearSlot.Mask] = new GearChoice(glide, default);
        kit[GearSlot.Jersey] = new GearChoice(999, default);
        Loadout fixedUp = Gear.Normalised(kit);
        Assert.Equal(glide, fixedUp[GearSlot.Marker].Item);
        Assert.Equal(new GearColours(0x112233, 0x445566, 0x778899), fixedUp[GearSlot.Marker].Colours);
        Assert.Equal(Gear.DefaultItem(GearSlot.Mask), fixedUp[GearSlot.Mask].Item);
        Assert.Equal(Gear.DefaultItem(GearSlot.Jersey), fixedUp[GearSlot.Jersey].Item);
        Assert.Equal(-1, Gear.IndexOf("no_such_item"));
    }

    [Theory]
    [InlineData("\"slot\": \"pants\", \"brand\": \"vellis\"", "\"slot\": \"jersey\", \"brand\": \"vellis\"", "brand 'vellis' has no pants")]
    [InlineData("\"brand\": \"quarrow\", \"displayName\": \"Ranger\"", "\"brand\": \"quarow\", \"displayName\": \"Ranger\"", "unknown brand 'quarow'")]
    [InlineData("\"colours\": [\"#2c2e31\", \"#b4572b\", \"#c8c2b2\"] },", "\"colours\": [\"#2c2e31\", \"rust\", \"#c8c2b2\"] },", "'rust' isn't a colour")]
    [InlineData("\"mask\": \"norrel_issue_mask\"", "\"mask\": \"norrel_bowl\"", "'norrel_bowl' is a loader, not a mask")]
    [InlineData("\"mark\": \"VELLIS\"", "\"mark\": \"Vellis\"", "may only hold A–Z")]
    [InlineData("\"shape\": \"loader_hod\",", "\"shape\": \"loader_hod\", \"includes\": [\"tank\"],", "only a generated model can have other slots built in")]
    public void A_catalogue_mistake_is_named(string from, string to, string message)
    {
        var source = new EditedDataSource(TestData.Source).Edit("gear/catalog.jsonc", t =>
        {
            Assert.Contains(from, t);
            return ReplaceFirst(t, from, to);
        });
        var ex = Assert.Throws<DataException>(() => GameData.Load(source));
        Assert.Contains("gear/catalog.jsonc", ex.Message);
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void Colours_read_and_write_as_hex()
    {
        Assert.True(GearColours.TryParse("#b4572b", out uint rust));
        Assert.Equal(0xB4572Bu, rust);
        Assert.Equal("#b4572b", GearColours.Format(rust));
        Assert.False(GearColours.TryParse("#b4572", out _));
        Assert.False(GearColours.TryParse("orange", out _));
        var colours = new GearColours(1, 2, 3);
        Assert.Equal(new GearColours(1, 9, 3), colours.With(1, 9));
        Assert.Equal(3u, colours[2]);
    }

    [Fact]
    public void A_loadout_saved_to_the_profile_reads_back_the_same()
    {
        Loadout kit = Gear.Deal(7, 2, character: 1);
        kit[GearSlot.Jersey] = kit[GearSlot.Jersey] with { Colours = new GearColours(0x123456, 0xABCDEF, 0x0F0F0F) };
        var profile = new Pb.Sim.Match.ProfileData { Loadout = Gear.Write(kit) };
        Pb.Sim.Match.ProfileData? loaded = Pb.Sim.Match.ProfileData.FromJson(profile.ToJson());
        Assert.NotNull(loaded?.Loadout);
        Assert.True(Gear.Read(loaded!.Loadout, character: 0).SameAs(kit));
        Assert.Contains("\"#123456\"", profile.ToJson());
        Assert.Contains($"\"{Gear.Items[kit[GearSlot.Marker].Item].Id}\"", profile.ToJson());
    }

    [Fact]
    public void A_profile_from_before_the_locker_wears_the_default_kit_on_its_character()
    {
        Pb.Sim.Match.ProfileData? old = Pb.Sim.Match.ProfileData.FromJson("{ \"version\": 2, \"records\": [] }");
        Assert.Null(old!.Loadout);
        Assert.True(Gear.Read(old.Loadout, character: 2).SameAs(Gear.Default(2)));
    }

    [Fact]
    public void A_saved_item_the_catalogue_no_longer_has_reads_as_its_slots_default()
    {
        var saved = Gear.Write(Gear.Deal(9, 1, 0));
        saved.Marker = new SavedPick { Item = "gone_marker", Colours = new[] { "#000000", "#000000", "#000000" } };
        saved.Mask = new SavedPick { Item = "vellis_glide", Colours = new[] { "#000000", "#000000", "#000000" } };
        saved.Tank = null;
        saved.Pants = new SavedPick { Item = "vellis_pace", Colours = new[] { "red", "#000000" } };
        Loadout read = Gear.Read(saved, character: 0);
        Assert.Equal(Gear.DefaultItem(GearSlot.Marker), read[GearSlot.Marker].Item);
        Assert.Equal(Gear.Items[Gear.DefaultItem(GearSlot.Marker)].Colours, read[GearSlot.Marker].Colours);
        Assert.Equal(Gear.DefaultItem(GearSlot.Mask), read[GearSlot.Mask].Item);
        Assert.Equal(Gear.DefaultItem(GearSlot.Tank), read[GearSlot.Tank].Item);
        // An item it has, with colours that don't read: the item in its own colours.
        int pace = Gear.IndexOf("vellis_pace");
        Assert.Equal(new GearChoice(pace, Gear.Items[pace].Colours), read[GearSlot.Pants]);
    }

    private static string ReplaceFirst(string text, string from, string to)
    {
        int at = text.IndexOf(from, StringComparison.Ordinal);
        return text[..at] + to + text[(at + from.Length)..];
    }
}
