using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;

namespace Pb.Game.Ballistics;

/// <summary>
/// The paint on one surface as its shaders draw it (paint.gdshaderinc): a character's body, an item of gear, your
/// first-person rig. It keeps the last <c>paintSlots</c> splats in the surface's own frame (its owner says where each
/// lands in it), the oldest painted over first, each drying from wet to dry, and hands them to every material that
/// draws the surface. <see cref="SplatSystem"/> dries everything painted.
/// </summary>
public sealed class PaintSlots
{
    /// <summary>The most a shader holds (paint.gdshaderinc's arrays).</summary>
    public const int Most = 16;

    /// <summary>Every surface with paint still drying, for <see cref="DryAll"/>.</summary>
    private static readonly List<PaintSlots> Wet = new();

    /// <summary>Handles run on across every surface, so one names its splat wherever it is.</summary>
    private static int _lastId;

    private readonly Vector4[] _at = new Vector4[Most];
    private readonly Vector4[] _across = new Vector4[Most];
    private readonly Vector4[] _facing = new Vector4[Most];
    private readonly Vector4[] _colour = new Vector4[Most];
    private readonly double[] _born = new double[Most];
    private readonly int[] _id = new int[Most];
    private readonly List<ShaderMaterial> _materials = new();
    private readonly int _slots;
    private readonly float _drying_s;
    private int _count;
    private int _next;
    private double _pushedAt = double.NegativeInfinity;

    public PaintSlots(SplatDef def)
    {
        _slots = Math.Clamp(def.PaintSlots, 1, Most);
        _drying_s = def.Drying_s;
        Def = def;
    }

    public SplatDef Def { get; }

    /// <summary>Whether any of its paint is still drying (so it needs drying a little more each moment).</summary>
    public bool Drying { get; private set; }

    /// <summary>Draws this surface's paint in <paramref name="material"/> too (with the splats' atlas and their look).</summary>
    public void Bind(ShaderMaterial material, WetSplats splats)
    {
        material.SetShaderParameter("splat_atlas", splats.Atlas);
        material.SetShaderParameter("splat_wet_roughness", Def.WetRoughness);
        material.SetShaderParameter("splat_dry_roughness", Def.DryRoughness);
        material.SetShaderParameter("splat_wet_darken", Def.WetDarken);
        material.SetShaderParameter("splat_bump", Def.Bump);
        _materials.Add(material);
        Push(material);
    }

    /// <summary>
    /// A splat landing at <paramref name="at"/> facing <paramref name="facing"/> (the surface's frame, unit), turned so
    /// <paramref name="across"/> (unit, square to it) runs across it, <paramref name="radius"/> across, finding the
    /// surface up to <paramref name="reach"/> behind its middle. Its handle, for taking it off again.
    /// </summary>
    public int Add(Vector3 at, Vector3 facing, Vector3 across, float radius, float reach, int shape, Color colour, double now)
    {
        int i = _next;
        _next = (_next + 1) % _slots;
        _count = Math.Max(_count, i + 1);
        Color linear = colour.SrgbToLinear();
        _at[i] = new Vector4(at.X, at.Y, at.Z, radius);
        _across[i] = new Vector4(across.X, across.Y, across.Z, shape);
        _facing[i] = new Vector4(facing.X, facing.Y, facing.Z, 1f);
        _colour[i] = new Vector4(linear.R, linear.G, linear.B, reach);
        _born[i] = now;
        _id[i] = ++_lastId;
        if (!Drying)
        {
            Drying = true;
            Wet.Add(this);
        }

        PushAll(now);
        return _id[i];
    }

    /// <summary>Dries every surface's paint as of <paramref name="now"/> (s): once a frame (<see cref="SplatSystem"/>).</summary>
    public static void DryAll(double now)
    {
        for (int i = Wet.Count - 1; i >= 0; i--)
        {
            PaintSlots slots = Wet[i];
            slots.Dry(now);
            if (!slots.Drying)
            {
                Wet.RemoveAt(i);
            }
        }
    }

    /// <summary>The clock paint dries by (s): the game's own, so it stands still while the game does.</summary>
    public static double Now { get; private set; }

    /// <summary>Moves the paint's clock on (<see cref="SplatSystem"/> each frame; a demo skips ahead).</summary>
    public static void Advance(double seconds) => Now += Math.Max(0.0, seconds);

    /// <summary>Takes splat <paramref name="handle"/> off (if it's still there); true if it was.</summary>
    public bool Withdraw(int handle)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_id[i] == handle && _at[i].W > 0f)
            {
                _at[i].W = 0f;
                PushAll(_pushedAt);
                return true;
            }
        }

        return false;
    }

    /// <summary>Takes all its paint off.</summary>
    public void Clear()
    {
        Array.Clear(_at);
        _count = 0;
        _next = 0;
        Drying = false;
        PushAll(_pushedAt);
    }

    /// <summary>Dries its paint as of <paramref name="now"/> (s), handing it on a few times a second while any is wet.</summary>
    private void Dry(double now)
    {
        if (!Drying || now - _pushedAt < 0.25)
        {
            return;
        }

        bool wet = false;
        for (int i = 0; i < _count; i++)
        {
            float w = Mathf.Clamp(1f - (float)((now - _born[i]) / _drying_s), 0f, 1f);
            _facing[i].W = w;
            wet |= w > 0f && _at[i].W > 0f;
        }

        Drying = wet;
        PushAll(now);
    }

    private void PushAll(double now)
    {
        _pushedAt = now;
        foreach (ShaderMaterial material in _materials)
        {
            Push(material);
        }
    }

    private void Push(ShaderMaterial material)
    {
        material.SetShaderParameter("splat_count", _count);
        material.SetShaderParameter("splat_at", _at);
        material.SetShaderParameter("splat_across", _across);
        material.SetShaderParameter("splat_facing", _facing);
        material.SetShaderParameter("splat_colour", _colour);
    }

    /// <summary>A direction square to <paramref name="normal"/>, turned by <paramref name="spin"/> about it.</summary>
    public static Vector3 Across(Vector3 normal, float spin)
    {
        Vector3 helper = Mathf.Abs(normal.Y) < 0.9f ? Vector3.Up : Vector3.Right;
        Vector3 a = helper.Cross(normal).Normalized();
        return a.Rotated(normal, spin).Normalized();
    }
}

/// <summary>A splat as it lands: where (world), facing which way, its colour, how big across (m), its shape and turn.</summary>
public readonly record struct Splat(Vector3 Point, Vector3 Normal, Color Colour, float Radius, int Shape, float Spin);

/// <summary>Something that draws the paint landing on it in its own shaders: a player (each part of them), your first-person rig.</summary>
public interface IPaintTarget
{
    /// <summary>
    /// Paints <paramref name="splat"/> on hitbox part <paramref name="part"/> in the look of <paramref name="splats"/> and
    /// <paramref name="def"/>; its handle, or 0 if it took none (it's a decal then).
    /// </summary>
    int Paint(int part, in Splat splat, WetSplats splats, SplatDef def);

    /// <summary>Takes splat <paramref name="handle"/> off again, if it's still there.</summary>
    void Withdraw(int handle);
}
