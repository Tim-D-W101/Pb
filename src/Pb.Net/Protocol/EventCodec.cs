using System.Numerics;
using Pb.Net.Packing;
using Pb.Sim.Collision;
using Pb.Sim.Events;

namespace Pb.Net.Protocol;

/// <summary>Events the network carries that aren't the sim's own.</summary>
public static class NetEventTypes
{
    /// <summary>
    /// A bot's callout, heard from where it stands: <see cref="SimEvent.PlayerId"/> the caller, <see cref="SimEvent.Extra"/>
    /// the callout, <see cref="SimEvent.TargetId"/> which of its lines, <see cref="SimEvent.Position"/> where it was called.
    /// </summary>
    public const SimEventType Callout = (SimEventType)200;
}

/// <summary>
/// Packs the sim's events (and the network's own) for the wire, each kind with only the fields it uses. A copy turns them
/// back into the same events for its sound, splats and HUD.
/// </summary>
public static class EventCodec
{
    [Flags]
    private enum Field : ushort
    {
        None = 0,
        Player = 1 << 0,
        Target = 1 << 1,
        Collider = 1 << 2,
        Sequence = 1 << 3,
        Team = 1 << 4,
        Surface = 1 << 5,
        Position = 1 << 6,
        Normal = 1 << 7,
        Velocity = 1 << 8,
        Value = 1 << 9,
        Extra = 1 << 10,
        Lethal = 1 << 11,
    }

    private const float MaxBallSpeed = 128f;

    /// <summary>Whether the network carries this kind of event at all (the rest each copy works out itself, or doesn't need).</summary>
    public static bool Carried(SimEventType type) => FieldsOf(type) != Field.None;

    private static Field FieldsOf(SimEventType type) => type switch
    {
        SimEventType.ShotFired => Field.Player | Field.Sequence | Field.Team | Field.Position | Field.Velocity | Field.Value | Field.Extra,
        SimEventType.BallBroke => Field.Player | Field.Sequence | Field.Team | Field.Surface | Field.Position | Field.Normal | Field.Velocity |
                                  Field.Value | Field.Target | Field.Collider | Field.Lethal | Field.Extra,
        SimEventType.BallBounced => Field.Player | Field.Sequence | Field.Team | Field.Surface | Field.Position | Field.Normal | Field.Velocity |
                                    Field.Value | Field.Target | Field.Collider,
        SimEventType.BallDespawned => Field.Player | Field.Sequence | Field.Team | Field.Position | Field.Extra,
        SimEventType.PlayerEliminated => Field.Player | Field.Target | Field.Team | Field.Sequence | Field.Position | Field.Normal | Field.Extra,
        SimEventType.MaskSprayed => Field.Player | Field.Target | Field.Team | Field.Position | Field.Value,
        SimEventType.MatchPhaseChanged => Field.Extra,
        SimEventType.RoundEnded => Field.Target | Field.Extra | Field.Value,
        SimEventType.PickupTaken => Field.Player | Field.Target | Field.Team | Field.Extra | Field.Position,
        SimEventType.DoorMoved => Field.Player | Field.Target | Field.Collider | Field.Position | Field.Surface | Field.Value | Field.Extra,
        SimEventType.CaseTaken or SimEventType.CaseDropped => Field.Player | Field.Team | Field.Position,
        SimEventType.CaseExtracted => Field.Player | Field.Team | Field.Position | Field.Extra,
        SimEventType.HoldChanged => Field.Team | Field.Position | Field.Value | Field.Extra,
        SimEventType.BuzzerHanging or SimEventType.BuzzerHung => Field.Player | Field.Team | Field.Position | Field.Value | Field.Extra,
        SimEventType.FlagTaken or SimEventType.FlagDropped or SimEventType.FlagCaptured => Field.Player | Field.Team | Field.Position | Field.Value | Field.Extra,
        NetEventTypes.Callout => Field.Player | Field.Target | Field.Position | Field.Extra,
        SimEventType.CalledOut => Field.Player | Field.Target | Field.Team | Field.Position,
        _ => Field.None,
    };

    /// <summary>Writes <paramref name="e"/>, its tick as how far it came before <paramref name="snapshotTick"/>.</summary>
    public static void Write(BitWriter w, in SimEvent e, int snapshotTick, in PositionQuant grid)
    {
        Field fields = FieldsOf(e.Type);
        w.WriteByte((byte)e.Type);
        w.WriteVarUInt((uint)Math.Max(0, snapshotTick - e.Tick));
        if ((fields & Field.Player) != 0)
        {
            w.WriteVarInt(e.PlayerId);
        }

        if ((fields & Field.Target) != 0)
        {
            w.WriteVarInt(e.TargetId);
        }

        if ((fields & Field.Collider) != 0)
        {
            w.WriteVarInt(e.ColliderId);
        }

        if ((fields & Field.Sequence) != 0)
        {
            w.WriteVarUInt(e.ShotSequence);
        }

        if ((fields & Field.Team) != 0)
        {
            w.WriteBits(e.Team, 4);
        }

        if ((fields & Field.Surface) != 0)
        {
            w.WriteByte(e.Surface.Value);
        }

        if ((fields & Field.Position) != 0)
        {
            grid.Quantize(e.Position, out uint x, out uint y, out uint z);
            w.WriteBits(x, grid.BitsX);
            w.WriteBits(y, grid.BitsY);
            w.WriteBits(z, grid.BitsZ);
        }

        if ((fields & Field.Normal) != 0)
        {
            w.WriteBits(Quant.Range(e.Normal.X, -1f, 1f, 10), 10);
            w.WriteBits(Quant.Range(e.Normal.Y, -1f, 1f, 10), 10);
            w.WriteBits(Quant.Range(e.Normal.Z, -1f, 1f, 10), 10);
        }

        if ((fields & Field.Velocity) != 0)
        {
            w.WriteBits(Quant.Range(e.Velocity.X, -MaxBallSpeed, MaxBallSpeed, 16), 16);
            w.WriteBits(Quant.Range(e.Velocity.Y, -MaxBallSpeed, MaxBallSpeed, 16), 16);
            w.WriteBits(Quant.Range(e.Velocity.Z, -MaxBallSpeed, MaxBallSpeed, 16), 16);
        }

        if ((fields & Field.Value) != 0)
        {
            w.WriteFloat(e.Value);
        }

        if ((fields & Field.Extra) != 0)
        {
            w.WriteVarInt(e.Extra);
        }

        if ((fields & Field.Lethal) != 0)
        {
            w.WriteBool(e.Lethal);
        }
    }

    /// <summary>Reads one event back; false if the packet was short or the event is of a kind the network doesn't carry.</summary>
    public static bool Read(ref BitReader r, int snapshotTick, in PositionQuant grid, out SimEvent e)
    {
        e = new SimEvent { Type = (SimEventType)r.ReadByte(), TargetId = -1, ColliderId = -1, PlayerId = -1 };
        Field fields = FieldsOf(e.Type);
        e.Tick = snapshotTick - (int)r.ReadVarUInt();
        if ((fields & Field.Player) != 0)
        {
            e.PlayerId = r.ReadVarInt();
        }

        if ((fields & Field.Target) != 0)
        {
            e.TargetId = r.ReadVarInt();
        }

        if ((fields & Field.Collider) != 0)
        {
            e.ColliderId = r.ReadVarInt();
        }

        if ((fields & Field.Sequence) != 0)
        {
            e.ShotSequence = r.ReadVarUInt();
        }

        if ((fields & Field.Team) != 0)
        {
            e.Team = (byte)r.ReadBits(4);
        }

        if ((fields & Field.Surface) != 0)
        {
            e.Surface = new SurfaceId(r.ReadByte());
        }

        if ((fields & Field.Position) != 0)
        {
            uint x = r.ReadBits(grid.BitsX), y = r.ReadBits(grid.BitsY), z = r.ReadBits(grid.BitsZ);
            e.Position = grid.Dequantize(x, y, z);
        }

        if ((fields & Field.Normal) != 0)
        {
            var n = new Vector3(Quant.FromRange(r.ReadBits(10), -1f, 1f, 10), Quant.FromRange(r.ReadBits(10), -1f, 1f, 10),
                Quant.FromRange(r.ReadBits(10), -1f, 1f, 10));
            e.Normal = n.LengthSquared() > 1e-6f ? Vector3.Normalize(n) : Vector3.UnitY;
        }

        if ((fields & Field.Velocity) != 0)
        {
            e.Velocity = new Vector3(Quant.FromRange(r.ReadBits(16), -MaxBallSpeed, MaxBallSpeed, 16),
                Quant.FromRange(r.ReadBits(16), -MaxBallSpeed, MaxBallSpeed, 16), Quant.FromRange(r.ReadBits(16), -MaxBallSpeed, MaxBallSpeed, 16));
        }

        if ((fields & Field.Value) != 0)
        {
            e.Value = r.ReadFloat();
        }

        if ((fields & Field.Extra) != 0)
        {
            e.Extra = r.ReadVarInt();
        }

        if ((fields & Field.Lethal) != 0)
        {
            e.Lethal = r.ReadBool();
        }

        return fields != Field.None && !r.Overflowed;
    }
}
