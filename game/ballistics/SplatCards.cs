using System;
using Godot;
using Pb.Game.Core;
using Pb.Game.World;

namespace Pb.Game.Ballistics;

/// <summary>
/// The world's paint as cards in one batch (splat_cards.gdshader), for a PC on which the decals cost too much (the stress
/// mode measures it; the graphics settings' "Paint as cards"): a ring of <c>cap</c> cards lying on the surfaces they
/// struck, in the wet look of <see cref="WetSplats"/>, the oldest reused first and faded out across <c>fadeWindow</c> as
/// the decals are. A card lies flat, so <see cref="SplatSystem"/> draws none for a splat across an edge or a corner.
/// </summary>
public partial class SplatCards : MultiMeshInstance3D
{
    /// <summary>Cards sit this far off the surface, each a hair further than the one before so overlaps don't flicker (as old paint's).</summary>
    private const float Lift = 0.004f, Layer = 0.0003f;

    private ShaderMaterial _material = null!;
    private long[] _placedAs = Array.Empty<long>();
    private int _fadeWindow;
    private int _next;
    private long _placed;

    /// <summary>Cards drawn (the oldest still among them once the ring is full).</summary>
    public int Count { get; private set; }

    /// <summary>An empty ring of <c>cap</c> cards in the look of <paramref name="def"/>.</summary>
    public void Build(SplatDef def, WetSplats wet)
    {
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/splat_cards.gdshader") };
        Restyle(def, wet);
        int cap = Math.Max(1, def.Cap);
        Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = OldPaint.CardMesh(),
            InstanceCount = cap, VisibleInstanceCount = 0,
        };
        MaterialOverride = _material;
        CastShadow = ShadowCastingSetting.Off;
        _placedAs = new long[cap];
        Array.Fill(_placedAs, -1L);
        _next = 0;
        _placed = 0;
        Count = 0;
    }

    /// <summary>The cards' look from <paramref name="def"/> again (the data reloaded), keeping those already down.</summary>
    public void Restyle(SplatDef def, WetSplats wet)
    {
        _material.SetShaderParameter("atlas", wet.Atlas);
        _material.SetShaderParameter("drying_s", def.Drying_s);
        _material.SetShaderParameter("wet_roughness", def.WetRoughness);
        _material.SetShaderParameter("dry_roughness", def.DryRoughness);
        _material.SetShaderParameter("wet_darken", def.WetDarken);
        _material.SetShaderParameter("bump", def.Bump);
        _material.SetShaderParameter("fade_start", def.DistanceFadeBegin_m);
        _material.SetShaderParameter("fade_end", def.DistanceFadeBegin_m + def.DistanceFadeLength_m);
        _fadeWindow = def.FadeWindow;
    }

    /// <summary>
    /// A card for a splat at <paramref name="position"/> (on the surface), in the decal's frame <paramref name="basis"/>
    /// (the cell's across along X, out of the surface along Y, its down along Z), <paramref name="size"/> across: its index,
    /// and which placing it is (for taking it off again).
    /// </summary>
    public int Place(Vector3 position, Basis basis, float size, int shape, Color colour, double now, out long placing)
    {
        int i = _next;
        _next = (_next + 1) % _placedAs.Length;
        Count = Math.Max(Count, i + 1);
        Multimesh.VisibleInstanceCount = Count;
        Vector3 normal = basis.Y.Normalized();
        // The card's square lies in its XY plane facing +Z, its v down −Y: the decal's X across and Z down.
        var frame = new Basis(basis.X.Normalized() * size, -basis.Z.Normalized() * size, normal);
        Multimesh.SetInstanceTransform(i, new Transform3D(frame, position + normal * (Lift + (i % 8) * Layer)));
        Multimesh.SetInstanceColor(i, colour.SrgbToLinear() with { A = 1f });
        Multimesh.SetInstanceCustomData(i, new Color(shape, (float)now, 0f, 0f));
        placing = _placed++;
        _placedAs[i] = placing;
        if (Count == _placedAs.Length)
        {
            FadeOldest();
        }

        return i;
    }

    /// <summary>Takes card <paramref name="index"/> off, if it's still that <paramref name="placing"/>'s.</summary>
    public void Hide(int index, long placing)
    {
        if (index >= 0 && index < Count && _placedAs[index] == placing)
        {
            Multimesh.SetInstanceColor(index, new Color(0f, 0f, 0f, 0f));
            _placedAs[index] = -1;
        }
    }

    public override void _Process(double delta)
    {
        if (Count > 0)
        {
            _material.SetShaderParameter("paint_now", (float)PaintSlots.Now);
        }
    }

    /// <summary>The oldest cards (next in line for reuse) fade linearly across the fade window, as the decals do.</summary>
    private void FadeOldest()
    {
        int window = Math.Min(_fadeWindow, Count);
        for (int k = 0; k < window; k++)
        {
            int i = (_next + k) % Count;
            if (_placedAs[i] >= 0)
            {
                Color c = Multimesh.GetInstanceColor(i);
                Multimesh.SetInstanceColor(i, c with { A = (k + 1f) / (window + 1f) });
            }
        }
    }
}
