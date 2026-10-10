using System.Numerics;
using Pb.Net.Packing;
using Pb.Sim.Data;

namespace Pb.Net.Protocol;

/// <summary>What each packet is: its first byte.</summary>
public enum MessageType : byte
{
    None,

    /// <summary>A player asks to join: version, data, name (reliable).</summary>
    Hello,

    /// <summary>The server lets them in (reliable).</summary>
    Welcome,

    /// <summary>The server turns them away, and why (reliable).</summary>
    Refused,

    /// <summary>A player's latest commands (unreliable, 60 a second).</summary>
    Commands,

    /// <summary>The world as the server has it, and what happened (unreliable, 60 a second).</summary>
    Snapshot,

    /// <summary>A round is starting: where, how, who (reliable).</summary>
    RoundSetup,

    /// <summary>A player has the round's level built (reliable).</summary>
    Loaded,

    /// <summary>The round is over: the result and everyone's numbers (reliable).</summary>
    RoundOver,

    /// <summary>Who's in the lobby and what's chosen (reliable).</summary>
    Lobby,

    /// <summary>A player asks the lobby for something: a side, ready, a choice, a vote (reliable).</summary>
    LobbyRequest,

    /// <summary>A line of text chat (reliable).</summary>
    Chat,

    /// <summary>Leaving, or being sent away, and why (reliable).</summary>
    Bye,

    /// <summary>A player's kit, changed in the gear locker between rounds (reliable).</summary>
    Kit,
}

public static class NetProtocol
{
    /// <summary>
    /// Bumped whenever a message changes: copies on different versions refuse each other (2: the callout key; 3: who you
    /// are, in the hello; 4: everyone's kit, in the hello, the lobby and the round's roster; 5: speedball's countdown,
    /// buzzers and match score, in the snapshot, the round's setup and the lobby).
    /// </summary>
    public const int Version = 5;

    public static MessageType TypeOf(ReadOnlySpan<byte> packet) => packet.Length > 0 ? (MessageType)packet[0] : MessageType.None;
}

/// <summary>Why the server turned a player away.</summary>
public enum RefusedReason : byte
{
    /// <summary>The two copies speak different versions of the protocol.</summary>
    Protocol,

    /// <summary>The two copies are different builds, or have different data files.</summary>
    Version,

    Password,

    /// <summary>The game has as many people as it takes.</summary>
    Full,

    /// <summary>The host removed this player.</summary>
    Removed,
}

public sealed class HelloMessage
{
    public int Protocol { get; set; } = NetProtocol.Version;

    /// <summary>Which build this is (its version stamp).</summary>
    public string Build { get; set; } = "";

    /// <summary>A hash of every data file, so two copies only play together with the same rules.</summary>
    public string DataHash { get; set; } = "";

    public string Name { get; set; } = "";

    public string Password { get; set; } = "";

    /// <summary>Which character they play.</summary>
    public byte Look { get; set; }

    /// <summary>Who they are: their platform identity's id, which stays theirs from game to game (empty if unknown).</summary>
    public string Key { get; set; } = "";

    /// <summary>What they wear, from their gear locker (null: the field's own kit on their character).</summary>
    public Pb.Sim.Gear.Loadout? Kit { get; set; }

    public void Write(BitWriter w)
    {
        w.WriteByte((byte)MessageType.Hello);
        w.WriteVarUInt((uint)Protocol);
        w.WriteString(Build, 64);
        w.WriteString(DataHash, 80);
        w.WriteString(Name, 48);
        w.WriteString(Password, 64);
        w.WriteByte(Look);
        w.WriteString(Key, 64);
        KitCodec.Write(w, Kit);
    }

    public static HelloMessage? Read(ReadOnlySpan<byte> packet)
    {
        var r = new BitReader(packet);
        if ((MessageType)r.ReadByte() != MessageType.Hello)
        {
            return null;
        }

        var m = new HelloMessage
        {
            Protocol = (int)r.ReadVarUInt(), Build = r.ReadString(64), DataHash = r.ReadString(80), Name = r.ReadString(48),
            Password = r.ReadString(64), Look = r.ReadByte(),
        };

        // Older hellos end sooner; they're still read, so the copy is told which version to update to.
        if (m.Protocol >= 3)
        {
            m.Key = r.ReadString(64);
        }

        if (m.Protocol >= 4)
        {
            m.Kit = KitCodec.Read(ref r);
        }

        return r.Overflowed ? null : m;
    }
}

public sealed class WelcomeMessage
{
    /// <summary>The player's peer number on the server, which names them in the lobby.</summary>
    public int ClientId { get; set; }

    public string ServerName { get; set; } = "";

    /// <summary>The name they'll play under (the server makes it unique).</summary>
    public string Name { get; set; } = "";

    public void Write(BitWriter w)
    {
        w.WriteByte((byte)MessageType.Welcome);
        w.WriteVarUInt((uint)ClientId);
        w.WriteString(ServerName, 64);
        w.WriteString(Name, 48);
    }

    public static WelcomeMessage? Read(ReadOnlySpan<byte> packet)
    {
        var r = new BitReader(packet);
        if ((MessageType)r.ReadByte() != MessageType.Welcome)
        {
            return null;
        }

        var m = new WelcomeMessage { ClientId = (int)r.ReadVarUInt(), ServerName = r.ReadString(64), Name = r.ReadString(48) };
        return r.Overflowed ? null : m;
    }
}

public sealed class RefusedMessage
{
    public RefusedReason Reason { get; set; }

    /// <summary>What to tell the player, e.g. which version the server runs.</summary>
    public string Text { get; set; } = "";

    public void Write(BitWriter w)
    {
        w.WriteByte((byte)MessageType.Refused);
        w.WriteByte((byte)Reason);
        w.WriteString(Text, 200);
    }

    public static RefusedMessage? Read(ReadOnlySpan<byte> packet)
    {
        var r = new BitReader(packet);
        if ((MessageType)r.ReadByte() != MessageType.Refused)
        {
            return null;
        }

        var m = new RefusedMessage { Reason = (RefusedReason)r.ReadByte(), Text = r.ReadString(200) };
        return r.Overflowed ? null : m;
    }
}

/// <summary>Someone in a round: who they are, which side, person or bot, and where they start.</summary>
public sealed record RosterEntry
{
    public required int PlayerId { get; init; }

    public required byte Team { get; init; }

    public required string Name { get; init; }

    public required bool Person { get; init; }

    public byte Look { get; init; }

    /// <summary>What a person wears (their gear locker's; null: the field's own on their character). Bots' is dealt on every copy.</summary>
    public Pb.Sim.Gear.Loadout? Kit { get; init; }

    public required Vector3 Position { get; init; }

    public required float Yaw { get; init; }
}

/// <summary>
/// A round starting: which level and place, how it's played, its seed, and everyone in it in the order the server added
/// them, so every copy builds the same world (the same seed deals the same doors and objective).
/// </summary>
public sealed class RoundSetupMessage
{
    public int Round { get; set; }

    public string LevelId { get; set; } = "";

    /// <summary>The place in the area (null: the whole of it).</summary>
    public string? PlaceId { get; set; }

    public string ModeId { get; set; } = "";

    public int Size { get; set; }

    public ObjectiveKind Objective { get; set; }

    public string TierId { get; set; } = "";

    public ulong Seed { get; set; }

    public float TimeLimit { get; set; }

    public int StartPods { get; set; }

    public int BotPods { get; set; }

    public bool Pickups { get; set; }

    public byte Attackers { get; set; }

    public bool EndWhenPeopleOut { get; set; }

    /// <summary>A point of a speedball match: the points a side needs (0: not a match), and the score before this point.</summary>
    public int RaceTo { get; set; }

    public int Points0 { get; set; }

    public int Points1 { get; set; }

    /// <summary>The match's points played before this one (those nobody won included).</summary>
    public int PointsPlayed { get; set; }

    public List<RosterEntry> Roster { get; } = new();

    /// <summary>The player this copy plays (−1: watching).</summary>
    public int YourPlayerId { get; set; } = -1;

    public void Write(BitWriter w)
    {
        w.WriteByte((byte)MessageType.RoundSetup);
        w.WriteVarUInt((uint)Round);
        w.WriteString(LevelId, 64);
        w.WriteString(PlaceId ?? "", 64);
        w.WriteString(ModeId, 32);
        w.WriteVarUInt((uint)Size);
        w.WriteByte((byte)Objective);
        w.WriteString(TierId, 32);
        w.WriteUInt((uint)(Seed >> 32));
        w.WriteUInt((uint)Seed);
        w.WriteFloat(TimeLimit);
        w.WriteVarUInt((uint)StartPods);
        w.WriteVarUInt((uint)BotPods);
        w.WriteBool(Pickups);
        w.WriteByte(Attackers);
        w.WriteBool(EndWhenPeopleOut);
        w.WriteVarUInt((uint)RaceTo);
        w.WriteVarUInt((uint)Points0);
        w.WriteVarUInt((uint)Points1);
        w.WriteVarUInt((uint)PointsPlayed);
        w.WriteVarInt(YourPlayerId);
        w.WriteVarUInt((uint)Roster.Count);
        foreach (RosterEntry e in Roster)
        {
            w.WriteVarUInt((uint)e.PlayerId);
            w.WriteByte(e.Team);
            w.WriteString(e.Name, 48);
            w.WriteBool(e.Person);
            w.WriteByte(e.Look);
            KitCodec.Write(w, e.Kit);
            w.WriteFloat(e.Position.X);
            w.WriteFloat(e.Position.Y);
            w.WriteFloat(e.Position.Z);
            w.WriteFloat(e.Yaw);
        }
    }

    public static RoundSetupMessage? Read(ReadOnlySpan<byte> packet)
    {
        var r = new BitReader(packet);
        if ((MessageType)r.ReadByte() != MessageType.RoundSetup)
        {
            return null;
        }

        var m = new RoundSetupMessage
        {
            Round = (int)r.ReadVarUInt(), LevelId = r.ReadString(64), PlaceId = r.ReadString(64), ModeId = r.ReadString(32),
            Size = (int)r.ReadVarUInt(), Objective = (ObjectiveKind)r.ReadByte(), TierId = r.ReadString(32),
        };
        ulong high = r.ReadUInt();
        ulong low = r.ReadUInt();
        m.Seed = (high << 32) | low;
        m.TimeLimit = r.ReadFloat();
        m.StartPods = (int)r.ReadVarUInt();
        m.BotPods = (int)r.ReadVarUInt();
        m.Pickups = r.ReadBool();
        m.Attackers = r.ReadByte();
        m.EndWhenPeopleOut = r.ReadBool();
        m.RaceTo = (int)r.ReadVarUInt();
        m.Points0 = (int)r.ReadVarUInt();
        m.Points1 = (int)r.ReadVarUInt();
        m.PointsPlayed = (int)r.ReadVarUInt();
        m.YourPlayerId = r.ReadVarInt();
        if (m.PlaceId.Length == 0)
        {
            m.PlaceId = null;
        }

        uint count = r.ReadVarUInt();
        for (uint i = 0; i < count && i < 64 && !r.Overflowed; i++)
        {
            int id = (int)r.ReadVarUInt();
            byte team = r.ReadByte();
            string name = r.ReadString(48);
            bool person = r.ReadBool();
            byte look = r.ReadByte();
            Pb.Sim.Gear.Loadout? kit = KitCodec.Read(ref r);
            m.Roster.Add(new RosterEntry
            {
                PlayerId = id, Team = team, Name = name, Person = person, Look = look, Kit = kit,
                Position = new Vector3(r.ReadFloat(), r.ReadFloat(), r.ReadFloat()), Yaw = r.ReadFloat(),
            });
        }

        return r.Overflowed || count > 64 ? null : m;
    }
}

/// <summary>One player's numbers for the round, as the server kept them.</summary>
public readonly record struct StatsEntry(int PlayerId, int Shots, int Hits, int Eliminations, int Pickups, float TimeIn, int OutTick);

/// <summary>A round over: why, who won, how long it took and everyone's numbers.</summary>
public sealed class RoundOverMessage
{
    public int Round { get; set; }

    public Pb.Sim.Match.MatchResult Result { get; set; }

    public float Elapsed { get; set; }

    public int EndTick { get; set; }

    public List<StatsEntry> Stats { get; } = new();

    public void Write(BitWriter w)
    {
        w.WriteByte((byte)MessageType.RoundOver);
        w.WriteVarUInt((uint)Round);
        w.WriteByte((byte)Result.Reason);
        w.WriteVarInt(Result.Winner);
        w.WriteFloat(Elapsed);
        w.WriteVarInt(EndTick);
        w.WriteVarUInt((uint)Stats.Count);
        foreach (StatsEntry s in Stats)
        {
            w.WriteVarUInt((uint)s.PlayerId);
            w.WriteVarUInt((uint)s.Shots);
            w.WriteVarUInt((uint)s.Hits);
            w.WriteVarUInt((uint)s.Eliminations);
            w.WriteVarUInt((uint)s.Pickups);
            w.WriteFloat(s.TimeIn);
            w.WriteVarInt(s.OutTick);
        }
    }

    /// <summary>A joining copy: puts the server's numbers in its own round, for the summary.</summary>
    public void ApplyTo(Pb.Sim.Match.MatchState match)
    {
        foreach (StatsEntry s in Stats)
        {
            match.StatsFor(s.PlayerId)?.ApplyServer(s.Shots, s.Hits, s.Eliminations, s.Pickups, s.TimeIn, s.OutTick);
        }
    }

    public static RoundOverMessage? Read(ReadOnlySpan<byte> packet)
    {
        var r = new BitReader(packet);
        if ((MessageType)r.ReadByte() != MessageType.RoundOver)
        {
            return null;
        }

        var m = new RoundOverMessage { Round = (int)r.ReadVarUInt() };
        var reason = (Pb.Sim.Match.RoundEnd)r.ReadByte();
        m.Result = new Pb.Sim.Match.MatchResult(reason, r.ReadVarInt());
        m.Elapsed = r.ReadFloat();
        m.EndTick = r.ReadVarInt();
        uint count = r.ReadVarUInt();
        for (uint i = 0; i < count && i < 64 && !r.Overflowed; i++)
        {
            m.Stats.Add(new StatsEntry((int)r.ReadVarUInt(), (int)r.ReadVarUInt(), (int)r.ReadVarUInt(), (int)r.ReadVarUInt(),
                (int)r.ReadVarUInt(), r.ReadFloat(), r.ReadVarInt()));
        }

        return r.Overflowed || count > 64 ? null : m;
    }
}

/// <summary>A one-byte message that names a round (a player has it loaded) or nothing else.</summary>
public static class SimpleMessage
{
    public static void WriteLoaded(BitWriter w, int round)
    {
        w.WriteByte((byte)MessageType.Loaded);
        w.WriteVarUInt((uint)round);
    }

    public static int ReadLoaded(ReadOnlySpan<byte> packet)
    {
        var r = new BitReader(packet);
        if ((MessageType)r.ReadByte() != MessageType.Loaded)
        {
            return -1;
        }

        int round = (int)r.ReadVarUInt();
        return r.Overflowed ? -1 : round;
    }

    public static void WriteBye(BitWriter w, RefusedReason reason, string text)
    {
        w.WriteByte((byte)MessageType.Bye);
        w.WriteByte((byte)reason);
        w.WriteString(text, 200);
    }
}

/// <summary>A player's kit, changed in the gear locker: to the host, which checks it and puts it in the lobby.</summary>
public static class KitMessage
{
    public static void Write(BitWriter w, Pb.Sim.Gear.Loadout kit)
    {
        w.WriteByte((byte)MessageType.Kit);
        KitCodec.Write(w, kit);
    }

    public static Pb.Sim.Gear.Loadout? Read(ReadOnlySpan<byte> packet)
    {
        var r = new BitReader(packet);
        if ((MessageType)r.ReadByte() != MessageType.Kit)
        {
            return null;
        }

        Pb.Sim.Gear.Loadout? kit = KitCodec.Read(ref r);
        return r.Overflowed ? null : kit;
    }
}
