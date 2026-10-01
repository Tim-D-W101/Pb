using System;
using Godot;
using Pb.Game.Ui;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --round-tour</c>: a round's screens in order, for checking them by eye or capturing them with
/// <c>--write-movie</c>: the briefing card, the round going live, the pause menu and its settings, a
/// short duel (you eliminate an opponent, then a ball from another gets you), the spectator view and
/// the summary. Frames are counted, so the result is the same at any rendering speed.
/// </summary>
public partial class RoundTour : Node
{
    private const int BriefingFrames = 60;
    private const int PauseAt = BriefingFrames + 20;
    private const int SettingsAt = PauseAt + 22;
    private const int ResumeAt = SettingsAt + 22;
    private const int SummaryFrames = 75;
    private const int GiveUpAt = 1200;

    private Action _begin = null!;
    private Func<bool> _summaryShowing = null!;
    private PauseMenu _pause = null!;
    private int _frame;
    private int _summaryAt = -1;

    /// <param name="begin">Presses Start on the briefing card (and sets up the duel).</param>
    /// <param name="summaryShowing">Whether the summary screen is up.</param>
    public void Start(Action begin, Func<bool> summaryShowing, PauseMenu pause)
    {
        _begin = begin;
        _summaryShowing = summaryShowing;
        _pause = pause;
        // Keeps counting while the pause menu has the tree paused.
        ProcessMode = ProcessModeEnum.Always;
        GD.Print("ROUND TOUR briefing, live, pause menu, settings, duel, spectator view, summary");
    }

    public override void _Process(double delta)
    {
        if (_begin is null)
        {
            return;
        }

        switch (_frame)
        {
            case BriefingFrames:
                GD.Print($"ROUND TOUR frame {_frame}: start");
                _begin();
                break;
            case PauseAt:
                GD.Print($"ROUND TOUR frame {_frame}: pause menu");
                _pause.Toggle();
                break;
            case SettingsAt:
                GD.Print($"ROUND TOUR frame {_frame}: settings");
                _pause.ShowSettings(true);
                break;
            case ResumeAt:
                GD.Print($"ROUND TOUR frame {_frame}: resume");
                _pause.Close();
                break;
        }

        if (_summaryAt < 0 && _summaryShowing())
        {
            _summaryAt = _frame;
            GD.Print($"ROUND TOUR frame {_frame}: summary");
        }

        if ((_summaryAt >= 0 && _frame >= _summaryAt + SummaryFrames) || _frame >= GiveUpAt)
        {
            GD.Print(_summaryAt >= 0 ? $"ROUND TOUR done after {_frame} frames" : "ROUND TOUR gave up waiting for the summary");
            GetTree().Quit(_summaryAt >= 0 ? 0 : 1);
        }

        _frame++;
    }
}
