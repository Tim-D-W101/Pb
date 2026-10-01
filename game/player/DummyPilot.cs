using System;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Ballistics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Players;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Player;

/// <summary>
/// Practice-opponent behaviour until the bots arrive (M2.5). A dummy stands at its spawn. A sentry
/// that's <see cref="Hostile"/> turns toward the player when it can see them and, after a reaction
/// delay, pulls the trigger now and then with an aim that wanders. Once eliminated, either kind walks
/// toward the level's dead zone with its marker up and leaves the field. Deterministic per seed.
/// </summary>
public sealed class DummyPilot : ICommandSource
{
    private readonly SimWorld _sim;
    private readonly PlayerState _target;
    private readonly PracticeDef _def;
    private readonly bool _sentry;
    private readonly float _restYaw;
    private readonly SVector3 _exit;
    private Pcg32 _rng;
    private float _yaw;
    private float _pitch;
    private float _seenFor;
    private float _sincePull;
    private float _errorYaw;
    private float _errorPitch;
    private float _outFor;

    public DummyPilot(SimWorld sim, PlayerState self, PlayerState target, PracticeDef def, bool sentry, SVector3 exit)
    {
        _sim = sim;
        _target = target;
        _def = def;
        _sentry = sentry;
        _restYaw = _yaw = self.Yaw;
        _exit = exit;
        _rng = new Pcg32(SeedHash.Combine(sim.Config.MatchSeed, (ulong)self.Id));
        _sincePull = def.PullInterval_s;
    }

    /// <summary>Sentries only shoot while hostile (the smoke test and the screenshot tour keep them quiet).</summary>
    public bool Hostile { get; set; } = true;

    public bool IsSentry => _sentry;

    public InputCommand Next(int tick, PlayerState self)
    {
        float dt = _sim.Dt;
        if (!self.Alive)
        {
            return WalkOff(tick, self, dt);
        }

        InputButtons buttons = InputButtons.None;
        _sincePull += dt;
        if (_sentry && Hostile && CanSee(self, out SVector3 aimPoint, out float distance))
        {
            _seenFor += dt;
            (float wantYaw, float wantPitch) = ViewAngles.FromDirection(aimPoint - self.EyePosition);
            ProjectileParams ball = _sim.Config.Projectile;
            wantPitch += MathF.Atan(FlatFire.Drop(ball.DragFactor, ball.Gravity, _sim.Config.Shot.MuzzleVelocity, distance) / distance);

            // The aim error wanders (a bounded random walk), so some shots miss and some don't.
            float maxError = _def.AimError_deg * Units.DegreesToRadians;
            _errorYaw = Math.Clamp(_errorYaw + _rng.Symmetric(maxError * 0.6f), -maxError, maxError);
            _errorPitch = Math.Clamp(_errorPitch + _rng.Symmetric(maxError * 0.6f), -maxError, maxError);
            float turn = _def.TurnSpeed_degps * Units.DegreesToRadians * dt;
            _yaw = TurnTowards(_yaw, wantYaw + _errorYaw, turn);
            _pitch = TurnTowards(_pitch, wantPitch + _errorPitch, turn);

            bool onTarget = MathF.Abs(WrapAngle(wantYaw - _yaw)) < 2f * maxError + 0.02f;
            if (_seenFor >= _def.ReactionTime_s && _sincePull >= _def.PullInterval_s && onTarget)
            {
                buttons |= InputButtons.Fire;
                _sincePull = 0f;
            }
        }
        else
        {
            _seenFor = 0f;
            _yaw = TurnTowards(_yaw, _restYaw, 1.5f * dt);
            _pitch = TurnTowards(_pitch, 0f, 1.5f * dt);
        }

        if (self.Marker.Paint.Loader == 0 && !self.Marker.Refill.Active)
        {
            buttons |= InputButtons.Refill;
        }

        return new InputCommand { Tick = tick, Yaw = _yaw, Pitch = _pitch, Buttons = buttons };
    }

    private InputCommand WalkOff(int tick, PlayerState self, float dt)
    {
        _outFor += dt;
        SVector3 to = _exit - self.Position;
        to.Y = 0f;
        if (_outFor >= _def.WalkOffTime_s || to.Length() < 1.5f)
        {
            self.Present = false;
            return new InputCommand { Tick = tick, Yaw = self.Yaw };
        }

        float yaw = MathF.Atan2(-to.X, -to.Z);
        return new InputCommand { Tick = tick, Move = new SVector2(0f, 1f), Yaw = yaw, Pitch = 0f };
    }

    private bool CanSee(PlayerState self, out SVector3 aimPoint, out float distance)
    {
        aimPoint = _target.Position + new SVector3(0f, _target.EyeHeight * 0.72f, 0f);
        distance = SVector3.Distance(self.EyePosition, aimPoint);
        if (!_target.Alive || !_target.Present || distance > _def.SightRange_m || distance < 0.5f)
        {
            return false;
        }

        return !_sim.Collision.SweepSphere(self.EyePosition, aimPoint, 0f, out SweepHit _);
    }

    private static float TurnTowards(float current, float target, float maxStep)
    {
        float delta = WrapAngle(target - current);
        return MathF.Abs(delta) <= maxStep ? target : current + MathF.CopySign(maxStep, delta);
    }

    private static float WrapAngle(float a)
    {
        while (a > MathF.PI)
        {
            a -= MathF.Tau;
        }

        while (a < -MathF.PI)
        {
            a += MathF.Tau;
        }

        return a;
    }
}
