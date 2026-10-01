using System.Collections.Generic;
using System.Globalization;
using Godot;
using Pb.Game.Player;
using Pb.Sim;
using Pb.Sim.Core;
using Pb.Sim.Players;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --duel-demo</c>: a short scripted scene for checking eliminations by eye (or with
/// <c>--write-movie</c>). You stand in front of a passive bot and shoot them: they call "Hit!",
/// raise their marker, wear the splat and walk off. Then a ball from another opponent breaks on your
/// mask: paint sprays your goggles and the spectator view shows who got you. Prints the tick of each
/// step and quits. <c>--duel-distance=M</c> stands you M metres from the bot instead of six, for a close-up.
/// </summary>
public sealed class DuelDemo : ICommandSource
{
    private const int FireFrom = 40;
    private const int IncomingAfterOut = 150;
    private const int EndAfterIncoming = 420;

    private readonly Node _host;
    private readonly SimWorld _sim;
    private readonly IReadOnlyList<OpponentPawn> _opponents;
    private PlayerState? _target;
    private int _start = -1;
    private int _outAt = -1;
    private int _incomingAt = -1;

    public DuelDemo(Node host, SimWorld sim, IReadOnlyList<OpponentPawn> opponents)
    {
        _host = host;
        _sim = sim;
        _opponents = opponents;
    }

    /// <summary>Puts <paramref name="player"/> six metres (or <c>--duel-distance</c>) in front of a bot out in the open, facing them.</summary>
    public void Setup(PlayerController player)
    {
        float distance = float.TryParse(Args.Value("--duel-distance"), NumberStyles.Float, CultureInfo.InvariantCulture, out float d) ? d : 6f;
        foreach (OpponentPawn o in _opponents)
        {
            bool outdoors = _sim.Level?.AreaAt(o.State.Position) is not { Indoor: true };
            if (o.State.Alive && outdoors && ScenePositions.FindSpot(_sim, o.State, distance, out SVector3 spot))
            {
                _target = o.State;
                player.Teleport(spot, ScenePositions.Facing(spot, o.State.Position));
                return;
            }
        }
    }

    public InputCommand Next(int tick, PlayerState me)
    {
        if (_start < 0)
        {
            _start = tick;
            GD.Print($"DUEL target {_target?.Name ?? "none"}; firing from tick {FireFrom}");
        }

        int t = tick - _start;
        if (_target is null || t > 1500)
        {
            GD.Print(_target is null ? "DUEL no target out in the open" : "DUEL timed out");
            _host.GetTree().Quit();
            return default;
        }

        if (_outAt < 0 && !_target.Alive)
        {
            _outAt = t;
            GD.Print($"DUEL {_target.Name} out at tick {t}");
        }

        if (_outAt >= 0 && _incomingAt < 0 && t >= _outAt + IncomingAfterOut)
        {
            _incomingAt = t;
            Incoming(me);
            GD.Print($"DUEL incoming ball at tick {t}");
        }

        if (_incomingAt >= 0 && t >= _incomingAt + EndAfterIncoming)
        {
            _host.GetTree().Quit();
        }

        (float yaw, float pitch) = ScenePositions.AimAt(_sim, me, _target);
        bool pull = _target.Alive && t >= FireFrom && (t - FireFrom) % 24 < 2;
        return new InputCommand { Tick = tick, Yaw = yaw, Pitch = pitch, Buttons = pull ? InputButtons.Fire : InputButtons.None };
    }

    /// <summary>A ball from the nearest other opponent, five metres out, straight at your mask.</summary>
    private void Incoming(PlayerState me)
    {
        PlayerState? shooter = null;
        float best = float.MaxValue;
        foreach (OpponentPawn o in _opponents)
        {
            float d = SVector3.Distance(o.State.Position, me.Position);
            if (o.State.Alive && d < best)
            {
                best = d;
                shooter = o.State;
            }
        }

        // From the first direction (ahead, then the sides, then behind) with a clear line to the face.
        SVector3 mask = me.EyePosition + ViewAngles.Forward(me.Yaw, me.Pitch) * 0.06f;
        SVector3 from = mask;
        foreach (float turn in new[] { 0.35f, -0.35f, 1.2f, -1.2f, 2.6f })
        {
            from = me.EyePosition + ViewAngles.FlatForward(me.Yaw + turn) * 5f + new SVector3(0f, 0.1f, 0f);
            if (!_sim.Collision.SweepSphere(from, mask, 0.01f, out _))
            {
                break;
            }
        }

        SVector3 velocity = SVector3.Normalize(mask - from) * _sim.Config.Shot.MuzzleVelocity;
        _sim.Ballistics.Spawn(from, velocity, shooter?.Id ?? -1, 9999, 1, new Pcg32(77), _sim.Dt, _sim.Tick, _sim.Events);
    }
}
