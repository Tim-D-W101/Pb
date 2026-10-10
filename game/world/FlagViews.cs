using System;
using Godot;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Match;
using Pb.Sim.Players;

namespace Pb.Game.World;

/// <summary>
/// Capture the flag in the world, looks only (presentation.jsonc "flags"). Each flag is a cloth on a pole, built in code
/// and waving in a little shader: standing in its stand at home, riding on its carrier's back, or lying still where it
/// was dropped, smoothly between ticks. Each flag's stand stays at its home, and a ring on the ground in each side's
/// colour marks where that side scores. The sim decides everything: who carries a flag, where it lies, who scores.
/// </summary>
public partial class FlagViews : Node3D
{
    private const string ClothShader = @"
shader_type spatial;
render_mode cull_disabled;
uniform vec4 colour : source_color;
uniform float width = 0.6;
uniform float wave_speed = 1.0;
uniform float wave_amount = 0.05;
uniform float waving = 1.0;
uniform float phase = 0.0;
uniform float chequers = 0.0;
void vertex() {
    // From the pole (its left edge) to its free end: the further from the pole, the more it moves.
    float along = clamp((VERTEX.x + width * 0.5) / width, 0.0, 1.0);
    float t = TIME * wave_speed * 6.2831853 + phase;
    VERTEX.z += sin(t - along * 5.0) * wave_amount * along * waving;
    VERTEX.y += sin(t * 0.7 - along * 3.0) * wave_amount * 0.2 * along * waving;
}
void fragment() {
    // The one in the middle of a field is chequered black and its colour, so it reads as nobody's.
    vec2 cell = floor(UV * vec2(chequers, max(1.0, floor(chequers * 0.66))));
    float dark = chequers > 0.0 ? mod(cell.x + cell.y, 2.0) : 0.0;
    ALBEDO = mix(colour.rgb, vec3(0.05), dark);
    ROUGHNESS = 0.85;
}";

    private SimWorld _sim = null!;
    private FlagSet? _flags;
    private FlagsViewDef _view = null!;
    private int _hideCarrier = -1;
    private Node3D[] _flag = Array.Empty<Node3D>();
    private ShaderMaterial[] _cloth = Array.Empty<ShaderMaterial>();
    private Transform3D[] _previous = Array.Empty<Transform3D>();
    private Transform3D[] _current = Array.Empty<Transform3D>();

    /// <param name="colourOf">A side's colour.</param>
    /// <param name="hideCarrier">Whose own flag isn't drawn while they carry it (you, in first person).</param>
    public void Build(SimWorld sim, FlagsViewDef view, Func<int, Color> colourOf, int hideCarrier)
    {
        _sim = sim;
        _view = view;
        _flags = sim.Match?.Flags;
        _hideCarrier = hideCarrier;
        if (_flags is not { } flags)
        {
            return;
        }

        int count = flags.Count;
        _flag = new Node3D[count];
        _cloth = new ShaderMaterial[count];
        _previous = new Transform3D[count];
        _current = new Transform3D[count];
        var shader = new Shader { Code = ClothShader };
        for (int i = 0; i < count; i++)
        {
            Color colour = flags.Owner(i) < 0 ? Color.FromHtml(view.CentreColor) : colourOf(flags.Owner(i));
            AddChild(Stand(view, flags.Home(i).ToGodot(), i));
            (_flag[i], _cloth[i]) = BuildFlag(view, shader, colour, i);
            _cloth[i].SetShaderParameter("chequers", flags.Owner(i) < 0 ? (float)view.CentreChequers : 0f);
            AddChild(_flag[i]);
        }

        for (int side = 0; side < 2; side++)
        {
            AddChild(Ring(flags.ScoreAt(side).ToGodot(), flags.Rules.ScoreReach, colourOf(side), view, side));
        }

        Capture();
        Capture();
    }

    /// <summary>After each sim step: where each flag will be drawn towards.</summary>
    public void Capture()
    {
        if (_flags is not { } flags)
        {
            return;
        }

        for (int i = 0; i < flags.Count; i++)
        {
            _previous[i] = _current[i];
            _current[i] = Pose(flags, i);
        }
    }

    public override void _Process(double delta)
    {
        if (_flags is not { } flags)
        {
            return;
        }

        float alpha = (float)Engine.GetPhysicsInterpolationFraction();
        for (int i = 0; i < flags.Count; i++)
        {
            // Across a change of state (taken, dropped) it jumps rather than sliding there.
            bool jumped = _previous[i].Origin.DistanceSquaredTo(_current[i].Origin) > 4f;
            _flag[i].GlobalTransform = jumped ? _current[i] : _previous[i].InterpolateWith(_current[i], alpha);
            _flag[i].Visible = !(flags.Carrier(i) == _hideCarrier && _hideCarrier >= 0 && _sim.FindPlayer(_hideCarrier) is { Alive: true });
            _cloth[i].SetShaderParameter("waving", flags.Status(i) == FlagStatus.Dropped ? 0f : 1f);
        }
    }

    /// <summary>
    /// The flag's pole foot and its axes: upright in its stand at home (or where it was carried to, once captured), leaning
    /// back on its carrier's back with the cloth streaming behind them, or lying flat where it was dropped.
    /// </summary>
    private Transform3D Pose(FlagSet flags, int i)
    {
        Vector3 at = flags.Position(i).ToGodot();
        switch (flags.Status(i))
        {
            case FlagStatus.Carried when _sim.FindPlayer(flags.Carrier(i)) is { } carrier:
            {
                float yaw = carrier.Yaw;
                var back = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                float lean = Mathf.DegToRad(_view.CarryLean_deg);
                float stoop = carrier.EyeHeight / _sim.Config.Movement.StandEyeHeight;
                Vector3 up = Vector3.Up * Mathf.Cos(lean) + back * Mathf.Sin(lean);
                Vector3 trail = back * Mathf.Cos(lean) - Vector3.Up * Mathf.Sin(lean);
                Vector3 foot = carrier.Position.ToGodot() + Vector3.Up * (_view.CarryFoot_m * stoop) + back * _view.CarryBack_m;
                return new Transform3D(new Basis(trail, up, trail.Cross(up)), foot);
            }
            case FlagStatus.Dropped:
            {
                // Lying on its side, the pole across where its carrier fell.
                float a = 0.7f + i * 1.9f;
                var along = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var across = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector3 foot = at - along * (_view.PoleHeight_m * 0.6f) + Vector3.Up * (_view.PoleRadius_m + 0.01f);
                return new Transform3D(new Basis(across, along, across.Cross(along)), foot);
            }
            default:
                return new Transform3D(new Basis(Vector3.Up, 0.4f + i * 1.3f), at + Vector3.Up * 0.04f);
        }
    }

    /// <summary>A flag, its pole's foot at the origin up the y axis, the cloth hung from the top along +x.</summary>
    private static (Node3D Flag, ShaderMaterial Cloth) BuildFlag(FlagsViewDef view, Shader shader, Color colour, int index)
    {
        var root = new Node3D { Name = $"Flag {index}" };
        float h = view.PoleHeight_m, r = view.PoleRadius_m, w = view.ClothSize_m[0], ch = view.ClothSize_m[1];
        var metal = new StandardMaterial3D { AlbedoColor = Color.FromHtml(view.PoleColor), Metallic = 0.6f, Roughness = 0.35f };
        root.AddChild(new MeshInstance3D
        {
            Name = "Pole", Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = h, RadialSegments = 8, Material = metal },
            Position = new Vector3(0f, h * 0.5f, 0f),
        });
        root.AddChild(new MeshInstance3D
        {
            Name = "Finial", Mesh = new SphereMesh { Radius = r * 2.4f, Height = r * 4.8f, RadialSegments = 8, Rings = 4, Material = metal },
            Position = new Vector3(0f, h + r * 2f, 0f),
        });
        var cloth = new ShaderMaterial { Shader = shader };
        cloth.SetShaderParameter("colour", colour);
        cloth.SetShaderParameter("width", w);
        cloth.SetShaderParameter("wave_speed", view.WaveSpeed);
        cloth.SetShaderParameter("wave_amount", view.WaveAmount_m);
        cloth.SetShaderParameter("phase", index * 2.1f);
        root.AddChild(new MeshInstance3D
        {
            Name = "Cloth",
            Mesh = new PlaneMesh
            {
                Size = new Vector2(w, ch), SubdivideWidth = 10, SubdivideDepth = 3, Orientation = PlaneMesh.OrientationEnum.Z, Material = cloth,
            },
            Position = new Vector3(r + w * 0.5f, h - ch * 0.5f - r, 0f),
        });
        return (root, cloth);
    }

    /// <summary>A flag's weighted stand: a low disc with a sleeve the pole stands in.</summary>
    private static Node3D Stand(FlagsViewDef view, Vector3 at, int index)
    {
        var root = new Node3D { Name = $"Stand {index}", Position = at };
        var dark = new StandardMaterial3D { AlbedoColor = Color.FromHtml(view.StandColor), Roughness = 0.6f, Metallic = 0.2f };
        float r = view.StandRadius_m;
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = r * 0.85f, BottomRadius = r, Height = 0.05f, RadialSegments = 20, Material = dark },
            Position = new Vector3(0f, 0.025f, 0f),
        });
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = view.PoleRadius_m * 2.2f, BottomRadius = view.PoleRadius_m * 2.6f, Height = 0.28f, RadialSegments = 10, Material = dark },
            Position = new Vector3(0f, 0.19f, 0f),
        });
        return root;
    }

    /// <summary>Where a side scores: a thin ring on the ground, as wide as the rules' reach, in its colour.</summary>
    private static MeshInstance3D Ring(Vector3 at, float radius, Color colour, FlagsViewDef view, int side)
    {
        const int Segments = 64;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float inner = radius - view.RingWidth_m * 0.5f, outer = radius + view.RingWidth_m * 0.5f;
        for (int k = 0; k < Segments; k++)
        {
            float a0 = Mathf.Tau * k / Segments, a1 = Mathf.Tau * (k + 1) / Segments;
            Vector3 i0 = new(Mathf.Cos(a0) * inner, 0f, Mathf.Sin(a0) * inner), o0 = new(Mathf.Cos(a0) * outer, 0f, Mathf.Sin(a0) * outer);
            Vector3 i1 = new(Mathf.Cos(a1) * inner, 0f, Mathf.Sin(a1) * inner), o1 = new(Mathf.Cos(a1) * outer, 0f, Mathf.Sin(a1) * outer);
            foreach (Vector3 v in new[] { i0, o0, o1, i0, o1, i1 })
            {
                st.SetNormal(Vector3.Up);
                st.AddVertex(v);
            }
        }

        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled, AlbedoColor = colour with { A = view.RingAlpha },
        };
        return new MeshInstance3D
        {
            Name = $"Scores {side}", Mesh = st.Commit(), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = at + new Vector3(0f, 0.025f, 0f),
        };
    }
}
