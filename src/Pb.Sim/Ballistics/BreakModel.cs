using Pb.Sim.Collision;

namespace Pb.Sim.Ballistics;

/// <summary>How one surface treats an impact. Built from break_model.jsonc.</summary>
public readonly record struct SurfaceResponse(float Break50, float BreakWidth, float Restitution, float TangentRetain);

/// <summary>
/// Break-vs-bounce curve (spec §1.1). With <c>vₙ</c> the impact speed along the surface normal:
/// <c>P(break) = 1 / (1 + e^(−(vₙ − Break50) / BreakWidth))</c>. Fast, head-on hits almost always
/// break; slow or glancing ones mostly bounce. A bounce scales the normal component by
/// <see cref="SurfaceResponse.Restitution"/> and the tangential one by <see cref="SurfaceResponse.TangentRetain"/>.
/// </summary>
public sealed class BreakModel
{
    private readonly SurfaceResponse[] _responses;

    public BreakModel(SurfaceRegistry surfaces, IReadOnlyList<SurfaceResponse> responses, float restSpeed, int maxBounces)
    {
        if (responses.Count != surfaces.Count)
        {
            throw new ArgumentException("One response per registered surface is required.");
        }

        Surfaces = surfaces;
        _responses = responses.ToArray();
        RestSpeed = restSpeed;
        MaxBounces = maxBounces;
    }

    public SurfaceRegistry Surfaces { get; }

    /// <summary>A bounced ball slower than this is considered at rest and removed (m/s).</summary>
    public float RestSpeed { get; }

    /// <summary>A ball is removed after this many bounces.</summary>
    public int MaxBounces { get; }

    public ref readonly SurfaceResponse Response(SurfaceId surface) => ref _responses[surface.Value];

    public float BreakProbability(SurfaceId surface, float normalSpeed)
    {
        ref readonly SurfaceResponse r = ref _responses[surface.Value];
        return 1f / (1f + MathF.Exp(-(normalSpeed - r.Break50) / r.BreakWidth));
    }
}
