using System.Collections.Generic;
using System.Linq;
using Pb.Game.Core;
using Pb.Game.Ui;
using Pb.Net.Lobby;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Pb.Sim.Match;

namespace Pb.Game.Net;

/// <summary>A lobby's choices as the data has them: the area, the place in it, the mode, size, objective and difficulty.</summary>
public sealed record ChosenRound(AreaEntryDef Entry, LevelLayout Area, PlaceSpec Place, GameMode Mode, int Size, ObjectiveChoice Objective, TierDef Tier)
{
    /// <summary>"Oxbarrow Works" or "Oxbarrow Works: the warehouse".</summary>
    public string Where => Place.Whole ? Entry.DisplayName : $"{Entry.DisplayName}: {Place.DisplayName}";

    /// <summary>"Teams · 3 v 3 · Retrieve · Normal".</summary>
    public string How => new RoundInfo(Area, Tier, Mode, Size, Objective).Line;
}

/// <summary>
/// The host's choices for a game with others: where they start (the menus' last round, or the command line's --level,
/// --place, --mode, --size, --objective and --tier), what they name once checked against the data (anything that
/// doesn't fit falls back: the whole area, the mode's default size, eliminate, Normal), and the places a vote offers.
/// </summary>
public static class RoundChoices
{
    public static LobbyChoices FromMenu(GameData data)
    {
        bool menu = GameSession.LevelId is not null;
        string level = GameSession.LevelId ?? Args.Value("--level") ?? "";
        string? place = menu ? GameSession.PlaceId : Args.Value("--place");
        string mode = GameSession.ModeId ?? Args.Value("--mode") ?? "";
        int size = GameSession.ModeId is not null && GameSession.Size is { } chosen ? chosen : int.TryParse(Args.Value("--size"), out int given) ? given : 0;
        string objective = (menu ? GameSession.ObjectiveId : null) ?? Args.Value("--objective") ?? RecordBook.Eliminate;
        string tier = GameSession.TierId ?? Args.Value("--tier") ?? "normal";
        int raceTo = Args.Ticks("--race-to", 0) ?? 0;
        return Fit(data, new LobbyChoices { LevelId = level, PlaceId = place, ModeId = mode, Size = size, ObjectiveId = objective, TierId = tier, RaceTo = raceTo });
    }

    /// <summary>The choices made to fit the data.</summary>
    public static LobbyChoices Fit(GameData data, LobbyChoices choices)
    {
        ChosenRound r = Resolve(data, choices);
        return choices with
        {
            LevelId = r.Entry.Id, PlaceId = r.Place.Whole ? null : r.Place.Id, ModeId = r.Mode.Id, Size = r.Size,
            ObjectiveId = RecordBook.IdOf(r.Objective.Kind), TierId = r.Tier.Id,
        };
    }

    public static ChosenRound Resolve(GameData data, LobbyChoices c)
    {
        AreaEntryDef[] areas = data.Areas.Areas.Where(a => data.Levels.ContainsKey(a.Id)).ToArray();
        AreaEntryDef entry = areas.FirstOrDefault(a => a.Id == c.LevelId) ?? areas[0];
        LevelLayout area = data.Levels[entry.Id];
        PlaceSpec place = area.Places.FirstOrDefault(p => p.Id == c.PlaceId) ?? area.Places[0];
        MatchRules rules = data.Config.Rules;
        // Only the modes the area offers (the field plays speedball alone); another mode starts at its own size.
        IReadOnlyList<GameMode> modes = rules.ModesFor(entry);
        GameMode mode = modes.FirstOrDefault(m => m.Id == c.ModeId) ?? modes[0];
        bool sameMode = c.ModeId.Length == 0 || mode.Id == c.ModeId;
        int smallest = mode.Kind == MatchModeKind.FreeForAll ? 2 : 1;
        int size = sameMode && c.Size >= smallest && mode.PlayersFor(c.Size) <= rules.MaxPlayers ? c.Size : mode.DefaultSize;
        ObjectiveChoice eliminate = rules.Objectives.Find(ObjectiveKind.Eliminate)!;
        ObjectiveChoice objective = rules.Objectives.Kinds.FirstOrDefault(k => RecordBook.IdOf(k.Kind) == c.ObjectiveId) ?? eliminate;
        if (mode.Kind == MatchModeKind.FreeForAll || mode.Format == MatchFormat.Speedball ||
            (objective.Kind != ObjectiveKind.Eliminate && !area.ForPlace(place).Objectives.Offers(objective.Kind)))
        {
            objective = eliminate;
        }

        TierDef tier = entry.Tiers.FirstOrDefault(t => t.Id == c.TierId) ?? entry.Tiers.FirstOrDefault(t => t.Id == "normal") ?? entry.Tiers[0];
        return new ChosenRound(entry, area, place, mode, size, objective, tier);
    }

    /// <summary>The objectives a place has room for (eliminate always).</summary>
    public static ObjectiveChoice[] Offered(GameData data, LevelLayout area, PlaceSpec place)
    {
        LevelLayout layout = area.ForPlace(place);
        return data.Config.Rules.Objectives.Kinds.Where(k => k.Kind == ObjectiveKind.Eliminate || layout.Objectives.Offers(k.Kind)).ToArray();
    }

    /// <summary>
    /// Places to vote on: up to <paramref name="count"/>, each one the mode and objective can be played in (in an area that
    /// offers the mode), never the one just played, dealt from <paramref name="seed"/>.
    /// </summary>
    public static IReadOnlyList<VoteOption> VoteOptions(GameData data, LobbyChoices choices, int count, ulong seed)
    {
        ChosenRound now = Resolve(data, choices);
        var all = new List<VoteOption>();
        foreach (AreaEntryDef entry in data.Areas.Areas.Where(a => data.Levels.ContainsKey(a.Id)))
        {
            if (!data.Config.Rules.ModesFor(entry).Contains(now.Mode))
            {
                continue;
            }

            LevelLayout area = data.Levels[entry.Id];
            foreach (PlaceSpec place in area.Places)
            {
                if (entry.Id == now.Entry.Id && place.Id == now.Place.Id)
                {
                    continue;
                }

                if (now.Objective.Kind != ObjectiveKind.Eliminate && !area.ForPlace(place).Objectives.Offers(now.Objective.Kind))
                {
                    continue;
                }

                all.Add(new VoteOption(entry.Id, place.Whole ? null : place.Id, place.Whole ? entry.DisplayName : $"{entry.DisplayName}: {place.DisplayName}"));
            }
        }

        var rng = new Pb.Sim.Core.Pcg32(seed, 0x707E);
        for (int i = all.Count - 1; i > 0; i--)
        {
            int j = (int)(rng.NextUInt() % (uint)(i + 1));
            (all[i], all[j]) = (all[j], all[i]);
        }

        return all.Take(count).ToList();
    }
}
