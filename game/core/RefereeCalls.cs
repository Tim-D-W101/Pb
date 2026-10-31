using System.Collections.Generic;
using Godot;
using Pb.Game.Audio;
using Pb.Game.Ui;
using Pb.Sim;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Match;
using Pb.Sim.Players;

namespace Pb.Game.Core;

/// <summary>
/// The referee (presentation.jsonc hud.referee, paced by hud.refereePacing): a soft cue and "Game on!" when the round goes
/// live, a minute's warning, the whistle and how it went when the round ends, a call when you're out, and in a hold when
/// your side has the room to itself or it's contested, once that has settled. Each line is a subtitle and, once it's been
/// recorded, the referee's voice; lines wait for the one before to finish, and everyday ones that would come too soon
/// after the last aren't said.
/// </summary>
public partial class RefereeCalls : Node, ISimEventListener
{
    /// <summary>Without a recording, a line holds the referee this long before the next.</summary>
    private const double SilentLine = 1.4;

    /// <summary>"Game on!" comes this long into the breakout horn.</summary>
    private const double AfterHorn = 1.1;

    private readonly Queue<(string Line, double At)> _queue = new();
    private SimWorld _sim = null!;
    private PlayerState _player = null!;
    private RefereeDef _lines = null!;
    private RefereePacingDef _pacing = new();
    private Hud _hud = null!;
    private AudioDirector? _audio;
    private bool _oneMinute;
    private bool _thirty;
    private int _pick;
    private double _now;
    private double _busyUntil;
    private double _lastLine = double.NegativeInfinity;
    private HoldStatus? _roomPending;
    private double _roomSince;
    private readonly Dictionary<HoldStatus, double> _roomSaid = new();

    /// <summary>Every line called so far, in order (for the smoke tests).</summary>
    public List<string> Called { get; } = new();

    public void Initialize(SimWorld sim, PlayerState player, RefereeDef lines, Hud hud, AudioDirector? audio, RefereePacingDef? pacing = null)
    {
        _sim = sim;
        _player = player;
        _lines = lines;
        _pacing = pacing ?? new RefereePacingDef();
        _hud = hud;
        _audio = audio;
    }

    public void OnSimEvent(in SimEvent e)
    {
        if (_lines is null)
        {
            return;
        }

        switch (e.Type)
        {
            case SimEventType.MatchPhaseChanged when (MatchPhase)e.Extra == MatchPhase.Live:
                _oneMinute = _thirty = false;
                _roomPending = null;
                _roomSaid.Clear();
                if (_pacing.StartCue == "horn")
                {
                    _audio?.Round(Sfx.Horn);
                }
                else if (_pacing.StartCue == "soft")
                {
                    _audio?.Round(Sfx.StartCue);
                }

                if (_pacing.SayStart)
                {
                    Say(_lines.Start, _pacing.StartCue == "horn" ? AfterHorn : 0.45, always: true);
                }

                break;
            case SimEventType.RoundEnded:
                _roomPending = null;
                if (_pacing.EndWhistle)
                {
                    _audio?.Round(Sfx.WhistleTriple);
                }

                if (OutcomeLines(_sim.Match?.OutcomeFor(_player.Team) ?? RoundOutcome.None) is { } outcome)
                {
                    Say(outcome, always: true);
                }

                break;
            case SimEventType.PlayerEliminated when e.TargetId == _player.Id:
                Say(_lines.YoureOut, always: true);
                break;
            case SimEventType.HoldChanged:
                // Called once it has settled (_Process): a doorway fought over flips it back and forth every few seconds.
                _roomPending = (HoldStatus)e.Extra;
                _roomSince = _now;
                break;
        }
    }

    public override void _Process(double delta)
    {
        _now += delta;
        if (_sim?.Match is { Phase: MatchPhase.Live } match)
        {
            if (!_oneMinute && match.TimeLeft <= 60f && match.Setup.TimeLimit > 90f)
            {
                _oneMinute = true;
                if (_pacing.OneMinute)
                {
                    Say(_lines.OneMinute);
                }
            }

            if (!_thirty && match.TimeLeft <= 30f && match.Setup.TimeLimit > 45f)
            {
                _thirty = true;
                if (_pacing.ThirtySeconds && Say(_lines.ThirtySeconds, 0.6))
                {
                    _audio?.Round(Sfx.WhistlePips);
                }
            }

            CallRoom(match);
        }

        if (_queue.Count > 0 && _now >= _busyUntil && _now >= _queue.Peek().At)
        {
            (string line, _) = _queue.Dequeue();
            _hud.RefereeSubtitle(line);
            bool voiced = _audio?.Referee(line) == true;
            _busyUntil = _now + (voiced ? _audio!.RefereeLength + 0.15 : SilentLine);
        }
    }

    /// <summary>How the round went, from your side.</summary>
    private string[]? OutcomeLines(RoundOutcome outcome) => outcome switch
    {
        RoundOutcome.Extracted => _lines.CaseOut,
        RoundOutcome.Held => _lines.RoomHeld,
        RoundOutcome.Cleared => _sim.Match?.Setup.Mode == MatchModeKind.FreeForAll ? _lines.LastStanding : _lines.Won,
        RoundOutcome.Traded => _lines.Won,
        RoundOutcome.HeldOff => _lines.Won,
        RoundOutcome.Eliminated or RoundOutcome.CaseLost or RoundOutcome.RoomLost => _lines.Lost,
        RoundOutcome.TimeUp => _lines.TimeUp,
        _ => null,
    };

    /// <summary>
    /// The room changing hands, once it's stayed so for the pacing's settle time, and not the same call again within its
    /// repeat time: "Room taken. Hold it!" when your side (the side playing for it) has it alone, "contested" when both do.
    /// </summary>
    private void CallRoom(MatchState match)
    {
        if (_roomPending is not { } status || _now - _roomSince < _pacing.RoomSettle_s)
        {
            return;
        }

        _roomPending = null;
        if (match.Objective?.Status != status || (_roomSaid.TryGetValue(status, out double said) && _now - said < _pacing.RoomRepeat_s))
        {
            return; // it changed again meanwhile, or this was called a moment ago
        }

        string[]? lines = status switch { HoldStatus.Ours => _lines.RoomTaken, HoldStatus.Contested => _lines.RoomContested, _ => null };
        if (lines is not null && Say(lines))
        {
            _roomSaid[status] = _now;
        }
    }

    /// <summary>
    /// Queues one of <paramref name="lines"/>; an everyday line (not <paramref name="always"/>) due within the pacing's gap
    /// of the last line isn't said. True when it was queued.
    /// </summary>
    private bool Say(string[] lines, double delay = 0, bool always = false)
    {
        double at = _now + delay;
        if (!always && at - _lastLine < _pacing.Gap_s)
        {
            return false;
        }

        string line = lines[_pick++ % lines.Length];
        Called.Add(line);
        _queue.Enqueue((line, at));
        _lastLine = at;
        return true;
    }
}
