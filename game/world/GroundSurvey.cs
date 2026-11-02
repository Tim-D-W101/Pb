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
/// sphere dropped from the sky lands on the ground there). On a level whose ground rolls (M5.8) the
/// ground's own height and cover come from its <see cref="Terrain"/>, and "at ground level" means
/// within <see cref="GroundTop"/> of it there.
/// </summary>
public sealed class GroundSurvey
{
    /// <summary>Ground surfaces are below this height above the ground (patches and ground-floor slabs are a few millimetres thick).</summary>
    public const float GroundTop = 0.06f;

    /// <summary>The side of the squares the slabs are filed under (m), so a point looks only at the slabs near it.</summary>
    private const float BucketSize = 8f;

    private readonly LevelLayout _level;
    private readonly ICollisionWorld _world;
    private readonly List<LevelPrimitive> _slabs = new();
    private readonly Dictionary<long, List<int>> _buckets = new();
    private readonly float _top;

    public GroundSurvey(LevelLayout level, ICollisionWorld world)
    {
        _level = level;
        _world = world;
        _top = level.Bounds.Max.Y;
        foreach (LevelPrimitive p in level.Primitives)
        {
            // A slab lying on the ground: its middle at most a few centimetres off it (a track's pieces slope with it).
            if ((p.Role is PrimitiveRole.GroundPatch or PrimitiveRole.Floor) && p.HalfExtents.Y < GroundTop &&
                p.Center.Y - level.GroundAt(p.Center.X, p.Center.Z) < GroundTop)
            {
                int index = _slabs.Count;
                _slabs.Add(p);
                Aabb b = p.Bounds;
                for (int bz = Bucket(b.Min.Z); bz <= Bucket(b.Max.Z); bz++)
                {
                    for (int bx = Bucket(b.Min.X); bx <= Bucket(b.Max.X); bx++)
                    {
                        long key = Key(bx, bz);
                        if (!_buckets.TryGetValue(key, out List<int>? list))
                        {
                            _buckets[key] = list = new List<int>();
                        }

                        list.Add(index); // in order, so the last slab over a point is the last in its square's list
                    }
                }
            }
        }
    }

    private static int Bucket(float v) => (int)MathF.Floor(v / BucketSize);

    private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

    /// <summary>The height of the ground itself at (x, z): the rolling ground's, else 0.</summary>
    public float GroundAt(float x, float z) => _level.GroundAt(x, z);

    /// <summary>Whether a surface at height <paramref name="y"/> over (x, z) is up off the ground (a raised floor, a platform), not on it.</summary>
    public bool Raised(float x, float z, float y) => y >= GroundAt(x, z) + GroundTop;

    /// <summary>The ground material at (x, z): a ground-floor slab beats a ground patch beats the level's ground.</summary>
    public string MaterialAt(float x, float z) => SurfaceAt(x, z, out _);

    /// <summary>The ground material at (x, z), and the height of its surface.</summary>
    public string SurfaceAt(float x, float z, out float y)
    {
        float ground = GroundAt(x, z);
        y = ground;
        Aabb b = _level.Bounds;
        if (x < b.Min.X || x > b.Max.X || z < b.Min.Z || z > b.Max.Z)
        {
            return LevelBuilder.SurroundingsMaterial;
        }

        string? patch = null;
        float patchTop = ground;
        List<int>? near = _buckets.TryGetValue(Key(Bucket(x), Bucket(z)), out List<int>? found) ? found : null;
        for (int k = (near?.Count ?? 0) - 1; k >= 0; k--)
        {
            LevelPrimitive s = _slabs[near![k]];
            SVector3 local = SVector3.Transform(new SVector3(x - s.Center.X, 0f, z - s.Center.Z), SQuaternion.Conjugate(s.Rotation));
            if (MathF.Abs(local.X) > s.HalfExtents.X || MathF.Abs(local.Z) > s.HalfExtents.Z)
            {
                continue;
            }

            if (s.Role == PrimitiveRole.Floor)
            {
                y = MathF.Max(TopAt(s, x, z), ground);
                return _level.Materials[s.Material].Id;
            }

            if (patch is null)
            {
                patch = _level.Materials[s.Material].Id;
                patchTop = MathF.Max(TopAt(s, x, z), ground);
            }
        }

        y = patchTop;
        if (patch is not null)
        {
            return patch;
        }

        // The rolling ground's own cover there (a wood's leaf litter, a yard, the brook's mud).
        if (_level.Terrain is { } terrain)
        {
            int c = Math.Clamp((int)MathF.Round((x - terrain.MinX) / terrain.Cell), 0, terrain.Cols - 1);
            int r = Math.Clamp((int)MathF.Round((z - terrain.MinZ) / terrain.Cell), 0, terrain.Rows - 1);
            return _level.Materials[terrain.MaterialOf(terrain.KindOf(c, r))].Id;
        }

        return _level.GroundMaterial.Id;
    }

    /// <summary>The height of a slab's top face over (x, z) (a track's piece slopes along the ground).</summary>
    private static float TopAt(LevelPrimitive s, float x, float z)
    {
        SVector3 up = SVector3.Transform(SVector3.UnitY, s.Rotation);
        SVector3 top = s.Center + up * s.HalfExtents.Y;
        return up.Y > 0.5f ? top.Y - (up.X * (x - top.X) + up.Z * (z - top.Z)) / up.Y : s.Bounds.Max.Y;
    }

    /// <summary>
    /// True when a sphere of <paramref name="radius"/> dropped from the sky lands flat on the ground at
    /// (x, z), so nothing overhead (roof, floor or prop) covers it; <paramref name="y"/> is where its
    /// bottom comes to rest.
    /// </summary>
    public bool OpenGround(float x, float z, float radius, out float y)
    {
        float ground = GroundAt(x, z);
        y = ground;
        if (!_world.SweepSphere(new SVector3(x, _top, z), new SVector3(x, ground - 0.5f, z), radius, out SweepHit hit) || hit.Normal.Y < 0.9f)
        {
            return false;
        }

        y = hit.Point.Y - radius;
        return y < ground + GroundTop;
    }
}
