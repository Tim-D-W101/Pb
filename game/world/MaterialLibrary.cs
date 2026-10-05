using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Data;
using Pb.Sim.Level;

namespace Pb.Game.World;

/// <summary>
/// One Godot material per kit material, made the first time it's asked for (so its textures only load
/// if something uses it). Opaque materials use weathered.gdshader, which draws a procedural pattern
/// (concrete, brick, corrugated metal…) plus grime until a texture is supplied in
/// kit/materials.jsonc; see-through materials (alpha &lt; 1) use a plain transparent material.
/// </summary>
public sealed class MaterialLibrary
{
    private readonly Dictionary<int, MaterialDef> _defs = new();
    private readonly Dictionary<string, int> _ids = new(System.StringComparer.Ordinal);
    private readonly Dictionary<int, Material> _materials = new();
    private readonly Dictionary<(int Index, Color Color), Material> _recoloured = new();
    private readonly Shader _shader;

    public MaterialLibrary(IReadOnlyList<KitMaterial> materials)
    {
        _shader = GD.Load<Shader>("res://shaders/weathered.gdshader");
        foreach (KitMaterial m in materials)
        {
            _defs[m.Index] = m.Def;
            _ids[m.Id] = m.Index;
        }
    }

    public Material this[int index]
    {
        get
        {
            if (!_materials.TryGetValue(index, out Material? material))
            {
                material = Create(_defs[index]);
                _materials[index] = material;
            }

            return material;
        }
    }

    public bool IsTransparent(int index) => this[index] is StandardMaterial3D;

    /// <summary>The index of the kit material with this id, or −1.</summary>
    public int Find(string id) => _ids.TryGetValue(id, out int index) ? index : -1;

    /// <summary>
    /// The material in another colour: its procedural base colour replaced, or its photo tinted.
    /// Made once per colour.
    /// </summary>
    public Material Recoloured(int index, Color color)
    {
        if (_recoloured.TryGetValue((index, color), out Material? material))
        {
            return material;
        }

        material = (Material)this[index].Duplicate();
        if (material is ShaderMaterial shader)
        {
            shader.SetShaderParameter(shader.GetShaderParameter("use_textures").AsBool() ? "texture_tint" : "base_color", color);
        }
        else if (material is StandardMaterial3D standard)
        {
            standard.AlbedoColor = new Color(color, standard.AlbedoColor.A);
        }

        _recoloured[(index, color)] = material;
        return material;
    }

    private Material Create(MaterialDef def)
    {
        Color color = Conv.ParseColor(def.Color, new Color(0.6f, 0.6f, 0.6f));
        if (def.Alpha < 0.999f)
        {
            // The kit's UVs are in metres, so a texture repeats every tile_m. A roughness map holds the
            // roughness itself, so it isn't scaled down by the base value.
            Texture2D? roughnessMap = LoadTexture(def.RoughnessMap);
            return new StandardMaterial3D
            {
                AlbedoColor = new Color(color, def.Alpha),
                AlbedoTexture = LoadTexture(def.Albedo),
                RoughnessTexture = roughnessMap,
                Uv1Scale = Vector3.One / def.Tile_m,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = roughnessMap is null ? def.Roughness : 1f,
                Metallic = def.Metallic,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
        }

        var material = new ShaderMaterial { Shader = _shader };
        material.SetShaderParameter("base_color", color);
        material.SetShaderParameter("pattern", (int)def.Pattern);
        material.SetShaderParameter("tile_m", def.Tile_m);
        material.SetShaderParameter("roughness_base", def.Roughness);
        material.SetShaderParameter("metallic_base", def.Metallic);
        material.SetShaderParameter("weathering", def.Weathering);

        Texture2D? albedo = LoadTexture(def.Albedo);
        Texture2D? normal = LoadTexture(def.Normal);
        Texture2D? roughness = LoadTexture(def.RoughnessMap);
        material.SetShaderParameter("use_textures", albedo is not null);
        if (albedo is not null)
        {
            material.SetShaderParameter("albedo_tex", albedo);
            material.SetShaderParameter("break_up", def.BreakUpRepeat);
            material.SetShaderParameter("texture_tint", Conv.ParseColor(def.Tint, Colors.White));
        }

        material.SetShaderParameter("has_normal_tex", normal is not null);
        if (normal is not null)
        {
            material.SetShaderParameter("normal_tex", normal);
        }

        material.SetShaderParameter("has_roughness_tex", roughness is not null);
        if (roughness is not null)
        {
            material.SetShaderParameter("roughness_tex", roughness);
        }

        return material;
    }

    private static Texture2D? LoadTexture(string? path) => ArtFiles.Load<Texture2D>(path);
}
