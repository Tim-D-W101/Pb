using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pb.Sim.Data;

namespace Pb.Game.Core;

/// <summary>
/// Builds Godot's InputMap from input.jsonc's bindings with your overrides from the settings on top
/// (<see cref="BindingSet"/>), and turns bindings to and from Godot's input events: what the settings menu captures
/// when you press a new key or button, and how it names them.
/// </summary>
public static class InputSetup
{
    /// <summary>The input.jsonc in use (the settings menu lists its actions).</summary>
    public static InputDef? Current { get; private set; }

    /// <summary>
    /// Applies input.jsonc's own bindings (checking every one is a real key, button or axis), and remembers its actions
    /// for the settings, which are loaded next and then applied over them with <see cref="Apply(IReadOnlyDictionary{string, BindingOverride})"/>.
    /// </summary>
    public static void Apply(InputDef def)
    {
        Current = def;
        GameSettings.KnownActions = def.Actions.Select(a => a.Name).ToArray();
        foreach (InputActionDef action in def.Actions)
        {
            foreach (string binding in action.Bindings)
            {
                if (ToEvent(binding) is null)
                {
                    throw new DataException(InputDef.File, $"actions '{action.Name}': unknown {Kind(binding)} '{binding[(binding.IndexOf(':') + 1)..]}'" +
                        (binding.StartsWith("axis:", StringComparison.Ordinal) ? " (use e.g. \"LeftY-\" or \"TriggerRight+\")" : ""));
                }
            }
        }

        Map(Bindings(def, null));
    }

    /// <summary>Applies your overrides over input.jsonc's bindings.</summary>
    public static void Apply(IReadOnlyDictionary<string, BindingOverride> overrides)
    {
        if (Current is { } def)
        {
            Map(Bindings(def, overrides));
        }
    }

    /// <summary>input.jsonc's bindings with <paramref name="overrides"/> on top (none: the defaults).</summary>
    public static BindingSet Bindings(InputDef def, IReadOnlyDictionary<string, BindingOverride>? overrides) =>
        new(def.Actions.Select(a => (a.Name, a.Bindings)), overrides);

    /// <summary>Godot's input event for a binding, or null if it names no key, button or axis.</summary>
    public static InputEvent? ToEvent(string binding)
    {
        int colon = binding.IndexOf(':');
        if (colon < 0)
        {
            return null;
        }

        string name = binding[(colon + 1)..];
        switch (binding[..colon])
        {
            case "key":
                Key code = OS.FindKeycodeFromString(name);
                return code == Key.None ? null : new InputEventKey { PhysicalKeycode = code, Device = -1 };
            case "mouse":
                return Enum.TryParse(name, ignoreCase: true, out MouseButton mouse) && mouse != MouseButton.None
                    ? new InputEventMouseButton { ButtonIndex = mouse, Device = -1 }
                    : null;
            case "pad":
                return Enum.TryParse(name, ignoreCase: true, out JoyButton joy) && joy != JoyButton.Invalid
                    ? new InputEventJoypadButton { ButtonIndex = joy, Device = -1 }
                    : null;
            case "axis":
                if (name.Length < 2 || name[^1] is not ('+' or '-') || !Enum.TryParse(name[..^1], ignoreCase: true, out JoyAxis axis) ||
                    axis == JoyAxis.Invalid)
                {
                    return null;
                }

                return new InputEventJoypadMotion { Axis = axis, AxisValue = name[^1] == '+' ? 1f : -1f, Device = -1 };
            default:
                return null;
        }
    }

    /// <summary>
    /// The binding an input event would be, for the settings menu capturing a new one: a key or mouse button for a
    /// keyboard-and-mouse slot, a pad button or a stick or trigger pushed well over for the pad slot; else null.
    /// </summary>
    public static string? FromEvent(InputEvent e, bool pad)
    {
        switch (e)
        {
            case InputEventKey { Pressed: true, Echo: false } key when !pad:
                Key code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
                return code == Key.None ? null : "key:" + OS.GetKeycodeString(code);
            case InputEventMouseButton { Pressed: true } mouse when !pad:
                return "mouse:" + mouse.ButtonIndex;
            case InputEventJoypadButton { Pressed: true } button when pad:
                return "pad:" + button.ButtonIndex;
            case InputEventJoypadMotion motion when pad && Mathf.Abs(motion.AxisValue) > 0.6f:
                return "axis:" + motion.Axis + (motion.AxisValue > 0f ? "+" : "-");
            default:
                return null;
        }
    }

    /// <summary>A binding as the menus show it: "W", "Left mouse", "Pad A", "Right trigger", "L stick up".</summary>
    public static string Describe(string binding)
    {
        if (binding.Length == 0)
        {
            return "—";
        }

        int colon = binding.IndexOf(':');
        string name = colon >= 0 ? binding[(colon + 1)..] : binding;
        return binding[..Math.Max(colon, 0)] switch
        {
            "mouse" => name switch
            {
                "WheelUp" => "Wheel up",
                "WheelDown" => "Wheel down",
                _ => $"{name} mouse",
            },
            "pad" => $"Pad {name}",
            "axis" => name[..^1] switch
            {
                "TriggerLeft" => "Left trigger",
                "TriggerRight" => "Right trigger",
                "LeftX" => name[^1] == '+' ? "L stick right" : "L stick left",
                "LeftY" => name[^1] == '+' ? "L stick down" : "L stick up",
                "RightX" => name[^1] == '+' ? "R stick right" : "R stick left",
                "RightY" => name[^1] == '+' ? "R stick down" : "R stick up",
                _ => name,
            },
            _ => name,
        };
    }

    /// <summary>What to press for <paramref name="action"/>, for prompts: its first keyboard key (or mouse button), else "?".</summary>
    public static string KeyName(string action)
    {
        if (!InputMap.HasAction(action))
        {
            return "?";
        }

        foreach (InputEvent e in InputMap.ActionGetEvents(action))
        {
            switch (e)
            {
                case InputEventKey key:
                    return OS.GetKeycodeString(key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode);
                case InputEventMouseButton mouse:
                    return mouse.ButtonIndex + " mouse";
            }
        }

        return "?";
    }

    private static void Map(BindingSet bindings)
    {
        foreach (InputActionDef action in Current!.Actions)
        {
            StringName name = action.Name;
            if (InputMap.HasAction(name))
            {
                InputMap.EraseAction(name);
            }

            InputMap.AddAction(name, action.Deadzone > 0f ? action.Deadzone : Current.Deadzone);
            foreach (string binding in bindings.Effective(action.Name))
            {
                if (ToEvent(binding) is { } e)
                {
                    InputMap.ActionAddEvent(name, e);
                }
            }
        }
    }

    private static string Kind(string binding) => binding[..Math.Max(binding.IndexOf(':'), 0)] switch
    {
        "key" => "key",
        "mouse" => "mouse button",
        "pad" => "gamepad button",
        _ => "gamepad axis",
    };
}
