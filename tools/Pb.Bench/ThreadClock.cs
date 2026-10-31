using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>
/// The CPU time this thread has had (ms), next to the wall-clock time a tick took: a tick that took far longer than its
/// CPU time was waiting (the system gave the core to something else, or a collection paused the thread), not working.
/// Linux reads the thread's own clock; Windows counts the thread's cycles, timed against the wall clock at start; other
/// systems have neither (<see cref="Available"/> is false).
/// </summary>
internal static class ThreadClock
{
    private static readonly double CyclesPerMs = OperatingSystem.IsWindows() ? Calibrate() : 0.0;

    public static bool Available { get; } = OperatingSystem.IsLinux() || (OperatingSystem.IsWindows() && CyclesPerMs > 0.0);

    /// <summary>This thread's CPU time so far (ms), or 0 where it can't be had.</summary>
    public static double NowMs()
    {
        if (OperatingSystem.IsLinux())
        {
            return clock_gettime(ClockThreadCpuTime, out Timespec t) == 0 ? t.Seconds * 1000.0 + t.Nanoseconds / 1e6 : 0.0;
        }

        if (OperatingSystem.IsWindows() && CyclesPerMs > 0.0 && QueryThreadCycleTime(GetCurrentThread(), out ulong cycles))
        {
            return cycles / CyclesPerMs;
        }

        return 0.0;
    }

    /// <summary>Windows: the thread's cycles per millisecond, over 50 ms of spinning (the thread runs all of it).</summary>
    private static double Calibrate()
    {
        try
        {
            if (!QueryThreadCycleTime(GetCurrentThread(), out ulong start))
            {
                return 0.0;
            }

            var watch = Stopwatch.StartNew();
            double spin = 0.0;
            while (watch.Elapsed.TotalMilliseconds < 50.0)
            {
                spin += Math.Sqrt(spin + 1.0);
            }

            QueryThreadCycleTime(GetCurrentThread(), out ulong end);
            return spin >= 0.0 ? (end - start) / watch.Elapsed.TotalMilliseconds : 0.0;
        }
        catch (EntryPointNotFoundException)
        {
            return 0.0;
        }
    }

    private const int ClockThreadCpuTime = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec
    {
        public long Seconds;
        public long Nanoseconds;
    }

    [DllImport("libc", SetLastError = false)]
    private static extern int clock_gettime(int clock, out Timespec time);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryThreadCycleTime(IntPtr thread, out ulong cycles);
}
