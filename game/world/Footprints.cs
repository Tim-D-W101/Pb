using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Collision;
using Pb.Sim.Events;

namespace Pb.Game.World;

/// <summary>
/// Footprints pressed into soft ground (presentation.jsonc "footprints"): each step on a surface that
/// takes them leaves a boot print, left and right in turn, pointing the way the walker was going, and
/// fading out over a few minutes; a landing leaves both feet side by side. From the sim's footstep
/// events, so bots leave tracks too. Flat cards in one MultiMesh, used round as a ring, the sole painted
/// at load, darkening the ground under them (footprints.gdshader). Looks only.
/// </summary>
public partial class Footprints : Node3D, ISimEventListener
{
    private static readonly StringName Now = "now";
    private static ImageTexture? _sole;

    /// <summary>How far above the ground the prints lie (m), over the ground cards.</summary>
    private const float Lift = 0.0105f;

    private readonly HashSet<byte> _surfaces = new();
    private readonly Dictionary<int, bool> _leftFoot = new();
    private readonly Random _random = new(0xF007);
    private SimWorld _sim = null!;
    private FootprintsDef _def = null!;
    private MultiMesh? _multimesh;
    private ShaderMaterial _material = null!;
    private double _now;
    private int _next;

    public void Initialize(SimWorld sim, FootprintsDef def)
    {
        _sim = sim;
        _def = def;
        foreach (string surface in def.On)
        {
            if (sim.Config.Surfaces.TryGet(surface, out SurfaceId id))
            {
                _surfaces.Add(id.Value);
            }
            else
            {
                GD.PushWarning($"presentation.jsonc footprints.on: no surface '{surface}' in break_model.jsonc");
            }
        }

        if (_surfaces.Count == 0 || def.Max == 0)
        {
            return;
        }

        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/footprints.gdshader"), RenderPriority = -1 };
        _material.SetShaderParameter("sole", _sole ??= SoleTexture());
        _multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = new PlaneMesh { Size = Vector2.One },
        };
        _multimesh.InstanceCount = def.Max;
        for (int i = 0; i < def.Max; i++)
        {
            _multimesh.SetInstanceCustomData(i, new Color(-1000f, 1f, 0f, 0f));
        }

        AddChild(new MultiMeshInstance3D
        {
            Name = "Prints",
            Multimesh = _multimesh,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 1f,
        });
    }

    public void OnSimEvent(in SimEvent e)
    {
        if (_multimesh is null || e.Type != SimEventType.Footstep || !_surfaces.Contains(e.Surface.Value))
        {
            return;
        }

        var kind = (FootstepKind)e.Extra;
        if (kind is not (FootstepKind.Step or FootstepKind.Land))
        {
            return;
        }

        // Pointing the way they were going, or the way they face when they weren't going anywhere.
        Pb.Sim.Players.PlayerState? walker = _sim.FindPlayer(e.PlayerId);
        Vector3 velocity = walker?.Velocity.ToGodot() ?? Vector3.Zero;
        var forward = new Vector3(velocity.X, 0f, velocity.Z);
        if (forward.LengthSquared() < 0.04f)
        {
            float yaw = walker?.Yaw ?? 0f;
            forward = new Vector3(-MathF.Sin(yaw), 0f, -MathF.Cos(yaw));
        }

        forward = forward.Normalized();
        Vector3 right = forward.Cross(Vector3.Up);
        Vector3 at = e.Position.ToGodot();
        if (kind == FootstepKind.Land)
        {
            Press(at - right * 0.12f, forward, false);
            Press(at + right * 0.12f, forward, true);
            return;
        }

        bool left = _leftFoot.TryGetValue(e.PlayerId, out bool l) && l;
        _leftFoot[e.PlayerId] = !left;
        Press(at + right * (left ? -0.11f : 0.11f), forward, !left);
    }

    public override void _Process(double delta)
    {
        if (_multimesh is null)
        {
            return;
        }

        _now += delta;
        _material.SetShaderParameter(Now, (float)_now);
    }

    private void Press(Vector3 at, Vector3 forward, bool rightFoot)
    {
        int i = _next;
        _next = (_next + 1) % _def.Max;
        // A little askew, as feet are.
        Vector3 along = forward.Rotated(Vector3.Up, ((float)_random.NextDouble() - 0.5f) * 0.25f + (rightFoot ? -0.06f : 0.06f));
        Vector3 across = along.Cross(Vector3.Up);
        // The card's x across the sole and z along it, toe towards −z (the top of the painted sole).
        var basis = new Basis(across * _def.Size_m[0], Vector3.Up, -along * _def.Size_m[1]);
        Color colour = Color.FromHtml(_def.Color) * (0.9f + 0.2f * (float)_random.NextDouble());
        colour.A = _def.Opacity * (0.75f + 0.25f * (float)_random.NextDouble());
        _multimesh!.SetInstanceTransform(i, new Transform3D(basis, at + Vector3.Up * Lift));
        _multimesh.SetInstanceColor(i, colour);
        _multimesh.SetInstanceCustomData(i, new Color((float)_now, _def.Fade_s, rightFoot ? 1f : 0f, 0f));
    }

    /// <summary>
    /// A left boot's sole, toe at the top: the outline of a sole and heel, pressed deepest at the heel and the
    /// ball of the foot, with bars of tread across it. White, with how deep it's pressed in alpha.
    /// </summary>
    private static ImageTexture SoleTexture()
    {
        const int width = 64, height = 160;
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // u across (−1 at the outside edge of a left sole … 1 at its inside), v from the toe (0) to the heel (1).
                float u = (x + 0.5f) / width * 2f - 1f, v = (y + 0.5f) / height;
                // Widest across the ball of the foot, narrow at the waist, a rounded heel; the inside edge curves in.
                float half = v < 0.12f ? 0.85f * MathF.Sqrt(MathF.Max(0f, 1f - MathF.Pow((0.12f - v) / 0.12f, 2f)))
                    : v < 0.45f ? 0.9f
                    : v < 0.62f ? 0.9f - 0.3f * MathF.Sin((v - 0.45f) / 0.17f * MathF.PI * 0.5f)
                    : v < 0.88f ? 0.7f
                    : 0.7f * MathF.Sqrt(MathF.Max(0f, 1f - MathF.Pow((v - 0.88f) / 0.12f, 2f)));
                float centre = -0.06f * MathF.Sin(v * MathF.PI);
                float edge = half - MathF.Abs(u - centre);
                if (edge <= 0f)
                {
                    continue;
                }

                // Deepest under the ball and the heel; the tread's bars stand out darker.
                float press = 0.55f + 0.45f * MathF.Max(MathF.Exp(-MathF.Pow((v - 0.28f) / 0.14f, 2f)), MathF.Exp(-MathF.Pow((v - 0.8f) / 0.1f, 2f)));
                bool tread = MathF.Abs((v * 16f) % 1f - 0.5f) > 0.3f || MathF.Abs(u - centre) > half - 0.12f;
                int i = (y * width + x) * 4;
                byte shade = (byte)(tread ? 150 : 255);
                rgba[i] = rgba[i + 1] = rgba[i + 2] = shade;
                rgba[i + 3] = (byte)(Mathf.Clamp(edge * 12f, 0f, 1f) * press * 255f);
            }
        }

        Image image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }
}
