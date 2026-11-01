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
/// <param name="CullMask">The render layers the splat paints (0: everything but the viewmodel and the doors).</param>
public readonly record struct SplatAnchor(Node3D Node, float Reach = 0f, uint CullMask = 0);

/// <summary>
/// Team-coloured paint splats (spec §1.3): a pool of Decal nodes with a cap. Once full, the
/// oldest splats fade progressively and are reused first. Splats on moving receivers (targets,
/// players) are parented to them so they move along. Since Phase 5 a splat takes the shape of how the ball hit: round
/// head-on, stretched along the ball's travel at a glance (a streak with spatter thrown on ahead from
/// <see cref="SplatDef.StreakFrom"/>); on a body, less so.
/// </summary>
public partial class SplatSystem : Node3D, ISimEventListener
{
    private readonly List<Decal> _decals = new();
    private readonly RandomNumberGenerator _rng = new();
    private ImageTexture[] _textures = Array.Empty<ImageTexture>();
    private ImageTexture[] _streaks = Array.Empty<ImageTexture>();
    private Color[] _teamColors = Array.Empty<Color>();
    private Func<int, int, Vector3, SplatAnchor?>? _receiverNode;
    private Func<int, SplatAnchor?>? _colliderNode;
    private SplatDef _def = null!;
    private int _next;

    public int ActiveCount => _decals.Count;

    public int Capacity => _def?.Cap ?? 0;

    /// <summary>What world splats paint: everything but your marker and the doors (a splat on a door moves with it).</summary>
    public const uint WorldMask = 0xFFFFF & ~ViewModel.RenderLayer & ~World.DoorViews.RenderLayer;

    /// <param name="receiverNode">What a splat on (receiver id, hitbox part, hit point) should stick to, or null for the world.</param>
    /// <param name="colliderNode">What a splat on a moving paint collider (a door) should stick to, or null for the world.</param>
    public void Initialize(PresentationDef view, Func<int, int, Vector3, SplatAnchor?> receiverNode, Func<int, SplatAnchor?>? colliderNode = null)
    {
        _receiverNode = receiverNode;
        _colliderNode = colliderNode;
        _rng.Seed = 1234;
        _textures = SplatTextures.Create(variants: 6, size: 128, seed: 777);
        _streaks = SplatTextures.CreateStreaks(variants: 4, size: 128, seed: 778);
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
            SplatAnchor anchor = (e.TargetId >= 0 ? _receiverNode?.Invoke(e.TargetId, e.Extra, position)
                : e.ColliderId >= 0 ? _colliderNode?.Invoke(e.ColliderId) : null) ?? new SplatAnchor(this);
            Spawn(position, e.Normal.ToGodot(), e.Velocity.ToGodot(), _teamColors[e.Team % _teamColors.Length], anchor);
        }
    }

    /// <summary>Streaked splats placed so far (the rest are round).</summary>
    public int Streaks { get; private set; }

    /// <summary>
    /// How much a ball arriving at <paramref name="velocity"/> on a surface facing <paramref name="normal"/> stretches its
    /// splat: 1 head-on, 1 / cos(angle from the normal) at a glance, up to <paramref name="most"/>.
    /// </summary>
    public static float Stretch(Vector3 velocity, Vector3 normal, float most)
    {
        float speed = velocity.Length();
        if (speed < 1e-3f)
        {
            return 1f;
        }

        float cos = Mathf.Abs(velocity.Dot(normal)) / speed;
        return Mathf.Clamp(1f / Mathf.Max(cos, 1e-3f), 1f, most);
    }

    public override void _ExitTree()
    {
        SplatTextures.Release(_textures);
        SplatTextures.Release(_streaks);
        _textures = Array.Empty<ImageTexture>();
        _streaks = Array.Empty<ImageTexture>();
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

    private void Spawn(Vector3 position, Vector3 normal, Vector3 velocity, Color color, SplatAnchor anchor)
    {
        Node3D parent = anchor.Node;
        Decal decal;
        if (_decals.Count < _def.Cap)
        {
            decal = new Decal
            {
                CullMask = WorldMask,
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
        decal.CullMask = anchor.CullMask != 0 ? anchor.CullMask : WorldMask;
        float size = _rng.RandfRange(_def.SizeMin_m, _def.SizeMax_m);
        // On a body (a model round its hitboxes) a splat wraps a curved surface, so it's stretched less.
        float stretch = Stretch(velocity, normal, anchor.Reach > 0f ? Mathf.Min(_def.StretchMax, 1.6f) : _def.StretchMax);
        bool streak = stretch >= _def.StreakFrom;
        Streaks += streak ? 1 : 0;
        // Longer along the travel, a little narrower across it.
        float along = Mathf.Pow(stretch, 0.75f), across = Mathf.Pow(stretch, -0.25f);
        decal.Size = new Vector3(size * along, _def.Depth_m + 2f * anchor.Reach, size * across);
        decal.TextureAlbedo = streak ? _streaks[_rng.RandiRange(0, _streaks.Length - 1)] : _textures[_rng.RandiRange(0, _textures.Length - 1)];
        decal.Modulate = color;
        decal.GlobalTransform = new Transform3D(SplatBasis(normal, velocity, stretch > 1.05f), position);

        if (_decals.Count == _def.Cap)
        {
            FadeOldest();
        }
    }

    /// <summary>
    /// The decal's frame: up along the surface's normal (it projects along −Y), and, for a stretched splat, its x along the
    /// ball's travel across the surface (the texture's u); otherwise turned at random.
    /// </summary>
    private Basis SplatBasis(Vector3 normal, Vector3 velocity, bool along)
    {
        Vector3 n = normal.Normalized();
        Vector3 tangent = velocity - n * velocity.Dot(n);
        if (!along || tangent.LengthSquared() < 1e-6f)
        {
            return Conv.BasisFromUp(normal, _rng.RandfRange(0f, Mathf.Tau));
        }

        Vector3 x = tangent.Normalized();
        return new Basis(x, n, x.Cross(n));
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
