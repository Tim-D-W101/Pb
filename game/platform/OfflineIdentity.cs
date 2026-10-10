using Pb.Game.Core;
using Pb.Net.Platform;

namespace Pb.Game.Platform;

/// <summary>
/// Offline, you're who your profile (the settings file) says: the name you typed, in Play with others or Settings →
/// Gameplay, and an id made the first time the game ran (<see cref="GameSettings.ProfileId"/>). Read afresh each time,
/// so it's always what was saved last.
/// </summary>
public sealed class OfflineIdentity : IIdentity
{
    private readonly PresentationDef _view;

    public OfflineIdentity(PresentationDef view)
    {
        _view = view;
    }

    public string Name => GameSettings.CleanName(Profile().PlayerName) is { Length: > 0 } name ? name : "Player";

    public string Id => "offline-" + Profile().ProfileId;

    public bool ChoosesName => true;

    private GameSettings Profile() => GameSettings.Load(_view);
}
