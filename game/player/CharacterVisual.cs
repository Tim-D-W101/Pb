using Godot;
using Pb.Game.Ballistics;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Collision;
using Pb.Sim.Gear;
using Pb.Sim.Level;
using Pb.Sim.Players;

namespace Pb.Game.Player;

/// <summary>
/// Draws a character from the sim's hitbox rig, interpolated between ticks. With a rigged model
/// (<see cref="CharacterModel"/>) the body is the model, posed to match the hitboxes, and the gear
/// (marker, loader, tank) is the player's kit: the generated marker model (<see cref="MarkerModel"/>) held
/// along the marker's box for the field's marker, or each item built in code fitted inside its own box
/// (<see cref="GearModels"/>), with the hands on the marker's grips; without a model, every hitbox is drawn as a box, so what you see is
/// exactly what you can hit. Each part is an unscaled node with the box or shapes under it, so splats
/// parented to a part keep their shape and move with it.
/// </summary>
public partial class CharacterVisual : Node3D, Pb.Game.Ballistics.IPaintTarget
{
    private readonly Node3D[] _parts = new Node3D[HitboxRig.PartCount];
    private readonly MeshInstance3D?[] _meshes = new MeshInstance3D?[HitboxRig.PartCount];
    private readonly Vector3[] _half = new Vector3[HitboxRig.PartCount];
    private readonly int[] _indexOfPart = new int[16];
    private PosedBox[] _previous = new PosedBox[HitboxRig.PartCount];
    private PosedBox[] _current = new PosedBox[HitboxRig.PartCount];
    private HitboxPose _poseBefore;
    private HitboxPose _poseNow;
    private SimWorld _sim = null!;
    private PlayerState _state = null!;
    private CharacterModel? _model;
    private CharactersDef? _look;
    private float _standEye;
    private float _refillPhase = 1f;
    private Vector3 _flinchPush;
    private float _flinchAge = 10f;
    private float _kneesOut;
    private float _elbowsOut;
    private LadderLimbs? _limbs;
    private float _flinchStrength;
    private float _breathPhase;
    private float _exertion;
    // Out of the round: how long ago, and the gear's boxes as they were the tick before (the raise starts from them).
    private float _outAge = -1f;
    private readonly PosedBox[] _lastIn = new PosedBox[HitboxRig.PartCount];
    private bool _wasIn;
    private Color _paint = Colors.White;
    private Node3D? _pod;
    private float _crouchEye;
    private Vector3 _lastFeet;
    private bool _shadowOnly;
    private Node3D? _marker;
    private MarkerModelDef? _markerDef;
    private GearModels.Worn? _markerWorn;
    private readonly GeometryInstance3D?[] _gear = new GeometryInstance3D?[3];
    private readonly Pb.Game.Ballistics.PaintSlots?[] _gearPaint = new Pb.Game.Ballistics.PaintSlots?[3];
    private readonly System.Collections.Generic.Dictionary<GeometryInstance3D, (GeometryInstance3D.ShadowCastingSetting Cast, bool Visible)> _drawn = new();
    private StepGait.GroundQuery _ground = null!;

    public bool HasModel => _model is not null;

    /// <summary>The drawn model, if the character has one (null: hitbox boxes).</summary>
    public CharacterModel? Model => _model;

    /// <summary>Whether the generated marker is drawn: one piece in its own finish, its loader and tank with it.</summary>
    public bool WearsMarkerModel => _marker is not null;

    /// <summary>
    /// Only the character's shadow shows (your own body in first person): the model casts its shadow but
    /// isn't drawn, and the gear is left out with the arms in the clips' carry, so no shadow of a marker
    /// that isn't drawn falls across the one in your hands. False draws all of it.
    /// </summary>
    public bool ShadowOnly
    {
        get => _shadowOnly;
        set
        {
            _shadowOnly = value;
            if (_model is null)
            {
                return;
            }

            foreach (Node node in _model.FindChildren("*", nameof(GeometryInstance3D), true, false))
            {
                if (node is GeometryInstance3D geometry)
                {
                    Draw(geometry);
                }
            }

            foreach (HitboxPart gear in new[] { HitboxPart.Marker, HitboxPart.Loader, HitboxPart.Tank })
            {
                _parts[_indexOfPart[(int)gear]].Visible = !value;
            }

            _model.Poser.HandsOnMarker = !value;
        }
    }

    /// <param name="jersey">The player's paint colour: the armbands, the paint in a see-through loader, the boxes without a model.</param>
    /// <param name="kit">What they wear: their character (which model) and their gear, in its colours.</param>
    /// <param name="index">Which tint: each further copy of a model gets the next.</param>
    /// <param name="marker">The generated marker model, for the field's marker; null (or its art missing) draws the marker built in code.</param>
    public void Build(SimWorld sim, PlayerState state, Color jersey, Kit kit, CharactersDef? characters = null, int index = 0, MarkerModelDef? marker = null)
    {
        _sim = sim;
        _state = state;
        _look = characters;
        _ground = Ground;
        _paint = jersey;
        _standEye = sim.Config.Movement.StandEyeHeight;
        _crouchEye = sim.Config.Movement.CrouchEyeHeight;
        TopLevel = true;
        var box = new BoxMesh { Size = Vector3.One };
        _sim.PlayerHits.PoseNow(state, _current);
        _poseNow = _poseBefore = HitboxPose.Of(state);
        _lastFeet = state.Position.ToGodot();
        if (characters is { Models.Length: > 0 })
        {
            int copy = index / characters.Models.Length;
            Color tint = characters.Tints.Length > 0 ? Color.FromHtml(characters.Tints[copy % characters.Tints.Length]) : Colors.White;
            int look = ((kit.Loadout.Character % characters.Models.Length) + characters.Models.Length) % characters.Models.Length;
            _model = CharacterModel.TryCreate(characters.Models[look], tint, jersey, characters);
            if (_model is not null)
            {
                AddChild(_model);
                _model.Dress(kit);
                // Their own timing for breathing and shifting weight.
                _model.Steps?.Vary(state.Id * 7919 + index);
                _breathPhase = (state.Id * 0.618034f) % 1f;
            }
        }

        // Gear keeps its size, so its model or meshes are built once: the generated marker (loader and bottle
        // included) along the marker's box with its muzzle at the box's front, or each item built in code fitted
        // into its own box (shared by everyone wearing it, in this player's colours).
        GearModels.Prepare(kit.Gear);
        GearItem markerItem = kit.Item(GearSlot.Marker);
        if (_model is not null && marker is not null && markerItem.Model == marker.Model)
        {
            foreach (PosedBox gear in _current)
            {
                if (gear.Part == HitboxPart.Marker)
                {
                    _marker = MarkerModel.Create(marker, new Vector3(0f, marker.MuzzleAbove_m, -gear.HalfExtents.Z));
                    _markerDef = _marker is null ? null : marker;
                }
            }
        }

        for (int i = 0; i < _parts.Length; i++)
        {
            HitboxPart part = _current[i].Part;
            _indexOfPart[(int)part] = i;
            _parts[i] = new Node3D { Name = part.ToString() };
            AddChild(_parts[i]);
            if (_model is not null && IsGear(part))
            {
                GearSlot slot = part switch
                {
                    HitboxPart.Loader => GearSlot.Loader,
                    HitboxPart.Tank => GearSlot.Tank,
                    _ => GearSlot.Marker,
                };
                if (_marker is not null && part == HitboxPart.Marker)
                {
                    _parts[i].AddChild(_marker);
                }
                else if (_marker is null || !kit.Gear.Included(kit.Loadout, slot))
                {
                    GearModels.Worn worn = GearModels.Fit(kit.Item(slot), _current[i].HalfExtents.ToGodot() * 2f);
                    _gear[(int)slot] = GearModels.Instance(slot.ToString(), worn.Mesh, kit.Colours(slot), jersey);
                    _parts[i].AddChild(_gear[(int)slot]);
                    if (slot == GearSlot.Marker)
                    {
                        _markerWorn = worn;
                    }
                }

                continue;
            }

            _meshes[i] = new MeshInstance3D { Mesh = box, MaterialOverride = MaterialFor(part, jersey), Visible = _model is null };
            _parts[i].AddChild(_meshes[i]);
        }

        Capture();
        ApplyPose(1f);
    }

    /// <summary>
    /// Repaints what they wear in <paramref name="kit"/>'s colours: the same items as built, so only the colours may
    /// differ (the locker, as you pick them; a new item or character is built again).
    /// </summary>
    public void Recolour(Kit kit)
    {
        foreach (GearSlot slot in new[] { GearSlot.Marker, GearSlot.Loader, GearSlot.Tank })
        {
            if (_gear[(int)slot] is { } instance)
            {
                GearModels.Colour(instance, kit.Colours(slot), _paint);
            }
        }

        _model?.Recolour(kit);
    }

    /// <summary>Whether they hold their marker up (true) or have it out of the way with their hands free (the gear demo's head close-ups).</summary>
    public bool HoldsMarker
    {
        set
        {
            foreach (HitboxPart gear in new[] { HitboxPart.Marker, HitboxPart.Loader, HitboxPart.Tank })
            {
                _parts[_indexOfPart[(int)gear]].Visible = value;
            }

            if (_model is not null)
            {
                _model.Poser.HandsOnMarker = value;
            }
        }
    }

    /// <summary>
    /// Paint on them, as their shaders draw it: on their marker, loader or tank in the item's own frame, on the rest of
    /// them through the model (<see cref="CharacterModel.Paint"/>). Its handle, or 0 where it can't (the generated
    /// marker, or a model that isn't the generator's rig, or none): a decal, then.
    /// </summary>
    public int Paint(int part, in Pb.Game.Ballistics.Splat splat, Pb.Game.Ballistics.WetSplats splats, SplatDef def)
    {
        var hit = (HitboxPart)part;
        GearSlot? slot = hit switch
        {
            HitboxPart.Marker => GearSlot.Marker,
            HitboxPart.Loader => GearSlot.Loader,
            HitboxPart.Tank => GearSlot.Tank,
            _ => null,
        };
        if (slot is not { } s)
        {
            return _model?.Paint(hit, splat, splats, def) ?? 0;
        }

        if (_gear[(int)s] is not MeshInstance3D piece)
        {
            return 0;
        }

        _gearPaint[(int)s] ??= GearModels.Paintable(piece, splats, def);
        return _gearPaint[(int)s] is { } slots ? GearModels.Paint(slots, piece, splat, 0.05f) : 0;
    }

    /// <summary>Takes splat <paramref name="handle"/> off them, wherever it is on them.</summary>
    public void Withdraw(int handle)
    {
        foreach (Pb.Game.Ballistics.PaintSlots? slots in _gearPaint)
        {
            if (slots?.Withdraw(handle) == true)
            {
                return;
            }
        }

        _model?.Withdraw(handle);
    }

    /// <summary>What a splat at <paramref name="point"/> on <paramref name="part"/> should stick to.</summary>
    public SplatAnchor PartNode(HitboxPart part, Vector3 point) =>
        _model?.PartNode(part, point) ?? new SplatAnchor(_parts[_indexOfPart[(int)part]]);

    /// <summary>Call once per sim tick, after the player moved: the pose to interpolate towards.</summary>
    public void Capture()
    {
        (_previous, _current) = (_current, _previous);
        _sim.PlayerHits.PoseNow(_state, _current);
        _poseBefore = _poseNow;
        _poseNow = HitboxPose.Of(_state);
        if (_poseNow.Alive)
        {
            _outAge = -1f;
            System.Array.Copy(_current, _lastIn, _current.Length);
            _wasIn = true;
        }
        else if (_outAge < 0f)
        {
            // Out already when first drawn: nothing to raise from, so the gear's up from the start.
            _outAge = _wasIn ? 0f : 60f;
        }
    }

    public override void _Process(double delta)
    {
        if (_state is not null)
        {
            Visible = _state.Present;
            if (_outAge >= 0f)
            {
                _outAge += (float)delta;
            }

            ApplyPose((float)Engine.GetPhysicsInterpolationFraction());
        }
    }

    private void ApplyPose(float alpha)
    {
        // Just out, the gear comes up over the head over a moment (the sim has it up at once; nothing counts on it now).
        float raise = Raise();
        bool raising = _outAge >= 0f && raise < 1f;
        for (int i = 0; i < _parts.Length; i++)
        {
            ref PosedBox a = ref _previous[i];
            ref PosedBox b = ref _current[i];
            float t = alpha;
            if (raising && IsGearOrArms(b.Part))
            {
                a = ref _lastIn[i];
                t = raise;
            }

            Quaternion q = Quat(a).Slerp(Quat(b), t);
            Vector3 centre = a.Center.ToGodot().Lerp(b.Center.ToGodot(), t);
            _parts[i].GlobalTransform = new Transform3D(new Basis(q), centre);
            _half[i] = a.HalfExtents.ToGodot().Lerp(b.HalfExtents.ToGodot(), alpha);
            if (_meshes[i] is { } mesh)
            {
                mesh.Scale = _half[i] * 2f;
            }
        }

        if (_model is not null && _look is not null)
        {
            PoseModel(alpha);
        }
    }

    /// <summary>Stands the model where the player stands and hands the poser this frame's pose.</summary>
    private void PoseModel(float alpha)
    {
        Vector3 feet = _poseBefore.Position.ToGodot().Lerp(_poseNow.Position.ToGodot(), alpha);
        float yaw = Mathf.LerpAngle(_poseBefore.Yaw, _poseNow.Yaw, alpha);
        float eye = Mathf.Lerp(_poseBefore.EyeHeight, _poseNow.EyeHeight, alpha);
        _model!.GlobalTransform = new Transform3D(new Basis(Vector3.Up, yaw), feet);

        CharacterPoser poser = _model.Poser;
        poser.Forward = new Vector3(-Mathf.Sin(yaw), 0f, -Mathf.Cos(yaw));
        poser.Right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
        poser.HipDrop = Mathf.Max(0f, _standEye - eye) * _look!.HipDropPerEyeDrop;
        poser.LeanRoll = Mathf.Lerp(_poseBefore.LeanRoll, _poseNow.LeanRoll, alpha);
        poser.Pitch = _poseNow.Alive ? Mathf.Lerp(_poseBefore.Pitch, _poseNow.Pitch, alpha) : 0f;
        // A flinch snaps the upper body away from the hit and eases it back.
        _flinchAge += (float)GetProcessDeltaTime();
        float flinch = _flinchAge < 0.04f ? _flinchAge / 0.04f : Mathf.Exp(-(_flinchAge - 0.04f) / 0.15f);
        poser.Flinch = flinch > 0.01f ? Vector3.Up.Cross(_flinchPush) * (_flinchStrength * flinch) : Vector3.Zero;
        poser.RightHanded = Mathf.Lerp(_poseBefore.Shoulder, _poseNow.Shoulder, alpha) >= 0f;
        poser.HeadYaw = _poseNow.Alive ? Mathf.LerpAngle(_poseBefore.HeadYaw, _poseNow.HeadYaw, alpha) : 0f;
        poser.Breath = Breathe((float)GetProcessDeltaTime());
        poser.SupportRaise = _shadowOnly ? 0f : Raise();

        // Wrists on the marker: the trigger hand at the pistol grip, the other under the front.
        int marker = _indexOfPart[(int)HitboxPart.Marker];
        Transform3D frame = _parts[marker].GlobalTransform;
        if (_marker is not null && _markerDef is not null)
        {
            poser.TriggerHand = frame * MarkerModel.Point(_marker, _markerDef.TriggerWrist_m);
            poser.SupportHand = Refill(frame * MarkerModel.Point(_marker, _markerDef.SupportWrist_m), feet, eye, poser);
        }
        else if (_markerWorn is not null)
        {
            poser.TriggerHand = frame * (_markerWorn.Fit * _markerWorn.Build.TriggerWrist);
            poser.SupportHand = Refill(frame * (_markerWorn.Fit * _markerWorn.Build.SupportWrist), feet, eye, poser);
        }
        else
        {
            Vector3 half = _half[marker];
            Vector3 Grip(float along) => frame * new Vector3(0f, -half.Y - _look.GripDrop_m, half.Z * (1f - 2f * along));
            poser.TriggerHand = Grip(_look.TriggerGrip);
            poser.SupportHand = Refill(Grip(_look.SupportGrip), feet, eye, poser);
        }

        // Out: the support hand leaves the marker and goes up, open, over the head on its own side.
        if (poser.SupportRaise > 0f)
        {
            float side = poser.RightHanded ? -1f : 1f;
            Vector3 up = feet + Vector3.Up * (eye + 0.12f + _look.OutHandAbove_m) + poser.Right * (_look.OutHandOut_m * side) + poser.Forward * 0.04f;
            poser.SupportHand = poser.SupportHand.Lerp(up, poser.SupportRaise);
        }

        // On a ladder: hands and feet on the rungs (the gear's slung on the back, by its hitboxes).
        if (_state.Ladder >= 0 && _state.Ladder < _sim.Ladders.Count && _state.LadderPhase == LadderPhase.Climbing)
        {
            LadderSpec ladder = _sim.Ladders[_state.Ladder];
            _limbs ??= new LadderLimbs();
            _limbs.Update(ladder, _state.Ladder, feet, _look.LadderLimbMove_s, _look.LadderLimbSwing_m, (float)GetProcessDeltaTime());
            poser.Ladder = new CharacterPoser.LadderGrip(_limbs.LeftFoot, _limbs.RightFoot, _limbs.LeftHand, _limbs.RightHand, ladder.Forward.ToGodot());
            poser.LadderHipsIn = _look.LadderHipsIn_m;
            poser.LadderHipsDown = _look.LadderHipsDown_m;
            poser.SupportRaise = 0f;
        }
        else
        {
            poser.Ladder = null;
            _limbs?.Leave();
        }

        // The legs: planted steps where the body goes, or the clips by the ground the feet covered.
        Vector3 moved = feet - _lastFeet;
        _lastFeet = feet;
        float crouch = (_standEye - eye) / Mathf.Max(_standEye - _crouchEye, 0.01f);
        float delta = (float)GetProcessDeltaTime();
        // Tucked in behind something: knees (crouched) and elbows out to the sides, clear of it.
        float ease = 1f - Mathf.Exp(-delta / 0.15f);
        if (_look.Steps is { } fit)
        {
            float knees = crouch > 0.2f && _poseNow.Alive ? Closeness(feet, poser.Forward, 0.42f, fit.KneeReach_m, fit.KneeClose_m) * Mathf.Clamp(crouch, 0f, 1f) : 0f;
            float elbows = _poseNow.Alive ? Closeness(feet, poser.Forward, eye - 0.48f, fit.ElbowReach_m, fit.ElbowClose_m) : 0f;
            _kneesOut = Mathf.Lerp(_kneesOut, knees, ease);
            poser.ElbowsOut = _elbowsOut = Mathf.Lerp(_elbowsOut, elbows, ease);
        }

        if (_model.Steps is { } steps)
        {
            steps.KneesOut = _kneesOut;
            steps.Update(feet, yaw, crouch, poser.HipDrop, Mathf.Lerp(_poseBefore.Shoulder, _poseNow.Shoulder, alpha), _state.Stance == Stance.Sliding,
                _state.Grounded, delta, _ground);
        }
        else
        {
            _model.Gait?.Update(moved, _state.Velocity.ToGodot(), poser.Forward, crouch, _state.Stance == Stance.Sliding, _state.Grounded, delta);
        }
    }

    /// <summary>
    /// The breath this frame (rad): slow at rest, quicker and deeper just after running hard, easing back.
    /// </summary>
    private float Breathe(float delta)
    {
        CharactersDef look = _look!;
        float pace = new Vector2(_state.Velocity.X, _state.Velocity.Z).Length() / _sim.Config.Movement.SprintSpeed;
        float hard = _state.Sprinting ? 1f : Mathf.Clamp(pace, 0f, 1f) * 0.7f;
        _exertion = hard > _exertion
            ? Mathf.Lerp(_exertion, hard, 1f - Mathf.Exp(-delta / 3f))
            : Mathf.Lerp(_exertion, hard, 1f - Mathf.Exp(-delta / look.BreathRecover_s));
        _breathPhase = Mathf.PosMod(_breathPhase + Mathf.Lerp(look.BreathRest_hz, look.BreathHard_hz, _exertion) * delta, 1f);
        // In a little quicker than out.
        float wave = _breathPhase < 0.4f ? Mathf.Sin(Mathf.Pi * _breathPhase / 0.4f * 0.5f) : Mathf.Cos(Mathf.Pi * (_breathPhase - 0.4f) / 0.6f * 0.5f);
        return Mathf.DegToRad(look.Breath_deg) * (0.6f + 0.6f * _exertion) * (wave - 0.5f);
    }

    /// <summary>
    /// A ball hit the body, pushing along <paramref name="push"/> at <paramref name="speed"/> (m/s): the upper
    /// body flinches away from it, harder the faster the ball, and recovers in a fraction of a second.
    /// </summary>
    public void Flinch(Vector3 push, float speed)
    {
        var flat = new Vector3(push.X, 0f, push.Z);
        if (flat.LengthSquared() < 1e-4f || _look is null)
        {
            return;
        }

        _flinchPush = flat.Normalized();
        _flinchStrength = Mathf.Clamp(speed / 90f, 0.3f, 1f) * Mathf.DegToRad(_look.Flinch_deg);
        _flinchAge = 0f;
    }

    /// <summary>
    /// Refilling from a pod (in step with the sim's refill; run on to the end if it stops early): the
    /// support hand leaves <paramref name="grip"/> for the pods on the belt, pulls one out, brings it up
    /// beside the loader to tip it in, and goes back. The marker stays where its hitbox is; only the hand moves.
    /// </summary>
    private Vector3 Refill(Vector3 grip, Vector3 feet, float eye, CharacterPoser poser)
    {
        Pb.Sim.Gear.Marker marker = _state.Marker;
        float delta = (float)GetProcessDeltaTime();
        _refillPhase = marker.Refill.Active && _state.Alive ? marker.Refill.Progress(marker.Paint.Params) : Mathf.MoveToward(_refillPhase, 1f, delta * 2.5f);
        float f = _shadowOnly ? 1f : _refillPhase;
        if (f >= 1f)
        {
            if (_pod is not null)
            {
                _pod.Visible = false;
            }

            return grip;
        }

        float side = poser.RightHanded ? -1f : 1f;
        int loaderIndex = _indexOfPart[(int)HitboxPart.Loader];
        Transform3D loader = _parts[loaderIndex].GlobalTransform;
        Vector3 loaderHalf = _half[loaderIndex];
        // Beside the loader on the support side, a little below its top (with the marker shouldered, the
        // loader's top is by the face), and the pods on the belt at the hip.
        Vector3 pour = loader * new Vector3(0f, loaderHalf.Y * 0.4f, 0f) + poser.Right * ((loaderHalf.X + 0.07f) * side);
        Vector3 hip = feet + Vector3.Up * (eye * 0.55f) + poser.Right * (0.2f * side) - poser.Forward * 0.04f;
        Vector3 hand = f < 0.2f ? grip.Lerp(hip, Mathf.SmoothStep(0f, 0.2f, f))
            : f < 0.6f ? hip + Vector3.Up * (0.02f * Mathf.Sin(f * 40f))
            : f < 0.72f ? hip.Lerp(pour, Mathf.SmoothStep(0.6f, 0.72f, f))
            : f < 0.85f ? pour + Vector3.Up * (0.01f * Mathf.Sin(f * 70f))
            : pour.Lerp(grip, Mathf.SmoothStep(0.85f, 1f, f));
        _pod ??= _model!.Pod(poser.RightHanded ? "LeftHand" : "RightHand", _paint);
        _pod.Visible = f is > 0.3f and < 0.88f;
        return hand;
    }

    /// <summary>
    /// How close something solid is ahead of the body at <paramref name="height"/> over its feet (paint geometry): 0
    /// from <paramref name="reach"/> away or nothing, 1 at <paramref name="close"/> or nearer.
    /// </summary>
    private float Closeness(Vector3 feet, Vector3 forward, float height, float reach, float close)
    {
        System.Numerics.Vector3 from = (feet + Vector3.Up * height).ToSim();
        System.Numerics.Vector3 to = (feet + Vector3.Up * height + forward * reach).ToSim();
        if (!_sim.Collision.SweepSphere(from, to, 0.06f, out SweepHit hit))
        {
            return 0f;
        }

        float distance = hit.T * reach;
        return Mathf.Clamp((reach - distance) / Mathf.Max(reach - close, 0.01f), 0f, 1f);
    }

    /// <summary>The ground's height near <paramref name="at"/>: what a drop down from a little above it lands on (paint geometry, so the drawn stair treads).</summary>
    private bool Ground(Vector3 at, out float height)
    {
        System.Numerics.Vector3 from = at.ToSim() + new System.Numerics.Vector3(0f, 0.6f, 0f);
        System.Numerics.Vector3 to = at.ToSim() - new System.Numerics.Vector3(0f, 0.6f, 0f);
        if (_sim.Collision.SweepSphere(from, to, 0.02f, out SweepHit hit) && hit.Normal.Y > 0.6f)
        {
            height = hit.Point.Y - 0.02f * hit.Normal.Y;
            return true;
        }

        height = at.Y;
        return false;
    }

    private static bool IsGear(HitboxPart part) => part is HitboxPart.Marker or HitboxPart.Loader or HitboxPart.Tank;

    private static bool IsGearOrArms(HitboxPart part) => IsGear(part) || part == HitboxPart.Arms;

    /// <summary>How far through bringing the gear up and the hand up since going out (0–1; 1 while in, as nothing's raising).</summary>
    private float Raise()
    {
        if (_outAge < 0f || _look is null)
        {
            return _outAge < 0f ? 0f : 1f;
        }

        return Mathf.SmoothStep(0f, _look.OutRaise_s, _outAge);
    }

    /// <summary>Draws <paramref name="geometry"/> as the character is drawn: all of it, or only its shadow.</summary>
    private void Draw(GeometryInstance3D geometry)
    {
        if (!_drawn.TryGetValue(geometry, out var was))
        {
            _drawn[geometry] = was = (geometry.CastShadow, geometry.Visible);
        }

        bool casts = was.Cast != GeometryInstance3D.ShadowCastingSetting.Off;
        geometry.CastShadow = _shadowOnly && casts ? GeometryInstance3D.ShadowCastingSetting.ShadowsOnly : was.Cast;
        geometry.Visible = was.Visible && (casts || !_shadowOnly);
    }

    private static Quaternion Quat(in PosedBox b)
    {
        System.Numerics.Quaternion q = b.Rotation;
        return new Quaternion(q.X, q.Y, q.Z, q.W).Normalized();
    }

    private static StandardMaterial3D MaterialFor(HitboxPart part, Color jersey)
    {
        (Color color, float roughness, float metallic) = part switch
        {
            HitboxPart.Torso => (jersey.Darkened(0.15f), 0.85f, 0f),
            HitboxPart.Arms => (jersey.Darkened(0.3f), 0.85f, 0f),
            HitboxPart.Legs => (new Color(0.17f, 0.18f, 0.19f), 0.9f, 0f),
            HitboxPart.Head => (new Color(0.22f, 0.22f, 0.23f), 0.9f, 0f),
            HitboxPart.Mask => (new Color(0.07f, 0.075f, 0.08f), 0.35f, 0.1f),
            HitboxPart.Marker => (new Color(0.12f, 0.13f, 0.14f), 0.45f, 0.5f),
            HitboxPart.Loader => (jersey.Lightened(0.1f), 0.4f, 0f),
            HitboxPart.Tank => (new Color(0.72f, 0.74f, 0.77f), 0.3f, 0.85f),
            _ => (Colors.Gray, 0.8f, 0f),
        };
        return new StandardMaterial3D { AlbedoColor = color, Roughness = roughness, Metallic = metallic };
    }
}
