using System;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Pb.Sim.Match;

namespace Pb.Game.Ui;

/// <summary>Builds the round's overlays: the briefing card before it starts and the summary after it ends.</summary>
public static class RoundScreens
{
    /// <summary>
    /// The briefing card (the sim waits in its briefing phase meanwhile): level, difficulty, what to do,
    /// and Start (Enter, Space or a click begins the round) or Back to level select.
    /// </summary>
    public static Control Briefing(LevelLayout level, LadderTierDef tier, Action start, Action back)
    {
        VBoxContainer column = UiKit.Column(12);
        column.AddChild(UiKit.Body("BRIEFING", 18, UiKit.Accent));
        column.AddChild(UiKit.Title(level.DisplayName, 44));
        column.AddChild(UiKit.Body($"{tier.DisplayName} · {tier.Opponents.Length} opponents · {Clock(tier.TimeLimit_s)} on the clock", 22, UiKit.Dim));
        Label description = UiKit.Body(level.Description, 19, UiKit.Text, wrap: true);
        description.CustomMinimumSize = new Vector2(720, 0);
        column.AddChild(description);
        column.AddChild(new HSeparator());
        foreach (string line in new[]
                 {
                     "Clear the compound: eliminate every opponent before the time runs out.",
                     "One hit and you're out, them too. A bounce doesn't count.",
                     $"You start with a full loader and {tier.StartPods} pod{(tier.StartPods == 1 ? "" : "s")}. " +
                     (tier.Pickups ? "Paint pods and air are lying around the compound." : "There are no pickups."),
                     "Crouch to move quietly, lean (Q/E) round corners, and swap shoulders (X) for left-hand edges.",
                 })
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

    /// <summary>The summary: how the round ended, your numbers, and what next. <paramref name="hitBy"/> says who got you, if anyone did.</summary>
    public static Control Summary(LevelLayout level, LadderTierDef tier, MatchState match, PlayerStats you, int opponents, string? hitBy,
        Action retry, Action levelSelect, Action mainMenu)
    {
        (string title, Color colour, string line) = match.Outcome switch
        {
            RoundOutcome.Cleared => ("CLEARED", UiKit.Good, "Every opponent is out."),
            RoundOutcome.Eliminated => ("ELIMINATED", UiKit.Bad, hitBy ?? "You were hit."),
            RoundOutcome.Traded => ("TRADED", UiKit.Accent, "You and the last opponent went out together."),
            RoundOutcome.TimeUp => ("TIME UP", UiKit.Bad, "The clock ran out."),
            _ => ("ROUND OVER", UiKit.Text, ""),
        };

        VBoxContainer column = UiKit.Column(12);
        column.AddChild(UiKit.Body($"{level.DisplayName} · {tier.DisplayName}", 20, UiKit.Dim));
        column.AddChild(UiKit.Title(title, 56, colour));
        column.AddChild(UiKit.Body(line, 20));
        column.AddChild(new HSeparator());

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 40);
        grid.AddThemeConstantOverride("v_separation", 8);
        foreach ((string name, string value) in new[]
                 {
                     ("Time", Clock(match.Elapsed)),
                     ("Eliminations", $"{you.Eliminations} of {opponents}"),
                     ("Shots", you.Shots.ToString()),
                     ("Hits", you.Hits.ToString()),
                     ("Accuracy", you.Shots > 0 ? $"{you.Accuracy * 100:0}%" : "–"),
                     ("Pickups", you.Pickups.ToString()),
                 })
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

    public static string Clock(float seconds)
    {
        int s = (int)MathF.Ceiling(MathF.Max(0f, seconds));
        return $"{s / 60}:{s % 60:00}";
    }
}
