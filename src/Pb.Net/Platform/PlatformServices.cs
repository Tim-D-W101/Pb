using Pb.Net.Discovery;

namespace Pb.Net.Platform;

/// <summary>
/// What the game asks of the platform it runs on: who you are, finding games to join, friends and invites, and a store.
/// The game talks to these alone, so a platform's own (Steam, for crossplay: its accounts, lobbies, invites and relayed
/// connections as another <c>ITransport</c>) can stand in for the offline one without the game changing.
/// </summary>
public interface IPlatformServices : IDisposable
{
    /// <summary>"Offline", or the platform's name.</summary>
    string Name { get; }

    IIdentity Identity { get; }

    ISessionBrowser Sessions { get; }

    IFriends Friends { get; }

    /// <summary>The platform's store; null while there's none.</summary>
    IStore? Store { get; }

    /// <summary>Lets the platform do its work (searches, callbacks), once a frame; <paramref name="now"/> in seconds.</summary>
    void Poll(double now);
}

/// <summary>Who you are.</summary>
public interface IIdentity
{
    /// <summary>The name you play under.</summary>
    string Name { get; }

    /// <summary>
    /// Stays yours from game to game, so a host knows you again when you come back: offline, one made once and kept in
    /// your profile; on a platform, your account's.
    /// </summary>
    string Id { get; }

    /// <summary>The game asks you for your name and keeps it (offline); on a platform, your account's name is used.</summary>
    bool ChoosesName { get; }
}

/// <summary>A game to join: where ("host:port"), and what it says about itself.</summary>
public sealed record FoundGame(string Address, GameAnnouncement Game);

/// <summary>Finding games to join. A typed address needs no finding: the game joins it straight away.</summary>
public interface ISessionBrowser
{
    /// <summary>On while a screen lists games: it searches as <see cref="IPlatformServices.Poll"/> goes. Off, it stops and forgets them.</summary>
    bool Searching { get; set; }

    /// <summary>False when there's nothing to search (no network).</summary>
    bool CanSearch { get; }

    /// <summary>The games heard from lately, by name.</summary>
    IReadOnlyList<FoundGame> Found { get; }
}

/// <summary>Someone on your friends list.</summary>
public sealed record Friend(string Id, string Name, bool Online);

/// <summary>A friend asking you to join their game.</summary>
public sealed record Invite(Friend From, string Address);

/// <summary>Friends and invites.</summary>
public interface IFriends
{
    IReadOnlyList<Friend> All { get; }

    bool CanInvite { get; }

    /// <summary>Asks a friend to join the game at <paramref name="address"/>; false if it can't.</summary>
    bool Invite(Friend friend, string address);

    /// <summary>The oldest invite not yet taken, if any.</summary>
    bool TakeInvite(out Invite? invite);
}

/// <summary>A store: what you own.</summary>
public interface IStore
{
    bool Owns(string itemId);
}
