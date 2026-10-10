using System;
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
/// The referee (presentation.jsonc hud.referee): the breakout horn and "Game on!" when the round goes live, a call a
/// minute and thirty seconds from time (two pips of the whistle with the second), the whistle and how it went when the
/// round ends, a call when you're out, and in a hold when your side has the room to itself or it's contested; in
/// speedball a pip on each second of the countdown to the horn, and at a point's end the buzzer and whose point it is.
/// Each line is a subtitle and, once it's been recorded, the referee's voice; lines wait for the one before to finish.
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
    private Hud _hud = null!;
    private AudioDirector? _audio;
    private bool _oneMinute;
    private bool _thirty;
    private int _pipAt;
    private int _pick;
    private double _now;
    private double _busyUntil;

    /// <summary>Every line called so far, in order (for the smoke tests).</summary>
    public List<string> Called { get; } = new();

    public void Initialize(SimWorld sim, PlayerState player, RefereeDef lines, Hud hud, AudioDirector? audio)
    {
        _sim = sim;
        _player = player;
        _lines = lines;
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
                _audio?.Round(Sfx.Horn);
                Say(_lines.Start, AfterHorn);
                break;
            case SimEventType.RoundEnded:
                _audio?.Round(Sfx.WhistleTriple);
                if (_sim.Match is { Setup.Format: MatchFormat.Speedball } point)
                {
                    // A point of a speedball match: the buzzer, then whose point it is.
                    if (point.Result.Reason == RoundEnd.Hung)
                    {
                        Say(_lines.Buzzer);
                    }

                    Say(point.Result.Winner == _player.Team ? _lines.PointWon : point.Result.Winner >= 0 ? _lines.PointLost : _lines.NoPoint);
                }
                else if (OutcomeLines(_sim.Match?.OutcomeFor(_player.Team) ?? RoundOutcome.None) is { } outcome)
                {
                    Say(outcome);
                }

                break;
            case SimEventType.PlayerEliminated when e.TargetId == _player.Id:
                Say(_lines.YoureOut);
                break;
            case SimEventType.HoldChanged when (HoldStatus)e.Extra == HoldStatus.Ours:
                Say(_lines.RoomTaken);
                break;
            case SimEventType.HoldChanged when (HoldStatus)e.Extra == HoldStatus.Contested:
                Say(_lines.RoomContested);
                break;
        }
    }

    public override void _Process(double delta)
    {
        _now += delta;
        // Speedball's countdown: a pip on each of its last seconds, then the horn as it goes live.
        if (_sim?.Match is { Phase: MatchPhase.Countdown } counting)
        {
            int second = (int)MathF.Ceiling(counting.CountdownLeft);
            if (second != _pipAt && second > 0)
            {
                _pipAt = second;
                _audio?.Round(Sfx.CountdownPip);
            }
        }

        if (_sim?.Match is { Phase: MatchPhase.Live } match)
        {
            if (!_oneMinute && match.TimeLeft <= 60f && match.Setup.TimeLimit > 90f)
            {
                _oneMinute = true;
                Say(_lines.OneMinute);
            }

            if (!_thirty && match.TimeLeft <= 30f && match.Setup.TimeLimit > 45f)
            {
                _thirty = true;
                _audio?.Round(Sfx.WhistlePips);
                Say(_lines.ThirtySeconds, 0.6);
            }
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

    private void Say(string[] lines, double delay = 0)
    {
        string line = lines[_pick++ % lines.Length];
        Called.Add(line);
        _queue.Enqueue((line, _now + delay));
    }
}
