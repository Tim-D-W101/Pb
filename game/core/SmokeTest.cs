using Godot;
using Pb.Game.Ballistics;
using Pb.Sim;
using Pb.Sim.Events;

namespace Pb.Game.Core;

/// <summary>
/// Headless end-to-end check run by CI (<c>-- --smoke-test</c>): the autopilot fires at targets
/// while stress mode sustains ~1,000 balls, through the real scene, nodes and presentation code.
/// Halfway through it hot-reloads every data file (the F9 path), and for the last quarter the world's
/// paint goes on cards (the fallback when decals cost too much). It exits 0 only if balls flew,
/// broke, bounced, hit targets and left splats, as decals and as cards, with no errors.
/// </summary>
public sealed class SmokeTest
{
    private readonly Node _host;
    private readonly SimWorld _sim;
    private readonly SimDriver _driver;
    private readonly SplatSystem _splats;
    private readonly int _ticks;
    private readonly System.Action _reloadData;
    private int _elapsed;
    private int _maxLive;
    private int _shots;
    private int _breaks;
    private int _bounces;
    private int _targetHits;
    private int _decals;
    private int _moved;

    public SmokeTest(Node host, SimWorld sim, SimDriver driver, SplatSystem splats, int ticks, System.Action reloadData)
    {
        _host = host;
        _reloadData = reloadData;
        _sim = sim;
        _driver = driver;
        _splats = splats;
        _ticks = ticks;
        driver.Ticked += _ => AfterTick();
        GD.Print($"SMOKE start: {ticks} ticks at {sim.Config.TickRate} Hz, stress target {sim.Stress?.TargetLiveBalls}");
    }

    public void OnSimEvent(in SimEvent e)
    {
        switch (e.Type)
        {
            case SimEventType.ShotFired when e.PlayerId >= 0:
                _shots++;
                break;
            case SimEventType.BallBroke:
                _breaks++;
                break;
            case SimEventType.BallBounced:
                _bounces++;
                break;
            case SimEventType.TargetHit:
                _targetHits++;
                break;
        }
    }

    private void AfterTick()
    {
        _maxLive = System.Math.Max(_maxLive, _sim.Ballistics.Pool.Count);
        if (++_elapsed == _ticks / 2)
        {
            _reloadData(); // exercises F9 hot reload mid-run
        }

        if (_elapsed == _ticks * 3 / 4)
        {
            // The decals so far move to cards, and the paint after them lands as cards.
            _decals = _splats.DecalCount;
            _splats.Cards = true;
            _moved = _splats.CardCount;
        }

        if (_elapsed < _ticks)
        {
            return;
        }

        int target = _sim.Stress?.TargetLiveBalls ?? 0;
        bool ok = _maxLive >= target * 0.9 && _shots > 0 && _breaks > 0 && _bounces > 0 && _targetHits > 0 &&
                  _decals > 0 && _moved > 0 && _splats.CardCount > _moved && _driver.ErrorCount == 0;
        GD.Print($"SMOKE {(ok ? "PASS" : "FAIL")}: ticks={_elapsed} maxLiveBalls={_maxLive}/{target} playerShots={_shots} " +
                 $"breaks={_breaks} bounces={_bounces} targetHits={_targetHits} splats={_decals} (moved to {_moved} cards, now {_splats.CardCount}; " +
                 $"{_splats.CardsSkipped} not flat) " +
                 $"simErrors={_driver.ErrorCount} avgStepMs={_driver.AverageStepMs:0.000}");
        _host.GetTree().Quit(ok ? 0 : 1);
    }
}
