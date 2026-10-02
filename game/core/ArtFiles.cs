using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Pb.Game.Core;

/// <summary>
/// Loads art from the import pipeline (<c>game/art</c>). A missing file gives null and the caller falls
/// back (materials to their procedural look, props to greybox, opponents to their hitbox boxes), so the
/// game never depends on the art. <c>-- --no-art</c> ignores all of it; CI's bot match runs that way to
/// keep the fallbacks working. Exported builds carry the art in packs beside the game, one per asset
/// (<see cref="PackFolder"/>, made by tools/package/art-packs.sh), so an update that changes only code
/// or data doesn't download the art again, and new art costs just its own packs; they're mounted the
/// first time anything asks for art.
/// </summary>
public static class ArtFiles
{
    /// <summary>The folder beside the game holding the art packs (Pb-art-&lt;asset&gt;.pck).</summary>
    public const string PackFolder = "art";

    /// <summary>The single art pack of builds before there was one per asset.</summary>
    private const string OldPack = "Pb-art.pck";

    private const string Folder = "res://art";

    static ArtFiles()
    {
        if (Disabled || !OS.HasFeature("template"))
        {
            return; // the editor and `godot --path game` read game/art directly
        }

        string home = OS.GetExecutablePath().GetBaseDir();
        string folder = home.PathJoin(PackFolder);
        var packs = new List<string>();
        if (DirAccess.DirExistsAbsolute(folder))
        {
            packs.AddRange(DirAccess.GetFilesAt(folder).Where(f => f.EndsWith(".pck", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal).Select(f => folder.PathJoin(f)));
        }

        if (packs.Count == 0 && FileAccess.FileExists(home.PathJoin(OldPack)))
        {
            packs.Add(home.PathJoin(OldPack));
        }

        if (packs.Count == 0)
        {
            GD.Print($"No art packs in {folder}: the procedural look is used");
            return;
        }

        int mounted = 0;
        foreach (string pack in packs)
        {
            if (ProjectSettings.LoadResourcePack(pack, replaceFiles: false))
            {
                mounted++;
            }
            else
            {
                GD.PushWarning($"Couldn't load the art pack {pack}: what it holds falls back to the procedural look");
            }
        }

        // The packs' own project files (their UID lists among them) don't replace the game's, so the
        // engine is told the art's UIDs here: models find their textures by them.
        GD.Print($"Art packs in {folder}: {mounted} of {packs.Count} mounted, {RegisterUids(Folder)} art files");
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
