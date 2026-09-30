using System.Numerics;
using Pb.Sim.Gear;

namespace Pb.Sim.Players;

public enum Stance : byte
{
    Standing,
    Crouching,
}

/// <summary>Authoritative state of one participant. Position is at the feet.</summary>
public sealed class PlayerState
{
    public PlayerState(int id, byte team, Marker marker)
    {
        Id = id;
        Team = team;
        Marker = marker;
    }

    public int Id { get; }

    public byte Team { get; }

    public Marker Marker { get; }

    public bool Alive { get; set; } = true;

    public Vector3 Position { get; set; }

    public Vector3 Velocity { get; set; }

    public float Yaw { get; set; }

    public float Pitch { get; set; }

    public Stance Stance { get; set; }

    /// <summary>Current eye height above the feet (smoothed between stances).</summary>
    public float EyeHeight { get; set; }

    public bool Sprinting { get; set; }

    public Vector3 EyePosition => Position + new Vector3(0f, EyeHeight, 0f);

    public float HorizontalSpeed => new Vector2(Velocity.X, Velocity.Z).Length();
}
