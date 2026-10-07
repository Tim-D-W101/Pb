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
    private static ImageTexture? _highLayer;
    private static (float Cover, float From, float To) _highLayerKey;

    public static void ApplyLighting(WorldEnvironment world, DirectionalLight3D sun, LightingDef l)
    {
        Environment env = world.Environment ??= new Environment();

        sun.Basis = Basis.LookingAt(-ToSun(l), Vector3.Up);
        sun.LightColor = Color.FromHtml(l.SunColor);
        sun.LightEnergy = l.SunEnergy;
        sun.ShadowEnabled = true;
        sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
        sun.ShadowBlur = l.SunShadowBlur;

        ImageTexture clouds = HighLayer(l);
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = Color.FromHtml(l.SkyTopColor),
            SkyHorizonColor = Color.FromHtml(l.SkyHorizonColor),
            // Below the horizon the sky shows past the edge of the scrubland: match the fog there.
            GroundHorizonColor = Color.FromHtml(l.FogColor),
            GroundBottomColor = Color.FromHtml(l.GroundColor),
            SkyCurve = 0.12f,
            SkyCover = clouds,
            // A faint, still, high layer behind the drifting deck (CloudDeck).
            SkyCoverModulate = new Color(0.82f, 0.8f, 0.78f, Mathf.Lerp(0.3f, 0.95f, l.CloudCover) * l.Clouds.HighLayer),
            SunAngleMax = 18f,
            SunCurve = 0.08f,
        };

        env.BackgroundMode = Environment.BGMode.Sky;
        env.Sky = new Sky { SkyMaterial = sky };
        if (world.GetNodeOrNull<CloudDeck>("CloudDeck") is not { } deck)
        {
            deck = new CloudDeck { Name = "CloudDeck" };
            world.AddChild(deck);
        }

        deck.Configure(l);
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
        // How the volumetric fog looks where a preset turns it on (ApplyPreset).
        env.VolumetricFogDensity = l.VolumetricFog.Density;
        env.VolumetricFogLength = l.VolumetricFog.Length_m;
        env.VolumetricFogAlbedo = Color.FromHtml(l.VolumetricFog.Albedo);
        env.VolumetricFogSkyAffect = l.VolumetricFog.SkyAffect;
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
        env.SsrEnabled = p.Ssr;
        env.GlowEnabled = p.Glow;
        env.GlowIntensity = 0.35f;
        env.GlowBloom = 0.04f;

        sun.ShadowEnabled = p.Shadows;
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

    /// <summary>Draws the 3D view at <paramref name="scale"/> of the screen's resolution, upscaled with FSR 1.0 below 1.</summary>
    public static void ApplyRenderScale(Viewport viewport, float scale, GraphicsDef graphics)
    {
        bool scaled = scale < 0.999f;
        viewport.Scaling3DMode = scaled ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
        viewport.Scaling3DScale = scaled ? scale : 1f;
        viewport.FsrSharpness = graphics.FsrSharpness;
    }

    /// <summary>
    /// The sky's own still cloud (its cover, a panorama: the top row is the point overhead), white with
    /// the cloud in its alpha. It fades out between <c>clouds.highLayerFade_deg</c>: towards the top
    /// a panorama's rows squeeze into a point, which drew the noise out into streaks over the player's head.
    /// It wraps round by blending in a strip of noise past its right-hand end, keeping the noise's
    /// contrast: Godot's seamless noise averages two fields across the middle (due north and along the
    /// horizon), which thinned the cloud there into a hazy band.
    /// </summary>
    private static ImageTexture HighLayer(LightingDef l)
    {
        // Painting it takes a fifth of a second, so the menu, the levels and the range share one.
        var key = (l.CloudCover, l.Clouds.HighLayerFade_deg[0], l.Clouds.HighLayerFade_deg[1]);
        if (_highLayer is { } cached && _highLayerKey == key)
        {
            return cached;
        }

        const int width = 1024, height = 512, skirt = 160;
        var noise = new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            Frequency = 0.004f,
            FractalOctaves = 5,
            FractalGain = 0.55f,
            Seed = 12,
        };
        Image grey = noise.GetImage(width + skirt, height);
        grey.Convert(Image.Format.L8);
        byte[] values = grey.GetData();
        float Value(int x, int y) => values[y * (width + skirt) + x] / 255f - 0.5f;
        // Higher cover pushes the clear-sky threshold down so more of the noise reads as cloud.
        float clear = Mathf.Lerp(0.62f, 0.3f, l.CloudCover), full = Mathf.Min(1f, clear + 0.35f);
        float[] fade = l.Clouds.HighLayerFade_deg;
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            float above = (0.5f - (y + 0.5f) / height) * 180f;
            float keep = 1f - Mathf.SmoothStep(fade[0], fade[1], above);
            for (int x = 0; x < width; x++)
            {
                float n = Value(x, y);
                if (x < skirt)
                {
                    // From the strip past the right-hand end (continuing it) into this column; scaled so
                    // the mix of two fields keeps one field's contrast.
                    float t = Mathf.SmoothStep(0f, 1f, (float)x / skirt);
                    n = (n * t + Value(x + width, y) * (1f - t)) / Mathf.Sqrt(t * t + (1f - t) * (1f - t));
                }

                int i = y * width + x;
                float cloud = Mathf.Clamp((n + 0.5f - clear) / (full - clear), 0f, 1f) * keep;
                rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = 255;
                rgba[i * 4 + 3] = (byte)(cloud * 255f + 0.5f);
            }
        }

        Image image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
        image.GenerateMipmaps();
        _highLayerKey = key;
        return _highLayer = ImageTexture.CreateFromImage(image);
    }
}
