using System.Collections.Generic;
using Godot;

namespace Pb.Game.Player;

/// <summary>
/// What a gear recipe reports about the item it built, in the item's own frame (+X right, +Y up, −Z forward):
/// where the hands go on a marker, where its loader and tank fit, the box it fills on a player, and where
/// the paint lies in a see-through loader.
/// </summary>
public sealed class GearBuild
{
    /// <summary>A marker: the middle of the pistol grip's top, where the trigger hand holds it (<see cref="HandShape.PistolGripTop"/> on the field's).</summary>
    public Vector3 PistolGrip { get; set; } = HandShape.PistolGripTop;

    /// <summary>A marker: the top of the foregrip, where the support hand holds it.</summary>
    public Vector3 Foregrip { get; set; } = HandShape.ForegripTop;

    /// <summary>A marker: where an opponent's wrists go on it.</summary>
    public Vector3 TriggerWrist { get; set; }

    public Vector3 SupportWrist { get; set; }

    /// <summary>A marker: the top of its feed neck, where a loader's own origin sits.</summary>
    public Vector3 Feed { get; set; }

    /// <summary>A marker: where a tank's regulator screws in (the tank's own origin; it runs back along +Z from there).</summary>
    public Vector3 TankMount { get; set; }

    /// <summary>
    /// The box the item fills (centre, size), mapped onto its hitbox on a player: a marker by its length, so the
    /// barrel reaches the box's front; anything else to fit inside. Null fits the mesh's own bounds.
    /// </summary>
    public (Vector3 Center, Vector3 Size)? Nominal { get; set; }

    /// <summary>A see-through loader: where the paintballs lie, lowest first, so drawing the first so many shows it that full.</summary>
    public List<Vector3>? Balls { get; set; }
}
