using Godot;
using Pb.Game.Core;
using Pb.Sim.Range;

namespace Pb.Game.World;

/// <summary>Visible dummy built from a target's hitbox parts; flashes and counts lethal hits.</summary>
public partial class TargetView : Node3D
{
    private readonly System.Collections.Generic.List<StandardMaterial3D> _materials = new();
    private TargetSpec _spec = null!;
    private Label3D _label = null!;
    private float _flash;
    private int _hits;

    public void Build(TargetSpec spec)
    {
        _spec = spec;
        Position = spec.BasePosition.ToGodot();
        Rotation = new Vector3(0, spec.Yaw, 0);

        foreach (TargetPartSpec part in spec.Kind.Parts)
        {
            bool mask = part.Part == Pb.Sim.Collision.HitboxPart.Mask;
            var material = new StandardMaterial3D
            {
                AlbedoColor = mask ? new Color(0.12f, 0.12f, 0.13f) : new Color(0.93f, 0.9f, 0.82f),
                Roughness = mask ? 0.3f : 0.7f,
            };
            _materials.Add(material);

            switch (part.Kind)
            {
                case PartShapeKind.Capsule:
                {
                    Vector3 from = part.From.ToGodot();
                    Vector3 to = part.To.ToGodot();
                    Vector3 axis = to - from;
                    var instance = new MeshInstance3D
                    {
                        Mesh = new CapsuleMesh { Radius = part.Radius, Height = axis.Length() + 2f * part.Radius },
                        MaterialOverride = material,
                        Position = (from + to) * 0.5f,
                    };
                    if (axis.Normalized().Dot(Vector3.Up) < 0.999f)
                    {
                        instance.Basis = Conv.BasisFromUp(axis, 0f);
                    }

                    AddChild(instance);
                    break;
                }

                case PartShapeKind.Sphere:
                    AddChild(new MeshInstance3D
                    {
                        Mesh = new SphereMesh { Radius = part.Radius, Height = part.Radius * 2f },
                        MaterialOverride = material,
                        Position = part.Center.ToGodot(),
                    });
                    break;
                case PartShapeKind.Box:
                    AddChild(new MeshInstance3D
                    {
                        Mesh = new BoxMesh { Size = part.Size.ToGodot() },
                        MaterialOverride = material,
                        Position = part.Center.ToGodot(),
                    });
                    break;
            }
        }

        // Stand (visual only).
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.6f, 0.04f, 0.4f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.3f, 0.32f) },
            Position = new Vector3(0, 0.02f, 0),
        });

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
        foreach (StandardMaterial3D material in _materials)
        {
            material.EmissionEnabled = _flash > 0f;
            material.Emission = new Color(1f, 0.35f, 0.1f);
            material.EmissionEnergyMultiplier = _flash * 2.5f;
        }
    }
}
