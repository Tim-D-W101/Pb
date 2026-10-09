using System.Runtime.CompilerServices;

namespace Pb.Sim.Gear;

/// <summary>The paint in each of a harness's pods (up to eight), kept in place.</summary>
[InlineArray(MaxPods)]
public struct PodCounts
{
    public const int MaxPods = 8;

    private int _first;
}

/// <summary>
/// Everything a <see cref="Marker"/> remembers, with its times relative to a moment rather than to one copy's clock: a
/// server sends its player's to the copy that player plays on, which puts it back and replays its presses from there
/// (the two clocks count from different starts). Restoring it at the same moment gives back the same marker.
/// </summary>
public struct MarkerState
{
    public int Loader;

    public PodCounts Pods;

    public byte PodCount;

    /// <summary>Tank pressure (Pa).</summary>
    public float Pressure;

    public bool RefillActive;

    public float RefillElapsed;

    /// <summary>The pod the refill is pouring from (−1: none).</summary>
    public sbyte RefillPod;

    public FireMode Mode;

    public bool Ramping;

    /// <summary>The trigger as fire control last saw it.</summary>
    public bool FireTrigger;

    /// <summary>Seconds from the moment until the next shot may leave (−∞: any time).</summary>
    public float NextShotIn;

    /// <summary>Seconds since the last pull (+∞: never).</summary>
    public float SinceLastPull;

    public byte FastPulls;

    public byte Buffered;

    /// <summary>The trigger, refill and fire-mode buttons as the marker last saw them.</summary>
    public bool Trigger;

    public bool RefillButton;

    public bool Toggle;

    public bool LowAirReported;

    public uint ShotSequence;

    public readonly bool SameAs(in MarkerState other)
    {
        if (Loader != other.Loader || PodCount != other.PodCount || Pressure != other.Pressure || RefillActive != other.RefillActive ||
            RefillElapsed != other.RefillElapsed || RefillPod != other.RefillPod || Mode != other.Mode || Ramping != other.Ramping ||
            FireTrigger != other.FireTrigger || FastPulls != other.FastPulls || Buffered != other.Buffered || Trigger != other.Trigger ||
            RefillButton != other.RefillButton || Toggle != other.Toggle || LowAirReported != other.LowAirReported ||
            ShotSequence != other.ShotSequence || !Close(NextShotIn, other.NextShotIn) || !Close(SinceLastPull, other.SinceLastPull))
        {
            return false;
        }

        for (int i = 0; i < PodCount; i++)
        {
            if (Pods[i] != other.Pods[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Times from two clocks agree to well under a tick (or are the same infinity).</summary>
    private static bool Close(float a, float b) => a == b || MathF.Abs(a - b) < 1e-4f;
}
