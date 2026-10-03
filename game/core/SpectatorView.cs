using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Ui;
using Pb.Sim.Collision;
using Pb.Sim.Players;

namespace Pb.Game.Core;

/// <summary>
/// After you're eliminated (spec §1.2): a camera above and behind where you stood, turned to follow
/// whoever got you, with a banner naming them, the part hit and the distance. After a few seconds the
/// banner goes and <c>finished</c> runs. Then, while the round goes on without you (teams, free-for-all),
/// <see cref="Follow"/> trails one of the players still in from behind: Next (Space or a click) moves to
/// the next one, and Skip to summary (Enter) leaves.
/// </summary>
public partial class SpectatorView : Node3D
{
    private Camera3D? _camera;
    private PlayerState _victim = null!;
    private PlayerState? _shooter;
    private Action _finished = null!;
    private CanvasLayer _layer = null!;
    private float _left;
    private bool _holding;
    private SpectatorDef _def = null!;

    // Following someone.
    private const float CameraRadius = 0.25f;
    private Func<IReadOnlyList<PlayerState>>? _watchable;
    private Action? _skip;
    private PlayerState? _watching;
    private Label? _watchLabel;
    private Func<int, Color> _colourOf = _ => Colors.White;
    private ICollisionWorld? _world;
    private bool _snap;

    /// <summary>True while trailing a player who's still in.</summary>
    public bool Following => _watchable is not null;

    /// <summary>
    /// Shows who got <paramref name="victim"/> from above and behind where they stood, kept out of
    /// <paramref name="world"/>'s walls and ceilings, then runs <paramref name="finished"/>.
    /// </summary>
    public void Start(PlayerState victim, PlayerState? shooter, SpectatorDef def, float farClip, ICollisionWorld? world, Action finished)
    {
        _victim = victim;
        _shooter = shooter;
        _finished = finished;
        _def = def;
        _left = def.Duration_s;

        Vector3 eye = victim.EyePosition.ToGodot();
        Vector3 towards = shooter is not null ? (Focus() - eye) with { Y = 0f } : Vector3.Forward;
        towards = towards.LengthSquared() > 1e-4f ? towards.Normalized() : Vector3.Forward;
        _camera = new Camera3D { Name = "SpectatorCamera", Fov = 62f, Near = 0.05f, Far = farClip, TopLevel = true };
        AddChild(_camera);
        // Up from their head as far as the ceiling allows, then back from there short of any wall, so it
        // stays inside (in a corridor the full height would be above the ceiling) and still sees past them.
        Vector3 above = Clear(world, eye, eye + Vector3.Up * def.Height_m);
        _camera.GlobalPosition = Clear(world, above, above - towards * def.Back_m);
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

    /// <summary>
    /// Trails one of <paramref name="watchable"/> (the players still in that you may watch), coloured by
    /// <paramref name="colourOf"/> (a team's colour), until <paramref name="skip"/> is chosen. The camera
    /// keeps out of <paramref name="world"/>'s walls, like a spring arm.
    /// </summary>
    public void Follow(Func<IReadOnlyList<PlayerState>> watchable, Func<int, Color> colourOf, Action skip, ICollisionWorld world)
    {
        _watchable = watchable;
        _colourOf = colourOf;
        _skip = skip;
        _world = world;
        _layer.Visible = true;
        foreach (Node child in _layer.GetChildren())
        {
            child.QueueFree();
        }

        var bar = new HBoxContainer { Theme = UiKit.Theme, Alignment = BoxContainer.AlignmentMode.Center };
        bar.AddThemeConstantOverride("separation", 16);
        bar.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        bar.AnchorLeft = 0f;
        bar.AnchorRight = 1f;
        bar.OffsetTop = -96f;
        bar.OffsetBottom = -40f;
        _watchLabel = UiKit.Body("", 26);
        _watchLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _watchLabel.AddThemeConstantOverride("outline_size", 8);
        bar.AddChild(_watchLabel);
        bar.AddChild(UiKit.Button("Next player (Space)", Next, 240));
        bar.AddChild(UiKit.Button("Skip to summary (Enter)", () => _skip?.Invoke(), 280));
        _layer.AddChild(bar);
        Next();
    }

    /// <summary>The next player still in after the one being watched.</summary>
    private void Next()
    {
        IReadOnlyList<PlayerState> players = _watchable?.Invoke() ?? Array.Empty<PlayerState>();
        if (players.Count == 0)
        {
            _watching = null;
            return;
        }

        int at = _watching is null ? -1 : IndexOf(players, _watching);
        _watching = players[(at + 1) % players.Count];
        _snap = true; // straight there, not gliding through the level
        if (_watchLabel is not null)
        {
            _watchLabel.Text = $"Watching {_watching.Name}";
            _watchLabel.AddThemeColorOverride("font_color", _colourOf(_watching.Team));
        }
    }

    /// <summary>As far from <paramref name="from"/> towards <paramref name="to"/> as the camera fits.</summary>
    private static Vector3 Clear(ICollisionWorld? world, Vector3 from, Vector3 to) =>
        world is not null && world.SweepSphere(from.ToSim(), to.ToSim(), CameraRadius, out SweepHit hit) ? hit.Point.ToGodot() : to;

    private static int IndexOf(IReadOnlyList<PlayerState> players, PlayerState p)
    {
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i] == p)
            {
                return i;
            }
        }

        return -1;
    }

    public override void _Process(double delta)
    {
        if (_camera is null || _holding)
        {
            return;
        }

        float dt = (float)delta;
        if (Following)
        {
            if (_watching is not { Alive: true })
            {
                Next();
            }

            if (_watching is not null)
            {
                // Behind and a little above their eyes, looking where they look, pulled in front of any wall
                // between their head and the camera.
                Vector3 eye = _watching.EyePosition.ToGodot();
                Vector3 forward = Pb.Sim.Core.ViewAngles.FlatForward(_watching.Yaw).ToGodot();
                Vector3 from = eye - forward * _def.FollowBack_m + Vector3.Up * _def.FollowHeight_m;
                bool blocked = false;
                if (_world is not null && _world.SweepSphere(eye.ToSim(), from.ToSim(), CameraRadius, out SweepHit hit))
                {
                    from = hit.Point.ToGodot();
                    blocked = true;
                }

                Transform3D goal = new Transform3D(Basis.Identity, from).LookingAt(eye + forward * 4f, Vector3.Up);
                _camera.GlobalTransform = _snap || blocked ? goal : _camera.GlobalTransform.InterpolateWith(goal, Mathf.Min(1f, dt * 5f));
                _snap = false;
            }

            return;
        }

        // Keep the shooter in view as they move.
        Transform3D target = _camera.GlobalTransform.LookingAt(Focus(), Vector3.Up);
        _camera.GlobalTransform = _camera.GlobalTransform.InterpolateWith(target, Mathf.Min(1f, dt * 4f));
        if (_left <= 0f)
        {
            return;
        }

        _left -= dt;
        if (_left <= 0f)
        {
            _layer.Visible = false;
            _finished();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Following)
        {
            return;
        }

        if (e is InputEventKey { Pressed: true, Echo: false } key && key.Keycode is Key.Enter or Key.KpEnter)
        {
            GetViewport().SetInputAsHandled();
            _skip?.Invoke();
        }
        else if (e.IsActionPressed("jump") || e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            GetViewport().SetInputAsHandled();
            Next();
        }
    }

    /// <summary>Stops following (the summary is up): the camera holds where it is.</summary>
    public void StopFollowing()
    {
        _watchable = null;
        _holding = true;
        _layer.Visible = false;
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
