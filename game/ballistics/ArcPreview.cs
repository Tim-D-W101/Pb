using System;
using System.Text;
using Godot;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Ballistics;
using Pb.Sim.Players;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Ballistics;

/// <summary>
/// Debug overlay (F2): the predicted path of a ball fired now (same integrator and collision as
/// play, no randomness), with a marker at each probe distance. The drop below the launch line at
/// each probe is reported in <see cref="Summary"/> for the HUD.
/// </summary>
public partial class ArcPreview : MeshInstance3D
{
    private const int MaxProbes = 8;

    private readonly SVector3[] _points = new SVector3[2048];
    private readonly Vector3[] _markers = new Vector3[MaxProbes];
    private readonly StringBuilder _summary = new();
    private ImmediateMesh _mesh = null!;
    private float[] _probes = Array.Empty<float>();
    private SimWorld _sim = null!;
    private PlayerState _player = null!;
    private float _maxTime;

    public bool Enabled { get; set; }

    /// <summary>"Arc: 10 m −0.07 · 20 m −0.34 …", or empty when disabled.</summary>
    public string Summary { get; private set; } = string.Empty;

    public void Initialize(SimWorld sim, PlayerState player, PresentationDef view)
    {
        _sim = sim;
        _player = player;
        _mesh = new ImmediateMesh();
        Mesh = _mesh;
        CastShadow = ShadowCastingSetting.Off;
        MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            NoDepthTest = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        ApplyView(view);
    }

    public void ApplyView(PresentationDef view)
    {
        _maxTime = view.ArcPreview.MaxTime_s;
        _probes = view.ArcPreview.Probes_m.Length > MaxProbes ? view.ArcPreview.Probes_m[..MaxProbes] : view.ArcPreview.Probes_m;
    }

    public override void _Process(double delta)
    {
        if (_mesh is null)
        {
            return;
        }

        _mesh.ClearSurfaces();
        Summary = string.Empty;
        if (!Enabled)
        {
            return;
        }

        ShotSolution shot = _sim.SolveShot(_player);
        float speed = _player.Marker.MuzzleVelocity * _player.Marker.Air.VelocityFactor;
        TrajectoryResult result = TrajectoryPredictor.Predict(_sim.Config.Projectile, _sim.Collision, _sim.Targets, _sim.Tick,
            shot.Origin, shot.Direction * speed, _sim.Dt, _maxTime, _points);
        if (result.PointCount < 2)
        {
            return;
        }

        _mesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip);
        for (int i = 0; i < result.PointCount; i++)
        {
            float t = i / (float)result.PointCount;
            _mesh.SurfaceSetColor(new Color(1f, 0.9f - 0.5f * t, 0.2f, 0.9f));
            _mesh.SurfaceAddVertex(_points[i].ToGodot());
        }

        _mesh.SurfaceEnd();

        // Drop below the straight launch line at each probe's horizontal distance.
        SVector3 origin = shot.Origin;
        float flat = MathF.Max(new System.Numerics.Vector2(shot.Direction.X, shot.Direction.Z).Length(), 1e-4f);
        int markers = 0;
        _summary.Clear();
        _summary.Append("Arc (F2):");
        foreach (float distance in _probes)
        {
            for (int i = 1; i < result.PointCount; i++)
            {
                float d0 = Horizontal(_points[i - 1] - origin);
                float d1 = Horizontal(_points[i] - origin);
                if (d1 < distance || d0 > distance)
                {
                    continue;
                }

                SVector3 at = SVector3.Lerp(_points[i - 1], _points[i], (distance - d0) / MathF.Max(d1 - d0, 1e-6f));
                SVector3 line = origin + shot.Direction * (distance / flat);
                _markers[markers++] = at.ToGodot();
                _summary.Append("  ").Append(distance.ToString("0")).Append(" m drop ").Append((line.Y - at.Y).ToString("0.00")).Append(" m");
                break;
            }
        }

        if (result.Impact)
        {
            _summary.Append("  | impact at ").Append(Horizontal(result.ImpactPoint - origin).ToString("0.0")).Append(" m");
        }

        Summary = _summary.ToString();
        if (markers == 0)
        {
            return;
        }

        _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        for (int m = 0; m < markers; m++)
        {
            Vector3 g = _markers[m];
            _mesh.SurfaceSetColor(new Color(1f, 1f, 1f, 0.9f));
            _mesh.SurfaceAddVertex(g + new Vector3(-0.15f, 0, 0));
            _mesh.SurfaceAddVertex(g + new Vector3(0.15f, 0, 0));
            _mesh.SurfaceAddVertex(g + new Vector3(0, -0.15f, 0));
            _mesh.SurfaceAddVertex(g + new Vector3(0, 0.15f, 0));
        }

        _mesh.SurfaceEnd();
    }

    private static float Horizontal(SVector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);
}
