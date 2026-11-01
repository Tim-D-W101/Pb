using System;
using Godot;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Players;

namespace Pb.Game.Ballistics;

/// <summary>
/// Watches the balls in flight after every sim step for ones passing close to your head (<see cref="FlybyScan"/>), and
/// tells whoever listens (the whizz, the incoming-fire arc). Presentation only: it reads the sim's ball pool and changes
/// nothing. Not once you're out.
/// </summary>
public partial class FlybyWatch : Node
{
    private readonly Flyby[] _found = new Flyby[8];
    private SimWorld _sim = null!;
    private PlayerState _player = null!;
    private FlybyScan _scan = null!;

    /// <summary>A ball passed close by your head this tick.</summary>
    public event Action<Flyby>? Passed;

    /// <summary>How many have, so far.</summary>
    public int Count { get; private set; }

    public float Radius => _scan.Radius;

    public void Initialize(SimWorld sim, PlayerState player, FeedbackDef def, SimDriver driver)
    {
        _sim = sim;
        _player = player;
        _scan = new FlybyScan(def.FlybyRadius_m, def.FlybyMinSpeed_mps);
        driver.Ticked += _ => Scan();
    }

    private void Scan()
    {
        if (_player is not { Alive: true })
        {
            return;
        }

        int n = _scan.Scan(_sim.Ballistics.Pool, _player.EyePosition, _player.Id, _found);
        for (int i = 0; i < n; i++)
        {
            Count++;
            Passed?.Invoke(_found[i]);
        }
    }
}
