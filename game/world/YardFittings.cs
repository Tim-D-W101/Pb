using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Collision;
using Pb.Sim.Level;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.World;

/// <summary>
/// Cast-iron fittings set into the yard (presentation.jsonc "yardFittings"): round manhole covers in
/// their frames, ribbed, with two lifting slots, out in the open; and drain gratings, bars over a dark
/// hole, along the foot of the buildings' outside walls and here and there in the yard. Each sits on
/// one of the listed ground materials under the open sky, flat, with nothing standing on it; seeded by
/// the level. Flush with the ground and looks only: paint and feet meet the ground beneath.
/// </summary>
public partial class YardFittings : Node3D
{
    private const int Iron = 0, Void = 1;

    public int Count { get; private set; }

    public void Build(LevelLayout level, ICollisionWorld world, YardFittingsDef def, MaterialLibrary materials)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        int iron = -1;
        foreach (KitMaterial m in level.Materials)
        {
            if (m.Id == def.Material)
            {
                iron = m.Index;
            }
        }

        Count = 0;
        if (iron < 0)
        {
            GD.PushWarning($"yardFittings: no kit material '{def.Material}'");
            return;
        }

        var survey = new GroundSurvey(level, world);
        var on = new HashSet<string>(def.On, StringComparer.Ordinal);
        var random = new Random(LevelBuilder.StableHash(level.Id) ^ 0xD8A1);
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
        var taken = new List<(Vector2 At, float Radius)>();
        var mesh = new ShapeMesh();
        mesh.Place(Transform3D.Identity, 0.05f);

        bool Spaced(float x, float z, float radius)
        {
            foreach ((Vector2 other, float r) in taken)
            {
                if (new Vector2(x, z).DistanceTo(other) < r + radius + 1.5f)
                {
                    return false;
                }
            }

            return true;
        }

        // Somewhere it can go: on the listed ground, flat, open to the sky, clear of everything standing
        // and of the fittings already down.
        bool Fits(float x, float z, float radius, out float y)
        {
            y = 0f;
            if (!Spaced(x, z, radius))
            {
                return false;
            }

            if (!on.Contains(survey.SurfaceAt(x, z, out y)) || y >= GroundSurvey.GroundTop || !survey.OpenGround(x, z, 0.05f, out _))
            {
                return false;
            }

            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.Tau * i / 6f;
                if (!on.Contains(survey.SurfaceAt(x + MathF.Cos(a) * radius, z + MathF.Sin(a) * radius, out float edge)) || MathF.Abs(edge - y) > 0.004f)
                {
                    return false;
                }
            }

            // Nothing standing within reach of it, from knee height up.
            return !world.SweepSphere(new SVector3(x, 1.4f, z), new SVector3(x, y + radius + 0.35f, z), radius + 0.3f, out _);
        }

        Pb.Sim.Collision.Aabb b = level.Bounds;
        for (int placed = 0, attempt = 0; placed < def.Manholes && attempt < def.Manholes * 60; attempt++)
        {
            float radius = R(def.ManholeRadius_m[0], def.ManholeRadius_m[1]);
            float x = R(b.Min.X, b.Max.X), z = R(b.Min.Z, b.Max.Z);
            if (Fits(x, z, radius + 0.06f, out float y))
            {
                Manhole(mesh, new Vector3(x, y, z), radius, R(0f, Mathf.Tau));
                taken.Add((new Vector2(x, z), radius + 0.06f));
                placed++;
            }
        }

        // Drains: most at the foot of an outside wall, lying along it; the rest out in the yard.
        var faces = new List<(Vector3 Centre, Vector3 Along, Vector3 Out, float Half)>();
        foreach (LevelPrimitive p in level.Primitives)
        {
            if (p.Role != PrimitiveRole.Wall || p.Kind != PrimitiveKind.Box || p.Bounds.Min.Y > 0.1f || p.Height < 1.5f || p.HalfExtents.X < 0.6f && p.HalfExtents.Z < 0.6f)
            {
                continue;
            }

            Vector3 x = SVector3.Transform(SVector3.UnitX, p.Rotation).ToGodot(), z = SVector3.Transform(SVector3.UnitZ, p.Rotation).ToGodot();
            bool longX = p.HalfExtents.X >= p.HalfExtents.Z;
            Vector3 along = longX ? x : z, across = longX ? z : x;
            float half = longX ? p.HalfExtents.X : p.HalfExtents.Z, thick = longX ? p.HalfExtents.Z : p.HalfExtents.X;
            foreach (float side in new[] { -1f, 1f })
            {
                faces.Add((p.Center.ToGodot() + across * (side * thick), along, across * side, half));
            }
        }

        for (int placed = 0, attempt = 0; placed < def.Drains && attempt < def.Drains * 60; attempt++)
        {
            float length = R(def.DrainSize_m[0], def.DrainSize_m[1]), width = length * R(0.55f, 0.7f);
            Vector3 at;
            float yaw;
            bool atWall = faces.Count > 0 && random.NextDouble() < def.AlongWalls;
            if (atWall)
            {
                (Vector3 centre, Vector3 along, Vector3 out_, float half) = faces[random.Next(faces.Count)];
                at = centre + along * (R(-1f, 1f) * MathF.Max(half - length, 0f)) + out_ * (width * 0.5f + 0.06f);
                yaw = MathF.Atan2(along.X, along.Z);
            }
            else
            {
                at = new Vector3(R(b.Min.X, b.Max.X), 0f, R(b.Min.Z, b.Max.Z));
                yaw = R(0f, Mathf.Tau);
            }

            // Against a wall the sphere test would always find the wall: check just the ground under it.
            float y = 0f;
            bool fits = atWall
                ? Spaced(at.X, at.Z, length * 0.5f) && FitsAgainstWall(survey, on, at, length, width, yaw, out y)
                : Fits(at.X, at.Z, length * 0.5f, out y);
            if (fits)
            {
                Drain(mesh, new Vector3(at.X, y, at.Z), length, width, yaw, random);
                taken.Add((new Vector2(at.X, at.Z), length * 0.5f));
                placed++;
            }
        }

        Count = taken.Count;
        if (Count == 0)
        {
            return;
        }

        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.02f, 0.02f, 0.02f), Roughness = 1f };
        var built = new ArrayMesh();
        mesh.Commit(built, m => m == Void ? dark : materials[iron]);
        AddChild(new MeshInstance3D { Name = "Fittings", Mesh = built, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    /// <summary>
    /// A drain at the foot of a wall: its own footprint on the listed ground, flat and open to the sky
    /// (the wall beside it would stop a sphere test, so only the ground is checked).
    /// </summary>
    private static bool FitsAgainstWall(GroundSurvey survey, HashSet<string> on, Vector3 at, float length, float width, float yaw, out float y)
    {
        y = 0f;
        if (!on.Contains(survey.SurfaceAt(at.X, at.Z, out y)) || y >= GroundSurvey.GroundTop || !survey.OpenGround(at.X, at.Z, 0.05f, out _))
        {
            return false;
        }

        var turn = new Basis(Vector3.Up, yaw);
        foreach ((float u, float v) in new[] { (-0.5f, -0.5f), (0.5f, -0.5f), (0.5f, 0.5f), (-0.5f, 0.5f) })
        {
            Vector3 corner = at + turn * new Vector3(v * width, 0f, u * length);
            if (!on.Contains(survey.SurfaceAt(corner.X, corner.Z, out float cy)) || MathF.Abs(cy - y) > 0.004f)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A round cover in its frame: a raised ring, the cover with ribs out from a boss, and two lifting slots.</summary>
    private static void Manhole(ShapeMesh mesh, Vector3 at, float radius, float turn)
    {
        var up = Basis.Identity;
        // The frame, a ring a little proud of the ground.
        var frame = new[] { new Vector2(radius + 0.06f, 0f), new Vector2(radius + 0.055f, 0.008f), new Vector2(radius, 0.008f), new Vector2(radius, 0f) };
        mesh.Lathe(Iron, at, up, frame, 24);
        mesh.Cylinder(Iron, at + Vector3.Up * 0.005f, up, radius - 0.004f, 0.01f, 24);
        // Raised ribs: a ring and spokes, and a boss in the middle.
        var ring = new[] { new Vector2(radius * 0.62f + 0.012f, 0f), new Vector2(radius * 0.62f + 0.012f, 0.004f), new Vector2(radius * 0.62f - 0.012f, 0.004f), new Vector2(radius * 0.62f - 0.012f, 0f) };
        mesh.Lathe(Iron, at + Vector3.Up * 0.01f, up, ring, 24);
        mesh.Cylinder(Iron, at + Vector3.Up * 0.012f, up, radius * 0.16f, 0.004f, 12);
        var basis = new Basis(Vector3.Up, turn);
        for (int i = 0; i < 8; i++)
        {
            Vector3 dir = basis * new Vector3(MathF.Cos(Mathf.Tau * i / 8f), 0f, MathF.Sin(Mathf.Tau * i / 8f));
            mesh.Bar(Iron, at + dir * (radius * 0.18f) + Vector3.Up * 0.012f, at + dir * (radius * 0.92f) + Vector3.Up * 0.012f, 0.018f, 0.004f);
        }

        // Two slots for the lifting keys, opposite each other near the rim.
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 dir = basis * new Vector3(side, 0f, 0f);
            mesh.Box(Void, at + dir * (radius * 0.8f) + Vector3.Up * 0.0105f, new Vector3(0.05f, 0.002f, 0.016f), basis);
        }
    }

    /// <summary>A grating: a frame round a dark hole and bars across it, some of them bent or missing.</summary>
    private static void Drain(ShapeMesh mesh, Vector3 at, float length, float width, float yaw, Random random)
    {
        var turn = new Basis(Vector3.Up, yaw);
        Vector3 along = turn * Vector3.Back, across = turn * Vector3.Right;
        const float frame = 0.035f, height = 0.012f;
        // The hole: dark, just above the ground so it shows through the bars.
        mesh.Box(Void, at + Vector3.Up * 0.002f, new Vector3(width - frame, 0.003f, length - frame), turn);
        foreach (float side in new[] { -1f, 1f })
        {
            mesh.Box(Iron, at + along * (side * (length - frame) * 0.5f) + Vector3.Up * (height * 0.5f), new Vector3(width, height, frame), turn);
            mesh.Box(Iron, at + across * (side * (width - frame) * 0.5f) + Vector3.Up * (height * 0.5f), new Vector3(frame, height, length), turn);
        }

        int bars = Math.Max(4, (int)((length - 2f * frame) / 0.045f));
        for (int i = 0; i < bars; i++)
        {
            if (random.NextDouble() < 0.06)
            {
                continue;
            }

            float t = Mathf.Lerp(-0.5f, 0.5f, (i + 0.5f) / bars) * (length - 2f * frame);
            mesh.Box(Iron, at + along * t + Vector3.Up * (height * 0.5f), new Vector3(width - 2f * frame, height * 0.9f, 0.014f), turn);
        }
    }
}
