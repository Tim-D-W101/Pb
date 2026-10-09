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

    public int PacketBudget_bytes { get; set; }

    public int MaxPeople { get; set; }

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
        v.InRange(nameof(PacketBudget_bytes), PacketBudget_bytes, 256, 1400);
        v.InRange(nameof(MaxPeople), MaxPeople, 1, 10);
    }
}

/// <summary>How the network behaves, in sim ticks and seconds (from net.jsonc).</summary>
public sealed class NetSettings
{
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

    public required int PacketBudget { get; init; }

    public required int MaxPeople { get; init; }

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
        PacketBudget = d.PacketBudget_bytes,
        MaxPeople = d.MaxPeople,
    };

    public static NetSettings Load(IDataSource source) => From(Jsonc.Load<NetDef>(source, NetDef.File));
}
