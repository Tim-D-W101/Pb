using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Gear;

namespace Pb.Sim.Players;

public enum Stance : byte
{
    Standing,
    Crouching,

    /// <summary>A slide from a run; it always ends crouched.</summary>
    Sliding,
}

/// <summary>Where someone on a ladder is with it.</summary>
public enum LadderPhase : byte
{
    /// <summary>On the rungs, going up or down or hanging there.</summary>
    Climbing,

    /// <summary>At the top, stepping on over it onto what it climbs to.</summary>
    SteppingOff,

    /// <summary>At the top, stepping back out over it onto the rungs to climb down.</summary>
    GettingOn,
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

    /// <summary>Display name (callouts, kill feed, spectator view).</summary>
    public string Name { get; set; } = "";

    /// <summary>Still in the round: false once eliminated.</summary>
    public bool Alive { get; set; } = true;

    /// <summary>On the field at all: an eliminated player is present until they've walked off.</summary>
    public bool Present { get; set; } = true;

    /// <summary>Playing with others: they left the game mid-round (<see cref="SimWorld.Withdraw"/>).</summary>
    public bool Left { get; internal set; }

    /// <summary>The callout key was held last tick, and when the last callout went (sim time): the authority's own record.</summary>
    internal bool CalloutHeld { get; set; }

    internal double CalledOutAt { get; set; } = double.NegativeInfinity;

    /// <summary>Can't sprint (carrying the case, when the rules say a carrier can't).</summary>
    public bool SprintBlocked { get; set; }

    /// <summary>Who eliminated this player (−1 = nobody yet), when, and where the ball broke.</summary>
    public int EliminatedBy { get; set; } = -1;

    public int EliminatedTick { get; set; } = -1;

    public HitboxPart EliminatedPart { get; set; }

    /// <summary>Opponents this player has eliminated.</summary>
    public int Eliminations { get; set; }

    /// <summary>Balls of theirs that broke on an opponent this round (the scoreboard; the round's stats keep the same).</summary>
    public int Hits { get; set; }

    public Vector3 Position { get; set; }

    public Vector3 Velocity { get; set; }

    public float Yaw { get; set; }

    public float Pitch { get; set; }

    public Stance Stance { get; set; }

    /// <summary>Current eye height above the feet (smoothed between stances), before any lean.</summary>
    public float EyeHeight { get; set; }

    public bool Sprinting { get; set; }

    /// <summary>On the ground after this tick's collide-and-slide (written by the host).</summary>
    public bool Grounded { get; set; } = true;

    /// <summary>−1 = leaning fully left, 0 = upright, +1 = fully right.</summary>
    public float Lean { get; set; }

    /// <summary>World-space shift of the eye caused by the lean (set by <see cref="MovementModel"/>).</summary>
    public Vector3 LeanOffset { get; set; }

    /// <summary>Body roll from the lean (rad, positive = leaning right).</summary>
    public float LeanRoll { get; set; }

    /// <summary>
    /// Which side the marker is on: +1 = right shoulder, −1 = left, in between while swapping.
    /// The muzzle offset's sideways component is scaled by it.
    /// </summary>
    public float Shoulder { get; set; } = 1f;

    /// <summary>The side the marker is moving to (+1 right, −1 left).</summary>
    public float ShoulderTarget { get; set; } = 1f;

    /// <summary>
    /// How far the head is turned from the aim (rad, positive to the left): the head and mask turn with it, and a
    /// bot sees where its head looks. 0 once out.
    /// </summary>
    public float HeadYaw { get; set; }

    /// <summary>
    /// How far the marker is pitched up off a wall in front (rad, 0 = shouldered): close to a wall the
    /// gear comes up about the back of the marker until the barrel clears it, rather than poke through.
    /// </summary>
    public float Tuck { get; set; }

    /// <summary>Seconds into the current slide.</summary>
    public float SlideTime { get; set; }

    public float SlideCooldown { get; set; }

    public float JumpCooldown { get; set; }

    /// <summary>Seconds until the marker is back up after sprinting.</summary>
    public float SprintRecovery { get; set; }

    /// <summary>Buttons held last tick, for edge detection (swap shoulder, slide, jump).</summary>
    public InputButtons PreviousButtons { get; set; }

    /// <summary>Surface under the feet, from the last time the player was on the ground.</summary>
    public SurfaceId GroundSurface { get; set; }

    /// <summary>Ground covered since the last footstep (m).</summary>
    public float StrideDistance { get; set; }

    /// <summary>Last tick's position, velocity, grounded flag and stance (footsteps and landings).</summary>
    internal Vector3 LastPosition { get; set; }

    internal Vector3 LastVelocity { get; set; }

    internal bool LastGrounded { get; set; } = true;

    internal Stance LastStance { get; set; }

    /// <summary>
    /// The ladder being climbed (an index into the sim's <see cref="Level.LadderSet"/>; −1: none) and what they're doing on
    /// it. On a ladder both hands are on the rungs: no firing, refilling, crouching, leaning or sprinting.
    /// </summary>
    public int Ladder { get; internal set; } = -1;

    public LadderPhase LadderPhase { get; internal set; }

    /// <summary>Seconds into stepping off or getting on at the top.</summary>
    internal float LadderTime { get; set; }

    /// <summary>Height climbed since the last foot on a rung (m).</summary>
    internal float ClimbDistance { get; set; }

    public bool OnLadder => Ladder >= 0;

    /// <summary>The door being worked with the interact button (−1: none), and for how long it's been held.</summary>
    public int InteractDoor { get; internal set; } = -1;

    internal float InteractHeld { get; set; }

    internal bool InteractDown { get; set; }

    /// <summary>The marker is up and settled: not mid-swap and not recovering from a sprint.</summary>
    public bool MarkerReady => SprintRecovery <= 0f && MathF.Abs(Shoulder) >= 1f;

    public Vector3 EyePosition => Position + new Vector3(0f, EyeHeight, 0f) + LeanOffset;

    public float HorizontalSpeed => new Vector2(Velocity.X, Velocity.Z).Length();
}
