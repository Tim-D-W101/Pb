using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Level;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.World;

/// <summary>
/// Skirting boards along the foot of the buildings' inside walls (each template's <c>"skirting"</c>): a
/// board along every face of the building's upright wall pieces that stands on a floor and looks into
/// one of the level's indoor areas, so it stops at doorways (the wall is in pieces round them) and runs
/// under windows. Looks only, merged into the props' meshes.
/// </summary>
public static class Skirting
{
    private const float Height = 0.1f, Depth = 0.018f;

    /// <summary>Adds the boards to <paramref name="meshFor"/>; returns how many faces got one.</summary>
    public static int Build(LevelLayout level, Func<string, int> material, Func<Vector3, ShapeMesh> meshFor)
    {
        var byOwner = new Dictionary<int, int>();
        foreach (PlacedBuilding b in level.Buildings)
        {
            if (b.Template.Def.Skirting is { } id && material(id) is var m and >= 0)
            {
                byOwner[b.Owner] = m;
            }
        }

        if (byOwner.Count == 0)
        {
            return 0;
        }

        var floors = new List<LevelPrimitive>();
        foreach (LevelPrimitive p in level.Primitives)
        {
            if (p.Role == PrimitiveRole.Floor && p.Kind == PrimitiveKind.Box)
            {
                floors.Add(p);
            }
        }

        int boards = 0;
        foreach (LevelPrimitive p in level.Primitives)
        {
            if (p.Role != PrimitiveRole.Wall || p.Kind != PrimitiveKind.Box || !byOwner.TryGetValue(p.Owner, out int board))
            {
                continue;
            }

            Vector3 x = SVector3.Transform(SVector3.UnitX, p.Rotation).ToGodot();
            Vector3 y = SVector3.Transform(SVector3.UnitY, p.Rotation).ToGodot();
            Vector3 z = SVector3.Transform(SVector3.UnitZ, p.Rotation).ToGodot();
            if (y.Y < 0.99f || p.HalfExtents.Y < 0.25f)
            {
                continue;
            }

            bool longX = p.HalfExtents.X >= p.HalfExtents.Z;
            Vector3 along = longX ? x : z, normal = longX ? z : x;
            float half = longX ? p.HalfExtents.X : p.HalfExtents.Z, thick = longX ? p.HalfExtents.Z : p.HalfExtents.X;
            if (half < 0.12f)
            {
                continue;
            }

            Vector3 centre = p.Center.ToGodot();
            float foot = centre.Y - p.HalfExtents.Y;
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 n = normal * side;
                Vector3 face = new Vector3(centre.X, foot, centre.Z) + n * thick;
                Vector3 probe = face + n * 0.35f + Vector3.Up * 0.3f;
                if (!Indoor(level, probe) || !OnFloor(floors, face + n * 0.1f, foot))
                {
                    continue;
                }

                ShapeMesh mesh = meshFor(face);
                mesh.Place(Transform3D.Identity, Height);
                mesh.Box(board, face + n * (Depth * 0.5f) + Vector3.Up * (Height * 0.5f), new Vector3(half * 2f, Height, Depth), new Basis(along, Vector3.Up, along.Cross(Vector3.Up)));
                boards++;
            }
        }

        return boards;
    }

    private static bool Indoor(LevelLayout level, Vector3 at)
    {
        foreach (AreaSpec area in level.Areas)
        {
            Pb.Sim.Collision.Aabb b = area.Box;
            if (area.Indoor && at.X >= b.Min.X && at.X <= b.Max.X && at.Y >= b.Min.Y && at.Y <= b.Max.Y && at.Z >= b.Min.Z && at.Z <= b.Max.Z)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a floor's top lies within a few centimetres of <paramref name="foot"/> under this point.</summary>
    private static bool OnFloor(List<LevelPrimitive> floors, Vector3 at, float foot)
    {
        foreach (LevelPrimitive f in floors)
        {
            Pb.Sim.Collision.Aabb b = f.Bounds;
            if (at.X >= b.Min.X && at.X <= b.Max.X && at.Z >= b.Min.Z && at.Z <= b.Max.Z && MathF.Abs(b.Max.Y - foot) < 0.08f)
            {
                return true;
            }
        }

        return false;
    }
}
