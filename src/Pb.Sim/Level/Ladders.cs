using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Players;

namespace Pb.Sim.Level;

/// <summary>A prop's ladder in the prop's frame (kit/props.jsonc "ladders"), in SI units; the game draws it from these numbers.</summary>
public sealed class LadderTemplate
{
    /// <summary>The middle of the ladder's bottom, in the plane of its rungs.</summary>
    public required Vector3 Foot { get; init; }

    public required float Height { get; init; }

    /// <summary>The way a climber on it faces (rad; 0 = −Z, positive turns left).</summary>
    public required float Facing { get; init; }

    public required float Width { get; init; }

    /// <summary>How far the stiles carry on above the top (drawn only).</summary>
    public required float Rails { get; init; }

    /// <summary>How far past the rungs stepping off at the top takes you.</summary>
    public required float Exit { get; init; }

    /// <summary>How far back from the rungs its brackets reach (drawn only).</summary>
    public required float Bracket { get; init; }
}

/// <summary>
/// A ladder as it stands in a level, in world space. A climber hangs out in front of the rungs, facing them along
/// <see cref="Forward"/>; at the top they step on past the rungs by <see cref="Exit"/>, onto what it climbs to.
/// </summary>
public sealed class LadderSpec
{
    public required Vector3 Foot { get; init; }

    public required float Height { get; init; }

    public required float Facing { get; init; }

    public required float Width { get; init; }

    public required float Exit { get; init; }

    /// <summary>How far the stiles carry on above the top (drawn only: handholds).</summary>
    public float Rails { get; init; }

    /// <summary>What the rungs are made of: what a foot on one sounds like.</summary>
    public required SurfaceId Surface { get; init; }

    /// <summary>Index into the level's owners: the prop it's on.</summary>
    public required int Owner { get; init; }

    /// <summary>The height of the floor at the top.</summary>
    public float TopY => Foot.Y + Height;

    /// <summary>Horizontal unit vector the way a climber faces: into the ladder.</summary>
    public Vector3 Forward => ViewAngles.FlatForward(Facing);

    /// <summary>Horizontal unit vector to a climber's right.</summary>
    public Vector3 Across => ViewAngles.Right(Facing);

    /// <summary>Where a climber's feet are when they're <paramref name="y"/> up it, <paramref name="standoff"/> out from the rungs.</summary>
    public Vector3 ClimbPoint(float y, float standoff)
    {
        Vector3 forward = Forward;
        return new Vector3(Foot.X - forward.X * standoff, y, Foot.Z - forward.Z * standoff);
    }

    /// <summary>Where stepping off at the top puts your feet.</summary>
    public Vector3 TopPoint
    {
        get
        {
            Vector3 forward = Forward;
            return new Vector3(Foot.X + forward.X * Exit, TopY, Foot.Z + forward.Z * Exit);
        }
    }

    /// <summary>How far <paramref name="feet"/> are out in front of the rungs (negative: behind them, over the top).</summary>
    public float Ahead(Vector3 feet)
    {
        Vector3 forward = Forward;
        return -((feet.X - Foot.X) * forward.X + (feet.Z - Foot.Z) * forward.Z);
    }

    /// <summary>How far <paramref name="feet"/> are to a climber's right of the ladder's middle.</summary>
    public float Aside(Vector3 feet)
    {
        Vector3 across = Across;
        return (feet.X - Foot.X) * across.X + (feet.Z - Foot.Z) * across.Z;
    }
}

/// <summary>
/// The level's ladders and the rule for getting on one. Climbing itself is part of the movement rules
/// (<see cref="MovementModel"/>), so bots, the game and headless runs all climb alike.
/// </summary>
public sealed class LadderSet
{
    /// <summary>How far to either side of a ladder you can still reach it, beyond its stiles.</summary>
    private const float SideReach = 0.3f;

    private LadderSpec[] _ladders = Array.Empty<LadderSpec>();

    public int Count => _ladders.Length;

    public LadderSpec this[int index] => _ladders[index];

    public void Load(IReadOnlyList<LadderSpec> ladders) => _ladders = ladders.ToArray();

    /// <summary>
    /// The ladder interact would put someone standing at <paramref name="feet"/> and facing <paramref name="yaw"/> on, or −1:
    /// in front of it within reach and facing it, anywhere from its foot to a metre under its top; or, to climb down,
    /// behind its top within reach and facing out over it (<paramref name="fromTop"/>). The nearest wins.
    /// </summary>
    public int FindGrab(Vector3 feet, float yaw, ClimbParams p, out bool fromTop)
    {
        fromTop = false;
        int best = -1;
        float bestDistance = float.MaxValue;
        Vector3 facing = ViewAngles.FlatForward(yaw);
        float cos = MathF.Cos(p.GrabAngle);
        for (int i = 0; i < _ladders.Length; i++)
        {
            LadderSpec l = _ladders[i];
            float aside = l.Aside(feet);
            if (MathF.Abs(aside) > l.Width * 0.5f + SideReach)
            {
                continue;
            }

            Vector3 forward = l.Forward;
            float toward = facing.X * forward.X + facing.Z * forward.Z;
            float ahead = l.Ahead(feet);
            bool below = ahead > 0.05f && ahead <= p.Reach && toward >= cos && feet.Y >= l.Foot.Y - 0.6f && feet.Y <= l.TopY - 1f;
            bool above = ahead < 0f && -ahead <= p.Reach + l.Exit && toward <= -cos && feet.Y >= l.TopY - 0.3f && feet.Y <= l.TopY + 0.6f;
            if (!below && !above)
            {
                continue;
            }

            float distance = MathF.Abs(ahead) + MathF.Abs(aside);
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
                fromTop = above;
            }
        }

        return best;
    }
}
