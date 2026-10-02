using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Collision;
using Pb.Sim.Events;
using Pb.Sim.Players;

namespace Pb.Game.World;

/// <summary>
/// Dust kicked up underfoot (presentation.jsonc "footDust"), on the surfaces that have any: a puff at
/// each footstep, bigger the harder the step (a walk's to a sprint's; crouched steps raise none), a ring
/// of them on landing, one on a jump, and a trail along a slide; and where paint breaks or bounces on
/// the ground. Driven by the sim's footstep and ball events and its sliding players, so bots and people
/// raise it alike; soft lit puffs in one MultiMesh, used round as a ring (foot_dust.gdshader). Looks only.
/// </summary>
public partial class FootDust : Node3D, ISimEventListener
{
    private static readonly StringName Now = "now";
    private static ImageTexture? _puff;

    private readonly Dictionary<byte, Color> _colours = new();
    private readonly Dictionary<int, Vector3> _slides = new();
    private readonly Dictionary<int, bool> _leftFoot = new();
    private readonly Random _random = new(0xD057);
    private SimWorld _sim = null!;
    private FootDustDef _def = null!;
    private FootstepParams _steps = null!;
    private MultiMesh _multimesh = null!;
    private ShaderMaterial _material = null!;
    private double _now;
    private int _next;
    private int _budget;

    /// <summary>How many puffs have been raised.</summary>
    public int Raised { get; private set; }

    public void Initialize(SimWorld sim, FootDustDef def, Vector2 wind)
    {
        _sim = sim;
        _def = def;
        _steps = sim.Config.Movement.Footsteps;
        _budget = def.PerFrame;
        foreach ((string surface, string colour) in def.Colors)
        {
            if (sim.Config.Surfaces.TryGet(surface, out SurfaceId id))
            {
                _colours[id.Value] = Color.FromHtml(colour);
            }
            else
            {
                GD.PushWarning($"presentation.jsonc footDust.colors: no surface '{surface}' in break_model.jsonc");
            }
        }

        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/foot_dust.gdshader") };
        _material.SetShaderParameter("puff", _puff ??= PuffTexture());
        _material.SetShaderParameter("drift", new Vector3(wind.X, 0f, wind.Y) * def.WindShare);
        _material.SetShaderParameter("rise", def.Rise_mps);
        _material.SetShaderParameter("grow", def.Grow);
        _multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = new QuadMesh { Size = Vector2.One },
        };
        _multimesh.InstanceCount = def.Max;
        for (int i = 0; i < def.Max; i++)
        {
            // Long dead: nothing drawn until a puff is raised in its place.
            _multimesh.SetInstanceCustomData(i, new Color(-1000f, 0f, 1f, 0f));
        }

        AddChild(new MultiMeshInstance3D
        {
            Name = "Puffs",
            Multimesh = _multimesh,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // Puffs drift, rise and grow in the shader, away from where they were raised.
            ExtraCullMargin = 3f,
        });
    }

    public void OnSimEvent(in SimEvent e)
    {
        if (e.Type is SimEventType.BallBroke or SimEventType.BallBounced)
        {
            // A ball hitting the ground there knocks a little up.
            float size = e.Type == SimEventType.BallBroke ? _def.BreakSize_m : _def.BounceSize_m;
            if (size > 0f && e.Normal.Y > 0.5f && _colours.TryGetValue(e.Surface.Value, out Color ground))
            {
                Raise(e.Position.ToGodot(), size, ground, 0f);
            }

            return;
        }

        if (e.Type != SimEventType.Footstep || !_colours.TryGetValue(e.Surface.Value, out Color colour))
        {
            return;
        }

        Vector3 at = e.Position.ToGodot();
        switch ((FootstepKind)e.Extra)
        {
            case FootstepKind.Step:
                // How hard the step was, from its noise on this surface: a walk's 0 to a sprint's 1.
                float loud = e.Value / MathF.Max(_steps.Loudness(e.Surface), 0.01f);
                float effort = (loud - _steps.WalkRadius) / MathF.Max(_steps.SprintRadius - _steps.WalkRadius, 0.01f);
                if (effort < -0.01f)
                {
                    return;
                }

                float size = Mathf.Lerp(_def.StepSize_m[0], _def.StepSize_m[1], Mathf.Clamp(effort, 0f, 1f));
                Vector3 velocity = _sim.FindPlayer(e.PlayerId)?.Velocity.ToGodot() ?? Vector3.Zero;
                Vector3 back = new Vector3(-velocity.X, 0f, -velocity.Z).Normalized();
                bool left = _leftFoot.TryGetValue(e.PlayerId, out bool l) && l;
                _leftFoot[e.PlayerId] = !left;
                Vector3 foot = at + back.Cross(Vector3.Up) * (left ? 0.11f : -0.11f);
                Raise(foot, size, colour, 0f);
                Raise(foot + back * 0.25f, size * 0.7f, colour, 0.05f);
                break;
            case FootstepKind.Land:
                for (int i = 0; i < 5; i++)
                {
                    float angle = Mathf.Tau * (i + (float)_random.NextDouble() * 0.5f) / 5f;
                    Raise(at + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * 0.25f, _def.LandSize_m * R(0.8f, 1.1f), colour, 0f, angle / Mathf.Tau);
                }

                break;
            case FootstepKind.Jump:
                Raise(at, _def.JumpSize_m, colour, 0f);
                break;
            case FootstepKind.Slide:
                _slides[e.PlayerId] = at;
                Raise(at, _def.SlideSize_m, colour, 0f);
                break;
        }
    }

    public override void _Process(double delta)
    {
        _budget = _def.PerFrame;
        _now += delta;
        _material.SetShaderParameter(Now, (float)_now);
        // Sliding feet raise a trail until the slide ends.
        foreach (PlayerState p in _sim.Players)
        {
            if (!_slides.TryGetValue(p.Id, out Vector3 last))
            {
                continue;
            }

            if (!p.Alive || p.Stance != Stance.Sliding || !p.Grounded || !_colours.TryGetValue(p.GroundSurface.Value, out Color colour))
            {
                _slides.Remove(p.Id);
                continue;
            }

            Vector3 at = p.Position.ToGodot();
            if (at.DistanceTo(last) >= _def.SlideEvery_m)
            {
                _slides[p.Id] = at;
                Raise(at, _def.SlideSize_m * R(0.8f, 1.1f), colour, 0f);
            }
        }
    }

    private float R(float a, float b) => a + (float)_random.NextDouble() * (b - a);

    /// <summary>Raises a puff at <paramref name="at"/> (on the ground), <paramref name="delay"/> seconds from now.</summary>
    private void Raise(Vector3 at, float size, Color colour, float delay, float heading = -1f)
    {
        // A hail of paint raises only so many a frame.
        if (_budget <= 0 || GetViewport().GetCamera3D() is { } camera && camera.GlobalPosition.DistanceSquaredTo(at) > _def.Reach_m * _def.Reach_m)
        {
            return;
        }

        _budget--;

        int i = _next;
        _next = (_next + 1) % _def.Max;
        Raised++;
        Color tint = colour * R(0.9f, 1.05f);
        tint.A = colour.A * _def.Opacity * R(0.8f, 1f);
        _multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, at));
        _multimesh.SetInstanceColor(i, tint);
        _multimesh.SetInstanceCustomData(i, new Color((float)_now + delay, size * R(0.85f, 1.15f), _def.Lifetime_s * R(0.8f, 1.2f), heading >= 0f ? heading : (float)_random.NextDouble()));
    }

    /// <summary>A soft, lumpy blob of dust, white with its thickness in alpha.</summary>
    private static ImageTexture PuffTexture()
    {
        const int size = 64;
        var noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.09f, FractalOctaves = 3, Seed = 7 };
        Image lumps = noise.GetImage(size, size);
        lumps.Convert(Image.Format.L8);
        byte[] values = lumps.GetData();
        var rgba = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float r = MathF.Sqrt(dx * dx + dy * dy);
                float angle = MathF.Atan2(dy, dx);
                // A billowing edge, thicker in the middle and broken up inside.
                float edge = 0.8f + 0.1f * MathF.Sin(angle * 3f + 1.3f) + 0.07f * MathF.Sin(angle * 5f + 0.4f);
                float body = Mathf.SmoothStep(edge, edge * 0.25f, r);
                float lump = values[y * size + x] / 255f;
                float alpha = body * (0.55f + 0.45f * lump);
                int i = (y * size + x) * 4;
                rgba[i] = rgba[i + 1] = rgba[i + 2] = 255;
                rgba[i + 3] = (byte)(Mathf.Clamp(alpha, 0f, 1f) * 255f + 0.5f);
            }
        }

        Image image = Image.CreateFromData(size, size, false, Image.Format.Rgba8, rgba);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }
}
