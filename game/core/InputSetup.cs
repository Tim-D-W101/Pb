using System;
using Godot;
using Pb.Sim.Data;

namespace Pb.Game.Core;

/// <summary>
/// Builds Godot's InputMap from input.jsonc at startup, so bindings live in data and the
/// rebinding UI (Phase 5) only has to write user overrides.
/// </summary>
public static class InputSetup
{
    public static void Apply(InputDef def)
    {
        foreach (InputActionDef action in def.Actions)
        {
            StringName name = action.Name;
            if (InputMap.HasAction(name))
            {
                InputMap.EraseAction(name);
            }

            InputMap.AddAction(name, action.Deadzone > 0f ? action.Deadzone : def.Deadzone);

            foreach (string key in action.Keys ?? Array.Empty<string>())
            {
                Key code = OS.FindKeycodeFromString(key);
                if (code == Key.None)
                {
                    throw Bad(action, $"unknown key '{key}'");
                }

                InputMap.ActionAddEvent(name, new InputEventKey { PhysicalKeycode = code, Device = -1 });
            }

            foreach (string button in action.Mouse ?? Array.Empty<string>())
            {
                if (!Enum.TryParse(button, ignoreCase: true, out MouseButton mouse) || mouse == MouseButton.None)
                {
                    throw Bad(action, $"unknown mouse button '{button}'");
                }

                InputMap.ActionAddEvent(name, new InputEventMouseButton { ButtonIndex = mouse, Device = -1 });
            }

            foreach (string button in action.Buttons ?? Array.Empty<string>())
            {
                if (!Enum.TryParse(button, ignoreCase: true, out JoyButton joy) || joy == JoyButton.Invalid)
                {
                    throw Bad(action, $"unknown gamepad button '{button}'");
                }

                InputMap.ActionAddEvent(name, new InputEventJoypadButton { ButtonIndex = joy, Device = -1 });
            }

            foreach (string axis in action.Axes ?? Array.Empty<string>())
            {
                if (axis.Length < 2 || (axis[^1] != '+' && axis[^1] != '-') ||
                    !Enum.TryParse(axis[..^1], ignoreCase: true, out JoyAxis joyAxis) || joyAxis == JoyAxis.Invalid)
                {
                    throw Bad(action, $"unknown gamepad axis '{axis}' (use e.g. \"LeftY-\" or \"TriggerRight+\")");
                }

                InputMap.ActionAddEvent(name, new InputEventJoypadMotion
                {
                    Axis = joyAxis,
                    AxisValue = axis[^1] == '+' ? 1f : -1f,
                    Device = -1,
                });
            }
        }
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

    private static DataException Bad(InputActionDef action, string message) =>
        new(InputDef.File, $"actions '{action.Name}': {message}");
}
