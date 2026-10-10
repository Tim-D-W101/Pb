namespace Pb.Game.Player;

/// <summary>
/// The masks built in code, one per brand but the field's (Norrel's Issue is the mask each character came with): shells
/// fitted to each character's head (<see cref="MaskShapes"/>).
/// </summary>
public static class MaskRecipes
{
    public static readonly string[] Shapes = { "mask_visor", "mask_arc", "mask_thicket", "mask_own" };

    /// <summary>Whether <paramref name="shape"/> is the character's own mask, drawn as the model has it.</summary>
    public static bool IsOwn(string shape) => shape == "mask_own";
}
