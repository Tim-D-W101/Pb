using Godot;
using Pb.Game.Core;
using Pb.Sim.Data;

namespace Pb.Game.Player;

/// <summary>
/// Greybox first-person marker with loader and tank (spec §6). It's drawn by viewmodel.gdshader
/// with its own FOV and a squashed depth range, so it never clips into the bunker you're
/// hugging. It sits on its own render layer so paint decals don't project onto it.
/// </summary>
public partial class ViewModel : Node3D
{
    public const uint RenderLayer = 1u << 1;

    private Shader _shader = null!;
    private Vector3 _rest;
    private Basis _tilt = Basis.Identity;
    private Vector3 _muzzleTip;
    private float _viewFov;
    private float _kick;
    private float _kickBack;
    private float _kickRecover;

    public void Build(ViewModelDef def, Color loaderColor)
    {
        _shader = GD.Load<Shader>("res://shaders/viewmodel.gdshader");
        _rest = FromRightUpForward(Validator.ToVector3(def.Offset_m));
        System.Numerics.Vector3 tilt = Validator.ToVector3(def.Rotation_deg);
        _tilt = Basis.FromEuler(new Vector3(Mathf.DegToRad(tilt.X), Mathf.DegToRad(tilt.Y), Mathf.DegToRad(tilt.Z)));
        _muzzleTip = FromRightUpForward(Validator.ToVector3(def.MuzzleTip_m));
        _viewFov = def.Fov_deg;
        _kickBack = def.KickBack_m;
        _kickRecover = def.KickRecover_s;
        Position = _rest;
        Basis = _tilt;

        var body = Material(new Color(0.16f, 0.17f, 0.19f), 0.5f, 0.2f);
        var dark = Material(new Color(0.07f, 0.07f, 0.08f), 0.4f, 0.3f);
        var loader = Material(loaderColor, 0.35f, 0f);
        var tank = Material(new Color(0.72f, 0.74f, 0.78f), 0.28f, 0.9f);

        // Local frame: +X right, +Y up, −Z forward (down the barrel).
        Part(new BoxMesh { Size = new Vector3(0.05f, 0.08f, 0.26f) }, body, new Vector3(0, 0, 0));
        Part(new CylinderMesh { TopRadius = 0.013f, BottomRadius = 0.013f, Height = 0.30f, RadialSegments = 12 },
            dark, new Vector3(0, 0.02f, -0.28f), new Vector3(Mathf.Pi / 2, 0, 0));
        Part(new CylinderMesh { TopRadius = 0.018f, BottomRadius = 0.018f, Height = 0.06f, RadialSegments = 10 },
            dark, new Vector3(0, 0.055f, -0.02f));
        Part(new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 16, Rings = 8 },
            loader, new Vector3(0, 0.1f, 0.01f), Vector3.Zero, new Vector3(0.048f, 0.052f, 0.08f));
        Part(new BoxMesh { Size = new Vector3(0.03f, 0.1f, 0.036f) }, dark, new Vector3(0, -0.08f, 0.07f), new Vector3(-0.26f, 0, 0));
        Part(new CylinderMesh { TopRadius = 0.034f, BottomRadius = 0.034f, Height = 0.24f, RadialSegments = 14 },
            tank, new Vector3(0, -0.09f, -0.04f), new Vector3(Mathf.Pi / 2, 0, 0));
    }

    /// <summary>Small recoil kick when the local player fires.</summary>
    public void Kick() => _kick = 1f;

    public override void _Process(double delta)
    {
        if (_kick > 0f)
        {
            _kick = Mathf.Max(0f, _kick - (float)delta / _kickRecover);
        }

        Position = _rest + new Vector3(0, 0, _kickBack * _kick * _kick);
    }

    /// <summary>
    /// World point that appears where the barrel tip is drawn. The marker is projected with its
    /// own FOV, so its on-screen position is re-projected through the main camera.
    /// </summary>
    public Vector3 ApparentMuzzle(Camera3D camera)
    {
        Vector3 tipInCamera = _rest + _tilt * _muzzleTip;
        float scale = Mathf.Tan(Mathf.DegToRad(camera.Fov) * 0.5f) / Mathf.Tan(Mathf.DegToRad(_viewFov) * 0.5f);
        var apparent = new Vector3(tipInCamera.X * scale, tipInCamera.Y * scale, tipInCamera.Z);
        return camera.GlobalTransform * apparent;
    }

    private static Vector3 FromRightUpForward(System.Numerics.Vector3 v) => new(v.X, v.Y, -v.Z);

    private ShaderMaterial Material(Color albedo, float roughness, float metallic)
    {
        var material = new ShaderMaterial { Shader = _shader };
        material.SetShaderParameter("albedo", albedo);
        material.SetShaderParameter("roughness", roughness);
        material.SetShaderParameter("metallic", metallic);
        material.SetShaderParameter("viewmodel_fov_deg", _viewFov);
        return material;
    }

    private void Part(Mesh mesh, Material material, Vector3 position, Vector3 rotation = default, Vector3? scale = null)
    {
        var instance = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = material,
            Position = position,
            Rotation = rotation,
            Scale = scale ?? Vector3.One,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = RenderLayer,
        };
        AddChild(instance);
    }
}
