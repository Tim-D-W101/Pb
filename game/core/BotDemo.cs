using System.Collections.Generic;
using System.Linq;
using Godot;
using Pb.Game.Ai;
using Pb.Game.Player;
using Pb.Game.Ui;
using Pb.Sim;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Players;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --bot-demo</c>: watch the bots fight, to check their behaviour by eye or capture it with
/// <c>--write-movie</c>. Hitboxes are off, so nobody goes out. You stand in the yard firing just wide of
/// the nearest bot you can see; they spot you, take cover, peek and shoot back. A camera above and
/// behind you shows it with the F3 overlay (sight cones, meters, paths, cover points), then the view cuts
/// to your own eyes. Frames are counted, so the result is the same at any rendering speed.
/// </summary>
public partial class BotDemo : Node, ICommandSource
{
    private const int OverheadFrames = 240;
    private const int TotalFrames = 360;
    private const int PullEvery = 70;
    private const float Miss = 1.4f;

    private SimWorld _sim = null!;
    private PlayerController _player = null!;
    private IReadOnlyList<OpponentPawn> _opponents = null!;
    private BotDebugOverlay _overlay = null!;
    private Hud _hud = null!;
    private Camera3D _camera = null!;
    private int _frame = -1;
    private int _start = -1;

    public void Start(SimWorld sim, PlayerController player, IReadOnlyList<OpponentPawn> opponents, BotDebugOverlay overlay, Hud hud, float farClip)
    {
        _sim = sim;
        _player = player;
        _opponents = opponents;
        _overlay = overlay;
        _hud = hud;
        sim.PlayerHits.Enabled = false;

        // Stand in the open in front of the yard's east sentry (or whoever has room in front).
        OpponentPawn? anchor = opponents.FirstOrDefault(o => o.Name == "Opponent_yard_east") ?? opponents.FirstOrDefault();
        if (anchor is not null && ScenePositions.FindSpot(sim, anchor.State, 18f, out SVector3 spot))
        {
            player.Teleport(spot, ScenePositions.Facing(spot, anchor.State.Position));
        }

        player.AutoPilot = this;
        // Above and behind your shoulder, looking past you at the bots you're facing.
        SVector3 you = player.State.Position;
        SVector3 look = anchor is null ? you + Pb.Sim.Core.ViewAngles.FlatForward(player.State.Yaw) * 10f : (you + anchor.State.Position) * 0.5f;
        SVector3 back = SVector3.Normalize((you - look) with { Y = 0f });
        SVector3 side = new(back.Z, 0f, -back.X);
        _camera = new Camera3D { Name = "DemoCamera", Fov = 60f, Far = farClip, Near = 0.05f };
        AddChild(_camera);
        _camera.GlobalPosition = (you + back * 7f + side * 3f + new SVector3(0f, 6f, 0f)).ToGodot();
        _camera.LookAt(look.ToGodot(), Vector3.Up);
        _camera.MakeCurrent();
        overlay.Visible = true;
        hud.Visible = false;
        player.ViewModel.Visible = false;
        _frame = 0;
        GD.Print($"BOT DEMO {opponents.Count} bots; overhead for {OverheadFrames} frames, then your view");
    }

    public override void _Process(double delta)
    {
        if (_frame < 0)
        {
            return;
        }

        if (_frame == OverheadFrames)
        {
            // Cut to your own eyes, overlay off.
            _overlay.Visible = false;
            _hud.Visible = true;
            _hud.ShowPerf = false;
            _player.ViewModel.Visible = true;
            _player.Camera.MakeCurrent();
            GD.Print($"BOT DEMO frame {_frame}: your view");
        }

        if (_frame % 60 == 0)
        {
            string states = string.Join(", ", _opponents.Where(o => o.Pilot is BotPilot).Select(o => $"{o.State.Name}: {((BotPilot)o.Pilot).Brain.Label}"));
            GD.Print($"BOT DEMO frame {_frame}: {states}");
        }

        if (++_frame >= TotalFrames)
        {
            GetTree().Quit();
        }
    }

    /// <summary>Your script: fire just wide of the nearest bot in sight now and then, else face the nearest.</summary>
    public InputCommand Next(int tick, PlayerState me)
    {
        if (_start < 0)
        {
            _start = tick;
        }

        PlayerState? target = null;
        PlayerState? nearest = null;
        float best = float.MaxValue;
        float bestAny = float.MaxValue;
        foreach (OpponentPawn o in _opponents)
        {
            PlayerState s = o.State;
            if (!s.Present)
            {
                continue;
            }

            float d = SVector3.Distance(me.Position, s.Position);
            SVector3 chest = s.Position + new SVector3(0f, s.EyeHeight * 0.72f, 0f);
            if (d < bestAny)
            {
                bestAny = d;
                nearest = s;
            }

            if (d < best && !_sim.Collision.SweepSphere(me.EyePosition, chest, 0f, out SweepHit _))
            {
                best = d;
                target = s;
            }
        }

        PlayerState? aimAt = target ?? nearest;
        if (aimAt is null)
        {
            return new InputCommand { Tick = tick, Yaw = me.Yaw };
        }

        SVector3 point = aimAt.Position + new SVector3(0f, aimAt.EyeHeight * 0.72f, 0f);
        SVector3 across = SVector3.Normalize(SVector3.Cross(point - me.EyePosition, SVector3.UnitY));
        (float yaw, float pitch) = BotAim.Solve(_sim.Config, me.EyePosition, point + across * Miss, SVector3.Zero);
        bool pull = target is not null && (tick - _start) % PullEvery == 0 && tick - _start > 60;
        return new InputCommand { Tick = tick, Yaw = yaw, Pitch = pitch, Buttons = pull ? InputButtons.Fire : InputButtons.None };
    }
}
