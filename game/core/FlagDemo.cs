using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Player;
using Pb.Game.World;
using Pb.Sim;
using Pb.Sim.AI;
using Pb.Sim.Core;
using Pb.Sim.Match;
using Pb.Sim.Players;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --flag-demo</c> (with <c>--mode=flag</c>, in any area): a point of capture the flag through your own eyes, for
/// screenshots. The countdown, looking towards the flag you're after; then, the other side gone from the field, one of
/// your bots put by that flag (it takes it), you a few metres off watching; then you walk behind it as it carries the
/// flag home, a few metres back, until it scores and the point ends (on through the screen between points, or with
/// <c>--race-to=1</c> the match's summary). With <c>--flag-drop</c> the carrier goes out a few seconds into its way home,
/// and you look at the flag lying where it fell. Prints what it does; each point plays it again. Runs on the sim's clock,
/// so capture it at a low <c>--fixed-fps</c> for fewer frames.
/// </summary>
public sealed class FlagDemo : ICommandSource
{
    /// <summary>Ticks after the horn before your bot is put by the flag.</summary>
    private const int PutAt = 60;

    /// <summary>How far behind the carrier you follow (m), and how often you catch up (ticks).</summary>
    private const float Behind = 5f;
    private const int FollowEvery = 90;

    /// <summary>With <c>--flag-drop</c>, ticks the carrier carries it before going out.</summary>
    private const int DropAfter = 360;

    private readonly bool _drop = Args.Has("--flag-drop");
    private int _takenAt = -1;
    private bool _dropped;

    private readonly SimWorld _sim;
    private readonly PlayerController _player;
    private readonly IReadOnlyList<BotBrain> _bots;
    private readonly IReadOnlyList<OpponentPawn> _pawns;
    private readonly NavGrid _grid;
    private int _liveAt = -1;
    private int _followedAt;
    private PlayerState? _carrier;
    private SVector3 _lastSeen;

    public FlagDemo(SimWorld sim, PlayerController player, IReadOnlyList<BotBrain> bots, IReadOnlyList<OpponentPawn> pawns, NavGrid grid)
    {
        _sim = sim;
        _player = player;
        _bots = bots;
        _pawns = pawns;
        _grid = grid;
    }

    public InputCommand Next(int tick, PlayerState state)
    {
        if (_sim.Match is not { Flags: { } flags } match)
        {
            return new InputCommand { Tick = tick, Yaw = state.Yaw, Pitch = state.Pitch };
        }

        int target = flags.TargetOf(state.Team);
        if (match.Phase != MatchPhase.Live)
        {
            return Look(tick, state, flags.Position(target) + new SVector3(0f, 1.2f, 0f));
        }

        if (_liveAt < 0)
        {
            _liveAt = tick;
            GD.Print($"FLAG DEMO the horn at tick {tick}: your bot by the flag at +{PutAt}, then you behind it all the way home");
        }

        if (_carrier is null && tick - _liveAt >= PutAt)
        {
            Put(flags, target, state);
        }

        if (_carrier is { Alive: true } holding && flags.FlagOf(holding.Id) >= 0)
        {
            _takenAt = _takenAt < 0 ? tick : _takenAt;
            if (_drop && !_dropped && tick - _takenAt >= DropAfter)
            {
                // Out, where it stands: the flag falls there (the sim lays it down at the next step).
                holding.Alive = false;
                _dropped = true;
                GD.Print($"FLAG DEMO {holding.Name} out at {holding.Position}: the flag down there");
            }
        }

        if (_carrier is not { Alive: true } carrier)
        {
            return Look(tick, state, flags.Position(target) + new SVector3(0f, 0.3f, 0f));
        }

        // Behind the carrier: caught up every so often (not walking, so nothing in the way holds you up), watching it.
        if (flags.FlagOf(carrier.Id) >= 0 && tick - _followedAt >= FollowEvery)
        {
            _followedAt = tick;
            SVector3 heading = carrier.Position - _lastSeen;
            heading.Y = 0f;
            _lastSeen = carrier.Position;
            if (heading.LengthSquared() > 0.25f &&
                _grid.TrySnap(carrier.Position - SVector3.Normalize(heading) * Behind, out SVector3 spot) && MathF.Abs(spot.Y - carrier.Position.Y) < 1f)
            {
                _player.Teleport(spot, MathF.Atan2(-(carrier.Position.X - spot.X), -(carrier.Position.Z - spot.Z)));
            }
        }

        return Look(tick, state, carrier.Position + new SVector3(0f, 1.3f, 0f));
    }

    /// <summary>Their side off the field; your first bot a step from the flag (it takes it), and you a few metres off.</summary>
    private void Put(FlagSet flags, int target, PlayerState you)
    {
        foreach (BotBrain bot in _bots)
        {
            if (bot.Self.Team != you.Team)
            {
                bot.Passive = true;
                bot.Self.Present = false;
                bot.Self.Position = new SVector3(0f, -60f, 0f);
            }
        }

        BotBrain? mate = null;
        foreach (BotBrain bot in _bots)
        {
            if (bot.Self.Team == you.Team && bot.Self.Alive)
            {
                mate = bot;
                break;
            }
        }

        OpponentPawn? pawn = null;
        foreach (OpponentPawn p in _pawns)
        {
            if (mate is not null && p.State == mate.Self)
            {
                pawn = p;
            }
        }

        if (mate is null || pawn is null)
        {
            GD.PushError("FLAG DEMO has no bot on your side to carry the flag");
            return;
        }

        // From the flag towards where your side scores: your bot a step off it, you a few metres further and to the side.
        SVector3 flag = flags.Position(target), home = flags.ScoreAt(you.Team);
        SVector3 way = home - flag;
        way.Y = 0f;
        way = SVector3.Normalize(way);
        var across = new SVector3(-way.Z, 0f, way.X);
        float step = flags.IsCentre ? 1.35f : 1.0f;
        SVector3 by = Snap(flag + way * step, home);
        SVector3 watch = Snap(flag + way * 6f + across * 3f, home);
        pawn.Teleport(by, MathF.Atan2(-(flag.X - by.X), -(flag.Z - by.Z)));
        _player.Teleport(watch, MathF.Atan2(-(flag.X - watch.X), -(flag.Z - watch.Z)));
        _carrier = mate.Self;
        _lastSeen = by;
        _followedAt = _sim.Tick;
        GD.Print($"FLAG DEMO {mate.Self.Name} by the flag at {by}, you at {watch}");
    }

    /// <summary>On the ground (as high as <paramref name="ground"/>, where your side scores), not up on the bunker the flag may stand on.</summary>
    private SVector3 Snap(SVector3 at, SVector3 ground) => _grid.TrySnap(at with { Y = ground.Y }, out SVector3 spot) ? spot : at with { Y = ground.Y };

    private static InputCommand Look(int tick, PlayerState state, SVector3 at)
    {
        (float yaw, float pitch) = ViewAngles.FromDirection(at - state.EyePosition);
        return new InputCommand { Tick = tick, Yaw = yaw, Pitch = Math.Clamp(pitch, -0.6f, 0.4f) };
    }
}
