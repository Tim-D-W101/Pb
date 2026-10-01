using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Collision;
using Pb.Sim.Level;
using Aabb = Godot.Aabb;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.World;

/// <summary>
/// Daylight through the level's openings: a sunbeam, with dust drifting in it, wherever the sun
/// reaches through a window, door or roof hole into a roofed space; an unshadowed fill light just
/// inside each window or door; and a warm bounce light where a sun patch lands. All of it is worked out
/// at load from the level's apertures and the sun's direction, with sight lines tested against the
/// paint geometry.
/// </summary>
public partial class LightShafts : Node3D
{
    /// <summary>How far out from a wall opening to look for open sky (clear of the eaves).</summary>
    private const float SideProbe = 0.6f;

    /// <summary>How far to look for the sun before calling an opening sunlit.</summary>
    private const float SunReach = 150f;

    private readonly List<MeshInstance3D> _beams = new();
    private readonly List<GpuParticles3D> _dust = new();
    private readonly List<Light3D> _lights = new();
    private ShaderMaterial? _beamMaterial;
    private float _density;

    public int BeamCount => _beams.Count;

    public int LightCount => _lights.Count;

    public void Build(LevelLayout level, ICollisionWorld world, LightingDef lighting, ShaftsDef shafts, DustDef dust, WindowLightDef window)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        _beams.Clear();
        _dust.Clear();
        _lights.Clear();
        _density = shafts.Density_perM;

        Vector3 toSun = Atmosphere.ToSun(lighting);
        Vector3 light = -toSun;
        Color sun = Color.FromHtml(lighting.SunColor);
        _beamMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/light_shaft.gdshader") };
        _beamMaterial.SetShaderParameter("beam_color", sun.SrgbToLinear() * lighting.SunEnergy);
        _beamMaterial.SetShaderParameter("density", shafts.Density_perM);
        _beamMaterial.SetShaderParameter("falloff", shafts.Falloff_perM);
        _beamMaterial.SetShaderParameter("softness", shafts.EdgeSoftness_m);
        _beamMaterial.SetShaderParameter("spread", shafts.EdgeSpread);
        _beamMaterial.SetShaderParameter("patchiness", shafts.Patchiness);
        _beamMaterial.SetShaderParameter("swirl_size", shafts.SwirlSize_m);
        _beamMaterial.SetShaderParameter("swirl_speed", shafts.SwirlSpeed_mps);
        _beamMaterial.SetShaderParameter("near_fade", shafts.NearFade_m);
        _beamMaterial.SetShaderParameter("fade_start", shafts.FadeStart_m);
        _beamMaterial.SetShaderParameter("fade_end", shafts.FadeEnd_m);
        _beamMaterial.SetShaderParameter("samples", shafts.Samples);
        var box = new BoxMesh { Size = Vector3.One };
        StandardMaterial3D moteMaterial = MoteMaterial(sun, lighting.SunEnergy, dust.Brightness);
        var mote = new QuadMesh { Size = new Vector2(dust.Size_m, dust.Size_m), Material = moteMaterial };
        Dictionary<int, Aabb> buildings = OwnerBounds(level);

        float minEntry = MathF.Sin(Mathf.DegToRad(shafts.MinSunAngle_deg));
        var exterior = new List<(Aperture Opening, Vector3 Inward)>();
        var bounces = new List<(Vector3 At, float Energy)>();
        foreach (Aperture a in MergeStacked(level.Apertures))
        {
            Vector3 n = a.Normal.ToGodot();
            Vector3 c = a.Center.ToGodot();
            Vector3 inward = Vector3.Down;
            if (a.Kind != ApertureKind.RoofHole)
            {
                bool skyAhead = OpenSky(world, c + n * SideProbe);
                if (skyAhead == OpenSky(world, c - n * SideProbe))
                {
                    // Between two rooms, or open air on both sides (a breach in the perimeter wall).
                    continue;
                }

                inward = skyAhead ? -n : n;
                exterior.Add((a, inward));
            }

            float entry = light.Dot(inward);
            float lit = entry < minEntry ? 0f : SunlitShare(world, a, toSun);
            if (lit <= 0f)
            {
                continue;
            }

            float strength = lit * (a.Kind == ApertureKind.RoofHole ? shafts.RoofHoleFactor : 1f);
            Beam beam = MakeBeam(world, a, light, shafts.MaxLength_m);
            buildings.TryGetValue(a.Owner, out Aabb clip);
            AddBeam(beam, box, strength, clip, a);
            AddDust(beam, mote, dust, strength, a);
            Vector3 from = a.Center.ToGodot() + light * 0.05f;
            if (world.SweepSphere(from.ToSim(), (from + light * SunReach).ToSim(), 0f, out SweepHit landing))
            {
                bounces.Add((landing.Point.ToGodot() + landing.Normal.ToGodot() * 0.6f, window.BounceEnergy_perM2 * a.Area * lit));
            }
        }

        AddWindowLights(exterior, window);
        AddBounceLights(bounces, sun, window);
    }

    /// <summary>Turns the beams, dust and window lights on or off and sets the beams' strength.</summary>
    public void ApplyPreset(GraphicsPresetDef preset)
    {
        // With MSAA on, the depth buffer isn't available to the beam shader, which would then shine
        // through walls (seen on Mesa's Vulkan driver), so the beams switch off.
        bool beams = preset.LightShafts > 0f && preset.Msaa == 0;
        _beamMaterial?.SetShaderParameter("density", _density * preset.LightShafts);
        foreach (MeshInstance3D beam in _beams)
        {
            beam.Visible = beams;
        }

        foreach (GpuParticles3D dust in _dust)
        {
            dust.Visible = beams && preset.Dust;
            dust.Emitting = dust.Visible;
        }

        foreach (Light3D light in _lights)
        {
            light.Visible = preset.WindowLights;
        }
    }

    /// <summary>A beam: one corner of the opening and its edges across, up (or across) and along the light.</summary>
    private readonly record struct Beam(Vector3 Origin, Vector3 A, Vector3 B, Vector3 C)
    {
        public Vector3 Centre => Origin + (A + B + C) * 0.5f;

        public float Volume => MathF.Abs(A.Dot(B.Cross(C)));
    }

    private Beam MakeBeam(ICollisionWorld world, Aperture a, Vector3 light, float maxLength)
    {
        // Long enough to reach whatever the light lands on from every part of the opening; the depth
        // buffer ends it exactly where it hits.
        float length = 0.5f;
        foreach ((float u, float v) in new[] { (0f, 0f), (-0.85f, -0.85f), (0.85f, -0.85f), (0.85f, 0.85f), (-0.85f, 0.85f) })
        {
            Vector3 p = a.At(u, v).ToGodot();
            length = MathF.Max(length, Reach(world, p, light, maxLength));
        }

        Vector3 origin = a.At(-1f, -1f).ToGodot();
        Vector3 across = a.U.ToGodot() * (a.HalfWidth * 2f);
        Vector3 up = a.V.ToGodot() * (a.HalfHeight * 2f);
        Vector3 along = light * MathF.Min(maxLength, length + 0.3f);
        if (across.Dot(up.Cross(along)) < 0f)
        {
            // Keep the box right-handed so its back faces stay its back faces.
            origin += across;
            across = -across;
        }

        return new Beam(origin, across, up, along);
    }

    private void AddBeam(Beam beam, BoxMesh box, float strength, Aabb clip, Aperture a)
    {
        var node = new MeshInstance3D
        {
            Name = $"Beam_{a.Kind}_{_beams.Count}",
            Mesh = box,
            MaterialOverride = _beamMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Transform = new Transform3D(new Basis(beam.A, beam.B, beam.C), beam.Centre),
        };
        AddChild(node);
        node.SetInstanceShaderParameter("strength", strength);
        node.SetInstanceShaderParameter("size_m", new Vector3(beam.A.Length(), beam.B.Length(), beam.C.Length()));
        if (clip.Size != Vector3.Zero)
        {
            node.SetInstanceShaderParameter("clip_min", clip.Position - Vector3.One * 0.01f);
            node.SetInstanceShaderParameter("clip_max", clip.End + Vector3.One * 0.01f);
        }

        _beams.Add(node);
    }

    private void AddDust(Beam beam, QuadMesh mote, DustDef dust, float strength, Aperture a)
    {
        int amount = Math.Min(dust.MaxPerBeam, Mathf.RoundToInt(beam.Volume * dust.PerCubicMetre * strength));
        if (amount < 4)
        {
            return;
        }

        var process = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/dust_motes.gdshader") };
        process.SetShaderParameter("origin", beam.Origin);
        process.SetShaderParameter("axis_a", beam.A);
        process.SetShaderParameter("axis_b", beam.B);
        process.SetShaderParameter("axis_c", beam.C);
        process.SetShaderParameter("drift", dust.Drift_mps);
        var particles = new GpuParticles3D
        {
            Name = $"Dust_{a.Kind}_{_dust.Count}",
            Amount = amount,
            Lifetime = dust.Lifetime_s,
            Preprocess = dust.Lifetime_s,
            ProcessMaterial = process,
            DrawPass1 = mote,
            LocalCoords = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = BoundsOf(beam).Grow(0.5f),
        };
        AddChild(particles);
        _dust.Add(particles);
    }

    /// <summary>
    /// Warm lights where the beams land, standing in for sunlight bouncing off the floor. Landings
    /// close together share one light.
    /// </summary>
    private void AddBounceLights(List<(Vector3 At, float Energy)> bounces, Color sun, WindowLightDef window)
    {
        var merged = new List<(Vector3 At, float Energy)>();
        foreach ((Vector3 at, float energy) in bounces)
        {
            int near = merged.FindIndex(m => m.At.DistanceTo(at) < window.MergeDistance_m);
            if (near < 0)
            {
                merged.Add((at, energy));
                continue;
            }

            (Vector3 mAt, float mEnergy) = merged[near];
            merged[near] = ((mAt * mEnergy + at * energy) / (mEnergy + energy), mEnergy + energy);
        }

        Color tint = Color.FromHtml(window.BounceTint);
        foreach ((Vector3 at, float energy) in merged)
        {
            var bounce = new OmniLight3D
            {
                Name = $"Bounce_{_lights.Count}",
                Position = at,
                LightColor = new Color(sun.R * tint.R, sun.G * tint.G, sun.B * tint.B),
                LightEnergy = MathF.Min(window.MaxBounceEnergy, energy),
                LightSpecular = 0f,
                OmniRange = window.BounceRange_m,
                ShadowEnabled = false,
            };
            AddChild(bounce);
            _lights.Add(bounce);
        }
    }

    /// <summary>One spotlight per group of neighbouring openings on a wall, angled down and in, like skylight through a window.</summary>
    private void AddWindowLights(List<(Aperture Opening, Vector3 Inward)> exterior, WindowLightDef window)
    {
        int n = exterior.Count;
        int[] group = Enumerable.Range(0, n).ToArray();
        int Find(int i)
        {
            while (group[i] != i)
            {
                i = group[i] = group[group[i]];
            }

            return i;
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                (Aperture a, Vector3 ia) = exterior[i];
                (Aperture b, Vector3 ib) = exterior[j];
                Vector3 offset = (b.Center - a.Center).ToGodot();
                bool sameWall = ia.Dot(ib) > 0.99f && MathF.Abs(ia.Dot(offset)) < 0.3f && MathF.Abs(offset.Y) < 1.5f;
                if (sameWall && new Vector2(offset.X, offset.Z).Length() < window.MergeDistance_m)
                {
                    group[Find(i)] = Find(j);
                }
            }
        }

        Color color = Color.FromHtml(window.Color);
        foreach (IGrouping<int, int> members in Enumerable.Range(0, n).GroupBy(Find))
        {
            float area = 0f;
            Vector3 centre = Vector3.Zero;
            foreach (int i in members)
            {
                area += exterior[i].Opening.Area;
                centre += exterior[i].Opening.Center.ToGodot() * exterior[i].Opening.Area;
            }

            centre /= area;
            Vector3 inward = exterior[members.First()].Inward;
            Vector3 aim = (inward - Vector3.Up * 0.45f).Normalized();
            var spot = new SpotLight3D
            {
                Name = $"Window_{_lights.Count}",
                Transform = new Transform3D(Basis.LookingAt(aim, Vector3.Up), centre + inward * 0.25f),
                LightColor = color,
                LightEnergy = MathF.Min(window.MaxEnergy, window.Energy_perM2 * area),
                LightSpecular = 0.2f,
                SpotAngle = window.Angle_deg,
                SpotRange = window.Range_m,
                ShadowEnabled = false,
            };
            AddChild(spot);
            _lights.Add(spot);
        }
    }

    /// <summary>Share of the opening (3 × 3 samples) with a clear line to the sun.</summary>
    private static float SunlitShare(ICollisionWorld world, Aperture a, Vector3 toSun)
    {
        int clear = 0;
        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                Vector3 p = a.At(i * 0.66f, j * 0.66f).ToGodot();
                if (!world.SweepSphere((p + toSun * 0.05f).ToSim(), (p + toSun * SunReach).ToSim(), 0f, out _))
                {
                    clear++;
                }
            }
        }

        return clear / 9f;
    }

    /// <summary>How far light from <paramref name="p"/> travels along <paramref name="light"/> before it hits something.</summary>
    private static float Reach(ICollisionWorld world, Vector3 p, Vector3 light, float maxLength)
    {
        Vector3 from = p + light * 0.05f;
        return world.SweepSphere(from.ToSim(), (from + light * maxLength).ToSim(), 0f, out SweepHit hit) ? 0.05f + hit.T * maxLength : maxLength;
    }

    private static bool OpenSky(ICollisionWorld world, Vector3 p) => !world.SweepSphere(p.ToSim(), (p + Vector3.Up * 100f).ToSim(), 0f, out _);

    /// <summary>
    /// Wall openings stacked one on another (a loading bay through both the block base and the cladding
    /// above it) become one opening, so their beam has no seam.
    /// </summary>
    private static List<Aperture> MergeStacked(IReadOnlyList<Aperture> apertures)
    {
        var list = apertures.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            for (int j = i + 1; j < list.Count; j++)
            {
                Aperture a = list[i], b = list[j];
                if (a.Kind == ApertureKind.RoofHole || b.Kind == ApertureKind.RoofHole || MathF.Abs(SVector3.Dot(a.U, b.U)) < 0.999f ||
                    MathF.Abs(a.HalfWidth - b.HalfWidth) > 0.01f || MathF.Abs(a.Center.X - b.Center.X) + MathF.Abs(a.Center.Z - b.Center.Z) > 0.02f)
                {
                    continue;
                }

                float aBottom = a.Center.Y - a.HalfHeight, aTop = a.Center.Y + a.HalfHeight;
                float bBottom = b.Center.Y - b.HalfHeight, bTop = b.Center.Y + b.HalfHeight;
                if (MathF.Abs(aTop - bBottom) > 0.02f && MathF.Abs(bTop - aBottom) > 0.02f)
                {
                    continue;
                }

                float bottom = MathF.Min(aBottom, bBottom), top = MathF.Max(aTop, bTop);
                list[i] = a with { Center = a.Center with { Y = (bottom + top) * 0.5f }, HalfHeight = (top - bottom) * 0.5f };
                list.RemoveAt(j);
                j = i;
            }
        }

        return list;
    }

    /// <summary>World bounds of everything each owner (building) generated.</summary>
    private static Dictionary<int, Aabb> OwnerBounds(LevelLayout level)
    {
        var bounds = new Dictionary<int, Aabb>();
        foreach (LevelPrimitive p in level.Primitives)
        {
            if (!p.Has(PrimitiveFlags.Paint))
            {
                continue;
            }

            Pb.Sim.Collision.Aabb b = p.Bounds;
            var box = new Aabb(b.Min.ToGodot(), (b.Max - b.Min).ToGodot());
            bounds[p.Owner] = bounds.TryGetValue(p.Owner, out Aabb existing) ? existing.Merge(box) : box;
        }

        return bounds;
    }

    private static Aabb BoundsOf(Beam beam)
    {
        var box = new Aabb(beam.Origin, Vector3.Zero);
        foreach (Vector3 corner in new[] { beam.A, beam.B, beam.C, beam.A + beam.B, beam.A + beam.C, beam.B + beam.C, beam.A + beam.B + beam.C })
        {
            box = box.Expand(beam.Origin + corner);
        }

        return box;
    }

    private static StandardMaterial3D MoteMaterial(Color sun, float sunEnergy, float brightness)
    {
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(1f, 1f, 1f, 1f));
        gradient.SetColor(1, new Color(1f, 1f, 1f, 0f));
        float glow = sunEnergy * brightness;
        return new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            BillboardKeepScale = true,
            VertexColorUseAsAlbedo = true,
            AlbedoColor = new Color(sun.R * glow, sun.G * glow, sun.B * glow),
            AlbedoTexture = new GradientTexture2D
            {
                Gradient = gradient,
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f),
                FillTo = new Vector2(1f, 0.5f),
                Width = 32,
                Height = 32,
            },
            DisableReceiveShadows = true,
        };
    }
}
