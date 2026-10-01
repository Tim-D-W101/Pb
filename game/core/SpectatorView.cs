using System;
using Godot;
using Pb.Sim.Collision;
using Pb.Sim.Players;

namespace Pb.Game.Core;

/// <summary>
/// After you're eliminated (spec §1.2): a camera above and behind where you stood, turned to follow
/// whoever got you, with a banner naming them, the part hit and the distance. After a few seconds
/// the banner goes and the camera holds while the round summary shows.
/// </summary>
public partial class SpectatorView : Node3D
{
    private Camera3D _camera = null!;
    private PlayerState _victim = null!;
    private PlayerState? _shooter;
    private Action _finished = null!;
    private CanvasLayer _layer = null!;
    private float _left;

    public void Start(PlayerState victim, PlayerState? shooter, SpectatorDef def, float farClip, Action finished)
    {
        _victim = victim;
        _shooter = shooter;
        _finished = finished;
        _left = def.Duration_s;

        Vector3 eye = victim.EyePosition.ToGodot();
        Vector3 towards = shooter is not null ? (Focus() - eye) with { Y = 0f } : Vector3.Forward;
        towards = towards.LengthSquared() > 1e-4f ? towards.Normalized() : Vector3.Forward;
        _camera = new Camera3D { Name = "SpectatorCamera", Fov = 62f, Near = 0.05f, Far = farClip, TopLevel = true };
        AddChild(_camera);
        _camera.GlobalPosition = eye + Vector3.Up * def.Height_m - towards * def.Back_m;
        _camera.LookAt(Focus(), Vector3.Up);
        _camera.MakeCurrent();

        string who = shooter is null ? "a stray ball" : shooter.Name;
        float distance = shooter is null ? 0f : System.Numerics.Vector3.Distance(shooter.EyePosition, victim.EyePosition);
        var banner = new Label
        {
            Text = shooter is null
                ? "ELIMINATED"
                : $"ELIMINATED by {who}\n{PartName(victim.EliminatedPart)} · {distance:0} m",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            OffsetTop = 60f,
        };
        banner.AddThemeFontSizeOverride("font_size", 34);
        banner.AddThemeColorOverride("font_outline_color", Colors.Black);
        banner.AddThemeConstantOverride("outline_size", 10);
        _layer = new CanvasLayer { Layer = 6 };
        _layer.AddChild(banner);
        AddChild(_layer);
    }

    public override void _Process(double delta)
    {
        if (_camera is null)
        {
            return;
        }

        // Keep the shooter in view as they move.
        Transform3D target = _camera.GlobalTransform.LookingAt(Focus(), Vector3.Up);
        _camera.GlobalTransform = _camera.GlobalTransform.InterpolateWith(target, Mathf.Min(1f, (float)delta * 4f));
        _left -= (float)delta;
        if (_left <= 0f)
        {
            _camera = null!;
            _layer.Visible = false;
            _finished();
        }
    }

    private Vector3 Focus() => (_shooter ?? _victim).Position.ToGodot() + Vector3.Up * 1.3f;

    public static string PartName(HitboxPart part) => part switch
    {
        HitboxPart.Mask => "mask",
        HitboxPart.Head => "head",
        HitboxPart.Torso => "body",
        HitboxPart.Arms => "arm",
        HitboxPart.Legs => "leg",
        HitboxPart.Marker => "marker",
        HitboxPart.Loader => "loader",
        HitboxPart.Tank => "tank",
        _ => part.ToString().ToLowerInvariant(),
    };
}
