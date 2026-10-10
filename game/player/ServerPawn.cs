using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Players;

namespace Pb.Game.Player;

/// <summary>
/// The dedicated server's body for someone in the round: moved by their pilot (a bot's brain, or a person's commands
/// as their copy sends them) through Godot's walking collision, with nothing drawn. Off the field, it's out of
/// everyone's way.
/// </summary>
public partial class ServerPawn : PawnBody, IPlayerDriver
{
    private ICommandSource _pilot = null!;
    private bool _removed;

    public ICommandSource Pilot => _pilot;

    public void Initialize(SimWorld sim, PlayerState state, ICommandSource pilot)
    {
        InitializeBody(sim, state);
        _pilot = pilot;
    }

    /// <summary>Hands the body to another pilot.</summary>
    public void Steer(ICommandSource pilot) => _pilot = pilot;

    public InputCommand Step(int tick, float dt)
    {
        InputCommand cmd = new() { Tick = tick, Yaw = State.Yaw };
        if (State.Present)
        {
            cmd = _pilot.Next(tick, State);
            ApplyCommand(cmd, dt);
            return cmd;
        }

        if (!_removed)
        {
            _removed = true;
            CollisionLayer = 0;
            CollisionMask = 0;
        }

        if (_pilot.EveryTick)
        {
            _pilot.Next(tick, State);
        }

        return cmd;
    }
}
