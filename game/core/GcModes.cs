using System;
using System.Runtime;

namespace Pb.Game.Core;

/// <summary>
/// The garbage collector's latency mode, where the runtime has one. On a computer (.NET's CoreCLR) a level holds off full
/// blocking collections while it's up; Android's runtime (Mono) has only the one mode and throws
/// <see cref="PlatformNotSupportedException"/> at any other (Phase 6: thrown from the level's start, it left a phone on a
/// grey screen). Only this class sets the mode (a test holds every other file to that).
/// </summary>
public static class GcModes
{
    /// <summary>
    /// Asks the collector to avoid full blocking collections (sustained low latency); returns the mode it was in, to put
    /// back with <see cref="Restore"/>. Where the runtime can't (Mono), nothing changes.
    /// </summary>
    public static GCLatencyMode HoldOffFullCollections()
    {
        GCLatencyMode before = GCSettings.LatencyMode;
        try
        {
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        }
        catch (PlatformNotSupportedException)
        {
            // Mono (Android): its collector runs the one way.
        }

        return before;
    }

    /// <summary>Puts the collector back in <paramref name="mode"/> (what <see cref="HoldOffFullCollections"/> returned).</summary>
    public static void Restore(GCLatencyMode mode)
    {
        try
        {
            if (GCSettings.LatencyMode != mode)
            {
                GCSettings.LatencyMode = mode;
            }
        }
        catch (PlatformNotSupportedException)
        {
            // Mono (Android): nothing was changed.
        }
    }
}
