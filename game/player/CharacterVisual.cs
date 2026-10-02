using Godot;
using Pb.Game.Ballistics;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Collision;
using Pb.Sim.Players;

namespace Pb.Game.Player;

/// <summary>
/// Draws a character from the sim's hitbox rig, interpolated between ticks. With a rigged model
/// (<see cref="CharacterModel"/>) the body is the model, posed to match the hitboxes, and the gear
/// (marker, loader, tank) gets shapes fitted inside its boxes (<see cref="GearShapes"/>), with the hands
/// on the marker; without one, every hitbox is drawn as a box, so what you see is exactly what you
/// can hit. Each part is an unscaled node with the box or shapes under it, so splats parented to a
/// part keep their shape and move with it.
/// </summary>
public partial class CharacterVisual : Node3D
{
    private readonly Node3D[] _parts = new Node3D[HitboxRig.PartCount];
    private readonly MeshInstance3D?[] _meshes = new MeshInstance3D?[HitboxRig.PartCount];
    private readonly Vector3[] _half = new Vector3[HitboxRig.PartCount];
    private readonly int[] _indexOfPart = new int[16];
    private PosedBox[] _previous = new PosedBox[HitboxRig.PartCount];
    private PosedBox[] _current = new PosedBox[HitboxRig.PartCount];
    private HitboxPose _poseBefore;
    private HitboxPose _poseNow;
    private SimWorld _sim = null!;
    private PlayerState _state = null!;
    private CharacterModel? _model;
    private CharactersDef? _look;
    private float _standEye;
    private float _crouchEye;
    private Vector3 _lastFeet;

    public bool HasModel => _model is not null;

    public void Build(SimWorld sim, PlayerState state, Color jersey, CharactersDef? characters = null, int index = 0)
    {
        _sim = sim;
        _state = state;
        _look = characters;
        _standEye = sim.Config.Movement.StandEyeHeight;
        _crouchEye = sim.Config.Movement.CrouchEyeHeight;
        TopLevel = true;
        var box = new BoxMesh { Size = Vector3.One };
        _sim.PlayerHits.PoseNow(state, _current);
        _poseNow = _poseBefore = HitboxPose.Of(state);
        _lastFeet = state.Position.ToGodot();
        if (characters is { Models.Length: > 0 })
        {
            Color tint = characters.Tints.Length > 0 ? Color.FromHtml(characters.Tints[index % characters.Tints.Length]) : Colors.White;
            _model = CharacterModel.TryCreate(characters.Models[index % characters.Models.Length], tint, jersey, characters);
            if (_model is not null)
            {
                AddChild(_model);
            }
        }

        for (int i = 0; i < _parts.Length; i++)
        {
            HitboxPart part = _current[i].Part;
            _indexOfPart[(int)part] = i;
            _parts[i] = new Node3D { Name = part.ToString() };
            AddChild(_parts[i]);
            if (_model is not null && IsGear(part))
            {
                // Gear keeps its size, so its shapes are built once.
                GearShapes.Build(_parts[i], part, _current[i].HalfExtents.ToGodot() * 2f, jersey, index + 1);
                continue;
            }

            _meshes[i] = new MeshInstance3D { Mesh = box, MaterialOverride = MaterialFor(part, jersey), Visible = _model is null };
            _parts[i].AddChild(_meshes[i]);
        }

        Capture();
        ApplyPose(1f);
    }

    /// <summary>What a splat at <paramref name="point"/> on <paramref name="part"/> should stick to.</summary>
    public SplatAnchor PartNode(HitboxPart part, Vector3 point) =>
        _model?.PartNode(part, point) ?? new SplatAnchor(_parts[_indexOfPart[(int)part]]);

    /// <summary>Call once per sim tick, after the player moved: the pose to interpolate towards.</summary>
    public void Capture()
    {
        (_previous, _current) = (_current, _previous);
        _sim.PlayerHits.PoseNow(_state, _current);
        _poseBefore = _poseNow;
        _poseNow = HitboxPose.Of(_state);
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
            _half[i] = a.HalfExtents.ToGodot().Lerp(b.HalfExtents.ToGodot(), alpha);
            if (_meshes[i] is { } mesh)
            {
                mesh.Scale = _half[i] * 2f;
            }
        }

        if (_model is not null && _look is not null)
        {
            PoseModel(alpha);
        }
    }

    /// <summary>Stands the model where the player stands and hands the poser this frame's pose.</summary>
    private void PoseModel(float alpha)
    {
        Vector3 feet = _poseBefore.Position.ToGodot().Lerp(_poseNow.Position.ToGodot(), alpha);
        float yaw = Mathf.LerpAngle(_poseBefore.Yaw, _poseNow.Yaw, alpha);
        float eye = Mathf.Lerp(_poseBefore.EyeHeight, _poseNow.EyeHeight, alpha);
        _model!.GlobalTransform = new Transform3D(new Basis(Vector3.Up, yaw), feet);

        CharacterPoser poser = _model.Poser;
        poser.Forward = new Vector3(-Mathf.Sin(yaw), 0f, -Mathf.Cos(yaw));
        poser.Right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
        poser.HipDrop = Mathf.Max(0f, _standEye - eye) * _look!.HipDropPerEyeDrop;
        poser.LeanRoll = Mathf.Lerp(_poseBefore.LeanRoll, _poseNow.LeanRoll, alpha);
        poser.Pitch = _poseNow.Alive ? Mathf.Lerp(_poseBefore.Pitch, _poseNow.Pitch, alpha) : 0f;
        poser.RightHanded = Mathf.Lerp(_poseBefore.Shoulder, _poseNow.Shoulder, alpha) >= 0f;

        // Wrists on the marker: the trigger hand near its back, the other under the front.
        int marker = _indexOfPart[(int)HitboxPart.Marker];
        Transform3D frame = _parts[marker].GlobalTransform;
        Vector3 half = _half[marker];
        Vector3 Grip(float along) => frame * new Vector3(0f, -half.Y - _look.GripDrop_m, half.Z * (1f - 2f * along));
        poser.TriggerHand = Grip(_look.TriggerGrip);
        poser.SupportHand = Grip(_look.SupportGrip);

        // The legs, by the ground the feet covered since the last frame.
        Vector3 moved = feet - _lastFeet;
        _lastFeet = feet;
        float crouch = (_standEye - eye) / Mathf.Max(_standEye - _crouchEye, 0.01f);
        _model.Gait.Update(moved, _state.Velocity.ToGodot(), poser.Forward, crouch, _state.Stance == Stance.Sliding,
            _state.Grounded, (float)GetProcessDeltaTime());
    }

    private static bool IsGear(HitboxPart part) => part is HitboxPart.Marker or HitboxPart.Loader or HitboxPart.Tank;

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
