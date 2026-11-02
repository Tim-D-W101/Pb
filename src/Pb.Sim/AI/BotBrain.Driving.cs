using System.Numerics;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Players;
using Pb.Sim.Vehicles;

namespace Pb.Sim.AI;

/// <summary>Where a bot is with a ride (Phase 5).</summary>
public enum RideState : byte
{
    None,

    /// <summary>Walking to an empty buggy to drive it.</summary>
    Boarding,

    /// <summary>At the wheel, driving the tracks toward where it's going.</summary>
    Driving,

    /// <summary>Stopping to get out (there, or to fight, or stuck).</summary>
    Alighting,
}

/// <summary>
/// Bots driving (Phase 5, Thornwick Woods' buggies), by the same rules and the same commands as people: a bot going
/// farther than <see cref="BrainParams.DriveBeyond"/>, with a buggy standing empty nearby and no enemy in sight, walks to
/// its driver's door and gets in; drives the level's tracks (<see cref="RoadGraph"/>) to the point on them nearest where
/// it's going, steering for a point ahead along them and slowing for bends; with an enemy in sight it fires from the wheel
/// (one-handed, wider) and drives on, slower; and it stops and gets out there, or when an enemy comes close, or when it's
/// got nowhere after backing up. Then it walks the rest.
/// </summary>
public sealed partial class BotBrain
{
    private readonly List<Vector3> _route = new(32);
    private RideState _ride;
    private int _rideVehicle = -1;
    private Vector3 _rideGoal;
    private Vector3 _rideDoor;
    private BotGait _rideGait;
    private int _routeAt;
    private float _rideStuck;
    private float _backUpFor;
    private int _backUps;
    private float _boardingFor;
    private int _noRideUntil;

    /// <summary>Where this bot is with a ride.</summary>
    public RideState Ride => _ride;

    /// <summary>The goal it's riding toward (where it'll walk on from), while it has a ride.</summary>
    public Vector3 RideGoal => _rideGoal;

    /// <summary>
    /// A new goal while riding: the same place (or near it) changes nothing; anywhere else, boarding stops (the bot walks
    /// there instead) and a bot at the wheel drives there instead, or gets out if it's near. True if the ride took it.
    /// </summary>
    private bool RideTakes(Vector3 goal, BotGait gait)
    {
        if (_ride == RideState.None)
        {
            return false;
        }

        if (FlatDistance(goal, _rideGoal) < 3f)
        {
            _rideGait = gait;
            return true;
        }

        if (_ride is RideState.Driving or RideState.Alighting && Senses.Focus is { Visible: true })
        {
            return true; // fighting from the wheel: where to stand or take cover means nothing in a vehicle
        }

        if (_ride == RideState.Boarding)
        {
            EndRide();
            return false;
        }

        // At the wheel: on to the new goal along the tracks, or out here if it's near.
        _rideGoal = goal;
        _rideGait = gait;
        if (FlatDistance(Self.Position, goal) < _b.DriveBeyond * 0.5f || !Roads.Route(Self.Position, goal, _route, out _))
        {
            _ride = RideState.Alighting;
        }
        else
        {
            _ride = RideState.Driving;
            _routeAt = 0;
            _route.Insert(0, Self.Position with { Y = 0f });
        }

        return true;
    }

    /// <summary>
    /// Before walking a new path to the goal: a buggy instead, if the goal's far, one stands empty near, the way by the
    /// tracks is the quicker, and nothing's in sight. True if it's off to board one (its goal is the driver's door now).
    /// </summary>
    private bool StartRide()
    {
        VehicleSet vehicles = _sim.Vehicles;
        if (vehicles.Count == 0 || _tick < _noRideUntil || Self.OnLadder || Self.Vehicle >= 0 || Senses.Focus is { Visible: true } ||
            _sim.Match?.Objective?.IsKeyPlayer(Self.Id) == true || Self.Unarmed)
        {
            return false;
        }

        float far = FlatDistance(Self.Position, _goal);
        if (far < _b.DriveBeyond || !Roads.Nearest(_goal, out _, out float goalOff))
        {
            return false;
        }

        // How long it'd take on foot at the pace it's going, against walking to a buggy, driving the tracks (at about
        // the pace it keeps on them) and walking the rest.
        Players.MovementParams move = _sim.Config.Movement;
        float pace = _gait switch
        {
            BotGait.Run => move.RunSpeed,
            BotGait.Stroll => move.WalkSpeed * _b.StrollPace,
            _ => move.WalkSpeed,
        };
        float run = move.RunSpeed, drive = vehicles[0].Params.MaxSpeed * _b.DriveSpeedShare * 0.85f;
        int best = -1;
        float bestTime = far / pace;
        for (int i = 0; i < vehicles.Count; i++)
        {
            VehicleState v = vehicles[i];
            Vector3 door = VehicleSet.Door(v, v.Params.DriverSeat);
            float toDoor = FlatDistance(Self.Position, door);
            if (v.Driver >= 0 || MathF.Abs(v.Speed) > 0.2f || toDoor > _b.DriveVehicleWithin || !_squad.MayClaimVehicle(i, this) ||
                !Roads.Route(v.Position, _goal, _route, out float along))
            {
                continue;
            }

            Roads.Nearest(v.Position, out _, out float onto);
            float time = toDoor / run + (onto + along) / drive + goalOff / pace;
            if (time < bestTime)
            {
                best = i;
                bestTime = time;
            }
        }

        if (best < 0 || !_squad.ClaimVehicle(best, this))
        {
            return false;
        }

        _ride = RideState.Boarding;
        _rideVehicle = best;
        _rideGoal = _goal;
        _rideGait = _gait;
        _boardingFor = 0f;
        VehicleState chosen = vehicles[best];
        _rideDoor = VehicleSet.Door(chosen, chosen.Params.DriverSeat);
        _goal = _rideDoor;
        _gait = BotGait.Run;
        _needPath = true;
        _path.Clear();
        _waypoint = 0;
        return true;
    }

    /// <summary>
    /// Walking to the buggy (on the path to its door): in at the door, or giving up if someone else took it, it moved,
    /// or it's taken too long. True while it's what the bot's doing.
    /// </summary>
    private bool Board(float dt)
    {
        if (_ride != RideState.Boarding)
        {
            return false;
        }

        VehicleState v = _sim.Vehicles[_rideVehicle];
        _boardingFor += dt;
        if (v.Driver >= 0 || FlatDistance(VehicleSet.Door(v, v.Params.DriverSeat), _rideDoor) > 2f || _boardingFor > 30f)
        {
            Vector3 goal = _rideGoal;
            BotGait gait = _rideGait;
            EndRide();
            GoTo(goal, gait);
            return true;
        }

        if (FlatDistance(Self.Position, _rideDoor) <= 1.4f)
        {
            // At the door: in (a press is the button going down, so every other tick).
            _cmd.Move = Vector2.Zero;
            if ((_tick & 1) == 0)
            {
                _cmd.Buttons |= InputButtons.Interact;
            }

            LookToward(v.Position, slow: false);
            return true;
        }

        return false; // on its way: the path to the door
    }

    /// <summary>At the wheel (after the mode's had its say): the pedal and the wheel along the route, or stopping to get out.</summary>
    private void DriveTick(float dt)
    {
        VehicleState? v = _sim.Vehicles.Of(Self);
        if (v is null || v.Driver != Self.Id)
        {
            return; // a passenger rides
        }

        // Firing is the mode's to decide (it aims as on foot); the rest of the buttons mean nothing at the wheel.
        _cmd.Buttons &= ~(InputButtons.Crouch | InputButtons.Sprint | InputButtons.Jump | InputButtons.Interact | InputButtons.Walk | InputButtons.Refill);
        // An enemy in sight (or just seen) has its full attention: it looks that way to make it out and then aim, while
        // driving on; the mode aims and fires as on foot once it has.
        bool fighting = Senses.Focus is { Visible: true } or { HasLead: true };
        if (_ride == RideState.Boarding)
        {
            // Just in: the way along the tracks from here.
            _ride = RideState.Driving;
            _routeAt = 0;
            _rideStuck = 0f;
            _backUps = 0;
            _backUpFor = 0f;
            if (!Roads.Route(v.Position, _rideGoal, _route, out _))
            {
                _ride = RideState.Alighting;
            }
            else
            {
                _route.Insert(0, v.Position); // from where it stands onto the track
            }
        }
        else if (_ride is not (RideState.Driving or RideState.Alighting))
        {
            // In one it didn't mean to drive (put there by a test or the server): out again where it stands.
            _ride = RideState.Alighting;
            _rideGoal = Self.Position;
        }

        // In sight, where the enemy is (its last known spot can still be unset the moment it comes into view).
        if (_ride == RideState.Driving && Senses.Focus is { Visible: true } seen && _sim.FindPlayer(seen.TargetId) is { } foe &&
            FlatDistance(foe.Position, Self.Position) < _b.DriveFightOutWithin)
        {
            _ride = RideState.Alighting; // too close to fight from the wheel: out to fight on foot
            _noRideUntil = _tick + (int)(30f / _sim.Dt);
        }

        VehicleParams k = v.Params;
        if (_ride == RideState.Alighting)
        {
            // Braking to a stop (off the pedals at a crawl the brakes hold it), then out.
            _cmd.Move = new Vector2(0f, v.Speed > 0.5f ? -1f : v.Speed < -0.5f ? 1f : 0f);
            if (MathF.Abs(v.Speed) <= k.ExitBelow * 0.5f && (_tick & 1) == 0)
            {
                _cmd.Buttons |= InputButtons.Interact;
            }

            return;
        }

        // Where it's got to along the route, and the point ahead on it to steer for.
        Vector3 at = v.Position;
        float ahead = _b.DriveLookAhead + MathF.Abs(v.Speed) * _b.DriveLookAheadPerSpeed;
        Vector3 target = RoadGraph.Ahead(_route, ref _routeAt, at, ahead, out float left);
        if (left < _b.DriveGetOutWithin)
        {
            _ride = RideState.Alighting;
            _cmd.Move = new Vector2(0f, v.Speed > 0.5f ? -1f : 0f);
            return;
        }

        // Steer for it: the wheel turned toward it, as far as it goes.
        Vector3 to = target - at;
        Vector3 forward = v.Forward;
        float angle = MathF.Atan2(forward.X * to.Z - forward.Z * to.X, forward.X * to.X + forward.Z * to.Z); // + is to the right
        float wheel = Math.Clamp(-angle / MathF.Max(k.MaxSteer, 0.1f) * 1.4f, -1f, 1f); // + turns left
        // As fast as the bend ahead and the distance left allow.
        float bend = MathF.Min(1f, MathF.Abs(angle) / (MathF.PI * 0.5f));
        float want = MathF.Min(k.MaxSpeed * _b.DriveSpeedShare,
            _b.DriveCornerSpeed + (k.MaxSpeed * _b.DriveSpeedShare - _b.DriveCornerSpeed) * (1f - bend));
        // Slow enough to stop by the end braking at half what the ground under it allows, and to take the bend on it.
        float decel = v.StopDecel;
        want = MathF.Min(want, MathF.Sqrt(2f * decel * 0.5f * MathF.Max(0f, left - _b.DriveGetOutWithin * 0.5f)) + 0.5f);
        want *= MathF.Sqrt(Math.Clamp(decel / (VehicleParams.Gravity * _b.DriveGripFull), 0.3f, 1f));
        if (fighting)
        {
            want = MathF.Min(want, _b.DriveFightSpeed); // steadier to fire from
        }
        float pedal = Math.Clamp((want - v.Speed) * 0.8f, -1f, 1f);

        // Getting nowhere (something in the way): back up a little, turning the other way, then go on; twice, and out.
        if (_backUpFor > 0f)
        {
            _backUpFor -= dt;
            _cmd.Move = new Vector2(wheel, -1f); // wheel the other way when backing (stick right: wheel right)
            if (!fighting)
            {
                LookToward(target, slow: false);
            }

            return;
        }

        _rideStuck = pedal > 0.3f && MathF.Abs(v.Speed) < 0.3f ? _rideStuck + dt : 0f;
        if (_rideStuck >= _b.DriveStuck)
        {
            _rideStuck = 0f;
            if (++_backUps > 2)
            {
                _ride = RideState.Alighting;
                _noRideUntil = _tick + (int)(30f / _sim.Dt); // on foot for a while: the way it took is blocked
                return;
            }

            _backUpFor = 1.2f;
        }

        // The stick: forward and back are the pedal, right is the wheel to the right. Fighting, it looks at the enemy (the
        // mode's aim takes over once it's engaged); otherwise down the track.
        _cmd.Move = new Vector2(-wheel, pedal);
        if (!fighting)
        {
            LookToward(target, slow: false);
        }
        else if (Mode != BotMode.Engage && Senses.Focus is { } focus)
        {
            LookToward(focus.Visible && _sim.FindPlayer(focus.TargetId) is { } sighted ? sighted.Position : focus.LastKnown, slow: false);
        }
    }

    /// <summary>Out of the vehicle this tick (it was in one): the ride's over, on to the goal on foot.</summary>
    private void AfterRide()
    {
        if (_ride is RideState.Driving or RideState.Alighting && Self.Vehicle < 0)
        {
            Vector3 goal = _rideGoal;
            BotGait gait = _rideGait;
            EndRide();
            GoTo(goal, gait);
        }
    }

    private void EndRide()
    {
        if (_rideVehicle >= 0)
        {
            _squad.ReleaseVehicle(_rideVehicle, this);
        }

        _ride = RideState.None;
        _rideVehicle = -1;
        _route.Clear();
        _backUpFor = 0f;
        _rideStuck = 0f;
        _backUps = 0;
    }

    private RoadGraph Roads => _squad.Roads;
}
