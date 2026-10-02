using Godot;
using Pb.Game.Core;

namespace Pb.Game.World;

/// <summary>
/// A deck of cloud drifting across the sky (presentation.jsonc lighting "clouds"): a dome that follows
/// the camera, drawn with clouds.gdshader, which casts each view ray up to a flat layer of cloud and
/// reads two tiling noises there, moved by the wind; lit on the side towards the sun, grey underneath,
/// thinning into the haze at the horizon. The sky's own cloud cover stays behind it as a faint, still,
/// higher layer, so the sky's light and reflections don't change as it moves. Presentation only.
/// </summary>
public partial class CloudDeck : MeshInstance3D
{
    /// <summary>The dome's radius: inside the camera's far clip, beyond everything in the level.</summary>
    private const float Radius = 1200f;

    private static NoiseTexture2D? _base, _detail;

    public void Configure(LightingDef lighting)
    {
        CloudsDef def = lighting.Clouds;
        Mesh ??= new SphereMesh { Radius = Radius, Height = Radius, IsHemisphere = true, RadialSegments = 32, Rings = 12 };
        CastShadow = ShadowCastingSetting.Off;
        _base ??= Noise(0.0075f, 5, 31);
        _detail ??= Noise(0.02f, 4, 77);
        var material = MaterialOverride as ShaderMaterial ?? new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/clouds.gdshader") };
        material.SetShaderParameter("base_noise", _base);
        material.SetShaderParameter("detail_noise", _detail);
        material.SetShaderParameter("height", def.Height_m);
        material.SetShaderParameter("tile", def.Tile_m);
        material.SetShaderParameter("wind", new Vector2(def.Wind_mps[0], def.Wind_mps[1]));
        material.SetShaderParameter("cover", lighting.CloudCover);
        material.SetShaderParameter("opacity", def.Opacity);
        material.SetShaderParameter("sun_dir", Atmosphere.ToSun(lighting));
        material.SetShaderParameter("lit_color", Color.FromHtml(def.LitColor));
        material.SetShaderParameter("shade_color", Color.FromHtml(def.ShadeColor));
        material.SetShaderParameter("haze_color", Color.FromHtml(lighting.FogColor));
        MaterialOverride = material;
        // Drawn before anything else that's see-through.
        SortingOffset = -Radius;
    }

    public override void _Process(double delta)
    {
        if (GetViewport().GetCamera3D() is { } camera)
        {
            GlobalPosition = camera.GlobalPosition;
        }
    }

    /// <summary>A seamless tile of fractal noise, generated on a worker thread the first time.</summary>
    private static NoiseTexture2D Noise(float frequency, int octaves, int seed) => new()
    {
        Width = 512,
        Height = 512,
        Seamless = true,
        GenerateMipmaps = true,
        Noise = new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            Frequency = frequency,
            FractalOctaves = octaves,
            FractalGain = 0.5f,
            Seed = seed,
        },
    };
}
