using System.Diagnostics;
using System.Text;
using Godot;

namespace Pb.Game.Core;

/// <summary>
/// How long each part of loading a level took (M5.8), printed as one line once the round is ready, so a slow load can be
/// told apart: the data, the bots' grid, the world's meshes, the dressing, the characters.
/// </summary>
public sealed class LoadClock
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly StringBuilder _parts = new();
    private double _last;

    /// <summary>The part since the last one ended was <paramref name="what"/>.</summary>
    public void Lap(string what)
    {
        double now = _watch.Elapsed.TotalMilliseconds;
        _parts.Append(_parts.Length > 0 ? ", " : "").Append(what).Append(' ').Append((now - _last).ToString("0")).Append(" ms");
        _last = now;
    }

    /// <summary>Every part so far, with how long it took.</summary>
    public string Parts => _parts.ToString();

    /// <summary>The line: every part, the whole, and how long since the engine started.</summary>
    public string Line => $"Load: {_parts}; {_watch.Elapsed.TotalMilliseconds:0} ms in all, ready {Time.GetTicksMsec() / 1000.0:0.0} s after the start";
}
