using System;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Collision;
using Pb.Sim.Events;

namespace Pb.Game.World;

/// <summary>
/// Where a ball strikes an inflatable (the Sports Ground's bunkers, the training ground's), its fabric dents in and
/// shivers out from there for a moment (weathered.gdshader's wobble): the last few hits, in world space and when they
/// landed, and the clock, handed to every inflatable material the level has made. Presentation only.
/// </summary>
public partial class InflatableWobble : Node, ISimEventListener
{
    /// <summary>The break model's surface whose kit materials wobble.</summary>
    public const string Surface = "inflatable";

    /// <summary>The most hits the shader holds (weathered.gdshader's wobble_hits).</summary>
    private const int Most = 8;

    private static readonly Vector4[] Settled = Fill(new Vector4[Most]);

    private readonly Vector4[] _hits = Fill(new Vector4[Most]);
    private MaterialLibrary? _materials;
    private InflatablesDef _def = new();
    private SurfaceId _surface;
    private int _next;
    private double _now;
    private double _lastHit = double.NegativeInfinity;

    /// <summary>Hits on inflatables so far.</summary>
    public int Hits { get; private set; }

    /// <summary>A newly made inflatable material: wobbling, nothing having struck it yet.</summary>
    public static void Prepare(ShaderMaterial material)
    {
        material.SetShaderParameter("wobble", true);
        material.SetShaderParameter("wobble_hits", Settled);
    }

    public void Initialize(MaterialLibrary materials, SurfaceId surface, InflatablesDef def)
    {
        _materials = materials;
        _surface = surface;
        _def = def;
    }

    public void OnSimEvent(in SimEvent e)
    {
        if (_materials is null || e.Type is not (SimEventType.BallBroke or SimEventType.BallBounced) || e.Surface != _surface)
        {
            return;
        }

        _hits[_next] = new Vector4(e.Position.X, e.Position.Y, e.Position.Z, (float)_now);
        _next = (_next + 1) % Most;
        _lastHit = _now;
        Hits++;
        foreach (ShaderMaterial material in _materials.Wobbly)
        {
            material.SetShaderParameter("wobble_hits", _hits);
            material.SetShaderParameter("wobble_depth_m", _def.WobbleDepth_m);
            material.SetShaderParameter("wobble_reach_m", _def.WobbleReach_m);
            material.SetShaderParameter("wobble_fade_s", _def.WobbleFade_s);
            material.SetShaderParameter("wobble_hz", _def.WobbleHz);
        }
    }

    public override void _Process(double delta)
    {
        _now += delta;
        if (_materials is null || _now - _lastHit > _def.WobbleFade_s + 0.1)
        {
            return;
        }

        foreach (ShaderMaterial material in _materials.Wobbly)
        {
            material.SetShaderParameter("wobble_now", (float)_now);
        }
    }

    /// <summary>Hits long settled (so nothing moves until something strikes).</summary>
    private static Vector4[] Fill(Vector4[] hits)
    {
        Array.Fill(hits, new Vector4(0f, -1000f, 0f, -1000f));
        return hits;
    }
}
