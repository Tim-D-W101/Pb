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
    /// <summary>How close to a door leaf a body's middle may come (headless; less than the body's radius, so a bot slips past an open leaf).</summary>
    private const float LeafClearance = 0.1f;

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

        // Off a ladder the grid keeps the feet on a floor; on one, they're on the floor only at its foot.
        bool grounded = true;
        if (state.OnLadder)
        {
            int under = _grid.SpanAt(state.Position);
            grounded = under >= 0 && state.Position.Y - _grid.PositionOf(under).Y <= 0.05f;
        }

        MovementResult result = MovementModel.Step(state, cmd, _sim.Config.Movement, dt, grounded, _sim.Collision, _sim.Ladders);
        if (result.Climbing)
        {
            // Straight along the ladder: nothing on the grid holds a climber.
            var climb = new Vector3(result.HorizontalVelocity.X, result.ClimbVelocity, result.HorizontalVelocity.Z);
            state.Position += climb * dt;
            state.Velocity = climb;
            state.Grounded = false;
            return;
        }

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

        // A door leaf stops you as a wall would (the grid knows doorways only as open).
        if (_sim.Doors.Count > 0 && _sim.Doors.Blocks(state.Position, to with { Y = state.Position.Y }, LeafClearance))
        {
            return false;
        }

        state.Position = to with { Y = _grid.PositionOf(there).Y };
        return true;
    }
}
