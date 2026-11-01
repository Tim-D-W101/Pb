using System.Collections.Generic;
using Godot;

namespace Pb.Game.Player;

/// <summary>
/// The characters' animation set baked onto one model (<see cref="MoveClipBaker.BakeSet"/>): every clip by name
/// (presentation.jsonc "characters" → "animSet"; the clips themselves are made by tools/anim/build_clips.py).
/// </summary>
public sealed class AnimSet
{
    private readonly Dictionary<string, BakedClip> _clips;

    public AnimSet(Dictionary<string, BakedClip> clips, int bones)
    {
        _clips = clips;
        Bones = bones;
    }

    /// <summary>The rig's bone count: bone indices are the skeleton's the set was baked for.</summary>
    public int Bones { get; }

    public int Count => _clips.Count;

    /// <summary>The clip called <paramref name="name"/>, or null when the set hasn't one.</summary>
    public BakedClip? this[string name] => _clips.TryGetValue(name, out BakedClip? clip) ? clip : null;

    public IEnumerable<string> Names => _clips.Keys;
}

/// <summary>
/// One clip of the set baked for one rig: every bone's local rotation and the hips' position at even steps through it,
/// its length and whether it loops. A step cycle also knows the ground it covers in one cycle at which its planted feet
/// don't slide (measured from the baked feet), when each foot is down, and how far the hips sit below their rest height.
/// Loops are sampled by phase (cycles; every step cycle starts with the left foot landing, so a family blends in step),
/// one-shots by how far through they are (0-1).
/// </summary>
public sealed class BakedClip
{
    private readonly Quaternion[] _rotations;
    private readonly Vector3[] _hips;
    private readonly bool[] _leftDown;
    private readonly bool[] _rightDown;
    private readonly Vector3[] _ankles;
    private readonly Vector3[] _balls;
    private readonly int _bones;
    private readonly int _frames;

    public BakedClip(string name, int bones, Quaternion[] rotations, Vector3[] hips, float length, bool loops, float cycleDistance,
        float hipsDrop, bool[] leftDown, bool[] rightDown, Vector3[] ankles, Vector3[] balls)
    {
        _ankles = ankles;
        _balls = balls;
        Name = name;
        _bones = bones;
        _rotations = rotations;
        _hips = hips;
        _frames = hips.Length;
        _leftDown = leftDown;
        _rightDown = rightDown;
        Length_s = length;
        Loops = loops;
        CycleDistance_m = cycleDistance;
        HipsDrop_m = hipsDrop;
    }

    public string Name { get; }

    /// <summary>One cycle, or the whole clip (s).</summary>
    public float Length_s { get; }

    public bool Loops { get; }

    /// <summary>Ground covered in one cycle, its planted feet still (m: the speed it was made for, scaled to these legs); 0 for a clip that doesn't step.</summary>
    public float CycleDistance_m { get; }

    /// <summary>The ground speed it was made for (m/s).</summary>
    public float Speed_mps => Length_s > 0f ? CycleDistance_m / Length_s : 0f;

    /// <summary>How far the hips sit below their rest height, on average (m).</summary>
    public float HipsDrop_m { get; }

    /// <summary>Where <paramref name="phase"/> (cycles for a loop; 0-1 through a one-shot) falls among the baked frames.</summary>
    public Cursor At(float phase)
    {
        if (!Loops)
        {
            float g = Mathf.Clamp(phase, 0f, 1f) * (_frames - 1);
            int i = Mathf.Min((int)g, _frames - 2);
            return new Cursor(i, i + 1, g - i);
        }

        float f = phase * _frames;
        f -= Mathf.Floor(f / _frames) * _frames;
        int a = Mathf.Min((int)f, _frames - 1);
        return new Cursor(a, a + 1 == _frames ? 0 : a + 1, f - a);
    }

    /// <summary>A bone's local rotation (its pose relative to its parent) at the cursor.</summary>
    public Quaternion Rotation(in Cursor at, int bone) =>
        _rotations[at.A * _bones + bone].Slerp(_rotations[at.B * _bones + bone], at.T);

    /// <summary>The hips' position (skeleton space) at the cursor.</summary>
    public Vector3 Hips(in Cursor at) => _hips[at.A].Lerp(_hips[at.B], at.T);

    /// <summary>
    /// Where the left (0) or right (1) ankle goes at the cursor (the target's skeleton space; from the point under the
    /// body, scaled to its legs): the clip's own foot path, for the poser's IK.
    /// </summary>
    public Vector3 Ankle(in Cursor at, int foot) => _ankles[at.A * 2 + foot].Lerp(_ankles[at.B * 2 + foot], at.T);

    /// <summary>Where the ball of the left (0) or right (1) foot is at the cursor, likewise: still on the ground while the foot is down.</summary>
    public Vector3 Ball(in Cursor at, int foot) => _balls[at.A * 2 + foot].Lerp(_balls[at.B * 2 + foot], at.T);

    /// <summary>Whether the left (0) or right (1) foot is down at the cursor (on the ground, going from frame A to B).</summary>
    public bool Down(in Cursor at, int foot) => (foot == 0 ? _leftDown : _rightDown)[at.A];

    /// <summary>Two neighbouring baked frames and how far between them.</summary>
    public readonly record struct Cursor(int A, int B, float T);
}
