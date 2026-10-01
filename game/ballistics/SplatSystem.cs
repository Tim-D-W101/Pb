using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Game.Player;
using Pb.Sim.Events;

namespace Pb.Game.Ballistics;

/// <summary>What a splat on a receiver sticks to, and how far from the hit point the receiver's surface may lie (m).</summary>
/// <param name="Node">The node the splat moves with.</param>
/// <param name="Reach">0 when the receiver is drawn as its hitboxes. A model posed to match them has its surface somewhere
/// near the hit point instead: inside the box, or outside it where the model bulges past it.</param>
public readonly record struct SplatAnchor(Node3D Node, float Reach = 0f);

/// <summary>
/// Team-coloured paint splats (spec §1.3): a pool of Decal nodes with a cap. Once full, the
/// oldest splats fade progressively and are reused first. Splats on moving receivers (targets,
/// players) are parented to them so they move along.
/// </summary>
public partial class SplatSystem : Node3D, ISimEventListener
{
    private readonly List<Decal> _decals = new();
    private readonly RandomNumberGenerator _rng = new();
    private ImageTexture[] _textures = Array.Empty<ImageTexture>();
    private Color[] _teamColors = Array.Empty<Color>();
    private Func<int, int, Vector3, SplatAnchor?>? _receiverNode;
    private SplatDef _def = null!;
    private int _next;

    public int ActiveCount => _decals.Count;

    public int Capacity => _def?.Cap ?? 0;

    /// <param name="receiverNode">What a splat on (receiver id, hitbox part, hit point) should stick to, or null for the world.</param>
    public void Initialize(PresentationDef view, Func<int, int, Vector3, SplatAnchor?> receiverNode)
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
            Vector3 position = e.Position.ToGodot();
            SplatAnchor anchor = (e.TargetId >= 0 ? _receiverNode?.Invoke(e.TargetId, e.Extra, position) : null) ?? new SplatAnchor(this);
            Spawn(position, e.Normal.ToGodot(), _teamColors[e.Team % _teamColors.Length], anchor);
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

    private void Spawn(Vector3 position, Vector3 normal, Color color, SplatAnchor anchor)
    {
        Node3D parent = anchor.Node;
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

        // The box straddles the hit point, deeper by the anchor's reach each way; normal fade keeps
        // the paint off the far side.
        float size = _rng.RandfRange(_def.SizeMin_m, _def.SizeMax_m);
        decal.Size = new Vector3(size, _def.Depth_m + 2f * anchor.Reach, size);
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
