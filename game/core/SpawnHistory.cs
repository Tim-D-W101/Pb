using System.Collections.Generic;
using Pb.Sim.Match;

namespace Pb.Game.Core;

/// <summary>
/// Where the last round on each area and place started, for as long as the game runs (rounds alone, hosted and on the
/// dedicated server), so the next round there deals its starts elsewhere (rules.jsonc spawning "repeatRadius_m").
/// </summary>
public static class SpawnHistory
{
    private static readonly Dictionary<string, SpawnMemory> Last = new();

    /// <summary>The last round's starts on <paramref name="levelId"/> (in <paramref name="placeId"/>), if any.</summary>
    public static SpawnMemory? For(string levelId, string? placeId)
    {
        lock (Last)
        {
            return Last.TryGetValue(Key(levelId, placeId), out SpawnMemory? memory) ? memory : null;
        }
    }

    public static void Remember(string levelId, string? placeId, SpawnMemory memory)
    {
        lock (Last)
        {
            Last[Key(levelId, placeId)] = memory;
        }
    }

    private static string Key(string levelId, string? placeId) => $"{levelId}/{placeId ?? "whole"}";
}
