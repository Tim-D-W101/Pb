using System;
using Godot;
using Pb.Game.Core;

namespace Pb.Game.Ballistics;

/// <summary>
/// What the world's paint costs your GPU, measured by the training ground's stress mode: once the pool of decals is full,
/// the time the GPU takes over a frame with the decals and without them, <c>measureFrames</c> frames each way, twice.
/// Over <c>decalBudget_ms</c>, the paint on walls, floors and cover is drawn as cards from then on (the graphics settings'
/// "Paint as cards"). Where the GPU's time can't be read (headless, or a renderer that doesn't time it), it says so.
/// </summary>
public partial class PaintCost : Node
{
    /// <summary>Frames left after each switch before timing again (the GPU's times arrive a few frames late).</summary>
    private const int Settle = 8;

    private readonly double[] _means = new double[4];
    private SplatSystem _splats = null!;
    private SplatDef _def = null!;
    private Func<bool> _running = () => false;
    private Action<float> _overBudget = _ => { };
    private Rid _viewport;
    private int _phase = -1;
    private int _frames;
    private double _sum;
    private int _timed;
    private int _shown = -1;

    /// <summary>What it found (or that it's measuring, or can't), for the perf overlay; empty before the stress mode.</summary>
    public string Summary { get; private set; } = "";

    /// <summary>What the decals cost the GPU a frame (ms), once measured.</summary>
    public float? Cost_ms { get; private set; }

    /// <param name="running">Whether the stress mode is on.</param>
    /// <param name="overBudget">Called once with the cost (ms) when the decals cost more than the budget.</param>
    public void Start(SplatSystem splats, SplatDef def, Func<bool> running, Action<float> overBudget)
    {
        _splats = splats;
        _def = def;
        _running = running;
        _overBudget = overBudget;
        _viewport = GetViewport().GetViewportRid();
    }

    public override void _Process(double delta)
    {
        if (!_running())
        {
            if (_phase is >= 0 and < 4)
            {
                // Stopped part way: the decals back, and it starts again next time.
                _splats.Visible = true;
                _phase = -1;
                _shown = -1;
                Summary = "";
            }

            return;
        }

        if (_phase == 4)
        {
            return;
        }

        if (_phase < 0)
        {
            int count = _splats.Cards ? -2 - _splats.CardCount : _splats.DecalCount;
            if (count != _shown)
            {
                _shown = count;
                Summary = _splats.Cards ? $"paint: drawn as cards ({_splats.CardCount})"
                    : $"paint: measuring once {_splats.Capacity} decals are down ({_splats.DecalCount} so far)";
            }

            if (_splats.Cards || _splats.DecalCount < _splats.Capacity)
            {
                return;
            }

            RenderingServer.ViewportSetMeasureRenderTime(_viewport, true);
            Begin(0);
            return;
        }

        if (++_frames <= Settle)
        {
            return;
        }

        double gpu = RenderingServer.ViewportGetMeasuredRenderTimeGpu(_viewport);
        if (gpu > 0.0)
        {
            _sum += gpu;
            _timed++;
        }

        if (_frames < Settle + _def.MeasureFrames)
        {
            return;
        }

        _means[_phase] = _timed > 0 ? _sum / _timed : 0.0;
        if (_phase < 3)
        {
            Begin(_phase + 1);
            return;
        }

        _phase = 4;
        _splats.Visible = true;
        RenderingServer.ViewportSetMeasureRenderTime(_viewport, false);
        if (Array.Exists(_means, m => m <= 0.0))
        {
            Summary = "paint: the GPU's time can't be read here, so its cost isn't measured";
            GD.Print("PAINT COST not measured: no GPU timings");
            return;
        }

        float cost = (float)Math.Max(0.0, (_means[0] - _means[1] + _means[2] - _means[3]) * 0.5);
        Cost_ms = cost;
        bool over = cost > _def.DecalBudget_ms;
        Summary = $"paint: {_splats.Capacity} decals cost {cost:0.00} ms of the GPU a frame (budget {_def.DecalBudget_ms:0.0}){(over ? ": cards now" : "")}";
        GD.Print($"PAINT COST {cost:0.000} ms a frame for {_splats.Capacity} decals ({_means[0]:0.000} / {_means[1]:0.000} / {_means[2]:0.000} / {_means[3]:0.000} ms with and without)");
        if (over)
        {
            _overBudget(cost);
        }
    }

    /// <summary>Times the next stretch: with the decals (even phases) or without them.</summary>
    private void Begin(int phase)
    {
        _phase = phase;
        _frames = 0;
        _sum = 0.0;
        _timed = 0;
        _splats.Visible = phase % 2 == 0;
        Summary = $"paint: measuring what the decals cost ({phase + 1} of 4)";
    }
}
