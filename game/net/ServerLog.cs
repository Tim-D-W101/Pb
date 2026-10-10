using System;
using Godot;

namespace Pb.Game.Net;

/// <summary>
/// The dedicated server's log: who joined and left, each round and how it went, and anything it dropped, each line
/// stamped with the time, to the console and to user://logs/server.log (kept across runs; once it passes 10 MB, the
/// next run starts a new one and keeps the last as server.old.log).
/// </summary>
public static class ServerLog
{
    private const string Path = "user://logs/server.log";
    private const string OldPath = "user://logs/server.old.log";
    private const long MostBytes = 10L << 20;
    private static bool _checked;

    public static void Line(string text)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}";
        GD.Print(line);
        DirAccess.MakeDirRecursiveAbsolute("user://logs");
        if (!_checked)
        {
            _checked = true;
            StartAnewIfLong();
        }

        using FileAccess? file = FileAccess.FileExists(Path) ? FileAccess.Open(Path, FileAccess.ModeFlags.ReadWrite) : FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        if (file is null)
        {
            return;
        }

        file.SeekEnd();
        file.StoreLine(line);
    }

    private static void StartAnewIfLong()
    {
        if (!FileAccess.FileExists(Path))
        {
            return;
        }

        long length;
        using (FileAccess? file = FileAccess.Open(Path, FileAccess.ModeFlags.Read))
        {
            length = (long)(file?.GetLength() ?? 0);
        }

        if (length > MostBytes)
        {
            DirAccess.RemoveAbsolute(OldPath);
            DirAccess.RenameAbsolute(Path, OldPath);
        }
    }
}
