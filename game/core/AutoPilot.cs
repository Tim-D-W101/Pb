using System;
using System.Collections.Generic;
using Pb.Sim;
using Pb.Sim.Ballistics;
using Pb.Sim.Core;
using Pb.Sim.Players;
using Pb.Sim.Range;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// Scripted shooter for the headless smoke test and the demo capture. It cycles through the
/// static targets and aims with flat-fire holdover. Ramping pulls at 8 Hz, and it refills
/// when the loader runs dry.
/// </summary>
public sealed class AutoPilot : ICommandSource
{
    private readonly SimWorld _sim;
    private readonly List<TargetSpec> _targets = new();
    private readonly int _ticksPerTarget;

    public AutoPilot(SimWorld sim, float secondsPerTarget = 1.5f)
    {
        _sim = sim;
        foreach (TargetSpec t in sim.Range?.Targets ?? (IReadOnlyList<TargetSpec>)Array.Empty<TargetSpec>())
        {
            if (t.Motion is null)
            {
                _targets.Add(t);
            }
        }

        _ticksPerTarget = Math.Max(1, (int)(secondsPerTarget * sim.Config.TickRate));
    }

    /// <summary>When set, overrides the aim (demo camera moves).</summary>
    public Func<int, (float Yaw, float Pitch)?>? AimOverride { get; set; }

    public bool HoldFire { get; set; }

    public InputCommand Next(int tick, PlayerState state)
    {
        (float yaw, float pitch) = AimAtTarget(tick, state);
        if (AimOverride?.Invoke(tick) is { } aim)
        {
            (yaw, pitch) = aim;
        }

        InputButtons buttons = InputButtons.None;
        if (tick == 1 && state.Marker.Fire.Mode != Pb.Sim.Gear.FireMode.Ramping)
        {
            buttons |= InputButtons.ToggleFireMode;
        }

        if (state.Marker.Paint.Loader == 0 && !state.Marker.Refill.Active)
        {
            buttons |= tick % 2 == 0 ? InputButtons.Refill : InputButtons.None;
        }
        else if (!HoldFire && !state.Marker.Refill.Active && tick % 15 < 7)
        {
            buttons |= InputButtons.Fire;
        }

        return new InputCommand { Tick = tick, Yaw = yaw, Pitch = pitch, Buttons = buttons };
    }

    private (float Yaw, float Pitch) AimAtTarget(int tick, PlayerState state)
    {
        if (_targets.Count == 0)
        {
            return (state.Yaw, 0.05f);
        }

        TargetSpec target = _targets[tick / _ticksPerTarget % _targets.Count];
        SVector3 chest = target.BasePosition + new SVector3(0f, 1.0f, 0f);
        SVector3 eye = state.EyePosition;
        float distance = new System.Numerics.Vector2(chest.X - eye.X, chest.Z - eye.Z).Length();
        ProjectileParams p = _sim.Config.Projectile;
        float holdover = FlatFire.Drop(p.DragFactor, p.Gravity, _sim.Config.Shot.MuzzleVelocity, distance);
        return ViewAngles.FromDirection(chest + new SVector3(0f, holdover, 0f) - eye);
    }
}
