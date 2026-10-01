using Godot;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Collision;
using Pb.Sim.Players;

namespace Pb.Game.Player;

/// <summary>
/// Greybox character drawn from the sim's hitbox rig: one box per hitbox, posed from the player's
/// state and interpolated between ticks, so what you see is exactly what you can hit. Each part is
/// an unscaled node with the scaled box under it, so splats parented to a part keep their shape and
/// move with it. Real models (M2.6) will be posed to match the same rig.
/// </summary>
public partial class CharacterVisual : Node3D
{
    private readonly Node3D[] _parts = new Node3D[HitboxRig.PartCount];
    private readonly MeshInstance3D[] _meshes = new MeshInstance3D[HitboxRig.PartCount];
    private readonly int[] _indexOfPart = new int[16];
    private PosedBox[] _previous = new PosedBox[HitboxRig.PartCount];
    private PosedBox[] _current = new PosedBox[HitboxRig.PartCount];
    private SimWorld _sim = null!;
    private PlayerState _state = null!;

    public void Build(SimWorld sim, PlayerState state, Color jersey)
    {
        _sim = sim;
        _state = state;
        TopLevel = true;
        var box = new BoxMesh { Size = Vector3.One };
        _sim.PlayerHits.PoseNow(state, _current);
        for (int i = 0; i < _parts.Length; i++)
        {
            HitboxPart part = _current[i].Part;
            _indexOfPart[(int)part] = i;
            _parts[i] = new Node3D { Name = part.ToString() };
            AddChild(_parts[i]);
            _meshes[i] = new MeshInstance3D { Mesh = box, MaterialOverride = MaterialFor(part, jersey) };
            _parts[i].AddChild(_meshes[i]);
        }

        Capture();
        ApplyPose(1f);
    }

    /// <summary>The node a splat on <paramref name="part"/> should stick to.</summary>
    public Node3D PartNode(HitboxPart part) => _parts[_indexOfPart[(int)part]];

    /// <summary>Call once per sim tick, after the player moved: the pose to interpolate towards.</summary>
    public void Capture()
    {
        (_previous, _current) = (_current, _previous);
        _sim.PlayerHits.PoseNow(_state, _current);
    }

    public override void _Process(double delta)
    {
        if (_state is not null)
        {
            Visible = _state.Present;
            ApplyPose((float)Engine.GetPhysicsInterpolationFraction());
        }
    }

    private void ApplyPose(float alpha)
    {
        for (int i = 0; i < _parts.Length; i++)
        {
            ref PosedBox a = ref _previous[i];
            ref PosedBox b = ref _current[i];
            Quaternion q = Quat(a).Slerp(Quat(b), alpha);
            Vector3 centre = a.Center.ToGodot().Lerp(b.Center.ToGodot(), alpha);
            _parts[i].GlobalTransform = new Transform3D(new Basis(q), centre);
            _meshes[i].Scale = a.HalfExtents.ToGodot().Lerp(b.HalfExtents.ToGodot(), alpha) * 2f;
        }
    }

    private static Quaternion Quat(in PosedBox b)
    {
        System.Numerics.Quaternion q = b.Rotation;
        return new Quaternion(q.X, q.Y, q.Z, q.W).Normalized();
    }

    private static StandardMaterial3D MaterialFor(HitboxPart part, Color jersey)
    {
        (Color color, float roughness, float metallic) = part switch
        {
            HitboxPart.Torso => (jersey.Darkened(0.15f), 0.85f, 0f),
            HitboxPart.Arms => (jersey.Darkened(0.3f), 0.85f, 0f),
            HitboxPart.Legs => (new Color(0.17f, 0.18f, 0.19f), 0.9f, 0f),
            HitboxPart.Head => (new Color(0.22f, 0.22f, 0.23f), 0.9f, 0f),
            HitboxPart.Mask => (new Color(0.07f, 0.075f, 0.08f), 0.35f, 0.1f),
            HitboxPart.Marker => (new Color(0.12f, 0.13f, 0.14f), 0.45f, 0.5f),
            HitboxPart.Loader => (jersey.Lightened(0.1f), 0.4f, 0f),
            HitboxPart.Tank => (new Color(0.72f, 0.74f, 0.77f), 0.3f, 0.85f),
            _ => (Colors.Gray, 0.8f, 0f),
        };
        return new StandardMaterial3D { AlbedoColor = color, Roughness = roughness, Metallic = metallic };
    }
}
