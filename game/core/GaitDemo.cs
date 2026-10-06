using System.Collections.Generic;
using System.Linq;
using Godot;
using Pb.Game.Player;
using Pb.Game.Ui;
using Pb.Sim;
using Pb.Sim.Core;
using Pb.Sim.Players;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --gait-demo</c>: one opponent stands, walks, runs, sprints, pulls up, strafes, backs off, walks
/// crouched and stands looking round along a clear lane while a camera tracks them from the side (from
/// in front for the look round), for checking the movement clips (or the steps without them) by eye or
/// capturing them with <c>--write-movie</c>. <c>--gait-only=NAME</c> plays only the moves whose names start
/// with NAME (after a second's stand), to film one quickly. The moves run by sim ticks, so they play out
/// the same at any rendering speed; each is printed as it starts, and the game quits after the last.
/// </summary>
public partial class GaitDemo : Node, ICommandSource
{
    /// <summary>The lane: this far back from where the opponent starts, and this far ahead (m), clear for this far either side.</summary>
    private const float LaneBack = 2f, LaneAhead = 9.5f, LaneSide = 2f;

    private static readonly Move[] Moves =
    {
        new("stand", 1.0f, 0f, 0f, InputButtons.None),
        new("walk", 2.4f, 0f, 1f, InputButtons.Walk),
        new("run", 1.5f, 0f, 1f, InputButtons.None, Turn: true),
        new("sprint", 1.2f, 0f, 1f, InputButtons.Sprint, Turn: true),
        new("stop", 1.0f, 0f, 0f, InputButtons.None),
        new("strafe right", 1.2f, 1f, 0f, InputButtons.Walk),
        new("strafe left", 1.2f, -1f, 0f, InputButtons.Walk),
        new("back off", 2.0f, 0f, -1f, InputButtons.Walk),
        new("crouched walk", 2.4f, 0f, 1f, InputButtons.Crouch),
        new("stand", 1.5f, 0f, 0f, InputButtons.None),
        new("look round", 6f, 0f, 0f, InputButtons.None, Look: true),
    };

    /// <summary>The look round: the head's turn (deg, left positive) from this many seconds into it.</summary>
    private static readonly (float From, float Head)[] LookRound = { (0f, 0f), (0.8f, 55f), (2.2f, 0f), (3.2f, -60f), (4.6f, 0f) };

    private Move[] _moves = Moves;
    private float _head;

    private SimWorld _sim = null!;
    private OpponentPawn? _walker;
    private Camera3D _camera = null!;
    private SVector3 _start;
    private SVector3 _side;
    private float _yaw;
    private int _first = -1;
    private int _current = -1;

    /// <param name="towardSun">Which way the sun is (world, level): the camera watches from that side, so the walker is lit.</param>
    public void Start(SimWorld sim, IReadOnlyList<OpponentPawn> opponents, Hud hud, float farClip, SVector3 towardSun)
    {
        _sim = sim;
        // The nearest clear lane to the first opponent, searched outwards on a 2 m grid.
        _walker = opponents.FirstOrDefault(o => o.State.Alive);
        bool found = false;
        SVector3 origin = _walker?.State.Position ?? SVector3.Zero;
        for (int ring = 0; ring <= 40 && _walker is not null && !found; ring++)
        {
            for (int x = -ring; x <= ring && !found; x++)
            {
                for (int z = -ring; z <= ring && !found; z++)
                {
                    if (System.Math.Max(System.Math.Abs(x), System.Math.Abs(z)) != ring ||
                        !Ground(origin + new SVector3(x * 2f, 0f, z * 2f), out SVector3 at))
                    {
                        continue;
                    }

                    for (int i = 0; i < 8 && !found; i++)
                    {
                        float yaw = i * Mathf.Pi / 4f;
                        if (Clear(at, yaw))
                        {
                            (_start, _yaw, found) = (at, yaw, true);
                        }
                    }
                }
            }
        }

        hud.Visible = false;
        if (_walker is null || !found)
        {
            GD.PushError("GAIT DEMO found no clear lane for anyone to walk; quitting");
            GetTree().Quit(1);
            return;
        }

        if (Args.Value("--gait-only") is { Length: > 0 } only)
        {
            _moves = Moves.Take(1).Concat(Moves.Skip(1).Where(m => m.Name.StartsWith(only, System.StringComparison.Ordinal))).ToArray();
        }

        _walker.Teleport(_start, _yaw);
        _walker.Steer(this);
        SVector3 ahead = ViewAngles.FlatForward(_yaw);
        _side = new SVector3(-ahead.Z, 0f, ahead.X);
        if (SVector3.Dot(_side, towardSun) < 0f)
        {
            _side = -_side;
        }

        _camera = new Camera3D { Name = "GaitCamera", Fov = 50f, Far = farClip, Near = 0.05f };
        AddChild(_camera);
        _camera.MakeCurrent();
        Track();
        GD.Print($"GAIT DEMO {_walker.State.Name} walks a lane from {_start}, facing {Mathf.RadToDeg(_yaw):0}°");
    }

    public override void _Process(double delta)
    {
        if (_walker is not null)
        {
            Track();
            Measure((float)delta);
        }
    }

    // How the feet behave in each move, printed as the next starts: steps a second, how far a foot slides
    // while it's down (it shouldn't), and how far the hips rise and fall.
    private readonly Vector3[] _toe = new Vector3[2];
    private readonly bool[] _down = new bool[2];
    private int _steps;
    private float _slide;
    private float _downTime;
    private float _time;
    private float _hipsLow = float.MaxValue, _hipsHigh = float.MinValue;
    private int _measured = -1;
    private float _settledAt;

    /// <summary>Each move is measured from this long after it starts.</summary>
    private const float Settle = 0.6f;

    private void Measure(float delta)
    {
        if (_walker!.Visual.Model is not { } model || _current < 0)
        {
            return;
        }

        if (_measured != _current)
        {
            if (_measured >= 0 && _time > 0f)
            {
                CharacterPoser poser = model.Poser;
                GD.Print($"GAIT DEMO   {_moves[_measured].Name}: {_steps / Mathf.Max(_time - _settledAt, 0.01f):0.0} steps/s, feet slide {(poser.CheckDown > 0f ? poser.CheckSlide / poser.CheckDown : 0f):0.000} m/s while down, " +
                         $"hips {_hipsLow:0.00}–{_hipsHigh:0.00} m up, ankles up to {poser.CheckMiss * 100f:0.0} cm off their marks");
                poser.CheckSlide = poser.CheckDown = poser.CheckMiss = 0f;
            }

            _measured = _current;
            _steps = 0;
            _settledAt = 0f;
            _slide = _downTime = _time = 0f;
            _hipsLow = float.MaxValue;
            _hipsHigh = float.MinValue;
        }

        float floor = _walker.State.Position.Y;
        _time += delta;
        if (_time - delta < Settle && _time >= Settle)
        {
            // Measured once the move has settled in (not the start, the turn or the stop).
            model.Poser.CheckSlide = model.Poser.CheckDown = model.Poser.CheckMiss = 0f;
            _steps = 0;
            _settledAt = _time;
        }
        for (int i = 0; i < 2; i++)
        {
            Vector3 toe = model.Attachment(i == 0 ? "LeftToeBase" : "RightToeBase").GlobalPosition;
            bool down = model.Steps is { } steps ? (i == 0 ? steps.LeftPlanted : steps.RightPlanted) : toe.Y - floor < 0.06f;

            if (down && _down[i])
            {
                _slide += new Vector2(toe.X - _toe[i].X, toe.Z - _toe[i].Z).Length();
                _downTime += delta;
            }
            else if (down && !_down[i])
            {
                _steps++;
            }

            _down[i] = down;
            _toe[i] = toe;
        }

        float hips = model.Attachment("Hips").GlobalPosition.Y - floor;

        _hipsLow = Mathf.Min(_hipsLow, hips);
        _hipsHigh = Mathf.Max(_hipsHigh, hips);
    }

    public InputCommand Next(int tick, PlayerState me)
    {
        if (_first < 0)
        {
            _first = tick;
        }

        float seconds = (tick - _first) * _sim.Config.Dt;
        int index = 0;
        float yaw = _yaw;
        float start = 0f;
        for (float end = _moves[0].Seconds; index < _moves.Length - 1 && seconds >= end; end += _moves[index].Seconds)
        {
            start = end;
            index++;
            if (_moves[index].Turn)
            {
                yaw += Mathf.Pi;
            }
        }

        if (index != _current)
        {
            _current = index;
            GD.Print($"GAIT DEMO {_moves[index].Name} at tick {tick}");
        }

        if (index == _moves.Length - 1 && seconds >= Total())
        {
            GD.Print("GAIT DEMO done");
            GetTree().Quit();
        }

        Move move = _moves[index];
        // Looking round, the head turns to each look in turn at a bot's pace (brain.jsonc "headTurnSpeed_degps").
        float look = 0f;
        if (move.Look)
        {
            foreach ((float from, float head) in LookRound)
            {
                look = seconds - start >= from ? head : look;
            }
        }

        _head = Mathf.MoveToward(_head, Mathf.DegToRad(look), Mathf.DegToRad(300f) * _sim.Config.Dt);
        return new InputCommand
        {
            Tick = tick, Yaw = yaw, Pitch = 0f, HeadYaw = _head, Buttons = move.Buttons, Move = new System.Numerics.Vector2(move.Right, move.Forward),
        };
    }

    private float Total()
    {
        float total = 0f;
        foreach (Move m in _moves)
        {
            total += m.Seconds;
        }

        return total;
    }

    /// <summary>From the side of the lane, level with the hips, keeping its distance; for the look round, closer, from in front and to the side.</summary>
    private void Track()
    {
        SVector3 at = _walker!.State.Position;
        bool look = _current >= 0 && _moves[_current].Look;
        SVector3 eye = look
            ? at + (_side * 0.7f + ViewAngles.FlatForward(_walker.State.Yaw)) * 2.4f + new SVector3(0f, 1.5f, 0f)
            : at + _side * 5.5f + new SVector3(0f, 1.1f, 0f);
        _camera.GlobalPosition = eye.ToGodot();
        _camera.LookAt((at + new SVector3(0f, look ? 1.25f : 0.95f, 0f)).ToGodot(), Vector3.Up);
    }

    /// <summary>The ground under <paramref name="near"/>, if it's within a couple of metres of its height.</summary>
    private bool Ground(SVector3 near, out SVector3 at)
    {
        bool hit = _sim.Collision.SweepSphere(near + new SVector3(0f, 2f, 0f), near - new SVector3(0f, 2f, 0f), 0f, out var floor);
        at = hit ? floor.Point : near;
        return hit;
    }

    /// <summary>Whether the lane from <paramref name="from"/> along <paramref name="yaw"/> has room to stand and level ground all along it.</summary>
    private bool Clear(SVector3 from, float yaw)
    {
        SVector3 ahead = ViewAngles.FlatForward(yaw);
        SVector3 side = new(-ahead.Z, 0f, ahead.X);
        for (float d = -LaneBack; d <= LaneAhead; d += 0.5f)
        {
            foreach (float s in new[] { -LaneSide, 0f, LaneSide })
            {
                SVector3 at = from + ahead * d + side * s;
                bool room = !_sim.Collision.SweepSphere(at + new SVector3(0f, 0.45f, 0f), at + new SVector3(0f, 1.6f, 0f), 0.4f, out _);
                bool ground = _sim.Collision.SweepSphere(at + new SVector3(0f, 0.3f, 0f), at - new SVector3(0f, 0.3f, 0f), 0f, out var floor) &&
                              System.MathF.Abs(floor.Point.Y - from.Y) < 0.1f;
                if (!room || !ground || !(_sim.Level?.Bounds.Contains(at + new SVector3(0f, 1f, 0f)) ?? true))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>One move: how long, the stick (right and forward), the buttons, whether it turns round first, and whether it's the look round.</summary>
    private readonly record struct Move(string Name, float Seconds, float Right, float Forward, InputButtons Buttons, bool Turn = false, bool Look = false);
}
