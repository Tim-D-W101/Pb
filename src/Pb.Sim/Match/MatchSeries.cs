namespace Pb.Sim.Match;

/// <summary>
/// A speedball match: points raced to <see cref="RaceTo"/>, each won by side 0 or side 1, or by nobody (level at time up,
/// or both sides out together). Engine-free; offline the game keeps it across the level's reloads between points, and
/// online the lobby keeps it between rounds.
/// </summary>
public sealed class MatchSeries
{
    private readonly int[] _points = new int[2];

    public MatchSeries(int raceTo)
    {
        RaceTo = Math.Max(1, raceTo);
    }

    public int RaceTo { get; }

    /// <summary>Points played so far, those nobody won included.</summary>
    public int Played { get; private set; }

    /// <summary>The side that won the match, or −1 while it's still on.</summary>
    public int Winner { get; private set; } = -1;

    public bool Done => Winner >= 0;

    /// <summary>Side <paramref name="side"/>'s points.</summary>
    public int PointsOf(int side) => side is 0 or 1 ? _points[side] : 0;

    /// <summary>A point's result: its winner (−1 for nobody) scores, and reaching the target wins the match.</summary>
    public void Add(MatchResult result) => Add(result.Winner);

    /// <summary>A point won by side <paramref name="winner"/> (−1 for nobody).</summary>
    public void Add(int winner)
    {
        if (Done)
        {
            return;
        }

        Played++;
        if (winner is 0 or 1 && ++_points[winner] >= RaceTo)
        {
            Winner = winner;
        }
    }

    /// <summary>Puts back a match as it stood (a reload, or the lobby's word for it).</summary>
    public void Restore(int side0, int side1, int played)
    {
        _points[0] = side0;
        _points[1] = side1;
        Played = played;
        Winner = side0 >= RaceTo ? 0 : side1 >= RaceTo ? 1 : -1;
    }
}
