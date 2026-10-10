using System.Collections.Generic;
using Godot;
using Pb.Game.Ballistics;
using Pb.Game.Player;
using Pb.Game.Ui;
using Pb.Sim;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Events;
using Pb.Sim.Gear;
using Pb.Sim.Players;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --paint-demo</c>: opponents stand in a row on clear ground, each in one brand's kit, and balls break on them
/// (their bodies, masks, markers, loaders and tanks, from in front, the sides and behind), on the ground round them,
/// square and at a slant, and on the nearest wall. A camera looks at them clean, then wet, close up, while they turn
/// round (the paint goes round with them) and once it's dry; then through your own eyes, in front of that wall, as balls
/// break on it close enough to spatter your marker and gloves (<c>--paint-first</c>: only that). Each view is held for
/// <c>--paint-hold=N</c> frames (8) and printed as it comes, so a <c>--write-movie</c> run gives a still of each; it quits
/// after the last.
/// </summary>
public partial class PaintDemo : Node, ICommandSource
{
    /// <summary>Where the balls come from, as (degrees round from straight ahead of them, height above the hit, m).</summary>
    /// <summary>How far round the row a wall is looked for (m).</summary>
    private const float Reach = 16f;

    private static readonly (HitboxPart Part, float Around_deg, float Rise_m)[] Shots =
    {
        (HitboxPart.Torso, 0f, 0.2f), (HitboxPart.Torso, 25f, 0.4f), (HitboxPart.Torso, -30f, 0f), (HitboxPart.Mask, -20f, 0.3f),
        (HitboxPart.Head, 80f, 0.2f), (HitboxPart.Arms, 15f, 0.1f), (HitboxPart.Arms, -15f, 0.3f), (HitboxPart.Legs, 10f, 0.6f),
        (HitboxPart.Legs, -20f, 0.8f), (HitboxPart.Marker, 70f, 0.3f), (HitboxPart.Loader, 30f, 1.2f), (HitboxPart.Tank, 120f, 0.3f),
        (HitboxPart.Torso, 180f, 0.3f), (HitboxPart.Legs, 160f, 0.5f),
    };

    /// <summary>The views: what happens as each begins, and where the camera stands (null: your own eyes).</summary>
    private readonly List<(string Name, System.Action? Do, System.Func<(Vector3 From, Vector3 At)>? Camera)> _steps = new();
    private readonly List<OpponentPawn> _row = new();
    private readonly RandomNumberGenerator _rng = new() { Seed = 4242 };
    private SimWorld _sim = null!;
    private PlayerController _player = null!;
    private SplatSystem _splats = null!;
    private SplatDef _def = null!;
    private Camera3D _camera = null!;
    private SVector3 _centre;
    private SVector3 _ahead;
    private SVector3 _across;
    private SVector3? _wall;
    private SVector3 _wallNormal;
    private float _yaw;
    private float _turn;
    private float _turnTo;
    private uint _sequence;
    private int _hold = 8;
    private int _frame;
    private int _shown = -1;

    public void Start(SimWorld sim, GearCatalog gear, SplatSystem splats, SplatDef def, PlayerController player, IReadOnlyList<OpponentPawn> opponents, Hud hud,
        float farClip, SVector3 towardSun)
    {
        _sim = sim;
        _player = player;
        _splats = splats;
        _def = def;
        _hold = Args.Ticks("--paint-hold", 8) ?? 8;
        hud.Visible = false;
        int count = gear.Brands.Count;
        const float spacing = 1.5f;
        if (opponents.Count < count || !GearDemo.FindRoom(sim, opponents[0].State.Position, towardSun, count * spacing * 0.5f + 1.5f, out _centre, out _yaw))
        {
            GD.PushError($"PAINT DEMO needs {count} opponents and a clear patch of ground; quitting");
            GetTree().Quit(1);
            return;
        }

        _ahead = ViewAngles.FlatForward(_yaw);
        _across = new SVector3(-_ahead.Z, 0f, _ahead.X);
        for (int i = 0; i < opponents.Count; i++)
        {
            OpponentPawn pawn = opponents[i];
            if (i >= count)
            {
                pawn.Teleport(_centre - _ahead * 40f + _across * (i * 3f), _yaw);
                pawn.Visible = false;
                continue;
            }

            pawn.Teleport(_centre + _across * ((i - (count - 1) * 0.5f) * spacing), _yaw);
            pawn.Steer(this);
            pawn.Redress(new Kit(gear, Kit.Brand(gear, gear.Brands[i].Id, i) ?? gear.Default(i)));
            _row.Add(pawn);
        }

        _wall = FindWall(out _wallNormal);
        Vector3 up = Vector3.Up;
        Vector3 middle = (_centre + new SVector3(0f, 1.1f, 0f)).ToGodot();
        Vector3 ahead = _ahead.ToGodot();
        Vector3 across = _across.ToGodot();
        float wide = count * spacing * 0.55f;
        (Vector3, Vector3) Front() => (middle + ahead * (wide + 2.6f) + up * 0.3f, middle);
        if (!Args.Has("--paint-first"))
        {
            _steps.Add(("clean, from in front", null, Front));
            _steps.Add(("paint lands", Fire, Front));
            AddViews("wet", wide);
            _steps.Add(("they turn round", () => _turnTo = Mathf.Pi / 2f, Front));
            _steps.Add(("turned, from in front", null, Front));
            _steps.Add(("turned, from where they face", null, () => (middle - across * 3.4f + ahead * 3.6f + up * 0.3f, middle)));
            _steps.Add(("turned, one close up", null, () => Close(1, (ViewAngles.FlatForward(_yaw + _turn).ToGodot() + ahead).Normalized(), 0.25f)));
            _steps.Add(("they turn back", () => _turnTo = 0f, Front));
            _steps.Add(("it dries", () => PaintSlots.Advance(_def.Drying_s + 1.0), Front));
            AddViews("dry", wide);
        }

        if (_wall is not null)
        {
            _steps.Add(("first person: in front of the wall", StandAtWall, null));
            _steps.Add(("first person: balls break on the wall in front of you", BreakNearYou, null));
            _steps.Add(("first person: spattered", null, null));
            _steps.Add(("first person: dry", () => PaintSlots.Advance(_def.Drying_s + 1.0), null));
        }

        _camera = new Camera3D { Name = "PaintCamera", Fov = 40f, Far = farClip, Near = 0.03f };
        AddChild(_camera);
        _camera.MakeCurrent();
        player.ViewModel.Visible = false;
        GD.Print($"PAINT DEMO {_row.Count} kits at {_centre}, wall {(_wall is { } w ? w.ToString() : "none")}, {_steps.Count} views × {_hold} frames");
    }

    /// <summary>The views of the paint as it is (<paramref name="when"/>: wet or dry).</summary>
    private void AddViews(string when, float wide)
    {
        Vector3 up = Vector3.Up;
        Vector3 middle = (_centre + new SVector3(0f, 1.1f, 0f)).ToGodot();
        Vector3 ahead = _ahead.ToGodot();
        Vector3 across = _across.ToGodot();
        _steps.Add(($"{when}: the row from the right", null, () => (middle + ahead * 3.4f + across * 3.6f + up * 0.3f, middle)));
        _steps.Add(($"{when}: the row from behind", null, () => (middle - ahead * 3.6f - across * 2.4f + up * 0.6f, middle)));
        for (int i = 0; i < _row.Count; i++)
        {
            int k = i;
            _steps.Add(($"{when}: {k + 1} close up", null, () => Close(k, ahead, 0.25f)));
            _steps.Add(($"{when}: {k + 1}'s marker side", null, () => Close(k, (ahead + across).Normalized(), -0.15f)));
        }

        Vector3 ground = (_centre + _ahead * 1.6f).ToGodot();
        _steps.Add(($"{when}: the ground", null, () => (ground + ahead * 2.2f + up * 1.6f, ground)));
        // They face the sun, so from among them the sun is ahead: wet paint shines in it.
        _steps.Add(($"{when}: the ground against the light", null, () => (ground - ahead * 1.6f + up * 0.7f, ground + ahead * 0.6f)));
        if (_wall is { } wall)
        {
            Vector3 at = wall.ToGodot();
            Vector3 normal = _wallNormal.ToGodot();
            _steps.Add(($"{when}: the wall", null, () => (at + normal * 2.4f + up * 0.2f, at)));
        }
    }

    /// <summary>A close view of one in the row from <paramref name="from"/>'s side, its chest at <paramref name="lift"/> above the middle.</summary>
    private (Vector3, Vector3) Close(int index, Vector3 from, float lift)
    {
        Vector3 chest = (_row[index].State.Position + new SVector3(0f, 1.2f + lift, 0f)).ToGodot();
        return (chest + from * 1.9f + Vector3.Up * 0.15f, chest);
    }

    public override void _Process(double delta)
    {
        if (_steps.Count == 0)
        {
            return;
        }

        _turn = Mathf.MoveToward(_turn, _turnTo, (float)delta * 2.5f);
        int index = _frame / _hold;
        if (index >= _steps.Count)
        {
            GD.Print($"PAINT DEMO done: {_splats.PaintedOnPlayers} splats painted on players, {_splats.ActiveCount} decals");
            GetTree().Quit();
            return;
        }

        if (index != _shown)
        {
            _shown = index;
            (string name, System.Action? act, _) = _steps[index];
            act?.Invoke();
            GD.Print($"PAINT DEMO view {index}: {name} (frames {_frame}–{_frame + _hold - 1})");
        }

        if (_steps[index].Camera is { } camera)
        {
            (Vector3 from, Vector3 at) = camera();
            _camera.GlobalPosition = from;
            _camera.LookAt(at, Vector3.Up);
        }

        _frame++;
    }

    /// <summary>Standing still, the marker held level; turned by <see cref="_turn"/> once they turn round.</summary>
    public InputCommand Next(int tick, PlayerState me) => new() { Tick = tick, Yaw = _yaw + _turn, Pitch = 0f };

    /// <summary>Breaks balls on everyone in the row, on the ground round them and on the wall, as the sim would.</summary>
    private void Fire()
    {
        System.Span<PosedBox> parts = stackalloc PosedBox[HitboxRig.PartCount];
        int onPlayers = 0;
        for (int i = 0; i < _row.Count; i++)
        {
            PlayerState state = _row[i].State;
            _sim.PlayerHits.PoseNow(state, parts);
            foreach ((HitboxPart part, float around, float rise) in Shots)
            {
                if (!Box(parts, part, out PosedBox box))
                {
                    continue;
                }

                SVector3 target = box.Center + box.AxisX * (box.HalfExtents.X * _rng.RandfRange(-0.4f, 0.4f)) +
                                  box.AxisY * (box.HalfExtents.Y * _rng.RandfRange(-0.4f, 0.4f));
                SVector3 from = target + Around(around) * 6f + new SVector3(0f, rise, 0f);
                SVector3 travel = SVector3.Normalize(target - from);
                if (_sim.PlayerHits.SweepSphere(from, target + travel * 1.5f, _sim.Ballistics.Projectile.Radius, _sim.Tick, -1, out HitboxHit hit))
                {
                    Break(hit.Point, hit.Normal, travel, hit.ReceiverId, (int)hit.Part, -1, i + 1);
                    onPlayers++;
                }
            }
        }

        // The ground in front of them: square from above, and at a slant from far off (sprayed the way it went).
        for (int k = 0; k < 10; k++)
        {
            SVector3 spot = _centre + _ahead * _rng.RandfRange(0.8f, 2.8f) + _across * _rng.RandfRange(-2.6f, 2.6f);
            bool slant = k % 2 == 1;
            SVector3 from = slant ? spot + Around(_rng.RandfRange(-60f, 60f)) * 14f + new SVector3(0f, 1.2f, 0f)
                : spot + Around(_rng.RandfRange(-40f, 40f)) * 1.5f + new SVector3(0f, 3f, 0f);
            Ground(from, spot + (spot - from) * 0.2f, k);
        }

        if (_wall is { } wall)
        {
            SVector3 side = SVector3.Normalize(SVector3.Cross(_wallNormal, new SVector3(0f, 1f, 0f)));
            for (int k = 0; k < 6; k++)
            {
                SVector3 spot = wall + side * _rng.RandfRange(-1.2f, 1.2f) + new SVector3(0f, _rng.RandfRange(-0.5f, 0.6f), 0f);
                SVector3 from = k < 3 ? spot + _wallNormal * 5f : spot + _wallNormal * 2f + side * (k % 2 == 0 ? 7f : -7f);
                Ground(from, spot - _wallNormal * 0.3f, k);
            }
        }

        GD.Print($"PAINT DEMO fired: {onPlayers} on players, {_splats.PaintedOnPlayers} painted in their shaders, {_splats.ActiveCount} decals");
    }

    /// <summary>You stand facing the wall, an arm's length off it, through your own eyes.</summary>
    private void StandAtWall()
    {
        SVector3 wall = _wall!.Value;
        (float yaw, _) = ViewAngles.FromDirection(-_wallNormal);
        _player.Teleport(new SVector3(wall.X, _centre.Y, wall.Z) + _wallNormal * 0.62f, yaw);
        _player.ViewModel.Visible = true;
        _player.Camera.MakeCurrent();
    }

    /// <summary>Balls break on the wall in front of you from the side, close to your eye (once the camera is there).</summary>
    private void BreakNearYou()
    {
        SVector3 wall = _wall!.Value;
        SVector3 eye = _player.Camera.GlobalPosition.ToSim();
        SVector3 side = SVector3.Normalize(SVector3.Cross(_wallNormal, new SVector3(0f, 1f, 0f)));
        for (int k = 0; k < 3; k++)
        {
            SVector3 spot = new SVector3(wall.X, eye.Y, wall.Z) + side * ((k - 1) * 0.22f) + new SVector3(0f, (k % 2) * 0.15f - 0.12f, 0f);
            SVector3 from = spot + _wallNormal * 0.4f + side * (k == 1 ? -2.5f : 2.5f);
            Ground(from, spot + (spot - from) * 0.3f, k);
        }

        GD.Print($"PAINT DEMO first person: {_splats.Spattered} breaks spattered your marker, from {_player.Camera.GlobalPosition}");
    }

    /// <summary>A ball from <paramref name="from"/> towards <paramref name="to"/> breaking on the level, if it gets there.</summary>
    private void Ground(SVector3 from, SVector3 to, int k)
    {
        float radius = _sim.Ballistics.Projectile.Radius;
        if (_sim.Collision.SweepSphere(from, to, radius, out SweepHit hit))
        {
            Break(hit.Point - hit.Normal * radius, hit.Normal, SVector3.Normalize(to - from), -1, 0, hit.ColliderId, k % 3);
        }
    }

    private void Break(SVector3 point, SVector3 normal, SVector3 travel, int receiver, int part, int collider, int team)
    {
        var e = new SimEvent
        {
            Type = SimEventType.BallBroke, Tick = _sim.Tick, PlayerId = -7, ShotSequence = ++_sequence, Team = (byte)team,
            Position = point, Normal = normal, Velocity = travel * 80f, Value = 80f, TargetId = receiver, ColliderId = collider, Extra = part,
        };
        _splats.OnSimEvent(e);
    }

    /// <summary>Straight out from the row, turned <paramref name="degrees"/> round (positive to the left as they face).</summary>
    private SVector3 Around(float degrees)
    {
        float a = Mathf.DegToRad(degrees);
        return _ahead * Mathf.Cos(a) - _across * Mathf.Sin(a);
    }

    private static bool Box(System.Span<PosedBox> parts, HitboxPart part, out PosedBox box)
    {
        foreach (PosedBox p in parts)
        {
            if (p.Part == part)
            {
                box = p;
                return true;
            }
        }

        box = default;
        return false;
    }

    /// <summary>
    /// The nearest wall within 16 m of the row (its point 1.2 m up, and the way it faces), or null: upright, standing
    /// from knee height to over your head and running on to either side, so not a crate or a car.
    /// </summary>
    private SVector3? FindWall(out SVector3 normal)
    {
        normal = default;
        float best = float.MaxValue;
        SVector3? found = null;
        SVector3 eye = _centre + new SVector3(0f, 1.2f, 0f);
        for (int k = 0; k < 36; k++)
        {
            float a = k * Mathf.Tau / 36f;
            SVector3 way = new(Mathf.Sin(a), 0f, Mathf.Cos(a));
            if (!Upright(eye, way, out SweepHit hit) || hit.T * Reach >= best)
            {
                continue;
            }

            // The same flat face below, above and to either side of where it was found.
            SVector3 along = SVector3.Normalize(SVector3.Cross(hit.Normal, new SVector3(0f, 1f, 0f)));
            int faces = 0;
            foreach (SVector3 offset in new[] { new SVector3(0f, -0.5f, 0f), new SVector3(0f, 0.6f, 0f), along * 0.8f, along * -0.8f })
            {
                SVector3 from = hit.Point + hit.Normal * 1.5f + offset;
                faces += Upright(from, -hit.Normal, out SweepHit other) && SVector3.Dot(other.Normal, hit.Normal) > 0.95f &&
                         System.MathF.Abs(SVector3.Dot(other.Point - hit.Point, hit.Normal)) < 0.08f ? 1 : 0;
            }

            if (faces < 4)
            {
                continue;
            }

            best = hit.T * Reach;
            found = hit.Point;
            normal = hit.Normal;
        }

        return found;
    }

    private bool Upright(SVector3 from, SVector3 way, out SweepHit hit) =>
        _sim.Collision.SweepSphere(from, from + way * Reach, 0f, out hit) && System.MathF.Abs(hit.Normal.Y) < 0.2f;
}
