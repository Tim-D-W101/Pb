using System.Numerics;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Players;

namespace Pb.Sim.Match;

/// <summary>
/// Speedball's buzzers: each side's station at its back line (the field's <see cref="FieldSpec.Buzzers"/>, side 0's
/// first). A player of the other side hangs it by holding Interact within <see cref="SpeedballRules.HangReach"/> of its post
/// for <see cref="SpeedballRules.HangTime"/>, which wins the point; letting go, stepping off or going out starts it again.
/// One player hangs a station at a time: whoever got there first. The authority runs it; a joining copy is told how it
/// stands (<see cref="ApplyServer"/>).
/// </summary>
public sealed class BuzzerSet
{
    private readonly Vector3[] _posts;
    private readonly int[] _hanger;
    private readonly float[] _held;
    private readonly bool[] _holding;

    public BuzzerSet(IReadOnlyList<Vector3> posts, SpeedballRules rules)
    {
        _posts = posts.ToArray();
        _hanger = new int[_posts.Length];
        _held = new float[_posts.Length];
        _holding = new bool[_posts.Length];
        Array.Fill(_hanger, -1);
        Rules = rules;
    }

    public SpeedballRules Rules { get; }

    public int Count => _posts.Length;

    /// <summary>Side <paramref name="side"/>'s station (on the ground at its post).</summary>
    public Vector3 Post(int side) => _posts[side];

    /// <summary>Who is hanging side <paramref name="side"/>'s buzzer (−1: nobody).</summary>
    public int Hanger(int side) => _hanger[side];

    /// <summary>How far through the hang they are, 0 to 1.</summary>
    public float Progress(int side) => Math.Clamp(_held[side] / Rules.HangTime, 0f, 1f);

    /// <summary>The side whose buzzer was hung (−1 while neither has been), and who hung it.</summary>
    public int HungSide { get; private set; } = -1;

    public int HungBy { get; private set; } = -1;

    /// <summary>Whether <paramref name="player"/> stands close enough to hang side <paramref name="side"/>'s buzzer (and isn't on that side).</summary>
    public bool InReach(PlayerState player, int side)
    {
        Vector3 to = player.Position - _posts[side];
        return player.Team != side && to.X * to.X + to.Z * to.Z <= Rules.HangReach * Rules.HangReach && MathF.Abs(to.Y) < 1.5f;
    }

    /// <summary>
    /// <paramref name="player"/> is holding Interact this tick (<paramref name="held"/>: it may, and does): they start or
    /// go on hanging a station in reach, if nobody else is.
    /// </summary>
    internal void Hold(SimWorld sim, PlayerState player, bool held)
    {
        if (HungSide >= 0)
        {
            return;
        }

        for (int side = 0; side < _posts.Length; side++)
        {
            bool mine = _hanger[side] == player.Id;
            if (!held || !player.Alive || !player.Present || !InReach(player, side))
            {
                continue;
            }

            if (_hanger[side] < 0)
            {
                _hanger[side] = player.Id;
                _held[side] = 0f;
                Raise(sim, SimEventType.BuzzerHanging, player, side, 1f);
                mine = true;
            }

            _holding[side] |= mine;
        }
    }

    /// <summary>
    /// After everyone's held (or not) this tick: each hang goes on, is done, or starts again (its hanger let go, stepped
    /// off or went out).
    /// </summary>
    internal void Update(SimWorld sim, float dt)
    {
        for (int side = 0; side < _posts.Length; side++)
        {
            int id = _hanger[side];
            bool holding = _holding[side];
            _holding[side] = false;
            if (id < 0 || HungSide >= 0)
            {
                continue;
            }

            PlayerState? hanger = sim.FindPlayer(id);
            if (!holding || hanger is null)
            {
                _hanger[side] = -1;
                _held[side] = 0f;
                if (hanger is not null)
                {
                    Raise(sim, SimEventType.BuzzerHanging, hanger, side, 0f);
                }

                continue;
            }

            _held[side] += dt;
            if (_held[side] >= Rules.HangTime)
            {
                HungSide = side;
                HungBy = id;
                Raise(sim, SimEventType.BuzzerHung, hanger, side, 1f);
            }
        }
    }

    /// <summary>A joining copy: how the server says side <paramref name="side"/>'s station stands.</summary>
    internal void ApplyServer(int side, int hanger, float progress, int hungBy)
    {
        _hanger[side] = hanger;
        _held[side] = progress * Rules.HangTime;
        if (hungBy >= 0)
        {
            HungSide = side;
            HungBy = hungBy;
        }
    }

    private void Raise(SimWorld sim, SimEventType type, PlayerState player, int side, float value) => sim.Events.Add(new SimEvent
    {
        Type = type, Tick = sim.Tick, PlayerId = player.Id, TargetId = -1, ColliderId = -1, Team = player.Team, Position = _posts[side],
        Extra = side, Value = value,
    });
}
