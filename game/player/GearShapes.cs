using System.Collections.Generic;
using Godot;
using Pb.Game.World;
using Pb.Sim.Collision;

namespace Pb.Game.Player;

/// <summary>
/// An opponent's gear, drawn beside a character model: the marker built in code (<see cref="MarkerShape"/>)
/// split into its three groups, each fitted into its own hitbox (forward is −Z), so the receiver and
/// barrel fill the marker's box, the loader its box and the bottle the tank's. The boxes are still what
/// you can hit. The loader holds paint in the player's team colour.
/// </summary>
public static class GearShapes
{
    private static readonly Dictionary<MarkerPart, Material> Shared = new()
    {
        [MarkerPart.Body] = Standard(new Color(0.11f, 0.115f, 0.12f), 0.42f, 0.35f),
        [MarkerPart.Barrel] = Standard(new Color(0.05f, 0.05f, 0.055f), 0.3f, 0.45f),
        [MarkerPart.Rubber] = Standard(new Color(0.045f, 0.045f, 0.05f), 0.85f, 0f),
        [MarkerPart.Trim] = Standard(new Color(0.5f, 0.51f, 0.53f), 0.35f, 0.85f),
        [MarkerPart.Tank] = Standard(new Color(0.78f, 0.79f, 0.81f), 0.22f, 0.95f),
        [MarkerPart.Lid] = Standard(new Color(0.12f, 0.13f, 0.14f), 0.3f, 0f),
        [MarkerPart.Shell] = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.16f, 0.17f, 0.19f, 0.62f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.12f,
        },
    };

    public static void Build(Node3D part, HitboxPart kind, Vector3 size, Color paint, int seed)
    {
        MarkerGroup group = kind switch
        {
            HitboxPart.Loader => MarkerGroup.Loader,
            HitboxPart.Tank => MarkerGroup.Tank,
            _ => MarkerGroup.Marker,
        };

        // The marker is scaled to the length of its box; the loader and bottle to fit inside theirs.
        (Vector3 center, Vector3 nominal) = MarkerShape.NominalBox(group);
        float scale = group == MarkerGroup.Marker
            ? size.Z / nominal.Z
            : Mathf.Min(size.X / nominal.X, Mathf.Min(size.Y / nominal.Y, size.Z / nominal.Z));
        var shape = new ShapeMesh();
        shape.Place(new Transform3D(Basis.Identity.Scaled(Vector3.One * scale), -center * scale), size.Y);
        MarkerShape.Build(shape, group, seed);
        var mesh = new ArrayMesh();
        Material paintMaterial = Standard(paint, 0.25f, 0f);
        shape.Commit(mesh, p => (MarkerPart)p == MarkerPart.Paint ? paintMaterial : Shared[(MarkerPart)p]);
        part.AddChild(new MeshInstance3D { Name = group.ToString(), Mesh = mesh });
    }

    private static StandardMaterial3D Standard(Color color, float roughness, float metallic) =>
        new() { AlbedoColor = color, Roughness = roughness, Metallic = metallic };
}
