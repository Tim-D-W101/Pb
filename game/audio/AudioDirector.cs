using System;
using System.Collections.Generic;
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
/// splashing in puddles; doors by kind; gear, pickups, being hit and the hit marker for you; the objectives' cues. Since
/// Phase 5 also the feel of it: your loader rattling as you run and land and spinning when it's empty, balls whizzing
/// past your head (<see cref="Whizz"/>), a ball bouncing off you, the confirm when your ball puts someone out (a dull
/// one when it bounced off them, another for a teammate), and when you're hit, the thud and a ringing in your ears on the
/// stinger bus while the world's sound is muffled for a moment (<see cref="AudioBuses.Muffle"/>).
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
    private readonly List<(Sfx Sfx, double At)> _lateCues = new();
    private int _droppedCues;
    private double _clock;
    private AudioStreamPlayer _referee = null!;
    private AudioStreamPlayer _stinger = null!;
    private HitMomentDef _moment = null!;
    private float _muffleAge = -1f;
    private float _rattleWait;
    private float _whizzWait;
    private AudioStreamPlayer3D[] _pool = Array.Empty<AudioStreamPlayer3D>();
    private AudioStreamPlayer3D[] _speakers = Array.Empty<AudioStreamPlayer3D>();
    private AudioStreamPlayer3D[] _engines = Array.Empty<AudioStreamPlayer3D>();
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

    /// <summary>Balls heard whizzing past your head.</summary>
    public int Whizzes { get; private set; }

    /// <summary>Confirms played: your ball put someone out (or bounced off them, or put out a teammate).</summary>
    public int Confirms { get; private set; }

    /// <summary>Whether the world's sound is muffled right now (you were just hit).</summary>
    public bool Muffling => _muffleAge >= 0f;

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
        _stinger = Player2D(AudioBuses.Stinger, 4);
        _moment = view.Feedback.HitMoment;
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
        _clock += delta;
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

            PlayLateCues();
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
        Engines();
        Ambience?.Update(dt, _ear, _set);
        _rattleWait -= dt;
        _whizzWait -= dt;
        UpdateMuffle(dt);
    }

    /// <summary>
    /// The world's sound after you're hit: muffled at once to the hit moment's cutoff, then opening up again (evenly in
    /// pitch, so in octaves) over its recovery time.
    /// </summary>
    private void UpdateMuffle(float dt)
    {
        if (_muffleAge < 0f)
        {
            return;
        }

        _muffleAge += dt;
        float t = _muffleAge / _moment.MuffleRecover_s;
        if (t >= 1f)
        {
            _muffleAge = -1f;
            AudioBuses.Muffle(AudioBuses.Open);
            return;
        }

        float eased = t * t * (3f - 2f * t);
        AudioBuses.Muffle(_moment.MuffleCutoff_hz * MathF.Pow(AudioBuses.Open / _moment.MuffleCutoff_hz, eased));
    }

    /// <summary>
    /// A ball whizzed past your head (<see cref="Pb.Game.Ballistics.FlybyScan"/>): heard from where it passed nearest,
    /// louder the closer, a little higher the faster. At most one in 40 ms, so a burst of fire isn't a wall of whizzes.
    /// </summary>
    public void Whizz(Vector3 at, float speed, float distance, float radius)
    {
        if (_set is null || _whizzWait > 0f)
        {
            return;
        }

        float closeness = 1f - Math.Clamp(distance / MathF.Max(radius, 0.01f), 0f, 1f);
        float db = _mix.Whizz - 9f * (1f - closeness);
        if (Play3D(Sfx.Whizz, at, db, Math.Clamp(MathF.Sqrt(speed / 75f), 0.8f, 1.2f) * Jitter(0.04f), 12f))
        {
            Whizzes++;
            _whizzWait = 0.04f;
        }
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
            case SimEventType.BallBounced when PlayerHitboxes.IsPlayer(e.TargetId) && PlayerHitboxes.PlayerIdOf(e.TargetId) == _player.Id:
                // Off you: close, on your own gear.
                Play2D(_local, Sfx.BounceOffYou, _mix.BounceOffYou, Jitter(0.05f));
                break;
            case SimEventType.BallBounced when local && PlayerHitboxes.IsPlayer(e.TargetId) && _sim.FindPlayer(PlayerHitboxes.PlayerIdOf(e.TargetId)) is { Alive: true }:
                // Your ball bounced off someone still in: it doesn't count (you hear it bounce off them too).
                Play2D(_stinger, Sfx.HitBounce, _mix.HitBounce);
                Confirms++;
                if (Play3D(BounceOf(Family.Player), e.Position.ToGodot(), _mix.Bounce + SpeedDb(e.Value, 40f), Jitter(0.08f), BounceRange))
                {
                    Bounces++;
                }

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
                if (e.TargetId == _player.Id && e.Extra >= 0)
                {
                    // You're hit: the thud and your ears ringing, dry, while the world goes muffled.
                    Play2D(_stinger, Sfx.HitTaken, _mix.HitTaken);
                    Play2D(_stinger, Sfx.EarRing, _mix.EarRing, Jitter(0.02f));
                    _muffleAge = 0f;
                    AudioBuses.Muffle(_moment.MuffleCutoff_hz);
                }
                else if (local && e.Extra >= 0)
                {
                    bool teammate = _sim.FindPlayer(e.TargetId)?.Team == _player.Team;
                    Play2D(_stinger, teammate ? Sfx.HitTeammate : Sfx.HitConfirm, teammate ? _mix.HitTeammate : _mix.HitConfirm);
                    Confirms++;
                }

                break;
            case SimEventType.TargetHit when local && !PlayerHitboxes.IsPlayer(e.TargetId):
                Play2D(_local, Sfx.HitMarker, _mix.HitMarker);
                break;
            case SimEventType.DryFire when local:
                // Out of paint, the loader's motor spins on nothing; out of air, the hammer falls on an empty valve.
                bool noPaint = _player.Marker.Paint.Loader == 0;
                Play2D(_local, noPaint ? Sfx.LoaderEmpty : Sfx.DryFire, noPaint ? _mix.LoaderEmpty : _mix.Gear);
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
            // Capture the flag (e.Team: the flag's side) and the VIP: the case's sounds, good or bad for your side.
            case SimEventType.FlagTaken:
                Play3D(Sfx.CaseTake, e.Position.ToGodot(), _mix.Objective, 1f, CaseRange);
                Cue(e.Team == _player.Team ? Sfx.CaseAlarm : Sfx.HoldOurs);
                break;
            case SimEventType.FlagDropped:
                Play3D(Sfx.CaseDrop, e.Position.ToGodot(), _mix.Objective, 1f, CaseRange);
                Cue(e.Team == _player.Team ? Sfx.HoldOurs : Sfx.HoldTheirs);
                break;
            case SimEventType.FlagReturned:
                Cue(e.Team == _player.Team ? Sfx.HoldOurs : Sfx.HoldTheirs);
                break;
            case SimEventType.FlagCaptured:
            case SimEventType.VipEscaped:
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

    /// <summary>
    /// The round's start cue, the breakout horn or the referee's whistle (<see cref="Core.RefereeCalls"/>). Asked for
    /// before the sounds are ready (the start of a round that began as the level loaded), it waits for them, up to
    /// <see cref="AudioDef.LateRoundCue_s"/>.
    /// </summary>
    public void Round(Sfx sfx)
    {
        RoundCues++;
        if (_set is null)
        {
            _lateCues.Add((sfx, _clock));
            return;
        }

        PlayRound(sfx);
    }

    /// <summary>Round cues asked for, played (late ones included) and dropped (asked for too long before the sounds were ready).</summary>
    public int RoundCues { get; private set; }

    public int RoundCuesPlayed { get; private set; }

    /// <summary>Dropped, and still waiting for the sounds (which count as dropped when a check asks).</summary>
    public int RoundCuesDropped => _droppedCues + _lateCues.Count;

    /// <summary>The latest a waiting round cue played after it was asked for (s).</summary>
    public double LatestRoundCue { get; private set; }

    /// <summary>The round cues asked for so far, in order, with whether each played (for the smoke tests' logs).</summary>
    public string RoundCueLine => $"{RoundCuesPlayed} of {RoundCues} round cues played" +
                                  (LatestRoundCue > 0 ? $" (one {LatestRoundCue:0.00} s late, waiting for the sounds)" : "") +
                                  (RoundCuesDropped > 0 ? $", {RoundCuesDropped} dropped" : "");

    private void PlayRound(Sfx sfx)
    {
        RoundCuesPlayed++;
        Play2D(_round, sfx, sfx switch { Sfx.Horn => _mix.Horn, Sfx.StartCue => _mix.RoundStart, _ => _mix.Whistle });
    }

    /// <summary>The sounds are ready: round cues that waited for them play now, unless they're too late to mean anything.</summary>
    private void PlayLateCues()
    {
        foreach ((Sfx sfx, double at) in _lateCues)
        {
            double late = _clock - at;
            if (late <= _def.LateRoundCue_s)
            {
                LatestRoundCue = Math.Max(LatestRoundCue, late);
                PlayRound(sfx);
            }
            else
            {
                _droppedCues++;
                GD.PushWarning($"The {sfx} cue was asked for {late:0.0} s before the sounds were ready, too late to play.");
            }
        }

        _lateCues.Clear();
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

        // Whatever happens next (the menu, another round) isn't muffled.
        _muffleAge = -1f;
        AudioBuses.Muffle(AudioBuses.Open);
        foreach (AudioStreamPlayer player in new[] { _shot, _local, _steps, _round, _referee, _stinger })
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
            Rattle((FootstepKind)e.Extra);
        }
        else if (Play3D(sfx, e.Position.ToGodot() + Vector3.Up * 0.2f, mix + loudness, Jitter(0.05f), MathF.Max(8f, e.Value * 2.5f)))
        {
            Steps++;
        }
    }

    /// <summary>
    /// Your loader's balls rattling as you land, jump and run: loudest half full (a full one is packed tight, a nearly empty
    /// one has little to rattle), not at all when empty. At most one every 0.15 s.
    /// </summary>
    private void Rattle(FootstepKind kind)
    {
        int capacity = _player.Marker.Paint.Params.Capacity;
        float fill = capacity > 0 ? (float)_player.Marker.Paint.Loader / capacity : 0f;
        bool jolt = kind is FootstepKind.Land or FootstepKind.Jump || _player.HorizontalSpeed > 4.2f;
        if (!jolt || fill <= 0f || _rattleWait > 0f)
        {
            return;
        }

        float amount = MathF.Max(0.3f, 4f * fill * (1f - fill));
        Play2D(_local, Sfx.LoaderRattle, _mix.LoaderRattle + 20f * MathF.Log10(amount) + (kind == FootstepKind.Land ? 4f : 0f), Jitter(0.06f));
        _rattleWait = 0.15f;
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

    /// <summary>
    /// The vehicles' engines: running while someone's at the wheel, higher the faster it goes and the harder it's pushed
    /// (a loop on a player of its own, following the vehicle).
    /// </summary>
    private void Engines()
    {
        Pb.Sim.Vehicles.VehicleSet vehicles = _sim.Vehicles;
        if (_set is null || vehicles.Count == 0)
        {
            return;
        }

        if (_engines.Length != vehicles.Count)
        {
            foreach (AudioStreamPlayer3D old in _engines)
            {
                old.QueueFree();
            }

            _engines = new AudioStreamPlayer3D[vehicles.Count];
            for (int i = 0; i < _engines.Length; i++)
            {
                _engines[i] = Player3D(AudioBuses.Effects, _def.UnitSize_m, _def.MaxDistance_m);
                _engines[i].Stream = _set.Get(Sfx.Engine, 0);
            }
        }

        for (int i = 0; i < _engines.Length; i++)
        {
            Pb.Sim.Vehicles.VehicleState v = vehicles[i];
            AudioStreamPlayer3D engine = _engines[i];
            bool running = v.Driver >= 0 || MathF.Abs(v.Speed) > 0.3f;
            if (running != engine.Playing && !AudioBuses.Silent)
            {
                if (running)
                {
                    engine.Play();
                }
                else
                {
                    engine.Stop();
                }
            }

            float pace = MathF.Abs(v.Speed) / MathF.Max(v.Params.MaxSpeed, 1f);
            engine.GlobalPosition = v.Position.ToGodot() + Vector3.Up * 0.6f;
            engine.PitchScale = 0.8f + 1.1f * pace + 0.2f * MathF.Abs(v.Throttle);
            engine.VolumeDb = _def.Volume_db + _mix.Engine + 3f * MathF.Abs(v.Throttle) + 3f * pace;
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
