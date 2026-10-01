using Pb.Sim.Data;
using Pb.Sim.Match;

namespace Pb.Game.Ui;

/// <summary>How modes, sizes and placings read in menus and round screens.</summary>
public static class ModeText
{
    /// <summary>"6 opponents", "8 players" or "3 v 3".</summary>
    public static string Size(GameMode mode, int size) => mode.Kind switch
    {
        MatchModeKind.FreeForAll => $"{size} players",
        MatchModeKind.Teams => $"{size} v {size}",
        _ => size == 1 ? "1 opponent" : $"{size} opponents",
    };

    /// <summary>"1st", "2nd", "3rd", "4th" … "11th", "12th", "13th", "21st".</summary>
    public static string Ordinal(int n) =>
        n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
}
