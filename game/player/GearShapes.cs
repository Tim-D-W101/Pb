using Godot;
using Pb.Sim.Collision;

namespace Pb.Game.Player;

/// <summary>
/// Shapes for an opponent's gear, fitted inside its hitboxes (forward is −Z): the marker as a body,
/// barrel and grip, like the first-person one; the loader as a hopper; the tank as a bottle. They're
/// drawn beside a character model in place of the boxes, and the boxes are still what you can hit.
/// </summary>
public static class GearShapes
{
    public static void Build(Node3D part, HitboxPart kind, Vector3 size, StandardMaterial3D material)
    {
        switch (kind)
        {
            case HitboxPart.Marker:
                var dark = (StandardMaterial3D)material.Duplicate();
                dark.AlbedoColor = material.AlbedoColor.Darkened(0.45f);
                // Body along the back half, barrel out of its front, grip raked back under the trigger hand.
                Add(part, new BoxMesh { Size = new Vector3(size.X, 0.42f * size.Y, 0.47f * size.Z) }, material,
                    new Vector3(0f, 0.25f * size.Y, 0.22f * size.Z));
                Add(part, new CylinderMesh { TopRadius = 0.26f * size.X, BottomRadius = 0.26f * size.X, Height = 0.53f * size.Z, RadialSegments = 12 }, dark,
                    new Vector3(0f, 0.29f * size.Y, -0.235f * size.Z), new Vector3(Mathf.Pi / 2f, 0f, 0f));
                Add(part, new BoxMesh { Size = new Vector3(0.6f * size.X, 0.6f * size.Y, 0.065f * size.Z) }, dark,
                    new Vector3(0f, -0.2f * size.Y, 0.2f * size.Z), new Vector3(-0.26f, 0f, 0f));
                break;
            case HitboxPart.Loader:
                Add(part, new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 16, Rings = 8 }, material,
                    Vector3.Zero, Vector3.Zero, size * 0.96f);
                break;
            case HitboxPart.Tank:
                Add(part, new CapsuleMesh { Radius = 0.5f * Mathf.Min(size.X, size.Y), Height = size.Z, RadialSegments = 14, Rings = 4 }, material,
                    Vector3.Zero, new Vector3(Mathf.Pi / 2f, 0f, 0f));
                break;
        }
    }

    private static void Add(Node3D parent, Mesh mesh, Material material, Vector3 position, Vector3 rotation = default, Vector3? scale = null) =>
        parent.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = position, Rotation = rotation, Scale = scale ?? Vector3.One });
}
