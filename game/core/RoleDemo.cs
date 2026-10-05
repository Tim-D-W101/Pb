using System.Collections.Generic;
using System.Linq;
using Godot;
using Pb.Game.Ai;
using Pb.Game.Player;
using Pb.Game.Ui;
using Pb.Sim;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Players;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --role-demo=marksman</c> or <c>=flanker</c>: one of Phase 3's roles at work, seen with the F3 overlay (sight
/// cones, meters, paths), for screenshots with <c>--write-movie</c>. Hitboxes are off, so nobody goes out. It runs on
/// the sim's clock, so it plays out the same at any frame rate: a low <c>--fixed-fps</c> captures it in fewer frames.
/// It ends with a SMOKE line and exit code for CI: the Marksman has to spot you and open up, the Flanker has to go
/// round on the call and look out from its spot.
/// <list type="bullet">
/// <item><b>Marksman</b>: it goes up to its vantage while the camera follows it; then you step out into the open far
/// along the view it watches, and the camera looks over its shoulder at you as it settles its aim and takes its shots.</item>
/// <item><b>Flanker</b>: you stand where one of its teammates sees you and calls it, somewhere it has a way round to
/// your side. The camera looks over the caller's shoulder at you, follows the Flanker round, and looks over its
/// shoulder from the spot it reaches.</item>
/// </list>
/// </summary>
public partial class RoleDemo : Node, ICommandSource
{
    private const float StepOutAt = 5f;
    private const float MarksmanLength = 16f;
    private const float FlankerLength = 20f;
    /// <summary>How long the camera stays on the caller once the Flanker is on its way (s).</summary>
    private const float CallShot = 1.5f;
    private const float LogEvery = 2f;

    /// <summary>Where you might stand in front of the Flanker's teammate, tried in turn: metres from it, and radians off its facing.</summary>
    private static readonly float[] Distances = { 12f, 16f, 10f, 20f, 8f, 14f, 18f, 24f, 28f, 32f };
    private static readonly float[] Turns = { 0f, 0.3f, -0.3f, 0.6f, -0.6f, 0.9f, -0.9f };

    private SimWorld _sim = null!;
    private BotSquad _squad = null!;
    private PlayerController _player = null!;
    private IReadOnlyList<OpponentPawn> _opponents = null!;
    private Camera3D _camera = null!;
    private BotDebugOverlay _overlay = null!;
    private string _role = "";
    private OpponentPawn? _subject;
    private OpponentPawn? _caller;
    private SVector3? _flankTo;
    private SVector3? _eye;
    private SVector3 _look;
    private int _following = -1;
    private SVector3? _viewFrom;
    private int _startTick = -1;
    private int _lastTick;
    private bool _steppedOut;
    private float _flankingFrom = -1f;
    private float _lookingOutFrom = -1f;
    private float _engagedAt = -1f;
    private int _logged = -1;
    private string _lastLabel = "";
    private int _lastSubjectCall = -1;
    private int _lastCallerCall = -1;

    public void Start(string role, SimWorld sim, BotSquad squad, PlayerController player, IReadOnlyList<OpponentPawn> opponents, BotDebugOverlay overlay, Hud hud, float farClip)
    {
        _role = role;
        _sim = sim;
        _squad = squad;
        _player = player;
        _opponents = opponents;
        sim.PlayerHits.Enabled = false;
        player.AutoPilot = this;
        if (role == "flanker")
        {
            PlaceForFlank(squad);
        }
        else
        {
            _subject = opponents.FirstOrDefault(o => RoleOf(o) == role && o.State.Team != player.State.Team);
        }

        if (_subject is null)
        {
            GD.PushError(role == "flanker"
                ? "ROLE DEMO: no flanker here with a teammate who could call you out and a way round to your side (try another --level or --mode)"
                : $"ROLE DEMO: no {role} on this level in this round (try another --level or --mode)");
            GetTree().Quit(1);
            return;
        }

        // A low --fixed-fps asks for more sim ticks a frame than Godot's usual cap; the demo's clock keeps up anyway.
        Engine.MaxPhysicsStepsPerFrame = 64;
        _camera = new Camera3D { Name = "RoleCamera", Fov = 62f, Far = farClip, Near = 0.05f };
        AddChild(_camera);
        _camera.MakeCurrent();
        _overlay = overlay;
        overlay.Visible = true;
        hud.Visible = false;
        player.ViewModel.Visible = false;
        if (player.Body is { } body)
        {
            body.ShadowOnly = false; // you're seen from outside
        }

        _startTick = sim.Tick;
        _lastTick = sim.Tick;
        GD.Print($"ROLE DEMO {role}: {_subject.State.Name} at {_subject.State.Position}" +
                 (_caller is null ? "" : $", called by {_caller.State.Name} ({RoleOf(_caller)}) at {_caller.State.Position}") +
                 (_flankTo is { } to ? $", with a way round to {to}" : ""));
    }

    /// <summary>
    /// Stands you where a teammate of one of the other side's Flankers sees you (in front of it, within earshot of the
    /// Flanker, out of the Flanker's own sight), somewhere the Flanker has a spot to work round to wherever about you
    /// the call puts you.
    /// </summary>
    private void PlaceForFlank(BotSquad squad)
    {
        float range = squad.Config.Brain.CalloutRange;
        float muffled = range * squad.Config.Senses.WallMuffle;
        float error = squad.Config.Brain.ContactError;
        foreach (OpponentPawn flanker in _opponents.Where(o => RoleOf(o) == "flanker" && o.State.Team != _player.State.Team))
        {
            BotBrain brain = ((BotPilot)flanker.Pilot).Brain;
            IEnumerable<OpponentPawn> callers = _opponents
                .Where(o => o != flanker && o.State.Team == flanker.State.Team && RoleOf(o) is not ("flanker" or null))
                .Where(o => Hears(o.State.EyePosition, flanker.State.EyePosition, range, muffled))
                .OrderBy(o => SVector3.Distance(o.State.Position, flanker.State.Position));
            foreach (OpponentPawn caller in callers)
            {
                foreach (float distance in Distances)
                {
                    foreach (float turn in Turns)
                    {
                        SVector3 spot = caller.State.Position + Pb.Sim.Core.ViewAngles.FlatForward(caller.State.Yaw + turn) * distance;
                        if (!ScenePositions.IsSpot(_sim, caller.State, spot) ||
                            !_sim.Collision.SweepSphere(flanker.State.EyePosition, spot + new SVector3(0f, 1.2f, 0f), 0f, out _) ||
                            WayRound(brain, spot, caller.State.Position, error) is not { } cover)
                        {
                            continue;
                        }

                        _subject = flanker;
                        _caller = caller;
                        _flankTo = squad.Cover.Points[cover].Position;
                        _player.Teleport(spot, ScenePositions.Facing(spot, caller.State.Position));
                        GD.Print($"ROLE DEMO: you stand {distance:0} m from {caller.State.Name}, at {spot}");
                        return;
                    }
                }
            }
        }
    }

    /// <summary>
    /// The Flanker's spot for a call about you at <paramref name="spot"/>, if it has one there and wherever else within
    /// the call's error the call might put you (all but one of the corners and edges of that square), else null.
    /// </summary>
    private static int? WayRound(BotBrain brain, SVector3 spot, SVector3 caller, float error)
    {
        int cover = brain.FlankSpot(spot, caller);
        if (cover < 0)
        {
            return null;
        }

        int misses = 0;
        for (int i = 0; i < 8; i++)
        {
            float angle = i * System.MathF.PI * 0.25f;
            float reach = i % 2 == 0 ? error : error * System.MathF.Sqrt(2f);
            SVector3 off = new(System.MathF.Cos(angle) * reach, 0f, System.MathF.Sin(angle) * reach);
            if (brain.FlankSpot(spot + off, caller) < 0 && ++misses > 1)
            {
                return null;
            }
        }

        return cover;
    }

    private bool Hears(SVector3 from, SVector3 ear, float range, float muffled)
    {
        float d = SVector3.Distance(from, ear);
        return d <= muffled || (d <= range && !_sim.Collision.SweepSphere(from, ear, 0f, out _));
    }

    private static string? RoleOf(OpponentPawn o) => (o.Pilot as BotPilot)?.Brain.Archetype.Id;

    public override void _Process(double delta)
    {
        if (_startTick < 0 || _subject is null)
        {
            return;
        }

        float t = (_sim.Tick - _startTick) * _sim.Dt;
        float dt = (_sim.Tick - _lastTick) * _sim.Dt;
        _lastTick = _sim.Tick;
        BotBrain brain = ((BotPilot)_subject.Pilot).Brain;
        if (_role == "marksman")
        {
            if (!_steppedOut && t >= StepOutAt)
            {
                // Out into the open, far along the view the Marksman watches from its vantage.
                _steppedOut = true;
                float yaw = brain.CoverIndex >= 0 ? _squad.Vantage.FacingYaw(brain.CoverIndex) : _subject.State.Yaw;
                if (OpenGround(_subject.State, yaw, out SVector3 spot))
                {
                    _player.Teleport(spot, ScenePositions.Facing(spot, _subject.State.Position));
                    GD.Print($"ROLE DEMO {t:0.0} s: you step out {SVector3.Distance(spot, _subject.State.Position):0} m from the marksman, at {spot}");
                }
                else
                {
                    GD.PushError("ROLE DEMO: no open ground in the marksman's view");
                }
            }

            if (_steppedOut && brain.Mode == BotMode.Engage && _engagedAt < 0f)
            {
                _engagedAt = t;
            }

            // From behind it, looking where it looks as it settles in; then at you, far off past it, once you're out.
            PlayerState m = _subject.State;
            Watch(m, _steppedOut ? _player.State.EyePosition : m.EyePosition + Pb.Sim.Core.ViewAngles.FlatForward(m.Yaw) * 20f);
        }
        else
        {
            if (brain.Mode == BotMode.Flank && _flankingFrom < 0f)
            {
                _flankingFrom = t;
                if (brain.CoverIndex >= 0)
                {
                    GD.Print($"ROLE DEMO {t:0.0} s: {_subject.State.Name} makes for {_squad.Cover.Points[brain.CoverIndex].Position}");
                }
            }

            if (brain is { Mode: BotMode.Flank, Phase: CoverPhase.Peeking } && _lookingOutFrom < 0f)
            {
                _lookingOutFrom = t;
            }

            PlayerState f = _subject.State;
            if (_flankingFrom < 0f || t < _flankingFrom + CallShot)
            {
                // The call: over the caller's shoulder, at you.
                Behind(_caller!.State, _player.State.EyePosition, behind: 2.2f, above: 0.7f);
            }
            else if (_lookingOutFrom < 0f)
            {
                // The way round: following the Flanker.
                Behind(f, f.EyePosition + Pb.Sim.Core.ViewAngles.FlatForward(f.Yaw) * 12f, behind: 3.2f, above: 1.3f, ease: 0.25f, dt: dt);
            }
            else
            {
                // At its spot: over its shoulder, towards you.
                Behind(f, _player.State.EyePosition, behind: 2.2f, above: 0.7f, ease: 0.25f, dt: dt);
            }
        }

        if ((int)(t / LogEvery) != _logged || brain.Label != _lastLabel)
        {
            string caller = _caller is null ? "" : $"; {_caller.State.Name} {((BotPilot)_caller.Pilot).Brain.Label}";
            GD.Print($"ROLE DEMO {t:0.0} s: {_subject.State.Name} ({_role}) {brain.Label} at {_subject.State.Position}{caller}");
            _logged = (int)(t / LogEvery);
            _lastLabel = brain.Label;
        }

        SayCalls(_subject, t, ref _lastSubjectCall);
        if (_caller is not null)
        {
            SayCalls(_caller, t, ref _lastCallerCall);
        }

        if (t >= (_role == "flanker" ? FlankerLength : MarksmanLength))
        {
            _startTick = -1;
            bool flanker = _role == "flanker";
            bool ok = flanker ? _flankingFrom >= 0f && _lookingOutFrom >= 0f : _engagedAt >= 0f;
            GD.Print($"SMOKE {(ok ? "PASS" : "FAIL")}: role demo, {_role} {_subject.State.Name}: " + (flanker
                ? $"called by {_caller!.State.Name}, went round from {Seconds(_flankingFrom)} and looked out from its spot from {Seconds(_lookingOutFrom)}"
                : $"you stepped out at {StepOutAt:0.0} s and it opened up at {Seconds(_engagedAt)}"));
            GetTree().Quit(ok ? 0 : 1);
        }
    }

    /// <summary>
    /// Somewhere to stand on the ground (or a platform) far along <paramref name="yaw"/> from <paramref name="m"/>, with
    /// room to stand and in plain view of its eyes.
    /// </summary>
    private bool OpenGround(PlayerState m, float yaw, out SVector3 spot)
    {
        foreach (float distance in new[] { 40f, 32f, 48f, 24f })
        {
            foreach (float turn in new[] { 0f, 0.15f, -0.15f, 0.3f, -0.3f, 0.5f, -0.5f })
            {
                SVector3 flat = m.Position + Pb.Sim.Core.ViewAngles.FlatForward(yaw + turn) * distance;
                if (!_sim.Collision.SweepSphere(flat with { Y = m.Position.Y + 8f }, flat with { Y = -2f }, 0f, out SweepHit floor) ||
                    floor.Point.Y > 1.5f)
                {
                    continue; // nothing underfoot, or the first thing down is a roof
                }

                SVector3 at = floor.Point;
                bool room = !_sim.Collision.SweepSphere(at + new SVector3(0f, 0.45f, 0f), at + new SVector3(0f, 1.6f, 0f), 0.34f, out _);
                bool seen = !_sim.Collision.SweepSphere(m.EyePosition, at + new SVector3(0f, 1.2f, 0f), 0f, out _);
                bool inside = _sim.Level?.Bounds.Contains(at + new SVector3(0f, 1.2f, 0f)) ?? true;
                if (room && seen && inside)
                {
                    spot = at;
                    return true;
                }
            }
        }

        spot = default;
        return false;
    }

    private static string Seconds(float t) => t >= 0f ? $"{t:0.0} s" : "never";

    /// <summary>Your script: stand still, facing whoever you're here to be seen by.</summary>
    public InputCommand Next(int tick, PlayerState me)
    {
        PlayerState? face = (_caller ?? _subject)?.State;
        if (face is null)
        {
            return new InputCommand { Tick = tick, Yaw = me.Yaw };
        }

        (float yaw, float pitch) = BotAim.Solve(_sim.Config, me.EyePosition, face.EyePosition, SVector3.Zero);
        return new InputCommand { Tick = tick, Yaw = yaw, Pitch = pitch };
    }

    /// <summary>Logs each callout <paramref name="bot"/> makes.</summary>
    private static void SayCalls(OpponentPawn bot, float t, ref int last)
    {
        BotBrain brain = ((BotPilot)bot.Pilot).Brain;
        if (brain.CalloutTick >= 0 && brain.CalloutTick != last)
        {
            last = brain.CalloutTick;
            GD.Print($"ROLE DEMO {t:0.0} s: {bot.State.Name} calls {brain.Callout}");
        }
    }

    /// <summary>
    /// A camera that stays put behind <paramref name="who"/> (as seen from <paramref name="look"/>) for as long as it can
    /// see both, looking at <paramref name="look"/>; when it can't, it moves to the first spot that can, a few metres
    /// back (out through the glazing of a signal box, say), and failing that it looks over their shoulder.
    /// </summary>
    private void Watch(PlayerState who, SVector3 look)
    {
        if (_viewFrom is not { } from || !Sees(from, who, look))
        {
            _viewFrom = FindView(who, look);
            if (_viewFrom is null)
            {
                Behind(who, look, behind: 2.2f, above: 0.7f);
                return;
            }
        }

        _overlay.Unlabelled = -1; // far enough back to read its label
        _camera.GlobalPosition = _viewFrom.Value.ToGodot();
        _camera.LookAt(look.ToGodot(), Vector3.Up);
    }

    private SVector3? FindView(PlayerState who, SVector3 look)
    {
        SVector3 away = (who.Position - look) with { Y = 0f };
        away = away.LengthSquared() > 1e-4f ? SVector3.Normalize(away) : -Pb.Sim.Core.ViewAngles.FlatForward(who.Yaw);
        foreach (float distance in new[] { 6f, 8f, 4.5f, 11f })
        {
            foreach (float turn in new[] { 0f, 0.35f, -0.35f, 0.7f, -0.7f })
            {
                foreach (float above in new[] { 1f, 0.4f, 2.2f })
                {
                    float c = System.MathF.Cos(turn), sn = System.MathF.Sin(turn);
                    SVector3 dir = new(away.X * c - away.Z * sn, 0f, away.X * sn + away.Z * c);
                    SVector3 at = who.EyePosition + dir * distance + new SVector3(0f, above, 0f);
                    bool free = !_sim.Collision.SweepSphere(at, at + new SVector3(0f, 0.05f, 0f), 0.3f, out _);
                    if (free && Sees(at, who, look))
                    {
                        return at;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Whether a camera at <paramref name="from"/> has a clear line to <paramref name="who"/>'s head and to <paramref name="look"/>.</summary>
    private bool Sees(SVector3 from, PlayerState who, SVector3 look) =>
        !_sim.Collision.SweepSphere(from, who.EyePosition, 0f, out _) && !_sim.Collision.SweepSphere(from, look, 0f, out _);

    /// <summary>
    /// The camera behind and above <paramref name="who"/> and a little to one side, looking at <paramref name="look"/>,
    /// pulled in short of anything in the way. With an <paramref name="ease"/> time it glides there (about two thirds
    /// of the way in that many seconds) instead of cutting; it always cuts to someone new.
    /// </summary>
    private void Behind(PlayerState who, SVector3 look, float behind, float above, float ease = 0f, float dt = 0f)
    {
        _overlay.Unlabelled = who.Id; // its label would fill the view
        SVector3 eye = who.EyePosition;
        SVector3 toward = (look - eye) with { Y = 0f };
        SVector3 forward = toward.LengthSquared() > 1e-4f ? SVector3.Normalize(toward) : Pb.Sim.Core.ViewAngles.FlatForward(who.Yaw);
        SVector3 side = new(forward.Z, 0f, -forward.X);
        SVector3 want = eye - forward * behind + side * 0.4f + new SVector3(0f, above, 0f);
        if (_eye is { } was && ease > 0f && _following == who.Id)
        {
            float k = 1f - System.MathF.Exp(-dt / ease);
            _eye = SVector3.Lerp(was, want, k);
            _look = SVector3.Lerp(_look, look, k);
        }
        else
        {
            _eye = want;
            _look = look;
        }

        _following = who.Id;
        _camera.GlobalPosition = Clear(eye, _eye.Value).ToGodot();
        _camera.LookAt(_look.ToGodot(), Vector3.Up);
    }

    /// <summary>
    /// <paramref name="want"/>, pulled in short of anything between it and <paramref name="eye"/> (after any easing, so
    /// a glide never cuts through a corner). Under a low ceiling it comes down first, as far as just over the eyes,
    /// rather than in, so a camera behind someone in a small room stays behind them.
    /// </summary>
    private SVector3 Clear(SVector3 eye, SVector3 want)
    {
        SVector3 best = eye;
        float bestReach = -1f;
        foreach (float drop in new[] { 0f, 0.5f, 1f })
        {
            SVector3 at = SVector3.Lerp(want, want with { Y = eye.Y + 0.15f }, drop);
            if (!_sim.Collision.SweepSphere(eye, at, 0.35f, out SweepHit hit))
            {
                return at;
            }

            float reach = SVector3.Distance(eye, hit.Point);
            if (reach > bestReach)
            {
                bestReach = reach;
                best = hit.Point;
            }
        }

        return best;
    }
}
