using System.Numerics;
using Pb.Sim.Core;
using Pb.Sim.Data;

namespace Pb.Sim.Level;

/// <summary>One tree of a wood: where its foot is, its trunk's radius, its height, and a number for how it's drawn.</summary>
public readonly record struct TreeSpec(Vector3 Foot, float Radius, float Height, int Look);

/// <summary>A road: its line on the plan (at ground level), its width and what it's made of.</summary>
public sealed class RoadSpec
{
    public required string Name { get; init; }

    public required IReadOnlyList<Vector3> Points { get; init; }

    public required float Width { get; init; }

    public required KitMaterial Material { get; init; }
}

/// <summary>A vehicle standing somewhere when a round starts.</summary>
public readonly record struct VehicleSpot(string Vehicle, Vector3 Position, float Yaw);

/// <summary>
/// Grows a wood's trees (<see cref="TreeStandDef"/>): one to each cell of a grid <c>spacing</c> on a side, jittered within
/// it, a share of cells left empty, none in the clearings or too near another tree; sizes between the least and the most.
/// Deterministic: the same file grows the same wood on every copy.
/// </summary>
public static class WoodsGrower
{
    /// <summary>Trunk cylinders reach this share of a tree's height (above them, the crown is looks only).</summary>
    public const float TrunkShare = 0.55f;

    /// <summary>Two trunks' centres at least this many of the larger's radius apart, with room to walk between.</summary>
    private const float Apart = 1.1f;

    public static List<TreeSpec> Grow(TreeStandDef def)
    {
        var trees = new List<TreeSpec>();
        if (def.Rect_m.Length != 4 || def.TrunkRadius_m.Length != 2 || def.Height_m.Length != 2)
        {
            return trees; // reported by the validation
        }

        float x0 = MathF.Min(def.Rect_m[0], def.Rect_m[2]), x1 = MathF.Max(def.Rect_m[0], def.Rect_m[2]);
        float z0 = MathF.Min(def.Rect_m[1], def.Rect_m[3]), z1 = MathF.Max(def.Rect_m[1], def.Rect_m[3]);
        var rng = new Pcg32(SeedHash.Combine((ulong)(uint)def.Seed, 0x7EEE5));
        float cell = def.Spacing_m;
        for (float z = z0; z < z1; z += cell)
        {
            for (float x = x0; x < x1; x += cell)
            {
                float gap = rng.NextFloat();
                float px = x + cell * (0.1f + 0.8f * rng.NextFloat()), pz = z + cell * (0.1f + 0.8f * rng.NextFloat());
                float size = rng.NextFloat();
                float radius = def.TrunkRadius_m[0] + (def.TrunkRadius_m[1] - def.TrunkRadius_m[0]) * size * size;
                float height = def.Height_m[0] + (def.Height_m[1] - def.Height_m[0]) * (0.5f * size + 0.5f * rng.NextFloat());
                int look = (int)(rng.NextUInt() & 0x7FFFFFFF);
                if (gap < def.Gaps || px > x1 || pz > z1 || Cleared(def, px, pz, radius))
                {
                    continue;
                }

                var foot = new Vector3(px, 0f, pz);
                bool crowded = false;
                for (int i = Math.Max(0, trees.Count - 64); i < trees.Count && !crowded; i++)
                {
                    float need = (MathF.Max(radius, trees[i].Radius) + 0.45f) * 2f * Apart;
                    crowded = Vector3.DistanceSquared(trees[i].Foot, foot) < need * need;
                }

                if (!crowded)
                {
                    trees.Add(new TreeSpec(foot, radius, height, look));
                }
            }
        }

        return trees;
    }

    private static bool Cleared(TreeStandDef def, float x, float z, float radius)
    {
        for (int i = 0; def.Clear_m is not null && i < def.Clear_m.Length; i++)
        {
            float[] r = def.Clear_m[i];
            if (r.Length == 4 && x >= MathF.Min(r[0], r[2]) - radius && x <= MathF.Max(r[0], r[2]) + radius &&
                z >= MathF.Min(r[1], r[3]) - radius && z <= MathF.Max(r[1], r[3]) + radius)
            {
                return true;
            }
        }

        return false;
    }
}
