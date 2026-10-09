using Pb.Net.Packing;
using Pb.Sim.Players;

namespace Pb.Net.Protocol;

/// <summary>
/// A command as it travels: stick, look and buttons, quantised. A copy predicts with <see cref="Quantize"/>'s result, which
/// is exactly what the server will read, so its prediction and the server's moves agree.
/// </summary>
public static class CommandCodec
{
    public const InputButtons KnownButtons = (InputButtons)0x1FFF;

    private const int ButtonBits = 13;
    private const int YawBits = 16;
    private const int PitchBits = 14;
    private const int HeadBits = 12;
    private const float HalfTurn = MathF.PI * 0.5f;

    /// <summary>The command as it will arrive.</summary>
    public static InputCommand Quantize(in InputCommand c) => new()
    {
        Tick = c.Tick,
        Move = new System.Numerics.Vector2(Axis(c.Move.X) / 127f, Axis(c.Move.Y) / 127f),
        Yaw = Quant.FromAngle(Quant.Angle(Finite(c.Yaw), YawBits), YawBits),
        Pitch = Quant.FromRange(Quant.Range(Finite(c.Pitch), -HalfTurn, HalfTurn, PitchBits), -HalfTurn, HalfTurn, PitchBits),
        HeadYaw = c.HeadYaw == 0f ? 0f
            : Quant.FromRange(Quant.Range(Finite(c.HeadYaw), -HalfTurn, HalfTurn, HeadBits), -HalfTurn, HalfTurn, HeadBits),
        Buttons = c.Buttons & KnownButtons,
    };

    public static void Write(BitWriter w, in InputCommand c, uint viewBehind)
    {
        w.WriteSigned(Axis(c.Move.X), 8);
        w.WriteSigned(Axis(c.Move.Y), 8);
        w.WriteBits(Quant.Angle(Finite(c.Yaw), YawBits), YawBits);
        w.WriteBits(Quant.Range(Finite(c.Pitch), -HalfTurn, HalfTurn, PitchBits), PitchBits);
        bool head = c.HeadYaw != 0f;
        w.WriteBool(head);
        if (head)
        {
            w.WriteBits(Quant.Range(Finite(c.HeadYaw), -HalfTurn, HalfTurn, HeadBits), HeadBits);
        }

        w.WriteBits((uint)(c.Buttons & KnownButtons), ButtonBits);
        w.WriteBits(Math.Min(viewBehind, 255u), 8);
    }

    public static InputCommand Read(ref BitReader r, int tick, out uint viewBehind)
    {
        var c = new InputCommand
        {
            Tick = tick,
            Move = new System.Numerics.Vector2(r.ReadSigned(8) / 127f, r.ReadSigned(8) / 127f),
            Yaw = Quant.FromAngle(r.ReadBits(YawBits), YawBits),
            Pitch = Quant.FromRange(r.ReadBits(PitchBits), -HalfTurn, HalfTurn, PitchBits),
        };
        if (r.ReadBool())
        {
            c.HeadYaw = Quant.FromRange(r.ReadBits(HeadBits), -HalfTurn, HalfTurn, HeadBits);
        }

        c.Buttons = (InputButtons)r.ReadBits(ButtonBits);
        viewBehind = r.ReadBits(8);
        return c;
    }

    private static int Axis(float v) => (int)MathF.Round(Math.Clamp(Finite(v), -1f, 1f) * 127f);

    private static float Finite(float v) => float.IsFinite(v) ? v : 0f;
}
