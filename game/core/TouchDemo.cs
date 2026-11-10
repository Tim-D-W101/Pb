using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Pb.Game.Player;
using Pb.Game.Ui;
using Pb.Sim;
using Pb.Sim.Events;
using Pb.Sim.Players;
using Pb.Sim.Vehicles;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --touch-demo</c> (Phase 6; on a level with a buggy, Thornwick Woods): plays a round by simulated fingers, the
/// touches a phone would send, through the touch controls alone. It taps Start on the briefing card, looks round by
/// dragging, walks and sprints on the stick, jumps, crouches, aims, fires (looking as it does), reloads, gets into the
/// buggy, drives it with the stick and GAS, pulls the handbrake, fires from the wheel, brakes, gets out, holds up the
/// scores, and opens and closes the pause menu by its button and by the phone's back button. Each step checks what the
/// sim did with it; the run ends with SMOKE PASS or SMOKE FAIL and the steps that failed. It runs headless too (CI),
/// with <c>--touch</c>'s phone-sized screen (<c>--dpi</c>, <c>--safe-area</c>).
/// </summary>
public partial class TouchDemo : Node, ISimEventListener
{
    private sealed class Step
    {
        public required string Name { get; init; }
        public float Seconds { get; init; }
        public Action? Begin { get; init; }
        public Action<float>? During { get; init; }
        public Action? End { get; init; }
        public Func<string?>? Check { get; init; }

        /// <summary>Ends early once this holds (the step's time is then the most it waits).</summary>
        public Func<bool>? Until { get; init; }
    }

    private SimWorld _sim = null!;
    private PlayerController _player = null!;
    private TouchControls _touch = null!;
    private PauseMenu _pause = null!;
    private CanvasLayer _cards = null!;
    private Func<bool> _scoreboard = null!;
    private readonly List<Step> _steps = new();
    private readonly List<string> _failed = new();
    private readonly List<string> _passed = new();
    private readonly Dictionary<int, Vector2> _down = new();
    private int _at = -1;
    private float _clock;
    private bool _begun;
    private int _shots, _wheelShots;
    private bool _airborne, _handbrakeSeen;
    private float _yawBefore, _fovBefore, _walkSpeed, _speedSeen;
    private Vector2 _fireAt;
    private SVector3 _from;
    private int _vehicle = -1;

    public void Start(SimWorld sim, PlayerController player, TouchControls touch, PauseMenu pause, CanvasLayer cards, Func<bool> scoreboard)
    {
        _sim = sim;
        _player = player;
        _touch = touch;
        _pause = pause;
        _cards = cards;
        _scoreboard = scoreboard;
        // The pause menu stops the tree; the demo carries on to close it.
        ProcessMode = ProcessModeEnum.Always;
        Script();
        GD.Print($"TOUCH DEMO: {_steps.Count} steps on a screen of {TouchUi.Screen.X:0} × {TouchUi.Screen.Y:0} px, " +
                 $"{TouchUi.PxPerMm(GetTreeRoot()):0.0} px a millimetre, safe area {TouchUi.SafeArea(GetTreeRoot())}");
    }

    private Window GetTreeRoot() => ((SceneTree)Engine.GetMainLoop()).Root;

    public void OnSimEvent(in SimEvent e)
    {
        if (e.Type == SimEventType.ShotFired && e.PlayerId == _player.State.Id)
        {
            _shots++;
            if (_player.State.Vehicle >= 0)
            {
                _wheelShots++;
            }
        }
    }

    public override void _Process(double delta)
    {
        if (_at >= _steps.Count)
        {
            return;
        }

        _clock += (float)delta;
        PlayerState you = _player.State;
        _airborne |= !you.Grounded;
        if (_sim.Vehicles.Of(you) is { } ride)
        {
            _handbrakeSeen |= ride.Handbrake;
            _speedSeen = Math.Max(_speedSeen, MathF.Abs(ride.Speed));
        }

        if (_at < 0 || !_begun)
        {
            if (_at < 0)
            {
                _at = 0;
            }

            _clock = 0f;
            _begun = true;
            _steps[_at].Begin?.Invoke();
            return;
        }

        Step step = _steps[_at];
        float share = step.Seconds > 0f ? Math.Clamp(_clock / step.Seconds, 0f, 1f) : 1f;
        step.During?.Invoke(share);
        if (_clock < step.Seconds && step.Until?.Invoke() != true)
        {
            return;
        }

        step.End?.Invoke();
        string? problem = step.Check?.Invoke();
        if (problem is null)
        {
            _passed.Add(step.Name);
            GD.Print($"TOUCH DEMO {step.Name}: ok");
        }
        else
        {
            _failed.Add($"{step.Name} ({problem})");
            GD.Print($"TOUCH DEMO {step.Name}: FAILED, {problem}");
        }

        _at++;
        _clock = 0f;
        if (_at < _steps.Count)
        {
            _steps[_at].Begin?.Invoke();
        }
        else
        {
            Finish();
        }
    }

    private void Finish()
    {
        foreach (int finger in _down.Keys.ToArray())
        {
            Lift(finger);
        }

        bool ok = _failed.Count == 0;
        GD.Print(ok
            ? $"SMOKE PASS touch demo: {_passed.Count} steps by touch ({string.Join(", ", _passed)})"
            : $"SMOKE FAIL touch demo: {string.Join("; ", _failed)}");
        GetTree().Paused = false;
        GetTree().QuitCleanly(ok ? 0 : 1);
    }

    // ---- The script

    private void Script()
    {
        // The briefing card: Start, tapped (a tap is a click on the cards).
        _steps.Add(new Step
        {
            Name = "tap Start on the briefing",
            Seconds = 1.5f,
            Begin = () =>
            {
                if (FindButton(_cards, "Start") is { } start)
                {
                    Tap(9, start.GetGlobalRect().GetCenter());
                }
                else
                {
                    GD.Print("TOUCH DEMO: no Start on the briefing card");
                }
            },
            Until = () => _sim.IsLive && _touch.InControl,
            Check = () => !_sim.IsLive ? "the round didn't start" : !_touch.InControl ? "the touch controls didn't take over" : null,
        });
        _steps.Add(new Step
        {
            Name = "stand on the track",
            Seconds = 0.4f,
            Begin = StandOnTheTrack,
            Check = () => _sim.Vehicles.Count == 0 ? "no vehicle on this level (use --level=thornwick_woods)" : null,
        });
        _steps.Add(new Step
        {
            Name = "buttons clear of each other and the notch",
            Check = () => _touch.Misplaced() is { Length: > 0 } wrong ? wrong : null,
        });

        // Looking: a finger dragged 2 cm right turns you right by the look speed; dragged back, back again.
        float cm = 2f;
        _steps.Add(new Step
        {
            Name = "drag to look",
            Seconds = 0.5f,
            Begin = () =>
            {
                _yawBefore = _player.Yaw;
                Put(1, _touch.LookPoint);
            },
            During = share => DragTo(1, _touch.LookPoint + new Vector2(cm * 10f * Mm * share, 0f)),
            End = () => Lift(1),
            Check = () =>
            {
                float turned = Mathf.RadToDeg(Mathf.AngleDifference(_player.Yaw, _yawBefore));
                float expected = cm * Settings.TouchLookDegPerCm;
                GD.Print($"TOUCH DEMO: a {cm:0} cm drag turned {turned:0.0}° ({expected:0}° asked)");
                return turned > expected * 0.6f && turned < expected * 1.4f ? null : $"turned {turned:0}° for {expected:0}°";
            },
        });
        _steps.Add(new Step
        {
            Name = "drag back",
            Seconds = 0.5f,
            Begin = () => Put(1, _touch.LookPoint),
            During = share => DragTo(1, _touch.LookPoint - new Vector2(cm * 10f * Mm * share, 0f)),
            End = () => Lift(1),
            Check = () => Math.Abs(Mathf.RadToDeg(Mathf.AngleDifference(_player.Yaw, _yawBefore))) < 15f ? null : "didn't turn back",
        });

        // The stick: pushed up, you walk forward; pushed out past its ring, you sprint.
        _steps.Add(new Step
        {
            Name = "walk on the stick",
            Seconds = 1.2f,
            Begin = () =>
            {
                _from = _player.State.Position;
                Put(0, _touch.StickPoint);
            },
            During = share => DragTo(0, _touch.StickPoint + new Vector2(0f, -_touch.StickReach * Math.Min(1f, share * 6f) * 0.95f)),
            End = () =>
            {
                _walkSpeed = _player.State.HorizontalSpeed;
                GD.Print($"TOUCH DEMO: walking at {_walkSpeed:0.0} m/s on the stick");
            },
            Check = () => Flat(_player.State.Position - _from) > 1f && _walkSpeed > 1.5f && !_player.State.Sprinting ? null
                : $"went {Flat(_player.State.Position - _from):0.0} m at {_walkSpeed:0.0} m/s{(_player.State.Sprinting ? ", sprinting" : "")}",
        });
        _steps.Add(new Step
        {
            Name = "sprint past the ring",
            Seconds = 1.2f,
            During = share => DragTo(0, _touch.StickPoint + new Vector2(0f, -_touch.StickReach * 1.7f)),
            End = () =>
            {
                _speedSeen = _player.State.HorizontalSpeed;
                GD.Print($"TOUCH DEMO: sprinting at {_speedSeen:0.0} m/s past the ring");
            },
            Check = () => _player.State.Sprinting && _speedSeen > _walkSpeed + 0.3f ? null : $"sprinting {_player.State.Sprinting}, {_speedSeen:0.0} m/s",
        });
        _steps.Add(new Step { Name = "let go of the stick", Seconds = 0.6f, Begin = () => Lift(0), Check = () => _player.State.HorizontalSpeed < 1f ? null : "still moving" });

        _steps.Add(new Step
        {
            Name = "tap JUMP",
            Seconds = 0.8f,
            Begin = () =>
            {
                _airborne = false;
                TapButton("jump");
            },
            Check = () => _airborne ? null : "never left the ground",
        });
        _steps.Add(new Step
        {
            Name = "tap CROUCH",
            Seconds = 0.5f,
            Begin = () => TapButton("crouch"),
            Check = () => _player.State.Stance == Stance.Crouching ? null : $"stance {_player.State.Stance}",
        });
        _steps.Add(new Step
        {
            Name = "tap CROUCH again",
            Seconds = 0.5f,
            Begin = () => TapButton("crouch"),
            Check = () => _player.State.Stance == Stance.Standing ? null : $"stance {_player.State.Stance}",
        });
        _steps.Add(new Step
        {
            Name = "tap AIM",
            Seconds = 0.5f,
            Begin = () =>
            {
                _fovBefore = _player.Camera.Fov;
                TapButton("aim");
            },
            Check = () => _touch.Aiming && _player.Camera.Fov < _fovBefore * 0.93f ? null : $"view {_player.Camera.Fov:0.0}° from {_fovBefore:0.0}°",
        });
        _steps.Add(new Step
        {
            Name = "tap AIM off",
            Seconds = 0.5f,
            Begin = () => TapButton("aim"),
            Check = () => !_touch.Aiming && _player.Camera.Fov > _fovBefore * 0.98f ? null : $"view {_player.Camera.Fov:0.0}°",
        });
        _steps.Add(new Step
        {
            Name = "hold FIRE and drag",
            Seconds = 0.7f,
            Begin = () =>
            {
                _shots = 0;
                _yawBefore = _player.Yaw;
                _fireAt = _touch.Button("fire")?.At ?? Vector2.Zero;
                PutOn(1, "fire");
            },
            During = share => DragTo(1, _fireAt + new Vector2(4f * Mm * share, 0f)),
            End = () => Lift(1),
            Check = () => _shots < 1 ? "no shots" : Math.Abs(Mathf.AngleDifference(_player.Yaw, _yawBefore)) < 1e-3f ? "dragging on FIRE didn't look" : null,
        });
        _steps.Add(new Step
        {
            Name = "tap RELOAD",
            Seconds = 0.6f,
            Begin = () => TapButton("reload"),
            Until = () => _player.State.Marker.Refill.Active,
            Check = () => _player.State.Marker.Refill.Active ? null : "no refill started",
        });

        // The buggy: USE says GET IN at its door, and in you go.
        _steps.Add(new Step
        {
            Name = "USE says GET IN",
            Seconds = 0.5f,
            Begin = StandAtTheDoor,
            Check = () => _touch.Button("use") is { Label: "GET IN" } ? null : $"USE is {(_touch.Button("use") is { } b ? b.Label : "not up")}",
        });
        _steps.Add(new Step
        {
            Name = "tap GET IN",
            Seconds = 1.5f,
            Begin = () => TapButton("use"),
            Until = () => Driving,
            Check = () => Driving ? null : $"not at the wheel (vehicle {_player.State.Vehicle})",
        });
        _steps.Add(new Step
        {
            Name = "the wheel's buttons clear too",
            Check = () => _touch.Misplaced() is { Length: > 0 } wrong ? wrong : null,
        });
        _steps.Add(new Step
        {
            Name = "GAS and steer",
            Seconds = 2.5f,
            Begin = () =>
            {
                _yawBefore = Ride?.Yaw ?? 0f;
                _speedSeen = 0f;
                PutOn(1, "throttle");
                Put(0, _touch.StickPoint);
            },
            During = share => DragTo(0, _touch.StickPoint + new Vector2(_touch.StickReach * 0.7f * Math.Min(1f, share * 4f), 0f)),
            Check = () =>
            {
                float turned = Math.Abs(Mathf.RadToDeg(Mathf.AngleDifference(Ride?.Yaw ?? 0f, _yawBefore)));
                GD.Print($"TOUCH DEMO: driven up to {_speedSeen * 3.6f:0} km/h, turned {turned:0}°");
                return _speedSeen > 3f && turned > 8f ? null : $"{_speedSeen:0.0} m/s, turned {turned:0}°";
            },
        });
        _steps.Add(new Step
        {
            Name = "HANDBRAKE",
            Seconds = 0.5f,
            Begin = () =>
            {
                _handbrakeSeen = false;
                PutOn(2, "handbrake");
            },
            End = () => Lift(2),
            Check = () => _handbrakeSeen ? null : "the handbrake never went on",
        });
        _steps.Add(new Step
        {
            Name = "FIRE from the wheel",
            Seconds = 0.6f,
            Begin = () =>
            {
                _wheelShots = 0;
                PutOn(2, "fire");
            },
            End = () => Lift(2),
            Check = () => _wheelShots > 0 ? null : "no shots from the wheel",
        });
        _steps.Add(new Step
        {
            Name = "BRAKE",
            Seconds = 6f,
            Begin = () =>
            {
                Lift(0);
                Lift(1);
                PutOn(1, "brake");
            },
            Until = () => Ride is { } ride && MathF.Abs(ride.Speed) <= ride.Params.ExitBelow * 0.5f,
            End = () => Lift(1),
            Check = () => Ride is { } ride && MathF.Abs(ride.Speed) <= ride.Params.ExitBelow ? null : $"still at {Ride?.Speed:0.0} m/s",
        });
        _steps.Add(new Step
        {
            Name = "tap GET OUT",
            Seconds = 1.5f,
            Begin = () => TapButton("use"),
            Until = () => _player.State.Vehicle < 0,
            Check = () => _player.State.Vehicle < 0 ? null : "still in the buggy",
        });

        // The top row and the phone's back button.
        _steps.Add(new Step
        {
            Name = "hold SCORES",
            Seconds = 0.4f,
            Begin = () => PutOn(1, "scores"),
            End = () => Lift(1),
            Check = () => _scoreboard() ? null : "no scoreboard",
        });
        _steps.Add(new Step
        {
            Name = "scores go",
            Seconds = 0.3f,
            Check = () => !_scoreboard() ? null : "the scoreboard stayed up",
        });
        _steps.Add(new Step
        {
            Name = "tap pause",
            Seconds = 0.4f,
            Begin = () => TapButton("pause"),
            Check = () => _pause.Open && _touch.Suspended && Input.EmulateMouseFromTouch ? null : $"pause open {_pause.Open}, taps as clicks {Input.EmulateMouseFromTouch}",
        });
        _steps.Add(new Step
        {
            Name = "back button closes it",
            Seconds = 0.4f,
            Begin = Back,
            Check = () => !_pause.Open && !GetTree().Paused ? null : "the pause menu stayed",
        });
        _steps.Add(new Step
        {
            Name = "back button opens it",
            Seconds = 0.4f,
            Begin = Back,
            Check = () => _pause.Open ? null : "no pause menu",
        });
        _steps.Add(new Step
        {
            Name = "tap Resume",
            Seconds = 0.5f,
            Begin = () =>
            {
                if (FindButton(_pause, "Resume") is { } resume)
                {
                    Tap(9, resume.GetGlobalRect().GetCenter());
                }
            },
            Check = () => !_pause.Open && _touch.InControl ? null : "still paused",
        });
        _steps.Add(new Step
        {
            Name = "left-handed swaps the sides",
            Seconds = 0.1f,
            Begin = () => Settings.TouchLeftHanded = true,
            End = () =>
            {
                _leftFire = _touch.Button("fire")?.At.X ?? -1f;
                _leftStick = _touch.StickPoint.X;
                Settings.TouchLeftHanded = false;
            },
            Check = () => _leftFire >= 0f && _leftFire < TouchUi.Screen.X * 0.5f && _leftStick > TouchUi.Screen.X * 0.5f ? null
                : $"FIRE at {_leftFire:0}, the stick at {_leftStick:0} of {TouchUi.Screen.X:0}",
        });
    }

    private float _leftFire, _leftStick;

    private GameSettings Settings => _touch.Settings;

    private float Mm => TouchUi.PxPerMm(GetTreeRoot());

    private VehicleState? Ride => _sim.Vehicles.Of(_player.State);

    private bool Driving => Ride is { } ride && ride.Driver == _player.State.Id;

    /// <summary>A few metres ahead of the buggy along its heading, facing that way: open track to walk on.</summary>
    private void StandOnTheTrack()
    {
        if (_sim.Vehicles.Count == 0)
        {
            return;
        }

        _vehicle = Nearest();
        VehicleState v = _sim.Vehicles[_vehicle];
        var ahead = new SVector3(-MathF.Sin(v.Yaw), 0f, -MathF.Cos(v.Yaw));
        SVector3 at = v.Position + ahead * 7f;
        at.Y = (_sim.Level?.GroundAt(at.X, at.Z) ?? at.Y) + 0.02f;
        _player.Teleport(at, v.Yaw);
    }

    /// <summary>At the buggy's driver's door, facing it.</summary>
    private void StandAtTheDoor()
    {
        if (_vehicle < 0)
        {
            return;
        }

        VehicleState v = _sim.Vehicles[_vehicle];
        SVector3 door = VehicleSet.Door(v, v.Params.DriverSeat);
        _player.Teleport(door, MathF.Atan2(-(v.Position.X - door.X), -(v.Position.Z - door.Z)));
    }

    private int Nearest()
    {
        int best = 0;
        for (int i = 1; i < _sim.Vehicles.Count; i++)
        {
            if (Flat(_sim.Vehicles[i].Position - _player.State.Position) < Flat(_sim.Vehicles[best].Position - _player.State.Position))
            {
                best = i;
            }
        }

        return best;
    }

    private static float Flat(SVector3 d) => MathF.Sqrt(d.X * d.X + d.Z * d.Z);

    // ---- Fingers: touches as a phone sends them, in the window's pixels (the interface's, through its stretch)

    private void TapButton(string id)
    {
        if (_touch.Button(id) is { } button)
        {
            Tap(8, button.At);
        }
        else
        {
            GD.Print($"TOUCH DEMO: no {id} button up to tap");
        }
    }

    private void PutOn(int finger, string id)
    {
        if (_touch.Button(id) is { } button)
        {
            Put(finger, button.At);
        }
        else
        {
            GD.Print($"TOUCH DEMO: no {id} button up to hold");
        }
    }

    private void Tap(int finger, Vector2 at)
    {
        Put(finger, at);
        // Lifted a frame later, as a quick tap is (a press and release in one frame would never be held for a tick).
        GetTree().CreateTimer(0.08, processAlways: true).Timeout += () => Lift(finger);
    }

    private void Put(int finger, Vector2 at)
    {
        _down[finger] = at;
        Input.ParseInputEvent(new InputEventScreenTouch { Index = finger, Position = ToWindow(at), Pressed = true });
    }

    private void DragTo(int finger, Vector2 at)
    {
        if (!_down.TryGetValue(finger, out Vector2 was) || was == at)
        {
            return;
        }

        _down[finger] = at;
        Transform2D t = GetTreeRoot().GetFinalTransform();
        Input.ParseInputEvent(new InputEventScreenDrag { Index = finger, Position = t * at, Relative = t.BasisXform(at - was), ScreenRelative = t.BasisXform(at - was) });
    }

    private void Lift(int finger)
    {
        if (_down.Remove(finger, out Vector2 at))
        {
            Input.ParseInputEvent(new InputEventScreenTouch { Index = finger, Position = ToWindow(at), Pressed = false });
        }
    }

    private Vector2 ToWindow(Vector2 at) => GetTreeRoot().GetFinalTransform() * at;

    /// <summary>The phone's back button: Godot's go-back request, as Android sends it.</summary>
    private void Back() => GetTreeRoot().PropagateNotification((int)NotificationWMGoBackRequest);

    private static Button? FindButton(Node under, string text) =>
        under.FindChildren("*", nameof(Godot.Button), owned: false).OfType<Button>().FirstOrDefault(b => b.Text == text && b.IsVisibleInTree());
}
