using Godot;

namespace Pb.Game.Player;

/// <summary>
/// A movement clip (walk, run, crouched walk) baked for one rig by <see cref="MoveClipBaker"/>: every
/// bone's local rotation and the hips' position at even steps through one cycle, and what its feet
/// imply: the ground speed at which they don't slide, and where in the cycle the left foot is under
/// the body. Clips are played by the ground covered and sampled by phase (cycles, 0 with the left foot
/// under the body), so a walk and a run blend in step.
/// </summary>
public sealed class MoveClip
{
    private readonly Quaternion[] _rotations;
    private readonly Vector3[] _hips;
    private readonly int _bones;
    private readonly int _frames;
    private readonly float _anchor;

    public MoveClip(string name, int bones, Quaternion[] rotations, Vector3[] hips, float length, float speed, float anchor, float hipsDrop)
    {
        Name = name;
        _bones = bones;
        _rotations = rotations;
        _hips = hips;
        _frames = hips.Length;
        _anchor = anchor;
        Length_s = length;
        Speed_mps = speed;
        HipsDrop_m = hipsDrop;
    }

    public string Name { get; }

    /// <summary>The rig's bone count: bone indices are the skeleton's it was baked for.</summary>
    public int Bones => _bones;

    /// <summary>One cycle (s).</summary>
    public float Length_s { get; }

    /// <summary>The ground speed at which its feet don't slide (m/s).</summary>
    public float Speed_mps { get; }

    /// <summary>Ground covered in one cycle at that speed (m).</summary>
    public float CycleDistance_m => Speed_mps * Length_s;

    /// <summary>How far the hips sit below their rest height, on average over the cycle (m).</summary>
    public float HipsDrop_m { get; }

    /// <summary>Where <paramref name="phase"/> falls among the baked frames.</summary>
    public Cursor At(float phase)
    {
        float f = (phase + _anchor) * _frames;
        f -= Mathf.Floor(f / _frames) * _frames;
        int a = Mathf.Min((int)f, _frames - 1);
        return new Cursor(a, a + 1 == _frames ? 0 : a + 1, f - a);
    }

    /// <summary>A bone's local rotation (its pose relative to its parent) at the cursor.</summary>
    public Quaternion Rotation(in Cursor at, int bone) =>
        _rotations[at.A * _bones + bone].Slerp(_rotations[at.B * _bones + bone], at.T);

    /// <summary>The hips' position (skeleton space) at the cursor.</summary>
    public Vector3 Hips(in Cursor at) => _hips[at.A].Lerp(_hips[at.B], at.T);

    /// <summary>Two neighbouring baked frames and how far between them.</summary>
    public readonly record struct Cursor(int A, int B, float T);
}
