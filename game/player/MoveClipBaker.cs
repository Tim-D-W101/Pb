using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;

namespace Pb.Game.Player;

/// <summary>
/// Loads movement clips and bakes them onto character rigs (<see cref="MoveClip"/>). A clip file is a
/// GLB from the art pipeline holding one in-place clip on the generator's biped rig, and it fits any
/// model on that rig whatever its build: each bone, found by name, turns in skeleton space as the clip
/// turns it from its own rest pose, and the hips' sway is scaled by the two rigs' leg lengths. The
/// baked clip's feet then give its ground speed and phase. Baked once per clip and model.
/// </summary>
public static class MoveClipBaker
{
    /// <summary>Baked frames per second of clip.</summary>
    private const float Rate = 30f;

    /// <summary>A foot within this height of its lowest is on the ground (m).</summary>
    private const float ContactBand = 0.025f;

    private static readonly Dictionary<string, Rig?> Sources = new();
    private static readonly Dictionary<(string Clip, string Model), MoveClip?> Baked = new();

    /// <summary>
    /// The clip in <paramref name="clipPath"/> fitted to <paramref name="skeleton"/> (in the model
    /// <paramref name="model"/>, loaded from <paramref name="modelPath"/>), or null when the file is
    /// missing or holds no clip on a skeleton.
    /// </summary>
    public static MoveClip? Bake(string name, string? clipPath, string modelPath, Node3D model, Skeleton3D skeleton)
    {
        if (string.IsNullOrWhiteSpace(clipPath))
        {
            return null;
        }

        if (!Baked.TryGetValue((clipPath, modelPath), out MoveClip? clip))
        {
            Rig? source = Source(clipPath);
            Rig target = Rig.Of(model, skeleton, null);
            clip = source is null ? null : Retarget(name, source, target);
            Baked[(clipPath, modelPath)] = clip;
            if (clip is not null)
            {
                GD.Print($"Clip {name} on {modelPath.GetFile()}: {clip.Speed_mps:0.00} m/s, {clip.Length_s:0.00} s and " +
                         $"{clip.CycleDistance_m:0.00} m a cycle, hips {clip.HipsDrop_m * 100f:0} cm down");
            }
        }

        return clip;
    }

    private static Rig? Source(string path)
    {
        if (Sources.TryGetValue(path, out Rig? rig))
        {
            return rig;
        }

        if (ArtFiles.Load<PackedScene>(path) is { } scene)
        {
            Node3D root = scene.Instantiate<Node3D>();
            Skeleton3D? skeleton = Find<Skeleton3D>(root);
            AnimationPlayer? player = Find<AnimationPlayer>(root);
            string[] clips = player?.GetAnimationList() ?? Array.Empty<string>();
            if (skeleton is not null && clips.Length > 0 && skeleton.FindBone("Hips") >= 0)
            {
                rig = Rig.Of(root, skeleton, player!.GetAnimation(clips[0]));
            }
            else
            {
                GD.PushWarning($"{path} holds no clip on the generator's rig; its movement falls back");
            }

            root.Free();
        }
        else if (!ArtFiles.Disabled)
        {
            GD.PushWarning($"The movement clip {path} is missing; its movement falls back");
        }

        Sources[path] = rig;
        return rig;
    }

    private static MoveClip Retarget(string name, Rig source, Rig target)
    {
        Animation animation = source.Clip!;
        Cycle cycle = Cycle.Of(source);
        float length = (float)cycle.Period;
        int frames = Math.Max(2, Mathf.RoundToInt(length * Rate));
        int bones = target.Count;
        var rotations = new Quaternion[frames * bones];
        var hips = new Vector3[frames];

        // Turns in the source's skeleton space, seen in the target's.
        Quaternion into = target.FrameRotation.Inverse() * source.FrameRotation;
        var match = new int[bones];
        for (int b = 0; b < bones; b++)
        {
            match[b] = source.Find(target.Names[b]);
        }

        var sourceLocal = new Quaternion[source.Count];
        var sourceGlobal = new Quaternion[source.Count];
        var targetGlobal = new Quaternion[bones];
        float scale = target.LegLength_m / source.LegLength_m;
        Vector3 sourceHipsRest = source.RestLocal[source.Hips].Origin;
        for (int f = 0; f < frames; f++)
        {
            double time = cycle.Start + cycle.Period * f / frames;
            foreach (int b in source.Order)
            {
                int track = source.RotationTracks[b];
                sourceLocal[b] = track >= 0 ? cycle.Rotation(animation, track, time) : source.RestRotation[b];
                int parent = source.Parents[b];
                sourceGlobal[b] = parent >= 0 ? (sourceGlobal[parent] * sourceLocal[b]).Normalized() : sourceLocal[b];
            }

            foreach (int b in target.Order)
            {
                int parent = target.Parents[b];
                int s = match[b];
                if (s >= 0)
                {
                    // How the clip turns this bone from its rest, applied to the target's rest.
                    Quaternion turn = sourceGlobal[s] * source.RestGlobalRotation[s].Inverse();
                    targetGlobal[b] = (into * turn * into.Inverse() * target.RestGlobalRotation[b]).Normalized();
                }
                else
                {
                    targetGlobal[b] = parent >= 0 ? (targetGlobal[parent] * target.RestRotation[b]).Normalized() : target.RestRotation[b];
                }

                rotations[f * bones + b] = parent >= 0 ? (targetGlobal[parent].Inverse() * targetGlobal[b]).Normalized() : targetGlobal[b];
            }

            int hipsTrack = source.PositionTracks[source.Hips];
            Vector3 sourceHips = hipsTrack >= 0 ? cycle.Position(animation, hipsTrack, time) : sourceHipsRest;
            Vector3 sway = source.Frame.Basis * (sourceHips - sourceHipsRest) * scale;
            hips[f] = target.RestLocal[target.Hips].Origin + target.Frame.Basis.Inverse() * sway;
        }

        (float speed, float anchor, float drop) = Measure(name, target, rotations, hips, length);
        return new MoveClip(name, bones, rotations, hips, length, speed, anchor, drop);
    }

    /// <summary>
    /// What the baked clip's feet imply: the ground speed (a planted foot moves back past the hips at
    /// it; the median over the frames each foot is down), where in the cycle the left foot is halfway
    /// through its time on the ground, and how far the hips sit below their rest height on average.
    /// </summary>
    private static (float Speed, float Anchor, float Drop) Measure(string name, Rig rig, Quaternion[] rotations, Vector3[] hips, float length)
    {
        int frames = hips.Length;
        int bones = rig.Count;
        int[] feet = { rig.Find("LeftFoot"), rig.Find("RightFoot") };
        var height = new float[2, frames];
        var ahead = new float[2, frames];
        var globalRotation = new Quaternion[bones];
        var globalPosition = new Vector3[bones];
        float restHips = (rig.Frame * rig.RestGlobal[rig.Hips].Origin).Y;
        float drop = 0f;
        for (int f = 0; f < frames; f++)
        {
            foreach (int b in rig.Order)
            {
                Quaternion local = rotations[f * bones + b];
                Vector3 offset = b == rig.Hips ? hips[f] : rig.RestLocal[b].Origin;
                int parent = rig.Parents[b];
                globalPosition[b] = parent >= 0 ? globalPosition[parent] + globalRotation[parent] * offset : offset;
                // Kept unit length: rounding builds up down the chain, and turning a vector needs it.
                globalRotation[b] = (parent >= 0 ? globalRotation[parent] * local : local).Normalized();
            }

            Vector3 hipsAt = rig.Frame * globalPosition[rig.Hips];
            drop += restHips - hipsAt.Y;
            for (int i = 0; i < 2; i++)
            {
                if (feet[i] < 0)
                {
                    continue;
                }

                // Generated models face +Z.
                Vector3 foot = rig.Frame * globalPosition[feet[i]];
                height[i, f] = foot.Y;
                ahead[i, f] = foot.Z - hipsAt.Z;
            }
        }

        float step = length / frames;
        var speeds = new List<float>();
        int longestStart = 0, longestRun = 0;
        for (int i = 0; i < 2; i++)
        {
            float lowest = float.MaxValue;
            for (int f = 0; f < frames; f++)
            {
                lowest = MathF.Min(lowest, height[i, f]);
            }

            var down = new bool[frames];
            for (int f = 0; f < frames; f++)
            {
                down[f] = height[i, f] <= lowest + ContactBand;
                if (down[f])
                {
                    speeds.Add(-(ahead[i, (f + 1) % frames] - ahead[i, (f + frames - 1) % frames]) / (2f * step));
                }
            }

            if (i == 0)
            {
                (longestStart, longestRun) = LongestRun(down);
            }
        }

        // The middle of the speeds on the ground: a foot landing or lifting off moves differently.
        speeds.Sort();
        float speed = speeds.Count > 0 ? speeds[speeds.Count / 2] : 0f;
        if (speeds.Count == 0 || feet[0] < 0 || speed < 0.2f || speed > 15f)
        {
            // No clear footfalls: guess from how far the foot swings, on the ground for 60% of a cycle.
            float min = float.MaxValue, max = float.MinValue;
            for (int f = 0; f < frames; f++)
            {
                min = MathF.Min(min, ahead[0, f]);
                max = MathF.Max(max, ahead[0, f]);
            }

            float guess = Mathf.Clamp((max - min) / (0.6f * length), 0.3f, 8f);
            GD.PushWarning($"Clip {name}: no clear footfalls (measured {speed:0.00} m/s); playing it at {guess:0.00} m/s");
            speed = guess;
        }

        float anchor = longestRun > 0 ? (longestStart + longestRun * 0.5f) / frames : 0f;
        return (speed, anchor, drop / frames);
    }

    /// <summary>The longest stretch of true frames, round the end of the cycle too: its start and length.</summary>
    private static (int Start, int Length) LongestRun(bool[] on)
    {
        int frames = on.Length;
        bool anyOff = Array.IndexOf(on, false) >= 0;
        int bestStart = 0, best = 0;
        for (int start = 0; start < frames; start++)
        {
            // A stretch starts after a false frame (anywhere, when there's none).
            if (!on[start] || anyOff && on[(start + frames - 1) % frames])
            {
                continue;
            }

            int run = 0;
            while (run < frames && on[(start + run) % frames])
            {
                run++;
            }

            if (run > best)
            {
                best = run;
                bestStart = start;
            }
        }

        return (bestStart, best);
    }

    /// <summary>
    /// The span of a clip's keys that makes one cycle. A clip whose last keys repeat its first is a
    /// closed loop, from first key to last; otherwise the cycle runs one key spacing past the last
    /// key, back to the first.
    /// </summary>
    private readonly record struct Cycle(double Start, double Period, double Last, double Gap)
    {
        public static Cycle Of(Rig rig)
        {
            Animation clip = rig.Clip!;
            double first = double.MaxValue, last = double.MinValue;
            int keys = 0;
            foreach (int track in rig.RotationTracks)
            {
                if (track >= 0 && clip.TrackGetKeyCount(track) is var n and >= 2)
                {
                    first = Math.Min(first, clip.TrackGetKeyTime(track, 0));
                    last = Math.Max(last, clip.TrackGetKeyTime(track, n - 1));
                    keys = Math.Max(keys, n);
                }
            }

            if (keys < 2)
            {
                return new Cycle(0.0, Math.Max(clip.Length, 0.1), Math.Max(clip.Length, 0.1), 0.0);
            }

            bool closed = true;
            foreach (int track in rig.RotationTracks)
            {
                if (track >= 0 && clip.RotationTrackInterpolate(track, first).AngleTo(clip.RotationTrackInterpolate(track, last)) > 0.035f)
                {
                    closed = false;
                }
            }

            double span = last - first;
            return closed ? new Cycle(first, span, last, 0.0) : new Cycle(first, span * keys / (keys - 1), last, span / (keys - 1));
        }

        public Quaternion Rotation(Animation clip, int track, double time) => (time <= Last || Gap <= 0.0
            ? clip.RotationTrackInterpolate(track, Math.Min(time, Last))
            : clip.RotationTrackInterpolate(track, Last).Slerp(clip.RotationTrackInterpolate(track, Start), (float)((time - Last) / Gap))).Normalized();

        public Vector3 Position(Animation clip, int track, double time) => time <= Last || Gap <= 0.0
            ? clip.PositionTrackInterpolate(track, Math.Min(time, Last))
            : clip.PositionTrackInterpolate(track, Last).Lerp(clip.PositionTrackInterpolate(track, Start), (float)((time - Last) / Gap));
    }

    private static T? Find<T>(Node root) where T : Node
    {
        if (root is T match)
        {
            return match;
        }

        foreach (Node child in root.GetChildren())
        {
            if (Find<T>(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>A skeleton's bones and rest pose, and where it sits in its model (whose Y is up, +Z forward, in metres).</summary>
    private sealed class Rig
    {
        private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

        public int Count;
        public string[] Names = Array.Empty<string>();
        public int[] Parents = Array.Empty<int>();

        /// <summary>Bones with every parent before its children.</summary>
        public int[] Order = Array.Empty<int>();

        public Transform3D[] RestLocal = Array.Empty<Transform3D>();
        public Transform3D[] RestGlobal = Array.Empty<Transform3D>();
        public Quaternion[] RestRotation = Array.Empty<Quaternion>();
        public Quaternion[] RestGlobalRotation = Array.Empty<Quaternion>();

        /// <summary>Skeleton space to the model's space, and just its turn.</summary>
        public Transform3D Frame;
        public Quaternion FrameRotation;
        public int Hips;

        /// <summary>Hips above ankles at rest (m).</summary>
        public float LegLength_m;

        public Animation? Clip;
        public int[] RotationTracks = Array.Empty<int>();
        public int[] PositionTracks = Array.Empty<int>();

        public int Find(string name) => _index.TryGetValue(name, out int b) ? b : -1;

        public static Rig Of(Node3D root, Skeleton3D skeleton, Animation? clip)
        {
            int count = skeleton.GetBoneCount();
            var rig = new Rig
            {
                Count = count,
                Names = new string[count],
                Parents = new int[count],
                RestLocal = new Transform3D[count],
                RestGlobal = new Transform3D[count],
                RestRotation = new Quaternion[count],
                RestGlobalRotation = new Quaternion[count],
                Clip = clip,
                RotationTracks = new int[count],
                PositionTracks = new int[count],
            };
            for (int b = 0; b < count; b++)
            {
                rig.Names[b] = skeleton.GetBoneName(b);
                rig._index[rig.Names[b]] = b;
                rig.Parents[b] = skeleton.GetBoneParent(b);
                rig.RestLocal[b] = skeleton.GetBoneRest(b);
                rig.RestRotation[b] = rig.RestLocal[b].Basis.GetRotationQuaternion();
                rig.RotationTracks[b] = rig.PositionTracks[b] = -1;
            }

            // Parents first, then their children, generation by generation.
            var order = new List<int>(count);
            var placed = new bool[count];
            while (order.Count < count)
            {
                int before = order.Count;
                for (int b = 0; b < count; b++)
                {
                    if (!placed[b] && (rig.Parents[b] < 0 || placed[rig.Parents[b]]))
                    {
                        order.Add(b);
                        placed[b] = true;
                    }
                }

                if (order.Count == before)
                {
                    break; // a loop in the parents; can't happen in an imported skeleton
                }
            }

            rig.Order = order.ToArray();
            foreach (int b in rig.Order)
            {
                int parent = rig.Parents[b];
                rig.RestGlobal[b] = parent >= 0 ? rig.RestGlobal[parent] * rig.RestLocal[b] : rig.RestLocal[b];
                rig.RestGlobalRotation[b] = rig.RestGlobal[b].Basis.GetRotationQuaternion();
            }

            rig.Frame = skeleton.Transform;
            for (Node? node = skeleton.GetParent(); node is not null && node != root; node = node.GetParent())
            {
                if (node is Node3D spatial)
                {
                    rig.Frame = spatial.Transform * rig.Frame;
                }
            }

            rig.FrameRotation = rig.Frame.Basis.GetRotationQuaternion();
            rig.Hips = rig.Find("Hips");
            float hipsHeight = (rig.Frame * rig.RestGlobal[rig.Hips].Origin).Y;
            float ankles = float.MaxValue;
            foreach (string foot in new[] { "LeftFoot", "RightFoot" })
            {
                if (rig.Find(foot) is var b and >= 0)
                {
                    ankles = MathF.Min(ankles, (rig.Frame * rig.RestGlobal[b].Origin).Y);
                }
            }

            rig.LegLength_m = MathF.Max(0.2f, hipsHeight - (ankles == float.MaxValue ? 0f : ankles));

            if (clip is not null)
            {
                for (int t = 0; t < clip.GetTrackCount(); t++)
                {
                    int bone = rig.Find(clip.TrackGetPath(t).GetConcatenatedSubNames());
                    if (bone < 0)
                    {
                        continue;
                    }

                    switch (clip.TrackGetType(t))
                    {
                        case Animation.TrackType.Rotation3D:
                            rig.RotationTracks[bone] = t;
                            break;
                        case Animation.TrackType.Position3D:
                            rig.PositionTracks[bone] = t;
                            break;
                    }
                }
            }

            return rig;
        }
    }
}
