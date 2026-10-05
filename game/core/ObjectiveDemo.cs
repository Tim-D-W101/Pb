using System;
using Godot;
using Pb.Game.Player;
using Pb.Sim;
using Pb.Sim.AI;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --objective-demo</c> (with <c>--objective=retrieve</c> or <c>hold</c>): a short scripted look at the objective
/// through your own eyes, for screenshots, with the opponents quiet. Retrieve: from where you come in, the marker on the
/// case's building; then a few metres from the case (its light blinking, the marker on the case itself); then you pick it
/// up and turn to the nearest way out (its beam, the marker on it). Hold: the marker on the room from outside, then
/// inside it, its outline on the floor and the hold clock running. Prints what it does and quits.
/// </summary>
public sealed class ObjectiveDemo : ICommandSource
{
    private const int CloseAt = 60;
    private const int TakeAt = 160;
    private const int TurnAt = 170;
    private const int EndAt = 260;

    private readonly Node _host;
    private readonly SimWorld _sim;
    private readonly PlayerController _player;
    private readonly NavGrid _grid;
    private int _start = -1;
    private SVector3 _look;

    public ObjectiveDemo(Node host, SimWorld sim, PlayerController player, NavGrid grid)
    {
        _host = host;
        _sim = sim;
        _player = player;
        _grid = grid;
    }

    public InputCommand Next(int tick, PlayerState state)
    {
        ObjectiveState? objective = _sim.Match?.Objective;
        if (objective is null)
        {
            GD.PushError("--objective-demo needs --objective=retrieve or --objective=hold");
            _host.GetTree().Quit(1);
            return new InputCommand { Tick = tick };
        }

        if (_start < 0)
        {
            _start = tick;
            _look = objective.Kind == ObjectiveKind.Hold ? objective.Room!.Centre : Middle(objective.Spot.AreaBox);
            GD.Print($"OBJECTIVE DEMO {objective.Kind}: from where you come in, close up at {CloseAt}, " +
                     (objective.Kind == ObjectiveKind.Retrieve ? $"picked up at {TakeAt}, the way out from {TurnAt}" : "") + $", ends at {EndAt} (ticks)");
        }

        int t = tick - _start;
        if (t == CloseAt)
        {
            SVector3 target = objective.Kind == ObjectiveKind.Hold ? objective.Room!.Centre : objective.CasePosition;
            SVector3 spot = objective.Kind == ObjectiveKind.Hold ? Inside(objective.Room!) : Near(target, 4.5f);
            _player.Teleport(spot, YawTo(spot, target));
            _look = target;
            GD.Print($"OBJECTIVE DEMO t={t}: at {spot}, looking at {target}");
        }

        if (objective.Kind == ObjectiveKind.Retrieve && t == TakeAt)
        {
            _player.Teleport(objective.CasePosition, state.Yaw);
        }

        if (objective.Kind == ObjectiveKind.Retrieve && t == TurnAt)
        {
            ExitSpec exit = objective.Level.Exits[0];
            foreach (ExitSpec e in objective.Level.Exits)
            {
                if (SVector3.Distance(e.Position, state.Position) < SVector3.Distance(exit.Position, state.Position))
                {
                    exit = e;
                }
            }

            _look = exit.Position + new SVector3(0f, 4f, 0f);
            GD.Print($"OBJECTIVE DEMO t={t}: carrier={objective.Carrier}, looking at {exit.Name}");
        }

        if (t >= EndAt)
        {
            GD.Print($"OBJECTIVE DEMO done: carrier={objective.Carrier}, held={objective.Held:0.0} s, status={objective.Status}");
            _host.GetTree().Quit();
        }

        (float yaw, float pitch) = ViewAngles.FromDirection(_look + new SVector3(0f, 0.6f, 0f) - state.EyePosition);
        return new InputCommand { Tick = tick, Yaw = yaw, Pitch = Math.Clamp(pitch, -0.6f, 0.4f) };
    }

    private static SVector3 Middle(Pb.Sim.Collision.Aabb box) =>
        new((box.Min.X + box.Max.X) * 0.5f, MathF.Max(box.Min.Y, 0f) + 1f, (box.Min.Z + box.Max.Z) * 0.5f);

    /// <summary>Somewhere walkable about <paramref name="distance"/> from <paramref name="target"/> with a clear view of it.</summary>
    private SVector3 Near(SVector3 target, float distance)
    {
        for (int k = 0; k < 16; k++)
        {
            float a = k * MathF.Tau / 16f;
            var at = target + new SVector3(MathF.Sin(a) * distance, 0f, MathF.Cos(a) * distance);
            if (_grid.TrySnap(at, out SVector3 spot) && MathF.Abs(spot.Y - target.Y) < 0.5f &&
                !_sim.Collision.SweepSphere(spot + new SVector3(0f, 1.6f, 0f), target + new SVector3(0f, 0.2f, 0f), 0f, out _))
            {
                return spot;
            }
        }

        return target;
    }

    /// <summary>A walkable spot in the room near its edge, looking across it.</summary>
    private SVector3 Inside(HoldRoom room)
    {
        Pb.Sim.Collision.Aabb box = room.Boxes[0];
        for (float f = 0.2f; f < 0.5f; f += 0.05f)
        {
            var at = new SVector3(box.Min.X + (box.Max.X - box.Min.X) * f, room.Centre.Y, box.Min.Z + (box.Max.Z - box.Min.Z) * f);
            if (_grid.TrySnap(at, out SVector3 spot) && room.Contains(spot + new SVector3(0f, 0.1f, 0f)))
            {
                return spot;
            }
        }

        return room.Centre;
    }

    private static float YawTo(SVector3 from, SVector3 to) => MathF.Atan2(-(to.X - from.X), -(to.Z - from.Z));
}
