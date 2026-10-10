using Pb.Net.Protocol;
using Pb.Sim;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Pb.Sim.Match;

namespace Pb.Net;

/// <summary>
/// Builds a round's sim from its setup, the same on the server and on every joining copy: the level (or the part of it),
/// everyone in the roster's order at their starts, the match started (in its briefing). The same seed then deals the
/// same doors, case and room everywhere.
/// </summary>
public static class RoundWorld
{
    /// <param name="level">The round's level, already cut to its place; null for open ground (tests).</param>
    public static SimWorld Build(SimConfig config, RoundSetupMessage setup, LevelLayout? level, Action<SimWorld>? ground = null)
    {
        var sim = new SimWorld(config, setup.Seed);
        if (level is not null)
        {
            sim.LoadLevel(level);
        }
        else
        {
            ground?.Invoke(sim);
        }

        foreach (RosterEntry e in setup.Roster)
        {
            Pb.Sim.Players.PlayerState p = sim.AddPlayer(e.PlayerId, e.Team, e.Position, e.Yaw);
            p.Name = e.Name;
        }

        sim.StartMatch(MatchSetupOf(config, setup));
        return sim;
    }

    /// <summary>
    /// The match's setup a round's message describes. Its format (and a speedball point's countdown) comes with its mode:
    /// every copy has the same data.
    /// </summary>
    public static MatchSetup MatchSetupOf(SimConfig config, RoundSetupMessage setup)
    {
        GameMode? mode = config.Rules.FindMode(setup.ModeId);
        MatchFormat format = mode?.Format ?? MatchFormat.Round;
        return new MatchSetup
        {
            People = setup.Roster.Where(e => e.Person).Select(e => e.PlayerId).ToArray(),
            Mode = mode?.Kind ?? MatchModeKind.Solo,
            TimeLimit = setup.TimeLimit,
            StartPods = setup.StartPods,
            BotPods = setup.BotPods,
            Pickups = setup.Pickups,
            Objective = setup.Objective,
            Attackers = setup.Attackers,
            EndWhenPeopleOut = setup.EndWhenPeopleOut,
            Format = format,
            Countdown = config.Rules.PointsFor(format)?.Countdown ?? 0f,
        };
    }
}
