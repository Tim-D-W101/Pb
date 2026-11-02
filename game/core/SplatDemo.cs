using System.Collections.Generic;
using Godot;
using Pb.Game.Ballistics;
using Pb.Game.Player;
using Pb.Sim;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Events;
using Pb.Sim.Players;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --splat-demo</c> (Phase 5): the splats on a wall. It finds the nearest stretch of wall round your start long and
/// clear enough for the shots, stands you along it and fires a run of shots that hit it at a glance (they streak, along
/// the ball's travel), then turns square on and fires a few head-on (round), then holds the view on them. It counts what
/// your balls did there (broke on the wall, streaked or round, or bounced off) and the splats placed for them. Drawn (not
/// headless), it also compares the view before the first shot with the same view at the end at each place a ball broke:
/// a splat that's there but not drawn (a renderer without decals once showed none) fails it. It prints SMOKE PASS or
/// FAIL and quits.
/// </summary>
public sealed class SplatDemo : ICommandSource, ISimEventListener
{
    private const int GlanceFrom = 60, Glances = 12, SquareFrom = 250, Squares = 5, Every = 12, EndAt = 510;

    /// <summary>How far off the wall you stand, and how far along it from where the glancing shots land (m).</summary>
    private const float Off = 2.2f, Stand = 4.5f;

    /// <summary>How much clear wall the shots need either side of where the glancing ones start (m).</summary>
    private const float Before = 2.3f, After = 5.6f;

    /// <summary>Where you look from before the first shot to the end (along the wall): between the two runs, both in view.</summary>
    private const float Watch = 2.6f;

    /// <summary>How far round your start a wall is looked for (m).</summary>
    private const float Search = 40f;

    private readonly Node _host;
    private readonly SimWorld _sim;
    private readonly SplatSystem _splats;
    private readonly List<SVector3> _breaks = new();
    private SVector3 _wall;
    private SVector3 _normal;
    private SVector3 _along;
    private bool _found;
    private int _start = -1;
    private int _me = -1;
    private int _team;
    private int _shots, _bounced, _elsewhere, _glancingBroke, _squareBroke;
    private int _splatsBefore, _streaksBefore;
    private Image? _before, _after;
    private bool _finished;

    public SplatDemo(Node host, SimWorld sim, SplatSystem splats)
    {
        _host = host;
        _sim = sim;
        _splats = splats;
    }

    /// <summary>Finds the nearest wall in sight of <paramref name="player"/> with a clear stretch for the shots, and stands them by it.</summary>
    public void Setup(PlayerController player)
    {
        SVector3 eye = player.State.EyePosition;
        float best = float.MaxValue;
        for (int i = 0; i < 72; i++)
        {
            SVector3 dir = ViewAngles.FlatForward(i * System.MathF.Tau / 72f);
            if (!_sim.Collision.SweepSphere(eye, eye + dir * Search, 0f, out SweepHit hit) || System.MathF.Abs(hit.Normal.Y) > 0.2f || hit.T * Search >= best)
            {
                continue;
            }

            SVector3 normal = SVector3.Normalize(hit.Normal with { Y = 0f });
            foreach (float sign in new[] { 1f, -1f })
            {
                SVector3 along = SVector3.Normalize(SVector3.Cross(SVector3.UnitY, normal)) * sign;
                float back = Extent(hit.Point, normal, -along), ahead = Extent(hit.Point, normal, along);
                if (back + ahead < Before + After)
                {
                    continue;
                }

                // As near the point found as the clear stretch allows.
                SVector3 wall = hit.Point + along * System.Math.Clamp(0f, Before - back, ahead - After);
                SVector3 spot = wall + normal * Off + along * Stand;
                spot.Y = player.State.Position.Y;
                if (ScenePositions.IsSpot(_sim, player.State, spot))
                {
                    best = hit.T * Search;
                    (_wall, _normal, _along, _found) = (wall, normal, along, true);
                    break;
                }
            }
        }

        _me = player.State.Id;
        _team = player.State.Team;
        if (_found)
        {
            SVector3 at = _wall + _normal * Off + _along * Stand;
            at.Y = player.State.Position.Y;
            player.Teleport(at, ScenePositions.Facing(at, _wall + _along * Watch));
        }
    }

    /// <summary>
    /// How far the face through <paramref name="point"/> runs along <paramref name="along"/> flat and unbroken (no
    /// corner, opening or anything standing in front of it as far out as you stand), up to 12 m: rays straight at it at
    /// three heights round the point's, every quarter metre.
    /// </summary>
    private float Extent(SVector3 point, SVector3 normal, SVector3 along)
    {
        float reached = 0f, from = Off + 0.1f, length = from + 1f;
        for (float d = 0.25f; d <= 12f; d += 0.25f)
        {
            foreach (float h in new[] { -0.6f, 0f, 0.4f })
            {
                SVector3 o = point + normal * from + along * d + new SVector3(0f, h, 0f);
                if (!_sim.Collision.SweepSphere(o, o - normal * length, 0f, out SweepHit hit) || System.MathF.Abs(hit.T * length - from) > 0.12f ||
                    SVector3.Dot(hit.Normal, normal) < 0.95f)
                {
                    return reached;
                }
            }

            reached = d;
        }

        return reached;
    }

    public void OnSimEvent(in SimEvent e)
    {
        if (e.PlayerId != _me || _start < 0)
        {
            return;
        }

        switch (e.Type)
        {
            case SimEventType.ShotFired:
                _shots++;
                break;
            case SimEventType.BallBounced when OnWall(e.Position):
                _bounced++;
                break;
            case SimEventType.BallBroke when OnWall(e.Position):
                _breaks.Add(e.Position);
                if (e.Tick - _start < SquareFrom)
                {
                    _glancingBroke++;
                }
                else
                {
                    _squareBroke++;
                }

                break;
            case SimEventType.BallBroke:
                _elsewhere++;
                break;
        }
    }

    /// <summary>Whether a ball met the wall's face here (within a hand of its plane).</summary>
    private bool OnWall(SVector3 at) => _found && System.MathF.Abs(SVector3.Dot(at - _wall, _normal)) < 0.25f;

    public InputCommand Next(int tick, PlayerState me)
    {
        if (_start < 0)
        {
            _start = tick;
            _splatsBefore = _splats.ActiveCount;
            _streaksBefore = _splats.Streaks;
            GD.Print(_found ? $"SPLAT DEMO wall at {_wall} facing {_normal}, you at {me.Position}; glancing shots from tick {GlanceFrom}, square ones from {SquareFrom}" +
                              (_splats.Cards ? "; the renderer draws no decals: the splats are cards" : "")
                : "SPLAT DEMO no wall near the start");
        }

        int t = tick - _start;
        if (!_found)
        {
            Finish();
            return default;
        }

        if (t >= EndAt)
        {
            Finish(); // and hold the view on the splats till it quits
        }

        // The view to compare: the same before the first shot and at the end.
        if (t == GlanceFrom - 2)
        {
            _before = Grab();
        }
        else if (t == EndAt - 2)
        {
            _after = Grab();
        }

        // At a glance: aimed along the wall at points walking back towards you; square on: at the wall in front.
        SVector3 target = _wall + _along * Watch + new SVector3(0f, -0.3f, 0f);
        bool fire = false;
        if (t >= GlanceFrom && t < SquareFrom)
        {
            int k = System.Math.Clamp((t - GlanceFrom) / Every, 0, Glances - 1);
            target = _wall + _along * (0.8f - 0.25f * k) + new SVector3(0f, -0.35f + 0.15f * (k % 4), 0f);
            fire = (t - GlanceFrom) % Every == 0 && (t - GlanceFrom) / Every < Glances;
        }
        else if (t >= SquareFrom && t <= SquareFrom + Squares * Every + 30)
        {
            int k = System.Math.Clamp((t - SquareFrom) / Every, 0, Squares - 1);
            target = _wall + _along * (Stand + 0.35f * (k - 2)) + new SVector3(0f, -0.2f + 0.15f * (k % 2), 0f);
            fire = (t - SquareFrom) % Every == 0 && (t - SquareFrom) / Every < Squares;
        }

        (float yaw, float pitch) = ViewAngles.FromDirection(target - me.EyePosition);
        return new InputCommand { Tick = tick, Yaw = yaw, Pitch = pitch, Buttons = fire ? InputButtons.Fire : InputButtons.None };
    }

    /// <summary>The frame last drawn, or null where nothing is drawn (headless).</summary>
    private Image? Grab() => DisplayServer.GetName() == "headless" ? null : _host.GetViewport().GetTexture()?.GetImage();

    /// <summary>
    /// Of the places a ball broke on the wall that are in view, how many look painted: the colour at a handful of pixels
    /// round each has moved toward the paint's since before the first shot.
    /// </summary>
    private (int Seen, int Framed) Drawn()
    {
        Camera3D? camera = _host.GetViewport().GetCamera3D();
        if (_before is null || _after is null || camera is null || _before.GetSize() != _after.GetSize())
        {
            return (0, 0);
        }

        Color paint = _splats.TeamColor(_team);
        float mean = (paint.R + paint.G + paint.B) / 3f;
        Vector3 hue = new Vector3(paint.R - mean, paint.G - mean, paint.B - mean).Normalized();
        Vector2 view = _host.GetViewport().GetVisibleRect().Size;
        int w = _after.GetWidth(), h = _after.GetHeight();
        int seen = 0, framed = 0;
        foreach (SVector3 at in _breaks)
        {
            Vector3 p = at.ToGodot();
            if (camera.IsPositionBehind(p))
            {
                continue;
            }

            Vector2 s = camera.UnprojectPosition(p);
            int x = (int)(s.X / view.X * w), y = (int)(s.Y / view.Y * h);
            if (x < 4 || y < 4 || x >= w - 4 || y >= h - 4)
            {
                continue;
            }

            framed++;
            int painted = 0;
            for (int dy = -3; dy <= 3; dy++)
            {
                for (int dx = -3; dx <= 3; dx++)
                {
                    if (Toward(_after.GetPixel(x + dx, y + dy), hue) - Toward(_before.GetPixel(x + dx, y + dy), hue) > 0.05f)
                    {
                        painted++;
                    }
                }
            }

            seen += painted >= 4 ? 1 : 0;
        }

        return (seen, framed);
    }

    /// <summary>How far <paramref name="c"/> leans from grey toward the paint's hue.</summary>
    private static float Toward(Color c, Vector3 hue)
    {
        float mean = (c.R + c.G + c.B) / 3f;
        return (c.R - mean) * hue.X + (c.G - mean) * hue.Y + (c.B - mean) * hue.Z;
    }

    private void Finish()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        int splats = _splats.ActiveCount - _splatsBefore, streaks = _splats.Streaks - _streaksBefore;
        int broke = _glancingBroke + _squareBroke;
        var problems = new List<string>();
        if (!_found)
        {
            problems.Add("no wall near the start");
        }

        if (_squareBroke < Squares - 1)
        {
            problems.Add($"only {_squareBroke} of {Squares} square shots broke on the wall");
        }

        if (_glancingBroke < Glances / 3)
        {
            problems.Add($"only {_glancingBroke} of {Glances} glancing shots broke on the wall");
        }

        if (streaks < System.Math.Min(_glancingBroke, Glances / 3))
        {
            problems.Add($"{streaks} of the {_glancingBroke} glancing breaks streaked");
        }

        if (splats < broke)
        {
            problems.Add($"{splats} splats placed for {broke} breaks");
        }

        string drawn = "not drawn here (headless)";
        if (_after is not null)
        {
            (int seen, int framed) = Drawn();
            drawn = $"{seen} of the {framed} in view look painted{(_splats.Cards ? " (cards: the renderer draws no decals)" : "")}";
            if (framed < 4 || seen < System.Math.Max(3, framed / 2))
            {
                problems.Add($"only {seen} of the {framed} breaks in view look painted");
            }
        }

        bool ok = problems.Count == 0;
        GD.Print($"SMOKE {(ok ? "PASS" : "FAIL")}: splat demo: {_shots} shots; on the wall {_glancingBroke} of {Glances} glancing ones broke ({streaks} streaked) " +
                 $"and {_squareBroke} of {Squares} square ones, {_bounced} bounced off it, {_elsewhere} broke elsewhere; {splats} splats placed; {drawn}" +
                 (ok ? "" : ": " + string.Join("; ", problems)));
        _host.GetTree().QuitCleanly(ok ? 0 : 1);
    }
}
