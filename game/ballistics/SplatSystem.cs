using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Game.Player;
using Pb.Sim.Events;

namespace Pb.Game.Ballistics;

/// <summary>
/// Team-coloured paint splats (spec §1.3): a pool of Decal nodes with a cap. Once full, the
/// oldest splats fade progressively and are reused first. Splats on moving receivers (targets,
/// later players) are parented to them so they move along.
/// </summary>
public partial class SplatSystem : Node3D, ISimEventListener
{
    private readonly List<Decal> _decals = new();
    private readonly RandomNumberGenerator _rng = new();
    private ImageTexture[] _textures = Array.Empty<ImageTexture>();
    private Color[] _teamColors = Array.Empty<Color>();
    private Func<int, int, Node3D?>? _receiverNode;
    private SplatDef _def = null!;
    private int _next;

    public int ActiveCount => _decals.Count;

    public int Capacity => _def?.Cap ?? 0;

    /// <param name="receiverNode">Node a splat on (receiver id, hitbox part) should stick to, or null for the world.</param>
    public void Initialize(PresentationDef view, Func<int, int, Node3D?> receiverNode)
    {
        _receiverNode = receiverNode;
        _rng.Seed = 1234;
        _textures = SplatTextures.Create(variants: 6, size: 128, seed: 777);
        ApplyView(view);
    }

    public void ApplyView(PresentationDef view)
    {
        _def = view.Splat;
        _teamColors = new Color[view.TeamColors.Length];
        for (int i = 0; i < _teamColors.Length; i++)
        {
            _teamColors[i] = Color.FromHtml(view.TeamColors[i]);
        }

        while (_decals.Count > _def.Cap)
        {
            _decals[^1].QueueFree();
            _decals.RemoveAt(_decals.Count - 1);
        }

        _next %= Math.Max(1, _decals.Count);
    }

    public void OnSimEvent(in SimEvent e)
    {
        if (e.Type == SimEventType.BallBroke && _def.Cap > 0)
        {
            Node3D parent = (e.TargetId >= 0 ? _receiverNode?.Invoke(e.TargetId, e.Extra) : null) ?? this;
            Spawn(e.Position.ToGodot(), e.Normal.ToGodot(), _teamColors[e.Team % _teamColors.Length], parent);
        }
    }

    public void ClearAll()
    {
        foreach (Decal decal in _decals)
        {
            decal.QueueFree();
        }

        _decals.Clear();
        _next = 0;
    }

    private void Spawn(Vector3 position, Vector3 normal, Color color, Node3D parent)
    {
        Decal decal;
        if (_decals.Count < _def.Cap)
        {
            decal = new Decal
            {
                CullMask = 0xFFFFF & ~ViewModel.RenderLayer,
                NormalFade = _def.NormalFade,
                UpperFade = 0.3f,
                LowerFade = 0.3f,
                DistanceFadeEnabled = true,
                DistanceFadeBegin = _def.DistanceFadeBegin_m,
                DistanceFadeLength = _def.DistanceFadeLength_m,
            };
            parent.AddChild(decal);
            _decals.Add(decal);
        }
        else
        {
            decal = _decals[_next];
            _next = (_next + 1) % _decals.Count;
            if (decal.GetParent() != parent)
            {
                decal.Reparent(parent, keepGlobalTransform: false);
            }
        }

        float size = _rng.RandfRange(_def.SizeMin_m, _def.SizeMax_m);
        decal.Size = new Vector3(size, _def.Depth_m, size);
        decal.TextureAlbedo = _textures[_rng.RandiRange(0, _textures.Length - 1)];
        decal.Modulate = color;
        decal.GlobalTransform = new Transform3D(Conv.BasisFromUp(normal, _rng.RandfRange(0f, Mathf.Tau)), position);

        if (_decals.Count == _def.Cap)
        {
            FadeOldest();
        }
    }

    /// <summary>Oldest splats (next in line for reuse) fade linearly across the fade window.</summary>
    private void FadeOldest()
    {
        int window = Math.Min(_def.FadeWindow, _decals.Count);
        for (int k = 0; k < window; k++)
        {
            Decal decal = _decals[(_next + k) % _decals.Count];
            Color c = decal.Modulate;
            c.A = (k + 1f) / (window + 1f);
            decal.Modulate = c;
        }

        // The splat just placed is fully opaque.
        Decal newest = _decals[(_next - 1 + _decals.Count) % _decals.Count];
        Color n = newest.Modulate;
        n.A = 1f;
        newest.Modulate = n;
    }
}
