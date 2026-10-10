using System;
using System.Collections.Generic;

namespace Pb.Game.Player;

/// <summary>
/// The jerseys' and pants' patterns (a jersey or pants item's <c>shape</c> in gear/catalog.jsonc), drawn over each
/// character's own clothes by character.gdshader in the item's three colours: <c>own</c> keeps the clothes as they
/// are (recoloured by the main colour unless it's white), the rest lay a pattern over them.
/// </summary>
public static class ClothesPatterns
{
    /// <summary>Every pattern, in the order character.gdshader numbers them.</summary>
    public static readonly string[] Names = { "own", "yoke", "panels", "camo", "patches", "stripe" };

    /// <summary>The shader's number for <paramref name="name"/> (0, own, for one it doesn't know).</summary>
    public static int Index(string name) => Math.Max(0, Array.IndexOf(Names, name));

    public static IReadOnlyList<string> All => Names;
}
