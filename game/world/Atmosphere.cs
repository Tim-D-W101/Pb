using Godot;
using Pb.Game.Core;

namespace Pb.Game.World;

/// <summary>
/// Applies the lighting data (sun, cloudy sky, fog) and a graphics preset to the scene's
/// environment, sun and viewport. Kept separate from the level builder so presets can be switched
/// at runtime from the settings menu.
/// </summary>
public static class Atmosphere
{
    public static void ApplyLighting(WorldEnvironment world, DirectionalLight3D sun, LightingDef l)
    {
        Environment env = world.Environment ??= new Environment();

        sun.Basis = Basis.LookingAt(-ToSun(l), Vector3.Up);
        sun.LightColor = Color.FromHtml(l.SunColor);
        sun.LightEnergy = l.SunEnergy;
        sun.ShadowEnabled = true;
        sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
        sun.ShadowBlur = l.SunShadowBlur;

        var clouds = new NoiseTexture2D
        {
            Width = 1024,
            Height = 512,
            Seamless = true,
            Noise = new FastNoiseLite
            {
                NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
                Frequency = 0.004f,
                FractalOctaves = 5,
                FractalGain = 0.55f,
                Seed = 12,
            },
            ColorRamp = CloudRamp(l.CloudCover),
        };
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = Color.FromHtml(l.SkyTopColor),
            SkyHorizonColor = Color.FromHtml(l.SkyHorizonColor),
            // Below the horizon the sky shows past the edge of the scrubland: match the fog there.
            GroundHorizonColor = Color.FromHtml(l.FogColor),
            GroundBottomColor = Color.FromHtml(l.GroundColor),
            SkyCurve = 0.12f,
            SkyCover = clouds,
            SkyCoverModulate = new Color(0.82f, 0.8f, 0.78f, Mathf.Lerp(0.3f, 0.95f, l.CloudCover)),
            SunAngleMax = 18f,
            SunCurve = 0.08f,
        };

        env.BackgroundMode = Environment.BGMode.Sky;
        env.Sky = new Sky { SkyMaterial = sky };
        env.AmbientLightSource = Environment.AmbientSource.Sky;
        env.AmbientLightEnergy = l.AmbientEnergy;
        env.ReflectedLightSource = Environment.ReflectionSource.Sky;
        env.TonemapMode = Environment.ToneMapper.Aces;
        env.TonemapExposure = l.Exposure;
        env.TonemapWhite = 6f;
        env.FogEnabled = true;
        env.FogLightColor = Color.FromHtml(l.FogColor);
        env.FogDensity = l.FogDensity;
        env.FogSkyAffect = 0.15f;
        env.FogAerialPerspective = 0.3f;
        env.AdjustmentEnabled = true;
        env.AdjustmentSaturation = 0.88f;
        env.AdjustmentContrast = 1.04f;
    }

    /// <summary>Unit vector towards the sun: compass bearing with north = −Z and east = +X.</summary>
    public static Vector3 ToSun(LightingDef l)
    {
        float az = Mathf.DegToRad(l.SunAzimuth_deg);
        float el = Mathf.DegToRad(l.SunElevation_deg);
        return new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), -Mathf.Cos(az) * Mathf.Cos(el));
    }

    public static void ApplyPreset(WorldEnvironment world, DirectionalLight3D sun, Viewport viewport, GraphicsPresetDef p)
    {
        Environment env = world.Environment ??= new Environment();
        env.SsaoEnabled = p.Ssao;
        env.SsaoRadius = 1.2f;
        env.SsaoIntensity = 1.6f;
        env.SsilEnabled = p.Ssil;
        env.SdfgiEnabled = p.Sdfgi;
        env.SdfgiUseOcclusion = true;
        env.VolumetricFogEnabled = p.VolumetricFog;
        env.VolumetricFogDensity = 0.012f;
        env.VolumetricFogAlbedo = new Color(0.86f, 0.83f, 0.78f);
        env.VolumetricFogLength = 64f;
        env.SsrEnabled = p.Ssr;
        env.GlowEnabled = p.Glow;
        env.GlowIntensity = 0.35f;
        env.GlowBloom = 0.04f;

        sun.DirectionalShadowMaxDistance = p.ShadowDistance_m;
        RenderingServer.DirectionalShadowAtlasSetSize(p.ShadowSize, true);
        viewport.Msaa3D = p.Msaa switch
        {
            1 => Viewport.Msaa.Msaa2X,
            2 => Viewport.Msaa.Msaa4X,
            _ => Viewport.Msaa.Disabled,
        };
        viewport.ScreenSpaceAA = p.ScreenAa switch
        {
            "fxaa" => Viewport.ScreenSpaceAAEnum.Fxaa,
            "smaa" => Viewport.ScreenSpaceAAEnum.Smaa,
            _ => Viewport.ScreenSpaceAAEnum.Disabled,
        };
    }

    private static Gradient CloudRamp(float cover)
    {
        // Higher cover pushes the clear-sky threshold down so more of the noise reads as cloud.
        float clear = Mathf.Lerp(0.62f, 0.3f, cover);
        var ramp = new Gradient();
        ramp.SetOffset(0, clear);
        ramp.SetColor(0, new Color(1f, 1f, 1f, 0f));
        ramp.SetOffset(1, Mathf.Min(1f, clear + 0.35f));
        ramp.SetColor(1, new Color(1f, 1f, 1f, 1f));
        return ramp;
    }
}
