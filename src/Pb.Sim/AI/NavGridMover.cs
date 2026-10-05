using System.Numerics;
using Pb.Sim.Players;

namespace Pb.Sim.AI;

/// <summary>
/// Moves a player over the navigation grid without a physics engine, for headless runs (sim tests,
/// the benchmark). The sim's movement rules give the velocity and stance, as in the game; the grid
/// keeps the feet on walkable floors (stairs included) and out of walls, sliding along them. In the
/// game Godot's collide-and-slide does this job instead.
/// </summary>
public sealed class NavGridMover
{
    private readonly NavGrid _grid;
    private readonly SimWorld _sim;

    public NavGridMover(SimWorld sim, NavGrid grid)
    {
        _sim = sim;
        _grid = grid;
    }

    public void Step(PlayerState state, in InputCommand command, float dt)
    {
        InputCommand cmd = command;
        if (!_sim.IsLive && state.Alive)
        {
            cmd.Move = Vector2.Zero;
            cmd.Buttons = InputButtons.None;
        }

        MovementResult result = MovementModel.Step(state, cmd, _sim.Config.Movement, dt, true, _sim.Collision);
        var velocity = new Vector3(result.HorizontalVelocity.X, 0f, result.HorizontalVelocity.Z);
        int here = _grid.SpanAt(state.Position);
        Vector3 moved = state.Position + velocity * dt;
        if (!TryMove(here, moved, state))
        {
            // Slide along whatever's in the way: try each axis on its own.
            bool x = TryMove(here, state.Position + new Vector3(velocity.X * dt, 0f, 0f), state);
            if (x)
            {
                velocity.Z = 0f;
            }
            else if (TryMove(here, state.Position + new Vector3(0f, 0f, velocity.Z * dt), state))
            {
                velocity.X = 0f;
            }
            else
            {
                velocity = Vector3.Zero;
            }
        }

        state.Velocity = velocity;
        state.Grounded = true;
    }

    private bool TryMove(int here, Vector3 to, PlayerState state)
    {
        int there = _grid.SpanAt(to);
        if (there < 0 || (here >= 0 && !_grid.AreLinked(here, there)))
        {
            return false;
        }

        state.Position = to with { Y = _grid.PositionOf(there).Y };
        return true;
    }
}
