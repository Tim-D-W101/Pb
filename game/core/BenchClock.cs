using System;
using System.Diagnostics;
using Godot;

namespace Pb.Game.Core;

/// <summary>
/// One edge of the mobile bench's clock (<see cref="MobileBench"/>): set to process first (or last) of every node, in both
/// the frame's process pass and each physics tick, so the time between the first edge and the last is everything the
/// scene does in that pass (scripts, the sim's ticks, animation, skeletons).
/// </summary>
public partial class BenchClock : Node
{
    private static long _processAt, _physicsAt;

    /// <summary>The time the scene's process passes and physics ticks have taken since it was last read (ms).</summary>
    public static double ProcessMs { get; set; }

    public static double PhysicsMs { get; set; }

    public static int Ticks { get; set; }

    /// <summary>True for the edge that goes first; false for the last.</summary>
    public bool First { get; init; }

    public override void _Ready()
    {
        ProcessPriority = First ? int.MinValue : int.MaxValue;
        ProcessPhysicsPriority = First ? int.MinValue : int.MaxValue;
    }

    public override void _Process(double delta)
    {
        if (First)
        {
            _processAt = Stopwatch.GetTimestamp();
        }
        else if (_processAt != 0)
        {
            ProcessMs += Stopwatch.GetElapsedTime(_processAt).TotalMilliseconds;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (First)
        {
            _physicsAt = Stopwatch.GetTimestamp();
        }
        else if (_physicsAt != 0)
        {
            PhysicsMs += Stopwatch.GetElapsedTime(_physicsAt).TotalMilliseconds;
            Ticks++;
        }
    }
}
