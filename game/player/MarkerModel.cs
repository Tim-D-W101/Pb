using Godot;
using Pb.Game.Core;

namespace Pb.Game.Player;

/// <summary>
/// The generated marker, loader and tank (one model, <c>presentation.jsonc</c> → <c>markerModel</c>):
/// in first person (<see cref="ViewModel"/>) and in every opponent's hands (<see cref="CharacterVisual"/>),
/// with the marker built in code (<see cref="MarkerShape"/>) as the fallback. The model's barrel points
/// along −X as imported; placed, it points down −Z, the way a marker's frame faces.
/// </summary>
public static class MarkerModel
{
    /// <summary>
    /// The model, turned so its barrel points down −Z with its muzzle at <paramref name="muzzle"/> in its
    /// parent's frame, at <paramref name="scale"/> times its real size; null when the art is missing.
    /// </summary>
    public static Node3D? Create(MarkerModelDef def, Vector3 muzzle, float scale = 1f)
    {
        if (ArtFiles.Load<PackedScene>(def.Model) is not { } scene)
        {
            return null;
        }

        Node3D model = scene.Instantiate<Node3D>();
        model.Name = "MarkerModel";
        // A quarter turn about up takes −X (the barrel) to −Z.
        Basis basis = new Basis(Vector3.Up, -Mathf.Pi / 2f).Scaled(Vector3.One * scale);
        model.Transform = new Transform3D(basis, muzzle - basis * Point(def.Muzzle_m));
        return model;
    }

    /// <summary>A point on the model (as imported, metres) in the frame <see cref="Create"/> placed it in.</summary>
    public static Vector3 Point(Node3D model, float[] point) => model.Transform * Point(point);

    private static Vector3 Point(float[] p) => new(p[0], p[1], p[2]);
}
