using System;
using Godot;
using Pb.Game.Core;

namespace Pb.Game.Player;

/// <summary>
/// Plays the animation set (<see cref="AnimSet"/>) on one character and blends it from the sim's movement, every frame,
/// before <see cref="CharacterPoser"/> bends the result to the hitboxes (Phase 5, M5.2):
/// <list type="bullet">
/// <item><b>Locomotion.</b> The body's velocity in its own frame picks the clips: by pace (walk 1.65, jog 3, run 5.5 m/s,
/// sprint 7 straight ahead) and by direction (forward, right, back, left; between two, both), all on one phase, so feet
/// stay in step through every blend. The phase moves on by the ground covered over the blended clips' own stride, so a
/// planted foot doesn't slide at any speed between theirs. Crouched, the crouched walk in the same four directions.</item>
/// <item><b>Standing still</b>: the idle (or crouched idle) on its own clock, blended in as the body slows.</item>
/// <item><b>Leaning</b>: the legs and hips brace (the lean poses); the sim's roll of the upper body is the poser's.</item>
/// <item><b>Refilling</b>: the refill's body, in step with the sim's refill (its hands are the poser's IK, on the pod).</item>
/// <item><b>Out</b>: hands up, standing tall, over whatever the legs are doing (walking off).</item>
/// <item><b>Hit</b>: the flinch, added on top for its length.</item>
/// </list>
/// Everything moves on by the frame's time and every weight follows its target at a capped rate, so it looks the same at
/// any frame rate.
/// </summary>
public partial class ClipBlender : SkeletonModifier3D
{
    /// <summary>The paces' clip families, in order of speed (their speeds are the data's, presentation.jsonc "animPaces").</summary>
    private static readonly string[] Paces = { "walk", "jog", "run" };

    /// <summary>The four directions, round from forward to the right (rad).</summary>
    private static readonly string[] Directions = { "f", "r", "b", "l" };

    private AnimSet _set = null!;
    private int _bones;
    private int _hips;
    private bool[] _upper = Array.Empty<bool>();
    private bool[] _lower = Array.Empty<bool>();
    private bool[] _hitBones = Array.Empty<bool>();
    private Quaternion[] _pose = Array.Empty<Quaternion>();
    private Quaternion[] _layer = Array.Empty<Quaternion>();
    private Vector3 _hipsAt;
    private Vector3 _layerHips;
    private readonly Vector3[] _ankles = new Vector3[2];
    private readonly Vector3[] _layerAnkles = new Vector3[2];
    private readonly Vector3[] _balls = new Vector3[2];
    private readonly Vector3[] _layerBalls = new Vector3[2];
    private float _layerWeight;
    private readonly BakedClip?[,] _moves = new BakedClip?[3, 4];
    private readonly BakedClip?[] _crouches = new BakedClip?[4];
    private BakedClip? _sprint, _idle, _crouchIdle, _leanLeft, _leanRight, _refill, _hit, _handsUp;
    private readonly float[] _paceSpeeds = { 1.65f, 3f, 5.5f };
    private float _sprintPace = 7f;
    private float _paceFollow = 200f;
    private float _pace, _paceRate;
    private float _sprintW, _sprintRate;
    private AnimBlendDef _blend = new();
    private float _idleTime;
    private float _handsTime;
    private float _hitAge = 10f;
    private float _hitStrength;
    private float _moving, _movingSpeed;
    private float _crouchW, _crouchSpeed;
    private float _refillW, _refillSpeed;
    private float _outW, _outSpeed;
    private float _leanW, _leanSpeed;
    private float _lastRefill;
    private int _debugFrame;
    private readonly float[] _dirW = { 1f, 0f, 0f, 0f };
    private readonly float[] _dirSpeed = new float[4];

    /// <summary>Prints the blend every few frames (PB_ANIM_DEBUG set).</summary>
    public static bool Debug { get; } = System.Environment.GetEnvironmentVariable("PB_ANIM_DEBUG") is { Length: > 0 };

    // What the character is doing (set every frame by CharacterVisual before the skeleton is posed).

    /// <summary>The body's velocity in its own frame (m/s): x to its right, z forward.</summary>
    public Vector2 Velocity { get; set; }

    /// <summary>How far crouched (0 standing, 1 fully), as drawn.</summary>
    public float Crouch { get; set; }

    public bool Sprinting { get; set; }

    /// <summary>The lean as drawn, as a share of the most (−1 full left, 1 full right).</summary>
    public float Lean { get; set; }

    /// <summary>How far through a refill (0-1), or −1 when not refilling.</summary>
    public float Refill { get; set; } = -1f;

    /// <summary>Out of the round (hands up).</summary>
    public bool Out { get; set; }

    /// <summary>On the ground (in the air, sliding or on a ladder the step cycle holds where it is).</summary>
    public bool Stepping { get; set; } = true;

    /// <summary>Where the step cycle is (cycles: 0 with the left foot landing).</summary>
    public float Phase { get; private set; }

    /// <summary>How far below their rest height the playing clips carry the hips, on average (m).</summary>
    public float PostureDrop { get; private set; }

    /// <summary>
    /// Where the clips put the left and right ankles (skeleton space: their own foot paths, scaled to this build), for the
    /// poser to set the feet on by IK: copied onto other proportions, the clips' leg rotations alone let the feet drift.
    /// </summary>
    public Vector3 LeftAnkle => _ankles[0];

    public Vector3 RightAnkle => _ankles[1];

    /// <summary>Where the clips put the balls of the left and right feet (skeleton space): what stays put on the ground while a foot is down.</summary>
    public Vector3 LeftBall => _balls[0];

    public Vector3 RightBall => _balls[1];

    /// <summary>Whether each foot is down in the clips now playing (0 left, 1 right), for the checks.</summary>
    public bool LeftDown { get; private set; } = true;

    public bool RightDown { get; private set; } = true;

    /// <summary>How much the step cycle is driving the body (0 standing, 1 moving).</summary>
    public float Moving => _moving;

    /// <summary>The flinch: a ball hit the body, harder for <paramref name="strength"/> near 1.</summary>
    public void Hit(float strength)
    {
        _hitAge = 0f;
        _hitStrength = Mathf.Clamp(strength, 0.3f, 1f);
    }

    /// <summary>
    /// Sets the blender up on <paramref name="skeleton"/> (the one the set was baked for), its step clips made for
    /// <paramref name="paces"/>, its weights eased as <paramref name="blend"/> says; false if the set lacks its locomotion.
    /// </summary>
    public bool Bind(Skeleton3D skeleton, AnimSet set, AnimPacesDef paces, AnimBlendDef blend)
    {
        _set = set;
        _blend = blend;
        _paceSpeeds[0] = paces.Walk_mps;
        _paceSpeeds[1] = paces.Jog_mps;
        _paceSpeeds[2] = paces.Run_mps;
        _sprintPace = paces.Sprint_mps;
        _paceFollow = paces.Follow_mps3;
        _bones = set.Bones;
        _hips = skeleton.FindBone("Hips");
        for (int p = 0; p < Paces.Length; p++)
        {
            for (int d = 0; d < Directions.Length; d++)
            {
                _moves[p, d] = set[$"{Paces[p]}_{Directions[d]}"];
            }
        }

        for (int d = 0; d < Directions.Length; d++)
        {
            _crouches[d] = set[$"crouch_{Directions[d]}"];
        }

        _sprint = set["sprint_f"];
        _idle = set["idle"];
        _crouchIdle = set["crouch_idle"];
        _leanLeft = set["lean_l"];
        _leanRight = set["lean_r"];
        _refill = set["refill"];
        _hit = set["hit"];
        _handsUp = set["hands_up"];
        _pose = new Quaternion[_bones];
        _layer = new Quaternion[_bones];
        _upper = new bool[_bones];
        _lower = new bool[_bones];
        _hitBones = new bool[_bones];
        for (int b = 0; b < _bones; b++)
        {
            string name = skeleton.GetBoneName(b);
            bool legs = name.Contains("UpLeg") || name.EndsWith("Leg") || name.Contains("Foot") || name.Contains("Toe");
            _lower[b] = legs || name == "Hips";
            _upper[b] = !_lower[b];
            _hitBones[b] = name is "Hips" or "Spine02" or "Spine01" or "Spine" or "neck" or "Head";
        }

        // Each character's own timing for the idle.
        _idleTime = (skeleton.GetInstanceId() % 997) / 997f * (_idle?.Length_s ?? 1f);
        bool complete = _idle is not null && _moves[0, 0] is not null;
        return complete;
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        Skeleton3D skeleton = GetSkeleton();
        if (skeleton is null || _set is null || _idle is null)
        {
            return;
        }

        float dt = Mathf.Clamp((float)delta, 0f, 0.25f);
        Advance(dt);
        Blend();
        for (int b = 0; b < _bones; b++)
        {
            skeleton.SetBonePoseRotation(b, _pose[b]);
        }

        if (_hips >= 0)
        {
            skeleton.SetBonePosePosition(_hips, _hipsAt);
        }
    }

    /// <summary>Moves every clock and weight on by <paramref name="dt"/>.</summary>
    private void Advance(float dt)
    {
        float speed = Velocity.Length();
        float walk = _paceSpeeds[0];
        // Moving: in from a fifth of the walk's speed, fully by three fifths; out the same way. At most a quarter-second
        // either way at full acceleration (Follow caps how fast the weight's own rate changes).
        float movingTarget = Stepping ? Mathf.SmoothStep(_blend.MovingFrom * walk, _blend.MovingBy * walk, speed) : _moving;
        Follow.Toward(ref _moving, ref _movingSpeed, movingTarget, _blend.Moving_ps2, dt);
        _moving = Mathf.Clamp(_moving, 0f, 1f);
        Follow.Toward(ref _crouchW, ref _crouchSpeed, Mathf.Clamp(Crouch, 0f, 1f), _blend.Crouch_ps2, dt);
        _crouchW = Mathf.Clamp(_crouchW, 0f, 1f);
        Follow.Toward(ref _leanW, ref _leanSpeed, Mathf.Clamp(Lean, -1f, 1f), _blend.Lean_ps2, dt);

        FollowDirections(dt);

        // The speed the clips are picked by: the body's, eased, so a stop goes down through the gaits instead of snapping
        // through all of them in the sim's fraction of a second.
        Follow.Toward(ref _pace, ref _paceRate, speed, _paceFollow, dt);
        _pace = Mathf.Max(_pace, 0f);
        // The sprint over the run eases in and out like the other weights (letting go of sprint mid-stride is no snap).
        Follow.Toward(ref _sprintW, ref _sprintRate, SprintShare(_pace), _blend.Sprint_ps2, dt);
        _sprintW = Mathf.Clamp(_sprintW, 0f, 1f);

        // The step cycle moves on by the ground covered over the blended clips' stride (each clip's own, scaled to this
        // build's legs), so planted feet stay put whatever the blend; while the steps blend out after stopping, it carries
        // on a little (the last step finishes rather than freezing).
        float ground = Mathf.Max(speed, _moving * _blend.LastStep * walk);
        if (Stepping && ground > 0.05f)
        {
            float stride = Stride(Mathf.Max(_pace, _blend.LastStep * walk));
            Phase += ground / Mathf.Max(stride, 0.2f) * dt;
            Phase -= Mathf.Floor(Phase);
        }

        _idleTime += dt;
        _handsTime += dt;
        _hitAge += dt;

        // The refill plays in step with the sim's; stopped early, it runs on to its end quickly.
        float refill = Refill;
        if (refill >= 0f)
        {
            _lastRefill = refill;
        }
        else if (_lastRefill < 1f)
        {
            _lastRefill = Mathf.MoveToward(_lastRefill, 1f, dt * 2.5f);
        }

        float refillTarget = refill >= 0f || _lastRefill < 1f ? 1f : 0f;
        Follow.Toward(ref _refillW, ref _refillSpeed, refillTarget, _blend.Refill_ps2, dt);
        _refillW = Mathf.Clamp(_refillW, 0f, 1f);
        Follow.Toward(ref _outW, ref _outSpeed, Out ? 1f : 0f, _blend.Out_ps2, dt);
        _outW = Mathf.Clamp(_outW, 0f, 1f);
    }

    /// <summary>The ground one cycle covers for the clips blended at this speed and direction (m).</summary>
    private float Stride(float speed)
    {
        float stand = 0f, crouch = 0f, total = 0f, crouchTotal = 0f;
        Span<float> w = stackalloc float[4];
        DirectionWeights(w);
        for (int d = 0; d < 4; d++)
        {
            if (w[d] <= 0f)
            {
                continue;
            }

            (int lo, int hi, float t) = PaceAt(speed);
            float a = _moves[lo, d]?.CycleDistance_m ?? 0f, b = _moves[hi, d]?.CycleDistance_m ?? a;
            float s = Mathf.Lerp(a, b, t);
            if (d == 0 && _sprintW > 0f && _sprint is not null)
            {
                s = Mathf.Lerp(s, _sprint.CycleDistance_m, _sprintW);
            }

            stand += s * w[d];
            total += w[d];
            float c = _crouches[d]?.CycleDistance_m ?? 0f;
            crouch += c * w[d];
            crouchTotal += c > 0f ? w[d] : 0f;
        }

        float standing = total > 0f ? stand / total : _moves[0, 0]!.CycleDistance_m;
        float crouched = crouchTotal > 0f ? crouch / crouchTotal : standing;
        return Mathf.Lerp(standing, crouched, _crouchW);
    }

    /// <summary>
    /// How much of each direction (forward, right, back, left) is playing: each follows the velocity's own share
    /// (<see cref="TargetDirections"/>) at a capped rate, so a strafe reversing blends through rather than the legs' turn
    /// snapping from one side to the other in a frame.
    /// </summary>
    private void DirectionWeights(Span<float> w)
    {
        float sum = 0f;
        for (int d = 0; d < 4; d++)
        {
            sum += _dirW[d];
        }

        for (int d = 0; d < 4; d++)
        {
            w[d] = sum > 1e-4f ? _dirW[d] / sum : (d == 0 ? 1f : 0f);
        }
    }

    /// <summary>Moves the direction weights on towards the velocity's own shares.</summary>
    private void FollowDirections(float dt)
    {
        Span<float> target = stackalloc float[4];
        TargetDirections(target);
        for (int d = 0; d < 4; d++)
        {
            Follow.Toward(ref _dirW[d], ref _dirSpeed[d], target[d], _blend.Direction_ps2, dt);
            _dirW[d] = Mathf.Clamp(_dirW[d], 0f, 1f);
        }
    }

    /// <summary>How much of each direction (forward, right, back, left) the velocity is: the two it falls between.</summary>
    private void TargetDirections(Span<float> w)
    {
        w.Clear();
        if (Velocity.LengthSquared() < 0.04f)
        {
            // Too slow to say: as it was.
            for (int d = 0; d < 4; d++)
            {
                w[d] = _dirW[d];
            }

            return;
        }

        // Round from forward to the right, in quarter turns.
        float quarter = Mathf.PosMod(Mathf.Atan2(Velocity.X, Velocity.Y) / (Mathf.Pi * 0.5f), 4f);
        int a = (int)quarter % 4, b = (a + 1) % 4;
        float t = quarter - Mathf.Floor(quarter);
        // Eased, so each pure direction holds a little either side of it.
        t = Mathf.SmoothStep(0f, 1f, t);
        w[a] = 1f - t;
        w[b] += t;
    }

    /// <summary>
    /// Between which paces' clips <paramref name="speed"/> falls, and how far: by the speeds they were made for, not as
    /// scaled to this build (at the sim's own speeds a clip plays alone: two paces' feet are down at different times, so a
    /// blend of them slides).
    /// </summary>
    private (int Lo, int Hi, float T) PaceAt(float speed)
    {
        for (int p = 0; p < Paces.Length - 1; p++)
        {
            float lo = _paceSpeeds[p], hi = _paceSpeeds[p + 1];
            if (speed <= hi || p == Paces.Length - 2)
            {
                return (p, p + 1, Mathf.Clamp((speed - lo) / Mathf.Max(hi - lo, 0.05f), 0f, 1f));
            }
        }

        return (0, 0, 0f);
    }

    /// <summary>How much the sprint takes over the run: only sprinting, faster than the run's clip, going forward.</summary>
    private float SprintShare(float speed)
    {
        if (!Sprinting || _sprint is null || _moves[2, 0] is null)
        {
            return 0f;
        }

        float forward = Velocity.Y / Mathf.Max(Velocity.Length(), 1e-3f);
        float run = _paceSpeeds[2];
        return Mathf.Clamp((speed - run) / Mathf.Max(_sprintPace - run, 0.1f), 0f, 1f) * Mathf.SmoothStep(0.6f, 0.9f, forward);
    }

    /// <summary>This frame's pose: the layers, bottom to top.</summary>
    private void Blend()
    {
        // Standing still: the idle, the crouched idle.
        BakedClip.Cursor idle = _idle!.At(_idleTime / _idle.Length_s);
        Sample(_idle, idle, _pose, out _hipsAt, _ankles, _balls);
        float drop = _idle.HipsDrop_m;
        if (_crouchIdle is not null && _crouchW > 0.001f)
        {
            BakedClip.Cursor c = _crouchIdle.At(_idleTime / _crouchIdle.Length_s);
            Sample(_crouchIdle, c, _layer, out _layerHips, _layerAnkles, _layerBalls);
            Mix(_layer, _layerHips, _crouchW, null);
            drop = Mathf.Lerp(drop, _crouchIdle.HipsDrop_m, _crouchW);
        }

        bool left = true, right = true;
        // Moving: the step cycle over it.
        if (_moving > 0.001f)
        {
            float speed = _pace;
            Span<float> w = stackalloc float[4];
            DirectionWeights(w);
            float moveDrop = 0f;
            float bestWeight = 0f;
            BakedClip? best = null;
            // The standing step clips, then the crouched ones over them, into the layer, then the layer over the pose.
            bool first = true;
            for (int d = 0; d < 4; d++)
            {
                if (w[d] <= 0.001f)
                {
                    continue;
                }

                (int lo, int hi, float t) = PaceAt(speed);
                float sprint = d == 0 && _sprint is not null ? _sprintW : 0f;
                Accumulate(_moves[lo, d], w[d] * (1f - t) * (1f - sprint) * (1f - _crouchW), ref first, ref moveDrop, ref best, ref bestWeight);
                Accumulate(_moves[hi, d], w[d] * t * (1f - sprint) * (1f - _crouchW), ref first, ref moveDrop, ref best, ref bestWeight);
                if (sprint > 0f)
                {
                    Accumulate(_sprint, w[d] * sprint * (1f - _crouchW), ref first, ref moveDrop, ref best, ref bestWeight);
                }

                Accumulate(_crouches[d], w[d] * _crouchW, ref first, ref moveDrop, ref best, ref bestWeight);
            }

            if (!first)
            {
                moveDrop /= Mathf.Max(_layerWeight, 1e-6f);
                Normalise(_layer);
                Mix(_layer, _layerHips, _moving, null);
                drop = Mathf.Lerp(drop, moveDrop, _moving);
            }

            if (best is not null && _moving > 0.5f)
            {
                BakedClip.Cursor at = best.At(Phase);
                left = best.Down(at, 0);
                right = best.Down(at, 1);
            }
        }

        LeftDown = left;
        RightDown = right;
        PostureDrop = drop;
        if (Debug && (_debugFrame++ % 6) == 0)
        {
            GD.Print($"ANIM v=({Velocity.X:0.00},{Velocity.Y:0.00}) crouch {_crouchW:0.00} moving {_moving:0.00} phase {Phase:0.000} pace {_pace:0.00} stride {Stride(_pace):0.00} down {left}/{right} drop {drop:0.00}");
        }

        // Leaning: the legs and hips brace, mostly standing still.
        float lean = Mathf.Abs(_leanW) * (1f - 0.6f * _moving);
        BakedClip? leanClip = _leanW < 0f ? _leanLeft : _leanRight;
        if (leanClip is not null && lean > 0.001f)
        {
            Sample(leanClip, leanClip.At(0f), _layer, out _layerHips, _layerAnkles, _layerBalls);
            Mix(_layer, _layerHips, lean, _lower);
        }

        // Refilling: the body bows to watch the loader (the hands are the poser's).
        if (_refill is not null && _refillW > 0.001f)
        {
            float through = Refill >= 0f ? Refill : _lastRefill;
            Sample(_refill, _refill.At(through), _layer, out _layerHips, _layerAnkles, _layerBalls);
            Mix(_layer, _layerHips, _refillW, _upper);
        }

        // Out: hands up, standing tall, whatever the legs do.
        if (_handsUp is not null && _outW > 0.001f)
        {
            Sample(_handsUp, _handsUp.At(_handsTime / _handsUp.Length_s), _layer, out _layerHips, _layerAnkles, _layerBalls);
            Mix(_layer, _layerHips, _outW, _upper);
        }

        // A hit: the flinch, as it turns each bone from its first frame, added on top.
        if (_hit is not null && _hitAge < _hit.Length_s)
        {
            BakedClip.Cursor now = _hit.At(_hitAge / _hit.Length_s), start = _hit.At(0f);
            for (int b = 0; b < _bones; b++)
            {
                if (_hitBones[b])
                {
                    Quaternion turn = _hit.Rotation(now, b) * _hit.Rotation(start, b).Inverse();
                    _pose[b] = (Quaternion.Identity.Slerp(turn, _hitStrength) * _pose[b]).Normalized();
                }
            }

            _hipsAt += (_hit.Hips(now) - _hit.Hips(start)) * _hitStrength;
        }
    }

    /// <summary>Adds <paramref name="weight"/> of <paramref name="clip"/> at the step phase into the layer.</summary>
    private void Accumulate(BakedClip? clip, float weight, ref bool first, ref float drop, ref BakedClip? best, ref float bestWeight)
    {
        if (clip is null || weight <= 0.0005f)
        {
            return;
        }

        BakedClip.Cursor at = clip.At(Phase);
        if (first)
        {
            for (int b = 0; b < _bones; b++)
            {
                _layer[b] = new Quaternion(0f, 0f, 0f, 0f);
            }

            _layerHips = Vector3.Zero;
            _layerAnkles[0] = _layerAnkles[1] = Vector3.Zero;
            _layerBalls[0] = _layerBalls[1] = Vector3.Zero;
            _layerWeight = 0f;
            first = false;
        }

        for (int b = 0; b < _bones; b++)
        {
            Quaternion q = clip.Rotation(at, b);
            // Same hemisphere as what's there, so the weighted sum blends rather than cancels.
            if (_layer[b].Dot(q) < 0f)
            {
                q = -q;
            }

            _layer[b] += q * weight;
        }

        _layerHips += clip.Hips(at) * weight;
        _layerAnkles[0] += clip.Ankle(at, 0) * weight;
        _layerAnkles[1] += clip.Ankle(at, 1) * weight;
        _layerBalls[0] += clip.Ball(at, 0) * weight;
        _layerBalls[1] += clip.Ball(at, 1) * weight;
        _layerWeight += weight;
        drop += clip.HipsDrop_m * weight;
        if (weight > bestWeight)
        {
            bestWeight = weight;
            best = clip;
        }
    }

    /// <summary>Scales the summed layer to unit quaternions and its hips by the total weight.</summary>
    private void Normalise(Quaternion[] layer)
    {
        for (int b = 0; b < _bones; b++)
        {
            float length = layer[b].Length();
            layer[b] = length > 1e-6f ? layer[b] / length : Quaternion.Identity;
        }

        float total = Mathf.Max(_layerWeight, 1e-6f);
        _layerHips /= total;
        _layerAnkles[0] /= total;
        _layerAnkles[1] /= total;
        _layerBalls[0] /= total;
        _layerBalls[1] /= total;
    }

    /// <summary>Every bone of a clip at a cursor, into <paramref name="into"/>, and its feet into <paramref name="ankles"/> and <paramref name="balls"/>.</summary>
    private void Sample(BakedClip clip, in BakedClip.Cursor at, Quaternion[] into, out Vector3 hips, Vector3[] ankles, Vector3[] balls)
    {
        for (int b = 0; b < _bones; b++)
        {
            into[b] = clip.Rotation(at, b);
        }

        hips = clip.Hips(at);
        ankles[0] = clip.Ankle(at, 0);
        ankles[1] = clip.Ankle(at, 1);
        balls[0] = clip.Ball(at, 0);
        balls[1] = clip.Ball(at, 1);
    }

    /// <summary>Lays <paramref name="weight"/> of a layer over the pose, on the bones in <paramref name="mask"/> (null: all).</summary>
    private void Mix(Quaternion[] layer, Vector3 hips, float weight, bool[]? mask)
    {
        weight = Mathf.Clamp(weight, 0f, 1f);
        for (int b = 0; b < _bones; b++)
        {
            if (mask is null || mask[b])
            {
                _pose[b] = _pose[b].Slerp(layer[b], weight);
            }
        }

        if (mask is null || (_hips >= 0 && mask[_hips]))
        {
            _hipsAt = _hipsAt.Lerp(hips, weight);
            _ankles[0] = _ankles[0].Lerp(_layerAnkles[0], weight);
            _ankles[1] = _ankles[1].Lerp(_layerAnkles[1], weight);
            _balls[0] = _balls[0].Lerp(_layerBalls[0], weight);
            _balls[1] = _balls[1].Lerp(_layerBalls[1], weight);
        }
    }
}
