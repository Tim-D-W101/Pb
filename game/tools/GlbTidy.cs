using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Godot;

namespace Pb.Game.Tools;

/// <summary>
/// Tidies a generated GLB for the game:
/// <list type="bullet">
/// <item>every embedded picture is shrunk to at most a given size and stored as JPEG;</item>
/// <item>the emissive channel goes (the generator copies the colour map into it, so the model would glow in the dark), as does its specular boost;</item>
/// <item>untextured roughness can be set (the generator's 0.4 makes cloth look like plastic);</item>
/// <item>pictures nothing uses any more are dropped;</item>
/// <item>optionally, the model is scaled to a real height and stood on the origin.</item>
/// </list>
/// </summary>
public static class GlbTidy
{
    private const uint Magic = 0x46546C67;
    private const uint JsonChunk = 0x4E4F534A;
    private const uint BinChunk = 0x004E4942;

    public static byte[] Tidy(byte[] glb, int maxTexture, float? roughness, out string report)
    {
        (JsonObject json, byte[] bin) = Read(glb);
        var notes = new List<string>();
        if (json["materials"] is JsonArray materials)
        {
            foreach (JsonObject material in materials.OfType<JsonObject>())
            {
                if (material.Remove("emissiveTexture"))
                {
                    notes.Add("emissive removed");
                }

                material.Remove("emissiveFactor");
                if (material["extensions"] is JsonObject extensions)
                {
                    extensions.Remove("KHR_materials_specular");
                    if (extensions.Count == 0)
                    {
                        material.Remove("extensions");
                    }
                }

                if (roughness is { } r && material["pbrMetallicRoughness"] is JsonObject pbr && pbr["metallicRoughnessTexture"] is null)
                {
                    pbr["roughnessFactor"] = r;
                    notes.Add($"roughness {r}");
                }
            }
        }

        if (json["extensionsUsed"] is JsonArray used)
        {
            foreach (JsonNode? node in used.Where(n => n?.GetValue<string>() == "KHR_materials_specular").ToList())
            {
                used.Remove(node);
            }
        }

        int before = glb.Length;
        byte[] tidied = Write(json, Repack(json, bin, maxTexture, notes));
        report = $"{before / 1048576f:0.0} MB → {tidied.Length / 1048576f:0.0} MB; " + string.Join(", ", notes.Distinct());
        return tidied;
    }

    /// <summary>Wraps the scene in a node that scales it by <paramref name="scale"/> and moves it by <paramref name="offset"/>.</summary>
    public static byte[] Place(byte[] glb, float scale, Vector3 offset)
    {
        (JsonObject json, byte[] bin) = Read(glb);
        var nodes = (JsonArray)json["nodes"]!;
        var scene = (JsonObject)((JsonArray)json["scenes"]!)[json["scene"]?.GetValue<int>() ?? 0]!;
        var roots = (JsonArray)scene["nodes"]!;
        var wrapper = new JsonObject
        {
            ["name"] = "Placed",
            ["children"] = roots.DeepClone(),
            ["scale"] = new JsonArray(scale, scale, scale),
            ["translation"] = new JsonArray(offset.X, offset.Y, offset.Z),
        };
        nodes.Add(wrapper);
        scene["nodes"] = new JsonArray(nodes.Count - 1);
        return Write(json, bin);
    }

    /// <summary>
    /// Copies every buffer view the model still needs into a fresh buffer, with the kept pictures
    /// re-encoded as JPEG (normal maps at a higher quality).
    /// </summary>
    private static byte[] Repack(JsonObject json, byte[] bin, int maxTexture, List<string> notes)
    {
        var textures = json["textures"] as JsonArray ?? new JsonArray();
        var images = json["images"] as JsonArray ?? new JsonArray();
        var views = (JsonArray)json["bufferViews"]!;

        // Which textures the materials still use, and which of them are normal maps.
        var usedTextures = new SortedSet<int>();
        var normalTextures = new HashSet<int>();
        void Collect(JsonNode? node, string? key)
        {
            if (node is JsonObject obj)
            {
                if (key is not null && key.EndsWith("Texture", StringComparison.Ordinal) && obj["index"] is JsonNode index)
                {
                    usedTextures.Add(index.GetValue<int>());
                    if (key == "normalTexture")
                    {
                        normalTextures.Add(index.GetValue<int>());
                    }
                }

                foreach ((string name, JsonNode? child) in obj)
                {
                    Collect(child, name);
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? child in array)
                {
                    Collect(child, null);
                }
            }
        }

        Collect(json["materials"], null);
        var textureMap = new Dictionary<int, int>();
        var imageMap = new Dictionary<int, int>();
        var newTextures = new JsonArray();
        var newImages = new JsonArray();
        var normalImages = new HashSet<int>();
        foreach (int t in usedTextures)
        {
            var texture = (JsonObject)textures[t]!.DeepClone();
            if (texture["source"] is JsonNode source)
            {
                int oldImage = source.GetValue<int>();
                if (!imageMap.TryGetValue(oldImage, out int newImage))
                {
                    newImage = newImages.Count;
                    imageMap[oldImage] = newImage;
                    newImages.Add(images[oldImage]!.DeepClone());
                }

                texture["source"] = newImage;
                if (normalTextures.Contains(t))
                {
                    normalImages.Add(newImage);
                }
            }

            textureMap[t] = newTextures.Count;
            newTextures.Add(texture);
        }

        void Remap(JsonNode? node, string? key)
        {
            if (node is JsonObject obj)
            {
                if (key is not null && key.EndsWith("Texture", StringComparison.Ordinal) && obj["index"] is JsonNode index)
                {
                    obj["index"] = textureMap[index.GetValue<int>()];
                }

                foreach ((string name, JsonNode? child) in obj.ToList())
                {
                    Remap(child, name);
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? child in array)
                {
                    Remap(child, null);
                }
            }
        }

        Remap(json["materials"], null);
        if (images.Count > newImages.Count)
        {
            notes.Add($"{images.Count - newImages.Count} unused picture(s) dropped");
        }

        // Every non-picture view keeps its bytes, in order; pictures are appended after them.
        var pictureViews = new HashSet<int>(images.OfType<JsonObject>().Where(i => i["bufferView"] is not null).Select(i => i["bufferView"]!.GetValue<int>()));
        var output = new MemoryStream();
        var newViews = new JsonArray();
        var viewMap = new Dictionary<int, int>();
        void Align()
        {
            while (output.Length % 4 != 0)
            {
                output.WriteByte(0);
            }
        }

        for (int i = 0; i < views.Count; i++)
        {
            if (pictureViews.Contains(i))
            {
                continue;
            }

            var view = (JsonObject)views[i]!.DeepClone();
            int offset = view["byteOffset"]?.GetValue<int>() ?? 0;
            int length = view["byteLength"]!.GetValue<int>();
            Align();
            view["byteOffset"] = output.Length;
            view["buffer"] = 0;
            output.Write(bin, offset, length);
            viewMap[i] = newViews.Count;
            newViews.Add(view);
        }

        foreach (JsonObject accessor in (json["accessors"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            if (accessor["bufferView"] is JsonNode bv)
            {
                accessor["bufferView"] = viewMap[bv.GetValue<int>()];
            }

            if (accessor["sparse"] is JsonObject sparse)
            {
                foreach (string part in new[] { "indices", "values" })
                {
                    if (sparse[part] is JsonObject p && p["bufferView"] is JsonNode pv)
                    {
                        p["bufferView"] = viewMap[pv.GetValue<int>()];
                    }
                }
            }
        }

        int largest = 0;
        for (int i = 0; i < newImages.Count; i++)
        {
            var image = (JsonObject)newImages[i]!;
            var oldView = (JsonObject)views[image["bufferView"]!.GetValue<int>()]!;
            int offset = oldView["byteOffset"]?.GetValue<int>() ?? 0;
            byte[] bytes = bin.AsSpan(offset, oldView["byteLength"]!.GetValue<int>()).ToArray();
            string mime = image["mimeType"]?.GetValue<string>() ?? "image/png";
            var picture = new Image();
            Error error = mime switch
            {
                "image/jpeg" => picture.LoadJpgFromBuffer(bytes),
                "image/webp" => picture.LoadWebpFromBuffer(bytes),
                _ => picture.LoadPngFromBuffer(bytes),
            };
            if (error != Error.Ok)
            {
                throw new InvalidOperationException($"can't decode picture {i} ({mime}, {error})");
            }

            largest = Math.Max(largest, Math.Max(picture.GetWidth(), picture.GetHeight()));
            if (picture.GetWidth() > maxTexture || picture.GetHeight() > maxTexture)
            {
                float k = maxTexture / (float)Math.Max(picture.GetWidth(), picture.GetHeight());
                picture.Resize(Math.Max(1, (int)(picture.GetWidth() * k)), Math.Max(1, (int)(picture.GetHeight() * k)), Image.Interpolation.Lanczos);
            }

            picture.Convert(Image.Format.Rgb8);
            byte[] jpeg = picture.SaveJpgToBuffer(normalImages.Contains(i) ? 0.95f : 0.9f);
            Align();
            image["bufferView"] = newViews.Count;
            image["mimeType"] = "image/jpeg";
            image.Remove("uri");
            newViews.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = output.Length, ["byteLength"] = jpeg.Length });
            output.Write(jpeg);
        }

        if (newImages.Count > 0)
        {
            notes.Add($"{newImages.Count} picture(s) at {Math.Min(largest, maxTexture)} px JPEG (were up to {largest} px)");
        }

        Align();
        json["bufferViews"] = newViews;
        json["textures"] = newTextures;
        json["images"] = newImages;
        json["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = output.Length });
        if (newTextures.Count == 0)
        {
            json.Remove("textures");
            json.Remove("images");
        }

        return output.ToArray();
    }

    private static (JsonObject Json, byte[] Bin) Read(byte[] glb)
    {
        if (glb.Length < 20 || BitConverter.ToUInt32(glb, 0) != Magic || BitConverter.ToUInt32(glb, 4) != 2)
        {
            throw new InvalidOperationException("not a glTF 2.0 binary (GLB) file");
        }

        int jsonLength = BitConverter.ToInt32(glb, 12);
        if (BitConverter.ToUInt32(glb, 16) != JsonChunk)
        {
            throw new InvalidOperationException("the GLB's first chunk isn't JSON");
        }

        var json = (JsonObject)JsonNode.Parse(Encoding.UTF8.GetString(glb, 20, jsonLength))!;
        int binStart = 20 + jsonLength;
        byte[] bin = Array.Empty<byte>();
        if (binStart + 8 <= glb.Length && BitConverter.ToUInt32(glb, binStart + 4) == BinChunk)
        {
            bin = glb.AsSpan(binStart + 8, BitConverter.ToInt32(glb, binStart)).ToArray();
        }

        return (json, bin);
    }

    private static byte[] Write(JsonObject json, byte[] bin)
    {
        byte[] text = Encoding.UTF8.GetBytes(json.ToJsonString());
        int jsonPadded = (text.Length + 3) & ~3;
        int binPadded = (bin.Length + 3) & ~3;
        int total = 12 + 8 + jsonPadded + (bin.Length > 0 ? 8 + binPadded : 0);
        var output = new MemoryStream(total);
        void U32(uint v) => output.Write(BitConverter.GetBytes(v));
        U32(Magic);
        U32(2);
        U32((uint)total);
        U32((uint)jsonPadded);
        U32(JsonChunk);
        output.Write(text);
        for (int i = text.Length; i < jsonPadded; i++)
        {
            output.WriteByte((byte)' ');
        }

        if (bin.Length > 0)
        {
            U32((uint)binPadded);
            U32(BinChunk);
            output.Write(bin);
            for (int i = bin.Length; i < binPadded; i++)
            {
                output.WriteByte(0);
            }
        }

        return output.ToArray();
    }
}
