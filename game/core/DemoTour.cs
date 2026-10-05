using Godot;
using Pb.Game.Ballistics;
using Pb.Game.Player;
using Pb.Game.Ui;
using Pb.Sim;
using Pb.Sim.Core;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// Scripted tour (<c>-- --demo</c>) for capturing screenshots with Godot's Movie Maker
/// (<c>--write-movie out.png --fixed-fps 30</c>): target practice with the arc preview, then
/// stress mode with 1,000 balls, then sustained fire on props to show splats and bounces.
/// </summary>
public sealed class DemoTour
{
    private readonly SimWorld _sim;
    private readonly ArcPreview _arc;
    private readonly Hud _hud;
    private readonly PlayerController _player;

    public DemoTour(SimWorld sim, PlayerController player, ArcPreview arc, Hud hud)
    {
        _sim = sim;
        _player = player;
        _arc = arc;
        _hud = hud;
        Pilot = new AutoPilot(sim, secondsPerTarget: 1.2f) { AimOverride = Aim };
        _arc.Enabled = true;
        hud.ShowHelp = false;
        player.GetNode<SimDriver>("../SimDriver").Ticked += OnTick;
    }

    public AutoPilot Pilot { get; }

    private double Time => _sim.Tick * (double)_sim.Dt;

    private void OnTick(int tick)
    {
        double t = Time;
        _sim.Stress!.Enabled = t >= 6.0 && t < 11.0;
        _arc.Enabled = t < 6.0 || t >= 14.0;
        Pilot.HoldFire = t >= 6.0 && t < 8.0;
        if (tick == (int)(6.0 / _sim.Dt))
        {
            _hud.Toast("Stress mode: 1000 live balls");
        }
    }

    private (float Yaw, float Pitch)? Aim(int tick)
    {
        double t = tick * (double)_sim.Dt;
        if (t >= 6.0 && t < 11.0)
        {
            // Slow pan across the range while the cannons fill the air.
            float yaw = (float)(0.35 * System.Math.Sin((t - 6.0) * 0.6));
            return (yaw, 0.06f);
        }

        if (t >= 11.0 && t < 14.0)
        {
            return AimAt(new SVector3(-8f, 0.9f, -15f)); // the blue can: splats and bounces
        }

        return null;
    }

    private (float, float) AimAt(SVector3 point) => ViewAngles.FromDirection(point - _player.State.EyePosition);
}
