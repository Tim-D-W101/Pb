using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Data;

namespace Pb.Sim.Level;

/// <summary>Where the case may start, and the indoor area it's in (its building, the part the HUD marks).</summary>
public readonly record struct CaseSpot(Vector3 Position, string Area, Aabb AreaBox);

/// <summary>A way out with the case.</summary>
public readonly record struct ExitSpec(string Name, Vector3 Position);

/// <summary>A room to hold: every area of one name.</summary>
public sealed class HoldRoom
{
    public required string Name { get; init; }

    public required IReadOnlyList<Aabb> Boxes { get; init; }

    /// <summary>The middle of the room's floor (of its biggest part).</summary>
    public required Vector3 Centre { get; init; }

    public bool Contains(Vector3 point)
    {
        // An indexed loop: asked for every player every tick.
        for (int i = 0; i < Boxes.Count; i++)
        {
            if (Boxes[i].Contains(point))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Where a level's objectives are played (the level file's <c>objectives</c>).</summary>
public sealed class LevelObjectives
{
    public static readonly LevelObjectives None = new()
    {
        CaseSpots = Array.Empty<CaseSpot>(), Exits = Array.Empty<ExitSpec>(), Rooms = Array.Empty<HoldRoom>(),
    };

    public required IReadOnlyList<CaseSpot> CaseSpots { get; init; }

    public required IReadOnlyList<ExitSpec> Exits { get; init; }

    public required IReadOnlyList<HoldRoom> Rooms { get; init; }

    /// <summary>Whether the level has what <paramref name="kind"/> needs.</summary>
    public bool Offers(ObjectiveKind kind) => kind switch
    {
        ObjectiveKind.Retrieve => CaseSpots.Count > 0 && Exits.Count > 0,
        ObjectiveKind.Hold => Rooms.Count > 0,
        _ => true,
    };
}
