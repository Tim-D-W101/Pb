using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pb.Game.Audio;
using Pb.Game.Core;

namespace Pb.Game.Ui;

/// <summary>
/// Every action's bindings, for rebinding (Controls): two keyboard-and-mouse slots and a pad slot each, grouped as
/// movement, combat and shortcuts. Click a slot and press the new key, mouse button, pad button or stick (Esc cancels,
/// Backspace clears it). A binding another action already has is shown in red, with an offer to swap the two. Every
/// change is saved and applied at once.
/// </summary>
public partial class BindingsList : VBoxContainer
{
    private static readonly (string Id, string Name)[] Groups = { ("movement", "Movement"), ("combat", "Combat"), ("shortcuts", "Shortcuts") };

    private readonly Dictionary<(string Action, BindingSet.Slot Slot), Button> _buttons = new();
    private GameSettings _settings = null!;
    private InputDef _input = null!;
    private BindingSet _set = null!;
    private Label _message = null!;
    private HBoxContainer _offer = null!;
    private (string Action, BindingSet.Slot Slot)? _capturing;
    private (string Action, BindingSet.Slot Slot, string Other, BindingSet.Slot OtherSlot, string Previous)? _clash;

    /// <summary>Whether a slot is waiting for a key (so the menu around it doesn't take Esc as "back").</summary>
    public bool Capturing => _capturing is not null;

    public void Build(GameSettings settings, InputDef input)
    {
        _settings = settings;
        _input = input;
        _set = InputSetup.Bindings(input, settings.Bindings);
        AddThemeConstantOverride("separation", 6);

        HBoxContainer header = UiKit.Row(8);
        header.AddChild(Cell(UiKit.Body("Action", 18, UiKit.Dim), 230));
        header.AddChild(Cell(UiKit.Body("Keyboard and mouse", 18, UiKit.Dim), 296));
        header.AddChild(Cell(UiKit.Body("Pad", 18, UiKit.Dim), 186));
        AddChild(header);

        foreach ((string group, string title) in Groups)
        {
            InputActionDef[] actions = input.Actions.Where(a => a.Group == group).ToArray();
            if (actions.Length == 0)
            {
                continue;
            }

            AddChild(UiKit.Body(title.ToUpperInvariant(), 16, UiKit.Accent));
            foreach (InputActionDef action in actions)
            {
                AddChild(Row(action));
            }
        }

        _message = UiKit.Body("", 18, UiKit.Dim, wrap: true);
        _message.CustomMinimumSize = new Vector2(700, 0);
        AddChild(_message);
        _offer = UiKit.Row(10);
        _offer.AddChild(UiKit.Button("Swap them", Swap, 200));
        _offer.AddChild(UiKit.Button("Keep both", () => Settle(null), 200));
        _offer.Visible = false;
        AddChild(_offer);
        AddChild(UiKit.Button("Reset all bindings", () =>
        {
            _set.ResetAll();
            Changed("Every binding is back to its default.");
        }, 280));
        Refresh();
    }

    public override void _Input(InputEvent e)
    {
        if (_capturing is not { } slot)
        {
            return;
        }

        bool pad = slot.Slot == BindingSet.Slot.Pad;
        if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode == Key.Escape || key.PhysicalKeycode == Key.Escape)
            {
                _capturing = null;
                GetViewport().SetInputAsHandled();
                Refresh();
                Say("");
                return;
            }

            if (key.Keycode is Key.Backspace or Key.Delete)
            {
                _capturing = null;
                GetViewport().SetInputAsHandled();
                _set.Assign(slot.Action, slot.Slot, "");
                Changed($"{Label(slot.Action)}: that slot is empty now.");
                return;
            }
        }

        if (InputSetup.FromEvent(e, pad) is not { } binding)
        {
            return;
        }

        GetViewport().SetInputAsHandled();
        _capturing = null;
        string previous = _set.Get(slot.Action, slot.Slot);
        (string Action, BindingSet.Slot Slot)? holder = _set.Assign(slot.Action, slot.Slot, binding);
        if (holder is { } other)
        {
            _clash = (slot.Action, slot.Slot, other.Action, other.Slot, previous);
            Changed($"{InputSetup.Describe(binding)} is {Label(other.Action)} too. Swap them, so {Label(other.Action)} gets " +
                    $"{InputSetup.Describe(previous)}, or keep both?", offer: true);
        }
        else
        {
            Changed($"{Label(slot.Action)}: {InputSetup.Describe(binding)}.");
        }
    }

    private HBoxContainer Row(InputActionDef action)
    {
        HBoxContainer row = UiKit.Row(8);
        row.AddChild(Cell(UiKit.Body(action.Display, 19), 230));
        foreach (BindingSet.Slot slot in new[] { BindingSet.Slot.First, BindingSet.Slot.Second, BindingSet.Slot.Pad })
        {
            var button = new Button
            {
                CustomMinimumSize = new Vector2(slot == BindingSet.Slot.Pad ? 186 : 144, 38),
                FocusMode = Control.FocusModeEnum.All,
                ClipText = true,
                Name = $"{action.Name}_{slot}",
            };
            string name = action.Name;
            button.Pressed += () =>
            {
                UiSounds.Click();
                _capturing = (name, slot);
                Settle(null);
                Refresh();
                Say(slot == BindingSet.Slot.Pad
                    ? $"{Label(name)}: press a pad button or push a stick or trigger (Esc cancels, Backspace clears)."
                    : $"{Label(name)}: press a key or a mouse button (Esc cancels, Backspace clears).");
            };
            _buttons[(action.Name, slot)] = button;
            row.AddChild(button);
        }

        row.AddChild(UiKit.Button("Reset", () =>
        {
            _set.Reset(action.Name);
            Changed($"{action.Display} is back to its default.");
        }, 90));
        return row;
    }

    private void Swap()
    {
        if (_clash is { } c)
        {
            _set.Swap(c.Action, c.Slot, c.Other, c.OtherSlot, c.Previous);
            Settle($"Swapped: {Label(c.Other)} has {InputSetup.Describe(_set.Get(c.Other, c.OtherSlot))} now.");
        }
    }

    private void Settle(string? message)
    {
        _clash = null;
        _offer.Visible = false;
        if (message is not null)
        {
            Changed(message);
        }
    }

    /// <summary>Saves and applies the bindings, says what happened and redraws the slots.</summary>
    private void Changed(string message, bool offer = false)
    {
        _settings.Bindings = _set.Overrides();
        _settings.Save();
        InputSetup.Apply(_settings.Bindings);
        _offer.Visible = offer;
        if (!offer)
        {
            _clash = null;
        }

        Say(message);
        Refresh();
    }

    private void Refresh()
    {
        var clashing = new HashSet<string>(_set.Clashes().Select(c => c.Binding), StringComparer.Ordinal);
        foreach (((string action, BindingSet.Slot slot), Button button) in _buttons)
        {
            string binding = _set.Get(action, slot);
            bool listening = _capturing is { } c && c.Action == action && c.Slot == slot;
            button.Text = listening ? "Press…" : InputSetup.Describe(binding);
            button.TooltipText = binding.Length == 0 ? "Empty" : clashing.Contains(binding) ? "Also bound to another action" : "";
            button.AddThemeColorOverride("font_color", listening ? UiKit.Accent : clashing.Contains(binding) ? UiKit.Bad : binding.Length == 0 ? UiKit.Dim : UiKit.Text);
        }
    }

    private void Say(string message) => _message.Text = message;

    private string Label(string action) => _input.Actions.FirstOrDefault(a => a.Name == action)?.Display ?? action;

    private static Control Cell(Control content, float width)
    {
        content.CustomMinimumSize = new Vector2(width, content.CustomMinimumSize.Y);
        return content;
    }
}
