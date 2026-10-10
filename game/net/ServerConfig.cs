using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Pb.Game.Core;
using Pb.Net.Lobby;
using Pb.Sim.Data;

namespace Pb.Game.Net;

/// <summary>
/// The dedicated server's settings: server.jsonc from --server-config=PATH, else the one beside the program, else the
/// game's own (res://data/server.jsonc). Each round in the rotation is checked against the data (anything that
/// doesn't fit falls back, as the lobby's choices do).
/// </summary>
public sealed class ServerConfig
{
    public required string Name { get; init; }

    public required int Port { get; init; }

    public required string Password { get; init; }

    public required int MaxPeople { get; init; }

    public required IReadOnlyList<LobbyChoices> Rotation { get; init; }

    public required bool Bots { get; init; }

    public required bool Vote { get; init; }

    public required float LobbyWait { get; init; }

    public required float Summary { get; init; }

    /// <summary>Where it was read from.</summary>
    public required string Source { get; init; }

    /// <summary>What the rotation asked for that the data doesn't have, and what's played instead, for the log.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    public static ServerConfig Load(GameData data)
    {
        (IDataSource source, string where) = Find();
        ServerDef def = Jsonc.Load<ServerDef>(source, ServerDef.File);
        var rotation = new LobbyChoices[def.Rotation.Length];
        var warnings = new List<string>();
        for (int i = 0; i < rotation.Length; i++)
        {
            RotationDef r = def.Rotation[i];
            rotation[i] = RoundChoices.Fit(data, new LobbyChoices
            {
                LevelId = r.Level, PlaceId = r.Place, ModeId = r.Mode, Size = r.Size, ObjectiveId = r.Objective, TierId = r.Tier, Vote = def.Vote,
                RaceTo = r.RaceTo,
            });
            warnings.AddRange(Unfitted(i + 1, r, rotation[i]));
        }

        return new ServerConfig
        {
            Name = def.Name.Trim(), Port = def.Port, Password = def.Password, MaxPeople = def.MaxPeople, Rotation = rotation, Bots = def.Bots,
            Vote = def.Vote, LobbyWait = def.LobbyWait_s, Summary = def.Summary_s, Source = where, Warnings = warnings,
        };
    }

    /// <summary>What round <paramref name="n"/> of the rotation asked for that it doesn't get (a misspelt id, a size too big).</summary>
    private static IEnumerable<string> Unfitted(int n, RotationDef asked, LobbyChoices played)
    {
        if (asked.Level != played.LevelId)
        {
            yield return $"round {n}: there's no area \"{asked.Level}\", so it's {played.LevelId}";
        }
        else if (asked.Place is { Length: > 0 } place && place != "whole" && place != played.PlaceId)
        {
            yield return $"round {n}: {asked.Level} has no place \"{place}\", so it's the whole area";
        }

        if (asked.Mode != played.ModeId)
        {
            yield return $"round {n}: {played.LevelId} has no mode \"{asked.Mode}\", so it's {played.ModeId}";
        }
        else if (asked.Size != played.Size)
        {
            yield return $"round {n}: size {asked.Size} doesn't fit ten players, so it's {played.Size}";
        }

        if (asked.Objective != played.ObjectiveId)
        {
            yield return $"round {n}: {asked.Objective} can't be played there, so it's {played.ObjectiveId}";
        }

        if (asked.Tier != played.TierId)
        {
            yield return $"round {n}: there's no difficulty \"{asked.Tier}\", so it's {played.TierId}";
        }
    }

    private static (IDataSource Source, string Where) Find()
    {
        if (Args.Value("--server-config") is { Length: > 0 } given)
        {
            string full = Path.GetFullPath(given);
            return (new OneFileSource(full), full);
        }

        string beside = Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".", ServerDef.File);
        if (OS.HasFeature("template") && System.IO.File.Exists(beside))
        {
            return (new OneFileSource(beside), beside);
        }

        return (new GodotDataSource(), GodotDataSource.Root + ServerDef.File);
    }

    /// <summary>Reads one file wherever it is, whatever it's asked for by name.</summary>
    private sealed class OneFileSource : IDataSource
    {
        private readonly string _path;

        public OneFileSource(string path)
        {
            _path = path;
        }

        public string ReadAllText(string path) =>
            System.IO.File.Exists(_path) ? System.IO.File.ReadAllText(_path) : throw new DataException(_path, "file not found");
    }
}
