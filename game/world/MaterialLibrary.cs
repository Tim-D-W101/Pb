using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Data;
using Pb.Sim.Level;

namespace Pb.Game.World;

/// <summary>
/// One Godot material per kit material. Opaque materials use weathered.gdshader, which draws a
/// procedural pattern (concrete, brick, corrugated metal…) plus grime until a texture is supplied
/// in kit/materials.jsonc; see-through materials (alpha &lt; 1) use a plain transparent material.
/// </summary>
public sealed class MaterialLibrary
{
    private readonly Dictionary<int, Material> _materials = new();
    private readonly Shader _shader;

    public MaterialLibrary(IReadOnlyList<KitMaterial> materials)
    {
        _shader = GD.Load<Shader>("res://shaders/weathered.gdshader");
        foreach (KitMaterial m in materials)
        {
            _materials[m.Index] = Create(m.Def);
        }
    }

    public Material this[int index] => _materials[index];

    public bool IsTransparent(int index) => _materials[index] is StandardMaterial3D;

    private Material Create(MaterialDef def)
    {
        Color color = Conv.ParseColor(def.Color, new Color(0.6f, 0.6f, 0.6f));
        if (def.Alpha < 0.999f)
        {
            return new StandardMaterial3D
            {
                AlbedoColor = new Color(color, def.Alpha),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = def.Roughness,
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
