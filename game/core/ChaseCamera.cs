using System;
using Godot;

namespace Pb.Game.Core;

/// <summary>
/// A camera that follows something from behind, above and to one side (the vehicle demo's <c>--chase</c>, for captures of
/// a buggy as it drives: its lean, its wheels on their springs, what its tyres throw up, its driver at the wheel).
/// </summary>
public partial class ChaseCamera : Camera3D
{
    /// <summary>What it follows, as drawn this frame.</summary>
    public Func<Transform3D>? Target { get; set; }

    /// <summary>Where it sits from what it follows: so far behind, above and to the right (m), in its heading's frame.</summary>
    public Vector3 Offset { get; set; } = new(2.8f, 2.4f, 6.5f);

    private bool _placed;

    public override void _Process(double delta)
    {
        if (Target is null)
        {
            return;
        }

        Transform3D target = Target();
        Vector3 f = -target.Basis.Z;
        var heading = new Basis(Vector3.Up, Mathf.Atan2(-f.X, -f.Z));
        Vector3 want = target.Origin + heading * Offset;
        float ease = _placed ? 1f - Mathf.Exp(-(float)delta / 0.35f) : 1f;
        _placed = true;
        GlobalPosition = GlobalPosition.Lerp(want, ease);
        LookAt(target.Origin + Vector3.Up * 0.9f, Vector3.Up);
    }
}
