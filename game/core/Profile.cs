using Godot;
using Pb.Sim.Data;
using Pb.Sim.Match;

namespace Pb.Game.Core;

/// <summary>
/// Your records (your best in each area, place, mode, objective and difficulty) and the menu's last choices, saved to
/// user://profile.json. The rules live in the sim's <see cref="RecordBook"/>; this only reads and writes the file. An
/// unreadable file is set aside as profile.bad.json and you start afresh, so a broken save never stops the game. Saves
/// from the ladder load too (their records count as the whole area's; the levels they had opened don't matter now).
/// </summary>
public static class Profile
{
    private const string Path = "user://profile.json";

    /// <summary>The saved records, or a fresh book.</summary>
    public static RecordBook Load(AreaListDef areas)
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

        return new RecordBook(areas, data);
    }

    public static void Save(RecordBook records)
    {
        using FileAccess? file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        if (file is null)
        {
            GD.PushWarning($"Couldn't save {Path}: {FileAccess.GetOpenError()}");
            return;
        }

        file.StoreString(records.Data.ToJson());
    }
}
