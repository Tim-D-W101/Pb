using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Pb.Sim.Match;

namespace Pb.Game.Ui;

/// <summary>What a round is: the level, its difficulty tier, the mode and the size.</summary>
public sealed record RoundInfo(LevelLayout Level, LadderTierDef Tier, GameMode Mode, int Size)
{
    /// <summary>"Free-for-all · 8 players · Normal".</summary>
    public string Line => $"{Mode.DisplayName} · {ModeText.Size(Mode, Size)} · {Tier.DisplayName}";
}

/// <summary>How the round stands for the summary, worked out by the level from the sim.</summary>
public sealed record SummaryFacts
{
    /// <summary>The round is over (false when you skip ahead to the summary while the others play on).</summary>
    public required bool Over { get; init; }

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
}

/// <summary>Builds the round's overlays: the briefing card before it starts and the summary after it ends.</summary>
public static class RoundScreens
{
    /// <summary>
    /// The briefing card (the sim waits in its briefing phase meanwhile): level, mode, size, difficulty,
    /// what to do, and Start (Enter, Space or a click begins the round) or Back to level select.
    /// </summary>
    public static Control Briefing(RoundInfo round, Action start, Action back)
    {
        LadderTierDef tier = round.Tier;
        VBoxContainer column = UiKit.Column(12);
        column.AddChild(UiKit.Body("BRIEFING", 18, UiKit.Accent));
        column.AddChild(UiKit.Title(round.Level.DisplayName, 44));
        column.AddChild(UiKit.Body($"{round.Line} · {Clock(tier.TimeLimit_s)} on the clock", 22, UiKit.Dim));
        Label description = UiKit.Body(round.Level.Description, 19, UiKit.Text, wrap: true);
        description.CustomMinimumSize = new Vector2(720, 0);
        column.AddChild(description);
        column.AddChild(new HSeparator());
        var lines = new List<string>(Goal(round.Mode))
        {
            "One hit and you're out, them too. A bounce doesn't count.",
            $"You start with a full loader and {tier.StartPods} pod{(tier.StartPods == 1 ? "" : "s")}. " +
            (tier.Pickups ? "Paint pods and air are lying around the compound." : "There are no pickups."),
            "Crouch to move quietly, lean (Q/E) round corners, and swap shoulders (X) for left-hand edges.",
        };
        foreach (string line in lines)
        {
            Label l = UiKit.Body("·  " + line, 18, UiKit.Text, wrap: true);
            l.CustomMinimumSize = new Vector2(720, 0);
            column.AddChild(l);
        }

        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        HBoxContainer buttons = UiKit.Row(12);
        Button go = UiKit.Button("Start", start, 240);
        go.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        buttons.AddChild(go);
        buttons.AddChild(UiKit.Button("Back", back, 200));
        column.AddChild(buttons);
        Control overlay = UiKit.Overlay(UiKit.Panel(column, 800f), dim: 0.35f);
        go.CallDeferred(Control.MethodName.GrabFocus);
        return overlay;
    }

    private static string[] Goal(GameMode mode) => mode.Kind switch
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
        _ => new[] { "Clear the compound: eliminate every opponent before the time runs out." },
    };

    /// <summary>The summary: how the round went from your side, your numbers, and what next.</summary>
    public static Control Summary(RoundInfo round, MatchState match, PlayerStats you, SummaryFacts facts, Action retry, Action levelSelect,
        Action mainMenu)
    {
        (string title, Color colour, string line) = Verdict(round.Mode, match.Outcome, facts);

        VBoxContainer column = UiKit.Column(12);
        column.AddChild(UiKit.Body($"{round.Level.DisplayName} · {round.Line}", 20, UiKit.Dim));
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

        rows.Add(("Time", Clock(match.Elapsed)));
        rows.Add(("Eliminations", $"{you.Eliminations} of {facts.Opponents}"));
        rows.Add(("Shots", you.Shots.ToString()));
        rows.Add(("Hits", you.Hits.ToString()));
        rows.Add(("Accuracy", you.Shots > 0 ? $"{you.Accuracy * 100:0}%" : "–"));
        rows.Add(("Pickups", you.Pickups.ToString()));
        foreach ((string name, string value) in rows)
        {
            grid.AddChild(UiKit.Body(name, 22, UiKit.Dim));
            grid.AddChild(UiKit.Body(value, 22));
        }

        column.AddChild(grid);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        HBoxContainer buttons = UiKit.Row(12);
        Button again = UiKit.Button("Retry", retry, 200);
        buttons.AddChild(again);
        buttons.AddChild(UiKit.Button("Level select", levelSelect, 200));
        buttons.AddChild(UiKit.Button("Main menu", mainMenu, 200));
        column.AddChild(buttons);
        Control overlay = UiKit.Overlay(UiKit.Panel(column, 660f));
        again.CallDeferred(Control.MethodName.GrabFocus);
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
