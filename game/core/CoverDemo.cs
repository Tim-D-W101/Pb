using System.Collections.Generic;
using System.Linq;
using Godot;
using Pb.Game.Player;
using Pb.Game.Ui;
using Pb.Sim;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Players;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --cover-demo</c>: an opponent tucks in behind the nearest low cover, stands to shoot over it and
/// tucks in again, seen from the side, for checking that knees and elbows stay out of what they crouch
/// behind. It prints how far into the cover the knees and elbows got (as spheres the size of the joint) and
/// quits. The cover is the nearest out in the open to the first opponent, or with <c>--cover-at=x,z</c> the
/// nearest to that point, indoors or out. Like the gait demo it runs by sim ticks, so it plays out the same at
/// any rendering speed.
/// </summary>
public partial class CoverDemo : Node, ICommandSource
{
    /// <summary>Crouched, then stood up over the cover, then crouched again (s).</summary>
    private const float Crouch = 3f, Stand = 2f, CrouchAgain = 3f;

    /// <summary>How big a knee or an elbow is for the check (m).</summary>
    private const float Joint = 0.05f;

    private static readonly string[] Knees = { "LeftLeg", "RightLeg" };
    private static readonly string[] Elbows = { "LeftForeArm", "RightForeArm" };

    private SimWorld _sim = null!;
    private OpponentPawn? _who;
    private Camera3D _camera = null!;
    private SVector3 _spot;
    private SVector3 _side;
    private float _yaw;
    private int _first = -1;
    private float _knees, _elbows, _kneesStanding;
    private bool _printed;

    /// <param name="towardSun">Which way the sun is (world, level): the camera watches from that side, so they're lit.</param>
    public void Start(SimWorld sim, BotSquad squad, IReadOnlyList<OpponentPawn> opponents, Hud hud, float farClip, SVector3 towardSun)
    {
        _sim = sim;
        _who = opponents.FirstOrDefault(o => o.State.Alive);
        hud.Visible = false;
        SVector3 near = _who?.State.Position ?? SVector3.Zero;
        bool given = Args.Value("--cover-at") is { } text && TryPoint(text, out near);
        if (_who is null || !FindSpot(squad.Cover, near, outdoors: !given, out CoverPoint point))
        {
            GD.PushError("COVER DEMO found nobody or no low cover with a clear side view; quitting");
            GetTree().Quit(1);
            return;
        }

        _spot = point.Position;
        SVector3 face = -point.Normal;
        _yaw = Mathf.Atan2(-face.X, -face.Z);
        _side = new SVector3(-face.Z, 0f, face.X);
        if (SVector3.Dot(_side, towardSun) < 0f && Clear(point, -_side))
        {
            _side = -_side;
        }

        _who.Teleport(_spot, _yaw);
        _who.Steer(this);
        _camera = new Camera3D { Name = "CoverCamera", Fov = 45f, Far = farClip, Near = 0.05f };
        AddChild(_camera);
        _camera.MakeCurrent();
        SVector3 eye = _spot + _side * 2.6f + face * 0.25f + new SVector3(0f, 0.95f, 0f);
        _camera.GlobalPosition = eye.ToGodot();
        _camera.LookAt((_spot + face * 0.25f + new SVector3(0f, 0.75f, 0f)).ToGodot(), Vector3.Up);
        GD.Print($"COVER DEMO {_who.State.Name} tucks in behind the low cover at {_spot}, facing {Mathf.RadToDeg(_yaw):0}°");
    }

    public InputCommand Next(int tick, PlayerState me)
    {
        if (_first < 0)
        {
            _first = tick;
        }

        float seconds = (tick - _first) * _sim.Config.Dt;
        bool standing = seconds >= Crouch && seconds < Crouch + Stand;
        if (seconds >= Crouch + Stand + CrouchAgain && !_printed)
        {
            _printed = true;
            GD.Print($"COVER DEMO crouched: knees up to {_knees * 100f:0.0} cm into the cover, elbows up to {_elbows * 100f:0.0} cm; " +
                     $"standing over it: knees up to {_kneesStanding * 100f:0.0} cm");
            GetTree().Quit();
        }

        // Standing to shoot over the top, the aim drops a little onto whoever's beyond.
        return new InputCommand { Tick = tick, Yaw = _yaw, Pitch = standing ? -0.08f : 0f, Buttons = standing ? InputButtons.None : InputButtons.Crouch };
    }

    public override void _Process(double delta)
    {
        if (_who?.Visual.Model is not { } model || _first < 0)
        {
            return;
        }

        // Measured once they've settled into each pose.
        float seconds = (_sim.Tick - _first) * _sim.Config.Dt;
        bool settledCrouch = seconds is > 0.8f and < Crouch || seconds > Crouch + Stand + 0.8f;
        bool settledStand = seconds > Crouch + 0.6f && seconds < Crouch + Stand;
        Vector3 hips = model.Attachment("Hips").GlobalPosition;
        foreach (string knee in Knees)
        {
            float depth = Into(hips, model.Attachment(knee).GlobalPosition);
            _knees = settledCrouch ? Mathf.Max(_knees, depth) : _knees;
            _kneesStanding = settledStand ? Mathf.Max(_kneesStanding, depth) : _kneesStanding;
        }

        foreach (string elbow in Elbows)
        {
            string arm = elbow == "LeftForeArm" ? "LeftArm" : "RightArm";
            float depth = Into(model.Attachment(arm).GlobalPosition, model.Attachment(elbow).GlobalPosition);
            _elbows = settledCrouch ? Mathf.Max(_elbows, depth) : _elbows;
        }
    }

    /// <summary>How far a joint at <paramref name="joint"/>, reached from <paramref name="from"/>, is into whatever's between (m; 0 if clear).</summary>
    private float Into(Vector3 from, Vector3 joint)
    {
        SVector3 a = from.ToSim(), b = joint.ToSim();
        if (!_sim.Collision.SweepSphere(a, b, Joint, out SweepHit hit))
        {
            return 0f;
        }

        return (1f - hit.T) * SVector3.Distance(a, b);
    }

    /// <summary>The nearest low cover point to <paramref name="near"/> out in the open (sky overhead, so it's lit) with room beside it to watch from.</summary>
    private bool FindSpot(CoverSet cover, SVector3 near, bool outdoors, out CoverPoint found)
    {
        found = default;
        float best = float.MaxValue;
        foreach (CoverPoint p in cover.Points)
        {
            float d = SVector3.DistanceSquared(p.Position with { Y = 0f }, near with { Y = 0f });
            if (p.Height != CoverHeight.Half || d >= best ||
                outdoors && _sim.Collision.SweepSphere(p.Position + new SVector3(0f, 1.2f, 0f), p.Position + new SVector3(0f, 40f, 0f), 0.2f, out _))
            {
                continue;
            }

            SVector3 face = -p.Normal;
            SVector3 side = new(-face.Z, 0f, face.X);
            if (Clear(p, side) || Clear(p, -side))
            {
                best = d;
                found = p;
            }
        }

        return best < float.MaxValue;
    }

    /// <summary>A point on the plan from <c>x,z</c> (m).</summary>
    private static bool TryPoint(string text, out SVector3 point)
    {
        string[] parts = text.Split(',');
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        bool ok = parts.Length == 2 & float.TryParse(parts[0], culture, out float x) & float.TryParse(parts.Length > 1 ? parts[1] : "", culture, out float z);
        point = new SVector3(x, 0f, z);
        return ok;
    }

    /// <summary>Whether the side view of <paramref name="p"/> from along <paramref name="side"/> is unobstructed.</summary>
    private bool Clear(CoverPoint p, SVector3 side)
    {
        SVector3 eye = p.Position + side * 2.6f + new SVector3(0f, 0.95f, 0f);
        foreach (float height in new[] { 0.45f, 1.0f })
        {
            if (_sim.Collision.SweepSphere(eye, p.Position + new SVector3(0f, height, 0f) + side * 0.35f, 0.05f, out _))
            {
                return false;
            }
        }

        return true;
    }
}
