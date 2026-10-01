using Godot;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Players;

namespace Pb.Game.Player;

/// <summary>
/// An opponent on the field: a <see cref="PawnBody"/> driven by a pilot (a practice dummy now, a bot
/// from M2.5), drawn by a <see cref="CharacterVisual"/>. When hit it calls "Hit!" and, once it has
/// walked off, stops colliding.
/// </summary>
public partial class OpponentPawn : PawnBody, IPlayerDriver
{
    private ICommandSource _pilot = null!;
    private Label3D _callout = null!;
    private float _calloutLeft;
    private bool _removed;

    public CharacterVisual Visual { get; private set; } = null!;

    public ICommandSource Pilot => _pilot;

    public void Initialize(SimWorld sim, PlayerState state, Color jersey, ICommandSource pilot)
    {
        InitializeBody(sim, state);
        _pilot = pilot;
        Visual = new CharacterVisual { Name = "Visual" };
        AddChild(Visual);
        Visual.Build(sim, state, jersey);
        _callout = new Label3D
        {
            Name = "Callout",
            Text = "HIT!",
            FontSize = 96,
            PixelSize = 0.004f,
            OutlineSize = 18,
            Modulate = new Color(1f, 0.95f, 0.85f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Visible = false,
            TopLevel = true,
        };
        AddChild(_callout);
    }

    /// <summary>Shows the "Hit!" call above the head for a couple of seconds.</summary>
    public void CallHit()
    {
        _callout.Visible = true;
        _calloutLeft = 2f;
    }

    public InputCommand Step(int tick, float dt)
    {
        InputCommand cmd = new() { Tick = tick, Yaw = State.Yaw };
        if (State.Present)
        {
            cmd = _pilot.Next(tick, State);
            ApplyCommand(cmd, dt);
        }
        else if (!_removed)
        {
            // Walked off: out of everyone's way.
            _removed = true;
            CollisionLayer = 0;
            CollisionMask = 0;
        }

        Visual.Capture();
        return cmd;
    }

    public override void _Process(double delta)
    {
        if (_calloutLeft > 0f)
        {
            _calloutLeft -= (float)delta;
            _callout.Visible = _calloutLeft > 0f && State.Present;
            _callout.GlobalPosition = (State.Position + new System.Numerics.Vector3(0f, State.EyeHeight + 0.75f, 0f)).ToGodot();
        }
    }
}
