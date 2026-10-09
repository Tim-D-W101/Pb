using Pb.Net.Packing;
using Pb.Net.Protocol;

namespace Pb.Net.Discovery;

/// <summary>A game on your network, as its host answers a search: what to call it, where it plays, how full it is.</summary>
public sealed record GameAnnouncement
{
    public string Name { get; init; } = "";

    /// <summary>The port to join on (the answer comes from the discovery port).</summary>
    public int Port { get; init; }

    /// <summary>"Oxbarrow Works: the warehouse".</summary>
    public string Where { get; init; } = "";

    /// <summary>"Teams · 3 v 3 · Normal".</summary>
    public string How { get; init; } = "";

    public int People { get; init; }

    public int MaxPeople { get; init; }

    /// <summary>The host's build: only the same build may join.</summary>
    public string Build { get; init; } = "";

    public bool Password { get; init; }

    /// <summary>A round is on: joining now waits for the next.</summary>
    public bool InRound { get; init; }
}

/// <summary>
/// Finding games on your network: a copy broadcasts a search on the discovery port (UDP 47821), and every host there
/// answers with its <see cref="GameAnnouncement"/>. Both carry a mark and the protocol version, so nothing else's
/// packets are taken for them.
/// </summary>
public static class DiscoveryMessage
{
    private const uint SearchMark = 0x50425131; // "PBQ1"
    private const uint AnswerMark = 0x50424131; // "PBA1"

    public static void WriteSearch(BitWriter w)
    {
        w.WriteUInt(SearchMark);
        w.WriteVarUInt(NetProtocol.Version);
    }

    /// <summary>Whether a packet is a search this version answers.</summary>
    public static bool IsSearch(ReadOnlySpan<byte> packet)
    {
        var r = new BitReader(packet);
        uint mark = r.ReadUInt();
        uint version = r.ReadVarUInt();
        return !r.Overflowed && mark == SearchMark && version == NetProtocol.Version;
    }

    public static void WriteAnswer(BitWriter w, GameAnnouncement game)
    {
        w.WriteUInt(AnswerMark);
        w.WriteVarUInt(NetProtocol.Version);
        w.WriteString(game.Name, 64);
        w.WriteVarUInt((uint)game.Port);
        w.WriteString(game.Where, 96);
        w.WriteString(game.How, 96);
        w.WriteVarUInt((uint)game.People);
        w.WriteVarUInt((uint)game.MaxPeople);
        w.WriteString(game.Build, 64);
        w.WriteBool(game.Password);
        w.WriteBool(game.InRound);
    }

    /// <summary>A host's answer (null if this isn't one, or it's another version's).</summary>
    public static GameAnnouncement? ReadAnswer(ReadOnlySpan<byte> packet)
    {
        var r = new BitReader(packet);
        if (r.ReadUInt() != AnswerMark || r.ReadVarUInt() != NetProtocol.Version)
        {
            return null;
        }

        var game = new GameAnnouncement
        {
            Name = r.ReadString(64), Port = (int)r.ReadVarUInt(), Where = r.ReadString(96), How = r.ReadString(96), People = (int)r.ReadVarUInt(),
            MaxPeople = (int)r.ReadVarUInt(), Build = r.ReadString(64), Password = r.ReadBool(), InRound = r.ReadBool(),
        };
        return r.Overflowed || game.Port is <= 0 or > 65535 ? null : game;
    }
}
