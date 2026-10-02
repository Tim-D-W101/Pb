using Pb.Game.Core;
using Pb.Sim.AI;
using Pb.Sim.Players;

namespace Pb.Game.Ai;

/// <summary>Drives an opponent's body with a bot brain: the brain's command each tick, like a human's input.</summary>
public sealed class BotPilot : ICommandSource
{
    public BotPilot(BotBrain brain)
    {
        Brain = brain;
    }

    public BotBrain Brain { get; }

    public InputCommand Next(int tick, PlayerState state) => Brain.Think(tick);
}
