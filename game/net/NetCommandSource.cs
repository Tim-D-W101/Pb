using Pb.Game.Core;
using Pb.Net.Server;
using Pb.Sim.Players;

namespace Pb.Game.Net;

/// <summary>
/// On the host: a remote player's commands, as they come from their copy (standing still until they do). Once they're out,
/// <see cref="WalkOff"/> walks them off the field the way the bots go (their copy shows them where the host has them).
/// </summary>
public sealed class NetCommandSource : ICommandSource
{
    private readonly NetServer _server;

    public NetCommandSource(NetServer server)
    {
        _server = server;
    }

    public ICommandSource? WalkOff { get; set; }

    public bool EveryTick => true;

    public InputCommand Next(int tick, PlayerState state)
    {
        // Their commands are taken in turn even once they're out, so their queue doesn't fill up.
        bool sent = _server.TryCommand(state.Id, tick, out InputCommand command);
        if (!state.Alive && WalkOff is { } walk)
        {
            return walk.Next(tick, state);
        }

        return sent ? command : new InputCommand { Tick = tick, Yaw = state.Yaw, Pitch = state.Pitch };
    }
}
