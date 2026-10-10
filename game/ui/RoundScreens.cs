using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Pb.Sim.Match;

namespace Pb.Game.Ui;

/// <summary>What a round is: the level (for the place in it), its difficulty tier, the mode, the size and the objective.</summary>
public sealed record RoundInfo(LevelLayout Level, TierDef Tier, GameMode Mode, int Size, ObjectiveChoice Objective)
{
    /// <summary>"Free-for-all · 8 players · Normal", "Teams · 3 v 3 · Retrieve · Hard".</summary>
    public string Line => Objective.Kind == ObjectiveKind.Eliminate
        ? $"{Mode.DisplayName} · {ModeText.Size(Mode, Size)} · {Tier.DisplayName}"
        : $"{Mode.DisplayName} · {ModeText.Size(Mode, Size)} · {Objective.DisplayName} · {Tier.DisplayName}";

    /// <summary>The part of the area the round is in, or null for the whole of it.</summary>
    public PlaceSpec? Place => Level.Place is { Whole: false } place ? place : null;

    /// <summary>"Oxbarrow Works" or "Oxbarrow Works: the warehouse".</summary>
    public string Where => Place is { } place ? $"{Level.DisplayName}: {place.DisplayName}" : Level.DisplayName;

    /// <summary>The clock it's played on (s), once the round is set up: a point of a match has its mode's, not the tier's.</summary>
    public float? TimeLimit { get; init; }
}

/// <summary>How the round stands for the summary, worked out by the level from the sim.</summary>
public sealed record SummaryFacts
{
    /// <summary>The round is over (false when you skip ahead to the summary while the others play on).</summary>
    public required bool Over { get; init; }

    /// <summary>How it ended for your side (<see cref="MatchState.OutcomeFor"/>).</summary>
    public required RoundOutcome Outcome { get; init; }

    public required bool YouAreOut { get; init; }

    /// <summary>Who got you, if anyone did: "Magpie got you: mask, 25 m."</summary>
    public string? HitBy { get; init; }

    /// <summary>Everyone against you (or, in free-for-all, everyone else): how many you could put out.</summary>
    public required int Opponents { get; init; }

    /// <summary>Your team (you included) and the others still in.</summary>
    public required int OursLeft { get; init; }

    public required int OthersLeft { get; init; }

    /// <summary>Free-for-all: where you finished, out of how many, and who won if the round is over.</summary>
    public int Placing { get; init; }

    public int Players { get; init; }

    public string? Winner { get; init; }

    /// <summary>How the objective went, in a sentence (for the headline's line), if the round had one.</summary>
    public string? ObjectiveLine { get; init; }

    /// <summary>The objective's row in the numbers: "Case" or "Held", and how it ended.</summary>
    public (string Name, string Value)? ObjectiveRow { get; init; }
}

/// <summary>Builds the round's overlays: the briefing card before it starts and the summary after it ends.</summary>
public static class RoundScreens
{
    /// <summary>
    /// The briefing card (the sim waits in its briefing phase meanwhile): level, mode, size, objective, difficulty,
    /// what to do, and Start (Enter, Space or a click begins the round) or Back to level select; with a
    /// plan of the level beside it if one is given.
    /// </summary>
    /// <param name="waiting">
    /// Playing with others: instead of Start, this line (a label named "Status" the level keeps up to date: who it's
    /// waiting for, the countdown), and Back becomes Leave.
    /// </param>
    /// <param name="race">A match of points: the points a side needs to win it.</param>
    public static Control Briefing(RoundInfo round, Action start, Action back, Control? map = null, ObjectiveState? objective = null,
        string? waiting = null, int? race = null)
    {
        TierDef tier = round.Tier;
        VBoxContainer column = UiKit.Column(12);
        column.AddChild(UiKit.Body("BRIEFING", 18, UiKit.Accent));
        column.AddChild(UiKit.Title(round.Level.DisplayName, 44));
        if (round.Place is { } place)
        {
            column.AddChild(UiKit.Title(place.DisplayName, 30, UiKit.Accent));
        }

        string clock = round.Mode.Format.IsMatch() && race is { } to
            ? $"first to {to} points, {Clock(round.TimeLimit ?? tier.TimeLimit_s)} a point"
            : $"{Clock(round.TimeLimit ?? tier.TimeLimit_s)} on the clock";
        column.AddChild(UiKit.Body($"{round.Line} · {clock}", 22, UiKit.Dim));
        Label description = UiKit.Body(round.Place?.Description ?? round.Level.Description, 19, UiKit.Text, wrap: true);
        description.CustomMinimumSize = new Vector2(720, 0);
        column.AddChild(description);
        column.AddChild(new HSeparator());
        var lines = new List<string>(objective is null ? Goal(round, race) : Goal(round.Mode, objective))
        {
            "One hit and you're out, them too. A bounce doesn't count.",
            $"You start with a full loader and {tier.StartPods} pod{(tier.StartPods == 1 ? "" : "s")}. " + Pickups(round),
            round.Mode.Format.IsMatch() && round.Level.Field is not null
                ? "Sprint for a bunker at the horn, crouch in behind it, lean (Q/E) out round its edges, and swap shoulders (X) for left-hand ones."
                : "Crouch to move quietly, lean (Q/E) round corners, and swap shoulders (X) for left-hand edges.",
        };
        if (round.Place is not null && round.Level.Field is null)
        {
            lines.Insert(lines.Count - 1, "The round stays inside the boundary tape: you can't walk out, but paint flies over it.");
        }
        foreach (string line in lines)
        {
            Label l = UiKit.Body("·  " + line, 18, UiKit.Text, wrap: true);
            l.CustomMinimumSize = new Vector2(720, 0);
            column.AddChild(l);
        }

        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        HBoxContainer buttons = UiKit.Row(12);
        Button? go = null;
        if (waiting is null)
        {
            go = UiKit.Button("Start", start, 240);
            go.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            buttons.AddChild(go);
        }
        else
        {
            Label status = UiKit.Body(waiting, 20, UiKit.Accent);
            status.Name = "Status";
            status.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            buttons.AddChild(status);
        }

        Button leave = UiKit.Button(waiting is null ? "Back" : "Leave", back, 200);
        buttons.AddChild(leave);
        column.AddChild(buttons);
        Control content = column;
        float width = 800f;
        if (map is not null)
        {
            HBoxContainer row = UiKit.Row(24);
            row.AddChild(column);
            map.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(map);
            content = row;
            width += map.CustomMinimumSize.X + 24f;
        }

        Control overlay = UiKit.Overlay(UiKit.Panel(content, width), dim: 0.35f);
        // Waiting for others, nothing has the focus: a press of Space or Enter mustn't leave the game.
        go?.CallDeferred(Control.MethodName.GrabFocus);
        return overlay;
    }

    /// <summary>What's lying around to pick up where the round is.</summary>
    private static string Pickups(RoundInfo round)
    {
        bool pods = round.Tier.Pickups && round.Level.Pickups.Any(p => p.Kind == PickupKind.Pod);
        bool air = round.Tier.Pickups && round.Level.Pickups.Any(p => p.Kind == PickupKind.Air);
        return (pods, air) switch
        {
            (true, true) => "Paint pods and air are lying around.",
            (true, false) => "Paint pods are lying around, but no air.",
            (false, true) => "There's air lying around, but no paint pods.",
            _ => "There are no pickups.",
        };
    }

    private static string[] Goal(RoundInfo round, int? race) => round.Mode.Format == MatchFormat.Flag ? FlagGoal(round, race)
        : round.Mode.Format == MatchFormat.Speedball ? new[]
        {
            (race is { } to ? $"Your side against theirs, point after point: the first side to {to} point{(to == 1 ? "" : "s")} wins the match. "
                : "Your side against theirs, point after point. ") +
            "Each point starts from the start boxes at the horn, after a countdown.",
            "Win a point by putting the whole other side out, or by hanging their buzzer: hold Interact (F) at the post in their start box. " +
            "At time up the side with more players still in takes it.",
            "Nobody leaves the field: the nets hold you in. A teammate's paint puts you out too.",
        }
        : round.Mode.Kind switch
    {
        MatchModeKind.FreeForAll => new[]
        {
            "Everyone is against everyone, each in their own paint colour: be the last one standing.",
            "The bots fight each other too. When you're out you can watch the rest, or skip to the summary.",
        },
        MatchModeKind.Teams => new[]
        {
            "Your team against theirs: the last team standing wins. Your teammates start beside you and wear your colour.",
            "A teammate's paint puts you out too, so watch your fire. When you're out you can watch your team play on.",
        },
        _ => new[]
        {
            $"Clear {(round.Place is { } place ? char.ToLowerInvariant(place.DisplayName[0]) + place.DisplayName[1..] : "the compound")}: " +
            "eliminate every opponent before the time runs out.",
        },
    };

    /// <summary>Capture the flag: one flag in the middle of a field, or one each elsewhere.</summary>
    private static string[] FlagGoal(RoundInfo round, int? race)
    {
        string match = race is { } to ? $"Your side against theirs, point after point: the first side to {to} point{(to == 1 ? "" : "s")} wins the match. "
            : "Your side against theirs, point after point. ";
        return round.Level.Field is not null
            ? new[]
            {
                match + "Each point starts from the start boxes at the horn, after a countdown.",
                "One flag stands on the centre bunker: take it (walk up to it) and carry it to their buzzer, in their start box. " +
                "You can't sprint with it, and if you're hit it drops where you fall, for either side to take.",
                "Putting the whole other side out wins the point too; at time up the side with more players still in takes it.",
            }
            : new[]
            {
                match + "Each point starts at the horn, after a countdown, with your side at your base.",
                "Your flag stands at your base and theirs at theirs: take theirs (walk up to it) and bring it to your base, and keep them off " +
                "yours. You can't sprint with a flag, and if its carrier's hit it drops where they fall.",
                "Putting the whole other side out wins the point too; at time up the side with more players still in takes it.",
            };
    }

    /// <summary>What to do in a round with an objective, with where it is.</summary>
    private static string[] Goal(GameMode mode, ObjectiveState objective)
    {
        string team = mode.Kind == MatchModeKind.Teams
            ? " Your teammates start beside you in your colour, and a teammate's paint puts you out too."
            : "";
        if (objective.Kind == ObjectiveKind.Retrieve)
        {
            string ways = string.Join(", ", objective.Level.Exits.Select(e => e.Name).Distinct());
            return new[]
            {
                $"The case is somewhere in {ModeText.The(objective.Spot.Area)} (marked on your screen). Walk over it to pick it up and carry it out: {ways}.",
                "Whoever carries it can't sprint, and if they're hit it falls where they were, for a teammate to pick up.",
                "Its holders guard it, and once it's gone they know where it is. Putting them all out wins too." + team,
            };
        }

        return new[]
        {
            $"Take {ModeText.The(objective.Room!.Name)} (marked) and hold it for {Clock(objective.HoldRules.HoldTime)} in all. The clock runs only while your side is in it and theirs isn't, and never runs back.",
            "They start in and round it, and come for it once you're in. Putting them all out wins too." + team,
        };
    }

    /// <summary>The summary: how the round went from your side, your numbers, and what next.</summary>
    /// <param name="actions">Playing with others: these buttons instead of Retry, Level select and Main menu.</param>
    /// <param name="note">Playing with others: a line under the numbers (what happens next).</param>
    /// <param name="points">A match of points: its result heads the summary, and its numbers over all its points fill it.</param>
    /// <param name="team">Your side in <paramref name="points"/>.</param>
    public static Control Summary(RoundInfo round, MatchState match, PlayerStats you, SummaryFacts facts, Action retry, Action levelSelect,
        Action mainMenu, IReadOnlyList<(string Text, Action Act)>? actions = null, string? note = null, PointsMatch? points = null,
        int team = 0)
    {
        (string title, Color colour, string line) = Verdict(round.Mode, facts.Outcome, facts);
        if (points is { Series: { Done: true } series })
        {
            bool won = series.Winner == team;
            title = won ? "MATCH WON" : "MATCH LOST";
            colour = won ? UiKit.Good : UiKit.Bad;
            line = $"{series.PointsOf(team)}–{series.PointsOf(1 - team)}, first to {series.RaceTo}. The last point: {char.ToLowerInvariant(line[0])}{line[1..]}";
        }

        VBoxContainer column = UiKit.Column(12);
        column.AddChild(UiKit.Body($"{round.Where} · {round.Line}", 20, UiKit.Dim));
        column.AddChild(UiKit.Title(title, 56, colour));
        Label said = UiKit.Body(line, 20, UiKit.Text, wrap: true);
        said.CustomMinimumSize = new Vector2(580, 0);
        column.AddChild(said);

        column.AddChild(new HSeparator());

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 40);
        grid.AddThemeConstantOverride("v_separation", 8);
        var rows = new List<(string, string)>();
        if (round.Mode.Kind == MatchModeKind.FreeForAll && facts.Placing > 0)
        {
            rows.Add(("Placing", $"{ModeText.Ordinal(facts.Placing)} of {facts.Players}"));
        }

        if (points is { Totals: true } whole)
        {
            // The whole match's numbers, over every point of it.
            rows.Add(("Points", $"{whole.Series.PointsOf(team)}–{whole.Series.PointsOf(1 - team)} in {whole.Series.Played}"));
            rows.Add(("Time", Clock(whole.Time)));
            rows.Add(("Eliminations", whole.Eliminations.ToString()));
            rows.Add(("Shots", whole.Shots.ToString()));
            rows.Add(("Hits", whole.Hits.ToString()));
            rows.Add(("Accuracy", whole.Shots > 0 ? $"{whole.Hits / (float)whole.Shots * 100:0}%" : "–"));
        }
        else
        {
            if (points is { } played)
            {
                rows.Add(("Points", $"{played.Series.PointsOf(team)}–{played.Series.PointsOf(1 - team)} in {played.Series.Played}"));
            }

            rows.Add((points is null ? "Time" : "Last point", Clock(match.Elapsed)));
            if (facts.ObjectiveRow is { } objectiveRow)
            {
                rows.Add(objectiveRow);
            }

            rows.Add(("Eliminations", $"{you.Eliminations} of {facts.Opponents}"));
            rows.Add(("Shots", you.Shots.ToString()));
            rows.Add(("Hits", you.Hits.ToString()));
            rows.Add(("Accuracy", you.Shots > 0 ? $"{you.Accuracy * 100:0}%" : "–"));
            rows.Add(("Pickups", you.Pickups.ToString()));
        }
        foreach ((string name, string value) in rows)
        {
            grid.AddChild(UiKit.Body(name, 22, UiKit.Dim));
            grid.AddChild(UiKit.Body(value, 22));
        }

        column.AddChild(grid);
        if (note is not null)
        {
            Label status = UiKit.Body(note, 18, UiKit.Dim, wrap: true);
            status.Name = "Status";
            status.CustomMinimumSize = new Vector2(580, 0);
            column.AddChild(status);
        }

        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        HBoxContainer buttons = UiKit.Row(12);
        Button? first = null;
        foreach ((string text, Action act) in actions ?? new (string, Action)[] { ("Retry", retry), ("Level select", levelSelect), ("Main menu", mainMenu) })
        {
            Button b = UiKit.Button(text, act, 200);
            first ??= b;
            buttons.AddChild(b);
        }

        column.AddChild(buttons);
        Control overlay = UiKit.Overlay(UiKit.Panel(column, 660f));
        first?.CallDeferred(Control.MethodName.GrabFocus);
        return overlay;
    }

    /// <summary>
    /// Between a match's points: who took the point and how (<paramref name="line"/>), the score, and the next
    /// point's countdown (the level reloads for it once <paramref name="wait"/> has passed).
    /// </summary>
    public static Control BetweenPoints(RoundInfo round, PointsMatch match, int team, string line, float wait)
    {
        MatchSeries series = match.Series;
        int winner = match.Last.Winner;
        (string title, Color colour) = winner == team ? ("YOUR POINT", UiKit.Good)
            : winner is 0 or 1 ? ("THEIR POINT", UiKit.Bad)
            : ("NO POINT", UiKit.Accent);

        VBoxContainer column = UiKit.Column(12);
        column.AddChild(UiKit.Body($"{round.Where} · {round.Line} · point {series.Played}", 20, UiKit.Dim));
        column.AddChild(UiKit.Title(title, 56, colour));
        Label said = UiKit.Body(line, 20, UiKit.Text, wrap: true);
        said.CustomMinimumSize = new Vector2(580, 0);
        column.AddChild(said);
        column.AddChild(new HSeparator());

        HBoxContainer score = UiKit.Row(28);
        score.Alignment = BoxContainer.AlignmentMode.Center;
        score.AddChild(UiKit.Body("YOURS", 22, UiKit.Dim));
        score.AddChild(UiKit.Title($"{series.PointsOf(team)}  –  {series.PointsOf(1 - team)}", 72));
        score.AddChild(UiKit.Body("THEIRS", 22, UiKit.Dim));
        column.AddChild(score);
        Label to = UiKit.Body($"First to {series.RaceTo}", 20, UiKit.Dim);
        to.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(to);

        Label next = UiKit.Body("", 22, UiKit.Accent);
        next.Name = "Status";
        next.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(next);
        Control overlay = UiKit.Overlay(UiKit.Panel(column, 660f), dim: 0.35f);
        // The countdown to the next point, kept up by a timer of its own (in the game's time, as the reload's timer is).
        const float step = 0.2f;
        float left = wait;
        void Count() => next.Text = $"Point {series.Played + 1} in {Math.Max(1, (int)Math.Ceiling(left))}…";
        Count();
        var tick = new Timer { WaitTime = step, Autostart = true };
        tick.Timeout += () =>
        {
            left -= step;
            Count();
        };
        overlay.AddChild(tick);
        return overlay;
    }

    /// <summary>The summary's headline, its colour, and a line saying what happened.</summary>
    private static (string Title, Color Colour, string Line) Verdict(GameMode mode, RoundOutcome outcome, SummaryFacts f)
    {
        string hit = f.HitBy is null ? "" : " " + f.HitBy;
        if (!f.Over)
        {
            // Skipped ahead while the others play on.
            return mode.Kind == MatchModeKind.FreeForAll
                ? ("ELIMINATED", UiKit.Bad, $"You placed {ModeText.Ordinal(f.Placing)} of {f.Players}; {f.OthersLeft} still in.{hit}")
                : ("YOU'RE OUT", UiKit.Bad, $"Your team fights on: {f.OursLeft} of yours against {f.OthersLeft}.{hit}");
        }

        if (outcome == RoundOutcome.Extracted)
        {
            return ("CASE RETRIEVED", UiKit.Good, f.ObjectiveLine ?? "The case is out.");
        }

        if (outcome == RoundOutcome.Held)
        {
            return ("ROOM HELD", UiKit.Good, f.ObjectiveLine ?? "The room is yours.");
        }

        if (outcome == RoundOutcome.TimeUp && f.ObjectiveLine is { } how)
        {
            return ("TIME UP", UiKit.Bad, how);
        }

        // Defending an objective against people (online), and the ways to win a point of a match.
        switch (outcome)
        {
            case RoundOutcome.BuzzerHung:
                return ("BUZZER HUNG", UiKit.Good, f.ObjectiveLine ?? "Your side hung their buzzer.");
            case RoundOutcome.BuzzerLost:
                return ("BUZZER LOST", UiKit.Bad, f.ObjectiveLine ?? "They hung your buzzer.");
            case RoundOutcome.FlagCaptured:
                return ("FLAG CAPTURED", UiKit.Good, f.ObjectiveLine ?? "Your side captured the flag.");
            case RoundOutcome.FlagLost:
                return ("FLAG LOST", UiKit.Bad, f.ObjectiveLine ?? "They captured the flag.");
            case RoundOutcome.AheadAtTime:
                return ("AHEAD AT TIME", UiKit.Good, $"Time up with {f.OursLeft} of yours against {f.OthersLeft} of theirs still in.");
            case RoundOutcome.BehindAtTime:
                return ("BEHIND AT TIME", UiKit.Bad, $"Time up with {f.OursLeft} of yours against {f.OthersLeft} of theirs still in.");
            case RoundOutcome.HeldOff:
                return ("HELD THEM OFF", UiKit.Good, "The clock ran out before the other side could do it.");
            case RoundOutcome.CaseLost:
                return ("CASE LOST", UiKit.Bad, f.ObjectiveLine ?? "The other side carried the case out.");
            case RoundOutcome.RoomLost:
                return ("ROOM LOST", UiKit.Bad, f.ObjectiveLine ?? "The other side held the room.");
        }

        return (mode.Kind, outcome) switch
        {
            (MatchModeKind.FreeForAll, RoundOutcome.Cleared) => ("LAST ONE STANDING", UiKit.Good, "Everyone else is out: you win."),
            (MatchModeKind.FreeForAll, RoundOutcome.Eliminated) =>
                ($"{(f.Winner ?? "SOMEONE ELSE").ToUpperInvariant()} WINS", UiKit.Bad, $"You placed {ModeText.Ordinal(f.Placing)} of {f.Players}.{hit}"),
            (MatchModeKind.FreeForAll, RoundOutcome.Traded) => ("TRADED", UiKit.Accent, "The last ones standing went out together."),
            (MatchModeKind.FreeForAll, RoundOutcome.TimeUp) =>
                ("TIME UP", UiKit.Bad, f.YouAreOut ? $"The clock ran out with {f.OthersLeft} still in. You placed {ModeText.Ordinal(f.Placing)} of {f.Players}."
                    : $"The clock ran out with {f.OthersLeft} others still in."),
            (MatchModeKind.Teams, RoundOutcome.Cleared) =>
                ("YOUR TEAM WINS", UiKit.Good, f.YouAreOut ? "Every opponent is out: your team finished the job without you." : "Every opponent is out."),
            (MatchModeKind.Teams, RoundOutcome.Eliminated) => ("YOUR TEAM IS OUT", UiKit.Bad, "The other team is the last one standing." + hit),
            (MatchModeKind.Teams, RoundOutcome.Traded) => ("TRADED", UiKit.Accent, "The last players of both teams went out together."),
            (MatchModeKind.Teams, RoundOutcome.TimeUp) =>
                ("TIME UP", UiKit.Bad, $"The clock ran out with {f.OursLeft} of yours and {f.OthersLeft} of theirs still in."),
            (_, RoundOutcome.Cleared) => ("CLEARED", UiKit.Good, "Every opponent is out."),
            (_, RoundOutcome.Eliminated) => ("ELIMINATED", UiKit.Bad, f.HitBy ?? "You were hit."),
            (_, RoundOutcome.Traded) => ("TRADED", UiKit.Accent, "You and the last opponent went out together."),
            (_, RoundOutcome.TimeUp) => ("TIME UP", UiKit.Bad, "The clock ran out."),
            _ => ("ROUND OVER", UiKit.Text, ""),
        };
    }

    public static string Clock(float seconds)
    {
        int s = (int)MathF.Ceiling(MathF.Max(0f, seconds));
        return $"{s / 60}:{s % 60:00}";
    }
}
