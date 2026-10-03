using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Collision;
using Pb.Sim.Level;
using Aabb = Pb.Sim.Collision.Aabb;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.World;

/// <summary>
/// Debris on the floors indoors, on every storey (presentation.jsonc "floorDebris"): papers lying about
/// or screwed up, plaster fallen off the walls along their foot, drifts of dust in the corners, leaves
/// blown in, and glass under the broken windows, inside and out. Flat cards from one atlas painted at
/// load (<see cref="DebrisPainter"/>), each tinted and placed where all of it lies on one flat floor
/// (found by rays down through the room), seeded by the level id. One MultiMesh; presentation only.
/// </summary>
public partial class FloorDebris : Node3D
{
    /// <summary>Cards lie this far above the floor, each a hair above the one before so overlaps don't flicker.</summary>
    private const float Lift = 0.005f, Layer = 0.0002f;

    private static readonly Dictionary<uint, ImageTexture> Atlases = new();

    public int Count { get; private set; }

    public void Build(LevelLayout level, CollisionWorld world, FloorDebrisDef def)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        uint seed = (uint)LevelBuilder.StableHash(level.Id) ^ 0xDEB815u;
        var placer = new Placer(level, world, new Random((int)(seed & 0x7fffffff)));
        var cards = new List<Card>();
        foreach (FloorDebrisKindDef kind in def.Kinds)
        {
            placer.Place(kind, cards);
        }

        Count = cards.Count;
        if (cards.Count == 0)
        {
            return;
        }

        if (!Atlases.TryGetValue(seed, out ImageTexture? atlas))
        {
            atlas = new DebrisPainter(seed).Paint();
            Atlases[seed] = atlas;
        }

        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/floor_debris.gdshader") };
        material.SetShaderParameter("atlas", atlas);
        material.SetShaderParameter("grid", new Vector2(DebrisPainter.Columns, DebrisPainter.Rows));
        material.SetShaderParameter("fade_start", def.FadeStart_m);
        material.SetShaderParameter("fade_end", def.FadeEnd_m);
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = CardMesh(),
        };
        multimesh.InstanceCount = cards.Count;
        for (int i = 0; i < cards.Count; i++)
        {
            Card c = cards[i];
            var basis = new Basis(Vector3.Up, c.Yaw) * Basis.FromScale(new Vector3(c.Size, 1f, c.Size));
            multimesh.SetInstanceTransform(i, new Transform3D(basis, c.Position + Vector3.Up * (Lift + (i % 16) * Layer)));
            multimesh.SetInstanceColor(i, c.Color);
            multimesh.SetInstanceCustomData(i, new Color(c.Cell, c.Opacity, c.Gloss, 0f));
        }

        AddChild(new MultiMeshInstance3D
        {
            Name = "DebrisCards",
            Multimesh = multimesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    private readonly record struct Card(Vector3 Position, float Yaw, float Size, int Cell, Color Color, float Opacity, float Gloss);

    /// <summary>A flat unit square lying on the origin, facing up, UV across it.</summary>
    private static ArrayMesh CardMesh()
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 a = new(-0.5f, 0f, -0.5f), b = new(0.5f, 0f, -0.5f), c = new(0.5f, 0f, 0.5f), d = new(-0.5f, 0f, 0.5f);
        // Clockwise seen from above (Godot's front faces).
        foreach ((Vector3 p, Vector2 uv) in new[] { (a, Vector2.Zero), (b, Vector2.Right), (c, Vector2.One), (a, Vector2.Zero), (c, Vector2.One), (d, Vector2.Down) })
        {
            tool.SetNormal(Vector3.Up);
            tool.SetUV(uv);
            tool.AddVertex(p);
        }

        return tool.Commit();
    }

    /// <summary>Finds room for each kind on the indoor floors: along the foot of the walls, anywhere in a room, or under the windows.</summary>
    private sealed class Placer
    {
        /// <summary>Floors this far above a room's base still count as its floor (a slab's thickness); desks and the like don't.</summary>
        private const float FloorReach = 0.35f;

        private readonly LevelLayout _level;
        private readonly CollisionWorld _world;
        private readonly Random _random;
        private readonly List<Aabb> _rooms = new();
        private readonly List<(Vector3 Centre, Vector3 Along, Vector3 Out, float Half, float Thick, float Foot)> _walls = new();

        public Placer(LevelLayout level, CollisionWorld world, Random random)
        {
            _level = level;
            _world = world;
            _random = random;
            foreach (AreaSpec area in level.Areas)
            {
                if (area.Indoor)
                {
                    _rooms.Add(area.Box);
                }
            }

            foreach (LevelPrimitive p in level.Primitives)
            {
                if (p.Role != PrimitiveRole.Wall || p.Kind != PrimitiveKind.Box || p.Height < 0.5f)
                {
                    continue;
                }

                Vector3 x = SVector3.Transform(SVector3.UnitX, p.Rotation).ToGodot(), z = SVector3.Transform(SVector3.UnitZ, p.Rotation).ToGodot();
                if (MathF.Abs(x.Y) > 0.05f || MathF.Abs(z.Y) > 0.05f)
                {
                    continue;
                }

                bool longX = p.HalfExtents.X >= p.HalfExtents.Z;
                _walls.Add((p.Center.ToGodot(), longX ? x : z, longX ? z : x, longX ? p.HalfExtents.X : p.HalfExtents.Z,
                    longX ? p.HalfExtents.Z : p.HalfExtents.X, p.Bounds.Min.Y));
            }
        }

        public void Place(FloorDebrisKindDef def, List<Card> cards)
        {
            DebrisKind kind = Enum.Parse<DebrisKind>(def.Kind, true);
            Color color = Color.FromHtml(def.Color).SrgbToLinear();
            void Add(Vector3 at, float yaw, float size) => cards.Add(new Card(at, yaw, size, DebrisPainter.Cell(kind, _random.Next(DebrisPainter.Variants)),
                (color * R(0.85f, 1.08f)) with { A = 1f }, def.Opacity * R(0.75f, 1f), def.Gloss));

            if (def.PerWindow > 0f)
            {
                UnderWindows(def, Add);
            }

            if (def.PerSquareMetre <= 0f)
            {
                return;
            }

            for (int r = 0; r < _rooms.Count; r++)
            {
                Aabb room = _rooms[r];
                // A ground-floor room's box reaches below the ground; its floor is the ground's.
                float floor = MathF.Max(room.Min.Y, 0f);
                int count = (int)MathF.Round((room.Max.X - room.Min.X) * (room.Max.Z - room.Min.Z) * def.PerSquareMetre);
                var walls = WallsIn(room);
                int placed = 0;
                for (int attempt = 0; attempt < count * 10 && placed < count; attempt++)
                {
                    float size = Mathf.Lerp(def.Size_m[0], def.Size_m[1], MathF.Pow(R(0f, 1f), 1.5f));
                    (float x, float z) = R(0f, 1f) < def.EdgeShare && walls.Count > 0 ? AlongWall(walls, size) : (R(room.Min.X, room.Max.X), R(room.Min.Z, room.Max.Z));
                    // Each spot belongs to the first room that holds it, so rooms inside others aren't covered twice.
                    if (!Holds(room, x, z) || Owner(x, z, room.Min.Y) != r)
                    {
                        continue;
                    }

                    float yaw = R(0f, Mathf.Tau);
                    if (Fits(floor + 1.6f, floor - 0.6f, floor + FloorReach, x, z, size, yaw, out float y))
                    {
                        Add(new Vector3(x, y, z), yaw, size);
                        placed++;
                    }
                }
            }
        }

        /// <summary>Glass under the windows on both sides, on the floor or the ground below the sill.</summary>
        private void UnderWindows(FloorDebrisKindDef def, Action<Vector3, float, float> add)
        {
            foreach (Aperture a in _level.Apertures)
            {
                if (a.Kind != ApertureKind.Window || _level.Owners[a.Owner].StartsWith("wall", StringComparison.Ordinal))
                {
                    continue;
                }

                Vector3 n = a.Normal.ToGodot();
                foreach (float side in new[] { -1f, 1f })
                {
                    int count = (int)MathF.Floor(def.PerWindow + R(0f, 1f));
                    for (int k = 0, attempt = 0; k < count && attempt < count * 6; attempt++)
                    {
                        float size = Mathf.Lerp(def.Size_m[0], def.Size_m[1], R(0f, 1f));
                        Vector3 at = a.At(R(-0.85f, 0.85f), -1f).ToGodot() + n * (side * (0.25f + size * 0.5f + R(0f, 0.6f)));
                        float yaw = R(0f, Mathf.Tau);
                        if (Fits(at.Y - 0.05f, at.Y - 8f, float.MaxValue, at.X, at.Z, size, yaw, out float y))
                        {
                            add(new Vector3(at.X, y, at.Z), yaw, size);
                            k++;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Whether a card fits at (x, z): a ray down from <paramref name="top"/> towards <paramref name="bottom"/>
        /// first meets a flat floor no higher than <paramref name="highest"/>, and its corners meet the same floor.
        /// </summary>
        private bool Fits(float top, float bottom, float highest, float x, float z, float size, float yaw, out float y)
        {
            y = 0f;
            if (!Floor(x, z, top, bottom, out float at) || at > highest)
            {
                return false;
            }

            float c = MathF.Cos(yaw) * size * 0.42f, s = MathF.Sin(yaw) * size * 0.42f;
            foreach ((float u, float w) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            {
                if (!Floor(x + c * u + s * w, z - s * u + c * w, top, bottom, out float corner) || MathF.Abs(corner - at) > 0.01f)
                {
                    return false;
                }
            }

            y = at;
            return true;
        }

        private bool Floor(float x, float z, float top, float bottom, out float y)
        {
            y = 0f;
            if (!_world.SweepSphere(new SVector3(x, top, z), new SVector3(x, bottom, z), 0f, out SweepHit hit) || hit.Normal.Y < 0.95f || hit.T <= 1e-4f)
            {
                return false;
            }

            y = hit.Point.Y;
            return true;
        }

        private List<(Vector3 Centre, Vector3 Along, Vector3 Out, float Half, float Thick, float Foot)> WallsIn(Aabb room)
        {
            var walls = new List<(Vector3, Vector3, Vector3, float, float, float)>();
            foreach (var w in _walls)
            {
                if (w.Foot < room.Max.Y && w.Foot > room.Min.Y - 0.5f
                    && w.Centre.X + w.Half >= room.Min.X && w.Centre.X - w.Half <= room.Max.X && w.Centre.Z + w.Half >= room.Min.Z && w.Centre.Z - w.Half <= room.Max.Z)
                {
                    walls.Add(w);
                }
            }

            return walls;
        }

        private (float X, float Z) AlongWall(List<(Vector3 Centre, Vector3 Along, Vector3 Out, float Half, float Thick, float Foot)> walls, float size)
        {
            (Vector3 centre, Vector3 along, Vector3 outward, float half, float thick, _) = walls[_random.Next(walls.Count)];
            float side = _random.Next(2) == 0 ? -1f : 1f;
            Vector3 p = centre + along * (R(-1f, 1f) * half) + outward * (side * (thick + size * R(0.3f, 0.55f) + R(0f, 0.12f)));
            return (p.X, p.Z);
        }

        private static bool Holds(Aabb room, float x, float z) => x >= room.Min.X && x <= room.Max.X && z >= room.Min.Z && z <= room.Max.Z;

        /// <summary>The first room holding (x, z) whose base is at this height.</summary>
        private int Owner(float x, float z, float baseY)
        {
            for (int r = 0; r < _rooms.Count; r++)
            {
                if (Holds(_rooms[r], x, z) && MathF.Abs(_rooms[r].Min.Y - baseY) < 0.5f)
                {
                    return r;
                }
            }

            return -1;
        }

        private float R(float a, float b) => a + (float)_random.NextDouble() * (b - a);
    }
}
