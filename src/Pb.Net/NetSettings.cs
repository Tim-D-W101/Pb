using Pb.Sim.Data;

namespace Pb.Net;

/// <summary>net.jsonc as written (unit-suffixed keys).</summary>
public sealed class NetDef : IValidatable
{
    public const string File = "net.jsonc";

    public int Port { get; set; }

    public int DiscoveryPort { get; set; }

    public int SendEveryTicks { get; set; }

    public int CommandsPerPacket { get; set; }

    public int CommandQueueTicks { get; set; }

    public int CommandQueueMostTicks { get; set; }

    public float CommandQueuePatience_ms { get; set; }

    public int CommandAheadTicks { get; set; }

    public float RepeatMissing_ms { get; set; }

    public int InterpolationSnapshots { get; set; }

    public int InterpolationMostSnapshots { get; set; }

    public float Extrapolation_ms { get; set; }

    public float CorrectionTolerance_m { get; set; }

    public float CorrectionSnap_m { get; set; }

    public float CorrectionEase_ms { get; set; }

    public float ConnectTimeout_ms { get; set; }

    public float DropAfter_ms { get; set; }

    public float LinkTimeoutMin_ms { get; set; }

    public float LinkTimeoutMax_ms { get; set; }

    public int PacketBudget_bytes { get; set; }

    public int MaxPeople { get; set; }

    public float LoadTimeout_s { get; set; }

    public float Countdown_s { get; set; }

    public float Briefing_s { get; set; }

    public float Summary_s { get; set; }

    public int VoteOptions { get; set; }

    public float VoteTime_s { get; set; }

    public int ChatMost_chars { get; set; }

    public int ChatLines { get; set; }

    public float ChatPer_s { get; set; }

    public float LobbyRefresh_s { get; set; }

    public float PoorRoundTrip_ms { get; set; }

    public float PoorSilence_ms { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Port), Port, 1024, 65535);
        v.InRange(nameof(DiscoveryPort), DiscoveryPort, 1024, 65535);
        if (Port == DiscoveryPort)
        {
            v.Error(nameof(DiscoveryPort), "must differ from port");
        }

        v.InRange(nameof(SendEveryTicks), SendEveryTicks, 1, 12);
        v.InRange(nameof(CommandsPerPacket), CommandsPerPacket, 1, 15);
        v.InRange(nameof(CommandQueueTicks), CommandQueueTicks, 0, 30);
        v.InRange(nameof(CommandQueueMostTicks), CommandQueueMostTicks, CommandQueueTicks + 1, 120);
        v.InRange(nameof(CommandQueuePatience_ms), CommandQueuePatience_ms, 50, 10000);
        v.InRange(nameof(CommandAheadTicks), CommandAheadTicks, 10, 2400);
        v.InRange(nameof(RepeatMissing_ms), RepeatMissing_ms, 0, 1000);
        v.InRange(nameof(InterpolationSnapshots), InterpolationSnapshots, 1, 10);
        v.InRange(nameof(InterpolationMostSnapshots), InterpolationMostSnapshots, InterpolationSnapshots, 20);
        v.InRange(nameof(Extrapolation_ms), Extrapolation_ms, 0, 1000);
        v.InRange(nameof(CorrectionTolerance_m), CorrectionTolerance_m, 0, 0.1);
        v.InRange(nameof(CorrectionSnap_m), CorrectionSnap_m, 0.01, 10);
        v.InRange(nameof(CorrectionEase_ms), CorrectionEase_ms, 0, 1000);
        v.InRange(nameof(ConnectTimeout_ms), ConnectTimeout_ms, 500, 120000);
        v.InRange(nameof(DropAfter_ms), DropAfter_ms, 500, 120000);
        v.InRange(nameof(LinkTimeoutMin_ms), LinkTimeoutMin_ms, 1000, 120000);
        v.InRange(nameof(LinkTimeoutMax_ms), LinkTimeoutMax_ms, LinkTimeoutMin_ms, 300000);
        v.InRange(nameof(PacketBudget_bytes), PacketBudget_bytes, 256, 1400);
        v.InRange(nameof(MaxPeople), MaxPeople, 1, 10);
        v.InRange(nameof(LoadTimeout_s), LoadTimeout_s, 1, 600);
        v.InRange(nameof(Countdown_s), Countdown_s, 0, 60);
        v.InRange(nameof(Briefing_s), Briefing_s, 0, 60);
        v.InRange(nameof(Summary_s), Summary_s, 0, 600);
        v.InRange(nameof(VoteOptions), VoteOptions, 2, 8);
        v.InRange(nameof(VoteTime_s), VoteTime_s, 3, 120);
        v.InRange(nameof(ChatMost_chars), ChatMost_chars, 10, 200);
        v.InRange(nameof(ChatLines), ChatLines, 1, 20);
        v.InRange(nameof(ChatPer_s), ChatPer_s, 1, 60);
        v.InRange(nameof(LobbyRefresh_s), LobbyRefresh_s, 0.25, 30);
        v.InRange(nameof(PoorRoundTrip_ms), PoorRoundTrip_ms, 50, 5000);
        v.InRange(nameof(PoorSilence_ms), PoorSilence_ms, 50, 10000);
    }
}

/// <summary>How the network behaves, in sim ticks and seconds (from net.jsonc).</summary>
public sealed class NetSettings
{
    private int _maxPeople;

    public required int Port { get; init; }

    public required int DiscoveryPort { get; init; }

    public required int SendEveryTicks { get; init; }

    public required int CommandsPerPacket { get; init; }

    public required int CommandQueueTicks { get; init; }

    public required int CommandQueueMostTicks { get; init; }

    public required float CommandQueuePatience { get; init; }

    public required int CommandAheadTicks { get; init; }

    public required float RepeatMissing { get; init; }

    public required int InterpolationSnapshots { get; init; }

    public required int InterpolationMostSnapshots { get; init; }

    public required float Extrapolation { get; init; }

    public required float CorrectionTolerance { get; init; }

    public required float CorrectionSnap { get; init; }

    public required float CorrectionEase { get; init; }

    public required float ConnectTimeout { get; init; }

    public required float DropAfter { get; init; }

    /// <summary>How long the connection waits at the least and at the most for a copy to confirm what it was sent (s).</summary>
    public required float LinkTimeoutMin { get; init; }

    public required float LinkTimeoutMax { get; init; }

    public required int PacketBudget { get; init; }

    public required int MaxPeople { get => _maxPeople; init => _maxPeople = value; }

    /// <summary>The lobby's countdown (s), the longest a round waits for everyone to have it built (s), and its briefing after (s).</summary>
    public required float Countdown { get; init; }

    public required float LoadTimeout { get; init; }

    public required float Briefing { get; init; }

    /// <summary>How long the summary stays up before the host goes back to the lobby (s; 0: until the host does).</summary>
    public required float Summary { get; init; }

    /// <summary>The vote: how many places it offers, and for how long (s).</summary>
    public required int VoteOptions { get; init; }

    public required float VoteTime { get; init; }

    /// <summary>Chat: the longest line (characters), and at most this many lines in this long (s).</summary>
    public required int ChatMostChars { get; init; }

    public required int ChatLines { get; init; }

    public required float ChatPer { get; init; }

    /// <summary>During a round, the lobby goes out again this often (s).</summary>
    public required float LobbyRefresh { get; init; }

    /// <summary>A joining copy's warnings: the connection's poor past this round trip (s), interrupted after this long without a word (s).</summary>
    public required float PoorRoundTrip { get; init; }

    public required float PoorSilence { get; init; }

    public static NetSettings From(NetDef d) => new()
    {
        Port = d.Port,
        DiscoveryPort = d.DiscoveryPort,
        SendEveryTicks = d.SendEveryTicks,
        CommandsPerPacket = d.CommandsPerPacket,
        CommandQueueTicks = d.CommandQueueTicks,
        CommandQueueMostTicks = d.CommandQueueMostTicks,
        CommandQueuePatience = d.CommandQueuePatience_ms / 1000f,
        CommandAheadTicks = d.CommandAheadTicks,
        RepeatMissing = d.RepeatMissing_ms / 1000f,
        InterpolationSnapshots = d.InterpolationSnapshots,
        InterpolationMostSnapshots = d.InterpolationMostSnapshots,
        Extrapolation = d.Extrapolation_ms / 1000f,
        CorrectionTolerance = d.CorrectionTolerance_m,
        CorrectionSnap = d.CorrectionSnap_m,
        CorrectionEase = d.CorrectionEase_ms / 1000f,
        ConnectTimeout = d.ConnectTimeout_ms / 1000f,
        DropAfter = d.DropAfter_ms / 1000f,
        LinkTimeoutMin = d.LinkTimeoutMin_ms / 1000f,
        LinkTimeoutMax = d.LinkTimeoutMax_ms / 1000f,
        PacketBudget = d.PacketBudget_bytes,
        MaxPeople = d.MaxPeople,
        Countdown = d.Countdown_s,
        LoadTimeout = d.LoadTimeout_s,
        Briefing = d.Briefing_s,
        Summary = d.Summary_s,
        VoteOptions = d.VoteOptions,
        VoteTime = d.VoteTime_s,
        ChatMostChars = d.ChatMost_chars,
        ChatLines = d.ChatLines,
        ChatPer = d.ChatPer_s,
        LobbyRefresh = d.LobbyRefresh_s,
        PoorRoundTrip = d.PoorRoundTrip_ms / 1000f,
        PoorSilence = d.PoorSilence_ms / 1000f,
    };

    public static NetSettings Load(IDataSource source) => From(Jsonc.Load<NetDef>(source, NetDef.File));

    /// <summary>The same settings taking at most <paramref name="people"/> people (a dedicated server's own limit).</summary>
    public NetSettings WithMaxPeople(int people)
    {
        var copy = (NetSettings)MemberwiseClone();
        copy._maxPeople = Math.Clamp(people, 1, MaxPeople);
        return copy;
    }
}
