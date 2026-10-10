using System;
using Godot;
using Pb.Game.Core;
using Pb.Game.World;
using Pb.Sim;
using Pb.Sim.Collision;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;

namespace Pb.Game.Audio;

/// <summary>
/// Plays the game's sounds from the sim's events (spec §8): your marker in 2D, its pitch and body following tank
/// pressure; everything else from where it happens, through a pool of 3D players on the Effects bus, dulled with
/// distance (air absorption) and muffled when the sim's collision says a wall is in the way. Breaks, bounces and
/// footsteps sound like the surface (presentation.jsonc "audio" → "surfaces"), footsteps as loud as they carry and
/// splashing in puddles; doors by kind; gear, pickups, being hit and the hit marker for you; the objectives' cues.
/// Bots' callouts play from the caller in their voice (<see cref="VoiceBank"/>), the referee's lines in 2D. The ambience
/// and the reverb are <see cref="Audio.Ambience"/>'s. Looks after nothing the sim decides.
/// </summary>
public partial class AudioDirector : Node, ISimEventListener
{
    /// <summary>How many callouts can be heard at once.</summary>
    private const int Speakers = 6;

    // How far each kind of sound carries (m), as a share of maxDistance_m or outright.
    private const float BreakRange = 70f, BounceRange = 30f, DoorRangePerNoise = 3f, GearRange = 20f, PickupRange = 25f, CaseRange = 30f,
        BeepRange = 18f;

    private SimWorld _sim = null!;
    private PlayerState _player = null!;
    private AudioDef _def = null!;
    private MixDef _mix = null!;
    private LevelLayout? _level;
    private Func<Vector3, bool>? _inWater;
    private float _blink;
    private Family[] _families = Array.Empty<Family>();
    private AudioStreamPlayer _shot = null!;
    private AudioStreamPlayer _local = null!;
    private AudioStreamPlayer _steps = null!;
    private AudioStreamPlayer _round = null!;
    private AudioStreamPlayer _referee = null!;
    private AudioStreamPlayer3D[] _pool = Array.Empty<AudioStreamPlayer3D>();
    private AudioStreamPlayer3D[] _speakers = Array.Empty<AudioStreamPlayer3D>();
    private int _next;
    private int _nextSpeaker;
    private int _budget;
    private Vector3 _ear;
    private SoundSet? _set;
    private readonly Random _random = new(0xA0D10);
    private float _feedIn = -1f;
    private float _pourIn = -1f;
    private float _beepIn;
    private bool _reported;

    /// <summary>What a surface sounds like.</summary>
    public enum Family : byte
    {
        Stone,
        Metal,
        Wood,
        Glass,
        Ground,
        Gravel,
        Grass,
        Tarp,
        Inflatable,
        Rubber,
        Player,
    }

    public Ambience? Ambience { get; private set; }

    public VoiceBank? Voices { get; private set; }

    /// <summary>The bank, once it's rendered.</summary>
    public SoundBank? Bank => _set?.Bank;

    /// <summary>How long the referee's current line lasts (s).</summary>
    public double RefereeLength => _referee?.Stream?.GetLength() ?? 0.0;

    // What's been heard so far, for the smoke tests' report.
    public int Played { get; private set; }

    public int Muffled { get; private set; }

    public int Skipped { get; private set; }

    public int Shots { get; private set; }

    public int Breaks { get; private set; }

    public int Bounces { get; private set; }

    public int Steps { get; private set; }

    public int Doors { get; private set; }

    public int Cues { get; private set; }

    public int Spoken { get; private set; }

    public int Unvoiced { get; private set; }

    /// <param name="level">The level (its doors and areas); null on the training ground.</param>
    /// <param name="inWater">Whether a point on the ground is in a puddle (footsteps there splash).</param>
    /// <param name="birds">The birds, for the crows' calls.</param>
    public void Initialize(SimWorld sim, PlayerState localPlayer, PresentationDef view, LevelLayout? level = null, Func<Vector3, bool>? inWater = null,
        Birds? birds = null)
    {
        _sim = sim;
        _player = localPlayer;
        _def = view.Audio;
        _mix = view.Audio.Mix;
        _level = level;
        _inWater = inWater;
        _blink = view.Objectives.CaseLightBlink_s;
        AudioBuses.Ensure();
        SoundBank.Warm(_def.Variations);
        _families = FamiliesOf(sim.Config.Surfaces, _def.Surfaces);

        _shot = Player2D(AudioBuses.Effects, 4);
        _local = Player2D(AudioBuses.Effects, 6);
        _steps = Player2D(AudioBuses.Effects, 3);
        _round = Player2D(AudioBuses.Effects, 2);
        _referee = Player2D(AudioBuses.Referee, 1);
        _pool = new AudioStreamPlayer3D[_def.Voices];
        for (int i = 0; i < _pool.Length; i++)
        {
            _pool[i] = Player3D(AudioBuses.Effects, _def.UnitSize_m, _def.MaxDistance_m);
        }

        _speakers = new AudioStreamPlayer3D[Speakers];
        for (int i = 0; i < _speakers.Length; i++)
        {
            _speakers[i] = Player3D(AudioBuses.Voices, _def.CalloutUnitSize_m, _def.CalloutRange_m);
        }

        Voices = new VoiceBank(_def, view.Characters.Models.Length);
        Ambience = new Ambience { Name = "Ambience" };
        AddChild(Ambience);
        Ambience.Initialize(_def, level, view.GroundWind, view.Weeds.GustSize_m, birds);
    }

    /// <summary>The family each surface id sounds like (stone for any the data doesn't name).</summary>
    public static Family[] FamiliesOf(SurfaceRegistry surfaces, System.Collections.Generic.IReadOnlyDictionary<string, string> map)
    {
        var families = new Family[surfaces.Count];
        for (int i = 0; i < families.Length; i++)
        {
            families[i] = map.TryGetValue(surfaces.Names[i], out string? name) && Enum.TryParse(name, ignoreCase: true, out Family f) ? f : Family.Stone;
        }

        return families;
    }

    public override void _Process(double delta)
    {
        if (_def is null)
        {
            return;
        }

        float dt = (float)delta;
        _budget = _def.MaxSoundsPerFrame;
        _ear = GetViewport()?.GetCamera3D()?.GlobalPosition ?? _player.EyePosition.ToGodot();
        if (_set is null)
        {
            if (SoundBank.Ready is not { } bank)
            {
                if (!_reported && SoundBank.Failure is { } failure)
                {
                    _reported = true;
                    GD.PushError($"The sound bank couldn't be rendered: {failure}");
                }

                return;
            }

            _set = new SoundSet(bank);
            foreach (string problem in bank.Problems)
            {
                GD.PushError($"Sound bank: {problem}");
            }
        }

        if (_feedIn >= 0f && (_feedIn -= dt) < 0f)
        {
            Play2D(_local, Sfx.LoaderFeed, _mix.Loader);
        }

        if (_pourIn >= 0f && (_pourIn -= dt) < 0f)
        {
            Play2D(_local, Sfx.RefillPour, _mix.Gear);
        }

        CaseBeacon(dt);
        Ambience?.Update(dt, _ear, _set);
    }

    public void OnSimEvent(in SimEvent e)
    {
        if (_set is null || _def is null)
        {
            return;
        }

        bool local = e.PlayerId == _player.Id;
        switch (e.Type)
        {
            case SimEventType.ShotFired when local:
                float fill = _player.Marker.Air.FillFraction;
                Sfx report = fill > 0.66f ? Sfx.ReportFull : fill > 0.33f ? Sfx.ReportMid : Sfx.ReportLow;
                Play2D(_shot, report, _mix.Shot, Mathf.Lerp(_def.ShotPitchEmpty, _def.ShotPitchFull, fill) * Jitter(0.015f));
                _feedIn = 0.06f;
                Shots++;
                break;
            case SimEventType.ShotFired:
                if (Play3D(Sfx.ReportMid, e.Position.ToGodot(), _mix.RemoteShot, Jitter(0.03f), _def.MaxDistance_m))
                {
                    Shots++;
                }

                break;
            case SimEventType.BallBroke:
            {
                Family family = e.Lethal || PlayerHitboxes.IsPlayer(e.TargetId) ? Family.Player : FamilyOf(e.Surface);
                Vector3 at = (e.Position + e.Normal * 0.05f).ToGodot();
                if (Play3D(BreakOf(family), at, _mix.Break + SpeedDb(e.Value, 60f), Jitter(0.05f), BreakRange))
                {
                    Breaks++;
                }

                break;
            }

            case SimEventType.MaskSprayed when e.TargetId == _player.Id:
                Play2D(_local, Sfx.BreakMask, _mix.HitTaken);
                break;
            case SimEventType.MaskSprayed:
                Play3D(Sfx.BreakMask, e.Position.ToGodot(), _mix.Break, Jitter(0.05f), BreakRange);
                break;
            case SimEventType.BallBounced when e.Value > 6f:
                if (Play3D(BounceOf(FamilyOf(e.Surface)), e.Position.ToGodot(), _mix.Bounce + SpeedDb(e.Value, 40f), Jitter(0.08f), BounceRange))
                {
                    Bounces++;
                }

                break;
            case SimEventType.Footstep:
                Footstep(e, local);
                break;
            case SimEventType.PlayerEliminated:
                if (e.TargetId == _player.Id)
                {
                    Play2D(_local, Sfx.HitTaken, _mix.HitTaken);
                }
                else if (local)
                {
                    Play2D(_local, Sfx.HitMarker, _mix.HitMarker);
                }

                break;
            case SimEventType.TargetHit when local && !PlayerHitboxes.IsPlayer(e.TargetId):
                Play2D(_local, Sfx.HitMarker, _mix.HitMarker);
                break;
            case SimEventType.DryFire when local:
                Play2D(_local, Sfx.DryFire, _mix.Gear);
                break;
            case SimEventType.FireModeChanged when local:
                Play2D(_local, Sfx.FireMode, _mix.Gear);
                break;
            case SimEventType.AirLow when local:
                Play2D(_local, Sfx.AirLow, _mix.Gear);
                break;
            case SimEventType.RefillStarted when local:
                Play2D(_local, Sfx.RefillPop, _mix.Gear);
                _pourIn = 0.12f;
                break;
            case SimEventType.RefillStarted:
                if (_sim.FindPlayer(e.PlayerId) is { } refiller)
                {
                    Play3D(Sfx.RefillPour, refiller.Position.ToGodot() + Vector3.Up, _mix.Gear - 2f, 1f, GearRange);
                }

                break;
            case SimEventType.RefillCompleted when local:
            case SimEventType.GearReset when local:
                Play2D(_local, Sfx.RefillSnap, _mix.Gear);
                break;
            case SimEventType.RefillCancelled when local:
                _pourIn = -1f;
                break;
            case SimEventType.RefillDenied when local:
                Play2D(_local, Sfx.RefillDenied, _mix.Gear);
                break;
            case SimEventType.PickupTaken:
                Sfx pickup = (PickupKind)e.Extra == PickupKind.Air ? Sfx.PickupAir : Sfx.PickupPod;
                if (local)
                {
                    Play2D(_local, pickup, _mix.Pickup);
                }
                else
                {
                    Play3D(pickup, e.Position.ToGodot(), _mix.Pickup - 3f, 1f, PickupRange);
                }

                break;
            case SimEventType.DoorMoved:
                Door(e);
                break;
            case SimEventType.CaseTaken:
                Play3D(Sfx.CaseTake, e.Position.ToGodot(), _mix.Objective, 1f, CaseRange);
                Cue(e.Team == _player.Team ? Sfx.HoldOurs : Sfx.CaseAlarm);
                break;
            case SimEventType.CaseDropped:
                Play3D(Sfx.CaseDrop, e.Position.ToGodot(), _mix.Objective, 1f, CaseRange);
                Cue(Sfx.HoldTheirs);
                break;
            case SimEventType.CaseExtracted:
                Cue(Sfx.CaseOut);
                break;
            case SimEventType.FlagTaken:
                // A flag's the case's cousin: the same take and drop where it happens, and the same cues for your side.
                Play3D(Sfx.CaseTake, e.Position.ToGodot(), _mix.Objective, 1f, CaseRange);
                Cue(e.Team == _player.Team ? Sfx.HoldOurs : Sfx.CaseAlarm);
                break;
            case SimEventType.FlagDropped:
                Play3D(Sfx.CaseDrop, e.Position.ToGodot(), _mix.Objective, 1f, CaseRange);
                Cue(e.Team == _player.Team ? Sfx.HoldTheirs : Sfx.HoldOurs);
                break;
            case SimEventType.FlagCaptured:
                Cue(Sfx.CaseOut);
                break;
            case SimEventType.HoldChanged:
                Sfx? hold = (HoldStatus)e.Extra switch
                {
                    HoldStatus.Ours => Sfx.HoldOurs,
                    HoldStatus.Contested => Sfx.HoldContested,
                    HoldStatus.Theirs => Sfx.HoldTheirs,
                    _ => null,
                };
                if (hold is { } h)
                {
                    Cue(h);
                }

                break;
        }
    }

    /// <summary>The breakout horn (and speedball's countdown pips) or the referee's whistle (<see cref="Core.RefereeCalls"/>).</summary>
    public void Round(Sfx sfx)
    {
        if (_set is not null)
        {
            Play2D(_round, sfx, sfx is Sfx.Horn or Sfx.CountdownPip ? _mix.Horn : _mix.Whistle);
        }
    }

    /// <summary>
    /// A bot's callout in its voice, from where it stands (muffled through walls); false when there's no recording of the
    /// line in that voice (the subtitle still shows).
    /// </summary>
    public bool Callout(PlayerState speaker, int look, string line)
    {
        if (Voices is null || _def is null)
        {
            return false;
        }

        AudioStream? clip = Voices.Clip(Voices.VoiceFor(look), line);
        if (clip is null)
        {
            Unvoiced++;
            return false;
        }

        AudioStreamPlayer3D speakerVoice = _speakers[_nextSpeaker];
        _nextSpeaker = (_nextSpeaker + 1) % _speakers.Length;
        Vector3 at = speaker.EyePosition.ToGodot();
        bool muffled = Blocked(at);
        speakerVoice.Stream = clip;
        speakerVoice.GlobalPosition = at;
        speakerVoice.VolumeDb = _def.Volume_db + _mix.Callout + (muffled ? _def.Occlusion.Volume_db : 0f);
        speakerVoice.AttenuationFilterCutoffHz = muffled ? _def.Occlusion.Cutoff_hz : _def.AirAbsorption.Cutoff_hz;
        if (!AudioBuses.Silent)
        {
            speakerVoice.Play();
        }

        Spoken++;
        return true;
    }

    /// <summary>The referee's line, in 2D; false when it hasn't been recorded.</summary>
    public bool Referee(string line)
    {
        if (Voices is null || _def is null)
        {
            return false;
        }

        AudioStream? clip = Voices.Clip(Voices.Referee, line);
        if (clip is null)
        {
            Unvoiced++;
            return false;
        }

        _referee.Stream = clip;
        _referee.VolumeDb = _def.Volume_db + _mix.Referee;
        if (!AudioBuses.Silent)
        {
            _referee.Play();
        }

        Spoken++;
        return true;
    }

    public override void _ExitTree()
    {
        // Stop everything and free this scene's streams (the bank's samples are shared and stay).
        foreach (AudioStreamPlayer3D voice in _pool)
        {
            voice.Stop();
            voice.Stream = null;
        }

        foreach (AudioStreamPlayer3D voice in _speakers)
        {
            voice.Stop();
            voice.Stream = null;
        }

        foreach (AudioStreamPlayer player in new[] { _shot, _local, _steps, _round, _referee })
        {
            if (player is not null)
            {
                player.Stop();
                player.Stream = null;
            }
        }

        Ambience?.Stop();
        _set?.Release();
        _set = null;
    }

    private void Footstep(in SimEvent e, bool own)
    {
        Family family = FamilyOf(e.Surface);
        bool water = (FootstepKind)e.Extra is FootstepKind.Step or FootstepKind.Land && _inWater?.Invoke(e.Position.ToGodot()) == true;
        (Sfx sfx, float mix) = (FootstepKind)e.Extra switch
        {
            FootstepKind.Slide => (SlideOf(family), _mix.Slide),
            FootstepKind.Jump => (Sfx.Jump, _mix.Footstep),
            FootstepKind.Land => (water ? Sfx.StepWater : LandOf(family), _mix.Land),
            _ => (water ? Sfx.StepWater : StepOf(family), _mix.Footstep),
        };

        // As loud as it carries: 20 dB for each tenfold of the radius it's heard out to.
        float loudness = 20f * MathF.Log10(MathF.Max(e.Value, 0.5f) / _def.StepReferenceRadius_m);
        if (own)
        {
            Play2D(_steps, sfx, _mix.OwnFootstep + (mix - _mix.Footstep) + loudness * 0.5f, Jitter(0.04f));
            Steps++;
        }
        else if (Play3D(sfx, e.Position.ToGodot() + Vector3.Up * 0.2f, mix + loudness, Jitter(0.05f), MathF.Max(8f, e.Value * 2.5f)))
        {
            Steps++;
        }
    }

    private void Door(in SimEvent e)
    {
        if (_level is null || e.TargetId < 0 || e.TargetId >= _level.Doors.Count)
        {
            return;
        }

        DoorSpec door = _level.Doors[e.TargetId];
        var motion = (DoorMotion)e.Extra;
        bool moving = motion != DoorMotion.Shut;
        Sfx? sfx = (door.Kind.Style, door.Sliding) switch
        {
            (_, true) => moving ? Sfx.SlideRoll : Sfx.SlideBang,
            ("cold_room", _) => motion switch { DoorMotion.Opening => Sfx.ColdOpen, DoorMotion.Closing => Sfx.DoorSteelOpen, _ => Sfx.ColdShut },
            ("steel", _) => moving ? Sfx.DoorSteelOpen : Sfx.DoorClank,
            ("swing", _) => motion switch { DoorMotion.Opening => Sfx.SwingOpen, DoorMotion.Closing => Sfx.SwingFlap, _ => null },
            _ => moving ? Sfx.DoorCreak : Sfx.DoorSlam,
        };
        if (sfx is { } s && Play3D(s, e.Position.ToGodot(), _mix.Door - (motion == DoorMotion.Closing ? 3f : 0f),
                motion == DoorMotion.Closing ? Jitter(0.04f) * 1.1f : Jitter(0.04f), MathF.Max(10f, e.Value * DoorRangePerNoise)))
        {
            Doors++;
        }
    }

    /// <summary>The case lying about beeps softly with each blink of its light.</summary>
    private void CaseBeacon(float dt)
    {
        ObjectiveState? objective = _sim.Match?.Objective;
        if (objective is not { Kind: ObjectiveKind.Retrieve, Carrier: < 0, Done: false } || _sim.Match?.Phase != MatchPhase.Live)
        {
            return;
        }

        if ((_beepIn -= dt) <= 0f)
        {
            _beepIn = _blink;
            Play3D(Sfx.CaseBeep, objective.CasePosition.ToGodot() + Vector3.Up * 0.3f, _mix.CaseBeep, 1f, BeepRange);
        }
    }

    /// <summary>An objective's cue: in 2D, for your side.</summary>
    private void Cue(Sfx sfx)
    {
        Play2D(_local, sfx, _mix.Objective);
        Cues++;
    }

    private bool Play3D(Sfx sfx, Vector3 at, float db, float pitch, float range)
    {
        if (_budget <= 0 || at.DistanceSquaredTo(_ear) > range * range)
        {
            Skipped++;
            return false;
        }

        _budget--;
        AudioStreamPlayer3D voice = NextVoice();
        bool muffled = Blocked(at);
        voice.Stream = _set!.Get(sfx, _random.Next());
        voice.GlobalPosition = at;
        voice.MaxDistance = range;
        voice.VolumeDb = _def.Volume_db + db + (muffled ? _def.Occlusion.Volume_db : 0f);
        voice.AttenuationFilterCutoffHz = muffled ? _def.Occlusion.Cutoff_hz : _def.AirAbsorption.Cutoff_hz;
        voice.PitchScale = pitch;
        if (!AudioBuses.Silent)
        {
            voice.Play();
        }

        Played++;
        Muffled += muffled ? 1 : 0;
        return true;
    }

    private void Play2D(AudioStreamPlayer player, Sfx sfx, float db, float pitch = 1f)
    {
        player.Stream = _set!.Get(sfx, _random.Next());
        player.VolumeDb = _def.Volume_db + db;
        player.PitchScale = pitch;
        if (!AudioBuses.Silent)
        {
            player.Play();
        }
    }

    /// <summary>A free player from the pool, else the next in turn (cutting off the oldest sound).</summary>
    private AudioStreamPlayer3D NextVoice()
    {
        for (int k = 0; k < _pool.Length; k++)
        {
            AudioStreamPlayer3D voice = _pool[(_next + k) % _pool.Length];
            if (!voice.Playing)
            {
                _next = (_next + k + 1) % _pool.Length;
                return voice;
            }
        }

        AudioStreamPlayer3D oldest = _pool[_next];
        _next = (_next + 1) % _pool.Length;
        return oldest;
    }

    /// <summary>Whether something the paint can't pass stands between you and <paramref name="at"/> (stopping short of the source itself).</summary>
    private bool Blocked(Vector3 at)
    {
        Vector3 to = at - _ear;
        float length = to.Length();
        if (length < 0.6f)
        {
            return false;
        }

        Vector3 end = _ear + to * ((length - 0.3f) / length);
        return _sim.Collision.SweepSphere(_ear.ToSim(), end.ToSim(), 0f, out _);
    }

    private Family FamilyOf(SurfaceId surface) => surface.Value < _families.Length ? _families[surface.Value] : Family.Stone;

    private float Jitter(float spread) => 1f + ((float)_random.NextDouble() * 2f - 1f) * spread;

    /// <summary>Faster impacts are louder: 0 dB at <paramref name="reference"/> m/s, 20 dB less for each tenfold slower.</summary>
    private static float SpeedDb(float speed, float reference) => Math.Clamp(20f * MathF.Log10(MathF.Max(speed, 1f) / reference), -18f, 3f);

    private static Sfx BreakOf(Family f) => f switch
    {
        Family.Metal => Sfx.BreakMetal,
        Family.Wood => Sfx.BreakWood,
        Family.Glass => Sfx.BreakGlass,
        Family.Ground or Family.Grass => Sfx.BreakGround,
        Family.Gravel => Sfx.BreakGravel,
        Family.Tarp => Sfx.BreakTarp,
        Family.Inflatable => Sfx.BreakInflatable,
        Family.Rubber => Sfx.BreakRubber,
        Family.Player => Sfx.BreakPlayer,
        _ => Sfx.BreakStone,
    };

    private static Sfx BounceOf(Family f) => f switch
    {
        Family.Metal => Sfx.BounceMetal,
        Family.Wood => Sfx.BounceWood,
        Family.Glass => Sfx.BounceGlass,
        Family.Stone => Sfx.BounceHard,
        _ => Sfx.BounceSoft,
    };

    private static Sfx StepOf(Family f) => f switch
    {
        Family.Metal => Sfx.StepMetal,
        Family.Wood => Sfx.StepWood,
        Family.Gravel => Sfx.StepGravel,
        Family.Ground => Sfx.StepGround,
        Family.Grass => Sfx.StepGrass,
        Family.Tarp => Sfx.StepTarp,
        Family.Rubber or Family.Inflatable or Family.Player => Sfx.StepRubber,
        _ => Sfx.StepStone,
    };

    private static Sfx SlideOf(Family f) => f switch
    {
        Family.Metal => Sfx.SlideMetal,
        Family.Gravel => Sfx.SlideGravel,
        Family.Ground or Family.Grass or Family.Tarp or Family.Rubber or Family.Inflatable => Sfx.SlideSoft,
        _ => Sfx.SlideHard,
    };

    private static Sfx LandOf(Family f) => f switch
    {
        Family.Metal => Sfx.LandMetal,
        Family.Ground or Family.Grass or Family.Gravel or Family.Tarp or Family.Rubber or Family.Inflatable => Sfx.LandSoft,
        _ => Sfx.LandHard,
    };

    private AudioStreamPlayer Player2D(StringName bus, int polyphony)
    {
        var player = new AudioStreamPlayer { Bus = bus, MaxPolyphony = polyphony };
        AddChild(player);
        return player;
    }

    private AudioStreamPlayer3D Player3D(StringName bus, float unitSize, float maxDistance)
    {
        var player = new AudioStreamPlayer3D
        {
            Bus = bus,
            UnitSize = unitSize,
            MaxDistance = maxDistance,
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
            AttenuationFilterCutoffHz = _def.AirAbsorption.Cutoff_hz,
            AttenuationFilterDb = _def.AirAbsorption.Db,
            DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.Disabled,
        };
        AddChild(player);
        return player;
    }
}
