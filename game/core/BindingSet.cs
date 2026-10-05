#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Pb.Game.Core;

/// <summary>
/// One binding, as text: <c>key:W</c> (a physical key), <c>mouse:Left</c> (a mouse button), <c>pad:A</c> (a gamepad
/// button, SDL layout) or <c>axis:TriggerRight+</c> (a stick or trigger, one way). Plain text, so bindings can be saved,
/// compared and tested without the engine; <see cref="InputSetup"/> turns them into Godot's input events.
/// </summary>
public static class Binding
{
    public static bool IsPad(string binding) => binding.StartsWith("pad:", StringComparison.Ordinal) || binding.StartsWith("axis:", StringComparison.Ordinal);

    /// <summary>Whether the text is a binding at all (the right prefix and a name; an axis ends + or −).</summary>
    public static bool IsWellFormed(string binding)
    {
        int colon = binding.IndexOf(':');
        if (colon <= 0 || colon == binding.Length - 1)
        {
            return false;
        }

        string kind = binding[..colon];
        return kind switch
        {
            "key" or "mouse" or "pad" => true,
            "axis" => binding.Length > colon + 2 && binding[^1] is '+' or '-',
            _ => false,
        };
    }
}

/// <summary>Your changes to one action's bindings: its two keyboard-and-mouse slots and its pad slot. Null keeps the default; empty leaves it unbound.</summary>
public sealed class BindingOverride
{
    public string? First { get; set; }

    public string? Second { get; set; }

    public string? Pad { get; set; }

    public bool IsEmpty => First is null && Second is null && Pad is null;
}

/// <summary>
/// Every action's bindings: input.jsonc's defaults with your overrides on top. Each action has two keyboard-and-mouse
/// slots and one pad slot. Two actions sharing a binding clash, unless they share it in the defaults (the pad's refill
/// button works doors too); assigning one that's taken says who has it, so the menu can offer to swap.
/// </summary>
public sealed class BindingSet
{
    public enum Slot
    {
        First,
        Second,
        Pad,
    }

    private static readonly Slot[] AllSlots = { Slot.First, Slot.Second, Slot.Pad };

    private readonly List<string> _actions = new();
    private readonly Dictionary<string, string[]> _defaults = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _current = new(StringComparer.Ordinal);
    private readonly HashSet<(string, string, string)> _sharedByDefault = new();

    /// <param name="defaults">Each action and its default bindings: up to two keyboard-and-mouse ones and one pad one, in order.</param>
    public BindingSet(IEnumerable<(string Action, IReadOnlyList<string> Bindings)> defaults, IReadOnlyDictionary<string, BindingOverride>? overrides = null)
    {
        foreach ((string action, IReadOnlyList<string> bindings) in defaults)
        {
            var slots = new[] { "", "", "" };
            foreach (string b in bindings)
            {
                if (Binding.IsPad(b))
                {
                    slots[2] = slots[2].Length == 0 ? b : throw new ArgumentException($"{action}: more than one pad binding");
                }
                else
                {
                    int free = slots[0].Length == 0 ? 0 : slots[1].Length == 0 ? 1 : throw new ArgumentException($"{action}: more than two keyboard and mouse bindings");
                    slots[free] = b;
                }
            }

            _actions.Add(action);
            _defaults[action] = slots;
            _current[action] = (string[])slots.Clone();
        }

        // Bindings two actions share in the defaults are meant to be shared.
        foreach ((string a, string b, string binding) in Shared())
        {
            _sharedByDefault.Add((a, b, binding));
        }

        if (overrides is not null)
        {
            Apply(overrides);
        }
    }

    public IReadOnlyList<string> Actions => _actions;

    /// <summary>What's in a slot ("" for nothing).</summary>
    public string Get(string action, Slot slot) => _current[action][(int)slot];

    public string Default(string action, Slot slot) => _defaults[action][(int)slot];

    /// <summary>The action's bindings now, the empty slots left out.</summary>
    public IEnumerable<string> Effective(string action) => _current[action].Where(b => b.Length > 0);

    /// <summary>
    /// Puts <paramref name="binding"/> in a slot ("" clears it). Returns where else it's used and clashes, if anywhere
    /// (it's assigned all the same: <see cref="Swap"/> undoes the clash by giving the other slot this one's old binding).
    /// </summary>
    public (string Action, Slot Slot)? Assign(string action, Slot slot, string binding)
    {
        if (binding.Length > 0 && (!Binding.IsWellFormed(binding) || Binding.IsPad(binding) != (slot == Slot.Pad)))
        {
            throw new ArgumentException($"'{binding}' can't go in the {slot} slot");
        }

        _current[action][(int)slot] = binding;
        return binding.Length == 0 ? null : ClashFor(action, slot);
    }

    /// <summary>Who else holds the binding in this slot, where that's a clash (not a sharing the defaults have).</summary>
    public (string Action, Slot Slot)? ClashFor(string action, Slot slot)
    {
        string binding = Get(action, slot);
        if (binding.Length == 0)
        {
            return null;
        }

        foreach (string other in _actions)
        {
            foreach (Slot s in AllSlots)
            {
                if ((other != action || s != slot) && Get(other, s) == binding && !SharedByDefault(action, other, binding))
                {
                    return (other, s);
                }
            }
        }

        return null;
    }

    /// <summary>Swaps two slots' bindings (after an assignment clashed: the other slot gets this one's old binding).</summary>
    public void Swap(string action, Slot slot, string other, Slot otherSlot, string previous)
    {
        _current[action][(int)slot] = Get(other, otherSlot);
        _current[other][(int)otherSlot] = previous;
    }

    /// <summary>Every pair of actions that clash over a binding.</summary>
    public List<(string A, string B, string Binding)> Clashes()
    {
        var clashes = new List<(string, string, string)>();
        foreach ((string a, string b, string binding) in Shared())
        {
            if (!SharedByDefault(a, b, binding))
            {
                clashes.Add((a, b, binding));
            }
        }

        return clashes;
    }

    public void Reset(string action) => _current[action] = (string[])_defaults[action].Clone();

    public void ResetAll()
    {
        foreach (string action in _actions)
        {
            Reset(action);
        }
    }

    /// <summary>Your changes, to save: only the slots that differ from the defaults.</summary>
    public Dictionary<string, BindingOverride> Overrides()
    {
        var overrides = new Dictionary<string, BindingOverride>(StringComparer.Ordinal);
        foreach (string action in _actions)
        {
            string[] now = _current[action], was = _defaults[action];
            var o = new BindingOverride
            {
                First = now[0] == was[0] ? null : now[0],
                Second = now[1] == was[1] ? null : now[1],
                Pad = now[2] == was[2] ? null : now[2],
            };
            if (!o.IsEmpty)
            {
                overrides[action] = o;
            }
        }

        return overrides;
    }

    /// <summary>
    /// Lays saved changes over the defaults, dropping any for actions that no longer exist or bindings that aren't well
    /// formed or don't fit their slot; returns how many it dropped.
    /// </summary>
    public int Apply(IReadOnlyDictionary<string, BindingOverride> overrides)
    {
        int dropped = 0;
        foreach ((string action, BindingOverride o) in overrides)
        {
            if (!_current.ContainsKey(action))
            {
                dropped++;
                continue;
            }

            foreach ((Slot slot, string? binding) in new[] { (Slot.First, o.First), (Slot.Second, o.Second), (Slot.Pad, o.Pad) })
            {
                if (binding is null)
                {
                    continue;
                }

                if (binding.Length > 0 && (!Binding.IsWellFormed(binding) || Binding.IsPad(binding) != (slot == Slot.Pad)))
                {
                    dropped++;
                    continue;
                }

                _current[action][(int)slot] = binding;
            }
        }

        return dropped;
    }

    private bool SharedByDefault(string a, string b, string binding) =>
        _sharedByDefault.Contains((a, b, binding)) || _sharedByDefault.Contains((b, a, binding));

    /// <summary>Every pair of different actions holding the same binding now.</summary>
    private IEnumerable<(string A, string B, string Binding)> Shared()
    {
        var holders = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string action in _actions)
        {
            foreach (string b in Effective(action))
            {
                if (!holders.TryGetValue(b, out List<string>? list))
                {
                    holders[b] = list = new List<string>();
                }

                if (!list.Contains(action))
                {
                    list.Add(action);
                }
            }
        }

        foreach ((string binding, List<string> list) in holders)
        {
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    yield return (list[i], list[j], binding);
                }
            }
        }
    }
}
