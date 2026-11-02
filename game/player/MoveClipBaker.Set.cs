using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;

namespace Pb.Game.Player;

/// <summary>
/// The animation set (Phase 5, M5.2): one GLB holding every clip on the generator's rig (tools/anim/build_clips.py), baked
/// onto each model like the movement clips: each bone turns in skeleton space as the clip turns it from the clip rig's
/// rest, applied to the model's own rest, and the hips' sway is scaled by the two rigs' leg lengths. The clips' lengths
/// are taken as made (a looping clip's last key repeats its first). A step cycle covers the ground its pace was made for
/// (presentation.jsonc "animPaces"; the script reads the same) scaled to the model's legs, and its feet say when each is
/// down.
/// </summary>
public static partial class MoveClipBaker
{
    /// <summary>The clips that play once (a refill, a flinch); every other clip in the set loops.</summary>
    private static readonly HashSet<string> OneShots = new(StringComparer.Ordinal) { "refill", "hit" };

    private static readonly Dictionary<(string Set, string Model), AnimSet?> BakedSets = new();

    /// <summary>
    /// Every clip of the set in <paramref name="setPath"/> (its step clips made for <paramref name="paces"/>) fitted to
    /// <paramref name="skeleton"/> (in the model <paramref name="model"/>, loaded from <paramref name="modelPath"/>), or
    /// null when the set is missing or isn't on the generator's rig.
    /// </summary>
    public static AnimSet? BakeSet(string? setPath, AnimPacesDef paces, string modelPath, Node3D model, Skeleton3D skeleton)
    {
        if (string.IsNullOrWhiteSpace(setPath))
        {
            return null;
        }

        if (BakedSets.TryGetValue((setPath, modelPath), out AnimSet? set))
        {
            return set;
        }

        set = null;
        if (SetSource(setPath) is { } source)
        {
            try
            {
                set = Bake(source, target: Rig.Of(model, skeleton, null), modelPath, paces);
            }
            finally
            {
                // Nothing of Godot's is kept between bakes (one held by C# past the game's end is reported as leaked).
                source.Root.Free();
            }
        }

        BakedSets[(setPath, modelPath)] = set;
        return set;
    }

    private static AnimSet Bake((Node3D Root, Skeleton3D Skeleton, AnimationPlayer Player) source, Rig target, string modelPath, AnimPacesDef paces)
    {
        var clips = new Dictionary<string, BakedClip>(StringComparer.Ordinal);
        foreach (string name in source.Player.GetAnimationList())
        {
            Animation animation = source.Player.GetAnimation(name);
            Rig rig = Rig.Of(source.Root, source.Skeleton, animation);
            string clipName = name.Contains('/') ? name[(name.LastIndexOf('/') + 1)..] : name;
            clips[clipName] = BakeOne(clipName, rig, target, animation, !OneShots.Contains(clipName), MadeFor(clipName, paces));
        }

        var set = new AnimSet(clips, target.Count);
        var lines = new System.Text.StringBuilder();
        foreach ((string name, BakedClip clip) in clips)
        {
            if (clip.CycleDistance_m > 0f)
            {
                lines.Append($" {name} {clip.Speed_mps:0.00} m/s;");
            }
        }

        GD.Print($"Animation set on {modelPath.GetFile()}: {clips.Count} clips;{lines}");
        return set;
    }

    /// <summary>The ground speed a clip was made for (m/s): a step clip's ("jog_r", the jog's to the right) its pace's; else 0.</summary>
    private static float MadeFor(string clip, AnimPacesDef paces)
    {
        int cut = clip.LastIndexOf('_');
        if (cut < 0 || clip[(cut + 1)..] is not ("f" or "r" or "b" or "l"))
        {
            return 0f;
        }

        return clip[..cut] switch
        {
            "walk" => paces.Walk_mps,
            "jog" => paces.Jog_mps,
            "run" => paces.Run_mps,
            "sprint" => paces.Sprint_mps,
            "crouch" => paces.Crouch_mps,
            _ => 0f,
        };
    }

    /// <summary>The set's scene, instantiated for one bake (the caller frees it), or null.</summary>
    private static (Node3D Root, Skeleton3D Skeleton, AnimationPlayer Player)? SetSource(string path)
    {
        (Node3D, Skeleton3D, AnimationPlayer)? source = null;
        if (ArtFiles.Load<PackedScene>(path) is { } scene)
        {
            Node3D root = scene.Instantiate<Node3D>();
            Skeleton3D? skeleton = Find<Skeleton3D>(root);
            AnimationPlayer? player = Find<AnimationPlayer>(root);
            if (skeleton is not null && player is not null && player.GetAnimationList().Length > 0 && skeleton.FindBone("Hips") >= 0)
            {
                source = (root, skeleton, player);
            }
            else
            {
                GD.PushWarning($"{path} holds no clips on the generator's rig; the characters fall back to their idle");
                root.Free();
            }
        }
        else if (!ArtFiles.Disabled)
        {
            GD.PushWarning($"The animation set {path} is missing; the characters fall back to their idle");
        }

        return source;
    }

    /// <summary>
    /// One clip of the set baked onto <paramref name="target"/>: its keys at <see cref="Rate"/> a second, and where its
    /// ankles are. The clips' rotations play on any build, but copied onto legs of other proportions the feet drift off the
    /// paths they were made on; so the source's ankle paths are kept too, measured from the point under the body and
    /// scaled by the two rigs' leg lengths, in the target's skeleton space, and the poser puts each foot on its path by IK.
    /// A step clip made for <paramref name="speed"/> covers that much ground a second, scaled likewise.
    /// </summary>
    private static BakedClip BakeOne(string name, Rig source, Rig target, Animation animation, bool loops, float speed)
    {
        // The clip's keys run from its first to its last; a loop's last repeats its first, so the cycle is that span.
        double first = double.MaxValue, last = double.MinValue;
        foreach (int track in source.RotationTracks)
        {
            if (track >= 0 && animation.TrackGetKeyCount(track) is var n and >= 1)
            {
                first = Math.Min(first, animation.TrackGetKeyTime(track, 0));
                last = Math.Max(last, animation.TrackGetKeyTime(track, n - 1));
            }
        }

        if (first > last)
        {
            first = 0.0;
            last = Math.Max(animation.Length, 0.1);
        }

        float length = (float)Math.Max(last - first, 1.0 / Rate);
        int frames = Math.Max(2, Mathf.RoundToInt(length * Rate)) + (loops ? 0 : 1);
        int bones = target.Count;
        var rotations = new Quaternion[frames * bones];
        var hips = new Vector3[frames];
        var ankles = new Vector3[2 * frames];
        var balls = new Vector3[2 * frames];
        var sourceAnkle = new Vector3[2, frames];
        var sourceToe = new Vector3[2, frames];
        var match = new int[bones];
        for (int b = 0; b < bones; b++)
        {
            match[b] = source.Find(target.Names[b]);
        }

        Quaternion into = target.FrameRotation.Inverse() * source.FrameRotation;
        var sourceLocal = new Quaternion[source.Count];
        var sourceGlobal = new Quaternion[source.Count];
        var sourcePosition = new Vector3[source.Count];
        var targetGlobal = new Quaternion[bones];
        float scale = target.LegLength_m / source.LegLength_m;
        Vector3 sourceHipsRest = source.RestLocal[source.Hips].Origin;
        int hipsTrack = source.PositionTracks[source.Hips];
        int[] sourceFeet = { source.Find("LeftFoot"), source.Find("RightFoot") };
        int[] sourceToes = { source.Find("LeftToeBase"), source.Find("RightToeBase") };
        float sourceAnkleRest = AnkleHeight(source), targetAnkleRest = AnkleHeight(target);
        float sourceBallRest = AnkleHeight(source, "ToeBase"), targetBallRest = AnkleHeight(target, "ToeBase");
        Transform3D targetFromModel = target.Frame.AffineInverse();
        for (int f = 0; f < frames; f++)
        {
            double time = first + (loops ? length * f / frames : length * f / (frames - 1));
            Vector3 sourceHips = hipsTrack >= 0 ? animation.PositionTrackInterpolate(hipsTrack, time) : sourceHipsRest;
            foreach (int b in source.Order)
            {
                int track = source.RotationTracks[b];
                sourceLocal[b] = track >= 0 ? animation.RotationTrackInterpolate(track, time).Normalized() : source.RestRotation[b];
                int parent = source.Parents[b];
                sourceGlobal[b] = parent >= 0 ? (sourceGlobal[parent] * sourceLocal[b]).Normalized() : sourceLocal[b];
                Vector3 offset = b == source.Hips ? sourceHips : source.RestLocal[b].Origin;
                sourcePosition[b] = parent >= 0 ? sourcePosition[parent] + sourceGlobal[parent] * offset : offset;
            }

            foreach (int b in target.Order)
            {
                int parent = target.Parents[b];
                int s = match[b];
                if (s >= 0)
                {
                    Quaternion turn = sourceGlobal[s] * source.RestGlobalRotation[s].Inverse();
                    targetGlobal[b] = (into * turn * into.Inverse() * target.RestGlobalRotation[b]).Normalized();
                }
                else
                {
                    targetGlobal[b] = parent >= 0 ? (targetGlobal[parent] * target.RestRotation[b]).Normalized() : target.RestRotation[b];
                }

                rotations[f * bones + b] = parent >= 0 ? (targetGlobal[parent].Inverse() * targetGlobal[b]).Normalized() : targetGlobal[b];
            }

            Vector3 sway = source.Frame.Basis * (sourceHips - sourceHipsRest) * scale;
            hips[f] = target.RestLocal[target.Hips].Origin + target.Frame.Basis.Inverse() * sway;

            for (int i = 0; i < 2; i++)
            {
                // The source's ankle in its model's space (Y up, from the point under the body), scaled to the target's legs.
                Vector3 ankle = sourceFeet[i] >= 0 ? source.Frame * sourcePosition[sourceFeet[i]] : Vector3.Zero;
                sourceAnkle[i, f] = ankle;
                sourceToe[i, f] = sourceToes[i] >= 0 ? source.Frame * sourcePosition[sourceToes[i]] : ankle;
                var model = new Vector3(ankle.X * scale, targetAnkleRest + (ankle.Y - sourceAnkleRest) * scale, ankle.Z * scale);
                ankles[f * 2 + i] = targetFromModel * model;
                Vector3 toe = sourceToe[i, f];
                balls[f * 2 + i] = targetFromModel * new Vector3(toe.X * scale, targetBallRest + (toe.Y - sourceBallRest) * scale, toe.Z * scale);
            }
        }

        (bool[] leftDown, bool[] rightDown) = Footing(sourceAnkle, sourceToe, frames, length, loops, speed);
        float drop = 0f;
        float restHips = (target.Frame * target.RestGlobal[target.Hips].Origin).Y;
        for (int f = 0; f < frames; f++)
        {
            drop += restHips - (target.Frame * target.RestGlobal[target.Hips].Origin + target.Frame.Basis * (hips[f] - target.RestLocal[target.Hips].Origin)).Y;
        }

        float distance = speed * length * scale;
        return new BakedClip(name, bones, rotations, hips, length, loops, distance, drop / frames, leftDown, rightDown, ankles, balls);
    }

    /// <summary>A rig's ankles' (or, for "ToeBase", the balls of its feet's) height at rest, in its model's space (m).</summary>
    private static float AnkleHeight(Rig rig, string joint = "Foot")
    {
        float sum = 0f;
        int n = 0;
        foreach (string foot in new[] { "Left" + joint, "Right" + joint })
        {
            if (rig.Find(foot) is var b and >= 0)
            {
                sum += (rig.Frame * rig.RestGlobal[b].Origin).Y;
                n++;
            }
        }

        return n > 0 ? sum / n : 0.08f;
    }

    /// <summary>
    /// When each of the source clip's feet is down, frame to frame (a frame's flag covers the step to the next): low (the
    /// ankle near its lowest, or the ball of the foot near its lowest) and moving back under the body at the clip's ground
    /// speed, <paramref name="speed"/> (still, for a clip that doesn't step). A foot just leaving the ground or about to
    /// land can be as low, but isn't moving with the ground.
    /// </summary>
    private static (bool[] LeftDown, bool[] RightDown) Footing(Vector3[,] ankle, Vector3[,] toe, int frames, float length, bool loops, float speed)
    {
        var leftDown = new bool[frames];
        var rightDown = new bool[frames];
        float step = loops ? length / frames : length / Math.Max(1, frames - 1);
        float expected = speed * step, slack = Math.Max(0.15f * expected, 0.004f);
        for (int i = 0; i < 2; i++)
        {
            float lowest = float.MaxValue, lowestToe = float.MaxValue;
            for (int f = 0; f < frames; f++)
            {
                lowest = MathF.Min(lowest, ankle[i, f].Y);
                lowestToe = MathF.Min(lowestToe, toe[i, f].Y);
            }

            bool[] down = i == 0 ? leftDown : rightDown;
            for (int f = 0; f < frames; f++)
            {
                int next = f + 1 < frames ? f + 1 : loops ? 0 : f;
                bool byAnkle = ankle[i, f].Y <= lowest + ContactBand && MathF.Abs(Flat(ankle[i, next] - ankle[i, f]) - expected) <= slack;
                bool byBall = toe[i, f].Y <= lowestToe + ContactBand && MathF.Abs(Flat(toe[i, next] - toe[i, f]) - expected) <= slack;
                down[f] = byAnkle || byBall;
            }
        }

        return (leftDown, rightDown);
    }

    private static float Flat(Vector3 v) => new Vector2(v.X, v.Z).Length();
}
