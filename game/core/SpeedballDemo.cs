using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Player;
using Pb.Sim;
using Pb.Sim.AI;
using Pb.Sim.Core;
using Pb.Sim.Match;
using Pb.Sim.Players;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --speedball-demo</c> (on <c>--level=sports_ground</c>): a speedball point through your own eyes, for screenshots.
/// The countdown from your start box, the breakout as the bots sprint for their bunkers, then (the other side gone quiet)
/// you at their buzzer, holding Interact until it's hung, and the score between points (or, with <c>--race-to=1</c>, the
/// match's summary). Prints what it does; each point plays it again.
/// </summary>
public sealed class SpeedballDemo : ICommandSource
{
    /// <summary>Ticks after the horn: watching the breakout, then at their buzzer.</summary>
    private const int HangAt = 300;

    private readonly SimWorld _sim;
    private readonly PlayerController _player;
    private readonly IReadOnlyList<BotBrain> _bots;
    private int _liveAt = -1;
    private bool _there;

    public SpeedballDemo(SimWorld sim, PlayerController player, IReadOnlyList<BotBrain> bots)
    {
        _sim = sim;
        _player = player;
        _bots = bots;
    }

    public InputCommand Next(int tick, PlayerState state)
    {
        if (_sim.Match is not { Buzzers: { } buzzers } match)
        {
            return new InputCommand { Tick = tick, Yaw = state.Yaw, Pitch = state.Pitch };
        }

        int theirs = 1 - state.Team;
        SVector3 post = buzzers.Post(theirs);
        if (match.Phase != MatchPhase.Live)
        {
            // In the box (or the point over): looking up the field, at their station.
            (float y, float p) = ViewAngles.FromDirection(post + new SVector3(0f, 1.2f, 0f) - state.EyePosition);
            return new InputCommand { Tick = tick, Yaw = y, Pitch = Math.Clamp(p, -0.3f, 0.2f) };
        }

        if (_liveAt < 0)
        {
            _liveAt = tick;
            GD.Print($"SPEEDBALL DEMO the horn at tick {tick}: watching the breakout, then at their buzzer from {HangAt} ticks on");
        }

        if (!_there && tick - _liveAt >= HangAt)
        {
            // Their side holds still, and you're at their station, a step short of its post, facing it.
            foreach (BotBrain bot in _bots)
            {
                if (bot.Self.Team == theirs)
                {
                    bot.Passive = true;
                }
            }

            SVector3 home = _sim.Level!.Field!.StartOf(theirs, 0, 1);
            SVector3 away = SVector3.Normalize(new SVector3(home.X - post.X, 0f, home.Z - post.Z) + new SVector3(0.6f, 0f, 0f));
            SVector3 at = post + away * 0.8f;
            _player.Teleport(at, MathF.Atan2(-(post.X - at.X), -(post.Z - at.Z)));
            _there = true;
            GD.Print($"SPEEDBALL DEMO at their buzzer ({post}): holding Interact");
        }

        (float yaw, float pitch) = ViewAngles.FromDirection(post + new SVector3(0f, 0.9f, 0f) - state.EyePosition);
        return new InputCommand
        {
            Tick = tick, Yaw = yaw, Pitch = Math.Clamp(pitch, -0.6f, 0.3f), Buttons = _there ? InputButtons.Interact : InputButtons.None,
        };
    }
}
