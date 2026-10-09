using Pb.Net.Protocol;
using Pb.Sim.Players;

namespace Pb.Net.Server;

/// <summary>A player connected to a server: who they are, their commands, what they've confirmed, their own state.</summary>
public sealed class ClientLink
{
    /// <summary>How many sent snapshots are kept, to send the next as a difference from one the player confirmed.</summary>
    internal const int Ring = 64;

    internal ClientLink(int peer, NetSettings settings, float dt, double now)
    {
        Peer = peer;
        Settings = settings;
        Dt = dt;
        Commands = new CommandQueue(settings, dt);
        LastHeard = now;
        for (int i = 0; i < Ring; i++)
        {
            Sent[i] = new OwnRecord();
        }
    }

    public int Peer { get; }

    /// <summary>The name they play under (unique in the game).</summary>
    public string Name { get; internal set; } = "";

    /// <summary>Which of the characters they play.</summary>
    public byte Look { get; internal set; }

    /// <summary>Who they are: their identity's id, the same each time they join (empty if their copy didn't say).</summary>
    public string Key { get; internal set; } = "";

    /// <summary>Let in: past the version and password checks.</summary>
    public bool Welcomed { get; internal set; }

    /// <summary>Their player in the round under way (−1: not playing in it, or none under way).</summary>
    public int PlayerId { get; internal set; } = -1;

    /// <summary>The last round they've said they have built.</summary>
    public int LoadedRound { get; internal set; } = -1;

    public CommandQueue Commands { get; internal set; }

    /// <summary>The round trip to them (s), from how long their confirmations take to come back.</summary>
    public float RoundTrip { get; internal set; }

    /// <summary>Bytes sent to them so far, and in the last second.</summary>
    public long BytesSent { get; internal set; }

    public float BytesPerSecond { get; internal set; }

    /// <summary>Things they sent that the server wouldn't take (logged by the host).</summary>
    public int Violations { get; internal set; }

    /// <summary>How many of their commands had been run two in a tick when that was last logged.</summary>
    internal int MergesLogged { get; set; }

    internal NetSettings Settings { get; }

    internal float Dt { get; }

    internal double LastHeard { get; set; }

    /// <summary>The newest snapshot tick they've confirmed (−1: none this round), and the events they have up to.</summary>
    internal int AckedTick { get; set; } = -1;

    internal uint AckedEvent { get; set; }

    /// <summary>When their newest command arrived (for their round-trip time), and its sequence.</summary>
    internal double NewestAt { get; set; }

    internal int NewestSeen { get; set; } = -1;

    internal OwnRecord[] Sent { get; } = new OwnRecord[Ring];

    internal long SecondBytes { get; set; }

    internal double SecondFrom { get; set; }

    internal void ResetForRound()
    {
        Commands = new CommandQueue(Settings, Dt);
        MergesLogged = 0;
        AckedTick = -1;
        AckedEvent = 0;
        NewestSeen = -1;
        foreach (OwnRecord r in Sent)
        {
            r.Tick = -1;
        }
    }

    internal OwnRecord? Record(int tick)
    {
        OwnRecord r = Sent[(tick / Math.Max(1, Settings.SendEveryTicks)) % Ring];
        return r.Tick == tick ? r : null;
    }

    internal OwnRecord Slot(int tick) => Sent[(tick / Math.Max(1, Settings.SendEveryTicks)) % Ring];
}

/// <summary>A snapshot as sent to one player: its tick, their own state's fields, the last event it took account of, when.</summary>
internal sealed class OwnRecord
{
    public int Tick = -1;

    public bool HasOwn;

    public readonly uint[] Own = new uint[OwnFields.Count];

    public uint LastEvent;

    public double SentAt;
}
