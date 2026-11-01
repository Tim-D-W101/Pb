using System;
using Godot;

namespace Pb.Game.Core;

/// <summary>
/// Quitting without Godot reporting leaks at exit. A Godot object that C# still holds (a material, a texture, a label's
/// settings) is only let go of when the .NET collector finalizes its wrapper, and one still waiting when the engine checks
/// for leaks is reported as leaked: harmless, but CI fails on the error line, and it came and went with the collector's
/// timing. So a run that quits frees the current scene first, gives it two frames to go, runs the finalizers, then quits.
/// </summary>
public static class CleanExit
{
    private static bool _quitting;

    /// <summary>Quits with <paramref name="code"/> once the current scene is gone and the finalizers have run.</summary>
    public static void QuitCleanly(this SceneTree tree, int code = 0)
    {
        if (_quitting)
        {
            return;
        }

        _quitting = true;
        tree.CurrentScene?.QueueFree();
        int frames = 0;
        tree.ProcessFrame += Step;

        void Step()
        {
            if (++frames < 3)
            {
                return;
            }

            tree.ProcessFrame -= Step;
            for (int i = 0; i < 2; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            tree.Quit(code);
        }
    }
}
