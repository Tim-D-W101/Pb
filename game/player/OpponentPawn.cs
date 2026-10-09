using Godot;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Players;

namespace Pb.Game.Player;

/// <summary>
/// An opponent on the field: a <see cref="PawnBody"/> driven by a pilot (a bot's brain, through
/// <c>BotPilot</c>), drawn by a <see cref="CharacterVisual"/>. When hit it calls "Hit!" and, once it has
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

    /// <summary>
    /// On a joining copy: someone posed from the server's snapshots, not moved by the rules here. The body only follows
    /// them (so you walk into them where they're drawn).
    /// </summary>
    public bool Puppet { get; set; }

    /// <param name="characters">How opponents look (models and tints); null draws the hitbox boxes.</param>
    /// <param name="look">Which model and tint this opponent gets, dealt in turn.</param>
    /// <param name="marker">The marker model in their hands; null draws the coded marker's shapes.</param>
    public void Initialize(SimWorld sim, PlayerState state, Color jersey, ICommandSource pilot, CharactersDef? characters = null, int look = 0,
        MarkerModelDef? marker = null)
    {
        InitializeBody(sim, state);
        _pilot = pilot;
        Visual = new CharacterVisual { Name = "Visual" };
        AddChild(Visual);
        Visual.Build(sim, state, jersey, characters, look, marker);
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

    /// <summary>Hands the opponent to another pilot (a scripted scene's).</summary>
    public void Steer(ICommandSource pilot) => _pilot = pilot;

    /// <summary>Shows the "Hit!" call above the head for a couple of seconds.</summary>
    public void CallHit()
    {
        _callout.Visible = true;
        _calloutLeft = 2f;
    }

    public InputCommand Step(int tick, float dt)
    {
        InputCommand cmd = new() { Tick = tick, Yaw = State.Yaw };
        if (Puppet)
        {
            if (State.Present)
            {
                FollowState();
            }
            else if (!_removed)
            {
                _removed = true;
                CollisionLayer = 0;
                CollisionMask = 0;
            }

            Visual.Capture();
            return cmd;
        }

        if (State.Present)
        {
            cmd = _pilot.Next(tick, State);
            ApplyCommand(cmd, dt);
        }
        else
        {
            if (!_removed)
            {
                // Walked off: out of everyone's way.
                _removed = true;
                CollisionLayer = 0;
                CollisionMask = 0;
            }

            if (_pilot.EveryTick)
            {
                _pilot.Next(tick, State);
            }
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
