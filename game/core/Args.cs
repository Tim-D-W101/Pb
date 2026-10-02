using System;
using Godot;

namespace Pb.Game.Core;

/// <summary>User command-line arguments (everything after "--" on the Godot command line).</summary>
public static class Args
{
    public static bool Has(string flag)
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == flag || arg.StartsWith(flag + "=", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The value of <c>--flag=value</c>, or null when absent.</summary>
    public static string? Value(string flag)
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith(flag + "=", StringComparison.Ordinal))
            {
                return arg[(flag.Length + 1)..];
            }
        }

        return null;
    }

    /// <summary>For <c>--flag</c> or <c>--flag=N</c>: N, or <paramref name="fallback"/>; null when the flag is absent.</summary>
    public static int? Ticks(string flag, int fallback)
    {
        if (!Has(flag))
        {
            return null;
        }

        return int.TryParse(Value(flag), out int n) ? n : fallback;
    }
}
