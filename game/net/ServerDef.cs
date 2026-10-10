using System;
using Pb.Sim.Data;

namespace Pb.Game.Net;

/// <summary>One round of the dedicated server's rotation, as server.jsonc has it.</summary>
public sealed class RotationDef : IValidatable
{
    public string Level { get; set; } = "";

    public string? Place { get; set; }

    public string Mode { get; set; } = "";

    public int Size { get; set; }

    public string Objective { get; set; } = "eliminate";

    public string Tier { get; set; } = "normal";

    /// <summary>Speedball: the points a side needs to win the match (0, or left out: the rules' own).</summary>
    [Optional]
    public int RaceTo { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(RaceTo), RaceTo, 0, 20);
        if (Level.Length == 0)
        {
            v.Error(nameof(Level), "needs an area's id");
        }

        if (Mode.Length == 0)
        {
            v.Error(nameof(Mode), "needs a mode's id");
        }
    }
}

/// <summary>server.jsonc as written.</summary>
public sealed class ServerDef : IValidatable
{
    public const string File = "server.jsonc";

    public string Name { get; set; } = "";

    public int Port { get; set; }

    public string Password { get; set; } = "";

    public int MaxPeople { get; set; }

    public RotationDef[] Rotation { get; set; } = Array.Empty<RotationDef>();

    public bool Bots { get; set; } = true;

    public bool Vote { get; set; }

    public float LobbyWait_s { get; set; }

    public float Summary_s { get; set; }

    public void Validate(Validator v)
    {
        if (Name.Trim().Length == 0)
        {
            v.Error(nameof(Name), "needs a name");
        }

        v.InRange(nameof(Port), Port, 1024, 65535);
        v.InRange(nameof(MaxPeople), MaxPeople, 1, 10);
        v.InRange(nameof(LobbyWait_s), LobbyWait_s, 0, 3600);
        v.InRange(nameof(Summary_s), Summary_s, 0, 600);
        if (Rotation.Length == 0)
        {
            v.Error(nameof(Rotation), "needs at least one round");
        }

        for (int i = 0; i < Rotation.Length; i++)
        {
            Rotation[i].Validate(v.Item(nameof(Rotation), i));
        }
    }
}
