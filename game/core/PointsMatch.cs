using Pb.Sim.Match;

namespace Pb.Game.Core;

/// <summary>
/// A match of points (speedball, capture the flag) played offline, kept in <see cref="GameSession"/> across the level's
/// reloads between its points:
/// which round it is (another choice starts a new match), the points so far, and your numbers over them all, for the
/// records once it's won or lost. Playing with others, each point's setup brings the score so far instead
/// (<see cref="Joined"/>), and the numbers are the point's alone.
/// </summary>
public sealed class PointsMatch
{
    public PointsMatch(string key, int raceTo)
    {
        Key = key;
        Series = new MatchSeries(raceTo);
    }

    /// <summary>A point of a match played with others, its score so far as its setup has it.</summary>
    public static PointsMatch Joined(int round, int raceTo, int points0, int points1, int played)
    {
        var match = new PointsMatch($"round {round}", raceTo) { Totals = false };
        match.Series.Restore(points0, points1, played);
        return match;
    }

    /// <summary>The numbers below cover the whole match (offline); false, only the point just played.</summary>
    public bool Totals { get; private init; } = true;

    /// <summary>The area, place, mode, size and difficulty it's played in.</summary>
    public string Key { get; }

    public MatchSeries Series { get; }

    /// <summary>The point being played (1 for the first).</summary>
    public int Point => Series.Played + 1;

    /// <summary>Your shots, hits and eliminations, and the time played, over the points so far.</summary>
    public int Shots { get; private set; }

    public int Hits { get; private set; }

    public int Eliminations { get; private set; }

    public float Time { get; private set; }

    /// <summary>The last point's result, for the screen between points.</summary>
    public MatchResult Last { get; private set; } = MatchResult.Undecided;

    /// <summary>A point's over: its result and what you did in it.</summary>
    public void Add(MatchResult result, PlayerStats you, float time)
    {
        Last = result;
        Series.Add(result);
        Shots += you.Shots;
        Hits += you.Hits;
        Eliminations += you.Eliminations;
        Time += time;
    }
}
