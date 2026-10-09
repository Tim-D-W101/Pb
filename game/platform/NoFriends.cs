using System;
using System.Collections.Generic;
using Pb.Net.Platform;

namespace Pb.Game.Platform;

/// <summary>Offline there are no friends lists or invites: people find your game on their network, or join it by address.</summary>
public sealed class NoFriends : IFriends
{
    public IReadOnlyList<Friend> All => Array.Empty<Friend>();

    public bool CanInvite => false;

    public bool Invite(Friend friend, string address) => false;

    public bool TakeInvite(out Invite? invite)
    {
        invite = null;
        return false;
    }
}
