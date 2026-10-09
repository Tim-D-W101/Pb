using Pb.Game.Core;
using Pb.Net.Platform;

namespace Pb.Game.Platform;

/// <summary>
/// Playing without a platform: you're who your profile says (<see cref="OfflineIdentity"/>), games are found on your
/// network or joined by address (<see cref="LanSessions"/>), and there are no friends lists, invites or store.
/// </summary>
public sealed class OfflinePlatform : IPlatformServices
{
    private readonly LanSessions _sessions;

    public OfflinePlatform(PresentationDef view, int discoveryPort)
    {
        Identity = new OfflineIdentity(view);
        _sessions = new LanSessions(discoveryPort);
    }

    public string Name => "Offline";

    public IIdentity Identity { get; }

    public ISessionBrowser Sessions => _sessions;

    public IFriends Friends { get; } = new NoFriends();

    public IStore? Store => null;

    public void Poll(double now) => _sessions.Poll(now);

    public void Dispose() => _sessions.Dispose();
}
