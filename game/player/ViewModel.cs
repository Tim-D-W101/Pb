using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Game.World;
using Pb.Sim.Data;

namespace Pb.Game.Player;

/// <summary>
/// First-person marker with loader and tank (spec §6), the model built in code (<see cref="MarkerShape"/>),
/// with the player's paint in its loader. It's drawn by viewmodel.gdshader
/// with its own FOV and a squashed depth range, so it never clips into the bunker you're
/// hugging. It sits on its own render layer so paint decals don't project onto it.
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

    public void Build(ViewModelDef def, Color loaderColor)
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
        var shape = new ShapeMesh();
        MarkerShape.Build(shape, closeUp: true);
        HandShape.Build(shape);
        var mesh = new ArrayMesh();
        shape.Commit(mesh, part => materials[part]);
        AddChild(new MeshInstance3D
        {
            Name = "Marker",
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = RenderLayer,
        });
    }

    /// <summary>Which shoulder the marker is on: +1 right, −1 left, in between mid-swap (follows the sim).</summary>
    public float Side { get; set; } = 1f;

    /// <summary>Small recoil kick when the local player fires.</summary>
    public void Kick() => _kick = 1f;

    public override void _Process(double delta)
    {
        if (_kick > 0f)
        {
            _kick = Mathf.Max(0f, _kick - (float)delta / _kickRecover);
        }

        Position = Rest + new Vector3(0, 0, _kickBack * _kick * _kick);
        // On the left shoulder the hands swap: the whole model is mirrored, while it passes under your chin.
        Basis = Tilt * Basis.FromScale(new Vector3(Side < 0f ? -1f : 1f, 1f, 1f));
    }

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
