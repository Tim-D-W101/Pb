using Godot;
using Pb.Game.Core;

namespace Pb.Game.World;

/// <summary>
/// Rings of distant tree lines round a place (presentation.jsonc "horizon"): jagged silhouettes
/// broken by open country. They're lit like the ground (normals up) so they don't flash where the
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
                foreach (Vector3 v in new[] { p0, t0, t1, p0, t1, p1 })
                {
                    tool.SetNormal(Vector3.Up);
                    tool.SetColor(shade);
                    tool.AddVertex(v);
                }
            }
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
}
