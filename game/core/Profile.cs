using Godot;
using Pb.Sim.Data;
using Pb.Sim.Match;

namespace Pb.Game.Core;

/// <summary>
/// Your progress up the ladder (levels opened, records, last menu choices), saved to user://profile.json. The
/// rules live in the sim's <see cref="LadderProgress"/>; this only reads and writes the file. An unreadable file
/// is set aside as profile.bad.json and you start afresh, so a broken save never stops the game.
/// </summary>
public static class Profile
{
    private const string Path = "user://profile.json";

    /// <summary>The saved progress (or a fresh one), with every level shown open if the settings or <c>--unlock-all</c> say so.</summary>
    public static LadderProgress Load(LadderDef ladder, GameSettings settings)
    {
        ProfileData? data = null;
        if (FileAccess.FileExists(Path))
        {
            using FileAccess? file = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
            data = file is null ? null : ProfileData.FromJson(file.GetAsText());
            if (data is null)
            {
                GD.PushWarning($"Ignoring unreadable {Path}; it's kept as profile.bad.json");
                DirAccess.RenameAbsolute(ProjectSettings.GlobalizePath(Path), ProjectSettings.GlobalizePath("user://profile.bad.json"));
            }
        }

        return new LadderProgress(ladder, data) { OpenAll = settings.OpenAllLevels || Args.Has("--unlock-all") };
    }

    public static void Save(LadderProgress progress)
    {
        using FileAccess? file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        if (file is null)
        {
            GD.PushWarning($"Couldn't save {Path}: {FileAccess.GetOpenError()}");
            return;
        }

        file.StoreString(progress.Data.ToJson());
    }
}
