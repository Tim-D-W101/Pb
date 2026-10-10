using Godot;
using Pb.Game.Core;
using Pb.Game.Net;
using Pb.Net.Platform;
using Pb.Sim.Data;

namespace Pb.Game.Platform;

/// <summary>
/// The platform the game runs on, which the game asks who you are and what games there are to join. There's only the
/// offline one so far (<see cref="OfflinePlatform"/>); a platform's own (Steam, for crossplay) is chosen here once there
/// is one, and nothing else in the game changes.
/// </summary>
public static class Platforms
{
    private static IPlatformServices? _current;

    public static IPlatformServices Current => _current ??= Offline();

    /// <summary>Keeps the platform going every frame, wherever the game is (once, from the main menu).</summary>
    public static void Attach(SceneTree tree)
    {
        if (tree.Root.GetNodeOrNull(nameof(PlatformPump)) is null)
        {
            tree.Root.CallDeferred(Node.MethodName.AddChild, new PlatformPump { Name = nameof(PlatformPump) });
        }
    }

    private static OfflinePlatform Offline()
    {
        var source = new GodotDataSource();
        return new OfflinePlatform(Jsonc.Load<PresentationDef>(source, PresentationDef.File), NetStart.Settings().DiscoveryPort);
    }
}
