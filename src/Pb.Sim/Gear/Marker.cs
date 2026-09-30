using Pb.Sim.Events;

namespace Pb.Sim.Gear;

/// <summary>Per-tick marker controls, taken from the player's input command and state.</summary>
public readonly record struct MarkerInput(bool Trigger, bool Refill, bool ToggleMode, bool Sprinting, bool Alive);

/// <summary>A shot the marker fired this tick; the sim turns it into a ball.</summary>
public struct ShotRequest
{
    /// <summary>Offset from the tick start (s).</summary>
    public float TimeOffset;
    /// <summary>Muzzle speed before per-shot variance (m/s), already reduced for low air.</summary>
    public float MuzzleSpeed;
    public uint Sequence;
}

/// <summary>
/// Fire control + paint + refill + air, wired together: it decides whether the trigger produces
/// balls this tick and at what speed.
/// </summary>
public sealed class Marker
{
    private bool _previousTrigger;
    private bool _previousRefill;
    private bool _previousToggle;
    private bool _lowAirReported;

    public Marker(FireControlParams fire, LoaderParams loader, AirParams air, float muzzleVelocity)
    {
        Fire = new FireControl(fire);
        Paint = new PaintSupply(loader);
        Refill = new Refill();
        Air = new AirTank(air);
        MuzzleVelocity = muzzleVelocity;
    }

    public FireControl Fire { get; }

    public PaintSupply Paint { get; }

    public Refill Refill { get; }

    public AirTank Air { get; }

    public float MuzzleVelocity { get; set; }

    /// <summary>Shots fired so far; feeds the per-shot seed.</summary>
    public uint ShotSequence { get; private set; }

    public int Update(double t0, float dt, in MarkerInput input, Span<ShotRequest> shots, SimEventQueue events, int playerId, byte team, int tick)
    {
        bool pull = input.Trigger && !_previousTrigger;
        bool refillPressed = input.Refill && !_previousRefill;
        bool togglePressed = input.ToggleMode && !_previousToggle;
        _previousTrigger = input.Trigger;
        _previousRefill = input.Refill;
        _previousToggle = input.ToggleMode;

        if (!input.Alive)
        {
            Refill.Reset();
            Fire.Update(t0, dt, false, true, Span<float>.Empty);
            return 0;
        }

        if (togglePressed)
        {
            Fire.Mode = Fire.Mode == FireMode.Semi ? FireMode.Ramping : FireMode.Semi;
            events.Add(new SimEvent
            {
                Type = SimEventType.FireModeChanged, Tick = tick, PlayerId = playerId, Team = team,
                Extra = (int)Fire.Mode, TargetId = -1, ColliderId = -1,
            });
        }

        UpdateRefill(dt, input, pull, refillPressed, events, playerId, team, tick);

        bool empty = Paint.Loader == 0 || !Air.CanFire;
        if (pull && empty && !Refill.Active && !input.Sprinting)
        {
            events.Add(new SimEvent
            {
                Type = SimEventType.DryFire, Tick = tick, PlayerId = playerId, Team = team, TargetId = -1, ColliderId = -1,
            });
        }

        bool blocked = input.Sprinting || Refill.Active || empty;
        Span<float> offsets = stackalloc float[Math.Min(shots.Length, 8)];
        int wanted = Fire.Update(t0, dt, input.Trigger, blocked, offsets);

        int fired = 0;
        for (int i = 0; i < wanted; i++)
        {
            if (Paint.Loader == 0 || !Air.CanFire)
            {
                break;
            }

            float speed = MuzzleVelocity * Air.VelocityFactor;
            Paint.Loader--;
            Air.ConsumeShot();
            ShotSequence++;
            shots[fired++] = new ShotRequest { TimeOffset = offsets[i], MuzzleSpeed = speed, Sequence = ShotSequence };
        }

        if (!_lowAirReported && Air.BelowRegulator)
        {
            _lowAirReported = true;
            events.Add(new SimEvent
            {
                Type = SimEventType.AirLow, Tick = tick, PlayerId = playerId, Team = team, Value = Air.Pressure,
                TargetId = -1, ColliderId = -1,
            });
        }

        return fired;
    }

    /// <summary>Full loader, pods and tank (debug key; round start from Phase 2).</summary>
    public void ResetGear()
    {
        Paint.Fill();
        Air.Fill();
        Refill.Reset();
        Fire.Reset();
        _lowAirReported = false;
    }

    private void UpdateRefill(float dt, in MarkerInput input, bool pull, bool refillPressed, SimEventQueue events, int playerId, byte team, int tick)
    {
        LoaderParams p = Paint.Params;
        if (Refill.Active)
        {
            bool cancel = (p.CancelOnFire && pull) || (p.CancelOnSprint && input.Sprinting);
            if (cancel)
            {
                int moved = Refill.Cancel(Paint);
                events.Add(new SimEvent
                {
                    Type = SimEventType.RefillCancelled, Tick = tick, PlayerId = playerId, Team = team, Extra = moved,
                    TargetId = -1, ColliderId = -1,
                });
                return;
            }

            int transferred = Refill.Advance(Paint, dt, out bool completed);
            if (completed)
            {
                events.Add(new SimEvent
                {
                    Type = SimEventType.RefillCompleted, Tick = tick, PlayerId = playerId, Team = team, Extra = transferred,
                    TargetId = -1, ColliderId = -1,
                });
            }

            return;
        }

        if (!refillPressed || input.Sprinting)
        {
            return;
        }

        bool started = Refill.TryStart(Paint);
        events.Add(new SimEvent
        {
            Type = started ? SimEventType.RefillStarted : SimEventType.RefillDenied, Tick = tick, PlayerId = playerId,
            Team = team, TargetId = -1, ColliderId = -1,
        });
    }
}
