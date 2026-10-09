using System.Numerics;
using Pb.Sim.Gear;
using Pb.Sim.Players;

namespace Pb.Net.Protocol;

/// <summary>
/// A player's own state as the server sends it back to them: <see cref="PredictedState"/> to the bit (floats whole, so the
/// copy can put it back exactly and replay its presses from it), as a list of fields for <see cref="Delta"/>.
/// </summary>
public static class OwnFields
{
    private const int Floats = 23;
    private const int Small = 5;
    private const int PodFields = PodCounts.MaxPods;

    /// <summary>The floats, the small fields, the marker's own fields, then its pods.</summary>
    public const int Count = Floats + Small + 19 + PodFields;

    private static readonly byte[] WidthTable = BuildWidths();

    public static ReadOnlySpan<byte> Widths => WidthTable;

    public static void Capture(in PredictedState s, Span<uint> f)
    {
        int i = 0;
        F(f, ref i, s.Position.X);
        F(f, ref i, s.Position.Y);
        F(f, ref i, s.Position.Z);
        F(f, ref i, s.Velocity.X);
        F(f, ref i, s.Velocity.Y);
        F(f, ref i, s.Velocity.Z);
        F(f, ref i, s.LeanOffset.X);
        F(f, ref i, s.LeanOffset.Y);
        F(f, ref i, s.LeanOffset.Z);
        F(f, ref i, s.Yaw);
        F(f, ref i, s.Pitch);
        F(f, ref i, s.HeadYaw);
        F(f, ref i, s.EyeHeight);
        F(f, ref i, s.Lean);
        F(f, ref i, s.LeanRoll);
        F(f, ref i, s.Shoulder);
        F(f, ref i, s.ShoulderTarget);
        F(f, ref i, s.Tuck);
        F(f, ref i, s.SlideTime);
        F(f, ref i, s.SlideCooldown);
        F(f, ref i, s.JumpCooldown);
        F(f, ref i, s.SprintRecovery);
        F(f, ref i, s.LadderTime);
        f[i++] = (uint)s.Stance;
        f[i++] = (uint)s.PreviousButtons;
        f[i++] = (uint)(s.Ladder + 1);
        f[i++] = (uint)s.LadderPhase;
        f[i++] = (s.Sprinting ? 1u : 0) | (s.Grounded ? 2u : 0) | (s.Alive ? 4u : 0) | (s.Present ? 8u : 0) | (s.SprintBlocked ? 16u : 0);

        ref readonly MarkerState m = ref s.Marker;
        f[i++] = (uint)m.Loader;
        f[i++] = m.PodCount;
        F(f, ref i, m.Pressure);
        f[i++] = m.RefillActive ? 1u : 0;
        F(f, ref i, m.RefillElapsed);
        f[i++] = (uint)(m.RefillPod + 1);
        f[i++] = (uint)m.Mode;
        f[i++] = m.Ramping ? 1u : 0;
        f[i++] = m.FireTrigger ? 1u : 0;
        F(f, ref i, m.NextShotIn);
        F(f, ref i, m.SinceLastPull);
        f[i++] = m.FastPulls;
        f[i++] = m.Buffered;
        f[i++] = m.Trigger ? 1u : 0;
        f[i++] = m.RefillButton ? 1u : 0;
        f[i++] = m.Toggle ? 1u : 0;
        f[i++] = m.LowAirReported ? 1u : 0;
        f[i++] = m.ShotSequence;
        f[i++] = 0;
        for (int p = 0; p < PodFields; p++)
        {
            f[i++] = (uint)(p < m.PodCount ? m.Pods[p] : 0);
        }
    }

    public static PredictedState Read(ReadOnlySpan<uint> f)
    {
        int i = 0;
        var s = new PredictedState
        {
            Position = new Vector3(G(f, ref i), G(f, ref i), G(f, ref i)),
            Velocity = new Vector3(G(f, ref i), G(f, ref i), G(f, ref i)),
            LeanOffset = new Vector3(G(f, ref i), G(f, ref i), G(f, ref i)),
            Yaw = G(f, ref i),
            Pitch = G(f, ref i),
            HeadYaw = G(f, ref i),
            EyeHeight = G(f, ref i),
            Lean = G(f, ref i),
            LeanRoll = G(f, ref i),
            Shoulder = G(f, ref i),
            ShoulderTarget = G(f, ref i),
            Tuck = G(f, ref i),
            SlideTime = G(f, ref i),
            SlideCooldown = G(f, ref i),
            JumpCooldown = G(f, ref i),
            SprintRecovery = G(f, ref i),
            LadderTime = G(f, ref i),
        };
        s.Stance = (Stance)f[i++];
        s.PreviousButtons = (InputButtons)f[i++];
        s.Ladder = (short)((int)f[i++] - 1);
        s.LadderPhase = (LadderPhase)f[i++];
        uint flags = f[i++];
        s.Sprinting = (flags & 1) != 0;
        s.Grounded = (flags & 2) != 0;
        s.Alive = (flags & 4) != 0;
        s.Present = (flags & 8) != 0;
        s.SprintBlocked = (flags & 16) != 0;

        var m = new MarkerState
        {
            Loader = (int)f[i++],
            PodCount = (byte)Math.Min(f[i++], PodCounts.MaxPods),
            Pressure = G(f, ref i),
            RefillActive = f[i++] != 0,
            RefillElapsed = G(f, ref i),
            RefillPod = (sbyte)((int)f[i++] - 1),
            Mode = (FireMode)f[i++],
            Ramping = f[i++] != 0,
            FireTrigger = f[i++] != 0,
            NextShotIn = G(f, ref i),
            SinceLastPull = G(f, ref i),
            FastPulls = (byte)f[i++],
            Buffered = (byte)f[i++],
            Trigger = f[i++] != 0,
            RefillButton = f[i++] != 0,
            Toggle = f[i++] != 0,
            LowAirReported = f[i++] != 0,
            ShotSequence = f[i++],
        };
        i++;
        for (int p = 0; p < PodFields; p++)
        {
            m.Pods[p] = (int)f[i++];
        }

        s.Marker = m;
        return s;
    }

    private static void F(Span<uint> f, ref int i, float value) => f[i++] = BitConverter.SingleToUInt32Bits(value);

    private static float G(ReadOnlySpan<uint> f, ref int i) => BitConverter.UInt32BitsToSingle(f[i++]);

    private static byte[] BuildWidths()
    {
        var w = new byte[Count];
        int i = 0;
        for (int k = 0; k < Floats; k++)
        {
            w[i++] = 32;
        }

        // Stance, buttons held, ladder (+1), ladder phase, flags.
        w[i++] = 2;
        w[i++] = 16;
        w[i++] = 8;
        w[i++] = 2;
        w[i++] = 5;
        // The marker: loader, pod count, pressure, refill active, elapsed, pod (+1), mode, ramping, fire trigger,
        // next shot in, since last pull, fast pulls, buffered, the three buttons, low air, shot sequence, spare.
        w[i++] = 16;
        w[i++] = 4;
        w[i++] = 32;
        w[i++] = 1;
        w[i++] = 32;
        w[i++] = 4;
        w[i++] = 1;
        w[i++] = 1;
        w[i++] = 1;
        w[i++] = 32;
        w[i++] = 32;
        w[i++] = 8;
        w[i++] = 8;
        w[i++] = 1;
        w[i++] = 1;
        w[i++] = 1;
        w[i++] = 1;
        w[i++] = 32;
        w[i++] = 1;
        for (int p = 0; p < PodFields; p++)
        {
            w[i++] = 16;
        }

        return w;
    }
}
