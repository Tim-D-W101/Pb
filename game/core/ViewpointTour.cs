using System.Collections.Generic;
using System.Globalization;
using Godot;
using Pb.Game.Ui;
using Pb.Sim.Level;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --shots</c>: shows each of the level's (or the training ground's) viewpoints in turn through a free camera, then quits.
/// Run with Godot's <c>--write-movie</c> and <c>--fixed-fps</c> to capture screenshots; frames are
/// counted, so the result is the same at any rendering speed. Without <c>--write-movie</c>, each view's
/// average frame time is printed too (after a few frames to settle), for comparing presets with
/// <c>--preset</c>. <c>--views=…</c> replaces the level's viewpoints with your own, separated by
/// <c>;</c>: each <c>x,y,z,yaw,pitch</c> (metres, degrees) or <c>x,y,z&gt;tx,ty,tz</c> (from a point,
/// looking at another), for close-ups of anything in the level.
/// </summary>
public partial class ViewpointTour : Node
{
    /// <summary>Frames each viewpoint is held (the first few let probes settle); <c>--shots=N</c> overrides.</summary>
    private int _framesPerView = 40;

    private Camera3D _camera = null!;
    private IReadOnlyList<Viewpoint>? _views;
    private int _frame;
    private ulong _lastUsec;
    private double _viewUsec;
    private int _viewFrames;
    private const int SettleFrames = 3;

    public void Start(IReadOnlyList<Viewpoint> viewpoints, Hud hud, Node3D viewModel, float farClip)
    {
        _views = Args.Value("--views") is { } custom ? ParseViews(custom) : viewpoints;
        _framesPerView = Args.Ticks("--shots", 40) ?? 40;
        hud.Visible = false;
        viewModel.Visible = false; // drawn with its own projection, so it would show through any camera
        _camera = new Camera3D { Name = "TourCamera", Fov = 62f, Far = farClip, Near = 0.05f };
        AddChild(_camera);
        _camera.MakeCurrent();
        GD.Print($"TOUR {_views.Count} viewpoints × {_framesPerView} frames");
    }

    public override void _Process(double delta)
    {
        if (_views is null)
        {
            return;
        }

        int index = _frame / _framesPerView;
        ulong now = Time.GetTicksUsec();
        if (_frame % _framesPerView >= SettleFrames + 1)
        {
            _viewUsec += now - _lastUsec;
            _viewFrames++;
        }

        _lastUsec = now;
        if (_frame > 0 && _frame % _framesPerView == 0 && _viewFrames > 0)
        {
            GD.Print($"TOUR view {index - 1}: {_viewUsec / _viewFrames / 1000.0:0.0} ms per frame");
            _viewUsec = 0;
            _viewFrames = 0;
        }

        if (index >= _views.Count)
        {
            GetTree().Quit();
            return;
        }

        if (_frame % _framesPerView == 0)
        {
            Viewpoint vp = _views[index];
            _camera.GlobalPosition = vp.Position.ToGodot();
            _camera.Rotation = new Vector3(vp.Pitch, vp.Yaw, 0f);
            GD.Print($"TOUR view {index}: {vp.Name} (frames {_frame}–{_frame + _framesPerView - 1})");
        }

        _frame++;
    }

    /// <summary>Viewpoints from <c>--views</c>: <c>x,y,z,yaw,pitch</c> or <c>x,y,z&gt;tx,ty,tz</c>, separated by <c>;</c>.</summary>
    private static List<Viewpoint> ParseViews(string text)
    {
        var views = new List<Viewpoint>();
        foreach (string item in text.Split(';', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
        {
            string[] halves = item.Split('>');
            float[] from = Numbers(halves[0]);
            if (from.Length < 3)
            {
                GD.PushWarning($"TOUR view '{item}' needs at least x,y,z; skipped");
                continue;
            }

            var position = new System.Numerics.Vector3(from[0], from[1], from[2]);
            float yaw, pitch;
            if (halves.Length > 1 && Numbers(halves[1]) is { Length: >= 3 } to)
            {
                // Yaw 0 faces −Z and positive yaw turns left; positive pitch looks up.
                var d = new Vector3(to[0] - from[0], to[1] - from[1], to[2] - from[2]);
                yaw = Mathf.Atan2(-d.X, -d.Z);
                pitch = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length());
            }
            else
            {
                yaw = Mathf.DegToRad(from.Length > 3 ? from[3] : 0f);
                pitch = Mathf.DegToRad(from.Length > 4 ? from[4] : 0f);
            }

            views.Add(new Viewpoint($"view {views.Count}", position, yaw, pitch));
        }

        return views;
    }

    private static float[] Numbers(string csv) =>
        System.Array.ConvertAll(csv.Split(',', System.StringSplitOptions.TrimEntries), v => float.Parse(v, CultureInfo.InvariantCulture));
}
