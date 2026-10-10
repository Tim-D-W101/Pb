namespace Pb.Game.Core;

/// <summary>
/// What the menus chose, carried across scene changes: the area (its level), the place in it, the mode,
/// size, objective and difficulty tier to play. Empty when a scene is run directly (the level then takes its
/// options from the command line, or picks the first area, the whole of it, the first mode at its default
/// size, and "normal").
/// </summary>
public static class GameSession
{
    public static string? LevelId { get; set; }

    /// <summary>Where in the area (its level's "places"); null for the first, the whole of it.</summary>
    public static string? PlaceId { get; set; }

    public static string? TierId { get; set; }

    public static string? ModeId { get; set; }

    /// <summary>The size for <see cref="ModeId"/>: opponents (solo), players (free-for-all) or players a side (teams).</summary>
    public static int? Size { get; set; }

    /// <summary>"eliminate", "retrieve" or "hold" (solo and teams; free-for-all is always eliminate).</summary>
    public static string? ObjectiveId { get; set; }

    /// <summary>A match of points under way offline (speedball, capture the flag), kept across the level's reloads between points.</summary>
    public static PointsMatch? Points { get; set; }

    /// <summary>The menu to open on returning to the main scene ("levels" after a round).</summary>
    public static string? ReturnTo { get; set; }

    /// <summary>Something to tell you back at the menu (why a game with others ended).</summary>
    public static string? Notice { get; set; }

    public const string MainScene = "res://scenes/Main.tscn";

    public const string LevelScene = "res://scenes/Level.tscn";

    public const string RangeScene = "res://scenes/Range.tscn";

    /// <summary>Playing with others: who's in, between rounds.</summary>
    public const string LobbyScene = "res://scenes/Lobby.tscn";

    /// <summary>The dedicated server (-- --server).</summary>
    public const string ServerScene = "res://scenes/Server.tscn";
}
