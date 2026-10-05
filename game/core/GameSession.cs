namespace Pb.Game.Core;

/// <summary>
/// What the menus chose, carried across scene changes: the level, mode, size, objective and difficulty tier to play.
/// Empty when a scene is run directly (the level then takes its options from the command line, or picks
/// its first playable level, the first mode at its default size, and "normal").
/// </summary>
public static class GameSession
{
    public static string? LevelId { get; set; }

    public static string? TierId { get; set; }

    public static string? ModeId { get; set; }

    /// <summary>The size for <see cref="ModeId"/>: opponents (solo), players (free-for-all) or players a side (teams).</summary>
    public static int? Size { get; set; }

    /// <summary>"eliminate", "retrieve" or "hold" (solo and teams; free-for-all is always eliminate).</summary>
    public static string? ObjectiveId { get; set; }

    /// <summary>The menu to open on returning to the main scene ("levels" after a round).</summary>
    public static string? ReturnTo { get; set; }

    public const string MainScene = "res://scenes/Main.tscn";

    public const string LevelScene = "res://scenes/Level.tscn";

    public const string RangeScene = "res://scenes/Range.tscn";
}
