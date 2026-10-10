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
/// Team-coloured paint splats (spec §1.3), in the wet look of <see cref="WetSplats"/>: fresh paint is glossy, its colour
/// deeper, and dries to satin over <c>drying_s</c>. On players and their gear the paint is painted in their own shaders
/// (<see cref="IPaintTarget"/>), so it wraps round them and moves with them; everything else takes a decal from a pool
/// with a cap: once full, the oldest splats fade progressively and are reused first, and those on moving receivers
/// (targets, doors) are parented to them. Where decals cost the PC too much (<see cref="Cards"/>), what stands still
/// takes a card instead (<see cref="SplatCards"/>), on flat surfaces only. A ball breaking close to your eye spatters
/// your marker and gloves too (<see cref="FirstPerson"/>). The last few hundred splats are remembered by ball, so a
/// splat a joining copy drew that the server says didn't happen can come off again (<see cref="SimEventType.SplatWithdrawn"/>).
/// </summary>
public partial class SplatSystem : Node3D, ISimEventListener
{
    /// <summary>How many recent splats are remembered by ball, for taking one off.</summary>
    private const int Remembered = 256;

    private readonly List<Decal> _decals = new();
    private readonly List<double> _bornAt = new();
    private readonly List<long> _placedAs = new();
    private readonly List<Color> _dryColour = new();
    private readonly List<int> _shape = new();
    private readonly RandomNumberGenerator _rng = new();
    private readonly Record[] _recent = new Record[Remembered];
    private SplatCards? _cards;
    private bool _cardsOn;
    private WetSplats _wet = null!;
    private Color[] _teamColors = Array.Empty<Color>();
    private Func<int, int, Vector3, SplatAnchor?>? _receiverNode;
    private Func<int, SplatAnchor?>? _colliderNode;
    private Func<int, IPaintTarget?>? _paintTarget;
    private SplatDef _def = null!;
    private int _next;
    private long _placed;
    private long _dried;
    private int _recentNext;

    /// <summary>A splat placed by a ball: a decal or a card (its index and placing), or paint on a target (its handle).</summary>
    private struct Record
    {
        public int Owner;
        public uint Sequence;
        public int Decal;
        public int Card;
        public long Placing;
        public IPaintTarget? Target;
        public int Handle;
    }

    /// <summary>World splats drawn: decals, and cards once they're on.</summary>
    public int ActiveCount => _decals.Count + (_cards?.Count ?? 0);

    /// <summary>Decals in the pool.</summary>
    public int DecalCount => _decals.Count;

    /// <summary>World splats drawn as cards.</summary>
    public int CardCount => _cards?.Count ?? 0;

    /// <summary>
    /// What stands still takes cards instead of decals (the graphics settings' "Paint as cards", which the stress mode
    /// turns on when the decals cost too much). Turned on, the decals already on what stands still become cards too
    /// (where they lie flat), so their cost goes at once; doors and targets keep taking decals. Turned off, the cards stay.
    /// </summary>
    public bool Cards
    {
        get => _cardsOn;
        set
        {
            if (value != _cardsOn)
            {
                _cardsOn = value;
                if (value)
                {
                    MoveDecalsToCards();
                }
            }
        }
    }

    /// <summary>The level's paint collision, for the cards: one goes down only where the surface is flat under all of it.</summary>
    public Pb.Sim.Collision.CollisionWorld? Collision { get; set; }

    /// <summary>World splats a card couldn't take (across an edge or a corner), so not drawn.</summary>
    public int CardsSkipped { get; private set; }

    public int Capacity => _def?.Cap ?? 0;

    /// <summary>Splats painted in players' shaders so far.</summary>
    public int PaintedOnPlayers { get; private set; }

    /// <summary>Breaks close enough to your eye to spatter your marker and gloves so far.</summary>
    public int Spattered { get; private set; }

    /// <summary>Your first-person rig, which a ball breaking close to your eye spatters (null: no one's).</summary>
    public Func<ViewModel?>? FirstPerson { get; set; }

    /// <summary>What world splats paint: everything but your marker and the doors (a splat on a door moves with it).</summary>
    public const uint WorldMask = 0xFFFFF & ~ViewModel.RenderLayer & ~World.DoorViews.RenderLayer;

    /// <param name="receiverNode">What a splat on (receiver id, hitbox part, hit point) should stick to, or null for the world.</param>
    /// <param name="colliderNode">What a splat on a moving paint collider (a door) should stick to, or null for the world.</param>
    /// <param name="paintTarget">Who draws the paint on receiver id in their own shaders (a player), or null for a decal.</param>
    public void Initialize(PresentationDef view, Func<int, int, Vector3, SplatAnchor?> receiverNode, Func<int, SplatAnchor?>? colliderNode = null,
        Func<int, IPaintTarget?>? paintTarget = null)
    {
        _receiverNode = receiverNode;
        _colliderNode = colliderNode;
        _paintTarget = paintTarget;
        _rng.Seed = 1234;
        ForgetAll();
        ApplyView(view);
    }

    public void ApplyView(PresentationDef view)
    {
        if (_def is not null && _decals.Count > 0 && view.Splat.Cap != _def.Cap)
        {
            // Another pool size: start it again (the placings no longer line up with it).
            ClearAll();
        }

        bool rebuild = view.Splat.Cap != _def?.Cap;
        _def = view.Splat;
        _wet = WetSplats.Get(_def);
        if (rebuild)
        {
            _cards?.Build(_def, _wet);
        }
        else
        {
            _cards?.Restyle(_def, _wet);
        }

        _teamColors = new Color[view.TeamColors.Length];
        for (int i = 0; i < _teamColors.Length; i++)
        {
            _teamColors[i] = Color.FromHtml(view.TeamColors[i]);
        }

        _next %= Math.Max(1, _decals.Count);
    }

    public void OnSimEvent(in SimEvent e)
    {
        if (e.Type == SimEventType.SplatWithdrawn)
        {
            Withdraw(e.PlayerId, e.ShotSequence);
            return;
        }

        if (e.Type != SimEventType.BallBroke || _def.Cap <= 0)
        {
            return;
        }

        Vector3 position = e.Position.ToGodot();
        Vector3 normal = e.Normal.ToGodot();
        Color colour = _teamColors[e.Team % _teamColors.Length];
        float size = _rng.RandfRange(_def.SizeMin_m, _def.SizeMax_m);
        if (FirstPerson?.Invoke() is { } rig && rig.IsVisibleInTree() && rig.Eye.DistanceSquaredTo(position) < _def.SpatterReach_m * _def.SpatterReach_m)
        {
            // Close to your eye: a few drops of it land on your marker and gloves too.
            rig.Spatter(position, colour, _rng.RandiRange(_def.SpatterDrops[0], _def.SpatterDrops[1]), _wet, _def, _rng);
            Spattered++;
        }

        if (e.TargetId >= 0 && _paintTarget?.Invoke(e.TargetId) is { } target)
        {
            // On a player: in their own shader, a round splat a little smaller (cloth and curves take less of it).
            int handle = target.Paint(e.Extra, new Splat(position, normal, colour, size * 0.4f, _rng.RandiRange(0, 4), _rng.RandfRange(0f, Mathf.Tau)), _wet, _def);
            if (handle != 0)
            {
                PaintedOnPlayers++;
                Remember(e, -1, 0, target, handle);
                return;
            }
        }

        SplatAnchor anchor = (e.TargetId >= 0 ? _receiverNode?.Invoke(e.TargetId, e.Extra, position)
            : e.ColliderId >= 0 ? _colliderNode?.Invoke(e.ColliderId) : null) ?? new SplatAnchor(this);
        (int shape, Basis basis, float across) = Orient(normal, e.Velocity.ToGodot(), size);
        if (Cards && anchor.Node == this)
        {
            // A card, where the surface is flat under all of it; across an edge or a corner, none.
            if (!Flat(position, basis, across))
            {
                CardsSkipped++;
                return;
            }

            int card = MakeCards().Place(position, basis, across, shape, colour, PaintSlots.Now, out long placing);
            _recent[_recentNext] = new Record { Owner = e.PlayerId, Sequence = e.ShotSequence, Decal = -1, Card = card, Placing = placing };
            _recentNext = (_recentNext + 1) % Remembered;
            return;
        }

        int decal = Spawn(position, basis, shape, across, colour, anchor);
        Remember(e, decal, _placed - 1, null, 0);
    }

    public override void _Process(double delta)
    {
        PaintSlots.Advance(delta);
        double now = PaintSlots.Now;
        PaintSlots.DryAll(now);
        if (_def is null)
        {
            return;
        }

        // The decals dry in the order they were placed: the oldest still wet is the next to go.
        int count = _decals.Count;
        while (_dried < _placed && count > 0)
        {
            int i = (int)(_dried % count);
            if (_placedAs[i] != _dried)
            {
                // Placed again since (or taken off): nothing of this placing is left to dry.
                _dried++;
                continue;
            }

            if (now - _bornAt[i] < _def.Drying_s)
            {
                break;
            }

            Decal decal = _decals[i];
            decal.TextureOrm = _wet.DryOrm[_shape[i]];
            Color dry = _dryColour[i];
            decal.Modulate = new Color(dry, decal.Modulate.A);
            _dried++;
        }
    }

    public void ClearAll()
    {
        foreach (Decal decal in _decals)
        {
            decal.QueueFree();
        }

        _decals.Clear();
        _bornAt.Clear();
        _placedAs.Clear();
        _dryColour.Clear();
        _shape.Clear();
        _next = 0;
        _placed = 0;
        _dried = 0;
        _cards?.Build(_def, _wet);
        ForgetAll();
    }

    private SplatCards MakeCards()
    {
        if (_cards is null)
        {
            _cards = new SplatCards { Name = "Cards" };
            AddChild(_cards);
            _cards.Build(_def, _wet);
        }

        return _cards;
    }

    /// <summary>
    /// The decals on what stands still become cards, oldest first, where they lie flat (the rest go), keeping how wet each
    /// is; those on doors and targets stay decals.
    /// </summary>
    private void MoveDecalsToCards()
    {
        if (_def is null || _decals.Count == 0)
        {
            return;
        }

        SplatCards cards = MakeCards();
        int count = _decals.Count;
        int start = count < _def.Cap ? 0 : _next;
        var kept = new List<(Decal Decal, double Born, Color Dry, int Shape)>();
        for (int k = 0; k < count; k++)
        {
            int i = (start + k) % count;
            Decal decal = _decals[i];
            if (decal.GetParent() != this)
            {
                kept.Add((decal, _bornAt[i], _dryColour[i], _shape[i]));
                continue;
            }

            if (decal.Visible && _placedAs[i] >= 0 && Flat(decal.GlobalPosition, decal.GlobalBasis, decal.Size.X))
            {
                cards.Place(decal.GlobalPosition, decal.GlobalBasis, decal.Size.X, _shape[i], _dryColour[i], _bornAt[i], out _);
            }

            decal.QueueFree();
        }

        _decals.Clear();
        _bornAt.Clear();
        _placedAs.Clear();
        _dryColour.Clear();
        _shape.Clear();
        foreach ((Decal decal, double born, Color dry, int shape) in kept)
        {
            _placedAs.Add(_decals.Count);
            _decals.Add(decal);
            _bornAt.Add(born);
            _dryColour.Add(dry);
            _shape.Add(shape);
        }

        _next = 0;
        _placed = _decals.Count;
        _dried = 0;
        // What's remembered of them by ball no longer names them.
        for (int k = 0; k < Remembered; k++)
        {
            _recent[k].Decal = -1;
        }
    }

    /// <summary>No splat remembered by ball any more.</summary>
    private void ForgetAll()
    {
        for (int k = 0; k < Remembered; k++)
        {
            _recent[k] = new Record { Decal = -1, Card = -1 };
        }
    }

    /// <summary>Takes off the splat that ball (<paramref name="owner"/>'s shot <paramref name="sequence"/>) left, if it's remembered.</summary>
    public void Withdraw(int owner, uint sequence)
    {
        for (int k = 0; k < Remembered; k++)
        {
            ref Record r = ref _recent[k];
            if (r.Owner != owner || r.Sequence != sequence || (r.Target is null && r.Decal < 0 && r.Card < 0))
            {
                continue;
            }

            if (r.Target is { } target)
            {
                // Only while they're still drawn (someone dressed again since has their paint off already).
                if (target is not GodotObject node || IsInstanceValid(node))
                {
                    target.Withdraw(r.Handle);
                }
            }
            else if (r.Card >= 0)
            {
                _cards?.Hide(r.Card, r.Placing);
            }
            else if (r.Decal < _decals.Count && _placedAs[r.Decal] == r.Placing)
            {
                _decals[r.Decal].Visible = false;
                _placedAs[r.Decal] = -1;
            }

            r = default;
            r.Decal = -1;
            r.Card = -1;
        }
    }

    private void Remember(in SimEvent e, int decal, long placing, IPaintTarget? target, int handle)
    {
        _recent[_recentNext] = new Record { Owner = e.PlayerId, Sequence = e.ShotSequence, Decal = decal, Card = -1, Placing = placing, Target = target, Handle = handle };
        _recentNext = (_recentNext + 1) % Remembered;
    }

    /// <summary>
    /// A splat's shape and frame: a ball that struck at a slant sprays its paint on the way it was going (a longer splat),
    /// one that struck square splats round. Its frame's up is the surface's normal, the cell's across its X and down its Z.
    /// </summary>
    private (int Shape, Basis Basis, float Across) Orient(Vector3 normal, Vector3 velocity, float size)
    {
        Vector3 along = velocity - normal * velocity.Dot(normal);
        bool glancing = velocity.LengthSquared() > 1e-4f && along.Length() > 0.75f * velocity.Length();
        int shape = glancing ? 5 + _rng.RandiRange(0, 1) : _rng.RandiRange(0, 4);
        Basis basis = glancing ? Sprayed(normal, along.Normalized(), shape == 5 ? 0.35f : 3.6f) : Conv.BasisFromUp(normal, _rng.RandfRange(0f, Mathf.Tau));
        return (shape, basis, glancing ? size * 1.35f : size);
    }

    /// <summary>Whether a card <paramref name="across"/> wide in <paramref name="basis"/> at <paramref name="position"/> lies flat on one surface (as old paint's do).</summary>
    private bool Flat(Vector3 position, Basis basis, float across)
    {
        if (Collision is not { } world)
        {
            return true;
        }

        Vector3 normal = basis.Y.Normalized(), x = basis.X.Normalized(), z = basis.Z.Normalized();
        for (int k = 0; k < 4; k++)
        {
            Vector3 corner = position + (x * (k % 2 == 0 ? -0.42f : 0.42f) + z * (k < 2 ? -0.42f : 0.42f)) * across;
            if (!world.SweepSphere((corner + normal * 0.08f).ToSim(), (corner - normal * 0.08f).ToSim(), 0f, out Pb.Sim.Collision.SweepHit hit) ||
                hit.Normal.ToGodot().Dot(normal) < 0.95f || Mathf.Abs((hit.Point.ToGodot() - position).Dot(normal)) > 0.015f)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A decal for a splat (the next in the pool once it's full), of <paramref name="shape"/> in <paramref name="basis"/>; its index.</summary>
    private int Spawn(Vector3 position, Basis basis, int shape, float across, Color color, SplatAnchor anchor)
    {
        Node3D parent = anchor.Node;
        Decal decal;
        int index;
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
            index = _decals.Count;
            _decals.Add(decal);
            _bornAt.Add(0.0);
            _placedAs.Add(0);
            _dryColour.Add(color);
            _shape.Add(0);
        }
        else
        {
            index = _next;
            decal = _decals[index];
            _next = (_next + 1) % _decals.Count;
            if (decal.GetParent() != parent)
            {
                decal.Reparent(parent, keepGlobalTransform: false);
            }
        }

        // The box straddles the hit point, deeper by the anchor's reach each way; normal fade keeps the paint off the far side.
        decal.Visible = true;
        decal.CullMask = anchor.CullMask != 0 ? anchor.CullMask : WorldMask;
        decal.Size = new Vector3(across, _def.Depth_m + 2f * anchor.Reach, across);
        decal.TextureAlbedo = _wet.Albedo[shape];
        decal.TextureNormal = _wet.Normal[shape];
        decal.TextureOrm = _wet.WetOrm[shape];
        decal.Modulate = color.Darkened(_def.WetDarken);
        decal.GlobalTransform = new Transform3D(basis, position);
        _bornAt[index] = PaintSlots.Now;
        _placedAs[index] = _placed++;
        _dryColour[index] = color;
        _shape[index] = shape;

        if (_decals.Count == _def.Cap)
        {
            FadeOldest();
        }

        return index;
    }

    /// <summary>
    /// A decal's frame with its up along <paramref name="normal"/>, turned so the cell's spray (at <paramref name="turn"/>
    /// round from its u axis, towards v) runs along <paramref name="along"/>: the texture's u is the decal's X, v its Z.
    /// </summary>
    private static Basis Sprayed(Vector3 normal, Vector3 along, float turn)
    {
        float c = Mathf.Cos(turn), s = Mathf.Sin(turn);
        Vector3 x = (along * c - along.Cross(normal) * s).Normalized();
        Vector3 z = x.Cross(normal).Normalized();
        return new Basis(x, normal, z);
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
