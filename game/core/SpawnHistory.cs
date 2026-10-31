using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using Pb.Sim.Match;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// Where the last round on each area and place started, and the entry you came in at, so the next round there deals its
/// starts elsewhere (rules.jsonc spawning "repeatRadius_m"): rounds alone, bot matches, hosted rounds and the dedicated
/// server's. Kept in <c>user://spawn_history.json</c>, so a new game (or a new headless bot match) remembers the last
/// round too.
/// </summary>
public static class SpawnHistory
{
    private const string File = "user://spawn_history.json";

    private static readonly object Gate = new();
    private static Dictionary<string, SpawnMemory>? _last;

    /// <summary>The last round's starts on <paramref name="levelId"/> (in <paramref name="placeId"/>), if any.</summary>
    public static SpawnMemory? For(string levelId, string? placeId)
    {
        lock (Gate)
        {
            return Loaded().TryGetValue(Key(levelId, placeId), out SpawnMemory? memory) ? memory : null;
        }
    }

    public static void Remember(string levelId, string? placeId, SpawnMemory memory)
    {
        lock (Gate)
        {
            Loaded()[Key(levelId, placeId)] = memory;
            Save();
        }
    }

    private static string Key(string levelId, string? placeId) => $"{levelId}/{placeId ?? "whole"}";

    private static Dictionary<string, SpawnMemory> Loaded()
    {
        if (_last is not null)
        {
            return _last;
        }

        _last = new Dictionary<string, SpawnMemory>();
        try
        {
            string path = ProjectSettings.GlobalizePath(File);
            if (System.IO.File.Exists(path) &&
                JsonSerializer.Deserialize<Dictionary<string, Saved>>(System.IO.File.ReadAllText(path)) is { } saved)
            {
                foreach ((string key, Saved round) in saved)
                {
                    _last[key] = SpawnMemory.Of(round.Starts.Where(s => s.Length == 3).Select(s => new SVector3(s[0], s[1], s[2])),
                        round.Entry is { Length: 3 } e ? new SVector3(e[0], e[1], e[2]) : null);
                }
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Not worth stopping for: the next round just doesn't know where the last one started.
            GD.Print($"Spawn history not read ({e.Message}); starting afresh.");
        }

        return _last;
    }

    private static void Save()
    {
        try
        {
            var saved = _last!.ToDictionary(kv => kv.Key, kv => new Saved
            {
                Starts = kv.Value.Starts.Select(s => new[] { s.X, s.Y, s.Z }).ToArray(),
                Entry = kv.Value.YourEntry is { } e ? new[] { e.X, e.Y, e.Z } : null,
            });
            string path = ProjectSettings.GlobalizePath(File);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Written whole, then moved over the old one, so another copy reading it never sees half a file.
            string temp = $"{path}.{System.Environment.ProcessId}.tmp";
            System.IO.File.WriteAllText(temp, JsonSerializer.Serialize(saved));
            System.IO.File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.Print($"Spawn history not saved ({e.Message}).");
        }
    }

    private sealed class Saved
    {
        public float[][] Starts { get; set; } = Array.Empty<float[]>();

        public float[]? Entry { get; set; }
    }
}
