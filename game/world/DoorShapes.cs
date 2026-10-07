using System;
using Godot;
using Pb.Sim.Level;

namespace Pb.Game.World;

/// <summary>
/// The door leaves' meshes, built in code from a leaf's size and its kind's style (kit/doors.jsonc): panel (four sunk
/// panels in a frame), flush (plain with kick plates), steel (with a wired-glass vision panel), swing (a round vision
/// panel and push plates) and cold_room (a thick insulated leaf with strap hinges and a heavy lever). Each mesh is in
/// the leaf's own frame, centred on its box: x across from the hinge edge (−) to the free edge (+), y up, z through it.
/// Looks only: the sim's box is what paint and feet meet.
/// </summary>
public static class DoorShapes
{
    public static ArrayMesh Build(DoorSpec spec, MaterialLibrary materials)
    {
        float w = spec.Width, h = spec.Height, t = spec.Kind.Thickness;
        int leaf = spec.Kind.Material.Index;
        int metal = Find(materials, "steel_painted", leaf);
        int glass = Find(materials, "glass_dirty", metal);
        int rubber = Find(materials, "rubber_plain", metal);
        var mesh = new ShapeMesh();
        // Parts are given with y = 0 at the foot of the leaf, so the weathering ramp starts there; the mesh is centred.
        mesh.Place(new Transform3D(Basis.Identity, new Vector3(0f, -h * 0.5f, 0f)), h);
        float x0 = -w * 0.5f, x1 = w * 0.5f;

        void Slab(int material, float left, float right, float bottom, float top, float depth) =>
            mesh.Box(material, new Vector3((left + right) * 0.5f, (bottom + top) * 0.5f, 0f), new Vector3(right - left, top - bottom, depth));

        void BothFaces(int material, float cx, float cy, float sx, float sy, float depth)
        {
            float z = t * 0.5f + depth * 0.5f;
            mesh.Box(material, new Vector3(cx, cy, z), new Vector3(sx, sy, depth));
            mesh.Box(material, new Vector3(cx, cy, -z), new Vector3(sx, sy, depth));
        }

        void Lever(float y, float size)
        {
            // A rose on each face and a lever pointing back towards the hinge.
            float x = x1 - 0.075f;
            foreach (float side in new[] { 1f, -1f })
            {
                float z = side * (t * 0.5f + 0.008f);
                mesh.Cylinder(metal, new Vector3(x, y, z), Basis.FromEuler(new Vector3(Mathf.Pi * 0.5f, 0f, 0f)), 0.026f * size, 0.016f, 10);
                mesh.Box(metal, new Vector3(x - 0.055f * size, y, side * (t * 0.5f + 0.05f)), new Vector3(0.12f * size, 0.02f * size, 0.022f * size));
                mesh.Box(metal, new Vector3(x, y, side * (t * 0.5f + 0.03f)), new Vector3(0.018f * size, 0.018f * size, 0.045f));
            }
        }

        void Hinges(int count)
        {
            for (int i = 0; i < count; i++)
            {
                float y = count == 1 ? h * 0.5f : Mathf.Lerp(0.25f, h - 0.25f, i / (float)(count - 1));
                mesh.Cylinder(metal, new Vector3(x0, y, 0f), Basis.Identity, 0.012f, 0.1f, 8);
            }
        }

        switch (spec.Kind.Style)
        {
            case "panel":
            {
                float stile = MathF.Min(0.11f, w * 0.14f);
                Slab(leaf, x0, x0 + stile, 0f, h, t);
                Slab(leaf, x1 - stile, x1, 0f, h, t);
                Slab(leaf, x0 + stile, x1 - stile, 0f, 0.22f, t);
                Slab(leaf, x0 + stile, x1 - stile, h - 0.12f, h, t);
                Slab(leaf, x0 + stile, x1 - stile, 0.9f, 1.08f, t);
                // The muntin between each pair of panels, and the sunk panels themselves.
                float mid = (x0 + x1) * 0.5f;
                Slab(leaf, mid - 0.05f, mid + 0.05f, 0.22f, h - 0.12f, t);
                foreach ((float l, float r) in new[] { (x0 + stile, mid - 0.05f), (mid + 0.05f, x1 - stile) })
                {
                    Slab(leaf, l, r, 0.22f, 0.9f, t * 0.42f);
                    Slab(leaf, l, r, 1.08f, h - 0.12f, t * 0.42f);
                }

                Lever(1.0f, 1f);
                Hinges(3);
                break;
            }
            case "flush":
                Slab(leaf, x0, x1, 0f, h, t);
                BothFaces(metal, 0f, 0.11f, w - 0.06f, 0.2f, 0.003f);
                Lever(1.0f, 1f);
                Hinges(3);
                break;
            case "steel":
            {
                // The leaf round a vision panel of wired glass in its upper half.
                float gx0 = x0 + w * 0.3f, gx1 = x1 - w * 0.22f, gy0 = h * 0.58f, gy1 = h * 0.85f;
                Slab(leaf, x0, gx0, 0f, h, t);
                Slab(leaf, gx1, x1, 0f, h, t);
                Slab(leaf, gx0, gx1, 0f, gy0, t);
                Slab(leaf, gx0, gx1, gy1, h, t);
                Slab(glass, gx0, gx1, gy0, gy1, 0.008f);
                BothFaces(metal, (gx0 + gx1) * 0.5f, gy0 - 0.012f, gx1 - gx0 + 0.04f, 0.024f, 0.004f);
                BothFaces(metal, (gx0 + gx1) * 0.5f, gy1 + 0.012f, gx1 - gx0 + 0.04f, 0.024f, 0.004f);
                BothFaces(metal, 0f, 0.11f, w - 0.04f, 0.22f, 0.003f);
                Lever(1.02f, 1.1f);
                Hinges(3);
                break;
            }
            case "swing":
            {
                Slab(leaf, x0, x1, 0f, h, t);
                // A round window, ringed with a bead, at eye height.
                float r = MathF.Min(0.15f, w * 0.2f);
                Vector3 c = new(0f, 1.5f, 0f);
                mesh.Cylinder(glass, c, Basis.FromEuler(new Vector3(Mathf.Pi * 0.5f, 0f, 0f)), r, t + 0.004f, 20);
                foreach (float side in new[] { 1f, -1f })
                {
                    mesh.Cylinder(metal, c + new Vector3(0f, 0f, side * (t * 0.5f + 0.003f)), Basis.FromEuler(new Vector3(Mathf.Pi * 0.5f, 0f, 0f)),
                        r + 0.02f, 0.006f, 20, caps: false);
                }

                BothFaces(metal, x1 - 0.1f, 1.2f, 0.1f, 0.32f, 0.003f);
                BothFaces(metal, 0f, 0.13f, w - 0.04f, 0.25f, 0.003f);
                break;
            }
            case "cold_room":
            {
                Slab(leaf, x0, x1, 0f, h, t);
                // A rubber seal round the edge, two long strap hinges, a heavy lever and a kick plate.
                BothFaces(rubber, 0f, h - 0.02f, w, 0.04f, 0.006f);
                Slab(rubber, x1 - 0.02f, x1, 0f, h, t + 0.012f);
                foreach (float y in new[] { 0.35f, h - 0.35f })
                {
                    BothFaces(metal, x0 + 0.3f, y, 0.6f, 0.07f, 0.012f);
                }

                foreach (float side in new[] { 1f, -1f })
                {
                    float z = side * (t * 0.5f + 0.04f);
                    mesh.Box(metal, new Vector3(x1 - 0.12f, 1.05f, side * (t * 0.5f + 0.02f)), new Vector3(0.08f, 0.16f, 0.04f));
                    mesh.Box(metal, new Vector3(x1 - 0.3f, 1.05f, z), new Vector3(0.36f, 0.035f, 0.035f));
                }

                BothFaces(metal, 0f, 0.16f, w - 0.04f, 0.3f, 0.004f);
                break;
            }
            default:
                Slab(leaf, x0, x1, 0f, h, t);
                Lever(1.0f, 1f);
                break;
        }

        var array = new ArrayMesh();
        mesh.Commit(array, i => materials[i]);
        return array;
    }

    private static int Find(MaterialLibrary materials, string id, int fallback)
    {
        int index = materials.Find(id);
        return index >= 0 ? index : fallback;
    }
}
