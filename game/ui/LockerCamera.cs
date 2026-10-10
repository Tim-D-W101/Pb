using System;
using Godot;
using Pb.Game.Core;

namespace Pb.Game.Ui;

/// <summary>
/// The gear locker's camera (presentation.jsonc "locker"): it looks at you from a framing (the whole of you, or one
/// slot close up), turns round you as you drag, comes nearer or goes further as you scroll, and eases round to a
/// slot's framing when you pick one. It stops short of anything in the room between it and you.
/// </summary>
public partial class LockerCamera : Camera3D
{
    private LockerDef _def = null!;
    private Vector3 _centre;
    private Vector3 _front;
    private Vector3 _right;
    private Func<Vector3, Vector3, float> _clear = null!;
    // As drawn, and where each is easing to: round you (yaw, from straight in front, positive to your right), down on
    // you (pitch), how far off, the field of view and the point looked at.
    private float _yaw, _pitch, _distance, _fov;
    private float _toYaw, _toPitch, _toDistance, _toFov;
    private Vector3 _target, _toTarget;

    /// <summary>How far left (negative) or right of the screen's middle you're drawn, as a share of half its width (the panels' room).</summary>
    public float Shift { get; set; }

    /// <param name="centre">The top of the turntable, under your feet.</param>
    /// <param name="facing">Which way you face (yaw).</param>
    /// <param name="clear">How far along a line from the first point to the second is clear of the room (m).</param>
    public void Initialize(LockerDef def, Vector3 centre, float facing, Func<Vector3, Vector3, float> clear)
    {
        _def = def;
        _centre = centre;
        _front = new Vector3(-Mathf.Sin(facing), 0f, -Mathf.Cos(facing));
        _right = _front.Cross(Vector3.Up);
        _clear = clear;
        Near = 0.03f;
        Far = 300f;
        Frame(def.All, centre + Vector3.Up * def.All.Height_m, snap: true);
    }

    /// <summary>Eases round to <paramref name="framing"/> (the short way) looking at <paramref name="at"/>, or goes straight there.</summary>
    public void Frame(LockerFramingDef framing, Vector3 at, bool snap = false)
    {
        _toYaw = _yaw + Mathf.Wrap(Mathf.DegToRad(framing.Yaw_deg) - _yaw, -Mathf.Pi, Mathf.Pi);
        _toPitch = Mathf.DegToRad(framing.Pitch_deg);
        _toDistance = framing.Distance_m;
        _toTarget = at;
        _toFov = framing.Fov_deg;
        if (snap)
        {
            (_yaw, _pitch, _distance, _target, _fov) = (_toYaw, _toPitch, _toDistance, _toTarget, _toFov);
            Place();
        }
    }

    /// <summary>
    /// Turns round you by a drag of <paramref name="by"/> pixels: across turns you as if you'd taken hold of yourself
    /// and spun round (your front follows the mouse), down looks down on you more.
    /// </summary>
    public void Drag(Vector2 by)
    {
        float k = Mathf.DegToRad(_def.DragDegPerPx);
        float min = Mathf.DegToRad(_def.MinPitch_deg), max = Mathf.DegToRad(_def.MaxPitch_deg);
        _yaw += by.X * k;
        _toYaw += by.X * k;
        _pitch = Mathf.Clamp(_pitch + by.Y * k, min, max);
        _toPitch = Mathf.Clamp(_toPitch + by.Y * k, min, max);
    }

    /// <summary>Where it is and what it's looking at, for the tour's log.</summary>
    public string Describe() =>
        $"yaw {Mathf.RadToDeg(_yaw):0}° pitch {Mathf.RadToDeg(_pitch):0}° at {_target.X:0.00},{_target.Y:0.00},{_target.Z:0.00} " +
        $"from {GlobalPosition.DistanceTo(_target):0.00} m (wanted {_distance:0.00}), fov {_fov:0}°";

    /// <summary>Comes nearer (negative notches) or goes further.</summary>
    public void Zoom(float notches) =>
        _toDistance = Mathf.Clamp(_toDistance + notches * _def.ScrollStep_m, _def.MinDistance_m, _def.MaxDistance_m);

    public override void _Process(double delta)
    {
        if (_def is null)
        {
            return;
        }

        // Most of the way there over ease_s.
        float k = 1f - Mathf.Exp(-(float)delta * 3f / _def.Ease_s);
        _yaw = Mathf.Lerp(_yaw, _toYaw, k);
        _pitch = Mathf.Lerp(_pitch, _toPitch, k);
        _distance = Mathf.Lerp(_distance, _toDistance, k);
        _target = _target.Lerp(_toTarget, k);
        _fov = Mathf.Lerp(_fov, _toFov, k);
        Place();
    }

    private void Place()
    {
        Fov = _fov;
        Vector3 target = _target;
        Vector3 round = (_front * Mathf.Cos(_yaw) + _right * Mathf.Sin(_yaw)) * Mathf.Cos(_pitch) + Vector3.Up * Mathf.Sin(_pitch);
        float distance = Mathf.Min(_distance, _clear(target, target + round * _distance));
        // Off to one side of the screen: the camera and the point it looks at both move across, so you stay square on.
        Vector2 size = GetViewport()?.GetVisibleRect().Size ?? new Vector2(16f, 9f);
        float aspect = size.Y > 0f ? size.X / size.Y : 16f / 9f;
        Vector3 across = (-round).Cross(Vector3.Up).Normalized();
        Vector3 offset = across * (-Shift * distance * Mathf.Tan(Mathf.DegToRad(Fov) * 0.5f) * aspect);
        Vector3 at = target + offset;
        GlobalPosition = at + round * distance;
        LookAt(at, Vector3.Up);
    }
}
