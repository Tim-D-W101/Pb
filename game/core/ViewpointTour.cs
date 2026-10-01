using Godot;
using Pb.Game.Ui;
using Pb.Sim.Level;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --shots</c>: shows each of the level's viewpoints in turn through a free camera, then quits.
/// Run with Godot's <c>--write-movie</c> and <c>--fixed-fps</c> to capture screenshots; frames are
/// counted, so the result is the same at any rendering speed.
/// </summary>
public partial class ViewpointTour : Node
{
    /// <summary>Frames each viewpoint is held (the first few let probes settle); <c>--shots=N</c> overrides.</summary>
    private int _framesPerView = 40;

    private Camera3D _camera = null!;
    private LevelLayout _level = null!;
    private int _frame;

    public void Start(LevelLayout level, Hud hud, Node3D viewModel, float farClip)
    {
        _level = level;
        _framesPerView = Args.Ticks("--shots", 40) ?? 40;
        hud.Visible = false;
        viewModel.Visible = false; // drawn with its own projection, so it would show through any camera
        _camera = new Camera3D { Name = "TourCamera", Fov = 62f, Far = farClip, Near = 0.05f };
        AddChild(_camera);
        _camera.MakeCurrent();
        GD.Print($"TOUR {level.Viewpoints.Count} viewpoints × {_framesPerView} frames");
    }

    public override void _Process(double delta)
    {
        if (_level is null)
        {
            return;
        }

        int index = _frame / _framesPerView;
        if (index >= _level.Viewpoints.Count)
        {
            GetTree().Quit();
            return;
        }

        if (_frame % _framesPerView == 0)
        {
            Viewpoint vp = _level.Viewpoints[index];
            _camera.GlobalPosition = vp.Position.ToGodot();
            _camera.Rotation = new Vector3(vp.Pitch, vp.Yaw, 0f);
            GD.Print($"TOUR view {index}: {vp.Name} (frames {_frame}–{_frame + _framesPerView - 1})");
        }

        _frame++;
    }
}
