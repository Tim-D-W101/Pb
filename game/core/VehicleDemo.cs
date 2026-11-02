using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Player;
using Pb.Game.Ui;
using Pb.Game.World;
using Pb.Sim;
using Pb.Sim.Core;
using Pb.Sim.Players;
using Pb.Sim.Vehicles;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --vehicle-demo</c> (on a level with vehicles: <c>--level=thornwick_woods</c>; <c>--vehicle=N</c> picks one, else
/// the nearest): you get into a buggy at its driver's door, drive it along the tracks to the farthest point on them within
/// reach, stop and get out, and look back at it; through your own eyes, for screenshots, with the opponents quiet. It
/// checks the game's side of it: your body in the seat and out beside it, the buggy drawn where the sim has it, the HUD's
/// prompt, the engine; prints SMOKE PASS or FAIL and quits.
/// </summary>
public sealed class VehicleDemo : ICommandSource
{
    private const int InAt = 20;
    private const int LookBack = 150;

    private readonly Node _host;
    private readonly SimWorld _sim;
    private readonly PlayerController _player;
    private readonly VehicleViews _views;
    private readonly Hud _hud;
    private readonly List<SVector3> _route = new();
    private int _start = -1, _vehicle = -1, _outAt = -1, _routeAt, _inFor;
    private bool _promptSeen, _wasIn, _failed, _slung, _raised;
    private float _fastest, _driven, _leaned, _sprayed;
    private int _stuck, _backFor;
    private SVector3 _last;
    private string _problems = "";

    private readonly Action<PlayerState, SVector3> _moveAside;

    public VehicleDemo(Node host, SimWorld sim, PlayerController player, VehicleViews views, Hud hud, Action<PlayerState, SVector3> moveAside)
    {
        _host = host;
        _sim = sim;
        _player = player;
        _views = views;
        _hud = hud;
        _moveAside = moveAside;
    }

    /// <summary>Stands you at the driver's door of the vehicle to drive, facing it.</summary>
    public void Setup(int? which)
    {
        VehicleSet vehicles = _sim.Vehicles;
        if (vehicles.Count == 0)
        {
            return;
        }

        int best = which is { } w && w >= 0 && w < vehicles.Count ? w : 0;
        for (int i = 0; which is null && i < vehicles.Count; i++)
        {
            if (Flat(vehicles[i].Position - _player.State.Position) < Flat(vehicles[best].Position - _player.State.Position))
            {
                best = i;
            }
        }

        _vehicle = best;
        VehicleState v = vehicles[best];
        if (Args.Has("--chase"))
        {
            // Filmed from behind and beside it, your own body drawn in its seat.
            var chase = new ChaseCamera { Name = "Chase", Fov = 62f, Target = () => _views.Drawn(_vehicle) };
            if (Args.Value("--chase") == "near")
            {
                chase.Offset = new System.Numerics.Vector3(-2.2f, 1.6f, 1.2f).ToGodot(); // close by the driver's side
            }
            else if (Args.Value("--chase") == "side")
            {
                chase.Offset = new System.Numerics.Vector3(-3.2f, 1.3f, 0f).ToGodot(); // square to the driver's side
            }

            _host.AddChild(chase);
            chase.MakeCurrent();
            if (_player.Body is { } body)
            {
                body.ShadowOnly = false;
            }
        }

        SVector3 door = VehicleSet.Door(v, v.Params.DriverSeat);
        _player.Teleport(door, MathF.Atan2(-(v.Position.X - door.X), -(v.Position.Z - door.Z)));

        // The farthest point on the tracks within reach, and the way there along them.
        var roads = RoadGraph.Build(_sim.Level?.Roads ?? Array.Empty<Pb.Sim.Level.RoadSpec>());
        SVector3 far = v.Position;
        foreach (Pb.Sim.Level.RoadSpec road in _sim.Level?.Roads ?? Array.Empty<Pb.Sim.Level.RoadSpec>())
        {
            foreach (SVector3 p in road.Points)
            {
                if (Flat(p - v.Position) is var d and < 140f && d > Flat(far - v.Position) && Math.Abs(p.X) < 170f && Math.Abs(p.Z) < 120f)
                {
                    far = p;
                }
            }
        }

        roads.Route(v.Position, far, _route, out float length);
        _route.Insert(0, v.Position);
        GD.Print($"VEHICLE DEMO: vehicle {best} ({v.Params.DisplayName}) at {v.Position}, to {far} along {length:0} m of track");

        // The opponents stand quiet where they start: anyone on the way is moved off it (it'd stop for them otherwise).
        foreach (PlayerState other in _sim.Players)
        {
            if (other == _player.State)
            {
                continue;
            }

            for (int k = 0; k + 1 < _route.Count; k++)
            {
                SVector3 a = _route[k], b = _route[k + 1], ab = b - a;
                float along = Math.Clamp(System.Numerics.Vector3.Dot(other.Position - a, ab) / MathF.Max(ab.LengthSquared(), 1e-4f), 0f, 1f);
                SVector3 near = a + ab * along;
                if (Flat(other.Position - near) < 3.5f)
                {
                    var side = SVector3.Normalize(new SVector3(-ab.Z, 0f, ab.X));
                    _moveAside(other, near + side * 7f);
                    break;
                }
            }
        }
    }

    public InputCommand Next(int tick, PlayerState state)
    {
        if (_vehicle < 0)
        {
            GD.PushError("--vehicle-demo needs a level with vehicles (--level=thornwick_woods)");
            _host.GetTree().QuitCleanly(1);
            return new InputCommand { Tick = tick };
        }

        _start = _start < 0 ? tick : _start;
        int t = tick - _start;
        VehicleState v = _sim.Vehicles[_vehicle];
        bool inside = state.Vehicle == _vehicle;
        if (inside)
        {
            _wasIn = true;
            _fastest = MathF.Max(_fastest, v.Speed);
            _driven += Flat(v.Position - _last);
            _promptSeen |= _hud.PromptText.Contains("km/h", StringComparison.Ordinal);
            // The body leans and pitches as it drives; the tyres throw up the ground; driving, the marker's on its sling,
            // and comes up when the trigger's pulled.
            _leaned = MathF.Max(_leaned, MathF.Acos(Math.Clamp(v.Up.Y, -1f, 1f)) * 180f / MathF.PI);
            _sprayed = MathF.Max(_sprayed, _views.Spraying(_vehicle));
            _slung |= state.Sling >= 1f;
            _raised |= _slung && state.Sling <= 0f;
            // The body is put in the seat as its move comes round each tick (a tick behind the seat at most; the tick it got
            // in, it's still at the door).
            Check(++_inFor < 3 || Flat(_player.GlobalPosition.ToSim() - state.Position) < MathF.Abs(v.Speed) * _sim.Dt + 0.05f,
                "your body isn't in your seat");
            if (tick % 240 == 0)
            {
                GD.Print($"VEHICLE DEMO t={t}: at {v.Position}, {v.Speed:0.0} m/s, blocked {v.Blocked}, route {_routeAt}/{_route.Count}");
            }

        }

        _last = v.Position;
        var cmd = new InputCommand { Tick = tick, Yaw = state.Yaw, Pitch = -0.05f };
        if (!inside && !_wasIn)
        {
            // At the door: in, on a press.
            cmd.Yaw = YawTo(state.Position, v.Position);
            cmd.Buttons = t == InAt ? InputButtons.Interact : InputButtons.None;
            if (t > InAt + 60)
            {
                Finish("it never got you in");
            }

            return cmd;
        }

        if (inside && _outAt < 0)
        {
            // Along the route: steer for a point ahead on it, slow for the end, out when stopped there.
            SVector3 target = RoadGraph.Ahead(_route, ref _routeAt, v.Position, 4f + MathF.Abs(v.Speed) * 0.6f, out float left);
            SVector3 to = target - v.Position;
            SVector3 f = v.Forward;
            float angle = MathF.Atan2(f.X * to.Z - f.Z * to.X, f.X * to.X + f.Z * to.Z);
            float wheel = Math.Clamp(-angle / v.Params.MaxSteer * 1.4f, -1f, 1f);
            // As fast as the bend ahead allows (a bend taken too fast runs wide: the tyres only grip so much), and slowing
            // to stop at the end.
            float bend = MathF.Min(1f, MathF.Abs(angle) / (MathF.PI * 0.5f));
            float want = MathF.Min(3.5f + 6.5f * (1f - bend), MathF.Sqrt(2f * v.StopDecel * 0.5f * MathF.Max(0f, left - 4f)));
            float pedal = left < 5f ? (v.Speed > 0.5f ? -1f : 0f) : Math.Clamp((want - v.Speed) * 0.8f, -1f, 1f);
            // Stuck against something: back off a little, the wheel the other way, then on.
            _stuck = pedal > 0.3f && MathF.Abs(v.Speed) < 0.3f ? _stuck + 1 : 0;
            if (_stuck > 180)
            {
                _backFor = 150;
                _stuck = 0;
            }

            if (_backFor > 0)
            {
                _backFor--;
                wheel = -wheel;
                pedal = -1f;
            }

            cmd.Move = new System.Numerics.Vector2(-wheel, pedal);
            cmd.Yaw = v.Yaw + angle * 0.3f;
            // A burst from the wheel along the way: the marker comes up off its sling one-handed, and goes back after.
            int driving = tick - _start - InAt;
            if (driving is > 8 * 120 and < 9 * 120 && (tick / 12) % 2 == 0)
            {
                cmd.Buttons |= InputButtons.Fire;
            }
            if (left < 5f && MathF.Abs(v.Speed) < 0.5f && tick % 2 == 0)
            {
                cmd.Buttons = InputButtons.Interact;
            }

            if (t > 60 * 120)
            {
                Finish("it never got there");
            }

            return cmd;
        }

        // Out: look back at it, then the checks.
        _outAt = _outAt < 0 ? t : _outAt;
        cmd.Yaw = YawTo(state.Position, v.Position);
        cmd.Pitch = -0.15f;
        if (t - _outAt == 2)
        {
            Check(state.Vehicle < 0 && Flat(state.Position - v.Position) < v.Params.Width * 0.5f + 1.5f, "you didn't get out beside it");
        }

        if (t - _outAt >= LookBack)
        {
            Check(Flat(_player.GlobalPosition.ToSim() - state.Position) < 0.1f, "your body isn't where you got out");
            Check(Flat(_views.Drawn(_vehicle).Origin.ToSim() - v.Position) < 0.05f, "the buggy isn't drawn where the sim has it");
            Check(_fastest > 6f, $"it never got going ({_fastest:0.0} m/s)");
            Check(_driven > 40f, $"it drove only {_driven:0} m");
            Check(_promptSeen, "the HUD never showed the buggy's speed");
            Check(_leaned > 0.5f, $"its body never leaned or pitched ({_leaned:0.0}°)");
            Check(_sprayed > 0.05f, "its tyres never threw anything up");
            Check(_slung && _raised, "the marker never went onto its sling and came up again");
            Finish(null);
        }

        return cmd;
    }

    private void Check(bool ok, string problem)
    {
        if (!ok && !_problems.Contains(problem, StringComparison.Ordinal))
        {
            _failed = true;
            _problems += (_problems.Length > 0 ? "; " : "") + problem;
        }
    }

    private void Finish(string? problem)
    {
        if (problem is not null)
        {
            Check(false, problem);
        }

        GD.Print($"SMOKE {(_failed ? "FAIL" : "PASS")}: vehicle demo: in at the door, drove {_driven:0} m (fastest {_fastest:0.0} m/s, leaning up to " +
                 $"{_leaned:0.0}°, the tyres' spray up to {_sprayed:P0}) along the tracks, the marker up off its sling and back, out beside it" +
                 $"{(_failed ? $": {_problems}" : "")}");
        _host.GetTree().QuitCleanly(_failed ? 1 : 0);
    }

    private static float YawTo(SVector3 from, SVector3 to) => MathF.Atan2(-(to.X - from.X), -(to.Z - from.Z));

    private static float Flat(SVector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);
}
