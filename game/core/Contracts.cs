using Pb.Sim.Events;
using Pb.Sim.Players;

namespace Pb.Game.Core;

/// <summary>Presentation systems that react to sim events (splats, audio, HUD, FX…).</summary>
public interface ISimEventListener
{
    void OnSimEvent(in SimEvent e);
}

/// <summary>
/// Something that controls one sim player each tick: samples a command and performs the
/// movement (collide-and-slide) before the sim step.
/// </summary>
public interface IPlayerDriver
{
    PlayerState State { get; }

    InputCommand Step(int tick, float dt);
}

/// <summary>Scripted input used instead of devices (smoke test, demo, and later bots).</summary>
public interface ICommandSource
{
    InputCommand Next(int tick, PlayerState state);

    /// <summary>
    /// Asked every tick, even once its player has left the field: it takes a remote player's commands in turn, and
    /// they'd pile up unread otherwise.
    /// </summary>
    bool EveryTick => false;
}
