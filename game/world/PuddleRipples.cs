using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Events;

namespace Pb.Game.World;

/// <summary>
/// Ripples on the puddles (presentation.jsonc "ripples"): where a foot comes down, or a ball breaks or
/// bounces, in a puddle's water (<see cref="GroundDetail.Puddles"/>), rings spread out from it and fade,
/// never wider than the water reaches from there. From the sim's footstep and ball events, so bots and
/// people make them alike; flat cards on the water in one MultiMesh, used round as a ring
/// (ripples.gdshader). Looks only.
/// </summary>
public partial class PuddleRipples : Node3D, ISimEventListener
{
    private static readonly StringName Now = "now";

    /// <summary>How far above the puddle's card the rings lie (m).</summary>
    private const float Lift = 0.004f;

    private readonly List<GroundDetail.Puddle> _puddles = new();
    private RipplesDef _def = null!;
    private MultiMesh? _multimesh;
    private ShaderMaterial _material = null!;
    private readonly Random _random = new(0x41BB);
    private double _now;
    private int _next;

    /// <summary>How many rings have spread so far.</summary>
    public int Raised { get; private set; }

    public void Initialize(IReadOnlyList<GroundDetail.Puddle> puddles, RipplesDef def)
    {
        _puddles.Clear();
        _puddles.AddRange(puddles);
        _def = def;
        // Built even with no puddles yet: a drip's pool may still bring water (AddWater).
        if (def.Max == 0)
        {
            return;
        }

        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ripples.gdshader") };
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
            Name = "Rings",
            Multimesh = _multimesh,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    public void OnSimEvent(in SimEvent e)
    {
        if (_multimesh is null)
        {
            return;
        }

        float size;
        int rings;
        if (e.Type == SimEventType.Footstep)
        {
            bool land = (FootstepKind)e.Extra == FootstepKind.Land;
            size = land ? _def.LandSize_m : _def.StepSize_m;
            rings = land ? 3 : 2;
        }
        else if (e.Type is SimEventType.BallBroke or SimEventType.BallBounced && e.Normal.Y > 0.5f)
        {
            size = _def.BallSize_m;
            rings = 2;
        }
        else
        {
            return;
        }

        Splash(e.Position.ToGodot(), size, rings);
    }

    /// <summary>More water for ripples to spread on (a drip's pool, say), in the same terms as <see cref="GroundDetail.Puddles"/>.</summary>
    public void AddWater(GroundDetail.Puddle water) => _puddles.Add(water);

    /// <summary>
    /// Spreads <paramref name="rings"/> rings up to <paramref name="size"/> across from <paramref name="at"/>, if
    /// it's in a puddle's water; false when it isn't.
    /// </summary>
    public bool Splash(Vector3 at, float size, int rings)
    {
        if (_multimesh is null)
        {
            return false;
        }

        foreach (GroundDetail.Puddle p in _puddles)
        {
            Vector3 d = at - p.Center;
            if (MathF.Abs(d.Y) > 0.3f)
            {
                continue;
            }

            // Where the point lies in the puddle's own frame, as a share of the way out to its water's edge.
            float c = MathF.Cos(p.Yaw), s = MathF.Sin(p.Yaw);
            float x = (d.X * c - d.Z * s) / p.HalfWidth, z = (d.X * s + d.Z * c) / p.HalfLength;
            float out_ = MathF.Sqrt(x * x + z * z);
            if (out_ > 1f)
            {
                continue;
            }

            // Never wider than the water reaches from here.
            float room = 2f * (1f - out_) * MathF.Min(p.HalfWidth, p.HalfLength);
            float across = MathF.Min(size, MathF.Max(room, 0.15f));
            var on = new Vector3(at.X, p.Center.Y + Lift, at.Z);
            for (int k = 0; k < rings; k++)
            {
                Raise(on, across * (1f - 0.22f * k), k * 0.14f);
            }

            return true;
        }

        return false;
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

    private void Raise(Vector3 at, float across, float delay)
    {
        int i = _next;
        _next = (_next + 1) % _def.Max;
        Raised++;
        _multimesh!.SetInstanceTransform(i, new Transform3D(Basis.FromScale(new Vector3(across, 1f, across)), at));
        _multimesh.SetInstanceColor(i, new Color(1f, 1f, 1f, _def.Opacity));
        _multimesh.SetInstanceCustomData(i, new Color((float)_now + delay, _def.Lifetime_s, (float)_random.NextDouble() * 6.3f, 0f));
    }
}
