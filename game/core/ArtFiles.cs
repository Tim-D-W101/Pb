using Godot;

namespace Pb.Game.Core;

/// <summary>
/// Loads art from the import pipeline (<c>game/art</c>). A missing file gives null and the caller falls
/// back (materials to their procedural look, props to greybox, opponents to their hitbox boxes), so the
/// game never depends on the art. <c>-- --no-art</c> ignores all of it; CI's bot match runs that way to
/// keep the fallbacks working. Exported builds carry the art in its own pack beside the game
/// (<see cref="PackFile"/>, made by tools/package/windows-build.sh), so an update that changes only code
/// or data doesn't download the art again; it's mounted the first time anything asks for art.
/// </summary>
public static class ArtFiles
{
    public const string PackFile = "Pb-art.pck";

    private const string Folder = "res://art";

    static ArtFiles()
    {
        if (Disabled || !OS.HasFeature("template"))
        {
            return; // the editor and `godot --path game` read game/art directly
        }

        string pack = OS.GetExecutablePath().GetBaseDir().PathJoin(PackFile);
        if (!FileAccess.FileExists(pack))
        {
            GD.Print($"No art pack at {pack}: the procedural look is used");
        }
        else if (!ProjectSettings.LoadResourcePack(pack, replaceFiles: false))
        {
            GD.PushWarning($"Couldn't load the art pack {pack}: the procedural look is used");
        }
        else
        {
            // The pack's own project files (its UID list among them) don't replace the game's, so the
            // engine is told the art's UIDs here: models find their textures by them.
            GD.Print($"Art pack {pack}: {RegisterUids(Folder)} art files");
        }
    }

    public static bool Disabled { get; } = Args.Has("--no-art");

    public static T? Load<T>(string? path) where T : Resource =>
        !Disabled && !string.IsNullOrWhiteSpace(path) && ResourceLoader.Exists(path) ? GD.Load<T>(path) : null;

    /// <summary>
    /// Registers the UID of every imported file under <paramref name="folder"/>, read from its .import file
    /// (an exported build's importer doesn't report them); returns how many files there are.
    /// </summary>
    private static int RegisterUids(string folder)
    {
        int files = 0;
        foreach (string sub in DirAccess.GetDirectoriesAt(folder))
        {
            files += RegisterUids(folder.PathJoin(sub));
        }

        foreach (string file in DirAccess.GetFilesAt(folder))
        {
            if (!file.EndsWith(".import", System.StringComparison.Ordinal))
            {
                continue;
            }

            files++;
            string path = folder.PathJoin(file[..^".import".Length]);
            foreach (string line in FileAccess.GetFileAsString(folder.PathJoin(file)).Split('\n'))
            {
                // uid="uid://cotcdwesnkmlt"
                string entry = line.Trim();
                if (entry.StartsWith("uid=\"", System.StringComparison.Ordinal) && entry.EndsWith('"'))
                {
                    long uid = ResourceUid.TextToId(entry[5..^1]);
                    if (uid != ResourceUid.InvalidId && !ResourceUid.HasId(uid))
                    {
                        ResourceUid.AddId(uid, path);
                    }

                    break;
                }
            }
        }

        return files;
    }
}
