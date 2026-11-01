using Godot;
using Pb.Game.Player;
using Pb.Sim;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Players;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --splat-demo</c> (Phase 5): checks the splats' shapes by eye. It finds the nearest wall round your start, stands
/// you along it and fires a run of shots that hit it at a glance (they streak, along the ball's travel), then turns square
/// on and fires a few head-on (round), then holds the view on them. Prints what it did and quits.
/// </summary>
public sealed class SplatDemo : ICommandSource
{
    private const int GlanceFrom = 30, Glances = 12, SquareFrom = 220, Squares = 5, Every = 12, EndAt = 480;

    private readonly Node _host;
    private readonly SimWorld _sim;
    private SVector3 _wall;
    private SVector3 _normal;
    private SVector3 _along;
    private bool _found;
    private int _start = -1;

    public SplatDemo(Node host, SimWorld sim)
    {
        _host = host;
        _sim = sim;
    }

    /// <summary>Finds a wall at least 3 m tall within 12 m of <paramref name="player"/> and stands them 2.2 m off it, 4.5 m along it.</summary>
    public void Setup(PlayerController player)
    {
        SVector3 eye = player.State.EyePosition;
        float best = float.MaxValue;
        for (int i = 0; i < 72; i++)
        {
            SVector3 dir = ViewAngles.FlatForward(i * System.MathF.Tau / 72f);
            SVector3 high = eye + new SVector3(0f, 1.5f, 0f);
            if (_sim.Collision.SweepSphere(eye, eye + dir * 12f, 0f, out SweepHit hit) && System.MathF.Abs(hit.Normal.Y) < 0.2f && hit.T * 12f < best &&
                _sim.Collision.SweepSphere(high, high + dir * 12f, 0f, out SweepHit top) && System.MathF.Abs(top.T - hit.T) * 12f < 0.3f)
            {
                SVector3 normal = SVector3.Normalize(hit.Normal with { Y = 0f });
                SVector3 along = SVector3.Normalize(SVector3.Cross(SVector3.UnitY, normal));
                SVector3 spot = hit.Point + normal * 2.2f + along * 4.5f;
                spot.Y = player.State.Position.Y;
                if (ScenePositions.IsSpot(_sim, player.State, spot))
                {
                    best = hit.T * 12f;
                    _wall = hit.Point;
                    _normal = normal;
                    _along = along;
                    _found = true;
                }
            }
        }

        if (_found)
        {
            SVector3 at = _wall + _normal * 2.2f + _along * 4.5f;
            at.Y = player.State.Position.Y;
            player.Teleport(at, ScenePositions.Facing(at, _wall));
        }
    }

    public InputCommand Next(int tick, PlayerState me)
    {
        if (_start < 0)
        {
            _start = tick;
            GD.Print(_found ? $"SPLAT DEMO wall at {_wall} facing {_normal}; glancing shots from tick {GlanceFrom}, square ones from {SquareFrom}"
                : "SPLAT DEMO no wall near the start");
        }

        int t = tick - _start;
        if (!_found || t >= EndAt)
        {
            _host.GetTree().QuitCleanly();
            return default;
        }

        // At a glance: aimed along the wall at points walking back towards you; square on: at the wall in front.
        SVector3 target;
        bool fire;
        if (t < SquareFrom)
        {
            int k = System.Math.Clamp((t - GlanceFrom) / Every, 0, Glances - 1);
            target = _wall + _along * (0.8f - 0.25f * k) + new SVector3(0f, -0.35f + 0.15f * (k % 4), 0f);
            fire = t >= GlanceFrom && (t - GlanceFrom) % Every == 0 && (t - GlanceFrom) / Every < Glances;
        }
        else
        {
            int k = System.Math.Clamp((t - SquareFrom) / Every, 0, Squares - 1);
            target = _wall + _along * (4.5f + 0.35f * (k - 2)) + new SVector3(0f, -0.2f + 0.15f * (k % 2), 0f);
            fire = (t - SquareFrom) % Every == 0 && (t - SquareFrom) / Every < Squares;
        }

        // At the end, look at the glancing ones from where you stand.
        if (t > SquareFrom + Squares * Every + 30)
        {
            target = _wall + _along * 1.2f + new SVector3(0f, -0.3f, 0f);
            fire = false;
        }

        (float yaw, float pitch) = ViewAngles.FromDirection(target - me.EyePosition);
        return new InputCommand { Tick = tick, Yaw = yaw, Pitch = pitch, Buttons = fire ? InputButtons.Fire : InputButtons.None };
    }
}
