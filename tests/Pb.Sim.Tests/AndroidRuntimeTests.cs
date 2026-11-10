using System.Text.RegularExpressions;
using Pb.Sim.Data;

namespace Pb.Sim.Tests;

/// <summary>
/// The Android build runs on Mono, not .NET's CoreCLR, and Mono turns down calls the computer takes (Phase 6: a level's
/// start set the collector's latency mode, Mono threw, and a phone was left on a grey screen). CI runs on CoreCLR, so this
/// holds the code to what Mono takes.
/// </summary>
public class AndroidRuntimeTests
{
    /// <summary>
    /// What Mono's collector refuses (PlatformNotSupportedException for anything but the default, as Android's
    /// System.Private.CoreLib has it): setting the latency or large-object compaction mode, and no-GC regions and full-GC
    /// notifications.
    /// </summary>
    private static readonly Regex MonoRefuses = new(
        @"GCSettings\s*\.\s*(LatencyMode|LargeObjectHeapCompactionMode)\s*=(?!=)|GC\s*\.\s*(TryStartNoGCRegion|EndNoGCRegion|RegisterForFullGCNotification)\s*\(");

    [Fact]
    public void Only_GcModes_sets_what_Monos_collector_refuses()
    {
        string root = Path.GetFullPath(Path.Combine(FileSystemDataSource.FindRepoData().Root, "..", ".."));
        var found = new List<string>();
        foreach (string folder in new[] { "game", "src", "tools" })
        {
            foreach (string file in Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
            {
                string path = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (path == "game/core/GcModes.cs" || path.Contains("/.godot/") || path.Contains("/obj/") || path.Contains("/bin/"))
                {
                    continue;
                }

                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (MonoRefuses.IsMatch(lines[i]))
                    {
                        found.Add($"{path}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
        }

        Assert.True(found.Count == 0,
            "Android's runtime (Mono) throws PlatformNotSupportedException at these; go through game/core/GcModes.cs, which holds off " +
            "full collections only where the runtime can:\n" + string.Join("\n", found));
    }
}
