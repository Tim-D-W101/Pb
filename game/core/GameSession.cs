namespace Pb.Game.Core;

/// <summary>
/// What the menus chose, carried across scene changes: the level and difficulty tier to play.
/// Empty when a scene is run directly (the level then picks its first playable level and tier).
/// </summary>
public static class GameSession
{
    public static string? LevelId { get; set; }

    public static string? TierId { get; set; }

    /// <summary>The menu to open on returning to the main scene ("levels" after a round).</summary>
    public static string? ReturnTo { get; set; }

    public const string MainScene = "res://scenes/Main.tscn";

    public const string LevelScene = "res://scenes/Level.tscn";

    public const string RangeScene = "res://scenes/Range.tscn";
}
