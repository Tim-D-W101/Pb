using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Pb.Game.Core;

namespace Pb.Game.Tools;

/// <summary>
/// Imports finished Higgsfield art into the game; <c>tools/art/import.sh</c> downloads the file and
/// runs this scene headless.
/// <list type="bullet">
/// <item><c>--texture</c>: a picture becomes a tiling material (albedo plus normal and roughness maps derived from it) in <c>art/textures/</c>.</item>
/// <item><c>--model</c>: a GLB is tidied (smaller JPEG textures, no baked glow) and goes into <c>art/models/</c>, optionally scaled to a real height; its measured size is printed so the prop's colliders can be fitted to it.</item>
/// <item><c>--selftest</c>: runs the texture steps on a generated picture and checks the result (CI).</item>
/// </list>
/// Every import records the job, prompt, generator and download URL in <c>data/assets.jsonc</c>: the
/// provenance record that the art is original.
/// </summary>
public partial class ArtImport : Node
{
    private const string Provenance = "res://data/assets.jsonc";

    private const string Header =
        "// Provenance of every imported art asset (spec: original IP only), written by tools/art/import.sh:\n" +
        "// the Higgsfield job that made it, the generator and prompt, where it was downloaded from, and the\n" +
        "// files the import produced. A sim test checks that every texture and model the kit uses is listed.\n";

    public override void _Ready()
    {
        int code;
        try
        {
            code = Args.Has("--selftest") ? SelfTest() : Args.Has("--texture") ? ImportTexture() : Args.Has("--model") ? ImportModel() : Usage();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or FormatException or InvalidOperationException or JsonException)
        {
            GD.PushError($"ART {ex.Message}");
            code = 1;
        }

        GetTree().Quit(code);
    }

    private static int Usage()
    {
        GD.PushError("ART usage: --texture|--model|--selftest --id=… --source=FILE --job=… --generator=… --prompt=… --url=… " +
                     "[texture: --size=1024 --region=x,y,w,h --stretch --repeats=across,down --flatten=0.8 --band=0.12 --normal-strength=2 --roughness=0.9 --roughness-variation=0.15] " +
                     "[model: --max-texture=1024 --roughness=R --height=M]");
        return 2;
    }

    private static int ImportTexture()
    {
        string id = Required("--id");
        int size = Number("--size", 1024);
        float band = Number("--band", 0.12f);
        float strength = Number("--normal-strength", 2f);
        float roughnessBase = Number("--roughness", 0.9f);
        float variation = Number("--roughness-variation", 0.15f);
        float flatten = Number("--flatten", 0.8f);

        Image picture = Image.LoadFromFile(Required("--source")) ?? throw new InvalidOperationException("can't read the source picture");
        picture.Convert(Image.Format.Rgb8);
        if (Args.Value("--region") is { } region)
        {
            // One tile of a picture holding several: x, y, width, height as fractions of the picture.
            float[] r = Array.ConvertAll(region.Split(','), f => float.Parse(f, CultureInfo.InvariantCulture));
            if (r.Length != 4)
            {
                throw new ArgumentException("--region=x,y,w,h takes four fractions");
            }

            int w = picture.GetWidth(), h = picture.GetHeight();
            picture = picture.GetRegion(new Rect2I((int)(r[0] * w), (int)(r[1] * h), (int)(r[2] * w), (int)(r[3] * h)));
        }

        if (!Args.Has("--stretch"))
        {
            int side = Math.Min(picture.GetWidth(), picture.GetHeight());
            picture = picture.GetRegion(new Rect2I((picture.GetWidth() - side) / 2, (picture.GetHeight() - side) / 2, side, side));
        }

        picture.Resize(size, size, Image.Interpolation.Lanczos);

        // How many times a regular pattern repeats across and down the region (bricks, courses, planks,
        // corrugations), so the seams are blended in step with it.
        int[] repeats = Args.Value("--repeats") is { } counts
            ? Array.ConvertAll(counts.Split(','), n => int.Parse(n, CultureInfo.InvariantCulture))
            : [0, 0];
        if (repeats.Length != 2)
        {
            throw new ArgumentException("--repeats=across,down takes two whole numbers");
        }

        RgbImage source = ToRgb(picture);
        RgbImage tiled = TextureMaker.MakeTileable(TextureMaker.Flatten(source, flatten, size / 8), band, repeats[0], repeats[1]);
        float[] height = TextureMaker.Height(tiled);
        RgbImage normal = TextureMaker.NormalMap(height, size, size, strength);
        float[] roughness = TextureMaker.Roughness(height, roughnessBase, variation);

        const string folder = "res://art/textures";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(folder));
        var files = new List<string>
        {
            Save(tiled, $"{folder}/{id}_albedo.jpg", 0.92f),
            Save(normal, $"{folder}/{id}_normal.jpg", 0.95f),
            Save(Grey(roughness, size, size), $"{folder}/{id}_roughness.jpg", 0.9f),
        };
        for (int i = 0; i < files.Count; i++)
        {
            WriteImportSettings(files[i], normalMap: i == 1);
        }

        Record("texture", id, files, string.Create(CultureInfo.InvariantCulture,
            $"{size} px{(Args.Value("--region") is { } cut ? $", region {cut}" : "")}{(Args.Has("--stretch") ? " stretched square" : "")}" +
            $"{(Args.Value("--repeats") is { } n ? $", pattern repeats {n}" : "")}, flatten {flatten}, seam band {band}, normal strength {strength}, roughness {roughnessBase} ± {variation}"));
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"ART texture {id}: seam ratio {TextureMaker.SeamRatio(source):0.00} → {TextureMaker.SeamRatio(tiled):0.00}. ") +
            $"In kit/materials.jsonc, set albedo, normal and roughnessMap to {string.Join(", ", files)}");
        return 0;
    }

    /// <summary>
    /// A generated GLB, tidied (see <see cref="GlbTidy"/>) and optionally scaled to <c>--height</c> metres
    /// and stood on the origin, then measured so the prop's colliders can be fitted to it.
    /// </summary>
    private static int ImportModel()
    {
        string id = Required("--id");
        string source = Required("--source");
        float? roughness = Args.Value("--roughness") is { } r ? float.Parse(r, CultureInfo.InvariantCulture) : null;
        byte[] model = GlbTidy.Tidy(File.ReadAllBytes(source), Number("--max-texture", 1024), roughness, out string tidied);
        Aabb bounds = Measure(model);
        string placed = "";
        if (Args.Value("--height") is { } h)
        {
            float height = float.Parse(h, CultureInfo.InvariantCulture);
            float scale = height / bounds.Size.Y;
            Vector3 centre = bounds.GetCenter();
            model = GlbTidy.Place(model, scale, new Vector3(-centre.X, -bounds.Position.Y, -centre.Z) * scale);
            bounds = Measure(model);
            placed = string.Create(CultureInfo.InvariantCulture, $", scaled ×{scale:0.###} to {height} m tall and stood on the origin");
        }

        const string folder = "res://art/models";
        string target = $"{folder}/{id}.glb";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(folder));
        File.WriteAllBytes(ProjectSettings.GlobalizePath(target), model);
        Vector3 size = bounds.Size, middle = bounds.GetCenter();
        string measured = string.Create(CultureInfo.InvariantCulture,
            $"{size.X:0.00} × {size.Y:0.00} × {size.Z:0.00} m (x × y × z), base at y = {bounds.Position.Y:0.00}, centre ({middle.X:0.00}, {middle.Z:0.00})");
        Record("model", id, new List<string> { target }, $"{measured}; {tidied}{placed}");
        GD.Print($"ART model {id}: {measured}; {tidied}{placed}. In kit/props.jsonc, fit the prop's colliders to that and set model to {target}");
        return 0;
    }

    /// <summary>
    /// Godot's import settings for a material texture: mipmaps (or it shimmers at a distance), VRAM
    /// compression, and the normal-map flag for normal maps. Godot fills in the rest on import.
    /// </summary>
    private static void WriteImportSettings(string path, bool normalMap)
    {
        string text = "[remap]\n\nimporter=\"texture\"\ntype=\"CompressedTexture2D\"\n\n[params]\n\n" +
                      "compress/mode=2\ncompress/high_quality=false\ncompress/lossy_quality=0.7\ncompress/hdr_compression=1\n" +
                      $"compress/normal_map={(normalMap ? 1 : 2)}\ncompress/channel_pack=0\nmipmaps/generate=true\nmipmaps/limit=-1\n" +
                      "roughness/mode=0\nroughness/src_normal=\"\"\nprocess/fix_alpha_border=true\nprocess/premult_alpha=false\n" +
                      "process/normal_map_invert_y=false\nprocess/hdr_as_srgb=false\nprocess/hdr_clamp_exposure=false\nprocess/size_limit=0\n" +
                      "detect_3d/compress_to=0\n";
        File.WriteAllText(ProjectSettings.GlobalizePath(path) + ".import", text);
    }

    private static Aabb Measure(byte[] glb)
    {
        var document = new GltfDocument();
        var state = new GltfState();
        Error error = document.AppendFromBuffer(glb, "", state);
        if (error != Error.Ok)
        {
            throw new InvalidOperationException($"can't read the model as glTF ({error})");
        }

        Node scene = document.GenerateScene(state);
        Aabb bounds = Bounds(scene, Transform3D.Identity) ?? throw new InvalidOperationException("the model has no meshes");
        scene.Free();
        return bounds;
    }

    /// <summary>The texture steps on a picture that doesn't tile, plus exact checks on flat and bumped pictures.</summary>
    private static int SelfTest()
    {
        const int size = 256;
        var noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.03f, FractalOctaves = 4, Seed = 7 };
        var picture = new RgbImage(size, size);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Noise plus a ramp each way, so the wrap-round edges jump.
                float v = Math.Clamp(0.5f + 0.35f * noise.GetNoise2D(x, y) + 0.25f * (x / (float)size - 0.5f) + 0.15f * (y / (float)size - 0.5f), 0f, 1f);
                int o = picture.Index(x, y);
                picture.Data[o] = v * 0.9f;
                picture.Data[o + 1] = v * 0.85f;
                picture.Data[o + 2] = v * 0.8f;
            }
        }

        float before = TextureMaker.SeamRatio(picture);
        float after = TextureMaker.SeamRatio(TextureMaker.MakeTileable(picture, 0.12f));

        // Flattening takes the ramps out: the outer columns end up about as bright as each other.
        float Column(RgbImage image, int x)
        {
            float sum = 0f;
            for (int y = 0; y < image.Height; y++)
            {
                sum += image.Data[image.Index(x, y)];
            }

            return sum / image.Height;
        }

        RgbImage flattened = TextureMaker.Flatten(picture, 1f, size / 8);
        float rampBefore = MathF.Abs(Column(picture, size - 8) - Column(picture, 8));
        float rampAfter = MathF.Abs(Column(flattened, size - 8) - Column(flattened, 8));

        const int small = 64;
        var flat = new RgbImage(small, small);
        Array.Fill(flat.Data, 0.5f);
        float[] flatHeight = TextureMaker.Height(flat);
        RgbImage flatNormals = TextureMaker.NormalMap(flatHeight, small, small, 2f);
        float normalError = 0f;
        for (int i = 0; i < flatNormals.Data.Length; i++)
        {
            normalError = MathF.Max(normalError, MathF.Abs(flatNormals.Data[i] - (i % 3 == 2 ? 1f : 0.5f)));
        }

        float roughnessError = TextureMaker.Roughness(flatHeight, 0.8f, 0.15f).Max(r => MathF.Abs(r - 0.8f));

        // A bright bump in the middle: its normals lean away from the top (green is up the picture).
        var bump = new float[small * small];
        for (int y = 0; y < small; y++)
        {
            for (int x = 0; x < small; x++)
            {
                float dx = x - small / 2, dy = y - small / 2;
                bump[y * small + x] = MathF.Exp(-(dx * dx + dy * dy) / 40f);
            }
        }

        RgbImage leaning = TextureMaker.NormalMap(bump, small, small, 2f);
        float R(int x, int y) => leaning.Data[leaning.Index(x, y)];
        float G(int x, int y) => leaning.Data[leaning.Index(x, y) + 1];
        int c = small / 2;
        bool bumpOk = R(c - 4, c) < 0.45f && R(c + 4, c) > 0.55f && G(c, c - 4) > 0.55f && G(c, c + 4) < 0.45f;

        // A pattern that repeats three times across and twice down already tiles: blended in step with
        // its repeats it comes through unchanged, while the plain half-picture shift muddles it.
        const int grid = 240;
        var pattern = new RgbImage(grid, grid);
        for (int y = 0; y < grid; y++)
        {
            for (int x = 0; x < grid; x++)
            {
                float v = 0.5f + 0.2f * MathF.Cos(MathF.Tau * x / 80f) + 0.2f * MathF.Cos(MathF.Tau * y / 120f);
                int o = pattern.Index(x, y);
                pattern.Data[o] = pattern.Data[o + 1] = pattern.Data[o + 2] = v;
            }
        }

        float Change(RgbImage a, RgbImage b) => a.Data.Zip(b.Data, (p, q) => MathF.Abs(p - q)).Max();
        float inStep = Change(pattern, TextureMaker.MakeTileable(pattern, 0.12f, 3, 2));
        float halfShift = Change(pattern, TextureMaker.MakeTileable(pattern, 0.12f));
        bool repeatsOk = inStep < 0.01f && halfShift > 0.1f;

        bool ok = before > 1.5f && after < 1.25f && rampAfter < rampBefore * 0.35f && normalError < 1e-3f && roughnessError < 1e-3f && bumpOk && repeatsOk;
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"SMOKE {(ok ? "PASS" : "FAIL")}: art pipeline seam ratio {before:0.00} → {after:0.00}, ramp {rampBefore:0.000} → {rampAfter:0.000}, flat normal error {normalError:0.0000}, " +
            $"roughness error {roughnessError:0.0000}, bump normals {(bumpOk ? "lean outwards" : "WRONG")}, " +
            $"repeating pattern changed by {inStep:0.000} in step with its repeats ({halfShift:0.000} by a half shift)"));
        return ok ? 0 : 1;
    }

    /// <summary>Adds (or replaces) this asset's record in the provenance file.</summary>
    private static void Record(string kind, string id, List<string> files, string notes)
    {
        string path = ProjectSettings.GlobalizePath(Provenance);
        var assets = new JsonArray();
        if (File.Exists(path))
        {
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            if (JsonNode.Parse(File.ReadAllText(path), documentOptions: options)?["assets"] is JsonArray existing)
            {
                foreach (JsonNode? entry in existing)
                {
                    if (entry is not null && !(entry["id"]?.GetValue<string>() == id && entry["kind"]?.GetValue<string>() == kind))
                    {
                        assets.Add(entry.DeepClone());
                    }
                }
            }
        }

        assets.Add(new JsonObject
        {
            ["id"] = id,
            ["kind"] = kind,
            ["job"] = Required("--job"),
            ["generator"] = Required("--generator"),
            ["prompt"] = Required("--prompt"),
            ["source"] = Required("--url"),
            ["imported"] = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["files"] = new JsonArray(files.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray()),
            ["notes"] = notes,
        });
        var write = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        File.WriteAllText(path, Header + new JsonObject { ["assets"] = assets }.ToJsonString(write) + "\n");
    }

    private static Aabb? Bounds(Node node, Transform3D parent)
    {
        Transform3D transform = node is Node3D spatial ? parent * spatial.Transform : parent;
        // A skinned mesh ignores its node's transform: its vertices are already in model space.
        Aabb? box = node is MeshInstance3D { Mesh: { } mesh } instance
            ? instance.Skin is not null ? mesh.GetAabb() : transform * mesh.GetAabb()
            : null;
        foreach (Node child in node.GetChildren())
        {
            if (Bounds(child, transform) is { } inner)
            {
                box = box is { } outer ? outer.Merge(inner) : inner;
            }
        }

        return box;
    }

    private static RgbImage ToRgb(Image image)
    {
        var rgb = new RgbImage(image.GetWidth(), image.GetHeight());
        byte[] bytes = image.GetData();
        for (int i = 0; i < rgb.Data.Length; i++)
        {
            rgb.Data[i] = bytes[i] / 255f;
        }

        return rgb;
    }

    private static RgbImage Grey(float[] values, int width, int height)
    {
        var grey = new RgbImage(width, height);
        for (int i = 0; i < values.Length; i++)
        {
            grey.Data[i * 3] = grey.Data[i * 3 + 1] = grey.Data[i * 3 + 2] = values[i];
        }

        return grey;
    }

    private static string Save(RgbImage rgb, string path, float quality)
    {
        var bytes = new byte[rgb.Data.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)Math.Clamp((int)(rgb.Data[i] * 255f + 0.5f), 0, 255);
        }

        Error error = Image.CreateFromData(rgb.Width, rgb.Height, false, Image.Format.Rgb8, bytes).SaveJpg(ProjectSettings.GlobalizePath(path), quality);
        return error == Error.Ok ? path : throw new IOException($"can't write {path} ({error})");
    }

    private static string Required(string flag) =>
        Args.Value(flag) is { Length: > 0 } value ? value : throw new ArgumentException($"{flag}=… is required");

    private static int Number(string flag, int fallback) =>
        Args.Value(flag) is { } text ? int.Parse(text, CultureInfo.InvariantCulture) : fallback;

    private static float Number(string flag, float fallback) =>
        Args.Value(flag) is { } text ? float.Parse(text, CultureInfo.InvariantCulture) : fallback;
}
