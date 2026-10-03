using System;
using System.Collections.Generic;
using Pb.Sim.Collision;
using Pb.Sim.Level;
using Aabb = Pb.Sim.Collision.Aabb;
using SQuaternion = System.Numerics.Quaternion;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.World;

/// <summary>
/// What the ground is like at a point of a level, for the things that dress it (weeds, stains):
/// which material it is, how high its surface lies, and whether rain could fall on it (a small
/// sphere dropped from the sky lands on the ground there).
/// </summary>
public sealed class GroundSurvey
{
    /// <summary>Ground surfaces are below this height (patches and ground-floor slabs are a few millimetres thick).</summary>
    public const float GroundTop = 0.06f;

    private readonly LevelLayout _level;
    private readonly ICollisionWorld _world;
    private readonly List<LevelPrimitive> _slabs = new();
    private readonly float _top;

    public GroundSurvey(LevelLayout level, ICollisionWorld world)
    {
        _level = level;
        _world = world;
        _top = level.Bounds.Max.Y;
        foreach (LevelPrimitive p in level.Primitives)
        {
            if ((p.Role is PrimitiveRole.GroundPatch or PrimitiveRole.Floor) && p.Bounds.Max.Y < GroundTop)
            {
                _slabs.Add(p);
            }
        }
    }

    /// <summary>The ground material at (x, z): a ground-floor slab beats a ground patch beats the level's ground.</summary>
    public string MaterialAt(float x, float z) => SurfaceAt(x, z, out _);

    /// <summary>The ground material at (x, z), and the height of its surface.</summary>
    public string SurfaceAt(float x, float z, out float y)
    {
        y = 0f;
        Aabb b = _level.Bounds;
        if (x < b.Min.X || x > b.Max.X || z < b.Min.Z || z > b.Max.Z)
        {
            return LevelBuilder.SurroundingsMaterial;
        }

        string? patch = null;
        float patchTop = 0f;
        for (int i = _slabs.Count - 1; i >= 0; i--)
        {
            LevelPrimitive s = _slabs[i];
            SVector3 local = SVector3.Transform(new SVector3(x - s.Center.X, 0f, z - s.Center.Z), SQuaternion.Conjugate(s.Rotation));
            if (MathF.Abs(local.X) > s.HalfExtents.X || MathF.Abs(local.Z) > s.HalfExtents.Z)
            {
                continue;
            }

            if (s.Role == PrimitiveRole.Floor)
            {
                y = MathF.Max(s.Bounds.Max.Y, 0f);
                return _level.Materials[s.Material].Id;
            }

            if (patch is null)
            {
                patch = _level.Materials[s.Material].Id;
                patchTop = MathF.Max(s.Bounds.Max.Y, 0f);
            }
        }

        y = patchTop;
        return patch ?? _level.GroundMaterial.Id;
    }

    /// <summary>
    /// True when a sphere of <paramref name="radius"/> dropped from the sky lands flat on the ground at
    /// (x, z), so nothing overhead (roof, floor or prop) covers it; <paramref name="y"/> is where its
    /// bottom comes to rest.
    /// </summary>
    public bool OpenGround(float x, float z, float radius, out float y)
    {
        y = 0f;
        if (!_world.SweepSphere(new SVector3(x, _top, z), new SVector3(x, -0.5f, z), radius, out SweepHit hit) || hit.Normal.Y < 0.9f)
        {
            return false;
        }

        y = hit.Point.Y - radius;
        return y < GroundTop;
    }
}
