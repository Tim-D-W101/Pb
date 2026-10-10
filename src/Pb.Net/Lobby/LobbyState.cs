using Pb.Net.Packing;
using Pb.Net.Protocol;

namespace Pb.Net.Lobby;

/// <summary>Where a game with others is between and during its rounds.</summary>
public enum LobbyPhase : byte
{
    /// <summary>Choosing sides, characters and where to play; readying up.</summary>
    Lobby,

    /// <summary>Voting on where to play next.</summary>
    Vote,

    /// <summary>Everyone's ready: the round starts when it runs out.</summary>
    Countdown,

    /// <summary>Everyone's building the round's level.</summary>
    Loading,

    /// <summary>The round is on (its briefing, live, or just ended).</summary>
    Round,

    /// <summary>The round's over: everyone's summary.</summary>
    Summary,
}

/// <summary>What the host has chosen to play next.</summary>
public sealed record LobbyChoices
{
    public string LevelId { get; init; } = "";

    /// <summary>The place in the area (null: the whole of it).</summary>
    public string? PlaceId { get; init; }

    public string ModeId { get; init; } = "";

    /// <summary>Opponents (co-op), players (free-for-all) or players a side (teams), as the menus have it.</summary>
    public int Size { get; init; }

    /// <summary>"eliminate", "retrieve" or "hold".</summary>
    public string ObjectiveId { get; init; } = "eliminate";

    public string TierId { get; init; } = "normal";

    /// <summary>Teams: the sides kept within one person of each other.</summary>
    public bool Balance { get; init; } = true;

    /// <summary>Between rounds, a vote on where to play next.</summary>
    public bool Vote { get; init; }

    /// <summary>Speedball: the points a side needs to win the match (0: the rules' own).</summary>
    public int RaceTo { get; init; }
}

/// <summary>Someone in the game: who, which character, which side (teams), ready or not, their ping and their score so far.</summary>
public sealed class LobbyMember
{
    /// <summary>0 for the host playing in their own game, else their connection's number on the server.</summary>
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public byte Look { get; set; }

    /// <summary>Teams: 0 or 1. Co-op and free-for-all: −1 (no sides to choose).</summary>
    public int Side { get; set; } = -1;

    public bool Ready { get; set; }

    public bool Host { get; set; }

    /// <summary>Round trip to the server (ms; the host's own is 0).</summary>
    public int Ping_ms { get; set; }

    /// <summary>Opponents put out, and rounds won (on the winning side, or the last standing), across the session.</summary>
    public int Eliminations { get; set; }

    public int RoundsWon { get; set; }

    /// <summary>The vote option chosen (−1: none).</summary>
    public int Vote { get; set; } = -1;

    /// <summary>What they wear (their gear locker's, as the host checked it; null: the field's own kit on their character).</summary>
    public Pb.Sim.Gear.Loadout? Kit { get; set; }
}

/// <summary>A place to vote for: an area and a place in it, and what to call it.</summary>
public sealed record VoteOption(string LevelId, string? PlaceId, string Name);

/// <summary>
/// The lobby as everyone sees it: the host sends it (reliably) whenever it changes, and every couple of seconds during a
/// round for the pings.
/// </summary>
public sealed class LobbyState
{
    private const int MostMembers = 16;
    private const int MostOptions = 8;

    public string ServerName { get; set; } = "";

    public LobbyPhase Phase { get; set; }

    public LobbyChoices Choices { get; set; } = new();

    public List<LobbyMember> Members { get; } = new();

    /// <summary>The countdown's or the vote's time left (s).</summary>
    public float TimeLeft { get; set; }

    /// <summary>The host started the countdown without waiting for everyone to be ready.</summary>
    public bool Forced { get; set; }

    public int RoundsPlayed { get; set; }

    /// <summary>Rounds won by each side (co-op: side 0 is the people, side 1 the squad); in speedball, matches won.</summary>
    public int[] SideWins { get; } = new int[2];

    /// <summary>Speedball: the match under way, each side's points and the points played (0: none under way).</summary>
    public int[] MatchPoints { get; } = new int[2];

    public int MatchPlayed { get; set; }

    public List<VoteOption> VoteOptions { get; } = new();

    /// <summary>The most people the game takes.</summary>
    public int MaxPeople { get; set; }

    public LobbyMember? Find(int id)
    {
        foreach (LobbyMember m in Members)
        {
            if (m.Id == id)
            {
                return m;
            }
        }

        return null;
    }

    public void Write(BitWriter w)
    {
        w.WriteByte((byte)MessageType.Lobby);
        w.WriteString(ServerName, 64);
        w.WriteByte((byte)Phase);
        LobbyChoices c = Choices;
        w.WriteString(c.LevelId, 64);
        w.WriteString(c.PlaceId ?? "", 64);
        w.WriteString(c.ModeId, 32);
        w.WriteVarUInt((uint)Math.Max(0, c.Size));
        w.WriteString(c.ObjectiveId, 32);
        w.WriteString(c.TierId, 32);
        w.WriteBool(c.Balance);
        w.WriteBool(c.Vote);
        w.WriteVarUInt((uint)Math.Max(0, c.RaceTo));
        w.WriteFloat(TimeLeft);
        w.WriteBool(Forced);
        w.WriteVarUInt((uint)RoundsPlayed);
        w.WriteVarUInt((uint)SideWins[0]);
        w.WriteVarUInt((uint)SideWins[1]);
        w.WriteVarUInt((uint)MatchPoints[0]);
        w.WriteVarUInt((uint)MatchPoints[1]);
        w.WriteVarUInt((uint)MatchPlayed);
        w.WriteVarUInt((uint)MaxPeople);
        w.WriteVarUInt((uint)Math.Min(Members.Count, MostMembers));
        for (int i = 0; i < Members.Count && i < MostMembers; i++)
        {
            LobbyMember m = Members[i];
            w.WriteVarUInt((uint)m.Id);
            w.WriteString(m.Name, 48);
            w.WriteByte(m.Look);
            w.WriteVarInt(m.Side);
            w.WriteBool(m.Ready);
            w.WriteBool(m.Host);
            w.WriteVarUInt((uint)Math.Clamp(m.Ping_ms, 0, 9999));
            w.WriteVarUInt((uint)m.Eliminations);
            w.WriteVarUInt((uint)m.RoundsWon);
            w.WriteVarInt(m.Vote);
            KitCodec.Write(w, m.Kit);
        }

        w.WriteVarUInt((uint)Math.Min(VoteOptions.Count, MostOptions));
        for (int i = 0; i < VoteOptions.Count && i < MostOptions; i++)
        {
            w.WriteString(VoteOptions[i].LevelId, 64);
            w.WriteString(VoteOptions[i].PlaceId ?? "", 64);
            w.WriteString(VoteOptions[i].Name, 96);
        }
    }

    public static LobbyState? Read(ReadOnlySpan<byte> packet)
    {
        var r = new BitReader(packet);
        if ((MessageType)r.ReadByte() != MessageType.Lobby)
        {
            return null;
        }

        var s = new LobbyState { ServerName = r.ReadString(64), Phase = (LobbyPhase)r.ReadByte() };
        string level = r.ReadString(64), place = r.ReadString(64), mode = r.ReadString(32);
        int size = (int)r.ReadVarUInt();
        string objective = r.ReadString(32), tier = r.ReadString(32);
        bool balance = r.ReadBool(), vote = r.ReadBool();
        int raceTo = (int)r.ReadVarUInt();
        s.Choices = new LobbyChoices
        {
            LevelId = level, PlaceId = place.Length == 0 ? null : place, ModeId = mode, Size = size, ObjectiveId = objective, TierId = tier,
            Balance = balance, Vote = vote, RaceTo = raceTo,
        };
        s.TimeLeft = r.ReadFloat();
        s.Forced = r.ReadBool();
        s.RoundsPlayed = (int)r.ReadVarUInt();
        s.SideWins[0] = (int)r.ReadVarUInt();
        s.SideWins[1] = (int)r.ReadVarUInt();
        s.MatchPoints[0] = (int)r.ReadVarUInt();
        s.MatchPoints[1] = (int)r.ReadVarUInt();
        s.MatchPlayed = (int)r.ReadVarUInt();
        s.MaxPeople = (int)r.ReadVarUInt();
        uint members = r.ReadVarUInt();
        for (uint i = 0; i < members && i < MostMembers && !r.Overflowed; i++)
        {
            var member = new LobbyMember
            {
                Id = (int)r.ReadVarUInt(), Name = r.ReadString(48), Look = r.ReadByte(), Side = r.ReadVarInt(), Ready = r.ReadBool(),
                Host = r.ReadBool(), Ping_ms = (int)r.ReadVarUInt(), Eliminations = (int)r.ReadVarUInt(), RoundsWon = (int)r.ReadVarUInt(),
                Vote = r.ReadVarInt(),
            };
            member.Kit = KitCodec.Read(ref r);
            s.Members.Add(member);
        }

        uint options = r.ReadVarUInt();
        for (uint i = 0; i < options && i < MostOptions && !r.Overflowed; i++)
        {
            string l = r.ReadString(64), p = r.ReadString(64), n = r.ReadString(96);
            s.VoteOptions.Add(new VoteOption(l, p.Length == 0 ? null : p, n));
        }

        return r.Overflowed || members > MostMembers || options > MostOptions ? null : s;
    }
}

/// <summary>What a player asks of the lobby.</summary>
public enum LobbyAsk : byte
{
    /// <summary>Teams: the side to play on (0 or 1).</summary>
    Side,

    /// <summary>Ready (1) or not (0).</summary>
    Ready,

    /// <summary>Which character to play.</summary>
    Look,

    /// <summary>The vote option chosen.</summary>
    Vote,
}

/// <summary>A player's request of the lobby: what, and its value.</summary>
public static class LobbyRequest
{
    public static void Write(BitWriter w, LobbyAsk ask, int value)
    {
        w.WriteByte((byte)MessageType.LobbyRequest);
        w.WriteByte((byte)ask);
        w.WriteVarInt(value);
    }

    public static (LobbyAsk Ask, int Value)? Read(ReadOnlySpan<byte> packet)
    {
        var r = new BitReader(packet);
        if ((MessageType)r.ReadByte() != MessageType.LobbyRequest)
        {
            return null;
        }

        var ask = (LobbyAsk)r.ReadByte();
        int value = r.ReadVarInt();
        return r.Overflowed || ask > LobbyAsk.Vote ? null : (ask, value);
    }
}

/// <summary>A line of text chat: who said it, on which side, to everyone or only their side.</summary>
public sealed record ChatLine(int From, string Name, int Side, bool TeamOnly, string Text);

/// <summary>
/// Text chat: a player sends what they say (<see cref="WriteSay"/>), and the host sends each line on to everyone it's for
/// (<see cref="WriteLine"/>), with who said it.
/// </summary>
public static class ChatMessage
{
    /// <summary>The longest a line may be on the wire (net.jsonc's limit is checked by the host).</summary>
    public const int WireLength = 400;

    public static void WriteSay(BitWriter w, string text, bool teamOnly)
    {
        w.WriteByte((byte)MessageType.Chat);
        w.WriteBool(false);
        w.WriteBool(teamOnly);
        w.WriteString(text, WireLength);
    }

    public static void WriteLine(BitWriter w, ChatLine line)
    {
        w.WriteByte((byte)MessageType.Chat);
        w.WriteBool(true);
        w.WriteBool(line.TeamOnly);
        w.WriteString(line.Text, WireLength);
        w.WriteVarInt(line.From);
        w.WriteString(line.Name, 48);
        w.WriteVarInt(line.Side);
    }

    /// <summary>What a player said (null if this isn't that).</summary>
    public static (string Text, bool TeamOnly)? ReadSay(ReadOnlySpan<byte> packet)
    {
        var r = new BitReader(packet);
        if ((MessageType)r.ReadByte() != MessageType.Chat || r.ReadBool())
        {
            return null;
        }

        bool team = r.ReadBool();
        string text = r.ReadString(WireLength);
        return r.Overflowed ? null : (text, team);
    }

    /// <summary>A line the host sent on (null if this isn't that).</summary>
    public static ChatLine? ReadLine(ReadOnlySpan<byte> packet)
    {
        var r = new BitReader(packet);
        if ((MessageType)r.ReadByte() != MessageType.Chat || !r.ReadBool())
        {
            return null;
        }

        bool team = r.ReadBool();
        string text = r.ReadString(WireLength);
        int from = r.ReadVarInt();
        string name = r.ReadString(48);
        int side = r.ReadVarInt();
        return r.Overflowed ? null : new ChatLine(from, name, side, team, text);
    }
}
