using Pb.Net.Protocol;
using Pb.Sim.AI;
using Pb.Sim.Events;

namespace Pb.Net.Server;

/// <summary>
/// Hosting (from the game or the dedicated server): the bots' contacts, shouted aloud or not, go to every copy, so each
/// marks its own player's teammates' calls on its HUD as the host does.
/// </summary>
public static class ContactRelay
{
    /// <summary>
    /// Sends the contacts the bots in <paramref name="bots"/> shared since contact number <paramref name="sent"/> (a
    /// person's callout key travels as its own event) and returns how many have been shared in all.
    /// </summary>
    public static int Send(BotSquad squad, IReadOnlyList<BotBrain> bots, int sent, NetServer server, int tick)
    {
        for (int n = Math.Max(sent, squad.ContactsShared - BotSquad.RecentContacts); n < squad.ContactsShared; n++)
        {
            if (!squad.RecentContact(n, out BotSquad.Contact c) || !IsBot(bots, c.From))
            {
                continue;
            }

            server.AddEvent(new SimEvent
            {
                Type = NetEventTypes.Contact, Tick = tick, PlayerId = c.From, Team = c.Team, TargetId = c.TargetId, Position = c.At,
                ColliderId = -1,
            });
        }

        return squad.ContactsShared;
    }

    private static bool IsBot(IReadOnlyList<BotBrain> bots, int id)
    {
        for (int i = 0; i < bots.Count; i++)
        {
            if (bots[i].Self.Id == id)
            {
                return true;
            }
        }

        return false;
    }
}
