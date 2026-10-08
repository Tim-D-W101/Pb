using Godot;
using Pb.Game.Core;
using Pb.Sim.Level;

namespace Pb.Game.Player;

/// <summary>
/// Where a climber's hands and feet are on a ladder, for drawing them (the sim knows only the body): the balls of the
/// feet on rungs at or below the feet, the hands on rungs from about the shoulders up, the left foot and right hand
/// stepping together two rungs at a time as the body climbs, then the right foot and left hand, each swung out from the
/// ladder on its way. Rungs are every 0.3 m, as the ladders are drawn (game/world/PropShapes.cs).
/// </summary>
public sealed class LadderLimbs
{
    private const float Rung = 0.3f;

    // Left foot, right foot, left hand, right hand.
    private readonly Limb[] _limbs = { new(), new(), new(), new() };
    private int _ladder = -1;

    public Vector3 LeftFoot => _limbs[0].At;

    public Vector3 RightFoot => _limbs[1].At;

    public Vector3 LeftHand => _limbs[2].At;

    public Vector3 RightHand => _limbs[3].At;

    /// <summary>Moves on a frame, the climber's feet at <paramref name="feet"/> on ladder <paramref name="index"/>.</summary>
    public void Update(LadderSpec ladder, int index, Vector3 feet, float move, float swing, float delta)
    {
        Vector3 foot = ladder.Foot.ToGodot(), into = ladder.Forward.ToGodot(), across = ladder.Across.ToGodot();
        float cycle = (feet.Y - foot.Y) / (2f * Rung);
        float highest = ladder.Height + ladder.Rails - 0.1f;
        float footSide = 0.1f, handSide = ladder.Width * 0.5f - 0.06f;
        Vector3 On(float y, float side) => foot + Vector3.Up * Mathf.Clamp(y, 0f, highest) + across * side - into * 0.03f;
        Vector3[] targets =
        {
            On(2f * Rung * Mathf.Floor(cycle), -footSide),
            On(2f * Rung * Mathf.Floor(cycle + 0.5f) - Rung, footSide),
            On(2f * Rung * Mathf.Floor(cycle + 0.5f) + 5f * Rung, -handSide),
            On(2f * Rung * Mathf.Floor(cycle) + 6f * Rung, handSide),
        };

        bool fresh = index != _ladder;
        _ladder = index;
        for (int i = 0; i < _limbs.Length; i++)
        {
            _limbs[i].Update(targets[i], fresh, -into * swing, delta / Mathf.Max(move, 1e-3f));
        }
    }

    /// <summary>Off the ladder: the next one starts with every hand and foot where it belongs.</summary>
    public void Leave() => _ladder = -1;

    /// <summary>One hand or foot: on its rung, or on its way to the next.</summary>
    private sealed class Limb
    {
        private Vector3 _from;
        private Vector3 _to;
        private float _progress = 1f;

        public Vector3 At { get; private set; }

        public void Update(Vector3 target, bool snap, Vector3 swing, float step)
        {
            if (snap)
            {
                _from = _to = At = target;
                _progress = 1f;
                return;
            }

            if (!target.IsEqualApprox(_to))
            {
                _from = At;
                _to = target;
                _progress = 0f;
            }

            _progress = Mathf.Min(1f, _progress + step);
            float t = Mathf.SmoothStep(0f, 1f, _progress);
            At = _from.Lerp(_to, t) + swing * Mathf.Sin(Mathf.Pi * _progress);
        }
    }
}
