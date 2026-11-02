using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Ui;
using Pb.Sim.Collision;
using Pb.Sim.Players;

namespace Pb.Game.Core;

/// <summary>
/// After you're eliminated (spec §1.2): a camera above and behind you, turned to follow whoever got you, with a banner
/// naming them, the part hit and the distance (unless the hit moment says it: Phase 5). Since Phase 5 it can start in
/// your eyes, hold there for the hit, then ease out to that place over a moment, and it follows you as you put your hand
/// up and walk off, keeping whoever got you in view. After a few seconds the banner goes and <c>finished</c> runs. Then,
/// while the round goes on without you (teams, free-for-all), <see cref="Follow"/> trails one of the players still in
/// from behind: Next (Space or a click) moves to the next one, and Skip to summary (Enter) leaves.
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

    // Coming out of your eyes: where the camera started, how long it holds there, how long it eases out, and how far in.
    private Transform3D _from;
    private float _fromFov;
    private float _hold;
    private float _ease;
    private float _since;
    private bool _entering;
    private Vector3 _offset;
    private ICollisionWorld? _startWorld;
    private Action? _leftEyes;

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
    /// <param name="from">Where the camera starts (your eyes) when it eases out (<paramref name="ease"/> &gt; 0) after holding
    /// there for <paramref name="hold"/>, at <paramref name="fromFov"/>; <paramref name="leftEyes"/> runs once it's out of
    /// your head (your body can be drawn). Without it, the camera starts at its place.</param>
    /// <param name="showBanner">Whether to name who got you (the hit moment does, when there's one).</param>
    public void Start(PlayerState victim, PlayerState? shooter, SpectatorDef def, float farClip, ICollisionWorld? world, Action finished,
        Transform3D? from = null, float fromFov = 75f, float hold = 0f, float ease = 0f, Action? leftEyes = null, bool showBanner = true)
    {
        _victim = victim;
        _shooter = shooter;
        _finished = finished;
        _def = def;
        _left = def.Duration_s + hold + ease;
        _startWorld = world;
        _leftEyes = leftEyes;

        Vector3 eye = victim.EyePosition.ToGodot();
        Vector3 towards = shooter is not null ? (Focus() - eye) with { Y = 0f } : Vector3.Forward;
        towards = towards.LengthSquared() > 1e-4f ? towards.Normalized() : Vector3.Forward;
        _camera = new Camera3D { Name = "SpectatorCamera", Fov = 62f, Near = 0.05f, Far = farClip, TopLevel = true };
        AddChild(_camera);
        // Up from their head as far as the ceiling allows, then back from there short of any wall, so it
        // stays inside (in a corridor the full height would be above the ceiling) and still sees past them.
        Vector3 above = Clear(world, eye, eye + Vector3.Up * def.Height_m);
        _camera.GlobalPosition = Clear(world, above, above - towards * def.Back_m);
        _offset = _camera.GlobalPosition - eye;
        _camera.LookAt(Focus(), Vector3.Up);
        if (from is { } start && ease > 0f)
        {
            _entering = true;
            _from = start;
            _fromFov = fromFov;
            _hold = hold;
            _ease = ease;
            _since = 0f;
            _camera.GlobalTransform = start;
            _camera.Fov = fromFov;
        }
        else
        {
            _leftEyes?.Invoke();
            _leftEyes = null;
        }

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
        banner.Visible = showBanner;
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

        // Where it wants to be: above and behind you as you walk off (kept out of the walls), turned to keep whoever got you
        // in view.
        Vector3 eyeNow = _victim.EyePosition.ToGodot();
        Vector3 want = _victim.Present ? Clear(_startWorld, eyeNow, eyeNow + _offset) : _camera.GlobalPosition;
        Transform3D place = new Transform3D(_camera.GlobalTransform.Basis, want).LookingAt(Focus(), Vector3.Up);
        if (_entering)
        {
            _since += dt;
            float t = _since <= _hold ? 0f : Mathf.Clamp((_since - _hold) / _ease, 0f, 1f);
            float s = t * t * t * (t * (t * 6f - 15f) + 10f);
            // Up out of your head first, then back: the height comes in over the first half of the way.
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(s * 2f, 0f, 1f));
            Vector3 along = _from.Origin.Lerp(place.Origin, s);
            along.Y = Mathf.Lerp(_from.Origin.Y, place.Origin.Y, rise);
            _camera.GlobalTransform = new Transform3D(new Basis(_from.Basis.GetRotationQuaternion().Slerp(place.Basis.GetRotationQuaternion(), s)), along);
            _camera.Fov = Mathf.Lerp(_fromFov, 62f, s);
            if (_leftEyes is not null && _camera.GlobalPosition.DistanceTo(_from.Origin) > 0.6f)
            {
                _leftEyes();
                _leftEyes = null;
            }

            _entering = t < 1f;
        }
        else
        {
            _camera.GlobalTransform = _camera.GlobalTransform.InterpolateWith(place, Mathf.Min(1f, dt * 4f));
        }

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

    /// <summary>
    /// What the camera looks at: whoever got you, as before; walking off, a point a third of the way from you to them, so
    /// both are in the picture while they're within 30 m.
    /// </summary>
    private Vector3 Focus()
    {
        Vector3 shooter = (_shooter ?? _victim).Position.ToGodot() + Vector3.Up * 1.3f;
        Vector3 you = _victim.Position.ToGodot() + Vector3.Up * 1.1f;
        return _victim.Present && _shooter is not null && you.DistanceTo(shooter) < 30f ? you.Lerp(shooter, 0.35f) : shooter;
    }

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
