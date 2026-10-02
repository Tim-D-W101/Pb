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

    public GroundDetailDef GroundDetail { get; set; } = new();

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
        GroundDetail.Validate(v.Scope(nameof(GroundDetail)));
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

    public void Validate(Validator v)
    {
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
    }
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

    public void Validate(Validator v)
    {
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
