using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Collision;
using Pb.Sim.Level;

namespace Pb.Game.World;

/// <summary>
/// Litter blowing about the yard (presentation.jsonc "blowingLitter"): dry leaves, scraps of paper and
/// wrappers lying on open ground outdoors. When a gust rolling across the level is strong enough for a
/// piece, it lifts it: the piece tumbles, hops and skitters off downwind on a share of the ground wind
/// until the gust drops, it fetches up against something (a short sweep each frame against the sim's
/// collision) or it's blown in under a roof, where it stays. Pieces pinned for a while go back out onto
/// open ground somewhere when the camera isn't looking at either place. One MultiMesh; looks only.
/// </summary>
public partial class BlowingLitter : Node3D
{
    /// <summary>The sweep that finds what a piece fetches up against: its radius, and its height above the ground.</summary>
    private const float Radius = 0.05f, SweepHeight = 0.1f;

    private enum State
    {
        Resting,
        Moving,
        Pinned,
    }

    private sealed class Piece
    {
        public Vector3 At;
        public Vector2 Velocity;
        public State State;
        public LitterKindDef Kind = null!;
        public int Cell;
        public Color Color;
        public float Size, Lift, Share, Phase, HopPhase, Yaw, Flip, Pinned;
    }

    private static ImageTexture? _atlas;

    private readonly List<Piece> _pieces = new();
    private readonly Random _random = new(0x1EAF);
    private GroundSurvey _survey = null!;
    private ICollisionWorld _world = null!;
    private BlowingLitterDef _def = null!;
    private MultiMesh? _multimesh;
    private Pb.Sim.Collision.Aabb _bounds;
    private Vector2 _windDir;
    private float _windSpeed;
    private double _time;

    public int Count => _pieces.Count;

    public void Build(LevelLayout level, ICollisionWorld world, BlowingLitterDef def, Vector2 wind)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        _pieces.Clear();
        _multimesh = null;
        _survey = new GroundSurvey(level, world);
        _world = world;
        _def = def;
        _bounds = level.Bounds;
        _windDir = wind.Normalized();
        _windSpeed = wind.Length();
        float total = 0f;
        foreach (LitterKindDef k in def.Kinds)
        {
            total += k.Weight;
        }

        for (int i = 0; i < def.Count && total > 0f; i++)
        {
            float roll = R(0f, total);
            LitterKindDef kind = def.Kinds[^1];
            foreach (LitterKindDef k in def.Kinds)
            {
                roll -= k.Weight;
                if (roll <= 0f)
                {
                    kind = k;
                    break;
                }
            }

            var piece = new Piece
            {
                Kind = kind,
                Cell = kind.Kind switch { "leaf" => _random.Next(2), "paper" => 2, _ => 3 },
                Size = R(kind.Size_m[0], kind.Size_m[1]),
                Lift = R(def.Lift[0], def.Lift[1]),
                Share = R(def.Share[0], def.Share[1]),
                Phase = R(0f, Mathf.Tau),
            };
            Color c = Color.FromHtml(kind.Colors[_random.Next(kind.Colors.Length)]) * R(0.85f, 1.05f);
            c.A = 1f;
            piece.Color = c;
            if (Place(piece, null))
            {
                _pieces.Add(piece);
            }
        }

        if (_pieces.Count == 0)
        {
            return;
        }

        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/blowing_litter.gdshader") };
        material.SetShaderParameter("atlas", _atlas ??= Atlas());
        _multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = new PlaneMesh { Size = Vector2.One },
        };
        _multimesh.InstanceCount = _pieces.Count;
        for (int i = 0; i < _pieces.Count; i++)
        {
            _multimesh.SetInstanceColor(i, _pieces[i].Color);
            _multimesh.SetInstanceCustomData(i, new Color(_pieces[i].Cell, 0f, 0f, 0f));
        }

        AddChild(new MultiMeshInstance3D
        {
            Name = "Pieces",
            Multimesh = _multimesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = material,
            ExtraCullMargin = 1f,
        });
        Draw();
    }

    public override void _Process(double delta)
    {
        if (_multimesh is null)
        {
            return;
        }

        float dt = (float)Math.Min(delta, 0.1);
        _time += dt;
        Camera3D? camera = GetViewport().GetCamera3D();
        foreach (Piece p in _pieces)
        {
            Step(p, dt, camera);
        }

        Draw();
    }

    /// <summary>How hard the wind blows at a point now, 0 to 1: gusts roll downwind across the level, and each piece catches its own flurries.</summary>
    private float Gust(Vector3 at, float phase)
    {
        float t = (float)_time;
        float along = at.X * _windDir.X + at.Z * _windDir.Y;
        return Mathf.Clamp(0.5f + 0.3f * MathF.Sin(t * 0.45f - along * 0.06f) + 0.2f * MathF.Sin(t * 1.3f + phase), 0f, 1f);
    }

    private void Step(Piece p, float dt, Camera3D? camera)
    {
        float gust = Gust(p.At, p.Phase);
        switch (p.State)
        {
            case State.Resting:
                if (gust > p.Lift)
                {
                    p.State = State.Moving;
                }

                break;
            case State.Moving:
                Vector2 target = Vector2.Zero;
                if (gust > p.Lift - 0.15f)
                {
                    // Off downwind, weaving a little from side to side.
                    target = _windDir * (_windSpeed * p.Share * gust);
                    target += new Vector2(-_windDir.Y, _windDir.X) * (MathF.Sin((float)_time * 1.7f + p.Phase * 3f) * 0.35f * target.Length());
                }

                p.Velocity = p.Velocity.Lerp(target, 1f - MathF.Exp(-dt * 3f));
                float speed = p.Velocity.Length();
                if (target == Vector2.Zero && speed < 0.05f)
                {
                    Settle(p, State.Resting);
                    break;
                }

                Vector3 next = p.At + new Vector3(p.Velocity.X, 0f, p.Velocity.Y) * dt;
                if (_world.SweepSphere((p.At + Vector3.Up * SweepHeight).ToSim(), (next + Vector3.Up * SweepHeight).ToSim(), Radius, out _))
                {
                    // Fetched up against something: the wind holds it there.
                    Settle(p, State.Pinned);
                    break;
                }

                if (!_survey.OpenGround(next.X, next.Z, Radius, out float y) || !Inside(next))
                {
                    // Blown in under a roof, or off the level: it stops where it is.
                    Settle(p, State.Pinned);
                    break;
                }

                p.At = new Vector3(next.X, y, next.Z);
                p.HopPhase += speed * dt * 2.5f;
                p.Yaw += speed * dt * p.Kind.Spin_radPerM;
                p.Flip += speed * dt * p.Kind.Tumble_radPerM;
                break;
            case State.Pinned:
                p.Pinned += dt;
                if (p.Pinned > _def.Recycle_s && !Seen(camera, p.At))
                {
                    Place(p, camera);
                }

                break;
        }
    }

    /// <summary>Lays a piece flat (either face up) and stops it.</summary>
    private static void Settle(Piece p, State state)
    {
        p.State = state;
        p.Velocity = Vector2.Zero;
        p.Pinned = 0f;
        p.Flip = MathF.Round(p.Flip / MathF.PI) * MathF.PI;
    }

    /// <summary>Puts a piece down on open ground somewhere the camera isn't looking (any open ground, without one).</summary>
    private bool Place(Piece p, Camera3D? camera)
    {
        for (int tries = 0; tries < 60; tries++)
        {
            float x = R(_bounds.Min.X + 1f, _bounds.Max.X - 1f), z = R(_bounds.Min.Z + 1f, _bounds.Max.Z - 1f);
            if (!_survey.OpenGround(x, z, 0.15f, out float y) || Seen(camera, new Vector3(x, y, z)))
            {
                continue;
            }

            p.At = new Vector3(x, y, z);
            p.Yaw = R(0f, Mathf.Tau);
            p.Flip = _random.Next(2) * MathF.PI;
            Settle(p, State.Resting);
            return true;
        }

        return false;
    }

    private bool Inside(Vector3 at) =>
        at.X > _bounds.Min.X && at.X < _bounds.Max.X && at.Z > _bounds.Min.Z && at.Z < _bounds.Max.Z;

    /// <summary>Whether the camera could see a piece at <paramref name="at"/>: near it and in front.</summary>
    private bool Seen(Camera3D? camera, Vector3 at)
    {
        if (camera is null)
        {
            return false;
        }

        Vector3 to = at - camera.GlobalPosition;
        float distance = to.Length();
        return distance < _def.Hidden_m && (distance < 2f || to.Dot(-camera.GlobalBasis.Z) > distance * 0.3f);
    }

    private void Draw()
    {
        for (int i = 0; i < _pieces.Count; i++)
        {
            Piece p = _pieces[i];
            bool moving = p.State == State.Moving;
            float speed = p.Velocity.Length();
            float hop = moving ? p.Kind.Hop_m * MathF.Abs(MathF.Sin(p.HopPhase)) * MathF.Min(1f, speed / 1.2f) : 0f;
            // Paper doesn't tumble so much as lift at its edges and slide.
            float flip = p.Kind.Tumble_radPerM > 0f ? p.Flip : p.Flip + (moving ? 0.3f * MathF.Sin(p.HopPhase * 2f) : 0f);
            Basis basis = new Basis(Vector3.Up, p.Yaw) * new Basis(Vector3.Right, flip) * Basis.FromScale(new Vector3(p.Size, 1f, p.Size));
            // Clear of the ground however it's turned.
            float clearance = 0.006f + hop + 0.5f * p.Size * MathF.Abs(MathF.Sin(flip));
            _multimesh!.SetInstanceTransform(i, new Transform3D(basis, p.At + Vector3.Up * clearance));
        }
    }

    private float R(float a, float b) => a + (float)_random.NextDouble() * (b - a);

    /// <summary>
    /// The 2 × 2 atlas, white on clear (luminance for the detail): a pointed leaf with its midrib and veins,
    /// a lobed leaf, a torn scrap of paper with a fold across it, and a crumpled wrapper.
    /// </summary>
    private static ImageTexture Atlas()
    {
        const int cell = 128, size = cell * 2;
        var rgba = new byte[size * size * 4];
        var random = new Random(0x1EAF);
        float P(float a, float b) => a + (float)random.NextDouble() * (b - a);
        float wrinkleA = P(0f, Mathf.Tau), wrinkleB = P(0f, Mathf.Tau);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int which = y / cell * 2 + x / cell;
                // Cell coordinates, −1 to 1 across.
                float u = (x % cell + 0.5f) / cell * 2f - 1f, v = (y % cell + 0.5f) / cell * 2f - 1f;
                (float cover, float shade) = which switch
                {
                    0 => Leaf(u, v, lobed: false),
                    1 => Leaf(u, v, lobed: true),
                    2 => Scrap(u, v),
                    _ => Wrapper(u, v, wrinkleA, wrinkleB),
                };
                int i = (y * size + x) * 4;
                byte s = (byte)(Mathf.Clamp(shade, 0f, 1f) * 255f + 0.5f);
                rgba[i] = rgba[i + 1] = rgba[i + 2] = s;
                rgba[i + 3] = (byte)(Mathf.Clamp(cover, 0f, 1f) * 255f + 0.5f);
            }
        }

        Image image = Image.CreateFromData(size, size, false, Image.Format.Rgba8, rgba);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>A leaf along v (stalk at the bottom): a pointed blade, or one with three lobes each side.</summary>
    private static (float Cover, float Shade) Leaf(float u, float v, bool lobed)
    {
        float t = (0.85f - v) / 1.6f;
        if (t < 0f && MathF.Abs(u) < 0.035f && v < 0.98f)
        {
            // The stalk.
            return (1f, 0.55f);
        }

        if (t < 0f || t > 1f)
        {
            return (0f, 1f);
        }

        float half = 0.62f * MathF.Sin(MathF.PI * MathF.Pow(t, 0.75f));
        if (lobed)
        {
            half *= 0.8f + 0.25f * MathF.Abs(MathF.Sin(t * MathF.PI * 3.5f));
        }

        float edge = half - MathF.Abs(u);
        if (edge < 0f)
        {
            return (0f, 1f);
        }

        // Darker along the midrib and the veins running out from it, paler towards the edge.
        float vein = MathF.Abs(u) < 0.03f ? 0.7f : MathF.Abs(MathF.Sin((t * 9f - MathF.Abs(u) * 4f) * MathF.PI)) < 0.12f ? 0.82f : 1f;
        return (Mathf.Clamp(edge * 40f, 0f, 1f), vein * (0.8f + 0.2f * Mathf.Clamp(MathF.Abs(u) / MathF.Max(half, 0.01f), 0f, 1f)));
    }

    /// <summary>A scrap torn off a sheet: three straight edges and one ragged, a fold across it, ruled lines.</summary>
    private static (float Cover, float Shade) Scrap(float u, float v)
    {
        float ragged = 0.62f + 0.06f * MathF.Sin(u * 23f) + 0.04f * MathF.Sin(u * 41f + 1.7f);
        if (MathF.Abs(u) > 0.8f || v < -0.9f || v > ragged)
        {
            return (0f, 1f);
        }

        float shade = u + v * 0.3f > 0.1f ? 0.88f : 1f;
        if (MathF.Abs(((v + 1f) * 9f) % 1f - 0.5f) > 0.46f)
        {
            shade *= 0.82f;
        }

        return (1f, shade);
    }

    /// <summary>A crumpled wrapper: a lumpy outline with creased facets of light and shade.</summary>
    private static (float Cover, float Shade) Wrapper(float u, float v, float a, float b)
    {
        float angle = MathF.Atan2(v, u), r = MathF.Sqrt(u * u + v * v);
        float edge = 0.7f * (1f + 0.15f * MathF.Sin(5f * angle + a) + 0.1f * MathF.Sin(9f * angle + b));
        if (r > edge)
        {
            return (0f, 1f);
        }

        float crease = MathF.Sin(u * 11f + v * 7f + a) * MathF.Sin(v * 13f - u * 5f + b);
        return (1f, 0.75f + 0.25f * crease);
    }
}
