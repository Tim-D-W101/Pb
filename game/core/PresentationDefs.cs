using System.Collections.Generic;
using System.Linq;
using Pb.Sim.Data;

namespace Pb.Game.Core;

// Game-side data files (presentation.jsonc, input.jsonc). Same loader and rules as the sim's data:
// unit-suffixed keys, unknown keys rejected, every key required unless [Optional].
#pragma warning disable CA1707

public sealed class PresentationDef : IValidatable
{
    public const string File = "presentation.jsonc";

    public string[] TeamColors { get; set; } = System.Array.Empty<string>();

    public BallViewDef Ball { get; set; } = new();

    public SplatDef Splat { get; set; } = new();

    public FxDef Fx { get; set; } = new();

    public CameraDef Camera { get; set; } = new();

    public ViewModelDef ViewModel { get; set; } = new();

    public LookDef Look { get; set; } = new();

    public CrosshairDef Crosshair { get; set; } = new();

    public ArcPreviewDef ArcPreview { get; set; } = new();

    public AudioDef Audio { get; set; } = new();

    public GraphicsDef Graphics { get; set; } = new();

    public LightingDef Lighting { get; set; } = new();

    public HorizonDef Horizon { get; set; } = new();

    public WeedsDef Weeds { get; set; } = new();

    public CracksDef Cracks { get; set; } = new();

    public DampDef Damp { get; set; } = new();

    public GraffitiDef Graffiti { get; set; } = new();

    public WornPathsDef WornPaths { get; set; } = new();

    public SnaggedBagsDef SnaggedBags { get; set; } = new();

    public FootDustDef FootDust { get; set; } = new();

    public BlowingLitterDef BlowingLitter { get; set; } = new();

    /// <summary>
    /// The wind near the ground ([x, z] m/s: +x east, +z south): the weeds sway, the bags on the wire
    /// stream and dust drifts with it. The clouds and the chimney smoke, higher up, have their own.
    /// </summary>
    public float[] GroundWind_mps { get; set; } = System.Array.Empty<float>();

    /// <summary><see cref="GroundWind_mps"/> as a plan vector (x, z).</summary>
    public Godot.Vector2 GroundWind => new(GroundWind_mps[0], GroundWind_mps[1]);

    public GroundDetailDef GroundDetail { get; set; } = new();

    public OldPaintDef OldPaint { get; set; } = new();

    public MarkingsViewDef Markings { get; set; } = new();

    public BirdsDef Birds { get; set; } = new();

    public CobwebsDef Cobwebs { get; set; } = new();

    public ContactShadowsDef ContactShadows { get; set; } = new();

    public RunOffDef RunOff { get; set; } = new();

    public FloorDebrisDef FloorDebris { get; set; } = new();

    public WoodsDef Woods { get; set; } = new();

    public YardFittingsDef YardFittings { get; set; } = new();

    public WallHangingsDef WallHangings { get; set; } = new();

    public CreepersDef Creepers { get; set; } = new();

    public MenuBackdropDef MenuBackdrop { get; set; } = new();

    public TrainingGroundDef TrainingGround { get; set; } = new();

    public ShaftsDef Shafts { get; set; } = new();

    public DustDef Dust { get; set; } = new();

    public WindowLightDef WindowLight { get; set; } = new();

    public CharactersDef Characters { get; set; } = new();

    public MaskSprayViewDef MaskSpray { get; set; } = new();

    public SpectatorDef Spectator { get; set; } = new();

    public HudDef Hud { get; set; } = new();

    public void Validate(Validator v)
    {
        if (TeamColors.Length < 2)
        {
            v.Error(nameof(TeamColors), "needs at least two colours");
        }

        foreach (string c in TeamColors)
        {
            if (!Godot.Color.HtmlIsValid(c))
            {
                v.Error(nameof(TeamColors), $"'{c}' is not a valid colour");
            }
        }

        Ball.Validate(v.Scope(nameof(Ball)));
        Splat.Validate(v.Scope(nameof(Splat)));
        Fx.Validate(v.Scope(nameof(Fx)));
        Camera.Validate(v.Scope(nameof(Camera)));
        ViewModel.Validate(v.Scope(nameof(ViewModel)));
        Look.Validate(v.Scope(nameof(Look)));
        Crosshair.Validate(v.Scope(nameof(Crosshair)));
        ArcPreview.Validate(v.Scope(nameof(ArcPreview)));
        Audio.Validate(v.Scope(nameof(Audio)));
        Graphics.Validate(v.Scope(nameof(Graphics)));
        Lighting.Validate(v.Scope(nameof(Lighting)));
        Horizon.Validate(v.Scope(nameof(Horizon)));
        Weeds.Validate(v.Scope(nameof(Weeds)));
        Cracks.Validate(v.Scope(nameof(Cracks)));
        Damp.Validate(v.Scope(nameof(Damp)));
        Graffiti.Validate(v.Scope(nameof(Graffiti)));
        WornPaths.Validate(v.Scope(nameof(WornPaths)));
        SnaggedBags.Validate(v.Scope(nameof(SnaggedBags)));
        FootDust.Validate(v.Scope(nameof(FootDust)));
        BlowingLitter.Validate(v.Scope(nameof(BlowingLitter)));
        if (GroundWind_mps.Length != 2 || System.MathF.Abs(GroundWind_mps[0]) > 30f || System.MathF.Abs(GroundWind_mps[1]) > 30f ||
            GroundWind_mps[0] * GroundWind_mps[0] + GroundWind_mps[1] * GroundWind_mps[1] < 0.01f)
        {
            v.Error(nameof(GroundWind_mps), "takes [x, z], each within ±30 m/s and not both zero");
        }
        GroundDetail.Validate(v.Scope(nameof(GroundDetail)));
        OldPaint.Validate(v.Scope(nameof(OldPaint)));
        Markings.Validate(v.Scope(nameof(Markings)));
        Birds.Validate(v.Scope(nameof(Birds)));
        Cobwebs.Validate(v.Scope(nameof(Cobwebs)));
        ContactShadows.Validate(v.Scope(nameof(ContactShadows)));
        RunOff.Validate(v.Scope(nameof(RunOff)));
        FloorDebris.Validate(v.Scope(nameof(FloorDebris)));
        Woods.Validate(v.Scope(nameof(Woods)));
        YardFittings.Validate(v.Scope(nameof(YardFittings)));
        WallHangings.Validate(v.Scope(nameof(WallHangings)));
        Creepers.Validate(v.Scope(nameof(Creepers)));
        MenuBackdrop.Validate(v.Scope(nameof(MenuBackdrop)));
        TrainingGround.Validate(v.Scope(nameof(TrainingGround)));
        Shafts.Validate(v.Scope(nameof(Shafts)));
        Dust.Validate(v.Scope(nameof(Dust)));
        WindowLight.Validate(v.Scope(nameof(WindowLight)));
        Characters.Validate(v.Scope(nameof(Characters)));
        MaskSpray.Validate(v.Scope(nameof(MaskSpray)));
        Spectator.Validate(v.Scope(nameof(Spectator)));
        Hud.Validate(v.Scope(nameof(Hud)));
    }
}

public sealed class BallViewDef : IValidatable
{
    public float MinPixels { get; set; }

    public float Streak_s { get; set; }

    public float VisualBlend_s { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(MinPixels), MinPixels, 0, 20);
        v.InRange(nameof(Streak_s), Streak_s, 0, 0.05);
        v.InRange(nameof(VisualBlend_s), VisualBlend_s, 0, 1);
    }
}

public sealed class SplatDef : IValidatable
{
    public int Cap { get; set; }

    public int FadeWindow { get; set; }

    public float SizeMin_m { get; set; }

    public float SizeMax_m { get; set; }

    public float Depth_m { get; set; }

    public float NormalFade { get; set; }

    public float DistanceFadeBegin_m { get; set; }

    public float DistanceFadeLength_m { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Cap), Cap, 0, 20000);
        v.InRange(nameof(FadeWindow), FadeWindow, 1, 5000);
        v.InRange(nameof(SizeMin_m), SizeMin_m, 0.01, 2);
        v.InRange(nameof(SizeMax_m), SizeMax_m, SizeMin_m, 2);
        v.InRange(nameof(Depth_m), Depth_m, 0.01, 2);
        v.InRange(nameof(NormalFade), NormalFade, 0, 1);
        v.InRange(nameof(DistanceFadeBegin_m), DistanceFadeBegin_m, 1, 1000);
        v.InRange(nameof(DistanceFadeLength_m), DistanceFadeLength_m, 0.1, 1000);
    }
}

public sealed class FxDef : IValidatable
{
    public int MaxBurstsPerFrame { get; set; }

    public int BreakParticles { get; set; }

    public int BounceParticles { get; set; }

    public float MinCameraDistance_m { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(MaxBurstsPerFrame), MaxBurstsPerFrame, 0, 200);
        v.InRange(nameof(MinCameraDistance_m), MinCameraDistance_m, 0, 10);
        v.InRange(nameof(BreakParticles), BreakParticles, 1, 200);
        v.InRange(nameof(BounceParticles), BounceParticles, 1, 200);
    }
}

public sealed class CameraDef : IValidatable
{
    /// <summary>Horizontal field of view at 16:9 (wider screens see more).</summary>
    public float Fov_deg { get; set; }

    public float FovMin_deg { get; set; }

    public float FovMax_deg { get; set; }

    public float FovStep_deg { get; set; }

    public float NearClip_m { get; set; }

    public float FarClip_m { get; set; }

    public bool HeadBob { get; set; }

    /// <summary>Share of the body's lean roll the camera follows (0 = stays level).</summary>
    public float LeanRoll { get; set; }

    public float HeadBobAmplitude_m { get; set; }

    public float HeadBobStride_m { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(FovMin_deg), FovMin_deg, 30, 150);
        v.InRange(nameof(FovMax_deg), FovMax_deg, FovMin_deg, 150);
        v.InRange(nameof(Fov_deg), Fov_deg, FovMin_deg, FovMax_deg);
        v.InRange(nameof(FovStep_deg), FovStep_deg, 0.5, 20);
        v.InRange(nameof(NearClip_m), NearClip_m, 0.005, 0.5);
        v.InRange(nameof(FarClip_m), FarClip_m, 50, 10000);
        v.InRange(nameof(LeanRoll), LeanRoll, 0, 1);
        v.InRange(nameof(HeadBobAmplitude_m), HeadBobAmplitude_m, 0, 0.1);
        v.InRange(nameof(HeadBobStride_m), HeadBobStride_m, 0.2, 5);
    }
}

public sealed class ViewModelDef : IValidatable
{
    /// <summary>Vertical FOV the marker is drawn with, independent of the camera FOV.</summary>
    public float Fov_deg { get; set; }

    /// <summary>Marker position in camera space: [right, up, forward] (m).</summary>
    public float[] Offset_m { get; set; } = System.Array.Empty<float>();

    /// <summary>Marker tilt: [pitch up, yaw left, roll] (degrees), e.g. to angle the barrel toward the crosshair.</summary>
    public float[] Rotation_deg { get; set; } = System.Array.Empty<float>();

    /// <summary>Barrel tip relative to the marker root: [right, up, forward] (m).</summary>
    public float[] MuzzleTip_m { get; set; } = System.Array.Empty<float>();

    public float KickBack_m { get; set; }

    public float KickRecover_s { get; set; }

    /// <summary>The colours of your gloves and sleeves.</summary>
    public string GloveColor { get; set; } = "";

    public string SleeveColor { get; set; } = "";

    public void Validate(Validator v)
    {
        foreach ((string name, string color) in new[] { (nameof(GloveColor), GloveColor), (nameof(SleeveColor), SleeveColor) })
        {
            if (!Godot.Color.HtmlIsValid(color))
            {
                v.Error(name, $"'{color}' is not a valid colour");
            }
        }

        v.InRange(nameof(Fov_deg), Fov_deg, 20, 120);
        v.Vector(nameof(Offset_m), Offset_m);
        v.Vector(nameof(Rotation_deg), Rotation_deg);
        v.Vector(nameof(MuzzleTip_m), MuzzleTip_m);
        v.InRange(nameof(KickBack_m), KickBack_m, 0, 0.2);
        v.InRange(nameof(KickRecover_s), KickRecover_s, 0.01, 2);
    }
}

public sealed class LookDef : IValidatable
{
    public float MouseSensitivity_degPerCount { get; set; }

    public float SensitivityStep { get; set; }

    public float StickSpeed_degps { get; set; }

    public float StickExponent { get; set; }

    public bool InvertY { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(MouseSensitivity_degPerCount), MouseSensitivity_degPerCount, 0.001, 2);
        v.InRange(nameof(SensitivityStep), SensitivityStep, 0.0005, 0.5);
        v.InRange(nameof(StickSpeed_degps), StickSpeed_degps, 10, 2000);
        v.InRange(nameof(StickExponent), StickExponent, 0.5, 5);
    }
}

public sealed class CrosshairDef : IValidatable
{
    public bool Enabled { get; set; }

    public float Size_px { get; set; }

    public float Gap_px { get; set; }

    public float Thickness_px { get; set; }

    public string Color { get; set; } = "";

    public void Validate(Validator v)
    {
        v.InRange(nameof(Size_px), Size_px, 1, 100);
        v.InRange(nameof(Gap_px), Gap_px, 0, 100);
        v.InRange(nameof(Thickness_px), Thickness_px, 0.5, 20);
        if (!Godot.Color.HtmlIsValid(Color))
        {
            v.Error(nameof(Color), $"'{Color}' is not a valid colour");
        }
    }
}

public sealed class ArcPreviewDef : IValidatable
{
    public bool EnabledOnStart { get; set; }

    public float MaxTime_s { get; set; }

    public float[] Probes_m { get; set; } = System.Array.Empty<float>();

    public void Validate(Validator v)
    {
        v.InRange(nameof(MaxTime_s), MaxTime_s, 0.1, 20);
        if (Probes_m.Length > 8)
        {
            v.Error(nameof(Probes_m), "at most 8 probe distances");
        }
    }
}

public sealed class AudioDef : IValidatable
{
    public int Voices { get; set; }

    public int MaxSoundsPerFrame { get; set; }

    public float Volume_db { get; set; }

    /// <summary>Shot pitch scale at an empty and a full tank (the sound tracks tank pressure).</summary>
    public float ShotPitchEmpty { get; set; }

    public float ShotPitchFull { get; set; }

    public float MaxDistance_m { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Voices), Voices, 1, 128);
        v.InRange(nameof(MaxSoundsPerFrame), MaxSoundsPerFrame, 1, 128);
        v.InRange(nameof(Volume_db), Volume_db, -60, 12);
        v.InRange(nameof(ShotPitchEmpty), ShotPitchEmpty, 0.25, 4);
        v.InRange(nameof(ShotPitchFull), ShotPitchFull, 0.25, 4);
        v.InRange(nameof(MaxDistance_m), MaxDistance_m, 1, 1000);
    }
}

/// <summary>Graphics quality presets (Low / Medium / High). The player's choice is saved in settings.</summary>
public sealed class GraphicsDef : IValidatable
{
    public string DefaultPreset { get; set; } = "";

    /// <summary>Whether v-sync starts on (players can turn it off).</summary>
    public bool Vsync { get; set; }

    /// <summary>Render scale choices; the first is the default.</summary>
    public float[] RenderScales { get; set; } = System.Array.Empty<float>();

    public float FsrSharpness { get; set; }

    public GraphicsPresetDef[] Presets { get; set; } = System.Array.Empty<GraphicsPresetDef>();

    public GraphicsPresetDef Find(string name)
    {
        foreach (GraphicsPresetDef p in Presets)
        {
            if (p.Name == name)
            {
                return p;
            }
        }

        foreach (GraphicsPresetDef p in Presets)
        {
            if (p.Name == DefaultPreset)
            {
                return p;
            }
        }

        return Presets[0];
    }

    public void Validate(Validator v)
    {
        if (Presets.Length == 0)
        {
            v.Error(nameof(Presets), "must list at least one preset");
        }

        if (RenderScales.Length == 0)
        {
            v.Error(nameof(RenderScales), "must list at least one scale");
        }

        foreach (float scale in RenderScales)
        {
            if (scale is < 0.25f or > 1f)
            {
                v.Error(nameof(RenderScales), $"{scale} is outside 0.25–1");
            }
        }

        v.InRange(nameof(FsrSharpness), FsrSharpness, 0, 2);

        bool found = false;
        for (int i = 0; i < Presets.Length; i++)
        {
            Presets[i].Validate(v.Item(nameof(Presets), i));
            found |= Presets[i].Name == DefaultPreset;
        }

        if (!found)
        {
            v.Error(nameof(DefaultPreset), $"no preset named '{DefaultPreset}'");
        }
    }
}

public sealed class GraphicsPresetDef : IValidatable
{
    public string Name { get; set; } = "";

    /// <summary>Screen-space ambient occlusion.</summary>
    public bool Ssao { get; set; }

    /// <summary>Screen-space indirect light (light bouncing off nearby surfaces).</summary>
    public bool Ssil { get; set; }

    /// <summary>Signed-distance-field global illumination (dynamic bounced light; the costliest).</summary>
    public bool Sdfgi { get; set; }

    public bool VolumetricFog { get; set; }

    /// <summary>Screen-space reflections (puddles, glass, metal).</summary>
    public bool Ssr { get; set; }

    public bool Glow { get; set; }

    /// <summary>Interior reflection probes that dim indoor ambient light per room.</summary>
    public bool AmbientProbes { get; set; }

    public int ShadowSize { get; set; }

    public float ShadowDistance_m { get; set; }

    /// <summary>
    /// 0 = off, 1 = 2×, 2 = 4×. With MSAA on, Godot doesn't give shaders the depth buffer the sunbeams
    /// need to stop at walls, so the beams turn off; the shipped presets use <see cref="ScreenAa"/> instead.
    /// </summary>
    public int Msaa { get; set; }

    /// <summary>Screen-space antialiasing: "none", "fxaa" (cheapest, softest) or "smaa" (sharper).</summary>
    public string ScreenAa { get; set; } = "";

    /// <summary>Share of the weeds drawn (0 = none, 1 = all).</summary>
    public float WeedDensity { get; set; }

    /// <summary>Weeds shrink into the ground over the last few metres before this distance.</summary>
    public float WeedDistance_m { get; set; }

    /// <summary>Strength of the sunbeams through windows and roof holes (0 = off).</summary>
    public float LightShafts { get; set; }

    /// <summary>Whether things lying on the ground (oil, puddles, litter…) are drawn.</summary>
    public bool GroundDetail { get; set; }

    /// <summary>Whether old paint from past games is drawn on walls, cover and the ground.</summary>
    public bool OldPaint { get; set; }

    /// <summary>Dust motes drifting in the sunbeams.</summary>
    public bool Dust { get; set; }

    /// <summary>Unshadowed fill lights inside windows and doors, and warm bounce light where sun patches hit the floor.</summary>
    public bool WindowLights { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Name), Name);
        v.InRange(nameof(ShadowSize), ShadowSize, 512, 16384);
        v.InRange(nameof(ShadowDistance_m), ShadowDistance_m, 10, 1000);
        v.InRange(nameof(Msaa), Msaa, 0, 2);
        if (ScreenAa is not ("none" or "fxaa" or "smaa"))
        {
            v.Error(nameof(ScreenAa), $"'{ScreenAa}' is not one of none, fxaa, smaa");
        }

        v.InRange(nameof(WeedDensity), WeedDensity, 0, 1);
        v.InRange(nameof(WeedDistance_m), WeedDistance_m, 5, 300);
        v.InRange(nameof(LightShafts), LightShafts, 0, 4);
    }
}

/// <summary>
/// Weeds and dry grass wherever rain falls (nothing overhead): on scrubland and dirt, in cracks in the
/// asphalt, along the foot of walls and around props. Placement is seeded by the level id, so it's the
/// same every run. Presentation only: paint and players pass through weeds.
/// </summary>
public sealed class WeedsDef : IValidatable
{
    /// <summary>Tufts per square metre by ground material id. Materials not listed grow none.</summary>
    public Dictionary<string, float> Density_perM2 { get; set; } = new();

    /// <summary>Grassland: grasses outnumber weeds here.</summary>
    public string[] GrassMaterials { get; set; } = System.Array.Empty<string>();

    /// <summary>On these materials weeds only grow in cracks.</summary>
    public string[] CrackMaterials { get; set; } = System.Array.Empty<string>();

    /// <summary>Spacing of the crack network, and how wide a crack is.</summary>
    public float CrackSpacing_m { get; set; }

    public float CrackWidth_m { get; set; }

    /// <summary>Tufts per metre along the foot of walls, columns and props on open ground, within this reach of them.</summary>
    public float EdgeDensity_perM { get; set; }

    public float EdgeReach_m { get; set; }

    /// <summary>Weeds grow in patches about this big, with this share of the ground left bare between them.</summary>
    public float ClumpSize_m { get; set; }

    public float Bare { get; set; }

    public float HeightMin_m { get; set; }

    public float HeightMax_m { get; set; }

    /// <summary>A tuft's width as a multiple of its height.</summary>
    public float Width { get; set; }

    /// <summary>Tufts are tinted with one of these, from straw to olive.</summary>
    public string[] Colors { get; set; } = System.Array.Empty<string>();

    /// <summary>How far tips sway, how fast, and the size of the gusts that roll across.</summary>
    public float WindSway_m { get; set; }

    public float WindSpeed { get; set; }

    public float GustSize_m { get; set; }

    /// <summary>No tufts this close to a pickup, so grass never hides a pod.</summary>
    public float PickupClearance_m { get; set; }

    /// <summary>Tufts are grouped in squares this big so whole groups can be culled.</summary>
    public float ChunkSize_m { get; set; }

    public void Validate(Validator v)
    {
        foreach ((string material, float density) in Density_perM2)
        {
            if (density < 0f || density > 20f)
            {
                v.Error(nameof(Density_perM2), $"'{material}': {density} is outside 0–20");
            }
        }

        v.InRange(nameof(CrackSpacing_m), CrackSpacing_m, 0.5, 50);
        v.InRange(nameof(CrackWidth_m), CrackWidth_m, 0.01, CrackSpacing_m);
        v.InRange(nameof(EdgeDensity_perM), EdgeDensity_perM, 0, 20);
        v.InRange(nameof(EdgeReach_m), EdgeReach_m, 0.05, 2);
        v.InRange(nameof(ClumpSize_m), ClumpSize_m, 0.5, 100);
        v.InRange(nameof(Bare), Bare, 0, 0.95);
        v.InRange(nameof(HeightMin_m), HeightMin_m, 0.02, 2);
        v.InRange(nameof(HeightMax_m), HeightMax_m, HeightMin_m, 2);
        v.InRange(nameof(Width), Width, 0.2, 4);
        v.InRange(nameof(WindSway_m), WindSway_m, 0, 0.5);
        v.InRange(nameof(WindSpeed), WindSpeed, 0, 10);
        v.InRange(nameof(GustSize_m), GustSize_m, 1, 200);
        v.InRange(nameof(PickupClearance_m), PickupClearance_m, 0, 5);
        v.InRange(nameof(ChunkSize_m), ChunkSize_m, 4, 64);
        if (Colors.Length == 0)
        {
            v.Error(nameof(Colors), "needs at least one colour");
        }

        foreach (string c in Colors)
        {
            if (!Godot.Color.HtmlIsValid(c))
            {
                v.Error(nameof(Colors), $"'{c}' is not a valid colour");
            }
        }
    }
}

/// <summary>
/// Sunbeams through windows, doors and holes in the roof, wherever the sun reaches into a roofed space.
/// Each beam is a box of light drawn by ray-marching through it, cut off by whatever's in front.
/// </summary>
public sealed class ShaftsDef : IValidatable
{
    /// <summary>Glow per metre of beam you look through, before the preset's strength.</summary>
    public float Density_perM { get; set; }

    /// <summary>How fast a beam fades with distance from its opening (per metre).</summary>
    public float Falloff_perM { get; set; }

    /// <summary>Width of a beam's soft edge at the opening, and how much it widens per metre.</summary>
    public float EdgeSoftness_m { get; set; }

    public float EdgeSpread { get; set; }

    /// <summary>How much drifting dust breaks a beam up (0 = even glow, 1 = patchy).</summary>
    public float Patchiness { get; set; }

    /// <summary>Size of the dust swirls, and how fast they drift.</summary>
    public float SwirlSize_m { get; set; }

    public float SwirlSpeed_mps { get; set; }

    /// <summary>Roof holes are large and let in a broad sheet of light: their beams get this share of the glow.</summary>
    public float RoofHoleFactor { get; set; }

    /// <summary>No beam is longer than this.</summary>
    public float MaxLength_m { get; set; }

    /// <summary>Openings the sun strikes at less than this angle get no beam.</summary>
    public float MinSunAngle_deg { get; set; }

    /// <summary>Beam glow within this distance of the camera fades out, so standing in a beam doesn't wash out the view.</summary>
    public float NearFade_m { get; set; }

    /// <summary>Beams fade out between these distances from the camera (they're unaffected by fog).</summary>
    public float FadeStart_m { get; set; }

    public float FadeEnd_m { get; set; }

    /// <summary>Samples per pixel along the view ray through a beam.</summary>
    public int Samples { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Density_perM), Density_perM, 0, 10);
        v.InRange(nameof(Falloff_perM), Falloff_perM, 0, 5);
        v.InRange(nameof(EdgeSoftness_m), EdgeSoftness_m, 0.001, 2);
        v.InRange(nameof(EdgeSpread), EdgeSpread, 0, 1);
        v.InRange(nameof(Patchiness), Patchiness, 0, 1);
        v.InRange(nameof(SwirlSize_m), SwirlSize_m, 0.05, 20);
        v.InRange(nameof(SwirlSpeed_mps), SwirlSpeed_mps, 0, 5);
        v.InRange(nameof(RoofHoleFactor), RoofHoleFactor, 0, 4);
        v.InRange(nameof(MaxLength_m), MaxLength_m, 1, 100);
        v.InRange(nameof(MinSunAngle_deg), MinSunAngle_deg, 0, 60);
        v.InRange(nameof(NearFade_m), NearFade_m, 0, 20);
        v.InRange(nameof(FadeStart_m), FadeStart_m, 0, 500);
        v.InRange(nameof(FadeEnd_m), FadeEnd_m, FadeStart_m + 1, 1000);
        v.InRange(nameof(Samples), Samples, 1, 32);
    }
}

/// <summary>Dust motes drifting in the sunbeams: only lit dust shows, so they live inside the beams.</summary>
public sealed class DustDef : IValidatable
{
    public float PerCubicMetre { get; set; }

    public int MaxPerBeam { get; set; }

    public float Size_m { get; set; }

    public float Lifetime_s { get; set; }

    public float Drift_mps { get; set; }

    public float Brightness { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(PerCubicMetre), PerCubicMetre, 0, 200);
        v.InRange(nameof(MaxPerBeam), MaxPerBeam, 0, 5000);
        v.InRange(nameof(Size_m), Size_m, 0.001, 0.2);
        v.InRange(nameof(Lifetime_s), Lifetime_s, 0.5, 60);
        v.InRange(nameof(Drift_mps), Drift_mps, 0, 2);
        v.InRange(nameof(Brightness), Brightness, 0, 20);
    }
}

/// <summary>
/// Daylight spilling in: an unshadowed spotlight just inside each window or door (neighbours on one
/// wall share one), and a warm bounce light where a sun patch lands on the floor. Stands in for
/// bounced light on presets without global illumination.
/// </summary>
public sealed class WindowLightDef : IValidatable
{
    /// <summary>Spotlight energy per square metre of opening, its colour, cone and reach.</summary>
    public float Energy_perM2 { get; set; }

    public float MaxEnergy { get; set; }

    public string Color { get; set; } = "";

    public float Angle_deg { get; set; }

    public float Range_m { get; set; }

    /// <summary>Openings on the same wall within this distance share one light.</summary>
    public float MergeDistance_m { get; set; }

    /// <summary>Bounce light per square metre of sunlit opening, its tint (times the sun's colour) and reach.</summary>
    public float BounceEnergy_perM2 { get; set; }

    public float MaxBounceEnergy { get; set; }

    public string BounceTint { get; set; } = "";

    public float BounceRange_m { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Energy_perM2), Energy_perM2, 0, 10);
        v.InRange(nameof(MaxEnergy), MaxEnergy, 0, 32);
        v.InRange(nameof(Angle_deg), Angle_deg, 5, 89);
        v.InRange(nameof(Range_m), Range_m, 0.5, 50);
        v.InRange(nameof(MergeDistance_m), MergeDistance_m, 0, 20);
        v.InRange(nameof(BounceEnergy_perM2), BounceEnergy_perM2, 0, 10);
        v.InRange(nameof(MaxBounceEnergy), MaxBounceEnergy, 0, 32);
        v.InRange(nameof(BounceRange_m), BounceRange_m, 0.5, 50);
        foreach ((string key, string value) in new[] { (nameof(Color), Color), (nameof(BounceTint), BounceTint) })
        {
            if (!Godot.Color.HtmlIsValid(value))
            {
                v.Error(key, $"'{value}' is not a valid colour");
            }
        }
    }
}

/// <summary>
/// How opponents are drawn: rigged models from the art pipeline, posed to the sim's hitbox rig. A
/// missing model falls back to drawing the hitboxes themselves.
/// </summary>
public sealed class CharactersDef : IValidatable
{
    /// <summary>Models, dealt to opponents in turn.</summary>
    public string[] Models { get; set; } = System.Array.Empty<string>();

    /// <summary>Tints for the models' colours, dealt in turn, so copies of one model differ.</summary>
    public string[] Tints { get; set; } = System.Array.Empty<string>();

    /// <summary>The team-colour armband on each upper arm.</summary>
    public float ArmbandRadius_m { get; set; }

    public float ArmbandWidth_m { get; set; }

    /// <summary>Steps while moving: stride length and how high a foot lifts.</summary>
    public float Stride_m { get; set; }

    public float StepLift_m { get; set; }

    /// <summary>Speed at which steps reach their full stride, and how long they take to get there from a standstill.</summary>
    public float FullStrideSpeed_mps { get; set; }

    public float StrideEase_s { get; set; }

    /// <summary>How far the hips bob down at each footfall.</summary>
    public float HipBob_m { get; set; }

    /// <summary>How far the hips drop for each metre the eye drops (crouching).</summary>
    public float HipDropPerEyeDrop { get; set; }

    /// <summary>Shares of the aim pitch the chest and the head take.</summary>
    public float ChestPitch { get; set; }

    public float HeadPitch { get; set; }

    /// <summary>Where the wrists sit on the marker: along it from the back (0) to the front (1), and below it.</summary>
    public float TriggerGrip { get; set; }

    public float SupportGrip { get; set; }

    public float GripDrop_m { get; set; }

    /// <summary>Movement clips shared by every model; without a walk, the steps above stand in.</summary>
    public CharacterClipsDef Clips { get; set; } = new();

    /// <summary>How long clips take to blend in and out, and between walk, run and crouch.</summary>
    public float ClipBlend_s { get; set; }

    /// <summary>How far the legs turn from the facing towards where the body is going.</summary>
    public float MaxLegYaw_deg { get; set; }

    public void Validate(Validator v)
    {
        foreach (string tint in Tints)
        {
            if (!Godot.Color.HtmlIsValid(tint))
            {
                v.Error(nameof(Tints), $"'{tint}' is not a valid colour");
            }
        }

        v.InRange(nameof(ArmbandRadius_m), ArmbandRadius_m, 0.01, 0.3);
        v.InRange(nameof(ArmbandWidth_m), ArmbandWidth_m, 0.01, 0.5);
        v.InRange(nameof(Stride_m), Stride_m, 0.1, 2);
        v.InRange(nameof(StepLift_m), StepLift_m, 0, 0.5);
        v.InRange(nameof(FullStrideSpeed_mps), FullStrideSpeed_mps, 0.1, 20);
        v.InRange(nameof(StrideEase_s), StrideEase_s, 0.01, 5);
        v.InRange(nameof(HipBob_m), HipBob_m, 0, 0.2);
        v.InRange(nameof(HipDropPerEyeDrop), HipDropPerEyeDrop, 0, 1.5);
        v.InRange(nameof(ChestPitch), ChestPitch, 0, 1);
        v.InRange(nameof(HeadPitch), HeadPitch, 0, 1);
        v.InRange(nameof(TriggerGrip), TriggerGrip, 0, 1);
        v.InRange(nameof(SupportGrip), SupportGrip, 0, 1);
        v.InRange(nameof(GripDrop_m), GripDrop_m, -0.3, 0.3);
        v.InRange(nameof(ClipBlend_s), ClipBlend_s, 0.02, 2);
        v.InRange(nameof(MaxLegYaw_deg), MaxLegYaw_deg, 0, 90);
    }
}

/// <summary>
/// Movement clips (GLBs from the art pipeline, each one in-place clip on the generator's rig), fitted
/// to every model by bone name. Any can be left out.
/// </summary>
public sealed class CharacterClipsDef
{
    [Optional]
    public string? Walk { get; set; }

    [Optional]
    public string? Run { get; set; }

    [Optional]
    public string? CrouchWalk { get; set; }
}

/// <summary>Sun, sky and atmosphere (spec mood: overcast late afternoon).</summary>
public sealed class LightingDef : IValidatable
{
    /// <summary>Sun height above the horizon.</summary>
    public float SunElevation_deg { get; set; }

    /// <summary>Compass bearing of the sun: 0 = north, 90 = east, 180 = south, 270 = west.</summary>
    public float SunAzimuth_deg { get; set; }

    public string SunColor { get; set; } = "";

    public float SunEnergy { get; set; }

    public float SunShadowBlur { get; set; }

    public string SkyTopColor { get; set; } = "";

    public string SkyHorizonColor { get; set; } = "";

    public string GroundColor { get; set; } = "";

    /// <summary>0 = clear sky, 1 = fully overcast.</summary>
    public float CloudCover { get; set; }

    public float AmbientEnergy { get; set; }

    public string FogColor { get; set; } = "";

    public float FogDensity { get; set; }

    public float Exposure { get; set; }

    public CloudsDef Clouds { get; set; } = new();

    public VolumetricFogDef VolumetricFog { get; set; } = new();

    public void Validate(Validator v)
    {
        Clouds.Validate(v.Scope(nameof(Clouds)));
        VolumetricFog.Validate(v.Scope(nameof(VolumetricFog)));
        v.InRange(nameof(SunElevation_deg), SunElevation_deg, 1, 89);
        v.InRange(nameof(SunAzimuth_deg), SunAzimuth_deg, 0, 360);
        v.InRange(nameof(SunEnergy), SunEnergy, 0, 16);
        v.InRange(nameof(SunShadowBlur), SunShadowBlur, 0, 8);
        v.InRange(nameof(CloudCover), CloudCover, 0, 1);
        v.InRange(nameof(AmbientEnergy), AmbientEnergy, 0, 16);
        v.InRange(nameof(FogDensity), FogDensity, 0, 0.1);
        v.InRange(nameof(Exposure), Exposure, 0.05, 16);
        foreach ((string key, string value) in new[]
                 {
                     (nameof(SunColor), SunColor), (nameof(SkyTopColor), SkyTopColor), (nameof(SkyHorizonColor), SkyHorizonColor),
                     (nameof(GroundColor), GroundColor), (nameof(FogColor), FogColor),
                 })
        {
            if (!Godot.Color.HtmlIsValid(value))
            {
                v.Error(key, $"'{value}' is not a valid colour");
            }
        }
    }
}

/// <summary>
/// The volumetric fog of the presets that turn it on (Ultra): how thick it is per metre, how far from the
/// camera it reaches, its colour (sRGB), and how much it veils the sky (0: not at all, so the sky overhead
/// stays clear and the depth fog alone hazes the horizon; 1: as much as anything that far away).
/// </summary>
public sealed class VolumetricFogDef : IValidatable
{
    public float Density { get; set; }

    public float Length_m { get; set; }

    public string Albedo { get; set; } = "";

    public float SkyAffect { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Density), Density, 0, 0.1);
        v.InRange(nameof(Length_m), Length_m, 8, 1024);
        TrainingGroundDef.Colour(v, nameof(Albedo), Albedo);
        v.InRange(nameof(SkyAffect), SkyAffect, 0, 1);
    }
}

/// <summary>
/// The deck of cloud drifting over the sky (game/world/CloudDeck.cs): its height, the size of its noise's
/// tile on it (bigger makes bigger clouds), the wind moving it ([x, z] m/s: +x east, +z south), how
/// opaque the thickest cloud is, its lit and shaded colours (sRGB), how much of the sky's own still
/// cloud cover stays showing behind it as a higher layer (0-1), and the heights in the sky (degrees above
/// the horizon) that layer fades out between, before it pinches to a smear overhead. How much sky it
/// covers is the lighting's cloudCover.
/// </summary>
public sealed class CloudsDef : IValidatable
{
    public float Height_m { get; set; }

    public float Tile_m { get; set; }

    public float[] Wind_mps { get; set; } = System.Array.Empty<float>();

    public float Opacity { get; set; }

    public string LitColor { get; set; } = "";

    public string ShadeColor { get; set; } = "";

    public float HighLayer { get; set; }

    public float[] HighLayerFade_deg { get; set; } = System.Array.Empty<float>();

    public void Validate(Validator v)
    {
        v.InRange(nameof(Height_m), Height_m, 200, 5000);
        v.InRange(nameof(Tile_m), Tile_m, 200, 20000);
        if (Wind_mps.Length != 2 || System.MathF.Abs(Wind_mps[0]) > 60f || System.MathF.Abs(Wind_mps[1]) > 60f)
        {
            v.Error(nameof(Wind_mps), "takes [x, z], each within ±60 m/s");
        }

        v.InRange(nameof(Opacity), Opacity, 0, 1);
        TrainingGroundDef.Colour(v, nameof(LitColor), LitColor);
        TrainingGroundDef.Colour(v, nameof(ShadeColor), ShadeColor);
        v.InRange(nameof(HighLayer), HighLayer, 0, 1);
        FlockDef.Pair(v, nameof(HighLayerFade_deg), HighLayerFade_deg, 0f, 90f);
    }
}

public sealed class InputDef : IValidatable
{
    public const string File = "input.jsonc";

    public float Deadzone { get; set; }

    public InputActionDef[] Actions { get; set; } = System.Array.Empty<InputActionDef>();

    public void Validate(Validator v)
    {
        v.InRange(nameof(Deadzone), Deadzone, 0, 0.95);
        for (int i = 0; i < Actions.Length; i++)
        {
            Actions[i].Validate(v.Item(nameof(Actions), i));
        }
    }
}

public sealed class InputActionDef : IValidatable
{
    public string Name { get; set; } = "";

    /// <summary>Godot key names (physical, layout-independent), e.g. "W", "Shift", "F1".</summary>
    [Optional]
    public string[]? Keys { get; set; }

    /// <summary>Mouse buttons: "Left", "Right", "Middle", "WheelUp"…</summary>
    [Optional]
    public string[]? Mouse { get; set; }

    /// <summary>Gamepad buttons (SDL layout, so Xbox and PlayStation both work): "A", "Y", "LeftStick", "DpadUp"…</summary>
    [Optional]
    public string[]? Buttons { get; set; }

    /// <summary>Gamepad axes with direction: "LeftY-", "TriggerRight+"…</summary>
    [Optional]
    public string[]? Axes { get; set; }

    [Optional]
    public float Deadzone { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Name), Name);
        v.InRange(nameof(Deadzone), Deadzone, 0, 0.95);
    }
}

#pragma warning restore CA1707

/// <summary>Distant tree lines around compound levels.</summary>
public sealed class HorizonDef : IValidatable
{
    public string Color { get; set; } = "";

    public HorizonRingDef[] Rings { get; set; } = System.Array.Empty<HorizonRingDef>();

    /// <summary>Buildings far off among the trees, and their colour (looks only).</summary>
    [Optional]
    public LandmarkDef[]? Landmarks { get; set; }

    [Optional]
    public string? LandmarkColor { get; set; }

    /// <summary>How the smoke off the chimneys marked "smoke" drifts and looks.</summary>
    [Optional]
    public SmokeDef? Smoke { get; set; }

    public void Validate(Validator v)
    {
        if (!Godot.Color.HtmlIsValid(Color))
        {
            v.Error(nameof(Color), $"'{Color}' is not a valid colour");
        }

        for (int i = 0; i < Rings.Length; i++)
        {
            Rings[i].Validate(v.Item(nameof(Rings), i));
        }

        for (int i = 0; Landmarks is not null && i < Landmarks.Length; i++)
        {
            Landmarks[i].Validate(v.Item(nameof(Landmarks), i));
        }

        Smoke?.Validate(v.Scope(nameof(Smoke)));
        if (LandmarkColor is not null && !Godot.Color.HtmlIsValid(LandmarkColor))
        {
            v.Error(nameof(LandmarkColor), $"'{LandmarkColor}' is not a valid colour");
        }
    }
}

/// <summary>
/// A building far off on the horizon, a silhouette in the fog: a chimney, a shed with a sawtooth roof,
/// a gasholder's frame or a block of flats, at a compass bearing (0 north, 90 east) and distance from
/// the place's middle, this tall and this wide.
/// </summary>
public sealed class LandmarkDef : IValidatable
{
    public static readonly string[] Kinds = { "chimney", "shed", "gasholder", "flats" };

    public string Kind { get; set; } = "";

    public float Bearing_deg { get; set; }

    public float Distance_m { get; set; }

    public float Height_m { get; set; }

    public float Width_m { get; set; }

    /// <summary>A chimney with smoke drifting off it (the horizon's "smoke").</summary>
    [Optional]
    public bool Smoke { get; set; }

    public void Validate(Validator v)
    {
        if (System.Array.IndexOf(Kinds, Kind) < 0)
        {
            v.Error(nameof(Kind), $"'{Kind}' must be one of {string.Join(", ", Kinds)}");
        }

        v.InRange(nameof(Bearing_deg), Bearing_deg, -360, 360);
        v.InRange(nameof(Distance_m), Distance_m, 100, 5000);
        v.InRange(nameof(Height_m), Height_m, 2, 300);
        v.InRange(nameof(Width_m), Width_m, 1, 400);
    }
}

/// <summary>
/// Smoke drifting off far chimneys (game/world/Horizon.cs): how fast it rises, the wind carrying it off
/// ([x, z] m/s: +x east, +z south), how long a puff lasts, how many are in the air, a puff's size leaving
/// the chimney and fading out (m), its colour (sRGB, as seen through the haze; it isn't fogged again) and
/// how opaque it is at most.
/// </summary>
public sealed class SmokeDef : IValidatable
{
    public float Rise_mps { get; set; }

    public float[] Wind_mps { get; set; } = System.Array.Empty<float>();

    public float Lifetime_s { get; set; }

    public int Puffs { get; set; }

    public float[] Size_m { get; set; } = System.Array.Empty<float>();

    public string Color { get; set; } = "";

    public float Opacity { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Rise_mps), Rise_mps, 0, 20);
        if (Wind_mps.Length != 2 || !float.IsFinite(Wind_mps[0]) || !float.IsFinite(Wind_mps[1]) || System.MathF.Abs(Wind_mps[0]) > 30f || System.MathF.Abs(Wind_mps[1]) > 30f)
        {
            v.Error(nameof(Wind_mps), "must be [x, z] within ±30 m/s");
        }

        v.InRange(nameof(Lifetime_s), Lifetime_s, 1, 300);
        v.InRange(nameof(Puffs), Puffs, 1, 400);
        FlockDef.Pair(v, nameof(Size_m), Size_m, 0.5f, 200f);
        TrainingGroundDef.Colour(v, nameof(Color), Color);
        v.InRange(nameof(Opacity), Opacity, 0, 1);
    }
}

public sealed class HorizonRingDef : IValidatable
{
    /// <summary>Distance from the level's centre.</summary>
    public float Radius_m { get; set; }

    public float MinHeight_m { get; set; }

    public float MaxHeight_m { get; set; }

    /// <summary>Share of the ring left as open country, 0..1.</summary>
    public float Gaps { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Radius_m), Radius_m, 100, 5000);
        v.InRange(nameof(MinHeight_m), MinHeight_m, 0.5, 200);
        v.InRange(nameof(MaxHeight_m), MaxHeight_m, MinHeight_m, 200);
        v.InRange(nameof(Gaps), Gaps, 0, 0.95);
    }
}

/// <summary>Paint on your mask after a break near your face (spec §1.3).</summary>
public sealed class MaskSprayViewDef : IValidatable
{
    /// <summary>How long the spray takes to clear.</summary>
    public float Duration_s { get; set; }

    /// <summary>Opacity of a full-strength spray (0..1).</summary>
    public float MaxOpacity { get; set; }

    /// <summary>Drops per spray at full strength.</summary>
    public int Drops { get; set; }

    /// <summary>Drop size as a share of the screen height.</summary>
    public float MinSize { get; set; }

    public float MaxSize { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Duration_s), Duration_s, 0.1, 20);
        v.InRange(nameof(MaxOpacity), MaxOpacity, 0, 1);
        v.InRange(nameof(Drops), Drops, 1, 32);
        v.InRange(nameof(MinSize), MinSize, 0.01, 2);
        v.InRange(nameof(MaxSize), MaxSize, MinSize, 2);
    }
}

/// <summary>The match HUD: top bar, kill feed, subtitles, pickup prompts, hit marker, callsigns and callouts.</summary>
public sealed class HudDef : IValidatable
{
    public float IconSize_px { get; set; }

    public int KillFeedLines { get; set; }

    public float KillFeedTime_s { get; set; }

    public float SubtitleTime_s { get; set; }

    public float SubtitleRange_m { get; set; }

    public float PickupPromptRange_m { get; set; }

    public float HitMarkerTime_s { get; set; }

    public string HitMarkerColor { get; set; } = "";

    public string[] Callsigns { get; set; } = System.Array.Empty<string>();

    public CalloutsDef Callouts { get; set; } = new();

    public void Validate(Validator v)
    {
        v.InRange(nameof(IconSize_px), IconSize_px, 6, 64);
        v.InRange(nameof(KillFeedLines), KillFeedLines, 1, 20);
        v.InRange(nameof(KillFeedTime_s), KillFeedTime_s, 0.5, 60);
        v.InRange(nameof(SubtitleTime_s), SubtitleTime_s, 0.5, 30);
        v.InRange(nameof(SubtitleRange_m), SubtitleRange_m, 0, 500);
        v.InRange(nameof(PickupPromptRange_m), PickupPromptRange_m, 0, 50);
        v.InRange(nameof(HitMarkerTime_s), HitMarkerTime_s, 0.05, 5);
        if (!Godot.Color.HtmlIsValid(HitMarkerColor))
        {
            v.Error(nameof(HitMarkerColor), $"'{HitMarkerColor}' is not a valid colour");
        }

        if (Callsigns.Length < 9 || Callsigns.Distinct().Count() != Callsigns.Length)
        {
            v.Error(nameof(Callsigns), "needs at least nine different callsigns (one per opponent)");
        }

        Callouts.Validate(v.Scope(nameof(Callouts)));
    }
}

/// <summary>Lines bots shout, by occasion (one is picked per call).</summary>
public sealed class CalloutsDef : IValidatable
{
    public string[] Spotted { get; set; } = System.Array.Empty<string>();

    public string[] Lost { get; set; } = System.Array.Empty<string>();

    public string[] UnderFire { get; set; } = System.Array.Empty<string>();

    public string[] Refill { get; set; } = System.Array.Empty<string>();

    public string[] Hit { get; set; } = System.Array.Empty<string>();

    public string[] For(Pb.Sim.AI.CalloutKind kind) => kind switch
    {
        Pb.Sim.AI.CalloutKind.Spotted => Spotted,
        Pb.Sim.AI.CalloutKind.Lost => Lost,
        Pb.Sim.AI.CalloutKind.UnderFire => UnderFire,
        Pb.Sim.AI.CalloutKind.Refill => Refill,
        Pb.Sim.AI.CalloutKind.Hit => Hit,
        _ => System.Array.Empty<string>(),
    };

    public void Validate(Validator v)
    {
        foreach ((string name, string[] lines) in new[] { (nameof(Spotted), Spotted), (nameof(Lost), Lost), (nameof(UnderFire), UnderFire), (nameof(Refill), Refill), (nameof(Hit), Hit) })
        {
            if (lines.Length == 0)
            {
                v.Error(name, "needs at least one line");
            }
        }
    }
}

/// <summary>The view after you're eliminated: who got you.</summary>
public sealed class SpectatorDef : IValidatable
{
    public float Duration_s { get; set; }

    /// <summary>Camera position: this far above your eye and this far back from the shooter's line.</summary>
    public float Height_m { get; set; }

    public float Back_m { get; set; }

    /// <summary>Following someone after you're out: this far behind and above their eyes.</summary>
    public float FollowBack_m { get; set; }

    public float FollowHeight_m { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Duration_s), Duration_s, 0.5, 30);
        v.InRange(nameof(Height_m), Height_m, 0, 10);
        v.InRange(nameof(Back_m), Back_m, 0, 20);
        v.InRange(nameof(FollowBack_m), FollowBack_m, 0.5, 20);
        v.InRange(nameof(FollowHeight_m), FollowHeight_m, -1, 10);
    }
}

/// <summary>
/// Things lying on the ground (game/world/GroundDetail.cs): oil stains, puddles, damp patches, rust
/// run-off, tyre tracks, drifts of leaves, litter and broken chips, painted at load into one atlas and
/// drawn as flat cards in one draw call. Placed at load, seeded by the level id, so the same every run.
/// Presentation only.
/// </summary>
public sealed class GroundDetailDef : IValidatable
{
    /// <summary>Cards fade out between these camera distances.</summary>
    public float FadeStart_m { get; set; }

    public float FadeEnd_m { get; set; }

    public GroundDetailKindDef[] Kinds { get; set; } = System.Array.Empty<GroundDetailKindDef>();

    public void Validate(Validator v)
    {
        v.InRange(nameof(FadeStart_m), FadeStart_m, 5, 500);
        v.InRange(nameof(FadeEnd_m), FadeEnd_m, FadeStart_m, 600);
        for (int i = 0; i < Kinds.Length; i++)
        {
            Kinds[i].Validate(v.Item(nameof(Kinds), i));
        }
    }
}

/// <summary>
/// Old paint from games played here before (game/world/OldPaint.cs): faded splats where people
/// shooting from one cover spot at another would have hit, on the cover and walls and the ground
/// short of them. Shots are cast at load from the bots' cover points at others facing them across
/// their cover, with a spread; seeded by the level id. Presentation only.
/// </summary>
public sealed class OldPaintDef : IValidatable
{
    /// <summary>How many splats to place (fewer if shots run out).</summary>
    public int Count { get; set; }

    /// <summary>Farthest a shot is taken from (m).</summary>
    public float Range_m { get; set; }

    /// <summary>How far a shot strays round its target, about (m).</summary>
    public float Spread_m { get; set; }

    /// <summary>Smallest and largest card (m); most are small.</summary>
    public float[] Size_m { get; set; } = System.Array.Empty<float>();

    /// <summary>The paint colours, before fading.</summary>
    public string[] Colors { get; set; } = System.Array.Empty<string>();

    /// <summary>How far each splat has faded towards a weathered grey (0 fresh, 1 gone), least and most.</summary>
    public float[] Fade { get; set; } = System.Array.Empty<float>();

    /// <summary>Least and most opacity.</summary>
    public float[] Opacity { get; set; } = System.Array.Empty<float>();

    /// <summary>
    /// Props paint shows on (kit/props.jsonc ids), each with how far its shape's faces sit inside its
    /// colliders (m), so the paint lies on the shape. Walls, columns and the ground always take paint.
    /// </summary>
    public System.Collections.Generic.Dictionary<string, float> Props { get; set; } = new();

    /// <summary>Cards fade out between these camera distances.</summary>
    public float FadeStart_m { get; set; }

    public float FadeEnd_m { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Count), Count, 0, 20000);
        v.InRange(nameof(Range_m), Range_m, 5, 200);
        v.InRange(nameof(Spread_m), Spread_m, 0, 5);
        Pair(v, nameof(Size_m), Size_m, 0.02f, 3f);
        Pair(v, nameof(Fade), Fade, 0f, 1f);
        Pair(v, nameof(Opacity), Opacity, 0f, 1f);
        if (Colors.Length == 0)
        {
            v.Error(nameof(Colors), "needs at least one colour");
        }

        foreach (string c in Colors)
        {
            if (!Godot.Color.HtmlIsValid(c))
            {
                v.Error(nameof(Colors), $"'{c}' is not a valid colour");
            }
        }

        foreach ((string prop, float inset) in Props)
        {
            if (inset < 0f || inset > 0.3f)
            {
                v.Error(nameof(Props), $"{prop}: inset must be in [0, 0.3] m (got {inset})");
            }
        }

        v.InRange(nameof(FadeStart_m), FadeStart_m, 5, 500);
        v.InRange(nameof(FadeEnd_m), FadeEnd_m, FadeStart_m, 600);
    }

    private static void Pair(Validator v, string name, float[] pair, float min, float max)
    {
        if (pair.Length != 2 || !(pair[0] >= min && pair[0] <= pair[1] && pair[1] <= max))
        {
            v.Error(name, $"must be [least, most] within [{min}, {max}]");
        }
    }
}

/// <summary>
/// Cracks in the asphalt and concrete (game/world/Cracks.cs), drawn along the network the weeds grow in
/// (weeds → crackMaterials and crackSpacing_m): the open crack's width (narrowest, widest), how far it
/// wanders either side of the network, how open the cracks are on each crack material (a multiple, 1 if
/// not listed), the grimy band either side (half-width, how dark, colour), the crack's own darkness and
/// colour, how much lighter its broken edge is, the spurs branching off (per metre of crack, length), the
/// size of the patches where cracks gape or close to hairlines, the share of the network drawn at all,
/// and the distances they fade out over.
/// </summary>
public sealed class CracksDef : IValidatable
{
    public float[] Width_m { get; set; } = System.Array.Empty<float>();

    public float Wander_m { get; set; }

    public Dictionary<string, float> Open { get; set; } = new();

    public float Halo_m { get; set; }

    public float HaloDarkness { get; set; }

    public string HaloColor { get; set; } = "";

    public float Darkness { get; set; }

    public string Color { get; set; } = "";

    public float Rim { get; set; }

    public float Spurs_perM { get; set; }

    public float[] SpurLength_m { get; set; } = System.Array.Empty<float>();

    public float PatchSize_m { get; set; }

    public float Drawn { get; set; }

    public AlligatorDef Alligator { get; set; } = new();

    public float FadeStart_m { get; set; }

    public float FadeEnd_m { get; set; }

    public void Validate(Validator v)
    {
        FlockDef.Pair(v, nameof(Width_m), Width_m, 0.001f, 0.2f);
        v.InRange(nameof(Wander_m), Wander_m, 0, 0.5);
        foreach ((string id, float k) in Open)
        {
            if (!(k >= 0f && k <= 4f))
            {
                v.Error(nameof(Open), $"'{id}' must be within [0, 4]");
            }
        }

        v.InRange(nameof(Halo_m), Halo_m, 0.01, 0.5);
        v.InRange(nameof(HaloDarkness), HaloDarkness, 0, 1);
        TrainingGroundDef.Colour(v, nameof(HaloColor), HaloColor);
        v.InRange(nameof(Darkness), Darkness, 0, 1);
        TrainingGroundDef.Colour(v, nameof(Color), Color);
        v.InRange(nameof(Rim), Rim, 0, 1);
        v.InRange(nameof(Spurs_perM), Spurs_perM, 0, 5);
        FlockDef.Pair(v, nameof(SpurLength_m), SpurLength_m, 0.05f, 5f);
        v.InRange(nameof(PatchSize_m), PatchSize_m, 1, 200);
        v.InRange(nameof(Drawn), Drawn, 0, 1);
        Alligator.Validate(v.Scope(nameof(Alligator)));
        v.InRange(nameof(FadeStart_m), FadeStart_m, 2, 300);
        v.InRange(nameof(FadeEnd_m), FadeEnd_m, FadeStart_m, 400);
    }
}

/// <summary>
/// Damp (game/world/Damp.cs): the chance a length of wall standing on the ground (its foot below
/// risingBelow_m) has rising damp indoors and how high its tide mark climbs (least, most); the chance an
/// outside one has grime splashed up its foot, how high (least, most), and the colour multiplied into it
/// on the shady faces looking north (algae); the chance each top
/// corner of a wall as tall as a room (mouldWalls_m: least, most) has mould, and its size; water stains
/// per square metre of ceiling, their size, and the highest ceiling above a floor that gets them; how
/// opaque they all are (least, most); and the distances they fade out over.
/// </summary>
public sealed class DampDef : IValidatable
{
    public float Rising { get; set; }

    public float RisingBelow_m { get; set; }

    public float[] RisingHeight_m { get; set; } = System.Array.Empty<float>();

    public float Splash { get; set; }

    public float[] SplashHeight_m { get; set; } = System.Array.Empty<float>();

    public string SplashAlgae { get; set; } = "";

    public float Mould { get; set; }

    public float[] MouldSize_m { get; set; } = System.Array.Empty<float>();

    public float[] MouldWalls_m { get; set; } = System.Array.Empty<float>();

    public float Stains_perM2 { get; set; }

    public float[] StainSize_m { get; set; } = System.Array.Empty<float>();

    public float CeilingMaxHeight_m { get; set; }

    public float[] Opacity { get; set; } = System.Array.Empty<float>();

    public float FadeStart_m { get; set; }

    public float FadeEnd_m { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Rising), Rising, 0, 1);
        v.InRange(nameof(RisingBelow_m), RisingBelow_m, 0, 10);
        FlockDef.Pair(v, nameof(RisingHeight_m), RisingHeight_m, 0.1f, 3f);
        v.InRange(nameof(Splash), Splash, 0, 1);
        FlockDef.Pair(v, nameof(SplashHeight_m), SplashHeight_m, 0.05f, 2f);
        TrainingGroundDef.Colour(v, nameof(SplashAlgae), SplashAlgae);
        v.InRange(nameof(Mould), Mould, 0, 1);
        FlockDef.Pair(v, nameof(MouldSize_m), MouldSize_m, 0.1f, 4f);
        FlockDef.Pair(v, nameof(MouldWalls_m), MouldWalls_m, 0.5f, 20f);
        v.InRange(nameof(Stains_perM2), Stains_perM2, 0, 1);
        FlockDef.Pair(v, nameof(StainSize_m), StainSize_m, 0.1f, 6f);
        v.InRange(nameof(CeilingMaxHeight_m), CeilingMaxHeight_m, 1, 30);
        FlockDef.Pair(v, nameof(Opacity), Opacity, 0f, 1f);
        v.InRange(nameof(FadeStart_m), FadeStart_m, 2, 300);
        v.InRange(nameof(FadeEnd_m), FadeEnd_m, FadeStart_m, 400);
    }
}

/// <summary>
/// Paths worn across the soft ground (game/world/WornPaths.cs): the ground materials they show on, the
/// trodden width, how far they wander from a straight line, the longest a path between two doorways or
/// gaps may run, the size of the squares the cover on soft ground is gathered in (each square's middle is
/// somewhere a path leads), the dusty colour (sRGB) and how strongly it shows, how far either side of the
/// middle no weeds grow, and the distances they fade out over.
/// </summary>
public sealed class WornPathsDef : IValidatable
{
    public string[] On { get; set; } = System.Array.Empty<string>();

    public float Width_m { get; set; }

    public float Wander_m { get; set; }

    public float Longest_m { get; set; }

    public float HubSpacing_m { get; set; }

    public string Color { get; set; } = "";

    public float Opacity { get; set; }

    public float WeedFree_m { get; set; }

    public float FadeStart_m { get; set; }

    public float FadeEnd_m { get; set; }

    public void Validate(Validator v)
    {
        if (On.Length == 0)
        {
            v.Error(nameof(On), "needs at least one ground material");
        }

        v.InRange(nameof(Width_m), Width_m, 0.2, 4);
        v.InRange(nameof(Wander_m), Wander_m, 0, 10);
        v.InRange(nameof(Longest_m), Longest_m, 2, 500);
        v.InRange(nameof(HubSpacing_m), HubSpacing_m, 2, 100);
        TrainingGroundDef.Colour(v, nameof(Color), Color);
        v.InRange(nameof(Opacity), Opacity, 0, 1);
        v.InRange(nameof(WeedFree_m), WeedFree_m, 0, 2);
        v.InRange(nameof(FadeStart_m), FadeStart_m, 2, 300);
        v.InRange(nameof(FadeEnd_m), FadeEnd_m, FadeStart_m, 400);
    }
}

/// <summary>
/// Graffiti on the outside walls (game/world/Graffiti.cs): the words written (invented, capital letters A–Z,
/// 2–8 of them), the fill and outline colours (sRGB) the pieces are painted from, how many throw-ups and
/// how wide (least, most), how many tags and how wide, how opaque (least, most), how worn (0 none to 1
/// gone; least, most), how far the sun has bleached them towards grey, and the distances they fade over.
/// </summary>
public sealed class GraffitiDef : IValidatable
{
    public string[] Words { get; set; } = System.Array.Empty<string>();

    public string[] Fills { get; set; } = System.Array.Empty<string>();

    public string[] Outlines { get; set; } = System.Array.Empty<string>();

    public int ThrowUps { get; set; }

    public float[] ThrowUpWidth_m { get; set; } = System.Array.Empty<float>();

    public int Tags { get; set; }

    public float[] TagWidth_m { get; set; } = System.Array.Empty<float>();

    public float[] Opacity { get; set; } = System.Array.Empty<float>();

    public float[] Wear { get; set; } = System.Array.Empty<float>();

    public float Bleach { get; set; }

    public float FadeStart_m { get; set; }

    public float FadeEnd_m { get; set; }

    public void Validate(Validator v)
    {
        if (Words.Length == 0)
        {
            v.Error(nameof(Words), "needs at least one word");
        }

        foreach (string word in Words)
        {
            if (word.Length is < 2 or > 8 || !word.All(c => c is >= 'A' and <= 'Z'))
            {
                v.Error(nameof(Words), $"'{word}' must be 2–8 capital letters A–Z");
            }
        }

        foreach ((string key, string[] colours) in new[] { (nameof(Fills), Fills), (nameof(Outlines), Outlines) })
        {
            if (colours.Length == 0)
            {
                v.Error(key, "needs at least one colour");
            }

            foreach (string c in colours)
            {
                TrainingGroundDef.Colour(v, key, c);
            }
        }

        v.InRange(nameof(ThrowUps), ThrowUps, 0, 100);
        FlockDef.Pair(v, nameof(ThrowUpWidth_m), ThrowUpWidth_m, 0.5f, 8f);
        v.InRange(nameof(Tags), Tags, 0, 200);
        FlockDef.Pair(v, nameof(TagWidth_m), TagWidth_m, 0.3f, 4f);
        FlockDef.Pair(v, nameof(Opacity), Opacity, 0f, 1f);
        FlockDef.Pair(v, nameof(Wear), Wear, 0f, 1f);
        v.InRange(nameof(Bleach), Bleach, 0, 1);
        v.InRange(nameof(FadeStart_m), FadeStart_m, 2, 300);
        v.InRange(nameof(FadeEnd_m), FadeEnd_m, FadeStart_m, 400);
    }
}

/// <summary>
/// Plastic bags caught on the walls' barbed wire (game/world/SnaggedBags.cs): how many (at most one on
/// each strand between two brackets), how long they hang (least, most; never below the top of the wall),
/// their colours (sRGB, darkened a little at random), the angles the ground wind holds them out at (rad,
/// in a lull and in a gust), how far ripples flap their free end, and how often.
/// </summary>
public sealed class SnaggedBagsDef : IValidatable
{
    public int Count { get; set; }

    public float[] Size_m { get; set; } = System.Array.Empty<float>();

    public string[] Colors { get; set; } = System.Array.Empty<string>();

    public float[] Lean_rad { get; set; } = System.Array.Empty<float>();

    public float Flap_m { get; set; }

    public float Flutter_hz { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Count), Count, 0, 200);
        FlockDef.Pair(v, nameof(Size_m), Size_m, 0.1f, 1f);
        if (Colors.Length == 0)
        {
            v.Error(nameof(Colors), "needs at least one colour");
        }

        foreach (string c in Colors)
        {
            TrainingGroundDef.Colour(v, nameof(Colors), c);
        }

        FlockDef.Pair(v, nameof(Lean_rad), Lean_rad, 0f, 1.3f);
        v.InRange(nameof(Flap_m), Flap_m, 0, 0.3);
        v.InRange(nameof(Flutter_hz), Flutter_hz, 0, 5);
    }
}

/// <summary>
/// Litter blowing about the yard (game/world/BlowingLitter.cs): how many pieces; how strong a gust (0-1)
/// it takes to lift one (least, most); the share of the ground wind it skitters off with (least, most);
/// how long one stays pinned against something before it can be put back out on open ground; how far
/// from the camera (or behind it) that can happen unseen; and the kinds of piece.
/// </summary>
public sealed class BlowingLitterDef : IValidatable
{
    public int Count { get; set; }

    public float[] Lift { get; set; } = System.Array.Empty<float>();

    public float[] Share { get; set; } = System.Array.Empty<float>();

    public float Recycle_s { get; set; }

    public float Hidden_m { get; set; }

    public LitterKindDef[] Kinds { get; set; } = System.Array.Empty<LitterKindDef>();

    public void Validate(Validator v)
    {
        v.InRange(nameof(Count), Count, 0, 500);
        FlockDef.Pair(v, nameof(Lift), Lift, 0f, 1f);
        FlockDef.Pair(v, nameof(Share), Share, 0f, 2f);
        v.InRange(nameof(Recycle_s), Recycle_s, 0, 600);
        v.InRange(nameof(Hidden_m), Hidden_m, 1, 200);
        if (Kinds.Length == 0)
        {
            v.Error(nameof(Kinds), "needs at least one kind");
        }

        for (int i = 0; i < Kinds.Length; i++)
        {
            Kinds[i].Validate(v.Item(nameof(Kinds), i));
        }
    }
}

/// <summary>
/// One kind of blowing litter: which (leaf, paper or wrapper, for its shape), how often it's picked
/// against the others, its size (least, most), its colours (sRGB), how high it hops as it goes, and how
/// fast it tumbles over and spins round per metre blown (rad; paper that doesn't tumble slides, lifting
/// at its edges).
/// </summary>
public sealed class LitterKindDef : IValidatable
{
    public string Kind { get; set; } = "";

    public float Weight { get; set; }

    public float[] Size_m { get; set; } = System.Array.Empty<float>();

    public string[] Colors { get; set; } = System.Array.Empty<string>();

    public float Hop_m { get; set; }

    public float Tumble_radPerM { get; set; }

    public float Spin_radPerM { get; set; }

    public void Validate(Validator v)
    {
        if (Kind is not ("leaf" or "paper" or "wrapper"))
        {
            v.Error(nameof(Kind), $"'{Kind}' isn't one of leaf, paper, wrapper");
        }

        v.InRange(nameof(Weight), Weight, 0.001, 100);
        FlockDef.Pair(v, nameof(Size_m), Size_m, 0.02f, 1f);
        if (Colors.Length == 0)
        {
            v.Error(nameof(Colors), "needs at least one colour");
        }

        foreach (string c in Colors)
        {
            TrainingGroundDef.Colour(v, nameof(Colors), c);
        }

        v.InRange(nameof(Hop_m), Hop_m, 0, 1);
        v.InRange(nameof(Tumble_radPerM), Tumble_radPerM, 0, 30);
        v.InRange(nameof(Spin_radPerM), Spin_radPerM, 0, 30);
    }
}

/// <summary>
/// Dust kicked up underfoot (game/world/FootDust.cs): its colour (sRGB) on each surface that has any
/// (break_model.jsonc names; elsewhere feet raise none); a step's puff size from a walk's to a sprint's
/// (crouched steps raise none), a landing's (a ring of five), a jump's and a slide's, laid every so many
/// metres along it, and a ball's breaking or bouncing on the ground; how long a puff lasts, how many times its size it grows to, how fast it rises, the
/// share of the ground wind it drifts with, how thick it starts (0-1), how many can be up at once, and how
/// far from the camera any are raised.
/// </summary>
public sealed class FootDustDef : IValidatable
{
    public Dictionary<string, string> Colors { get; set; } = new();

    public float[] StepSize_m { get; set; } = System.Array.Empty<float>();

    public float LandSize_m { get; set; }

    public float JumpSize_m { get; set; }

    public float SlideSize_m { get; set; }

    public float SlideEvery_m { get; set; }

    public float BreakSize_m { get; set; }

    public float BounceSize_m { get; set; }

    public float Lifetime_s { get; set; }

    public float Grow { get; set; }

    public float Rise_mps { get; set; }

    public float WindShare { get; set; }

    public float Opacity { get; set; }

    public int Max { get; set; }

    public float Reach_m { get; set; }

    public void Validate(Validator v)
    {
        foreach ((string surface, string colour) in Colors)
        {
            TrainingGroundDef.Colour(v, $"{nameof(Colors)}.{surface}", colour);
        }

        FlockDef.Pair(v, nameof(StepSize_m), StepSize_m, 0.02f, 3f);
        v.InRange(nameof(LandSize_m), LandSize_m, 0.02, 3);
        v.InRange(nameof(JumpSize_m), JumpSize_m, 0.02, 3);
        v.InRange(nameof(SlideSize_m), SlideSize_m, 0.02, 3);
        v.InRange(nameof(SlideEvery_m), SlideEvery_m, 0.05, 5);
        v.InRange(nameof(BreakSize_m), BreakSize_m, 0, 3);
        v.InRange(nameof(BounceSize_m), BounceSize_m, 0, 3);
        v.InRange(nameof(Lifetime_s), Lifetime_s, 0.1, 10);
        v.InRange(nameof(Grow), Grow, 0.5, 6);
        v.InRange(nameof(Rise_mps), Rise_mps, 0, 3);
        v.InRange(nameof(WindShare), WindShare, 0, 1);
        v.InRange(nameof(Opacity), Opacity, 0, 1);
        v.InRange(nameof(Max), Max, 1, 4096);
        v.InRange(nameof(Reach_m), Reach_m, 1, 500);
    }
}

/// <summary>
/// Patches of alligator cracking, where asphalt has broken up into small pieces: how many, how far each
/// reaches (least, most), and how big the pieces are.
/// </summary>
public sealed class AlligatorDef : IValidatable
{
    public int Patches { get; set; }

    public float[] Radius_m { get; set; } = System.Array.Empty<float>();

    public float Spacing_m { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Patches), Patches, 0, 100);
        FlockDef.Pair(v, nameof(Radius_m), Radius_m, 0.3f, 10f);
        v.InRange(nameof(Spacing_m), Spacing_m, 0.1, 2);
    }
}

/// <summary>
/// Soft contact shadows round the foot of walls and columns and of props (game/world/ContactShadows.cs), on
/// presets without SSAO: how dark they are against the footprint and how far they reach out from it, for
/// each; pieces shorter than minHeight_m get none; they fade out between the two distances.
/// </summary>
public sealed class ContactShadowsDef : IValidatable
{
    public float WallStrength { get; set; }

    public float WallReach_m { get; set; }

    public float PropStrength { get; set; }

    public float PropReach_m { get; set; }

    public float MinHeight_m { get; set; }

    public float FadeStart_m { get; set; }

    public float FadeEnd_m { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(WallStrength), WallStrength, 0, 1);
        v.InRange(nameof(WallReach_m), WallReach_m, 0.05, 3);
        v.InRange(nameof(PropStrength), PropStrength, 0, 1);
        v.InRange(nameof(PropReach_m), PropReach_m, 0.05, 3);
        v.InRange(nameof(MinHeight_m), MinHeight_m, 0, 3);
        v.InRange(nameof(FadeStart_m), FadeStart_m, 2, 300);
        v.InRange(nameof(FadeEnd_m), FadeEnd_m, FadeStart_m, 400);
    }
}

/// <summary>
/// Things hanging on the inside walls of the buildings with finished interiors (game/world/WallHangings.cs):
/// the chance a length of wall gets one, the kinds (noticeboard, whiteboard, clock, planner, exitSign,
/// picture, hazardSign, sitePlan) with how often each comes up, where their middles hang above the floor
/// (most at eye level, clocks and exit signs high), how tall a piece of wall must be to take one, how far
/// they keep from its ends, how far they stand off it, and the share that hang crooked.
/// </summary>
public sealed class WallHangingsDef : IValidatable
{
    public float Chance { get; set; }

    public System.Collections.Generic.Dictionary<string, float> Kinds { get; set; } = new();

    public float EyeCentre_m { get; set; }

    public float HighCentre_m { get; set; }

    public float MinWallHeight_m { get; set; }

    public float EndClearance_m { get; set; }

    public float StandOff_m { get; set; }

    public float Crooked { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Chance), Chance, 0, 1);
        foreach ((string kind, float weight) in Kinds)
        {
            if (!System.Enum.TryParse<Pb.Game.World.HangingKind>(kind, true, out _))
            {
                v.Error(nameof(Kinds), $"'{kind}' is not one of {string.Join(", ", System.Enum.GetNames<Pb.Game.World.HangingKind>()).ToLowerInvariant()}");
            }

            if (weight < 0f)
            {
                v.Error(nameof(Kinds), $"{kind}: weight can't be negative (got {weight})");
            }
        }

        v.InRange(nameof(EyeCentre_m), EyeCentre_m, 0.5, 3);
        v.InRange(nameof(HighCentre_m), HighCentre_m, 0.5, 4);
        v.InRange(nameof(MinWallHeight_m), MinWallHeight_m, 0.5, 10);
        v.InRange(nameof(EndClearance_m), EndClearance_m, 0, 2);
        v.InRange(nameof(StandOff_m), StandOff_m, 0.001, 0.1);
        v.InRange(nameof(Crooked), Crooked, 0, 1);
    }
}

/// <summary>
/// Cast-iron fittings set into the yard (game/world/YardFittings.cs): how many manhole covers and their
/// radius (least, most), how many drain gratings and their length (least, most) and the share of them
/// at the foot of the buildings' outside walls, the ground materials they're set in, and the kit
/// material they're cast in.
/// </summary>
public sealed class YardFittingsDef : IValidatable
{
    public int Manholes { get; set; }

    public float[] ManholeRadius_m { get; set; } = System.Array.Empty<float>();

    public int Drains { get; set; }

    public float[] DrainSize_m { get; set; } = System.Array.Empty<float>();

    public float AlongWalls { get; set; }

    public string[] On { get; set; } = System.Array.Empty<string>();

    public string Material { get; set; } = "";

    public void Validate(Validator v)
    {
        v.InRange(nameof(Manholes), Manholes, 0, 200);
        FlockDef.Pair(v, nameof(ManholeRadius_m), ManholeRadius_m, 0.15f, 1f);
        v.InRange(nameof(Drains), Drains, 0, 200);
        FlockDef.Pair(v, nameof(DrainSize_m), DrainSize_m, 0.2f, 1.5f);
        v.InRange(nameof(AlongWalls), AlongWalls, 0, 1);
        if (On.Length == 0)
        {
            v.Error(nameof(On), "needs at least one ground material");
        }

        if (string.IsNullOrWhiteSpace(Material))
        {
            v.Error(nameof(Material), "needs a kit material id");
        }
    }
}

/// <summary>
/// Trees out beyond the levels and the training ground (game/world/Woods.cs): how many copses, how many
/// trees in each (least, most) and how far they spread from its middle, how far the nearest stand from
/// the place and the farthest reach beyond that, how far they keep from power and pole lines, how tall
/// the trees grow (least, most), the leaves' colours (a copse takes one, a few trees another), the bark's,
/// and how far the leaves sway.
/// </summary>
public sealed class WoodsDef : IValidatable
{
    public int Copses { get; set; }

    public int[] TreesPerCopse { get; set; } = System.Array.Empty<int>();

    public float[] Spread_m { get; set; } = System.Array.Empty<float>();

    public float Clearance_m { get; set; }

    public float Reach_m { get; set; }

    public float LineClearance_m { get; set; }

    public float[] Height_m { get; set; } = System.Array.Empty<float>();

    public string[] LeafColors { get; set; } = System.Array.Empty<string>();

    public string BarkColor { get; set; } = "";

    public float Sway_m { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Copses), Copses, 0, 100);
        if (TreesPerCopse.Length != 2 || TreesPerCopse[0] < 1 || TreesPerCopse[1] < TreesPerCopse[0] || TreesPerCopse[1] > 40)
        {
            v.Error(nameof(TreesPerCopse), "takes [least, most], 1-40");
        }

        FlockDef.Pair(v, nameof(Spread_m), Spread_m, 1f, 100f);
        v.InRange(nameof(Clearance_m), Clearance_m, 0, 500);
        v.InRange(nameof(Reach_m), Reach_m, 10, 1000);
        v.InRange(nameof(LineClearance_m), LineClearance_m, 0, 100);
        FlockDef.Pair(v, nameof(Height_m), Height_m, 2f, 40f);
        if (LeafColors.Length == 0)
        {
            v.Error(nameof(LeafColors), "needs at least one colour");
        }

        foreach (string c in LeafColors)
        {
            TrainingGroundDef.Colour(v, nameof(LeafColors), c);
        }

        TrainingGroundDef.Colour(v, nameof(BarkColor), BarkColor);
        v.InRange(nameof(Sway_m), Sway_m, 0, 2);
    }
}

/// <summary>
/// Debris on the floors indoors (game/world/FloorDebris.cs): its kinds, and where they all fade out.
/// </summary>
public sealed class FloorDebrisDef : IValidatable
{
    public FloorDebrisKindDef[] Kinds { get; set; } = System.Array.Empty<FloorDebrisKindDef>();

    public float FadeStart_m { get; set; }

    public float FadeEnd_m { get; set; }

    public void Validate(Validator v)
    {
        for (int i = 0; i < Kinds.Length; i++)
        {
            Kinds[i].Validate(v.Scope($"{nameof(Kinds)}[{i}]"));
        }

        v.InRange(nameof(FadeStart_m), FadeStart_m, 2, 200);
        v.InRange(nameof(FadeEnd_m), FadeEnd_m, FadeStart_m, 300);
    }
}

/// <summary>
/// One kind of floor debris: papers, plaster, glass, dust or leaves; how many per square metre of
/// indoor floor and the share of those along the foot of the walls, and how many under each window
/// on each side (inside on the floor, outside on the ground); its size (least, most; m), colour (sRGB),
/// opacity and gloss (0 matte, 1 like glass).
/// </summary>
public sealed class FloorDebrisKindDef : IValidatable
{
    public string Kind { get; set; } = "";

    [Optional]
    public float PerSquareMetre { get; set; }

    [Optional]
    public float EdgeShare { get; set; }

    [Optional]
    public float PerWindow { get; set; }

    public float[] Size_m { get; set; } = System.Array.Empty<float>();

    public string Color { get; set; } = "";

    [Optional]
    public float Opacity { get; set; } = 1f;

    [Optional]
    public float Gloss { get; set; }

    public void Validate(Validator v)
    {
        if (!System.Enum.TryParse<Pb.Game.World.DebrisKind>(Kind, true, out _))
        {
            v.Error(nameof(Kind), $"'{Kind}' is not one of {string.Join(", ", System.Enum.GetNames<Pb.Game.World.DebrisKind>()).ToLowerInvariant()}");
        }

        v.InRange(nameof(PerSquareMetre), PerSquareMetre, 0, 2);
        v.InRange(nameof(EdgeShare), EdgeShare, 0, 1);
        v.InRange(nameof(PerWindow), PerWindow, 0, 10);
        FlockDef.Pair(v, nameof(Size_m), Size_m, 0.05f, 3f);
        TrainingGroundDef.Colour(v, nameof(Color), Color);
        v.InRange(nameof(Opacity), Opacity, 0, 1);
        v.InRange(nameof(Gloss), Gloss, 0, 1);
    }
}

/// <summary>
/// Run-off streaks down the outside walls (game/world/RunOff.cs), from under the windows' sills, the
/// fittings, the downpipes' collars and the wire's brackets, and along the gutters and copings: the
/// grime and rust colours (sRGB), how much rust each kit material sheds (0 grime only, 1 all rust;
/// materials not listed shed grime), the chance a source leaves a streak, how far streaks run from
/// sills, from fittings and from lines, how wide and how far apart they are along a line, least and
/// most opacity, and where they fade out.
/// </summary>
public sealed class RunOffDef : IValidatable
{
    public string Grime { get; set; } = "";

    public string Rust { get; set; } = "";

    public System.Collections.Generic.Dictionary<string, float> RustFrom { get; set; } = new();

    public float Chance { get; set; }

    public float[] SillLength_m { get; set; } = System.Array.Empty<float>();

    public float[] FittingLength_m { get; set; } = System.Array.Empty<float>();

    public float[] LineLength_m { get; set; } = System.Array.Empty<float>();

    public float[] LineWidth_m { get; set; } = System.Array.Empty<float>();

    public float[] LineSpacing_m { get; set; } = System.Array.Empty<float>();

    public float[] Opacity { get; set; } = System.Array.Empty<float>();

    public float FadeStart_m { get; set; }

    public float FadeEnd_m { get; set; }

    public void Validate(Validator v)
    {
        TrainingGroundDef.Colour(v, nameof(Grime), Grime);
        TrainingGroundDef.Colour(v, nameof(Rust), Rust);
        foreach ((string material, float share) in RustFrom)
        {
            if (share is < 0f or > 1f)
            {
                v.Error(nameof(RustFrom), $"{material}: share must be in [0, 1] (got {share})");
            }
        }

        v.InRange(nameof(Chance), Chance, 0, 1);
        FlockDef.Pair(v, nameof(SillLength_m), SillLength_m, 0.1f, 6f);
        FlockDef.Pair(v, nameof(FittingLength_m), FittingLength_m, 0.1f, 6f);
        FlockDef.Pair(v, nameof(LineLength_m), LineLength_m, 0.1f, 6f);
        FlockDef.Pair(v, nameof(LineWidth_m), LineWidth_m, 0.05f, 3f);
        FlockDef.Pair(v, nameof(LineSpacing_m), LineSpacing_m, 0.3f, 20f);
        FlockDef.Pair(v, nameof(Opacity), Opacity, 0f, 1f);
        v.InRange(nameof(FadeStart_m), FadeStart_m, 2, 200);
        v.InRange(nameof(FadeEnd_m), FadeEnd_m, FadeStart_m, 300);
    }
}

/// <summary>
/// Cobwebs in the corners of the buildings' doorways (the top ones) and windows (game/world/Cobwebs.cs):
/// the chance a corner has one, their size, least and most opacity, colour, and where they fade out.
/// </summary>
public sealed class CobwebsDef : IValidatable
{
    public float Chance { get; set; }

    public float[] Size_m { get; set; } = System.Array.Empty<float>();

    public float[] Opacity { get; set; } = System.Array.Empty<float>();

    public string Color { get; set; } = "";

    public float FadeStart_m { get; set; }

    public float FadeEnd_m { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Chance), Chance, 0, 1);
        FlockDef.Pair(v, nameof(Size_m), Size_m, 0.05f, 2f);
        FlockDef.Pair(v, nameof(Opacity), Opacity, 0f, 1f);
        TrainingGroundDef.Colour(v, nameof(Color), Color);
        v.InRange(nameof(FadeStart_m), FadeStart_m, 2, 200);
        v.InRange(nameof(FadeEnd_m), FadeEnd_m, FadeStart_m, 300);
    }
}

/// <summary>
/// Crows wheeling over each level and the training ground (game/world/Birds.cs): flocks circling round
/// points [x, z] from the place's middle, at a speed, their wingspan and colour, gliding for a while
/// (flapEvery_s, least and most) between bursts of wingbeats (flapFor_s).
/// </summary>
public sealed class BirdsDef : IValidatable
{
    public FlockDef[] Flocks { get; set; } = System.Array.Empty<FlockDef>();

    public float Speed_mps { get; set; }

    public float Wingspan_m { get; set; }

    public string Color { get; set; } = "";

    public float[] FlapEvery_s { get; set; } = System.Array.Empty<float>();

    public float[] FlapFor_s { get; set; } = System.Array.Empty<float>();

    /// <summary>How many sit on the level's wall tops, and how close the camera comes before they take off.</summary>
    public int Perched { get; set; }

    public float FlushDistance_m { get; set; }

    /// <summary>
    /// Sitting birds also take off at a shot this close, a ball breaking this close, or footsteps within
    /// this share of the distance they're heard (a sprint's carries furthest, a crouched step's hardly at all).
    /// </summary>
    public float ShotStartle_m { get; set; }

    public float BreakStartle_m { get; set; }

    public float StepStartle { get; set; }

    /// <summary>How many feed on the level's open ground, pecking and hopping about, and the ground materials they feed on.</summary>
    public int Feeding { get; set; }

    public string[] FeedOn { get; set; } = System.Array.Empty<string>();

    public void Validate(Validator v)
    {
        v.InRange(nameof(Perched), Perched, 0, 100);
        v.InRange(nameof(Feeding), Feeding, 0, 100);
        if (Feeding > 0 && FeedOn.Length == 0)
        {
            v.Error(nameof(FeedOn), "needs at least one ground material when birds feed");
        }

        v.InRange(nameof(FlushDistance_m), FlushDistance_m, 1, 100);
        v.InRange(nameof(ShotStartle_m), ShotStartle_m, 0, 200);
        v.InRange(nameof(BreakStartle_m), BreakStartle_m, 0, 200);
        v.InRange(nameof(StepStartle), StepStartle, 0, 2);
        for (int i = 0; i < Flocks.Length; i++)
        {
            Flocks[i].Validate(v.Item(nameof(Flocks), i));
        }

        v.InRange(nameof(Speed_mps), Speed_mps, 1, 40);
        v.InRange(nameof(Wingspan_m), Wingspan_m, 0.1, 3);
        TrainingGroundDef.Colour(v, nameof(Color), Color);
        FlockDef.Pair(v, nameof(FlapEvery_s), FlapEvery_s, 0.5f, 60f);
        FlockDef.Pair(v, nameof(FlapFor_s), FlapFor_s, 0.1f, 20f);
    }
}

/// <summary>A flock: this many birds round [x, z], each on a circle of a radius and at a height between the two given.</summary>
public sealed class FlockDef : IValidatable
{
    public float[] Center_m { get; set; } = System.Array.Empty<float>();

    public int Count { get; set; }

    public float[] Radius_m { get; set; } = System.Array.Empty<float>();

    public float[] Height_m { get; set; } = System.Array.Empty<float>();

    public void Validate(Validator v)
    {
        if (Center_m.Length != 2)
        {
            v.Error(nameof(Center_m), "must be [x, z]");
        }

        v.InRange(nameof(Count), Count, 0, 200);
        Pair(v, nameof(Radius_m), Radius_m, 2f, 500f);
        Pair(v, nameof(Height_m), Height_m, 3f, 300f);
    }

    internal static void Pair(Validator v, string name, float[] pair, float min, float max)
    {
        if (pair.Length != 2 || !(pair[0] >= min && pair[0] <= pair[1] && pair[1] <= max))
        {
            v.Error(name, $"must be [least, most] within [{min}, {max}]");
        }
    }
}

/// <summary>
/// How the buildings' paint markings are drawn (game/world/Markings.cs; where they are is each building
/// template's "markings"): least and most opacity of the worn paint, and the distances they fade out over.
/// </summary>
public sealed class MarkingsViewDef : IValidatable
{
    public float[] Opacity { get; set; } = System.Array.Empty<float>();

    public float FadeStart_m { get; set; }

    public float FadeEnd_m { get; set; }

    public void Validate(Validator v)
    {
        if (Opacity.Length != 2 || !(Opacity[0] >= 0f && Opacity[0] <= Opacity[1] && Opacity[1] <= 1f))
        {
            v.Error(nameof(Opacity), "must be [least, most] within [0, 1]");
        }

        v.InRange(nameof(FadeStart_m), FadeStart_m, 5, 500);
        v.InRange(nameof(FadeEnd_m), FadeEnd_m, FadeStart_m, 600);
    }
}

/// <summary>
/// Ivy and dead vines climbing the outside walls (game/world/Creepers.cs): cards standing on the
/// ground against walls of the listed materials, under the open sky, each with wall behind it all the
/// way. Seeded by the level id. Presentation only.
/// </summary>
public sealed class CreepersDef : IValidatable
{
    /// <summary>How many patches (fewer where there isn't room).</summary>
    public int Count { get; set; }

    /// <summary>Narrowest and widest, shortest and tallest patch (m).</summary>
    public float[] Width_m { get; set; } = System.Array.Empty<float>();

    public float[] Height_m { get; set; } = System.Array.Empty<float>();

    /// <summary>Wall materials they climb (kit/materials.jsonc ids).</summary>
    public string[] On { get; set; } = System.Array.Empty<string>();

    /// <summary>They thin out between these camera distances.</summary>
    public float FadeStart_m { get; set; }

    public float FadeEnd_m { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Count), Count, 0, 5000);
        if (Width_m.Length != 2 || !(Width_m[0] >= 0.1f && Width_m[0] <= Width_m[1] && Width_m[1] <= 10f))
        {
            v.Error(nameof(Width_m), "must be [narrowest, widest] within [0.1, 10]");
        }

        if (Height_m.Length != 2 || !(Height_m[0] >= 0.1f && Height_m[0] <= Height_m[1] && Height_m[1] <= 20f))
        {
            v.Error(nameof(Height_m), "must be [shortest, tallest] within [0.1, 20]");
        }

        v.InRange(nameof(FadeStart_m), FadeStart_m, 5, 500);
        v.InRange(nameof(FadeEnd_m), FadeEnd_m, FadeStart_m, 600);
    }
}

/// <summary>
/// The level behind the main menu (game/ui/MenuBackdrop.cs), seen from a camera drifting from one
/// point to the other and back over period_s, at fov_deg (vertical); shade is how dark the menu's
/// wash gets on its left, where the panels sit (it fades to a quarter of that on the right).
/// </summary>
public sealed class MenuBackdropDef : IValidatable
{
    public string Level { get; set; } = "";

    public CameraPointDef From { get; set; } = new();

    public CameraPointDef To { get; set; } = new();

    public float Period_s { get; set; }

    public float Fov_deg { get; set; }

    public float Shade { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Level), Level);
        From.Validate(v.Scope(nameof(From)));
        To.Validate(v.Scope(nameof(To)));
        v.InRange(nameof(Period_s), Period_s, 5, 600);
        v.InRange(nameof(Fov_deg), Fov_deg, 20, 120);
        v.InRange(nameof(Shade), Shade, 0, 1);
    }
}

/// <summary>A camera's place: where, which way (yaw left of north) and how far up or down it looks.</summary>
public sealed class CameraPointDef : IValidatable
{
    public float[] Position_m { get; set; } = System.Array.Empty<float>();

    public float Yaw_deg { get; set; }

    public float Pitch_deg { get; set; }

    public void Validate(Validator v)
    {
        v.Vector(nameof(Position_m), Position_m);
        v.InRange(nameof(Pitch_deg), Pitch_deg, -89, 89);
    }
}

public sealed class GroundDetailKindDef : IValidatable
{
    /// <summary>oil, puddle, damp, rust, tracks, leaves, litter or chips.</summary>
    public string Kind { get; set; } = "";

    /// <summary>How many to place (fewer where there isn't room).</summary>
    public int Count { get; set; }

    /// <summary>Smallest and largest length of a card.</summary>
    public float[] Size_m { get; set; } = System.Array.Empty<float>();

    /// <summary>A card's width as a share of its length (tyre tracks are long and narrow).</summary>
    [Optional]
    public float Aspect { get; set; } = 1f;

    /// <summary>Ground materials it lies on (kit/materials.jsonc ids).</summary>
    public string[] On { get; set; } = System.Array.Empty<string>();

    /// <summary>"open" (under the sky only), "covered" (indoors only) or "any".</summary>
    [Optional]
    public string Sky { get; set; } = "any";

    /// <summary>Props (kit/props.jsonc ids) it gathers round, and the share placed round them; the rest go anywhere.</summary>
    [Optional]
    public string[]? Near { get; set; }

    [Optional]
    public float NearShare { get; set; }

    /// <summary>The share placed along the foot of walls, where wind leaves things.</summary>
    [Optional]
    public float EdgeShare { get; set; }

    /// <summary>How opaque, from faint to full (multiplied with the painted coverage).</summary>
    [Optional]
    public float Opacity { get; set; } = 1f;

    public void Validate(Validator v)
    {
        if (!System.Enum.TryParse<Pb.Game.World.GroundDetailKind>(Kind, true, out _))
        {
            v.Error(nameof(Kind), $"'{Kind}' is not one of {string.Join(", ", System.Enum.GetNames<Pb.Game.World.GroundDetailKind>()).ToLowerInvariant()}");
        }

        v.InRange(nameof(Count), Count, 0, 5000);
        if (Size_m.Length != 2 || Size_m[0] <= 0f || Size_m[1] < Size_m[0] || Size_m[1] > 30f)
        {
            v.Error(nameof(Size_m), "takes [smallest, largest], 0–30 m");
        }

        v.InRange(nameof(Aspect), Aspect, 0.1, 10);
        if (On.Length == 0)
        {
            v.Error(nameof(On), "needs at least one ground material");
        }

        if (Sky is not ("open" or "covered" or "any"))
        {
            v.Error(nameof(Sky), $"'{Sky}' is not one of open, covered, any");
        }

        v.InRange(nameof(NearShare), NearShare, 0, 1);
        v.InRange(nameof(EdgeShare), EdgeShare, 0, 1);
        v.InRange(nameof(Opacity), Opacity, 0, 1);
    }
}

/// <summary>
/// The training ground (scenes/Range.tscn, the Phase 1 range) dressed with the level kit's materials
/// (kit/materials.jsonc ids, checked when it's built) under the levels' lighting and tree lines
/// (game/world/RangeBuilder.cs). Presentation only: ranges/phase1.jsonc decides what paint hits.
/// </summary>
public sealed class TrainingGroundDef : IValidatable
{
    /// <summary>The lane from the firing line to the backstop, and a tint multiplied with its photo.</summary>
    public string Field { get; set; } = "";

    public string FieldTint { get; set; } = "";

    /// <summary>The strip behind the firing line where shooters stand.</summary>
    public string FiringPoint { get; set; } = "";

    /// <summary>Paint of the distance lines and of the firing line.</summary>
    public string LineColor { get; set; } = "";

    public string FiringLineColor { get; set; } = "";

    public RangeNetsDef Nets { get; set; } = new();

    public BackstopDef Backstop { get; set; } = new();

    public DistanceBoardsDef Boards { get; set; } = new();

    public RangeHutDef Hut { get; set; } = new();

    /// <summary>The inflatable bunkers' fabric, drawn in each prop's colour, with seams a shade darker.</summary>
    public string Inflatable { get; set; } = "";

    /// <summary>Their tethers and the pegs holding them down.</summary>
    public string Tethers { get; set; } = "";

    /// <summary>Thin board props (surface "panel"), drawn in the prop's colour, and the stakes behind them.</summary>
    public string Panel { get; set; } = "";

    public string Stakes { get; set; } = "";

    public DummyViewDef Dummy { get; set; } = new();

    /// <summary>The track a moving target runs on: rails and sleepers.</summary>
    public string Rails { get; set; } = "";

    public string Sleepers { get; set; } = "";

    /// <summary>
    /// Old paint from earlier practice: shots from the firing line at the targets, the bunkers and the
    /// backstop, laid where they'd land (game/world/OldPaint.cs). Its props list is unused here.
    /// </summary>
    public OldPaintDef OldPaint { get; set; } = new();

    /// <summary>Old paint on each dummy: shots from the firing line at it, in its own frame so a moving one carries its paint.</summary>
    public OldPaintDef DummyPaint { get; set; } = new();

    /// <summary>Power lines and telegraph poles out beyond it, as a level's "scenery" (plan [x, z] round the lane).</summary>
    [Optional]
    public Pb.Sim.Data.SceneryDef? Scenery { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Field), Field);
        Colour(v, nameof(FieldTint), FieldTint);
        v.NotEmpty(nameof(FiringPoint), FiringPoint);
        Colour(v, nameof(LineColor), LineColor);
        Colour(v, nameof(FiringLineColor), FiringLineColor);
        Nets.Validate(v.Scope(nameof(Nets)));
        Backstop.Validate(v.Scope(nameof(Backstop)));
        Boards.Validate(v.Scope(nameof(Boards)));
        Hut.Validate(v.Scope(nameof(Hut)));
        v.NotEmpty(nameof(Inflatable), Inflatable);
        v.NotEmpty(nameof(Tethers), Tethers);
        v.NotEmpty(nameof(Panel), Panel);
        v.NotEmpty(nameof(Stakes), Stakes);
        Dummy.Validate(v.Scope(nameof(Dummy)));
        v.NotEmpty(nameof(Rails), Rails);
        v.NotEmpty(nameof(Sleepers), Sleepers);
        OldPaint.Validate(v.Scope(nameof(OldPaint)));
        DummyPaint.Validate(v.Scope(nameof(DummyPaint)));
        Scenery?.Validate(v.Scope(nameof(Scenery)));
    }

    internal static void Colour(Validator v, string name, string value)
    {
        if (!Godot.Color.HtmlIsValid(value))
        {
            v.Error(name, $"'{value}' is not a valid colour");
        }
    }
}

/// <summary>Netting down both sides of the lane, on posts with a cable along the top.</summary>
public sealed class RangeNetsDef : IValidatable
{
    public float Height_m { get; set; }

    public float PostSpacing_m { get; set; }

    public float PostRadius_m { get; set; }

    public string Posts { get; set; } = "";

    /// <summary>The twine's colour, the size of the netting's square mesh, and how much of each square's side the twine covers.</summary>
    public string Color { get; set; } = "";

    public float Mesh_m { get; set; }

    public float Twine { get; set; }

    /// <summary>How far the top cable sags between posts.</summary>
    public float Sag_m { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Height_m), Height_m, 1, 30);
        v.InRange(nameof(PostSpacing_m), PostSpacing_m, 1, 30);
        v.InRange(nameof(PostRadius_m), PostRadius_m, 0.01, 0.5);
        v.NotEmpty(nameof(Posts), Posts);
        TrainingGroundDef.Colour(v, nameof(Color), Color);
        v.InRange(nameof(Mesh_m), Mesh_m, 0.01, 0.5);
        v.InRange(nameof(Twine), Twine, 0.03, 0.5);
        v.InRange(nameof(Sag_m), Sag_m, 0, 2);
    }
}

/// <summary>
/// The far end: a wall of boards on posts reaching past each side of the lane, an earth bank behind it
/// rising a little above it, and netting on poles above that.
/// </summary>
public sealed class BackstopDef : IValidatable
{
    public string Boards { get; set; } = "";

    public string Posts { get; set; } = "";

    public string Bank { get; set; } = "";

    /// <summary>The grass on top of the bank.</summary>
    public string BankTop { get; set; } = "";

    public float Height_m { get; set; }

    /// <summary>How far the bank's crest rises above the wall, and how far back its far foot is.</summary>
    public float BankRise_m { get; set; }

    public float BankDepth_m { get; set; }

    /// <summary>The netting above reaches this high.</summary>
    public float NetHeight_m { get; set; }

    /// <summary>How far it reaches past each side of the lane.</summary>
    public float Overhang_m { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Boards), Boards);
        v.NotEmpty(nameof(Posts), Posts);
        v.NotEmpty(nameof(Bank), Bank);
        v.NotEmpty(nameof(BankTop), BankTop);
        v.InRange(nameof(Height_m), Height_m, 1, 10);
        v.InRange(nameof(BankRise_m), BankRise_m, 0, 10);
        v.InRange(nameof(BankDepth_m), BankDepth_m, 1, 40);
        v.InRange(nameof(NetHeight_m), NetHeight_m, Height_m, 40);
        v.InRange(nameof(Overhang_m), Overhang_m, 0, 50);
    }
}

/// <summary>
/// Boards on posts along the left of the lane at the labelled distances, bigger further out so they
/// read from the firing line: the nearest is width_m[0] across, the farthest width_m[1].
/// </summary>
public sealed class DistanceBoardsDef : IValidatable
{
    public string Board { get; set; } = "";

    public string Posts { get; set; } = "";

    public string TextColor { get; set; } = "";

    public float[] Width_m { get; set; } = System.Array.Empty<float>();

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Board), Board);
        v.NotEmpty(nameof(Posts), Posts);
        TrainingGroundDef.Colour(v, nameof(TextColor), TextColor);
        if (Width_m.Length != 2 || !(Width_m[0] >= 0.2f && Width_m[0] <= Width_m[1] && Width_m[1] <= 5f))
        {
            v.Error(nameof(Width_m), "must be [nearest, farthest] within [0.2, 5]");
        }
    }
}

/// <summary>
/// A lean-to shelter behind the firing point, its open front to the lane: a timber frame, a corrugated
/// roof sloping to the back, a boarded back wall with a bench along it, and a table with pods and a tank
/// on it. position_m is its middle [x, z]; it stops you walking through its walls, bench and table.
/// </summary>
public sealed class RangeHutDef : IValidatable
{
    public float[] Position_m { get; set; } = System.Array.Empty<float>();

    public float Width_m { get; set; }

    public float Depth_m { get; set; }

    /// <summary>Its front's height; the roof falls half a metre to the back.</summary>
    public float Height_m { get; set; }

    public string Frame { get; set; } = "";

    public string Roof { get; set; } = "";

    public string Boards { get; set; } = "";

    public void Validate(Validator v)
    {
        if (Position_m.Length != 2)
        {
            v.Error(nameof(Position_m), "must be [x, z]");
        }

        v.InRange(nameof(Width_m), Width_m, 3, 30);
        v.InRange(nameof(Depth_m), Depth_m, 1.5, 10);
        v.InRange(nameof(Height_m), Height_m, 2.2, 5);
        v.NotEmpty(nameof(Frame), Frame);
        v.NotEmpty(nameof(Roof), Roof);
        v.NotEmpty(nameof(Boards), Boards);
    }
}

/// <summary>
/// The target dummies: a padded body round their hitbox's capsule on a post in a weighted tyre, and a
/// head in a mask where their mask sphere is. They flash this colour when hit.
/// </summary>
public sealed class DummyViewDef : IValidatable
{
    public string Body { get; set; } = "";

    public string BodyColor { get; set; } = "";

    public string Straps { get; set; } = "";

    public string Head { get; set; } = "";

    public string HeadColor { get; set; } = "";

    public string Mask { get; set; } = "";

    public string Lens { get; set; } = "";

    public string Base { get; set; } = "";

    /// <summary>What the tyre is filled with.</summary>
    public string Fill { get; set; } = "";

    public string FlashColor { get; set; } = "";

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Body), Body);
        TrainingGroundDef.Colour(v, nameof(BodyColor), BodyColor);
        v.NotEmpty(nameof(Straps), Straps);
        v.NotEmpty(nameof(Head), Head);
        TrainingGroundDef.Colour(v, nameof(HeadColor), HeadColor);
        v.NotEmpty(nameof(Mask), Mask);
        v.NotEmpty(nameof(Lens), Lens);
        v.NotEmpty(nameof(Base), Base);
        v.NotEmpty(nameof(Fill), Fill);
        TrainingGroundDef.Colour(v, nameof(FlashColor), FlashColor);
    }
}
