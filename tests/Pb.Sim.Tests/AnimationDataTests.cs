using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pb.Sim.Tests;

/// <summary>
/// The characters' animation set (M5.2): its step clips were made for the speeds the sim moves at (so each plays alone,
/// unblended, and planted feet don't slide), and the set the script wrote holds every clip the blender plays, each starting
/// at 0 s (a clip whose keys start later is held at its first pose until then: a stall in every cycle).
/// </summary>
public class AnimationDataTests
{
    /// <summary>Every clip the game's blender plays (game/player/ClipBlender.cs).</summary>
    private static readonly string[] Clips =
    {
        "idle", "crouch_idle", "sprint_f", "lean_l", "lean_r", "refill", "hit", "hands_up",
        "walk_f", "walk_r", "walk_b", "walk_l", "jog_f", "jog_r", "jog_b", "jog_l",
        "run_f", "run_r", "run_b", "run_l", "crouch_f", "crouch_r", "crouch_b", "crouch_l",
    };

    private static string Presentation => TestData.Source.ReadAllText("presentation.jsonc");

    /// <summary>presentation.jsonc "characters" → "animPaces": the paces' speeds (m/s).</summary>
    private static Dictionary<string, float> Paces()
    {
        System.Text.RegularExpressions.Match block = Regex.Match(Presentation, "\"animPaces\":\\s*\\{([^}]*)\\}");
        Assert.True(block.Success, "presentation.jsonc has no animPaces");
        return Regex.Matches(block.Groups[1].Value, "\"([a-z]+)_mps\"\\s*:\\s*([0-9.]+)")
            .ToDictionary(m => m.Groups[1].Value, m => float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Every_speed_the_sim_moves_at_has_a_clip_made_for_it()
    {
        Dictionary<string, float> paces = Paces();
        var movement = TestData.Config.Movement;
        // Walking (Alt) plays the jog; the bots' stroll, a share of walking, the walk.
        Assert.Equal(movement.WalkSpeed, paces["jog"], 3);
        Assert.Equal(movement.RunSpeed, paces["run"], 3);
        Assert.Equal(movement.SprintSpeed, paces["sprint"], 3);
        Assert.Equal(movement.CrouchSpeed, paces["crouch"], 3);
        Assert.Equal(movement.WalkSpeed * TestData.Data.Bots.Brain.StrollPace, paces["walk"], 2);
    }

    [Fact]
    public void The_set_has_every_clip_and_each_starts_at_zero()
    {
        string path = Path.GetFullPath(Path.Combine(TestData.Source.Root, "..", "art", "models", "anim_set.glb"));
        Assert.True(File.Exists(path), $"{path} is missing");
        using JsonDocument gltf = GlbJson(File.ReadAllBytes(path));
        JsonElement root = gltf.RootElement;
        JsonElement accessors = root.GetProperty("accessors");
        var found = new Dictionary<string, (float First, float Last)>(StringComparer.Ordinal);
        foreach (JsonElement animation in root.GetProperty("animations").EnumerateArray())
        {
            float first = float.MaxValue, last = float.MinValue;
            foreach (JsonElement sampler in animation.GetProperty("samplers").EnumerateArray())
            {
                JsonElement input = accessors[sampler.GetProperty("input").GetInt32()];
                first = MathF.Min(first, input.GetProperty("min")[0].GetSingle());
                last = MathF.Max(last, input.GetProperty("max")[0].GetSingle());
            }

            found[animation.GetProperty("name").GetString()!] = (first, last);
        }

        foreach (string clip in Clips)
        {
            Assert.True(found.TryGetValue(clip, out var span), $"the set has no '{clip}' ({string.Join(", ", found.Keys)})");
            Assert.True(span.First < 1e-4f, $"'{clip}' starts at {span.First:0.000} s, not 0");
            Assert.True(span.Last > 0.2f, $"'{clip}' is {span.Last:0.000} s long");
        }
    }

    [Fact]
    public void A_step_cycle_is_as_long_as_its_cadence_makes_it()
    {
        // tools/anim/build_clips.py: a cycle is round(30 / cadence) frames at 30 a second, its last key repeating its
        // first, so the clip runs that many frames' time exactly.
        string script = File.ReadAllText(Path.GetFullPath(Path.Combine(TestData.Source.Root, "..", "..", "tools", "anim", "build_clips.py")));
        string path = Path.GetFullPath(Path.Combine(TestData.Source.Root, "..", "art", "models", "anim_set.glb"));
        using JsonDocument gltf = GlbJson(File.ReadAllBytes(path));
        JsonElement accessors = gltf.RootElement.GetProperty("accessors");
        foreach (string pace in new[] { "walk", "jog", "run", "sprint", "crouch" })
        {
            System.Text.RegularExpressions.Match row = Regex.Match(script, $"\"{pace}\":\\s*dict\\(cadence=([0-9.]+)");
            Assert.True(row.Success, $"build_clips.py has no '{pace}' gait");
            float cadence = float.Parse(row.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            float expected = MathF.Max(8, MathF.Round(30f / cadence)) / 30f;
            JsonElement animation = gltf.RootElement.GetProperty("animations").EnumerateArray()
                .First(a => a.GetProperty("name").GetString() == pace + "_f");
            float last = animation.GetProperty("samplers").EnumerateArray()
                .Max(s => accessors[s.GetProperty("input").GetInt32()].GetProperty("max")[0].GetSingle());
            Assert.Equal(expected, last, 3);
        }
    }

    /// <summary>The JSON chunk of a binary glTF.</summary>
    private static JsonDocument GlbJson(byte[] glb)
    {
        Assert.Equal(0x46546C67u, BitConverter.ToUInt32(glb, 0)); // "glTF"
        int length = BitConverter.ToInt32(glb, 12);
        Assert.Equal(0x4E4F534Au, BitConverter.ToUInt32(glb, 16)); // "JSON"
        return JsonDocument.Parse(new ReadOnlyMemory<byte>(glb, 20, length));
    }
}
