using System;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Events;
using Pb.Sim.Players;

namespace Pb.Game.Audio;

/// <summary>
/// Maps sim events to sounds (spec §8, placeholder set). The local marker plays in 2D with a
/// pitch that follows tank pressure; world sounds use pooled 3D players so distant fire is
/// directional. A per-frame budget keeps stress mode from flooding the mixer.
/// </summary>
public partial class AudioDirector : Node, ISimEventListener
{
    private AudioStreamPlayer _local = null!;
    private AudioStreamPlayer _ui = null!;
    private AudioStreamPlayer3D[] _voices = Array.Empty<AudioStreamPlayer3D>();
    private AudioStreamWav _shot = null!;
    private AudioStreamWav _splat = null!;
    private AudioStreamWav _bounce = null!;
    private AudioStreamWav _dry = null!;
    private AudioStreamWav _refill = null!;
    private PlayerState _player = null!;
    private AudioDef _def = null!;
    private int _next;
    private int _budget;

    public void Initialize(PlayerState localPlayer, PresentationDef view)
    {
        _player = localPlayer;
        _def = view.Audio;
        _shot = ProceduralSounds.Shot();
        _splat = ProceduralSounds.Splat();
        _bounce = ProceduralSounds.Bounce();
        _dry = ProceduralSounds.DryFire();
        _refill = ProceduralSounds.Refill();

        _local = new AudioStreamPlayer { Stream = _shot, VolumeDb = _def.Volume_db, MaxPolyphony = 4 };
        _ui = new AudioStreamPlayer { VolumeDb = _def.Volume_db - 4f, MaxPolyphony = 2 };
        AddChild(_local);
        AddChild(_ui);

        _voices = new AudioStreamPlayer3D[_def.Voices];
        for (int i = 0; i < _voices.Length; i++)
        {
            _voices[i] = new AudioStreamPlayer3D
            {
                VolumeDb = _def.Volume_db,
                UnitSize = 6f,
                MaxDistance = _def.MaxDistance_m,
                AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
            };
            AddChild(_voices[i]);
        }
    }

    public void OnSimEvent(in SimEvent e)
    {
        if (_player is null)
        {
            return;
        }

        bool local = e.PlayerId == _player.Id;
        switch (e.Type)
        {
            case SimEventType.ShotFired when local:
                float fill = _player.Marker.Air.FillFraction;
                _local.PitchScale = Mathf.Lerp(_def.ShotPitchEmpty, _def.ShotPitchFull, fill);
                _local.Play();
                break;
            case SimEventType.ShotFired:
                Play3D(_shot, e.Position.ToGodot(), -10f, 1f);
                break;
            case SimEventType.BallBroke:
                Play3D(_splat, e.Position.ToGodot(), 0f, 0.9f + (e.ShotSequence % 5) * 0.05f);
                break;
            case SimEventType.BallBounced when e.Value > 6f:
                Play3D(_bounce, e.Position.ToGodot(), -6f, 1f);
                break;
            case SimEventType.DryFire when local:
                PlayUi(_dry);
                break;
            case SimEventType.RefillStarted when local:
            case SimEventType.RefillCompleted when local:
                PlayUi(_refill);
                break;
        }
    }

    public override void _Process(double delta) => _budget = _def?.MaxSoundsPerFrame ?? 0;

    private void Play3D(AudioStream stream, Vector3 position, float volumeOffsetDb, float pitch)
    {
        if (_budget <= 0 || _voices.Length == 0)
        {
            return;
        }

        _budget--;
        AudioStreamPlayer3D voice = _voices[_next];
        _next = (_next + 1) % _voices.Length;
        voice.Stream = stream;
        voice.GlobalPosition = position;
        voice.VolumeDb = _def.Volume_db + volumeOffsetDb;
        voice.PitchScale = pitch;
        voice.Play();
    }

    private void PlayUi(AudioStream stream)
    {
        _ui.Stream = stream;
        _ui.Play();
    }
}
