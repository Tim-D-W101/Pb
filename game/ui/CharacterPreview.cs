using Godot;
using Pb.Game.Core;
using Pb.Game.Player;
using Pb.Sim;
using Pb.Sim.Collision;
using Pb.Sim.Data;
using Pb.Sim.Players;

namespace Pb.Game.Ui;

/// <summary>
/// The character you'll play, turning slowly in your side's colour, in a little world of its own (the lobby): the
/// round's own character drawing, on a tiny sim with only the ground and you in it.
/// </summary>
public partial class CharacterPreview : SubViewportContainer
{
    private SubViewport _viewport = null!;
    private Node3D _stage = null!;
    private SimWorld _sim = null!;
    private PlayerState _state = null!;
    private PresentationDef _view = null!;
    private CharacterVisual? _visual;
    private (int Look, Color Jersey) _shown = (-1, Colors.Black);

    /// <summary>How fast it turns (rad/s).</summary>
    public float TurnSpeed { get; set; } = 0.6f;

    public void Build(SimConfig config, PresentationDef view, Vector2 size)
    {
        _view = view;
        Stretch = true;
        CustomMinimumSize = size;
        MouseFilter = MouseFilterEnum.Ignore;
        _viewport = new SubViewport
        {
            Name = "Preview", OwnWorld3D = true, TransparentBg = true, Size = new Vector2I((int)size.X, (int)size.Y), Msaa3D = Viewport.Msaa.Msaa4X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(_viewport);
        _stage = new Node3D { Name = "Stage" };
        _viewport.AddChild(_stage);
        var camera = new Camera3D { Fov = 34f, Current = true };
        _stage.AddChild(camera);
        camera.LookAtFromPosition(new Vector3(0f, 1.25f, 3.6f), new Vector3(0f, 0.92f, 0f), Vector3.Up);
        var sun = new DirectionalLight3D { LightEnergy = 1.3f, ShadowEnabled = false };
        _stage.AddChild(sun);
        sun.LookAtFromPosition(new Vector3(2f, 4f, 3f), Vector3.Zero, Vector3.Up);
        var fill = new OmniLight3D { Position = new Vector3(-2f, 1.6f, 2f), LightEnergy = 0.6f, OmniRange = 8f };
        _stage.AddChild(fill);
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.ClearColor, AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.55f, 0.57f, 0.6f), AmbientLightEnergy = 0.8f,
        };
        _stage.AddChild(new WorldEnvironment { Environment = environment });

        _sim = new SimWorld(config);
        _sim.Collision.Add(new PlaneShape(System.Numerics.Vector3.UnitY, 0f), config.Surfaces.Get("concrete"), "ground");
        _state = _sim.AddPlayer(0, 0, System.Numerics.Vector3.Zero, 0.6f);
    }

    /// <summary>Shows character <paramref name="look"/> in <paramref name="jersey"/> (built again only when either changes).</summary>
    public void Show(int look, Color jersey)
    {
        if (_shown == (look, jersey))
        {
            return;
        }

        _shown = (look, jersey);
        _visual?.QueueFree();
        _visual = new CharacterVisual { Name = "Character" };
        _stage.AddChild(_visual);
        _visual.Build(_sim, _state, jersey, _view.Characters, look, _view.MarkerModel);
    }

    public override void _Process(double delta)
    {
        if (_visual is null)
        {
            return;
        }

        _state.Yaw = Mathf.Wrap(_state.Yaw + TurnSpeed * (float)delta, -Mathf.Pi, Mathf.Pi);
        _visual.Capture();
    }
}
