using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Game.World;
using Pb.Sim.Data;

namespace Pb.Game.Player;

/// <summary>
/// First-person marker with loader and tank (spec §6): the generated model (<see cref="MarkerModel"/>), with
/// your gloved hands moved onto its grips, or without it the model built in code (<see cref="MarkerShape"/>),
/// with the player's paint showing in its see-through loader. It's drawn by viewmodel.gdshader
/// with its own FOV and a squashed depth range, so it never clips into the bunker you're
/// hugging. It sits on its own render layer so paint decals don't project onto it. While you refill
/// the loader from a pod, the marker cants towards you, the support hand drops off the foregrip and
/// comes back up with a pod, tips it into the loader (paint pouring in) and goes back to the foregrip,
/// in step with the sim's refill (<see cref="RefillProgress"/>).
/// </summary>
public partial class ViewModel : Node3D
{
    public const uint RenderLayer = 1u << 1;

    private Shader _shader = null!;
    private Shader _clearShader = null!;
    private Vector3 _rest;
    private Basis _tilt = Basis.Identity;
    private Vector3 _muzzleTip;
    private float _viewFov;
    private float _kick;
    private float _kickBack;
    private float _kickRecover;
    private MultiMesh? _paint;
    private Node3D _support = null!;
    private Vector3 _foregrip;
    private Node3D _podHand = null!;
    private Vector3 _podTop;
    private readonly MeshInstance3D[] _pouring = new MeshInstance3D[5];
    private float _phase = 1f;
    private float _time;

    /// <summary>Where the pod's mouth is as it pours, and which way it points (marker frame: right, up, back).</summary>
    private static readonly Vector3 Mouth = new(-0.03f, 0.238f, 0f), Pour = new Vector3(0.75f, -0.62f, 0.15f).Normalized();

    public void Build(ViewModelDef def, MarkerModelDef markerModel, Color loaderColor)
    {
        _shader = GD.Load<Shader>("res://shaders/viewmodel.gdshader");
        _clearShader = GD.Load<Shader>("res://shaders/viewmodel_clear.gdshader");
        _rest = FromRightUpForward(Validator.ToVector3(def.Offset_m));
        System.Numerics.Vector3 tilt = Validator.ToVector3(def.Rotation_deg);
        _tilt = Basis.FromEuler(new Vector3(Mathf.DegToRad(tilt.X), Mathf.DegToRad(tilt.Y), Mathf.DegToRad(tilt.Z)));
        _muzzleTip = FromRightUpForward(Validator.ToVector3(def.MuzzleTip_m));
        _viewFov = def.Fov_deg;
        _kickBack = def.KickBack_m;
        _kickRecover = def.KickRecover_s;
        Position = _rest;
        Basis = _tilt;

        // The marker built in code (MarkerShape) in your gloved hands (HandShape): one mesh with a
        // surface per material, the loader's shell see-through so the paint shows in it.
        var materials = new Dictionary<int, Material>
        {
            [(int)MarkerPart.Body] = Material(new Color(0.11f, 0.115f, 0.12f), 0.42f, 0.35f),
            [(int)MarkerPart.Barrel] = Material(new Color(0.05f, 0.05f, 0.055f), 0.3f, 0.45f),
            [(int)MarkerPart.Rubber] = Material(new Color(0.045f, 0.045f, 0.05f), 0.85f, 0f),
            [(int)MarkerPart.Trim] = Material(new Color(0.5f, 0.51f, 0.53f), 0.35f, 0.85f),
            [(int)MarkerPart.Tank] = Material(new Color(0.78f, 0.79f, 0.81f), 0.22f, 0.95f),
            [(int)MarkerPart.Shell] = Material(new Color(0.16f, 0.17f, 0.19f, 0.62f), 0.12f, 0f, clear: true),
            [(int)MarkerPart.Lid] = Material(new Color(0.12f, 0.13f, 0.14f), 0.3f, 0f),
            [(int)MarkerPart.Paint] = Material(loaderColor, 0.25f, 0f),
            [HandPart.Glove] = Material(Color.FromHtml(def.GloveColor), 0.72f, 0f),
            [HandPart.Cuff] = Material(Color.FromHtml(def.GloveColor).Darkened(0.2f), 0.85f, 0f),
            [HandPart.Sleeve] = Material(Color.FromHtml(def.SleeveColor), 0.92f, 0f),
        };
        _foregrip = HandShape.ForegripTop;
        if (MarkerModel.Create(markerModel, _muzzleTip, markerModel.ViewScale) is { } model)
        {
            // The generated model, its muzzle on the barrel tip, with its own textures; the hands are
            // the coded ones, moved onto its grips. Its loader isn't see-through, so no paint shows.
            ToViewModel(model);
            AddChild(model);
            var trigger = new ShapeMesh();
            HandShape.BuildTrigger(trigger);
            AddChild(Part("TriggerHand", trigger, materials, HandShape.PistolGripTop - MarkerModel.Point(model, markerModel.PistolGrip_m)));
            _foregrip = MarkerModel.Point(model, markerModel.Foregrip_m);
        }
        else
        {
            var shape = new ShapeMesh();
            MarkerShape.Build(shape, closeUp: true, paint: false);
            HandShape.BuildTrigger(shape);
            AddChild(Part("Marker", shape, materials, Vector3.Zero));

            // The paint in the loader, lowest balls first, drawn as far up as the loader is full.
            var one = new ShapeMesh();
            one.Pillow((int)MarkerPart.Paint, Vector3.Zero, new Vector3(MarkerShape.Ball, MarkerShape.Ball, MarkerShape.Ball) * 2f, Basis.Identity, 2f, 6, 9);
            var ballMesh = new ArrayMesh();
            one.Commit(ballMesh, part => materials[part]);
            List<Vector3> balls = MarkerShape.LoaderBalls(1);
            _paint = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = ballMesh, InstanceCount = balls.Count };
            for (int i = 0; i < balls.Count; i++)
            {
                _paint.SetInstanceTransform(i, new Transform3D(Basis.Identity, balls[i]));
            }

            AddChild(new MultiMeshInstance3D
            {
                Name = "Paint", Multimesh = _paint, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = RenderLayer,
            });
        }

        // The support hand on its own, turning about the top of the foregrip, and the hand that brings
        // a pod up to the loader (hidden until you refill), turning about where it grips the pod.
        var support = new ShapeMesh();
        HandShape.BuildSupport(support);
        _support = Pivot("SupportHand", support, materials, HandShape.ForegripTop);
        _support.Position = _foregrip;
        var pod = new ShapeMesh();
        _podTop = HandShape.BuildPodHand(pod, Mouth, Pour, 24);
        _podHand = Pivot("PodHand", pod, materials, _podTop);
        _podHand.Visible = false;
        var ball = new SphereMesh { Radius = 0.0087f, Height = 0.0174f, RadialSegments = 8, Rings = 4 };
        for (int i = 0; i < _pouring.Length; i++)
        {
            _pouring[i] = new MeshInstance3D
            {
                Name = $"Pouring{i}", Mesh = ball, MaterialOverride = materials[(int)MarkerPart.Paint],
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = RenderLayer, Visible = false,
            };
            AddChild(_pouring[i]);
        }
    }

    /// <summary>
    /// How far through refilling the loader from a pod you are (0-1), or −1 when you aren't: the
    /// marker plays the refill in step (and finishes it quickly if the refill stops early).
    /// </summary>
    public float RefillProgress { get; set; } = -1f;

    /// <summary>How full the loader is (0-1): the paint in the coded marker's see-through loader shows that far up.</summary>
    public float LoaderFill
    {
        set
        {
            if (_paint is not null)
            {
                _paint.VisibleInstanceCount = Mathf.Clamp(Mathf.CeilToInt(value * _paint.InstanceCount), 0, _paint.InstanceCount);
            }
        }
    }

    private MeshInstance3D Part(string name, ShapeMesh shape, Dictionary<int, Material> materials, Vector3 offset)
    {
        var mesh = new ArrayMesh();
        shape.Commit(mesh, part => materials[part]);
        return new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            Position = -offset,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = RenderLayer,
        };
    }

    /// <summary>A part built in the marker's frame, under a pivot at <paramref name="at"/> so it can be turned about that point.</summary>
    private Node3D Pivot(string name, ShapeMesh shape, Dictionary<int, Material> materials, Vector3 at)
    {
        var pivot = new Node3D { Name = name, Position = at };
        pivot.AddChild(Part("Mesh", shape, materials, at));
        AddChild(pivot);
        return pivot;
    }

    /// <summary>Which shoulder the marker is on: +1 right, −1 left, in between mid-swap (follows the sim).</summary>
    public float Side { get; set; } = 1f;

    /// <summary>Small recoil kick when the local player fires.</summary>
    public void Kick() => _kick = 1f;

    /// <summary>On a ladder: the marker goes down out of sight (it's slung) while both hands are on the rungs.</summary>
    public bool Lowered { get; set; }

    private float _low;

    public override void _Process(double delta)
    {
        if (_kick > 0f)
        {
            _kick = Mathf.Max(0f, _kick - (float)delta / _kickRecover);
        }

        // The refill plays in step with the sim; stopped early, it runs on quickly to the end.
        _time += (float)delta;
        float progress = RefillProgress;
        if (progress >= 0f)
        {
            _phase = progress;
        }
        else
        {
            _phase = Mathf.MoveToward(_phase, 1f, (float)delta * 2.5f);
        }

        float f = _phase;
        float cant = Smooth(0f, 0.12f, f) * (1f - Smooth(0.86f, 1f, f));
        _low = Mathf.MoveToward(_low, Lowered ? 1f : 0f, (float)delta / 0.3f);
        float low = Smooth(0f, 1f, _low);
        Visible = low < 0.98f;
        Position = Rest + new Vector3(0, 0, _kickBack * _kick * _kick) + new Vector3(0f, -0.02f, 0.015f) * cant + new Vector3(0.04f, -0.34f, 0.1f) * low;
        // On the left shoulder the hands swap: the whole model is mirrored, while it passes under your chin.
        // Refilling, it rolls the loader towards you and tips its nose up a little; going onto a ladder, its nose drops away.
        Basis = Tilt * Basis.FromEuler(new Vector3(Mathf.DegToRad(7f) * cant - Mathf.DegToRad(40f) * low, 0f, Mathf.DegToRad(-16f) * cant)) *
                Basis.FromScale(new Vector3(Side < 0f ? -1f : 1f, 1f, 1f));

        // The support hand drops off the foregrip, down and to the left out of sight, and comes back.
        float off = Smooth(0f, 0.12f, f) * (1f - Smooth(0.88f, 1f, f));
        _support.Position = _foregrip + new Vector3(-0.1f, -0.3f, 0.1f) * off;
        _support.Basis = new Basis(Vector3.Back, Mathf.DegToRad(25f) * off);

        // The pod comes up lying on its side, tips over into the loader, pours, and goes back down.
        float hold = Smooth(0.12f, 0.3f, f) * (1f - Smooth(0.76f, 0.9f, f));
        _podHand.Visible = hold > 0.001f;
        float shake = Mathf.Sin(_time * 19f) * 0.003f * Smooth(0.85f, 1f, hold);
        _podHand.Position = _podTop + new Vector3(-0.12f, -0.32f, 0.12f) * (1f - hold) + new Vector3(shake, shake * 0.5f, 0f);
        _podHand.Basis = new Basis(Vector3.Back, Mathf.DegToRad(60f) * (1f - hold));

        // While it pours, balls tumble out of the mouth into the loader.
        bool pouring = hold > 0.97f;
        for (int i = 0; i < _pouring.Length; i++)
        {
            _pouring[i].Visible = pouring;
            if (pouring)
            {
                float t = (_time * 3.2f + i / (float)_pouring.Length) % 1f;
                _pouring[i].Position = Mouth + Pour * (0.012f + 0.03f * t) + Vector3.Down * (0.05f * t * t) + new Vector3(0.004f * Mathf.Sin(i * 2.4f), 0f, 0.004f * Mathf.Cos(i * 2.4f));
            }
        }
    }

    private static float Smooth(float from, float to, float x) => Mathf.SmoothStep(from, to, x);

    /// <summary>Rest position, mirrored across the face for a left-shoulder hold (and dipped mid-swap).</summary>
    private Vector3 Rest => new(_rest.X * Side, _rest.Y - 0.06f * (1f - Mathf.Abs(Side)), _rest.Z);

    /// <summary>The barrel's yaw toward the crosshair flips with the side.</summary>
    private Basis Tilt
    {
        get
        {
            Vector3 euler = _tilt.GetEuler();
            return Basis.FromEuler(new Vector3(euler.X, euler.Y * Side, euler.Z * Side));
        }
    }

    /// <summary>
    /// World point that appears where the barrel tip is drawn. The marker is projected with its
    /// own FOV, so its on-screen position is re-projected through the main camera.
    /// </summary>
    public Vector3 ApparentMuzzle(Camera3D camera)
    {
        Vector3 tipInCamera = Rest + Tilt * _muzzleTip;
        float scale = Mathf.Tan(Mathf.DegToRad(camera.Fov) * 0.5f) / Mathf.Tan(Mathf.DegToRad(_viewFov) * 0.5f);
        var apparent = new Vector3(tipInCamera.X * scale, tipInCamera.Y * scale, tipInCamera.Z);
        return camera.GlobalTransform * apparent;
    }

    private static Vector3 FromRightUpForward(System.Numerics.Vector3 v) => new(v.X, v.Y, -v.Z);

    /// <summary>Gives every surface of the generated model the view-model shader, with the model's own textures.</summary>
    private void ToViewModel(Node root)
    {
        foreach (Node node in root.FindChildren("*", nameof(MeshInstance3D), recursive: true, owned: false))
        {
            var mesh = (MeshInstance3D)node;
            mesh.Layers = RenderLayer;
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            for (int i = 0; i < mesh.GetSurfaceOverrideMaterialCount(); i++)
            {
                if (mesh.Mesh?.SurfaceGetMaterial(i) is not BaseMaterial3D source)
                {
                    continue;
                }

                ShaderMaterial material = Material(source.AlbedoColor, source.Roughness, source.Metallic);
                void Texture(string name, Texture2D? texture)
                {
                    if (texture is not null)
                    {
                        material.SetShaderParameter(name, texture);
                    }
                }

                Texture("albedo_tex", source.AlbedoTexture);
                Texture("roughness_tex", source.RoughnessTexture);
                material.SetShaderParameter("roughness_channel", Channel(source.RoughnessTextureChannel));
                Texture("metallic_tex", source.MetallicTexture);
                material.SetShaderParameter("metallic_channel", Channel(source.MetallicTextureChannel));
                material.SetShaderParameter("use_normal_tex", source.NormalEnabled && source.NormalTexture is not null);
                Texture("normal_tex", source.NormalTexture);
                mesh.SetSurfaceOverrideMaterial(i, material);
            }
        }
    }

    private static Vector4 Channel(BaseMaterial3D.TextureChannel channel) => channel switch
    {
        BaseMaterial3D.TextureChannel.Red => new Vector4(1f, 0f, 0f, 0f),
        BaseMaterial3D.TextureChannel.Green => new Vector4(0f, 1f, 0f, 0f),
        BaseMaterial3D.TextureChannel.Blue => new Vector4(0f, 0f, 1f, 0f),
        BaseMaterial3D.TextureChannel.Alpha => new Vector4(0f, 0f, 0f, 1f),
        _ => new Vector4(1f / 3f, 1f / 3f, 1f / 3f, 0f),
    };

    private ShaderMaterial Material(Color albedo, float roughness, float metallic, bool clear = false)
    {
        var material = new ShaderMaterial { Shader = clear ? _clearShader : _shader };
        material.SetShaderParameter("albedo", albedo);
        material.SetShaderParameter("roughness", roughness);
        material.SetShaderParameter("metallic", metallic);
        material.SetShaderParameter("viewmodel_fov_deg", _viewFov);
        return material;
    }
}
