using Godot;

namespace Pb.Game.Core;

/// <summary>
/// Loads art from the import pipeline (<c>game/art</c>). A missing file gives null and the caller falls
/// back (materials to their procedural look, props to greybox, opponents to their hitbox boxes), so the
/// game never depends on the art. <c>-- --no-art</c> ignores all of it; CI's bot match runs that way to
/// keep the fallbacks working.
/// </summary>
public static class ArtFiles
{
    public static bool Disabled { get; } = Args.Has("--no-art");

    public static T? Load<T>(string? path) where T : Resource =>
        !Disabled && !string.IsNullOrWhiteSpace(path) && ResourceLoader.Exists(path) ? GD.Load<T>(path) : null;
}
