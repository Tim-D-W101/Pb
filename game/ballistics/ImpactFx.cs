using System;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Events;

namespace Pb.Game.Ballistics;

/// <summary>Pooled one-shot particle bursts: coloured paint spray on breaks, a puff on bounces.</summary>
public partial class ImpactFx : Node3D, ISimEventListener
{
    private CpuBurst[] _breaks = Array.Empty<CpuBurst>();
    private CpuBurst[] _bounces = Array.Empty<CpuBurst>();
    private Color[] _teamColors = Array.Empty<Color>();
    private int _nextBreak;
    private int _nextBounce;
    private int _budget;
    private int _maxPerFrame;
    private float _minCameraDistance;

    public void Initialize(PresentationDef view)
    {
        _teamColors = new Color[view.TeamColors.Length];
        for (int i = 0; i < _teamColors.Length; i++)
        {
            _teamColors[i] = Color.FromHtml(view.TeamColors[i]);
        }

        _maxPerFrame = view.Fx.MaxBurstsPerFrame;
        _minCameraDistance = view.Fx.MinCameraDistance_m;
        _breaks = CreatePool(24, view.Fx.BreakParticles, 0.45f, 2.5f, 5f, 0.014f, 70f);
        _bounces = CreatePool(24, view.Fx.BounceParticles, 0.3f, 0.6f, 1.6f, 0.012f, 50f);
    }

    public void OnSimEvent(in SimEvent e)
    {
        if (_budget <= 0)
        {
            return;
        }

        if (e.Type == SimEventType.BallBroke)
        {
            Emit(_breaks, ref _nextBreak, e.Position.ToGodot(), e.Normal.ToGodot(), _teamColors[e.Team % _teamColors.Length]);
        }
        else if (e.Type == SimEventType.BallBounced && e.Value > 6f)
        {
            Emit(_bounces, ref _nextBounce, e.Position.ToGodot(), e.Normal.ToGodot(), new Color(0.85f, 0.85f, 0.8f, 0.8f));
        }
    }

    public override void _Process(double delta) => _budget = _maxPerFrame;

    private void Emit(CpuBurst[] pool, ref int next, Vector3 position, Vector3 normal, Color color)
    {
        if (GetViewport().GetCamera3D() is { } camera && camera.GlobalPosition.DistanceTo(position) < _minCameraDistance)
        {
            return;
        }

        _budget--;
        CpuBurst burst = pool[next];
        next = (next + 1) % pool.Length;
        burst.Particles.GlobalTransform = new Transform3D(Conv.BasisFromUp(normal, 0f), position + normal * 0.01f);
        burst.Particles.Color = color;
        burst.Particles.Restart();
    }

    private CpuBurst[] CreatePool(int count, int amount, float lifetime, float speedMin, float speedMax, float size, float spread)
    {
        var material = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        var mesh = new SphereMesh { Radius = size, Height = size * 2f, RadialSegments = 6, Rings = 3, Material = material };
        var pool = new CpuBurst[count];
        for (int i = 0; i < count; i++)
        {
            var particles = new CpuParticles3D
            {
                Emitting = false,
                OneShot = true,
                Amount = amount,
                Lifetime = lifetime,
                Explosiveness = 1f,
                Mesh = mesh,
                Direction = Vector3.Up,
                Spread = spread,
                InitialVelocityMin = speedMin,
                InitialVelocityMax = speedMax,
                Gravity = new Vector3(0, -9.8f, 0),
                ScaleAmountMin = 0.6f,
                ScaleAmountMax = 1.4f,
                LocalCoords = false,
            };
            AddChild(particles);
            pool[i] = new CpuBurst(particles);
        }

        return pool;
    }

    private readonly record struct CpuBurst(CpuParticles3D Particles);
}
