using System.Collections.Generic;
using System.Linq;
using Godot;
using Pb.Game.Player;
using Pb.Game.Ui;
using Pb.Sim;
using Pb.Sim.Level;
using Pb.Sim.Players;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --ladder-demo</c>: an opponent climbs a ladder, steps off at the top, turns round, gets back on and climbs down,
/// watched from the side by a camera that rises with them, for checking hands and feet on the rungs and the marker slung
/// on the back. The ladder is the tallest in the level, or with <c>--ladder=N</c> the level's Nth. It prints how long each
/// way took and quits. Like the gait demo it runs by sim ticks, so it plays out the same at any rendering speed.
/// </summary>
public partial class LadderDemo : Node, ICommandSource
{
    /// <summary>Standing before getting on, at the top before turning round, turning round, and at the bottom at the end (s).</summary>
    private const float Before = 0.6f, AtTop = 1.2f, Turn = 0.5f, After = 1f;

    private SimWorld _sim = null!;
    private OpponentPawn? _who;
    private Camera3D _camera = null!;
    private LadderSpec _ladder = null!;
    private SVector3 _side;
    private int _first = -1;
    private int _topAt = -1;
    private int _downAt = -1;
    private int _endAt = -1;
    private bool _was;

    /// <param name="towardSun">Which way the sun is (world, level): the camera watches from that side, so the climber's lit.</param>
    public void Start(SimWorld sim, IReadOnlyList<OpponentPawn> opponents, Hud hud, float farClip, SVector3 towardSun)
    {
        _sim = sim;
        _who = opponents.FirstOrDefault(o => o.State.Alive);
        hud.Visible = false;
        int index = int.TryParse(Args.Value("--ladder"), out int n) ? n : -1;
        if (index < 0 && sim.Ladders.Count > 0)
        {
            index = Enumerable.Range(0, sim.Ladders.Count).MaxBy(i => sim.Ladders[i].Height);
        }

        if (_who is null || index < 0 || index >= sim.Ladders.Count)
        {
            GD.PushError("LADDER DEMO found nobody or no such ladder in this level; quitting");
            GetTree().Quit(1);
            return;
        }

        _ladder = sim.Ladders[index];
        _side = _ladder.Across;
        if (SVector3.Dot(_side, towardSun) < 0f)
        {
            _side = -_side;
        }

        _who.Teleport(_ladder.ClimbPoint(_ladder.Foot.Y, sim.Config.Movement.Climbing.Standoff + 0.2f), _ladder.Facing);
        _who.Steer(this);
        _camera = new Camera3D { Name = "LadderCamera", Fov = 50f, Far = farClip, Near = 0.05f };
        AddChild(_camera);
        _camera.MakeCurrent();
        Aim();
        GD.Print($"LADDER DEMO {_who.State.Name} climbs the {sim.Level!.Owners[_ladder.Owner]} ladder, {_ladder.Height:0.0} m");
    }

    public InputCommand Next(int tick, PlayerState me)
    {
        if (_first < 0)
        {
            _first = tick;
        }

        float seconds = (tick - _first) * _sim.Config.Dt;
        float dt = _sim.Config.Dt;
        _was |= me.OnLadder;
        if (_topAt < 0 && _was && !me.OnLadder && me.Position.Y > _ladder.TopY - 0.15f)
        {
            _topAt = tick;
            _was = false;
        }
        else if (_topAt >= 0 && _downAt < 0 && _was && !me.OnLadder)
        {
            _downAt = tick;
        }

        if (_downAt >= 0 && _endAt < 0)
        {
            _endAt = tick;
            GD.Print($"LADDER DEMO up in {(_topAt - _first) * dt:0.0} s, down in {(_downAt - _topAt) * dt:0.0} s");
        }

        if (_endAt >= 0 && (tick - _endAt) * dt >= After)
        {
            GetTree().Quit();
        }

        float outward = _ladder.Facing + Mathf.Pi;
        if (_topAt < 0)
        {
            // Up: a tap on interact, then forward all the way.
            bool going = seconds >= Before;
            bool tap = going && seconds < Before + 2f * dt;
            return new InputCommand
            {
                Tick = tick, Yaw = _ladder.Facing, Move = new System.Numerics.Vector2(0f, going ? 1f : 0f),
                Buttons = tap ? InputButtons.Interact : InputButtons.None,
            };
        }

        float top = (tick - _topAt) * dt;
        if (top < AtTop)
        {
            return new InputCommand { Tick = tick, Yaw = _ladder.Facing };
        }

        // Round to face out over the top, a tap on interact, then back all the way down.
        float turned = Mathf.Clamp((top - AtTop) / Turn, 0f, 1f);
        bool down = top >= AtTop + Turn;
        return new InputCommand
        {
            Tick = tick, Yaw = _ladder.Facing + Mathf.Pi * turned, Move = new System.Numerics.Vector2(0f, down ? -1f : 0f),
            Buttons = down && top < AtTop + Turn + 2f * dt ? InputButtons.Interact : InputButtons.None,
        };
    }

    public override void _Process(double delta)
    {
        if (_who is not null)
        {
            Aim();
        }
    }

    /// <summary>From behind and to the side (the climber's back and the ladder's rungs both show), level with their chest.</summary>
    private void Aim()
    {
        SVector3 at = _who!.State.Position + new SVector3(0f, 1.2f, 0f) + _ladder.Forward * 0.2f;
        SVector3 eye = at + _side * 1.9f - _ladder.Forward * 1.9f + new SVector3(0f, 0.25f, 0f);
        _camera.GlobalPosition = eye.ToGodot();
        _camera.LookAt(at.ToGodot(), Vector3.Up);
    }
}
