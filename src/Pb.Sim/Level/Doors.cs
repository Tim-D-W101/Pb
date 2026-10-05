using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Players;

namespace Pb.Sim.Level;

/// <summary>A kind of door leaf (kit/doors.jsonc) in SI units, its material resolved.</summary>
public sealed class DoorKind
{
    public required string Id { get; init; }

    public required MaterialRef Material { get; init; }

    public required float Thickness { get; init; }

    public required float OpenTime { get; init; }

    public required float CloseTime { get; init; }

    /// <summary>How far a hinged leaf opens (rad).</summary>
    public required float Swing { get; init; }

    /// <summary>How far opening or shutting it carries (m).</summary>
    public required float Noise { get; init; }

    /// <summary>How the game draws it (presentation only).</summary>
    public required string Style { get; init; }
}

/// <summary>
/// One door leaf as hung in a level, in world space. A hinged leaf turns about the vertical line through
/// <see cref="Hinge"/>, lying along <see cref="Across"/> when shut and opening towards <see cref="Side"/> (either way for
/// a swing door); a sliding leaf slides from across the doorway towards its hinge edge, on its <see cref="Side"/> face.
/// </summary>
public sealed class DoorSpec
{
    public required DoorKind Kind { get; init; }

    /// <summary>Bottom of the hinge line (sliding: the hinge edge of the doorway, at the wall's middle).</summary>
    public required Vector3 Hinge { get; init; }

    /// <summary>Horizontal unit vector from the hinge edge across the doorway.</summary>
    public required Vector3 Across { get; init; }

    /// <summary>Horizontal unit vector to the side it opens into (a swing door: one of its two).</summary>
    public required Vector3 Side { get; init; }

    public required float Width { get; init; }

    public required float Height { get; init; }

    public required bool Sliding { get; init; }

    public required bool BothWays { get; init; }

    public required DoorStart Start { get; init; }

    /// <summary>The wall's thickness at the doorway (a sliding leaf runs just clear of its face).</summary>
    public required float WallThickness { get; init; }

    /// <summary>The other leaf of a pair, opened with this one, or −1.</summary>
    public required int Partner { get; init; }

    /// <summary>Which building or wall run it hangs in (index into <see cref="LevelLayout.Owners"/>).</summary>
    public required int Owner { get; init; }

    /// <summary>The doorway's aperture (index into <see cref="LevelLayout.Apertures"/>).</summary>
    public required int Aperture { get; init; }

    /// <summary>The middle of the doorway's sill line, where the closed leaf stands.</summary>
    public Vector3 ShutCenter => Hinge + Across * (Width * 0.5f);
}

/// <summary>Door rules (rules.jsonc "doors") in SI units.</summary>
public sealed class DoorRules
{
    public required float Reach { get; init; }

    /// <summary>Cosine of the largest angle off the view at which a door can be worked.</summary>
    public required float ConeCos { get; init; }

    public required float HoldTime { get; init; }

    public required float EaseRate { get; init; }

    public required float Ajar { get; init; }

    public required IReadOnlyList<(DoorStart Start, float Weight)> RandomStart { get; init; }

    public required float BotPassOpen { get; init; }
}

/// <summary>
/// A door leaf as a paint collider: an oriented box that follows the leaf, filed in the collision grid under the
/// bounds of its whole swing, so the broadphase never needs rebuilding as it moves.
/// </summary>
public sealed class DoorShape : Shape
{
    private Aabb _swing;

    public Vector3 Center { get; internal set; }

    public Vector3 AxisX { get; internal set; } = Vector3.UnitX;

    public Vector3 AxisY { get; internal set; } = Vector3.UnitY;

    public Vector3 AxisZ { get; internal set; } = Vector3.UnitZ;

    public Vector3 Half { get; internal set; }

    /// <summary>Everywhere the leaf can be.</summary>
    public override Aabb Bounds => _swing;

    internal void SetSwing(Aabb swing) => _swing = swing;

    public override bool Sweep(Vector3 p0, Vector3 d, float r, out float t, out Vector3 normal) =>
        BoxShape.SweepOriented(Center, AxisX, AxisY, AxisZ, Half, p0, d, r, out t, out normal);

    /// <summary>Distance from <paramref name="point"/> to the leaf's box (0 inside), and the nearest point on it.</summary>
    public float DistanceTo(Vector3 point, out Vector3 nearest)
    {
        Vector3 rel = point - Center;
        float x = Math.Clamp(Vector3.Dot(rel, AxisX), -Half.X, Half.X);
        float y = Math.Clamp(Vector3.Dot(rel, AxisY), -Half.Y, Half.Y);
        float z = Math.Clamp(Vector3.Dot(rel, AxisZ), -Half.Z, Half.Z);
        nearest = Center + AxisX * x + AxisY * y + AxisZ * z;
        return Vector3.Distance(point, nearest);
    }
}

/// <summary>
/// The level's doors (M3.2): each leaf's pose, where it's heading and how fast, worked by the players' interact
/// button. A tap swings a door all the way open or shut; a press held past the hold time eases it at the ease rate
/// and stops it where it's let go. A leaf stops rather than move into someone. Starting and shutting make a
/// <see cref="SimEventType.DoorMoved"/> event (bots hear it). Every leaf is a <see cref="DoorShape"/> in the paint
/// collision world, so shut doors stop paint, sight and aim. Steps without allocating.
/// </summary>
public sealed class DoorSet
{
    private const int SwingSamples = 12;

    private DoorSpec[] _specs = Array.Empty<DoorSpec>();
    private DoorShape[] _shapes = Array.Empty<DoorShape>();
    private int[] _colliders = Array.Empty<int>();
    private float[] _open = Array.Empty<float>();
    private float[] _target = Array.Empty<float>();
    private float[] _rate = Array.Empty<float>();
    private sbyte[] _dir = Array.Empty<sbyte>();
    private bool[] _moving = Array.Empty<bool>();
    private int[] _by = Array.Empty<int>();
    private DoorRules? _rules;
    private CollisionWorld? _world;

    public int Count => _specs.Length;

    public DoorSpec this[int leaf] => _specs[leaf];

    /// <summary>How far open the leaf is: 0 shut, 1 all the way.</summary>
    public float Open(int leaf) => _open[leaf];

    /// <summary>Where the leaf is heading (0 or 1, or where an eased leaf was let go).</summary>
    public float Target(int leaf) => _target[leaf];

    public DoorShape Shape(int leaf) => _shapes[leaf];

    /// <summary>The leaf's paint collider id.</summary>
    public int ColliderOf(int leaf) => _colliders[leaf];

    /// <summary>The leaf whose paint collider this is, or −1.</summary>
    public int LeafOfCollider(int colliderId)
    {
        for (int i = 0; i < _colliders.Length; i++)
        {
            if (_colliders[i] == colliderId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Puts a leaf (and its partner) at <paramref name="open"/>, at rest: for tests and scripted scenes.</summary>
    public void SetOpen(int leaf, float open)
    {
        foreach (int i in new[] { leaf, _specs[leaf].Partner })
        {
            if (i < 0)
            {
                continue;
            }

            _open[i] = _target[i] = Math.Clamp(open, 0f, 1f);
            _rate[i] = 0f;
            _moving[i] = false;
            Pose(i);
        }
    }

    /// <summary>Hangs the doors (none for an empty list), each in the match seed's start state, and files them as colliders.</summary>
    public void Load(IReadOnlyList<DoorSpec> doors, CollisionWorld world, ulong seed, DoorRules rules)
    {
        _rules = rules;
        _world = world;
        int n = doors.Count;
        _specs = doors.ToArray();
        _shapes = new DoorShape[n];
        _colliders = new int[n];
        _open = new float[n];
        _target = new float[n];
        _rate = new float[n];
        _dir = new sbyte[n];
        _moving = new bool[n];
        _by = new int[n];
        for (int i = 0; i < n; i++)
        {
            DoorSpec spec = _specs[i];
            int first = spec.Partner >= 0 ? Math.Min(i, spec.Partner) : i;
            float open = StartOpen(spec.Start, rules, SeedHash.Combine(seed, (ulong)(0xD0025 + first * 131)));
            _open[i] = _target[i] = open;
            _dir[i] = 1;
            _by[i] = -1;
            var shape = new DoorShape();
            shape.SetSwing(SwingBounds(spec));
            _shapes[i] = shape;
            Pose(i);
            _colliders[i] = world.Add(shape, spec.Kind.Material.Surface, $"door#{i}", dynamic: true).Id;
        }
    }

    /// <summary>The leaf's box: its centre, rotation and half extents (x across the leaf, y up, z through it).</summary>
    public void Pose(int leaf, out Vector3 center, out Quaternion rotation, out Vector3 half)
    {
        DoorShape s = _shapes[leaf];
        center = s.Center;
        half = s.Half;
        rotation = Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            s.AxisX.X, s.AxisX.Y, s.AxisX.Z, 0f,
            s.AxisY.X, s.AxisY.Y, s.AxisY.Z, 0f,
            s.AxisZ.X, s.AxisZ.Y, s.AxisZ.Z, 0f,
            0f, 0f, 0f, 1f));
    }

    /// <summary>
    /// The leaf a player at <paramref name="eye"/> looking along <paramref name="forward"/> would work: the one whose
    /// nearest point is within reach and closest to the middle of the view, inside the cone, with no wall between. −1 if none.
    /// </summary>
    public int FindTarget(Vector3 eye, Vector3 forward)
    {
        if (_rules is null)
        {
            return -1;
        }

        int best = -1;
        float bestCos = _rules.ConeCos;
        for (int i = 0; i < _specs.Length; i++)
        {
            float d = _shapes[i].DistanceTo(eye, out Vector3 nearest);
            if (d > _rules.Reach)
            {
                continue;
            }

            Vector3 to = nearest - eye;
            float length = to.Length();
            float cos = length < 1e-3f ? 1f : Vector3.Dot(to / length, forward);
            if (cos < bestCos)
            {
                continue;
            }

            // Not through a wall: the line to the leaf meets nothing standing still on the way.
            if (length > 0.05f && _world!.SweepSphere(eye, eye + to * ((length - 0.03f) / length), 0f, out _, includeDynamic: false))
            {
                continue;
            }

            best = i;
            bestCos = cos;
        }

        return best;
    }

    /// <summary>
    /// A leaf less than <paramref name="passOpen"/> open whose doorway the walk from <paramref name="from"/> to
    /// <paramref name="to"/> crosses (on the same floor), or −1: what a bot has to open on its way.
    /// </summary>
    public int ShutOnPath(Vector3 from, Vector3 to, float passOpen)
    {
        for (int i = 0; i < _specs.Length; i++)
        {
            if (_open[i] >= passOpen && _target[i] >= passOpen)
            {
                continue;
            }

            DoorSpec s = _specs[i];
            if (MathF.Abs(from.Y - s.Hinge.Y) > 1.2f && MathF.Abs(to.Y - s.Hinge.Y) > 1.2f)
            {
                continue;
            }

            Vector3 a = s.Hinge, b = s.Hinge + s.Across * s.Width;
            if (SegmentsCross(new Vector2(from.X, from.Z), new Vector2(to.X, to.Z), new Vector2(a.X, a.Z), new Vector2(b.X, b.Z)))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Whether a body of <paramref name="radius"/> walking from <paramref name="from"/> to <paramref name="to"/> (feet) would
    /// walk into a leaf: for headless movement, where no physics engine stops it.
    /// </summary>
    public bool Blocks(Vector3 from, Vector3 to, float radius)
    {
        Vector3 up = new(0f, 1f, 0f);
        for (int i = 0; i < _shapes.Length; i++)
        {
            if (_shapes[i].Sweep(from + up, to - from, radius, out float t, out _) && t <= 1f && t > 0f)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>One player's interact button this tick (pressed, held or let go).</summary>
    internal void Interact(SimWorld sim, PlayerState player, bool down, float dt)
    {
        if (_rules is null || _specs.Length == 0)
        {
            player.InteractDown = down;
            return;
        }

        if (down && !player.InteractDown)
        {
            int leaf = FindTarget(player.EyePosition, ViewAngles.Forward(player.Yaw, player.Pitch));
            player.InteractDoor = leaf;
            player.InteractHeld = 0f;
            if (leaf >= 0)
            {
                Begin(leaf, player);
            }
        }
        else if (down && player.InteractDoor >= 0)
        {
            player.InteractHeld += dt;
        }
        else if (!down && player.InteractDown && player.InteractDoor >= 0)
        {
            int leaf = player.InteractDoor;
            if (player.InteractHeld < _rules.HoldTime)
            {
                Finish(leaf);
            }
            else
            {
                Halt(leaf);
            }

            player.InteractDoor = -1;
        }

        player.InteractDown = down;
    }

    /// <summary>Moves every leaf that's going somewhere, unless it would move into someone.</summary>
    internal void Step(SimWorld sim, float dt)
    {
        for (int i = 0; i < _specs.Length; i++)
        {
            float open = _open[i];
            float target = _target[i];
            if (MathF.Abs(target - open) < 1e-5f || _rate[i] <= 0f)
            {
                _moving[i] = false;
                continue;
            }

            float step = _rate[i] * dt;
            float next = target > open ? MathF.Min(target, open + step) : MathF.Max(target, open - step);
            float before = Overlap(i, sim);
            _open[i] = next;
            Pose(i);
            float after = Overlap(i, sim);
            if (after > 1e-4f && after > before + 1e-4f)
            {
                // Someone's in the way: it stays put until they move (or it's sent the other way).
                _open[i] = open;
                Pose(i);
                continue;
            }

            if (!_moving[i])
            {
                _moving[i] = true;
                Noise(sim, i, target > open ? DoorMotion.Opening : DoorMotion.Closing);
            }

            if (next <= 0f && open > 0f)
            {
                _moving[i] = false;
                Noise(sim, i, DoorMotion.Shut);
            }
        }
    }

    private void Begin(int leaf, PlayerState player)
    {
        bool opening = _target[leaf] < 0.5f;
        StartMoving(leaf, opening, player);
        if (_specs[leaf].Partner is >= 0 and var partner)
        {
            StartMoving(partner, opening, player);
        }
    }

    private void StartMoving(int leaf, bool opening, PlayerState player)
    {
        DoorSpec s = _specs[leaf];
        if (opening && s.BothWays && _open[leaf] < 0.01f)
        {
            // A swing door opens away from whoever pushes it.
            _dir[leaf] = (sbyte)(Vector3.Dot(s.Side, player.Position - s.ShutCenter) <= 0f ? 1 : -1);
        }

        _target[leaf] = opening ? 1f : 0f;
        _rate[leaf] = _rules!.EaseRate;
        _by[leaf] = player.Id;
    }

    /// <summary>A tap: the leaf (and its partner) carries on all the way at full speed.</summary>
    private void Finish(int leaf)
    {
        SetFullRate(leaf);
        if (_specs[leaf].Partner is >= 0 and var partner)
        {
            SetFullRate(partner);
        }
    }

    private void SetFullRate(int leaf)
    {
        DoorKind k = _specs[leaf].Kind;
        _rate[leaf] = 1f / (_target[leaf] >= _open[leaf] ? k.OpenTime : k.CloseTime);
    }

    /// <summary>Let go after easing: the leaf (and its partner) stops where it is.</summary>
    private void Halt(int leaf)
    {
        _target[leaf] = _open[leaf];
        if (_specs[leaf].Partner is >= 0 and var partner)
        {
            _target[partner] = _open[partner];
        }
    }

    private void Noise(SimWorld sim, int leaf, DoorMotion motion)
    {
        DoorSpec s = _specs[leaf];
        sim.Events.Add(new SimEvent
        {
            Type = SimEventType.DoorMoved, Tick = sim.Tick, PlayerId = _by[leaf], TargetId = leaf, ColliderId = _colliders[leaf],
            Position = _shapes[leaf].Center, Surface = s.Kind.Material.Surface, Value = s.Kind.Noise, Extra = (int)motion,
        });
    }

    /// <summary>How deep the leaf cuts into anyone on the field (m; 0 when clear of everyone).</summary>
    private float Overlap(int leaf, SimWorld sim)
    {
        DoorShape shape = _shapes[leaf];
        MovementParams m = sim.Config.Movement;
        float r = m.CapsuleRadius;
        float worst = 0f;
        IReadOnlyList<PlayerState> players = sim.Players;
        for (int k = 0; k < players.Count; k++)
        {
            PlayerState p = players[k];
            if (!p.Present)
            {
                continue;
            }

            float height = m.CapsuleHeightFor(p.Stance);
            for (int j = 0; j < 3; j++)
            {
                float y = r + (height - 2f * r) * (j * 0.5f);
                float d = shape.DistanceTo(p.Position + new Vector3(0f, y, 0f), out _);
                worst = MathF.Max(worst, r - d);
            }
        }

        return worst;
    }

    /// <summary>Puts the leaf's box where its spec and openness say.</summary>
    private void Pose(int leaf)
    {
        Place(_specs[leaf], _open[leaf], _dir[leaf], _shapes[leaf]);
    }

    private static void Place(DoorSpec s, float open, int dir, DoorShape shape)
    {
        Vector3 up = Vector3.UnitY;
        float t = s.Kind.Thickness;
        Vector3 along;
        Vector3 center;
        if (s.Sliding)
        {
            along = s.Across;
            Vector3 face = s.Side * (s.WallThickness * 0.5f + t * 0.5f + 0.01f);
            center = s.Hinge + face + s.Across * (s.Width * (0.5f - open)) + up * (s.Height * 0.5f);
        }
        else
        {
            float angle = open * s.Kind.Swing;
            along = s.Across * MathF.Cos(angle) + s.Side * (dir * MathF.Sin(angle));
            center = s.Hinge + along * (s.Width * 0.5f) + up * (s.Height * 0.5f);
        }

        shape.Center = center;
        shape.AxisX = along;
        shape.AxisY = up;
        shape.AxisZ = Vector3.Cross(along, up);
        shape.Half = new Vector3(s.Width * 0.5f, s.Height * 0.5f, t * 0.5f);
    }

    /// <summary>The bounds of every pose the leaf can take.</summary>
    private static Aabb SwingBounds(DoorSpec s)
    {
        var probe = new DoorShape();
        Aabb all = default;
        bool any = false;
        foreach (int dir in s.BothWays ? new[] { 1, -1 } : new[] { 1 })
        {
            for (int k = 0; k <= SwingSamples; k++)
            {
                Place(s, k / (float)SwingSamples, dir, probe);
                Vector3 e = Vector3.Abs(probe.AxisX) * probe.Half.X + Vector3.Abs(probe.AxisY) * probe.Half.Y + Vector3.Abs(probe.AxisZ) * probe.Half.Z;
                Aabb box = Aabb.FromCenterExtents(probe.Center, e);
                all = any ? all.Union(box) : box;
                any = true;
            }
        }

        // Between samples a hinged leaf's far corner bulges past the chords a little.
        return Aabb.FromCenterExtents((all.Min + all.Max) * 0.5f, (all.Max - all.Min) * 0.5f + new Vector3(0.05f, 0f, 0.05f));
    }

    private static float StartOpen(DoorStart start, DoorRules rules, ulong seed)
    {
        if (start == DoorStart.Random)
        {
            var rng = new Pcg32(seed);
            float total = 0f;
            foreach ((DoorStart _, float weight) in rules.RandomStart)
            {
                total += weight;
            }

            float roll = rng.NextFloat() * total;
            start = DoorStart.Shut;
            foreach ((DoorStart s, float weight) in rules.RandomStart)
            {
                start = s;
                roll -= weight;
                if (roll < 0f)
                {
                    break;
                }
            }
        }

        return start switch
        {
            DoorStart.Open => 1f,
            DoorStart.Ajar => rules.Ajar,
            _ => 0f,
        };
    }

    private static bool SegmentsCross(Vector2 p, Vector2 p2, Vector2 q, Vector2 q2)
    {
        Vector2 r = p2 - p, s = q2 - q;
        float denom = r.X * s.Y - r.Y * s.X;
        if (MathF.Abs(denom) < 1e-8f)
        {
            return false;
        }

        Vector2 qp = q - p;
        float t = (qp.X * s.Y - qp.Y * s.X) / denom;
        float u = (qp.X * r.Y - qp.Y * r.X) / denom;
        return t is >= 0f and <= 1f && u is >= -0.05f and <= 1.05f;
    }
}

/// <summary>What a <see cref="SimEventType.DoorMoved"/> event says the door did.</summary>
public enum DoorMotion : byte
{
    Opening,
    Closing,

    /// <summary>A closing door reached its frame.</summary>
    Shut,
}
