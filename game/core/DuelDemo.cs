using System.Collections.Generic;
using System.Globalization;
using Godot;
using Pb.Game.Player;
using Pb.Game.Ui;
using Pb.Sim;
using Pb.Sim.Core;
using Pb.Sim.Players;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --duel-demo</c>: a short scripted scene for checking eliminations by eye (or with
/// <c>--write-movie</c>). You stand in front of a passive bot and shoot them: they call "Hit!",
/// raise their marker, wear the splat and walk off. Then paint comes at you from the other opponents: a ball whizzing
/// past your head, one breaking close by, and one breaking on your mask: paint sprays your goggles and the spectator
/// view shows who got you. Each shows an incoming-fire arc pointing back the way it came; drawn (not headless), a
/// moment after each the demo looks at the screen where the HUD says that arc is, to see it's really there (the arc for
/// the ball that put you out was once all but hidden under the paint on your mask). Prints the tick of each step and
/// SMOKE PASS or FAIL, and quits. <c>--duel-distance=M</c> stands you M metres from the bot instead of six, for a close-up.
/// </summary>
public sealed class DuelDemo : ICommandSource
{
    private const int FireFrom = 40;
    private const int FlybyAfterOut = 60;
    private const int NearAfterOut = 100;
    private const int IncomingAfterOut = 150;
    private const int EndAfterIncoming = 420;

    /// <summary>How long after each ball the screen is looked at for its arc (ticks; the ball on your mask, that and its flight).</summary>
    private const int LookAfter = 16;

    private readonly Node _host;
    private readonly SimWorld _sim;
    private readonly IReadOnlyList<OpponentPawn> _opponents;
    private readonly Hud _hud;
    private readonly List<string> _arcs = new();
    private readonly List<string> _problems = new();
    private PlayerState? _target;
    private int _start = -1;
    private int _outAt = -1;
    private int _incomingAt = -1;
    private Image? _clear;
    private bool _finished;

    public DuelDemo(Node host, SimWorld sim, IReadOnlyList<OpponentPawn> opponents, Hud hud)
    {
        _host = host;
        _sim = sim;
        _opponents = opponents;
        _hud = hud;
    }

    /// <summary>Puts <paramref name="player"/> six metres (or <c>--duel-distance</c>) in front of an opponent out in the open, facing them.</summary>
    public void Setup(PlayerController player)
    {
        float distance = float.TryParse(Args.Value("--duel-distance"), NumberStyles.Float, CultureInfo.InvariantCulture, out float d) ? d : 6f;
        foreach (OpponentPawn o in _opponents)
        {
            bool outdoors = _sim.Level?.AreaAt(o.State.Position) is not { Indoor: true };
            bool opponent = o.State.Team != player.State.Team;
            if (o.State.Alive && opponent && outdoors && ScenePositions.FindSpot(_sim, o.State, distance, out SVector3 spot))
            {
                _target = o.State;
                player.Teleport(spot, ScenePositions.Facing(spot, o.State.Position));
                return;
            }
        }
    }

    public InputCommand Next(int tick, PlayerState me)
    {
        if (_start < 0)
        {
            _start = tick;
            GD.Print($"DUEL target {_target?.Name ?? "none"}; firing from tick {FireFrom}");
        }

        int t = tick - _start;
        if (_target is null || t > 1500)
        {
            _problems.Add(_target is null ? "no target out in the open" : "timed out");
            Finish();
            return default;
        }

        if (_outAt < 0 && !_target.Alive)
        {
            _outAt = t;
            GD.Print($"DUEL {_target.Name} out at tick {t}");
        }

        if (_outAt >= 0)
        {
            int since = t - _outAt;
            if (since == FlybyAfterOut - 4)
            {
                _clear = Grab(); // the screen with no arcs on it, to tell an arc from what's behind it
            }
            else if (since == FlybyAfterOut)
            {
                // From ahead and to the left, across in front of your face and on.
                Shoot(me, me.EyePosition + ViewAngles.FlatForward(me.Yaw + 1.2f) * 5f + new SVector3(0f, 0.1f, 0f),
                    me.EyePosition + ViewAngles.FlatForward(me.Yaw) * 0.8f, 78);
                GD.Print($"DUEL ball past your head at tick {t}");
            }
            else if (since == NearAfterOut)
            {
                // From ahead and to the right, onto the ground beside you.
                Shoot(me, me.EyePosition + ViewAngles.FlatForward(me.Yaw - 0.9f) * 6f + new SVector3(0f, 0.2f, 0f),
                    me.Position + ViewAngles.FlatForward(me.Yaw - 0.5f) * 1.2f, 79);
                GD.Print($"DUEL ball breaking close by at tick {t}");
            }
            else if (since == IncomingAfterOut)
            {
                _incomingAt = t;
                Incoming(me);
                GD.Print($"DUEL incoming ball at tick {t}");
                // You're out a moment later, and this stops being asked for your moves: the look at its arc is timed.
                _host.GetTree().CreateTimer(LookAfter * _sim.Dt + 0.1).Timeout += () => Look(IncomingKind.Hit);
            }

            if (since == FlybyAfterOut + LookAfter)
            {
                Look(IncomingKind.Flyby);
            }
            else if (since == NearAfterOut + LookAfter)
            {
                Look(IncomingKind.NearBreak);
            }
        }

        if (_incomingAt >= 0 && t >= _incomingAt + EndAfterIncoming)
        {
            Finish();
        }

        (float yaw, float pitch) = ScenePositions.AimAt(_sim, me, _target);
        bool pull = _target.Alive && t >= FireFrom && (t - FireFrom) % 24 < 2;
        return new InputCommand { Tick = tick, Yaw = yaw, Pitch = pitch, Buttons = pull ? InputButtons.Fire : InputButtons.None };
    }

    /// <summary>A ball from the nearest other opponent, five metres out, straight at your mask.</summary>
    private void Incoming(PlayerState me)
    {
        // From the first direction (ahead, then the sides, then behind) with a clear line to the face.
        SVector3 mask = me.EyePosition + ViewAngles.Forward(me.Yaw, me.Pitch) * 0.06f;
        SVector3 from = mask;
        foreach (float turn in new[] { 0.35f, -0.35f, 1.2f, -1.2f, 2.6f })
        {
            from = me.EyePosition + ViewAngles.FlatForward(me.Yaw + turn) * 5f + new SVector3(0f, 0.1f, 0f);
            if (!_sim.Collision.SweepSphere(from, mask, 0.01f, out _))
            {
                break;
            }
        }

        Shoot(me, from, mask, 77);
    }

    /// <summary>A ball from <paramref name="from"/> through <paramref name="through"/>, from the nearest other opponent.</summary>
    private void Shoot(PlayerState me, SVector3 from, SVector3 through, ulong seed)
    {
        PlayerState? shooter = null;
        float best = float.MaxValue;
        foreach (OpponentPawn o in _opponents)
        {
            float d = SVector3.Distance(o.State.Position, me.Position);
            if (o.State.Alive && o.State.Team != me.Team && d < best)
            {
                best = d;
                shooter = o.State;
            }
        }

        SVector3 velocity = SVector3.Normalize(through - from) * _sim.Config.Shot.MuzzleVelocity;
        _sim.Ballistics.Spawn(from, velocity, shooter?.Id ?? -1, 9999, 1, new Pcg32(seed), _sim.Dt, _sim.Tick, _sim.Events);
    }

    /// <summary>The frame last drawn, or null where nothing is drawn (headless).</summary>
    private Image? Grab() => DisplayServer.GetName() == "headless" ? null : _host.GetViewport().GetTexture()?.GetImage();

    /// <summary>
    /// Whether the arc of <paramref name="kind"/> the HUD says it's showing is on the screen: enough of the pixels round
    /// the middle of its stroke have gone well toward its colour since before any arcs (part see-through, an arc is
    /// never quite its colour). Where what's behind it was its colour already, it can't be told, and doesn't count
    /// either way (but for the one that put you out).
    /// </summary>
    private void Look(IncomingKind kind)
    {
        int added = _hud.IncomingArcsAdded[(int)kind];
        (IncomingKind Kind, Vector2 At, Color Colour)? arc = null;
        foreach (var a in _hud.IncomingArcs.Showing())
        {
            if (a.Kind == kind)
            {
                arc = a;
                break;
            }
        }

        if (arc is null)
        {
            _problems.Add($"no {kind} arc showing ({added} added)");
            _arcs.Add($"{kind}: none");
            return;
        }

        Image? now = Grab();
        if (now is null || _clear is null || now.GetSize() != _clear.GetSize())
        {
            _arcs.Add($"{kind}: showing (not drawn here)");
            return;
        }

        Vector2 size = _hud.IncomingArcs.Size;
        int cx = (int)(arc.Value.At.X / size.X * now.GetWidth()), cy = (int)(arc.Value.At.Y / size.Y * now.GetHeight());
        int on = 0, before = 0;
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int x = System.Math.Clamp(cx + dx, 0, now.GetWidth() - 1), y = System.Math.Clamp(cy + dy, 0, now.GetHeight() - 1);
                float was = Off(_clear.GetPixel(x, y), arc.Value.Colour), @is = Off(now.GetPixel(x, y), arc.Value.Colour);
                on += @is < was * 0.6f && was - @is > 0.25f ? 1 : 0;
                before += was < 0.3f ? 1 : 0;
            }
        }

        if (before >= 3)
        {
            _arcs.Add($"{kind}: can't tell from what's behind it at ({cx}, {cy})");
            if (kind == IncomingKind.Hit)
            {
                _problems.Add("the Hit arc couldn't be told from what's behind it");
            }
        }
        else if (on >= 3)
        {
            _arcs.Add($"{kind}: on screen at ({cx}, {cy})");
        }
        else
        {
            _arcs.Add($"{kind}: not on screen at ({cx}, {cy})");
            _problems.Add($"the {kind} arc isn't on the screen");
        }
    }

    /// <summary>How far one colour is from another (the sum of the channels' differences).</summary>
    private static float Off(Color a, Color b) => System.MathF.Abs(a.R - b.R) + System.MathF.Abs(a.G - b.G) + System.MathF.Abs(a.B - b.B);

    /// <summary>Prints the report and quits (the spectator view ends a scripted run once it's shown who got you).</summary>
    public void Finish()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        int[] added = _hud.IncomingArcsAdded;
        if (_outAt < 0)
        {
            _problems.Add("the target was never put out");
        }

        foreach (IncomingKind kind in new[] { IncomingKind.Flyby, IncomingKind.NearBreak, IncomingKind.Hit })
        {
            if (added[(int)kind] == 0)
            {
                _problems.Add($"no {kind} arc added");
            }
        }

        bool ok = _problems.Count == 0;
        GD.Print($"SMOKE {(ok ? "PASS" : "FAIL")}: duel demo: arcs added {string.Join(", ", System.Linq.Enumerable.Select(new[] { 0, 1, 2, 3 }, k => $"{(IncomingKind)k} {added[k]}"))}; " +
                 string.Join("; ", _arcs) + (ok ? "" : ": " + string.Join("; ", _problems)));
        _host.GetTree().QuitCleanly(ok ? 0 : 1);
    }
}
