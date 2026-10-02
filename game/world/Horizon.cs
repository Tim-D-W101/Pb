using Godot;
using Pb.Game.Core;

namespace Pb.Game.World;

/// <summary>
/// Rings of distant tree lines round a place (presentation.jsonc "horizon"): jagged silhouettes
/// broken by open country, with a few buildings far off among them (chimneys, sheds, a gasholder,
/// flats). They're lit like the ground (normals up) so they don't flash where the
/// sun faces them, and fog does the rest. Used by the compound levels and the training ground.
/// </summary>
public static class Horizon
{
    /// <summary>The rings round <paramref name="center"/>, their outline seeded by <paramref name="seed"/>.</summary>
    public static MeshInstance3D Build(Vector3 center, int seed, HorizonDef horizon)
    {
        var noise = new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            FractalOctaves = 3,
            Seed = seed,
        };
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        Color baseColor = Color.FromHtml(horizon.Color);

        for (int r = 0; r < horizon.Rings.Length; r++)
        {
            HorizonRingDef ring = horizon.Rings[r];
            int segments = Mathf.CeilToInt(Mathf.Tau * ring.Radius_m / 3f);
            float layer = r * 97f;

            // Noise sampled around the circle so the ring closes without a seam.
            Vector3 At(int i) => center + new Vector3(Mathf.Cos(Mathf.Tau * i / segments), 0f, Mathf.Sin(Mathf.Tau * i / segments)) * ring.Radius_m;
            float Height(Vector3 p)
            {
                float woods = 0.5f + 0.5f * noise.GetNoise3D(p.X * 0.012f, p.Z * 0.012f, layer);
                float crowns = noise.GetNoise3D(p.X * 0.25f, p.Z * 0.25f, layer + 40f);
                float edge = Mathf.Clamp((woods - ring.Gaps) / 0.06f, 0f, 1f);
                float tall = Mathf.Clamp(0.55f + 0.45f * crowns + 0.6f * (woods - 0.5f), 0f, 1f);
                return edge * Mathf.Lerp(ring.MinHeight_m, ring.MaxHeight_m, tall);
            }

            for (int i = 0; i < segments; i++)
            {
                Vector3 p0 = At(i), p1 = At(i + 1);
                float h0 = Height(p0), h1 = Height(p1);
                if (h0 <= 0f && h1 <= 0f)
                {
                    continue;
                }

                Color shade = baseColor * (0.85f + 0.2f * (0.5f + 0.5f * noise.GetNoise3D(p0.X * 0.05f, p0.Z * 0.05f, layer + 80f)));
                shade.A = 1f;
                Vector3 t0 = p0 + Vector3.Up * h0, t1 = p1 + Vector3.Up * h1;
                Quad(tool, p0, t0, t1, p1, shade);
            }
        }

        Color landmark = Color.FromHtml(horizon.LandmarkColor ?? horizon.Color);
        foreach (LandmarkDef l in horizon.Landmarks ?? System.Array.Empty<LandmarkDef>())
        {
            Landmark(tool, center, l, landmark);
        }

        return new MeshInstance3D
        {
            Name = "Horizon",
            Mesh = tool.Commit(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                Roughness = 1f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
    }

    /// <summary>
    /// A building far off, facing the place's middle: a tapering chimney with a band near its top; a shed
    /// with a sawtooth roof; a gasholder's frame of standards and girders round a low drum; or a block of
    /// flats with a lift tower on its roof. Silhouettes, lit like the tree lines.
    /// </summary>
    private static void Landmark(SurfaceTool tool, Vector3 center, LandmarkDef l, Color color)
    {
        float bearing = Mathf.DegToRad(l.Bearing_deg);
        var toward = new Vector3(Mathf.Sin(bearing), 0f, -Mathf.Cos(bearing));
        Vector3 at = center + toward * l.Distance_m;
        // Its own frame: x across the view, z away from the middle.
        Vector3 x = Vector3.Up.Cross(toward).Normalized(), z = toward;
        float h = l.Height_m, w = l.Width_m;
        switch (l.Kind)
        {
            case "chimney":
                Tube(tool, at, w * 0.5f, w * 0.34f, 0f, h, color);
                Tube(tool, at, w * 0.38f, w * 0.38f, h * 0.9f, h * 0.94f, color * 0.85f);
                break;
            case "shed":
            {
                float d = w * 0.55f, wall = h * 0.62f;
                Block(tool, at, x, z, w, d, 0f, wall, color);
                int teeth = Mathf.Max(2, Mathf.RoundToInt(w / 14f));
                for (int i = 0; i < teeth; i++)
                {
                    float x0 = -w * 0.5f + w * i / teeth, x1 = x0 + w / teeth;
                    // Each tooth: a steep glazed face and a long slope, seen side on.
                    Vector3 a = at + x * x0 + Vector3.Up * wall, b = at + x * x1 + Vector3.Up * wall;
                    Vector3 peak = at + x * (x0 + (x1 - x0) * 0.2f) + Vector3.Up * h;
                    foreach (float side in new[] { -0.5f, 0.5f })
                    {
                        Vector3 off = z * (side * d);
                        Tri(tool, a + off, peak + off, b + off, color * 0.95f);
                    }

                    Quad(tool, a - z * d * 0.5f, peak - z * d * 0.5f, peak + z * d * 0.5f, a + z * d * 0.5f, color * 0.9f);
                    Quad(tool, peak - z * d * 0.5f, b - z * d * 0.5f, b + z * d * 0.5f, peak + z * d * 0.5f, color);
                }

                break;
            }

            case "gasholder":
            {
                float r = w * 0.5f;
                Tube(tool, at, r * 0.96f, r * 0.96f, 0f, h * 0.35f, color * 0.9f);
                const int Standards = 10;
                for (int i = 0; i < Standards; i++)
                {
                    float a = Mathf.Tau * i / Standards;
                    Vector3 foot = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    Block(tool, foot, x, z, 1.2f, 1.2f, 0f, h, color);
                }

                foreach (float y in new[] { h * 0.45f, h * 0.72f, h * 0.98f })
                {
                    Tube(tool, at, r + 0.4f, r + 0.4f, y - 0.7f, y, color);
                }

                break;
            }

            default:
                Block(tool, at, x, z, w, w * 0.4f, 0f, h, color);
                Block(tool, at + x * (w * 0.2f), x, z, w * 0.16f, w * 0.16f, h, h + h * 0.08f, color * 0.9f);
                break;
        }
    }

    /// <summary>An upright box in the frame (x across, z away), from <paramref name="y0"/> to <paramref name="y1"/>.</summary>
    private static void Block(SurfaceTool tool, Vector3 at, Vector3 x, Vector3 z, float w, float d, float y0, float y1, Color color)
    {
        Vector3 hx = x * (w * 0.5f), hz = z * (d * 0.5f), lo = Vector3.Up * y0, hi = Vector3.Up * y1;
        Vector3[] corners = { at - hx - hz, at + hx - hz, at + hx + hz, at - hx + hz };
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = corners[i], b = corners[(i + 1) % 4];
            Quad(tool, a + lo, a + hi, b + hi, b + lo, color * (i % 2 == 0 ? 1f : 0.9f));
        }

        Quad(tool, corners[0] + hi, corners[1] + hi, corners[2] + hi, corners[3] + hi, color);
    }

    /// <summary>An upright open tube about <paramref name="at"/>, its radius going from <paramref name="r0"/> to <paramref name="r1"/>.</summary>
    private static void Tube(SurfaceTool tool, Vector3 at, float r0, float r1, float y0, float y1, Color color)
    {
        const int Segments = 16;
        for (int i = 0; i < Segments; i++)
        {
            float a0 = Mathf.Tau * i / Segments, a1 = Mathf.Tau * (i + 1) / Segments;
            var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
            var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            Quad(tool, at + d0 * r0 + Vector3.Up * y0, at + d0 * r1 + Vector3.Up * y1, at + d1 * r1 + Vector3.Up * y1, at + d1 * r0 + Vector3.Up * y0, color);
        }
    }

    private static void Quad(SurfaceTool tool, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    {
        Tri(tool, a, b, c, color);
        Tri(tool, a, c, d, color);
    }

    private static void Tri(SurfaceTool tool, Vector3 a, Vector3 b, Vector3 c, Color color)
    {
        color.A = 1f;
        foreach (Vector3 v in new[] { a, b, c })
        {
            tool.SetNormal(Vector3.Up);
            tool.SetColor(color);
            tool.AddVertex(v);
        }
    }
}
