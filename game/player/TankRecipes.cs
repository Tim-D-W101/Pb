using Godot;
using Pb.Game.World;
using Pb.Sim.Gear;

namespace Pb.Game.Player;

/// <summary>
/// The air tanks built in code, one per brand, in the tank's own frame: the front of its regulator at the origin (where
/// it screws into a marker's back, <see cref="GearBuild.TankMount"/>), the bottle running back along +Z as the stock.
/// Kilnmark's Ingot is a squared-off steel bottle with banded ends and a block regulator; Vellis's Fibre a carbon-fibre
/// bottle with a coloured band and a label; Quarrow's Canteen a bottle in a cloth sleeve with straps and a patch;
/// Norrel's Bottle the field's plain aluminium one (<see cref="MarkerShape"/>'s).
/// </summary>
public static class TankRecipes
{
    public static readonly string[] Shapes = { "tank_ingot", "tank_fibre", "tank_sleeved", "tank_bottle" };

    public static GearBuild Build(string shape, ShapeMesh m, GearBrand brand) => shape switch
    {
        "tank_ingot" => Ingot(m, brand),
        "tank_fibre" => Fibre(m, brand),
        "tank_sleeved" => Sleeved(m, brand),
        _ => Bottle(m, brand),
    };

    /// <summary>A rotation whose +Y runs back (+Z), down the bottle.</summary>
    private static readonly Basis AlongBack = new(Vector3.Right, Mathf.Pi / 2f);

    private static readonly Basis Across = new(Vector3.Back, Mathf.Pi / 2f);

    private static Vector2[] Profile(params float[] xy)
    {
        var points = new Vector2[xy.Length / 2];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
        }

        return points;
    }

    /// <summary>The mark along the bottle's side at <paramref name="z"/>, <paramref name="out_"/> from its axis, on both sides.</summary>
    private static void Marks(ShapeMesh m, int zone, GearBrand brand, float z, float out_, float height)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            BrandMarks.Print(m, zone, brand, new Vector3(side * out_, 0f, z), side > 0f ? Vector3.Forward : Vector3.Back, Vector3.Up, height);
        }
    }

    private static GearBuild Ingot(ShapeMesh m, GearBrand brand)
    {
        int main = GearZone.Of(GearFinish.Metal, GearPaint.Main), second = GearZone.Of(GearFinish.Matt, GearPaint.Second);
        int accent = GearZone.Of(GearFinish.Metal, GearPaint.Accent), mark = GearZone.Of(GearFinish.Mark, GearPaint.Second);
        // A block regulator, bolted, with its gauge on the right.
        m.Box(accent, new Vector3(0f, 0f, 0.017f), new Vector3(0.036f, 0.036f, 0.034f));
        foreach ((float x, float y) in new[] { (-1f, -1f), (-1f, 1f), (1f, -1f), (1f, 1f) })
        {
            m.Cylinder(GearZone.Steel, new Vector3(x * 0.0182f, y * 0.011f, 0.017f + 0.009f * x * y), Across, 0.0028f, 0.002f, 6);
        }

        m.Cylinder(GearZone.Steel, new Vector3(0.021f, 0.006f, 0.016f), Across, 0.0088f, 0.006f, 14);
        m.Cylinder(GearZone.Lens, new Vector3(0.0242f, 0.006f, 0.016f), Across, 0.0072f, 0.0008f, 14);
        m.Cylinder(GearZone.Steel, new Vector3(0f, 0f, 0.039f), AlongBack, 0.0135f, 0.01f, 14);
        // The bottle, squared off, its ends banded.
        m.Pillow(main, new Vector3(0f, 0f, 0.1515f), new Vector3(0.066f, 0.066f, 0.215f), Basis.Identity, 4.5f, 8, 18);
        foreach (float z in new[] { 0.09f, 0.214f })
        {
            m.Pillow(second, new Vector3(0f, 0f, z), new Vector3(0.0694f, 0.0694f, 0.012f), Basis.Identity, 4.5f, 3, 18);
        }

        Marks(m, mark, brand, 0.152f, 0.0331f, 0.024f);
        return new GearBuild();
    }

    private static GearBuild Fibre(ShapeMesh m, GearBrand brand)
    {
        int main = GearZone.Of(GearFinish.Carbon, GearPaint.Main), second = GearZone.Of(GearFinish.Gloss, GearPaint.Second);
        int accent = GearZone.Of(GearFinish.Gloss, GearPaint.Accent), mark = GearZone.Of(GearFinish.Mark, GearPaint.Main);
        // A slim regulator with its gauge.
        m.Cylinder(GearZone.Steel, new Vector3(0f, 0f, 0.019f), AlongBack, 0.015f, 0.038f, 16);
        m.Cylinder(accent, new Vector3(0.0172f, 0.003f, 0.02f), Across, 0.0082f, 0.005f, 14);
        m.Cylinder(GearZone.Lens, new Vector3(0.0198f, 0.003f, 0.02f), Across, 0.0066f, 0.0008f, 14);
        // The carbon bottle, a band of colour near its neck and a label round its middle.
        const float start = 0.038f;
        m.Lathe(main, new Vector3(0f, 0f, start), AlongBack, Profile(
            0f, 0f, 0.011f, 0f, 0.012f, 0.006f, 0.02f, 0.013f, 0.027f, 0.022f, 0.0305f, 0.038f, 0.0305f, 0.235f,
            0.027f, 0.25f, 0.018f, 0.259f, 0f, 0.263f), 22);
        m.Lathe(second, new Vector3(0f, 0f, start), AlongBack, Profile(0.0303f, 0.05f, 0.031f, 0.0505f, 0.031f, 0.0635f, 0.0303f, 0.064f), 22);
        m.Lathe(accent, new Vector3(0f, 0f, start), AlongBack, Profile(0.0303f, 0.108f, 0.031f, 0.1085f, 0.031f, 0.1715f, 0.0303f, 0.172f), 22);
        Marks(m, mark, brand, start + 0.14f, 0.0312f, 0.018f);
        return new GearBuild();
    }

    private static GearBuild Sleeved(ShapeMesh m, GearBrand brand)
    {
        int main = GearZone.Of(GearFinish.Fabric, GearPaint.Main), second = GearZone.Of(GearFinish.Fabric, GearPaint.Second);
        int accent = GearZone.Of(GearFinish.Matt, GearPaint.Accent), mark = GearZone.Of(GearFinish.Mark, GearPaint.Accent);
        m.Cylinder(accent, new Vector3(0f, 0f, 0.018f), AlongBack, 0.016f, 0.036f, 16);
        // The sleeve over the bottle, a drawstring collar at its neck, two straps round it and a patch with the mark.
        const float start = 0.036f;
        m.Lathe(main, new Vector3(0f, 0f, start), AlongBack, Profile(
            0f, 0f, 0.024f, 0f, 0.033f, 0.012f, 0.035f, 0.03f, 0.035f, 0.21f, 0.033f, 0.226f, 0.025f, 0.236f, 0f, 0.24f), 18);
        m.Lathe(second, new Vector3(0f, 0f, start), AlongBack, Profile(0.0245f, -0.004f, 0.029f, 0f, 0.029f, 0.01f, 0.0255f, 0.014f), 18);
        foreach (float along in new[] { 0.07f, 0.17f })
        {
            m.Lathe(second, new Vector3(0f, 0f, start), AlongBack, Profile(0.035f, along - 0.008f, 0.0362f, along - 0.0075f, 0.0362f, along + 0.0075f, 0.035f, along + 0.008f), 18);
            m.Box(GearZone.Of(GearFinish.Plastic, GearPaint.Accent), new Vector3(0.0365f, 0f, start + along), new Vector3(0.004f, 0.016f, 0.02f));
        }

        foreach (float side in new[] { -1f, 1f })
        {
            m.Box(second, new Vector3(side * 0.0355f, 0f, start + 0.12f), new Vector3(0.0022f, 0.024f, 0.074f));
        }

        Marks(m, mark, brand, start + 0.12f, 0.0368f, 0.02f);
        return new GearBuild();
    }

    private static GearBuild Bottle(ShapeMesh m, GearBrand brand)
    {
        var shape = new ShapeMesh();
        MarkerShape.Build(shape, MarkerGroup.Tank);
        // The field's bottle is built in the marker's frame, its regulator 0.13 m behind the receiver's centre and 4 cm down.
        m.Absorb(shape, new Transform3D(Basis.Identity, new Vector3(0f, 0.04f, -0.13f)), part => (MarkerPart)part switch
        {
            MarkerPart.Tank => GearZone.Of(GearFinish.Metal, GearPaint.Main),
            _ => GearZone.Of(GearFinish.Metal, GearPaint.Accent),
        });
        Marks(m, GearZone.Of(GearFinish.Mark, GearPaint.Second), brand, 0.16f, 0.0322f, 0.02f);
        return new GearBuild();
    }
}
