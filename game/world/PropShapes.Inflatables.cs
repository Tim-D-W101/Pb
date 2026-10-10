using System;
using Godot;
using Pb.Sim.Level;

namespace Pb.Game.World;

/// <summary>
/// The Sports Ground's props. Its inflatable bunkers (kit/props.jsonc shape "inflatable"): each collider drawn as an air-filled one
/// of its shape (game/world/RangeShapes.cs: a can, a brick, a wedge or a lying tube), in its material's nylon, with seams
/// and a band in the club's trim, pegged down where it stands on the ground. A piece standing on another (a temple's
/// roof) has no pegs. They keep to their colliders, so paint lands on what you see. And the pits' pop-up gazebo ("gazebo":
/// its legs at the collider's corners, a peaked canopy over its top in the collider's nylon and a valance round it in the trim).
/// </summary>
public static partial class PropShapes
{
    /// <summary>The kit material of the club's trim on a bunker of each fabric (the other club colour, or the white).</summary>
    private static string Trim(string fabric) => fabric switch
    {
        "nylon_navy" => "nylon_yellow",
        "nylon_yellow" => "nylon_navy",
        _ => "nylon_white",
    };

    private static void Inflatable(Ctx c)
    {
        for (int i = 0; i < c.Count; i++)
        {
            PropColliderTemplate collider = c.Type.Colliders[i];
            int fabric = collider.Material.Index;
            int trim = c.Mat(Trim(c.Type.Colliders[i].MaterialId));
            var mat = new RangeShapes.InflatableMaterials(fabric, trim, c.Mat("strap_black"));
            Vector3 size = c.Size(i);
            Basis rot = c.Rot(i);
            // Built standing on its own foot (y = 0) and placed so its middle is the collider's.
            float halfUp = collider.Kind == PrimitiveKind.Capsule ? collider.HalfExtents.X : collider.HalfExtents.Y;
            Vector3 foot = c.Center(i) - rot.Y * halfUp;
            bool grounded = foot.Y < 0.05f;
            var part = new ShapeMesh();
            switch (collider.Kind)
            {
                case PrimitiveKind.Cylinder:
                    RangeShapes.Can(part, collider.HalfExtents.X, collider.HalfExtents.Y * 2f, mat, grounded);
                    break;
                case PrimitiveKind.Wedge:
                    RangeShapes.Wedge(part, size, mat, grounded);
                    break;
                case PrimitiveKind.Capsule:
                    RangeShapes.Tube(part, collider.HalfExtents.X, collider.HalfExtents.Z * 2f, mat, trim, grounded);
                    break;
                default:
                    RangeShapes.Brick(part, size, mat, grounded);
                    break;
            }

            c.M.Absorb(part, new Transform3D(rot, foot), m => m);
        }
    }

    /// <summary>A pop-up gazebo: legs at the corners of its canopy's box, the canopy peaked over it, a valance round its edge.</summary>
    private static void Gazebo(Ctx c)
    {
        int canopy = c.Default, trim = c.Mat(Trim(c.Type.Colliders[0].MaterialId)), legs = c.Mat("steel_galvanised");
        Vector3 size = c.Size(), top = c.Center() + Vector3.Up * (size.Y * 0.5f);
        float hx = size.X * 0.5f, hz = size.Z * 0.5f, eaves = top.Y - size.Y, peak = top.Y;
        var corners = new[] { new Vector3(-hx, eaves, -hz), new Vector3(hx, eaves, -hz), new Vector3(hx, eaves, hz), new Vector3(-hx, eaves, hz) };
        var apex = new Vector3(0f, peak, 0f);
        for (int k = 0; k < 4; k++)
        {
            Vector3 a = corners[k], b = corners[(k + 1) % 4];
            // The roof's face, and its underside, and the valance hanging from its edge.
            Vector3 n = (b - a).Cross(apex - a).Normalized();
            if (n.Y < 0f)
            {
                n = -n;
            }

            c.M.Tri(canopy, a, apex, b, n, Vector2.Zero, new Vector2(0.5f, 1f), Vector2.Right);
            c.M.Tri(canopy, a, b, apex, -n, Vector2.Zero, Vector2.Right, new Vector2(0.5f, 1f));
            Vector3 outward = new Vector3(n.X, 0f, n.Z).Normalized();
            float drop = 0.25f;
            c.M.Quad(trim, a, b, b - Vector3.Up * drop, a - Vector3.Up * drop, outward, Vector2.Zero, Vector2.Right, Vector2.One, Vector2.Down);
            c.M.Quad(trim, b, a, a - Vector3.Up * drop, b - Vector3.Up * drop, -outward, Vector2.Zero, Vector2.Right, Vector2.One, Vector2.Down);
            // A leg down from each corner to a weighted foot.
            var foot = new Vector3(a.X, 0f, a.Z);
            c.M.Rod(legs, foot, a, 0.022f, 6);
            c.M.Box(legs, foot + Vector3.Up * 0.03f, new Vector3(0.22f, 0.06f, 0.22f));
            c.M.Rod(legs, a - Vector3.Up * 0.6f + (new Vector3(-a.X, 0f, 0f).Normalized() * 0.5f), a, 0.012f, 4);
        }
    }
}
