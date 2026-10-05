using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Collision;
using Pb.Sim.Level;

namespace Pb.Game.World;

/// <summary>
/// Water still dripping from the edges of the holes where a roof has fallen in (presentation.jsonc
/// "roofDrips"): at places along each edge a drop gathers every few seconds and falls to the floor
/// below, into a small dark pool of its own, setting off a ripple there (<see cref="PuddleRipples"/>).
/// The drops fall on their own clock; seeded by the level; looks only.
/// </summary>
public partial class RoofDrips : Node3D
{
    private readonly List<Source> _sources = new();
    private readonly Random _random = new(0xD121);
    private RoofDripsDef _def = null!;
    private PuddleRipples _ripples = null!;
    private float _gravity;
    private MultiMesh? _drops;
    private Drop[] _falling = Array.Empty<Drop>();

    private sealed class Source
    {
        public Vector3 Top;
        public float Floor;
        public float Next;
    }

    private struct Drop
    {
        public int Source;
        public float Time;
        public bool Falling;
    }

    public int Count => _sources.Count;

    /// <summary>Finds the drips; <paramref name="gravity"/> (m/s²) is the sim's, so a drop falls like a ball.</summary>
    public void Build(LevelLayout level, ICollisionWorld world, PuddleRipples ripples, RoofDripsDef def, float gravity)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        _sources.Clear();
        _def = def;
        _ripples = ripples;
        _gravity = gravity;
        _drops = null;
        var random = new Random(LevelBuilder.StableHash(level.Id) ^ 0xD121);
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
        var pools = new List<(Vector3 At, float Size, float Seed)>();
        foreach (Aperture hole in level.Apertures)
        {
            if (hole.Kind != ApertureKind.RoofHole)
            {
                continue;
            }

            Vector3 u = hole.U.ToGodot(), v = hole.V.ToGodot();
            var edges = new (Vector3 From, Vector3 Along, float Length, Vector3 Out)[]
            {
                (hole.At(-1f, 1f).ToGodot(), u, hole.HalfWidth * 2f, v),
                (hole.At(-1f, -1f).ToGodot(), u, hole.HalfWidth * 2f, -v),
                (hole.At(1f, -1f).ToGodot(), v, hole.HalfHeight * 2f, u),
                (hole.At(-1f, -1f).ToGodot(), v, hole.HalfHeight * 2f, -u),
            };
            foreach ((Vector3 from, Vector3 along, float length, Vector3 outward) in edges)
            {
                for (float t = R(0.3f, def.Every_m); t < length - 0.3f; t += def.Every_m * R(0.7f, 1.3f))
                {
                    if (random.NextDouble() > def.Share)
                    {
                        continue;
                    }

                    // Just inside the hole, off the torn edge, falling to whatever floor is below.
                    Vector3 top = from + along * t - outward * 0.05f + Vector3.Down * 0.06f;
                    const float radius = 0.02f;
                    if (!world.SweepSphere(top.ToSim(), (top + Vector3.Down * 30f).ToSim(), radius, out SweepHit hit) || hit.Normal.Y < 0.9f)
                    {
                        continue;
                    }

                    float floor = hit.Point.Y - radius;
                    float size = R(def.PoolSize_m[0], def.PoolSize_m[1]);
                    var at = new Vector3(top.X, floor, top.Z);
                    pools.Add((at, size, R(0f, Mathf.Tau)));
                    ripples.AddWater(new GroundDetail.Puddle(at + Vector3.Up * 0.0115f, 0f, size * 0.42f, size * 0.42f));
                    _sources.Add(new Source { Top = top, Floor = floor, Next = R(0f, def.Interval_s[1]) });
                }
            }
        }

        if (_sources.Count == 0)
        {
            return;
        }

        var poolMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = new PlaneMesh { Size = Vector2.One },
        };
        poolMesh.InstanceCount = pools.Count;
        for (int i = 0; i < pools.Count; i++)
        {
            (Vector3 at, float size, float seed) = pools[i];
            poolMesh.SetInstanceTransform(i, new Transform3D(Basis.FromScale(new Vector3(size, 1f, size)), at + Vector3.Up * 0.0115f));
            poolMesh.SetInstanceCustomData(i, new Color(seed, 0f, 0f, 0f));
        }

        AddChild(new MultiMeshInstance3D
        {
            Name = "Pools",
            Multimesh = poolMesh,
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/drip_pools.gdshader"), RenderPriority = -1 },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/roof_drips.gdshader") };
        material.SetShaderParameter("streak", def.Streak_m);
        _drops = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new QuadMesh { Size = Vector2.One },
        };
        _drops.InstanceCount = def.Max;
        _falling = new Drop[def.Max];
        for (int i = 0; i < def.Max; i++)
        {
            _drops.SetInstanceTransform(i, new Transform3D(Basis.FromScale(Vector3.Zero), _sources[0].Top));
        }

        AddChild(new MultiMeshInstance3D
        {
            Name = "Drops",
            Multimesh = _drops,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 10f,
        });
    }

    public override void _Process(double delta)
    {
        if (_drops is null || !Visible)
        {
            return;
        }

        float dt = (float)Math.Min(delta, 0.1);
        for (int s = 0; s < _sources.Count; s++)
        {
            Source source = _sources[s];
            source.Next -= dt;
            if (source.Next > 0f)
            {
                continue;
            }

            source.Next = _def.Interval_s[0] + (float)_random.NextDouble() * (_def.Interval_s[1] - _def.Interval_s[0]);
            for (int i = 0; i < _falling.Length; i++)
            {
                if (!_falling[i].Falling)
                {
                    _falling[i] = new Drop { Source = s, Time = 0f, Falling = true };
                    break;
                }
            }
        }

        for (int i = 0; i < _falling.Length; i++)
        {
            ref Drop drop = ref _falling[i];
            if (!drop.Falling)
            {
                continue;
            }

            Source source = _sources[drop.Source];
            drop.Time += dt;
            float y = source.Top.Y - 0.5f * _gravity * drop.Time * drop.Time;
            if (y <= source.Floor)
            {
                drop.Falling = false;
                var landing = new Vector3(source.Top.X, source.Floor, source.Top.Z);
                _ripples.Splash(landing, _def.RippleSize_m, 1);
                _drops.SetInstanceTransform(i, new Transform3D(Basis.FromScale(Vector3.Zero), landing));
                continue;
            }

            _drops.SetInstanceTransform(i, new Transform3D(Basis.Identity, new Vector3(source.Top.X, y, source.Top.Z)));
        }
    }
}
