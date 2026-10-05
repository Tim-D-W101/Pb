using Godot;
using Pb.Sim.Players;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --posture-demo</c>: a short scripted sequence at a wall corner for checking the shoulder
/// swap, the muzzle-in-cover rule and lean by eye (or with <c>--write-movie</c>). The player stands
/// just behind the south-west corner of the guardhouse in Oxbarrow Works, looking past its left
/// edge. A shot from the right shoulder breaks on the wall in front of the barrel; after a swap to
/// the left shoulder the same shot flies. Then it leans left round the corner and crouches. Prints
/// the tick of each step (and each shot's muzzle) and quits.
/// </summary>
public sealed class PostureDemo : ICommandSource
{
    private const int FireBlockedAt = 90;
    private const int SwapAt = 130;
    private const int FireClearAt = 200;
    private const int LeanAt = 250;
    private const int CrouchAt = 330;
    private const int EndAt = 420;

    /// <summary>Just south of the guardhouse's south-west corner.</summary>
    public static readonly SVector3 Position = new(-1.95f, 0f, 37.6f);

    /// <summary>Looking a little left of the corner, so the eye sees past it but a right-shoulder barrel doesn't.</summary>
    public const float Yaw = 0.42f;

    private readonly Node _host;
    private readonly Pb.Sim.SimWorld _sim;
    private int _start = -1;

    public PostureDemo(Node host, Pb.Sim.SimWorld sim)
    {
        _host = host;
        _sim = sim;
    }

    public InputCommand Next(int tick, PlayerState state)
    {
        if (_start < 0)
        {
            _start = tick;
            GD.Print($"POSTURE shot at {FireBlockedAt}, swap at {SwapAt}, shot at {FireClearAt}, lean at {LeanAt}, " +
                     $"crouch at {CrouchAt} (ticks; 4 per frame at 30 fps)");
        }

        int t = tick - _start;
        if (t is FireBlockedAt or FireClearAt)
        {
            Pb.Sim.ShotSolution s = _sim.SolveShot(state);
            GD.Print($"POSTURE t={t}: shoulder={state.Shoulder:0.00} eye={state.EyePosition} muzzle={s.Origin} blocked={s.MuzzleBlocked}");
        }

        if (t >= EndAt)
        {
            _host.GetTree().Quit();
        }

        InputButtons buttons = t switch
        {
            >= FireBlockedAt and < FireBlockedAt + 3 => InputButtons.Fire,
            >= SwapAt and < SwapAt + 3 => InputButtons.SwapShoulder,
            >= FireClearAt and < FireClearAt + 3 => InputButtons.Fire,
            >= LeanAt and < CrouchAt => InputButtons.LeanLeft,
            >= CrouchAt => InputButtons.Crouch,
            _ => InputButtons.None,
        };

        return new InputCommand { Tick = tick, Yaw = Yaw, Pitch = -0.02f, Buttons = buttons };
    }
}
