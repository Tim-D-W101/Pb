using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Collision;
using Pb.Sim.Events;

namespace Pb.Game.World;

/// <summary>
/// Footprints (presentation.jsonc "footprints"): each step on a surface that takes them leaves a boot
/// print pressed into it, left and right in turn, pointing the way the walker was going, and fading out
/// over a few minutes; a landing leaves both feet side by side. Step in fresh paint on the ground and
/// the next few prints are in that paint, on any ground, fainter each step. From the sim's footstep and
/// ball events, so bots leave tracks too. Flat cards in two MultiMeshes, each used round as a ring: the
/// prints darken the ground under them (footprints.gdshader), the paint lies on it (paint_prints.gdshader).
/// Looks only.
/// </summary>
public partial class Footprints : Node3D, ISimEventListener
{
    private static readonly StringName Now = "now";
    private static ImageTexture? _sole;

    /// <summary>How far above the ground the prints lie (m), over the ground cards; paint a little higher.</summary>
    private const float Lift = 0.0105f, PaintLift = 0.0112f;

    private readonly HashSet<byte> _surfaces = new();
    private readonly Dictionary<int, bool> _leftFoot = new();
    private readonly Dictionary<int, (Color Color, int Left)> _boots = new();
    private readonly (Vector3 At, Color Color, double Time)[] _splats = new (Vector3, Color, double)[96];
    private readonly Random _random = new(0xF007);
    private SimWorld _sim = null!;
    private FootprintsDef _def = null!;
    private Color[] _teamColors = Array.Empty<Color>();
    private Ring? _pressed, _painted;
    private double _now;
    private int _splatNext;

    private sealed class Ring
    {
        public required MultiMesh Multimesh { get; init; }

        public required ShaderMaterial Material { get; init; }

        public int Next { get; set; }
    }

    public void Initialize(SimWorld sim, FootprintsDef def, string[] teamColors)
    {
        _sim = sim;
        _def = def;
        _teamColors = new Color[teamColors.Length];
        for (int i = 0; i < teamColors.Length; i++)
        {
            _teamColors[i] = Color.FromHtml(teamColors[i]);
        }

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

        for (int i = 0; i < _splats.Length; i++)
        {
            _splats[i].Time = double.NegativeInfinity;
        }

        if (def.Max == 0)
        {
            return;
        }

        _pressed = _surfaces.Count > 0 ? NewRing("Prints", "res://shaders/footprints.gdshader") : null;
        _painted = def.PaintSteps > 0 ? NewRing("PaintPrints", "res://shaders/paint_prints.gdshader") : null;
    }

    private Ring NewRing(string name, string shader)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>(shader), RenderPriority = -1 };
        material.SetShaderParameter("sole", _sole ??= SoleTexture());
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = new PlaneMesh { Size = Vector2.One },
        };
        multimesh.InstanceCount = _def.Max;
        for (int i = 0; i < _def.Max; i++)
        {
            multimesh.SetInstanceCustomData(i, new Color(-1000f, 1f, 0f, 0f));
        }

        AddChild(new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = multimesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 1f,
        });
        return new Ring { Multimesh = multimesh, Material = material };
    }

    public void OnSimEvent(in SimEvent e)
    {
        if (e.Type == SimEventType.BallBroke)
        {
            // Paint lying on the ground, for boots to tread in.
            if (e.TargetId < 0 && e.Normal.Y > 0.7f)
            {
                _splats[_splatNext] = (e.Position.ToGodot(), _teamColors[e.Team % _teamColors.Length], _now);
                _splatNext = (_splatNext + 1) % _splats.Length;
            }

            return;
        }

        if (e.Type != SimEventType.Footstep)
        {
            return;
        }

        var kind = (FootstepKind)e.Extra;
        if (kind is not (FootstepKind.Step or FootstepKind.Land))
        {
            return;
        }

        bool pressed = _pressed is not null && _surfaces.Contains(e.Surface.Value);
        Vector3 at = e.Position.ToGodot();
        TreadIn(e.PlayerId, at);
        if (!pressed && !(_painted is not null && _boots.TryGetValue(e.PlayerId, out (Color, int Left) boots) && boots.Left > 0))
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
        if (kind == FootstepKind.Land)
        {
            Foot(e.PlayerId, at - right * 0.12f, forward, false, pressed);
            Foot(e.PlayerId, at + right * 0.12f, forward, true, pressed);
            return;
        }

        bool left = _leftFoot.TryGetValue(e.PlayerId, out bool l) && l;
        _leftFoot[e.PlayerId] = !left;
        Foot(e.PlayerId, at + right * (left ? -0.11f : 0.11f), forward, !left, pressed);
    }

    public override void _Process(double delta)
    {
        _now += delta;
        _pressed?.Material.SetShaderParameter(Now, (float)_now);
        _painted?.Material.SetShaderParameter(Now, (float)_now);
    }

    /// <summary>A foot treading in fresh paint on the ground picks it up on its sole.</summary>
    private void TreadIn(int player, Vector3 at)
    {
        if (_painted is null)
        {
            return;
        }

        foreach ((Vector3 splat, Color color, double time) in _splats)
        {
            if (_now - time < _def.PaintFresh_s && splat.DistanceSquaredTo(at) < _def.PaintReach_m * _def.PaintReach_m)
            {
                _boots[player] = (color, _def.PaintSteps);
                return;
            }
        }
    }

    /// <summary>One foot down: pressed into the ground if it takes prints, and in paint while the boots still carry some.</summary>
    private void Foot(int player, Vector3 at, Vector3 forward, bool rightFoot, bool pressed)
    {
        // A little askew, as feet are.
        Vector3 along = forward.Rotated(Vector3.Up, ((float)_random.NextDouble() - 0.5f) * 0.25f + (rightFoot ? -0.06f : 0.06f));
        Vector3 across = along.Cross(Vector3.Up);
        // The card's x across the sole and z along it, toe towards −z (the top of the painted sole).
        var basis = new Basis(across * _def.Size_m[0], Vector3.Up, -along * _def.Size_m[1]);
        if (pressed)
        {
            Color colour = Color.FromHtml(_def.Color) * (0.9f + 0.2f * (float)_random.NextDouble());
            colour.A = _def.Opacity * (0.75f + 0.25f * (float)_random.NextDouble());
            Stamp(_pressed!, new Transform3D(basis, at + Vector3.Up * Lift), colour, rightFoot);
        }

        if (_painted is not null && _boots.TryGetValue(player, out (Color Color, int Left) boots) && boots.Left > 0)
        {
            Color paint = boots.Color;
            paint.A = _def.PaintOpacity * boots.Left / _def.PaintSteps;
            Stamp(_painted, new Transform3D(basis, at + Vector3.Up * PaintLift), paint, rightFoot);
            _boots[player] = (boots.Color, boots.Left - 1);
        }
    }

    private void Stamp(Ring ring, Transform3D transform, Color colour, bool rightFoot)
    {
        int i = ring.Next;
        ring.Next = (ring.Next + 1) % _def.Max;
        ring.Multimesh.SetInstanceTransform(i, transform);
        ring.Multimesh.SetInstanceColor(i, colour);
        ring.Multimesh.SetInstanceCustomData(i, new Color((float)_now, _def.Fade_s, rightFoot ? 1f : 0f, 0f));
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
