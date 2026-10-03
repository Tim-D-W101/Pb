using Godot;
using Pb.Game.Core;
using Pb.Sim.Range;

namespace Pb.Game.World;

/// <summary>
/// A target's dummy (built round its hitbox parts by <see cref="RangeShapes.Dummy"/>), moved along its
/// motion; it flashes when hit and its sign counts the hits.
/// </summary>
public partial class TargetView : Node3D
{
    private TargetSpec _spec = null!;
    private MeshInstance3D _dummy = null!;
    private StandardMaterial3D _flashMaterial = null!;
    private Color _flashColor;
    private Label3D _label = null!;
    private float _flash;
    private int _hits;

    public void Build(TargetSpec spec, Mesh dummy, Color flash)
    {
        _spec = spec;
        _flashColor = flash;
        Position = spec.BasePosition.ToGodot();
        Rotation = new Vector3(0, spec.Yaw, 0);
        _dummy = new MeshInstance3D { Name = "Dummy", Mesh = dummy };
        AddChild(_dummy);
        // Laid over the whole dummy while it flashes: its colour added, fading out.
        _flashMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };

        _label = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            PixelSize = 0.003f + 0.00008f * Mathf.Abs(spec.BasePosition.Z),
            FontSize = 48,
            OutlineSize = 12,
            Position = new Vector3(0, 2.0f + 0.01f * Mathf.Abs(spec.BasePosition.Z), 0),
        };
        AddChild(_label);
        SetHits(0);
    }

    public void SetTime(double time) => Position = _spec.PositionAt(time).ToGodot();

    public void Flash()
    {
        _flash = 1f;
        SetHits(_hits + 1);
    }

    public void SetHits(int hits)
    {
        _hits = hits;
        _label.Text = $"{_spec.Label}\n{hits} hit{(hits == 1 ? "" : "s")}";
    }

    public override void _Process(double delta)
    {
        if (_flash <= 0f)
        {
            return;
        }

        _flash = Mathf.Max(0f, _flash - (float)delta * 5f);
        _flashMaterial.AlbedoColor = new Color(_flashColor, _flash * 0.85f);
        _dummy.MaterialOverlay = _flash > 0f ? _flashMaterial : null;
    }
}
