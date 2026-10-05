using System.Numerics;

namespace Pb.Sim.Level;

/// <summary>
/// A railway track (the level file's <c>tracks</c>): its points along the middle, at the rails' foot; the gauge between
/// the rails' inside faces and the rail profile; and what its rails and sleepers are made of. The rails are paint-only
/// primitives of the level (so paint and sight meet them and feet step over them); the game draws them and the sleepers.
/// </summary>
public sealed class TrackSpec
{
    public required IReadOnlyList<Vector3> Points { get; init; }

    public required float Gauge { get; init; }

    public required float RailHeight { get; init; }

    public required float RailWidth { get; init; }

    public required KitMaterial RailMaterial { get; init; }

    public required KitMaterial SleeperMaterial { get; init; }
}
