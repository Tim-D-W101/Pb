using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Game.World;
using Pb.Sim.Gear;

namespace Pb.Game.Player;

/// <summary>
/// The gear items built in code (<see cref="GearRecipes"/>), as worn by players: each item's mesh is built once,
/// fitted into the hitbox it fills, with levels of detail, and shared by everyone wearing it; its surfaces are the
/// shared zone materials (gear.gdshader, gear_clear.gdshader), and each wearer's colours are set on their own
/// instance, so ten loadouts are ten instances, not ten sets of meshes. First person builds its own, unfitted and at
/// full detail (<see cref="ViewModel"/>).
/// </summary>
public static class GearModels
{
    private static readonly Dictionary<(int Item, Vector3 Size), Worn> Fitted = new();
    private static readonly Dictionary<int, Material> Materials = new();
    private static Shader? _shader;
    private static Shader? _clear;
    private static GearCatalog? _catalog;

    /// <summary>An item fitted into its hitbox: the shared mesh, the recipe's report, and the fit (item frame → hitbox frame).</summary>
    public sealed record Worn(ArrayMesh Mesh, GearBuild Build, Transform3D Fit);

    /// <summary>Gets the catalogue's marks painted; call before building anything.</summary>
    public static void Prepare(GearCatalog catalog)
    {
        if (_catalog == catalog)
        {
            return;
        }

        _catalog = catalog;
        BrandMarks.Atlas(catalog.Brands);
        foreach (string unknown in GearRecipes.Unknown(catalog))
        {
            GD.PushError($"gear/catalog.jsonc: no recipe for {unknown}; it's drawn as its slot's default");
        }
    }

    /// <summary>The item for <paramref name="slot"/> in <paramref name="loadout"/>, or the slot's default when its shape has no recipe.</summary>
    public static GearItem ItemFor(GearCatalog catalog, Loadout loadout, GearSlot slot)
    {
        GearItem item = catalog.Items[catalog.Fits(slot, loadout[slot].Item) ? loadout[slot].Item : catalog.DefaultItem(slot)];
        return GearRecipes.Has(slot, item.Shape) ? item : catalog.Items[catalog.DefaultItem(slot)];
    }

    /// <summary>Builds <paramref name="item"/> (a marker, loader or tank) unplaced, in its own frame, into a new mesh.</summary>
    public static (ShapeMesh Shape, GearBuild Build) Shape(GearItem item, int seed, bool paint)
    {
        var shape = new ShapeMesh();
        GearBuild build = GearRecipes.Build(item, shape, seed, paint);
        return (shape, build);
    }

    /// <summary>
    /// <paramref name="item"/> fitted into a hitbox of <paramref name="size"/> (a marker by its length, so its muzzle
    /// reaches the box's front; a loader or tank to fit inside), built the first time it's asked for.
    /// </summary>
    public static Worn Fit(GearItem item, Vector3 size)
    {
        if (Fitted.TryGetValue((item.Index, size), out Worn? worn))
        {
            return worn;
        }

        (ShapeMesh shape, GearBuild build) = Shape(item, seed: item.Index + 1, paint: true);
        (Vector3 centre, Vector3 nominal) = build.Nominal ?? Bounds(shape);
        float scale = item.Slot == GearSlot.Marker
            ? size.Z / nominal.Z
            : Mathf.Min(size.X / nominal.X, Mathf.Min(size.Y / nominal.Y, size.Z / nominal.Z));
        var fit = new Transform3D(Basis.Identity.Scaled(Vector3.One * scale), -centre * scale);
        var placed = new ShapeMesh();
        placed.Absorb(shape, fit, zone => zone);
        var mesh = new ArrayMesh();
        placed.Commit(mesh, Material);
        worn = new Worn(ShapeMesh.WithLods(mesh), build, fit);
        Fitted[(item.Index, size)] = worn;
        return worn;
    }

    /// <summary>A drawn instance of <paramref name="mesh"/> in a wearer's colours (and paint, for a see-through loader).</summary>
    public static MeshInstance3D Instance(string name, Mesh mesh, GearColours colours, Color paint)
    {
        var instance = new MeshInstance3D { Name = name, Mesh = mesh };
        Colour(instance, colours, paint);
        return instance;
    }

    /// <summary>
    /// <paramref name="instance"/> (a worn item) made able to take paint: its surfaces given materials of their own,
    /// copies of the shared ones so the paint is only on this one, drawing paint in the look of <paramref name="def"/>.
    /// Null if none of its surfaces is drawn by the gear shaders.
    /// </summary>
    public static Pb.Game.Ballistics.PaintSlots? Paintable(MeshInstance3D instance, Pb.Game.Ballistics.WetSplats splats, SplatDef def)
    {
        if (instance.Mesh is not { } mesh)
        {
            return null;
        }

        var slots = new Pb.Game.Ballistics.PaintSlots(def);
        bool any = false;
        for (int i = 0; i < mesh.GetSurfaceCount(); i++)
        {
            if ((instance.GetSurfaceOverrideMaterial(i) ?? mesh.SurfaceGetMaterial(i)) is ShaderMaterial shared)
            {
                var own = (ShaderMaterial)shared.Duplicate();
                instance.SetSurfaceOverrideMaterial(i, own);
                slots.Bind(own, splats);
                any = true;
            }
        }

        return any ? slots : null;
    }

    /// <summary>
    /// Paints <paramref name="splat"/> on <paramref name="instance"/> in its own frame (an item's mesh is fitted inside its
    /// hitbox, so its surface lies up to <paramref name="reach"/> behind the hit, m). Its handle.
    /// </summary>
    public static int Paint(Pb.Game.Ballistics.PaintSlots slots, Node3D instance, in Pb.Game.Ballistics.Splat splat, float reach)
    {
        Transform3D local = instance.GlobalTransform.AffineInverse();
        float unit = local.Basis.X.Length();
        Vector3 facing = (local.Basis * splat.Normal).Normalized();
        return slots.Add(local * splat.Point, facing, Pb.Game.Ballistics.PaintSlots.Across(facing, splat.Spin), splat.Radius * unit,
            (reach + splat.Radius * 0.5f) * unit, splat.Shape, splat.Colour, Pb.Game.Ballistics.PaintSlots.Now);
    }

    /// <summary>Sets a worn item's colours (the locker changes them as you pick).</summary>
    public static void Colour(GeometryInstance3D instance, GearColours colours, Color paint)
    {
        instance.SetInstanceShaderParameter("main_colour", Linear(colours.Main));
        instance.SetInstanceShaderParameter("second_colour", Linear(colours.Second));
        instance.SetInstanceShaderParameter("accent_colour", Linear(colours.Accent));
        Color p = paint.SrgbToLinear();
        instance.SetInstanceShaderParameter("paint_colour", new Vector3(p.R, p.G, p.B));
    }

    /// <summary>A colour (0xRRGGBB, sRGB) as the game's colour.</summary>
    public static Color ToColor(uint colour) => new(((colour >> 16) & 0xFF) / 255f, ((colour >> 8) & 0xFF) / 255f, (colour & 0xFF) / 255f);

    private static Vector3 Linear(uint colour)
    {
        Color c = ToColor(colour).SrgbToLinear();
        return new Vector3(c.R, c.G, c.B);
    }

    private static (Vector3 Centre, Vector3 Size) Bounds(ShapeMesh shape)
    {
        Aabb box = shape.Bounds();
        return (box.GetCenter(), box.Size);
    }

    /// <summary>The shared material for a zone (<see cref="GearZone"/>).</summary>
    public static Material Material(int zone)
    {
        if (Materials.TryGetValue(zone, out Material? material))
        {
            return material;
        }

        _shader ??= GD.Load<Shader>("res://shaders/gear.gdshader");
        _clear ??= GD.Load<Shader>("res://shaders/gear_clear.gdshader");
        GearFinish finish = GearZone.FinishOf(zone);
        GearPaint paint = GearZone.PaintOf(zone);
        (Color own, float roughness, float metallic, float specular, int pattern) = GearZone.Look(finish);
        var shaderMaterial = new ShaderMaterial { Shader = finish == GearFinish.Shell ? _clear : _shader };
        shaderMaterial.SetShaderParameter("paint_slot", (int)paint);
        shaderMaterial.SetShaderParameter("roughness", roughness);
        if (finish != GearFinish.Shell)
        {
            shaderMaterial.SetShaderParameter("own_colour", own);
            shaderMaterial.SetShaderParameter("metallic", metallic);
            shaderMaterial.SetShaderParameter("specular_level", specular);
            shaderMaterial.SetShaderParameter("pattern", pattern);
            if (finish == GearFinish.Mark && _catalog is not null)
            {
                shaderMaterial.SetShaderParameter("cutout", true);
                shaderMaterial.SetShaderParameter("mark_tex", BrandMarks.Atlas(_catalog.Brands));
            }
        }

        Materials[zone] = shaderMaterial;
        return shaderMaterial;
    }
}
