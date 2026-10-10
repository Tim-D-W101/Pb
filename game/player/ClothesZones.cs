using System;
using System.Collections.Generic;
using Godot;

namespace Pb.Game.Player;

/// <summary>
/// Each character model's surface sorted by what it is (Phase 5): every vertex falls in a zone by the bones that move
/// it (legs and hips are pants, spine and arms the jersey, neck and head the head, hands the gloves, feet the boots),
/// and the head's front is marked as the face, which a brand's mask hides. The model's mesh is built again once, with
/// the zones as vertex colours (pants, jersey, head, face) and each vertex's rest position (metres, the model's frame)
/// in CUSTOM0 (its rest normal in CUSTOM1), for character.gdshader to recolour the clothes and lay their patterns in a frame that moves with the
/// body; its levels of detail are kept. The body's landmarks (joints in the rest pose) and the head's reach in each
/// direction come with it, for the patterns and for fitting masks.
/// </summary>
public sealed class ClothesZones
{
    /// <summary>The generator's rig: which zone each bone's vertices belong to.</summary>
    private static readonly Dictionary<string, Zone> ByBone = new(StringComparer.Ordinal)
    {
        ["Hips"] = Zone.Pants, ["LeftUpLeg"] = Zone.Pants, ["LeftLeg"] = Zone.Pants, ["RightUpLeg"] = Zone.Pants, ["RightLeg"] = Zone.Pants,
        ["LeftFoot"] = Zone.Boots, ["LeftToeBase"] = Zone.Boots, ["RightFoot"] = Zone.Boots, ["RightToeBase"] = Zone.Boots,
        ["Spine"] = Zone.Jersey, ["Spine01"] = Zone.Jersey, ["Spine02"] = Zone.Jersey, ["LeftShoulder"] = Zone.Jersey, ["LeftArm"] = Zone.Jersey,
        ["LeftForeArm"] = Zone.Jersey, ["RightShoulder"] = Zone.Jersey, ["RightArm"] = Zone.Jersey, ["RightForeArm"] = Zone.Jersey,
        ["LeftHand"] = Zone.Gloves, ["RightHand"] = Zone.Gloves,
        ["neck"] = Zone.Head, ["Head"] = Zone.Head, ["head_end"] = Zone.Head, ["headfront"] = Zone.Head,
    };

    private static readonly Dictionary<string, ClothesZones?> Measured = new(StringComparer.Ordinal);

    public enum Zone
    {
        Pants,
        Jersey,
        Head,
        Gloves,
        Boots,
    }

    private ClothesZones(ArrayMesh mesh)
    {
        Mesh = mesh;
    }

    /// <summary>The model's mesh again, with its zones and rest positions in it.</summary>
    public ArrayMesh Mesh { get; }

    /// <summary>How many vertices fell in each zone (by <see cref="Zone"/>).</summary>
    public int[] Counts { get; } = new int[5];

    /// <summary>The picture's mean brightness (linear) over the jersey and over the pants: what recolouring keeps relative to.</summary>
    public float JerseyBrightness { get; private set; } = 0.05f;

    public float PantsBrightness { get; private set; } = 0.05f;

    /// <summary>Joints in the rest pose (metres, the mesh's frame: +X the model's left, +Y up from its feet, +Z its front).</summary>
    public Dictionary<string, Vector3> Joints { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The head's frame (the Head joint, its axes: right, up and back, from headfront and head_end) in the mesh's frame,
    /// and how far the head reaches along each axis (min, max), from its vertices.
    /// </summary>
    public Transform3D HeadFrame { get; private set; } = Transform3D.Identity;

    public Vector3 HeadMin { get; private set; }

    public Vector3 HeadMax { get; private set; }

    /// <summary>Where the face begins, back from the head's front (along its forward axis, in metres from the Head joint).</summary>
    public float FaceFrom { get; private set; }

    /// <summary>Where the face ends above the Head joint (m): the brow, above which the hood or hair stays.</summary>
    public float FaceBelow { get; private set; }

    /// <summary>Every vertex of the head, in the head's frame (<see cref="HeadFrame"/>): what a mask is fitted round.</summary>
    public List<Vector3> HeadPoints { get; } = new();

    /// <summary>The head's vertices that are the face (cut away under a brand's mask), in the head's frame.</summary>
    public List<Vector3> FacePoints { get; } = new();

    /// <summary>The skin's bind pose of the Head bone: the mesh's frame into the bone's (for hanging a mask on it).</summary>
    public Transform3D HeadBind { get; private set; } = Transform3D.Identity;

    /// <summary>
    /// The zones of the model at <paramref name="path"/> (measured the first time it's asked for, then shared), from its
    /// skinned mesh <paramref name="mesh"/>; null if its bones aren't the generator's rig.
    /// </summary>
    public static ClothesZones? Of(string path, MeshInstance3D mesh, FaceDef face)
    {
        if (!Measured.TryGetValue(path, out ClothesZones? zones))
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            zones = Measure(mesh, face);
            Measured[path] = zones;
            GD.Print(zones is null
                ? $"Clothes on {path.GetFile()}: not the generator's rig; drawn as it is"
                : $"Clothes on {path.GetFile()}: {zones.Counts[0]} vertices of pants, {zones.Counts[1]} of jersey, {zones.Counts[2]} of head " +
                  $"({zones.FacePoints.Count} of them the face, from {zones.FaceFrom * 100f:0} cm ahead of the head's joint), {zones.Counts[3]} of gloves and " +
                  $"{zones.Counts[4]} of boots, in {watch.Elapsed.TotalMilliseconds:0} ms");
        }

        return zones;
    }

    /// <summary>What's the face on a head (presentation.jsonc → characters → clothes): its front this deep, below this far above its joint.</summary>
    public readonly record struct FaceDef(float Depth, float Below);

    private static ClothesZones? Measure(MeshInstance3D instance, FaceDef face)
    {
        if (instance.Mesh is not ArrayMesh source || source.GetSurfaceCount() == 0 || instance.Skin is not { } skin)
        {
            return null;
        }

        // Each bind's zone, and the joints where they stand in the mesh's own frame (a bind pose takes the mesh into its bone).
        var bindZone = new Zone?[skin.GetBindCount()];
        // The face is what the head itself moves, not the neck under it.
        var bindHead = new bool[skin.GetBindCount()];
        var joints = new Dictionary<string, Vector3>(StringComparer.Ordinal);
        int headBind = -1;
        for (int b = 0; b < skin.GetBindCount(); b++)
        {
            string name = skin.GetBindName(b);
            bindZone[b] = ByBone.TryGetValue(name, out Zone zone) ? zone : null;
            bindHead[b] = name is "Head" or "head_end" or "headfront";
            joints[name] = skin.GetBindPose(b).AffineInverse().Origin;
            if (name == "Head")
            {
                headBind = b;
            }
        }

        if (headBind < 0 || !joints.ContainsKey("headfront") || !joints.ContainsKey("head_end") || !joints.ContainsKey("LeftArm"))
        {
            return null;
        }

        // The head's frame: forward towards headfront, up towards head_end.
        Vector3 head = joints["Head"];
        Vector3 forward = (joints["headfront"] - head).Normalized();
        Vector3 up = joints["head_end"] - head;
        up = (up - forward * up.Dot(forward)).Normalized();
        Vector3 back = -forward;
        var frame = new Transform3D(new Basis(up.Cross(back), up, back), head);
        // The face starts this far ahead of the Head joint: headfront marks the face's front.
        float faceFrom = (joints["headfront"] - head).Length() - face.Depth;

        Image? picture = Picture(source.SurfaceGetMaterial(0));
        var rebuilt = new ArrayMesh();
        var zones = new ClothesZones(rebuilt);
        foreach ((string name, Vector3 at) in joints)
        {
            zones.Joints[name] = at;
        }

        zones.HeadFrame = frame;
        var lum = new double[2];
        var lumCount = new int[2];
        Vector3 headMin = new(float.MaxValue, float.MaxValue, float.MaxValue), headMax = -headMin;
        for (int surface = 0; surface < source.GetSurfaceCount(); surface++)
        {
            Godot.Collections.Array arrays = source.SurfaceGetArrays(surface);
            Vector3[] vertices = arrays[(int)Godot.Mesh.ArrayType.Vertex].AsVector3Array();
            int[] bones = arrays[(int)Godot.Mesh.ArrayType.Bones].AsInt32Array();
            float[] weights = arrays[(int)Godot.Mesh.ArrayType.Weights].AsFloat32Array();
            Vector2[] uvs = arrays[(int)Godot.Mesh.ArrayType.TexUV].AsVector2Array();
            int per = vertices.Length > 0 ? bones.Length / vertices.Length : 0;
            var colours = new Color[vertices.Length];
            var rest = new float[vertices.Length * 4];
            var sums = new float[5];
            for (int v = 0; v < vertices.Length; v++)
            {
                // The zone the bones moving this vertex most belong to.
                Array.Clear(sums);
                float headWeight = 0f;
                for (int k = 0; k < per; k++)
                {
                    int bind = bones[v * per + k];
                    if (bind >= 0 && bind < bindZone.Length && bindZone[bind] is { } z)
                    {
                        sums[(int)z] += weights[v * per + k];
                        headWeight += bindHead[bind] ? weights[v * per + k] : 0f;
                    }
                }

                int best = 0;
                for (int z = 1; z < 5; z++)
                {
                    best = sums[z] > sums[best] ? z : best;
                }

                zones.Counts[best]++;
                Vector3 local = frame.AffineInverse() * vertices[v];
                // The face: the head's front (forward is −Z in its frame), from below the brow down.
                bool faceVertex = best == (int)Zone.Head && headWeight >= 0.5f && -local.Z > faceFrom && local.Y < face.Below;
                colours[v] = new Color(best == (int)Zone.Pants ? 1f : 0f, best == (int)Zone.Jersey ? 1f : 0f, best == (int)Zone.Head ? 1f : 0f, faceVertex ? 1f : 0f);
                rest[v * 4] = vertices[v].X;
                rest[v * 4 + 1] = vertices[v].Y;
                rest[v * 4 + 2] = vertices[v].Z;
                rest[v * 4 + 3] = 0f;
                if (best == (int)Zone.Head)
                {
                    headMin = headMin.Min(local);
                    headMax = headMax.Max(local);
                    zones.HeadPoints.Add(local);
                    if (faceVertex)
                    {
                        zones.FacePoints.Add(local);
                    }
                }

                if (best <= (int)Zone.Jersey && picture is not null && v < uvs.Length)
                {
                    Color c = picture.GetPixel(Mathf.Clamp((int)(uvs[v].X * picture.GetWidth()), 0, picture.GetWidth() - 1),
                        Mathf.Clamp((int)(uvs[v].Y * picture.GetHeight()), 0, picture.GetHeight() - 1)).SrgbToLinear();
                    lum[best] += 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
                    lumCount[best]++;
                }
            }

            // The rest normals beside them, for the paint to know which way the body faces where it lands.
            Vector3[] normals = arrays[(int)Godot.Mesh.ArrayType.Normal].AsVector3Array();
            var restNormals = new float[vertices.Length * 3];
            for (int v = 0; v < vertices.Length && v < normals.Length; v++)
            {
                restNormals[v * 3] = normals[v].X;
                restNormals[v * 3 + 1] = normals[v].Y;
                restNormals[v * 3 + 2] = normals[v].Z;
            }

            arrays[(int)Godot.Mesh.ArrayType.Color] = colours;
            arrays[(int)Godot.Mesh.ArrayType.Custom0] = rest;
            arrays[(int)Godot.Mesh.ArrayType.Custom1] = restNormals;
            var format = (Godot.Mesh.ArrayFormat)(((long)Godot.Mesh.ArrayCustomFormat.RgbaFloat << (int)Godot.Mesh.ArrayFormat.FormatCustom0Shift) |
                                                   ((long)Godot.Mesh.ArrayCustomFormat.RgbFloat << (int)Godot.Mesh.ArrayFormat.FormatCustom1Shift));
            if (per == 8)
            {
                format |= Godot.Mesh.ArrayFormat.FlagUse8BoneWeights;
            }

            rebuilt.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays, null, Lods(source, surface, vertices.Length), format);
            rebuilt.SurfaceSetMaterial(surface, source.SurfaceGetMaterial(surface));
        }

        rebuilt.ShadowMesh = source.ShadowMesh;
        zones.HeadMin = headMin;
        zones.HeadMax = headMax;
        zones.FaceFrom = faceFrom;
        zones.FaceBelow = face.Below;
        zones.HeadBind = skin.GetBindPose(headBind);
        zones.PantsBrightness = lumCount[0] > 0 ? Math.Max((float)(lum[0] / lumCount[0]), 0.005f) : 0.05f;
        zones.JerseyBrightness = lumCount[1] > 0 ? Math.Max((float)(lum[1] / lumCount[1]), 0.005f) : 0.05f;
        return zones;
    }

    /// <summary>The surface's levels of detail as the renderer holds them (index lists into the same vertices), to keep them.</summary>
    private static Godot.Collections.Dictionary Lods(ArrayMesh mesh, int surface, int vertexCount)
    {
        var lods = new Godot.Collections.Dictionary();
        Godot.Collections.Dictionary held = RenderingServer.MeshGetSurface(mesh.GetRid(), surface);
        if (!held.TryGetValue("lods", out Variant list) || !held.TryGetValue("index_count", out Variant count) || !held.TryGetValue("index_data", out Variant data))
        {
            return lods;
        }

        int indexBytes = (int)count > 0 ? data.AsByteArray().Length / (int)count : 2;
        foreach (Variant entry in list.AsGodotArray())
        {
            Godot.Collections.Dictionary lod = entry.AsGodotDictionary();
            byte[] bytes = lod["index_data"].AsByteArray();
            int[] indices = new int[bytes.Length / indexBytes];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = indexBytes == 2 ? BitConverter.ToUInt16(bytes, i * 2) : (int)BitConverter.ToUInt32(bytes, i * 4);
            }

            if (indices.Length > 0 && indices.Length % 3 == 0 && Array.TrueForAll(indices, x => x < vertexCount))
            {
                lods[lod["edge_length"].AsSingle()] = indices;
            }
        }

        return lods;
    }

    /// <summary>The model's picture, for its zones' brightness; null when there isn't one to read.</summary>
    private static Image? Picture(Material? material)
    {
        if (material is not BaseMaterial3D { AlbedoTexture: { } texture } || texture.GetImage() is not { } image)
        {
            return null;
        }

        if (image.IsCompressed() && image.Decompress() != Error.Ok)
        {
            return null;
        }

        // A small mipmap is plenty for a mean, and quick to read.
        if (image.GetWidth() > 256)
        {
            image.Resize(256, 256 * image.GetHeight() / image.GetWidth(), Image.Interpolation.Bilinear);
        }

        return image;
    }
}
