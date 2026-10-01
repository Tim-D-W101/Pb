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

    public MaskSprayViewDef MaskSpray { get; set; } = new();

    public SpectatorDef Spectator { get; set; } = new();

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
        MaskSpray.Validate(v.Scope(nameof(MaskSpray)));
        Spectator.Validate(v.Scope(nameof(Spectator)));
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

    /// <summary>0 = off, 1 = 2×, 2 = 4×.</summary>
    public int Msaa { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Name), Name);
        v.InRange(nameof(ShadowSize), ShadowSize, 512, 16384);
        v.InRange(nameof(ShadowDistance_m), ShadowDistance_m, 10, 1000);
        v.InRange(nameof(Msaa), Msaa, 0, 2);
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

/// <summary>The view after you're eliminated: who got you.</summary>
public sealed class SpectatorDef : IValidatable
{
    public float Duration_s { get; set; }

    /// <summary>Camera position: this far above your eye and this far back from the shooter's line.</summary>
    public float Height_m { get; set; }

    public float Back_m { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Duration_s), Duration_s, 0.5, 30);
        v.InRange(nameof(Height_m), Height_m, 0, 10);
        v.InRange(nameof(Back_m), Back_m, 0, 20);
    }
}
