using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --mobile-bench</c> (Phase 6): measures what the mobile graphics preset asks of a phone while the run it rides on
/// plays (on Thornwick Woods, the vehicle demo's drive along the tracks, or a team round with bots fighting and driving), and
/// as the run quits prints it (MOBILE BENCH lines) and fails the run past presentation.jsonc's
/// <c>graphics.mobileBudget</c>. Drawn as a phone draws it (the Compatibility renderer, <c>--rendering-method
/// gl_compatibility --rendering-driver opengl3_es</c>, Mesa's software OpenGL ES where there's no GPU; occlusion culling
/// off) it counts each frame's draw calls and triangles (shadows included): what a phone's GPU is asked to draw, the same
/// on any GPU, though the frame rate there means nothing. Headless it times the game's own work for a frame
/// at 30 frames a second (the scripts, and the sim's ticks for a thirtieth of a second): drawn in software, Godot's
/// process time takes in the wait for the frame being drawn, so it's timed where nothing's drawn.
/// </summary>
public partial class MobileBench : Node
{
    private MobileBudgetDef _budget = null!;
    private string _preset = "";
    private bool _headless;
    private readonly List<int> _draws = new();
    private readonly List<int> _triangles = new();
    private readonly List<int> _objects = new();
    private readonly List<float> _cpu = new();
    private int _frames;

    public void Start(MobileBudgetDef budget, string preset, float renderScale)
    {
        _budget = budget;
        _preset = preset;
        _headless = DisplayServer.GetName() == "headless";
        CleanExit.BeforeQuit = Report;
        // As a phone draws it: project.godot turns occlusion culling off there (use_occlusion_culling.mobile), so the
        // buildings' occluders hide nothing.
        GetViewport().UseOcclusionCulling = false;
        if (_headless)
        {
            // The clock's edges, first and last of the whole scene in each pass.
            Node scene = GetTree().CurrentScene;
            scene.AddChild(new BenchClock { Name = "BenchFirst", First = true });
            scene.AddChild(new BenchClock { Name = "BenchLast", First = false });
        }

        // --bench-hide=NAME,NAME hides the nodes so named (World's Stands, Woods, Ground...), to see what they cost.
        foreach (string name in (Args.Value("--bench-hide") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (Node n in GetTree().CurrentScene.FindChildren(name, "Node3D", recursive: true, owned: false))
            {
                ((Node3D)n).Visible = false;
                GD.Print($"MOBILE BENCH: hid {n.GetPath()}");
            }
        }

        GD.Print(_headless
            ? $"MOBILE BENCH: headless, timing the game's own work a frame at 30 fps (budget {budget.Cpu_ms:0.0} ms), preset {preset}"
            : $"MOBILE BENCH: the {RenderingServer.GetCurrentRenderingMethod()} renderer on {RenderingServer.GetVideoAdapterName()}, preset {preset}, " +
              $"3D view at {renderScale * 100f:0} % of {GetViewport().GetVisibleRect().Size.X:0} × {GetViewport().GetVisibleRect().Size.Y:0}; " +
              $"budget {budget.DrawCalls} draw calls, {budget.Triangles} triangles");
    }

    public override void _Process(double delta)
    {
        if (++_frames <= _budget.SkipFrames)
        {
            return;
        }

        if (_headless)
        {
            // The last frame's process pass, and this frame's ticks scaled to the ticks in a thirtieth of a second.
            if (BenchClock.Ticks > 0)
            {
                double perTick = BenchClock.PhysicsMs / BenchClock.Ticks;
                _cpu.Add((float)(BenchClock.ProcessMs + perTick * Engine.PhysicsTicksPerSecond / 30.0));
            }

            BenchClock.ProcessMs = 0;
            BenchClock.PhysicsMs = 0;
            BenchClock.Ticks = 0;
            return;
        }

        // What the last frame drew.
        _draws.Add((int)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame));
        _triangles.Add((int)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame));
        _objects.Add((int)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame));
    }

    /// <summary>The run is quitting with <paramref name="code"/>: the numbers, and the code to quit with (a failure past the budget).</summary>
    private int Report(int code)
    {
        var over = new List<string>();
        if (_headless)
        {
            if (_cpu.Count == 0)
            {
                GD.Print("MOBILE BENCH FAIL: no frames to time");
                return code == 0 ? 1 : code;
            }

            float mean = _cpu.Average();
            GD.Print($"MOBILE BENCH {_cpu.Count} frames ({_preset}, headless): the game's work {mean:0.00} ms a frame at 30 fps on average, " +
                     $"{Percentile(_cpu, 0.5):0.00} median, {Percentile(_cpu, 0.95):0.00} at the 95th percentile, {_cpu.Max():0.00} at most");
            if (mean > _budget.Cpu_ms)
            {
                over.Add($"{mean:0.00} ms of the game's work a frame (budget {_budget.Cpu_ms:0.0})");
            }
        }
        else
        {
            if (_draws.Count == 0 || _draws.Max() == 0)
            {
                GD.Print("MOBILE BENCH FAIL: nothing drawn to count");
                return code == 0 ? 1 : code;
            }

            int draws = (int)Percentile(_draws, 0.95), triangles = (int)Percentile(_triangles, 0.95);
            GD.Print($"MOBILE BENCH {_draws.Count} frames ({_preset}, drawn): draw calls {Percentile(_draws, 0.5):0} median, {draws} at the 95th " +
                     $"percentile, {_draws.Max()} at most; triangles {Percentile(_triangles, 0.5):0} median, {triangles} at the 95th, " +
                     $"{_triangles.Max()} at most; objects {Percentile(_objects, 0.95):0} at the 95th");
            if (draws > _budget.DrawCalls)
            {
                over.Add($"{draws} draw calls (budget {_budget.DrawCalls})");
            }

            if (triangles > _budget.Triangles)
            {
                over.Add($"{triangles} triangles (budget {_budget.Triangles})");
            }
        }

        if (over.Count > 0)
        {
            GD.Print($"MOBILE BENCH FAIL: {string.Join(", ", over)}");
            return code == 0 ? 1 : code;
        }

        GD.Print(_headless
            ? $"MOBILE BENCH PASS: within {_budget.Cpu_ms:0.0} ms of the game's work a frame"
            : $"MOBILE BENCH PASS: within {_budget.DrawCalls} draw calls and {_budget.Triangles} triangles");
        return code;
    }

    private static float Percentile<T>(List<T> values, double share) where T : IConvertible
    {
        var sorted = values.Select(v => v.ToSingle(null)).OrderBy(v => v).ToList();
        return sorted[Math.Clamp((int)Math.Ceiling(share * sorted.Count) - 1, 0, sorted.Count - 1)];
    }
}
