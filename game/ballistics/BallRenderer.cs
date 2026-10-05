using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Ballistics;
using Pb.Sim.Events;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Ballistics;

/// <summary>
/// Draws every live paintball with one MultiMesh, rewritten from a single float buffer each frame.
/// Positions are interpolated between sim ticks. The shader enforces a minimum on-screen size:
/// a 17.3 mm ball is under 1 px beyond ~17 m at 1080p, which would make incoming paint unreadable.
/// The local player's balls start at the drawn barrel and blend into their true path.
/// </summary>
public partial class BallRenderer : MultiMeshInstance3D, ISimEventListener
{
    // MultiMesh buffer layout per instance: Transform3D (12 floats) + Color (4) + custom data (4).
    private const int Stride = 20;
    private const int MaxLocalShots = 16;

    private readonly List<LocalShot> _localShots = new(MaxLocalShots);
    private BallPool _pool = null!;
    private BallisticsWorld _balls = null!;
    private MultiMesh _multiMesh = null!;
    private ShaderMaterial _material = null!;
    private float[] _buffer = Array.Empty<float>();
    private Color[] _teamColors = Array.Empty<Color>();
    private int _localPlayerId;
    private Func<Vector3>? _visualMuzzle;
    private float _blendTime;
    private double _time;

    public void Initialize(BallisticsWorld balls, PresentationDef view, int localPlayerId, Func<Vector3> visualMuzzle, Aabb bounds)
    {
        _balls = balls;
        _pool = balls.Pool;
        _localPlayerId = localPlayerId;
        _visualMuzzle = visualMuzzle;

        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ball.gdshader") };
        _multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 10, Rings = 5, Material = _material },
            InstanceCount = _pool.Capacity,
            VisibleInstanceCount = 0,
        };
        Multimesh = _multiMesh;
        _buffer = new float[_pool.Capacity * Stride];
        CastShadow = ShadowCastingSetting.Off;
        CustomAabb = bounds;
        ApplyView(view);
    }

    public void ApplyView(PresentationDef view)
    {
        _teamColors = new Color[view.TeamColors.Length];
        for (int i = 0; i < _teamColors.Length; i++)
        {
            _teamColors[i] = Color.FromHtml(view.TeamColors[i]);
        }

        _blendTime = view.Ball.VisualBlend_s;
        _material.SetShaderParameter("min_pixels", view.Ball.MinPixels);
        _material.SetShaderParameter("streak_time", view.Ball.Streak_s);
    }

    public void OnSimEvent(in SimEvent e)
    {
        if (e.Type == SimEventType.ShotFired && e.PlayerId == _localPlayerId && _visualMuzzle is not null && _blendTime > 0f)
        {
            if (_localShots.Count == MaxLocalShots)
            {
                _localShots.RemoveAt(0);
            }

            _localShots.Add(new LocalShot(e.ShotSequence, _visualMuzzle() - e.Position.ToGodot(), _time));
        }
    }

    public override void _Process(double delta)
    {
        if (_pool is null)
        {
            return;
        }

        _time += delta;
        for (int s = _localShots.Count - 1; s >= 0; s--)
        {
            if (_time - _localShots[s].SpawnTime > _blendTime)
            {
                _localShots.RemoveAt(s);
            }
        }

        float alpha = (float)Engine.GetPhysicsInterpolationFraction();
        float radius = _balls.Projectile.Radius;
        int count = _pool.Count;
        for (int i = 0; i < count; i++)
        {
            SVector3 position = SVector3.Lerp(_pool.PrevPosition[i], _pool.Position[i], alpha);
            if (_localShots.Count > 0 && _pool.Owner[i] == _localPlayerId)
            {
                position += LocalOffset(_pool.Sequence[i]).ToSim();
            }

            SVector3 velocity = _pool.Velocity[i];
            Color color = _teamColors[_pool.Team[i] % _teamColors.Length];
            int o = i * Stride;
            _buffer[o + 0] = radius;
            _buffer[o + 1] = 0f;
            _buffer[o + 2] = 0f;
            _buffer[o + 3] = position.X;
            _buffer[o + 4] = 0f;
            _buffer[o + 5] = radius;
            _buffer[o + 6] = 0f;
            _buffer[o + 7] = position.Y;
            _buffer[o + 8] = 0f;
            _buffer[o + 9] = 0f;
            _buffer[o + 10] = radius;
            _buffer[o + 11] = position.Z;
            _buffer[o + 12] = color.R;
            _buffer[o + 13] = color.G;
            _buffer[o + 14] = color.B;
            _buffer[o + 15] = 1f;
            _buffer[o + 16] = velocity.X;
            _buffer[o + 17] = velocity.Y;
            _buffer[o + 18] = velocity.Z;
            _buffer[o + 19] = 0f;
        }

        RenderingServer.MultimeshSetBuffer(_multiMesh.GetRid(), _buffer);
        _multiMesh.VisibleInstanceCount = count;
    }

    private Vector3 LocalOffset(uint sequence)
    {
        foreach (LocalShot shot in _localShots)
        {
            if (shot.Sequence == sequence)
            {
                float t = (float)((_time - shot.SpawnTime) / _blendTime);
                float weight = 1f - Mathf.SmoothStep(0f, 1f, t);
                return shot.Offset * weight;
            }
        }

        return Vector3.Zero;
    }

    private readonly record struct LocalShot(uint Sequence, Vector3 Offset, double SpawnTime);
}
