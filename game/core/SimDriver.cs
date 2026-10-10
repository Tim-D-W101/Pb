using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Pb.Sim;
using Pb.Sim.Events;
using Pb.Sim.Players;

namespace Pb.Game.Core;

/// <summary>
/// Runs the sim on Godot's fixed physics tick: each tick it lets every player driver sample a
/// command and move, steps the sim, then hands the tick's events to presentation listeners.
/// </summary>
public partial class SimDriver : Node
{
    private readonly List<IPlayerDriver> _drivers = new();
    private readonly List<ISimEventListener> _listeners = new();
    private InputCommand[] _commands = Array.Empty<InputCommand>();
    private double _stepMsAccumulator;
    private int _stepSamples;

    public SimWorld? Sim { get; private set; }

    public bool Paused { get; set; }

    /// <summary>Average sim step time over the last ~0.5 s (ms), for the perf overlay.</summary>
    public double AverageStepMs { get; private set; }

    /// <summary>Exceptions caught while stepping (the smoke test fails on any).</summary>
    public int ErrorCount { get; private set; }

    public event Action<int>? Ticked;

    /// <summary>Before the drivers move anyone (playing with others: the network is taken in).</summary>
    public Action? BeforeTick { get; set; }

    /// <summary>After the step, before its events go to the listeners (playing with others: the network's turn).</summary>
    public Action? AfterStep { get; set; }

    public void Initialize(SimWorld sim)
    {
        Sim = sim;
        Engine.PhysicsTicksPerSecond = (int)MathF.Round(sim.Config.TickRate);
    }

    public void AddDriver(IPlayerDriver driver)
    {
        _drivers.Add(driver);
        _commands = new InputCommand[_drivers.Count];
    }

    public void AddListener(ISimEventListener listener) => _listeners.Add(listener);

    public override void _PhysicsProcess(double delta)
    {
        if (Sim is null || Paused)
        {
            return;
        }

        long start = Stopwatch.GetTimestamp();
        try
        {
            BeforeTick?.Invoke();
            for (int i = 0; i < _drivers.Count; i++)
            {
                _commands[i] = _drivers[i].Step(Sim.Tick, Sim.Dt);
            }

            Sim.Step(_commands);
            AfterStep?.Invoke();

            ReadOnlySpan<SimEvent> events = Sim.Events.Items;
            for (int i = 0; i < events.Length; i++)
            {
                for (int l = 0; l < _listeners.Count; l++)
                {
                    _listeners[l].OnSimEvent(events[i]);
                }
            }
        }
        catch (Exception ex)
        {
            ErrorCount++;
            GD.PushError($"Sim step failed at tick {Sim.Tick}: {ex}");
        }
        finally
        {
            Sim.Events.Clear();
        }

        _stepMsAccumulator += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (++_stepSamples >= 60)
        {
            AverageStepMs = _stepMsAccumulator / _stepSamples;
            _stepMsAccumulator = 0;
            _stepSamples = 0;
        }

        Ticked?.Invoke(Sim.Tick);
    }
}
