using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Godot;

namespace Pb.Game.Net;

/// <summary>
/// What a copy tells a host about itself, so only copies that play by the same rules play together: the build (the stamp
/// packaging writes into res://build.txt, the commit the build came from; "dev" when run from the source) and a hash of
/// every data file.
/// </summary>
public static class BuildStamp
{
    private static string? _build;
    private static string? _data;

    public static string Build => _build ??= Read();

    /// <summary>SHA-256 of every file under res://data, paths and contents, in order of path.</summary>
    public static string DataHash => _data ??= HashData();

    private static string Read()
    {
        const string Path = "res://build.txt";
        return FileAccess.FileExists(Path) ? FileAccess.GetFileAsString(Path).Trim() : "dev";
    }

    private static string HashData()
    {
        var files = new List<string>();
        Collect("res://data", files);
        files.Sort(StringComparer.Ordinal);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string path in files)
        {
            sha.AppendData(Encoding.UTF8.GetBytes(path));
            sha.AppendData(FileAccess.GetFileAsBytes(path));
        }

        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    private static void Collect(string dir, List<string> into)
    {
        using DirAccess? d = DirAccess.Open(dir);
        if (d is null)
        {
            return;
        }

        foreach (string file in d.GetFiles())
        {
            if (file.EndsWith(".jsonc", StringComparison.Ordinal) || file.EndsWith(".json", StringComparison.Ordinal))
            {
                into.Add(dir + "/" + file);
            }
        }

        foreach (string sub in d.GetDirectories())
        {
            Collect(dir + "/" + sub, into);
        }
    }
}
