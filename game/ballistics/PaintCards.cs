using System.Collections.Generic;
using Godot;

namespace Pb.Game.Ballistics;

/// <summary>
/// Paint drawn as flat cards on the surface, for a renderer that draws no decals: the Compatibility renderer, which
/// Godot falls back to (OpenGL) on a graphics card or driver without Vulkan, has none, and every splat and run of paint
/// was invisible there. A card is a square a hair off the surface in the decal's frame (its Y the surface's normal, the
/// texture's u along its X and v along its Z, as a decal maps it) and of the decal's size across, so splats and runs
/// place either the same way. It can't wrap round a corner as a decal does; it hangs over the edge instead.
/// </summary>
public static class PaintCards
{
    /// <summary>How far off the surface a card lies (m): clear of the surface's own depth at a distance.</summary>
    private const float Lift = 0.006f;

    private static readonly Dictionary<(ulong Texture, Color Color), StandardMaterial3D> Materials = new();
    private static ArrayMesh? _quad;

    /// <summary>Whether the renderer draws decals: Forward+ and Mobile do. Compatibility (and a headless run) has no rendering device.</summary>
    public static bool DecalsDrawn => RenderingServer.GetRenderingDevice() is not null;

    /// <summary>A new card, drawn out to <paramref name="visibleTo"/> metres (as a decal fades out by then).</summary>
    public static MeshInstance3D Create(float visibleTo) => new()
    {
        Mesh = Quad(),
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        GIMode = GeometryInstance3D.GIModeEnum.Disabled,
        VisibilityRangeEnd = visibleTo,
    };

    /// <summary>
    /// Lays <paramref name="card"/> in <paramref name="frame"/> (a decal's: Y along the surface's normal) at
    /// <paramref name="size"/> (a decal's: X and Z across the surface), in <paramref name="texture"/> tinted
    /// <paramref name="color"/>.
    /// </summary>
    public static void Place(MeshInstance3D card, Transform3D frame, Vector3 size, Texture2D texture, Color color)
    {
        Basis b = frame.Basis.Orthonormalized();
        card.GlobalTransform = new Transform3D(new Basis(b.X * size.X, b.Y, b.Z * size.Z), frame.Origin + b.Y * Lift);
        card.MaterialOverride = Material(texture, color with { A = 1f });
        card.Transparency = 1f - color.A;
    }

    /// <summary>Resizes and moves a placed card (a run of paint lengthening down a wall).</summary>
    public static void Reshape(MeshInstance3D card, Transform3D frame, Vector3 size)
    {
        Basis b = frame.Basis.Orthonormalized();
        card.GlobalTransform = new Transform3D(new Basis(b.X * size.X, b.Y, b.Z * size.Z), frame.Origin + b.Y * Lift);
    }

    /// <summary>A card's opacity (the oldest splats fade before they're reused).</summary>
    public static float Alpha(MeshInstance3D card) => 1f - card.Transparency;

    public static void SetAlpha(MeshInstance3D card, float alpha) => card.Transparency = 1f - alpha;

    private static StandardMaterial3D Material(Texture2D texture, Color color)
    {
        if (!Materials.TryGetValue((texture.GetInstanceId(), color), out StandardMaterial3D? material) || !GodotObject.IsInstanceValid(material))
        {
            material = new StandardMaterial3D
            {
                AlbedoTexture = texture,
                AlbedoColor = color,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                Roughness = 0.55f,
            };
            Materials[(texture.GetInstanceId(), color)] = material;
        }

        return material;
    }

    /// <summary>A unit square in the XZ plane facing +Y, its UVs as a decal's: u along +X, v along +Z.</summary>
    private static ArrayMesh Quad()
    {
        if (_quad is not null && GodotObject.IsInstanceValid(_quad))
        {
            return _quad;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f),
        };
        arrays[(int)Mesh.ArrayType.Normal] = new[] { Vector3.Up, Vector3.Up, Vector3.Up, Vector3.Up };
        arrays[(int)Mesh.ArrayType.Tangent] = new[] { 1f, 0f, 0f, 1f, 1f, 0f, 0f, 1f, 1f, 0f, 0f, 1f, 1f, 0f, 0f, 1f };
        arrays[(int)Mesh.ArrayType.TexUV] = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        // Clockwise seen from above (Godot's front faces).
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 0, 2, 3 };
        _quad = new ArrayMesh();
        _quad.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return _quad;
    }
}
