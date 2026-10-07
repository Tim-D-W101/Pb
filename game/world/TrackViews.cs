using Godot;
using Pb.Game.Core;
using Pb.Sim.Level;

namespace Pb.Game.World;

/// <summary>
/// The level's railway tracks as you see them (the level file's <c>tracks</c>): each rail drawn with a head, a web and a
/// foot along its paint collider, and sleepers under them from the ground up to the rails' foot, a little uneven, one
/// mesh per track. Looks only: the rails' colliders are what paint and sight meet, and feet step over everything.
/// </summary>
public partial class TrackViews : Node3D
{
    /// <summary>Sleepers lie this far apart, this long across the track, this wide along it (m).</summary>
    private const float SleeperPitch = 0.68f;
    private const float SleeperLength = 2.6f;
    private const float SleeperWidth = 0.25f;

    public int SleeperCount { get; private set; }

    public void Build(LevelLayout level, MaterialLibrary materials)
    {
        int index = 0;
        foreach (TrackSpec track in level.Tracks)
        {
            var mesh = new ShapeMesh();
            var rng = new System.Random(LevelBuilder.StableHash(level.Id) + index * 7919);
            int rail = track.RailMaterial.Ref.Index, sleeper = track.SleeperMaterial.Ref.Index;
            float foot = track.Points[0].Y;
            float sleeperTop = Mathf.Max(0.04f, foot);
            for (int k = 1; k < track.Points.Count; k++)
            {
                Vector3 a = track.Points[k - 1].ToGodot(), b = track.Points[k].ToGodot();
                Vector3 along = b - a;
                float length = along.Length();
                if (length < 1e-3f)
                {
                    continue;
                }

                along /= length;
                var across = new Vector3(-along.Z, 0f, along.X);
                var basis = new Basis(across, Vector3.Up, along);
                Vector3 middle = (a + b) * 0.5f;
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 centre = middle + across * (side * (track.Gauge + track.RailWidth) * 0.5f);
                    float h = track.RailHeight;
                    // Foot, web and head, the head as wide as the collider.
                    mesh.Box(rail, centre with { Y = a.Y + h * 0.06f }, new Vector3(track.RailWidth * 1.9f, h * 0.12f, length), basis);
                    mesh.Box(rail, centre with { Y = a.Y + h * 0.45f }, new Vector3(track.RailWidth * 0.25f, h * 0.7f, length), basis);
                    mesh.Box(rail, centre with { Y = a.Y + h * 0.85f }, new Vector3(track.RailWidth, h * 0.3f, length), basis);
                }

                for (float d = SleeperPitch * 0.5f; d < length; d += SleeperPitch)
                {
                    Vector3 at = a + along * d + across * (float)(rng.NextDouble() * 0.06 - 0.03);
                    float yaw = (float)(rng.NextDouble() * 0.05 - 0.025);
                    float top = sleeperTop - (float)(rng.NextDouble() * 0.02);
                    mesh.Box(sleeper, at with { Y = top * 0.5f }, new Vector3(SleeperLength, top, SleeperWidth), basis * new Basis(Vector3.Up, yaw));
                    SleeperCount++;
                }
            }

            var array = new ArrayMesh();
            mesh.Commit(array, i => materials[i]);
            AddChild(new MeshInstance3D { Name = $"Track{++index}", Mesh = array });
        }
    }
}
