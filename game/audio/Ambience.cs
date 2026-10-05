using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Game.World;
using Pb.Sim.Level;

namespace Pb.Game.Audio;

/// <summary>
/// The sound of the place (presentation.jsonc "audio" → "ambience" and "reverb"), mixed by where you are: wind outdoors
/// that swells with the same gusts the weeds sway in, distant traffic, each indoor area's tone (its "tone" in the level,
/// or a room or a hall by its size) crossfaded as you move between them, crows calling from the birds in the air (and
/// wings where they're put up), and a train far off now and then. Indoors, everything outside is quieter and dull
/// (the Outside bus's low-pass closes), and the world's reverb grows with the room.
/// </summary>
public partial class Ambience : Node
{
    private static readonly Dictionary<string, Sfx> ToneSounds = new()
    {
        ["room"] = Sfx.ToneRoom,
        ["hall"] = Sfx.ToneHall,
        ["drip"] = Sfx.ToneDrip,
        ["pigeons"] = Sfx.TonePigeons,
        ["draught"] = Sfx.ToneDraught,
        ["cold"] = Sfx.ToneCold,
        ["hum"] = Sfx.ToneHum,
    };

    private readonly List<AreaSpec> _areas = new();
    private readonly Random _random = new(0xAB1E);
    private AudioDef _def = null!;
    private AmbienceDef _a = null!;
    private Vector2 _windDir;
    private float _gustSize;
    private Birds? _birds;
    private AudioStreamPlayer _wind = null!;
    private AudioStreamPlayer _traffic = null!;
    private readonly AudioStreamPlayer[] _tones = new AudioStreamPlayer[2];
    private readonly string[] _toneNames = { "", "" };
    private readonly float[] _toneLevels = { 0f, 0f };
    private int _active;
    private AudioStreamPlayer3D _crow = null!;
    private AudioStreamPlayer3D _wings = null!;
    private AudioStreamPlayer3D _train = null!;
    private bool _started;
    private float _crowIn;
    private float _trainIn;
    private float _trainTime;
    private float _wingsIn;
    private Vector3 _trainFrom;
    private Vector3 _trainTo;
    private Vector3 _centre;
    private Vector3? _flushedAt;
    private float _size = 0.2f;
    private float _damping = 0.7f;
    private float _wet = 0.04f;

    /// <summary>The area you're in ("outdoors" when none).</summary>
    public string Where { get; private set; } = "outdoors";

    /// <summary>1 outdoors, 0 indoors, easing between.</summary>
    public float Outdoor { get; private set; } = 1f;

    /// <summary>The tone playing (empty outdoors).</summary>
    public string Tone => _toneNames[_active];

    public int Crows { get; private set; }

    public int Trains { get; private set; }

    public void Initialize(AudioDef def, LevelLayout? level, Vector2 wind, float gustSize, Birds? birds)
    {
        _def = def;
        _a = def.Ambience;
        _windDir = wind.Normalized();
        _gustSize = gustSize;
        _birds = birds;
        if (birds is not null)
        {
            birds.Flushed += at => _flushedAt ??= at;
        }

        if (level is not null)
        {
            _areas.AddRange(level.Areas);
            _centre = ((level.Bounds.Min + level.Bounds.Max) * 0.5f).ToGodot();
            foreach (AreaSpec area in level.Areas)
            {
                if (area.Tone.Length > 0 && !ToneSounds.ContainsKey(area.Tone))
                {
                    GD.PushError($"{level.Id}: the area '{area.Name}' has the tone '{area.Tone}', which isn't one of {string.Join(", ", ToneSounds.Keys)}");
                }
            }
        }

        _wind = Loop(AudioBuses.Outside);
        _traffic = Loop(AudioBuses.Outside);
        _tones[0] = Loop(AudioBuses.Ambience);
        _tones[1] = Loop(AudioBuses.Ambience);
        _crow = Player3D(12f, 500f);
        _wings = Player3D(6f, 80f);
        _train = Player3D(80f, 4000f);
        _crowIn = Between(_a.CrowEvery_s);
        _trainIn = Between(_a.TrainEvery_s) * 0.5f;
    }

    public void Update(float dt, Vector3 ear, SoundSet bank)
    {
        if (!_started)
        {
            _started = true;
            _wind.Stream = bank.Get(Sfx.Wind, 0);
            _traffic.Stream = bank.Get(Sfx.Traffic, 0);
            Start(_wind);
            Start(_traffic);
        }

        AreaSpec? area = AreaAt(ear);
        bool indoor = area is { Indoor: true };
        Where = area?.Name ?? "outdoors";
        Outdoor = Mathf.MoveToward(Outdoor, indoor ? 0f : 1f, dt / 0.6f);
        float inside = 1f - Outdoor;

        // The wind follows the weeds' gusts where you stand (weeds.gdshader), and pitches up a little in a gust.
        float gust = Gust(ear);
        _wind.VolumeDb = Mathf.Lerp(_a.WindLull_db, _a.WindGust_db, gust) + inside * _a.WindIndoor_db;
        _wind.PitchScale = 0.92f + 0.16f * gust;
        _traffic.VolumeDb = _a.Traffic_db + inside * _a.WindIndoor_db;
        if (AudioBuses.OutsideFilter is { } filter)
        {
            filter.CutoffHz = MathF.Exp(Mathf.Lerp(MathF.Log(_a.IndoorCutoff_hz), MathF.Log(20000f), Outdoor));
        }

        string tone = !indoor ? "" : area!.Tone.Length > 0 ? area.Tone : area.Volume < _a.HallFrom_m3 ? "room" : "hall";
        Tones(dt, tone, bank);
        Reverb(dt, indoor ? area : null);
        Crow(dt, ear, bank);
        Wings(dt, bank);
        Train(dt, ear, bank);
    }

    /// <summary>The gust strength (0..1) where you stand: the weeds' shader's own formula, on the same clock.</summary>
    private float Gust(Vector3 at)
    {
        float time = (float)(Time.GetTicksMsec() / 1000.0 % 3600.0);
        float along = (at.X * _windDir.X + at.Z * _windDir.Y) / _gustSize;
        return Mathf.Clamp(0.55f + 0.45f * MathF.Sin(time * 0.31f + along), 0f, 1f);
    }

    /// <summary>The smallest area holding <paramref name="at"/> (rooms inside buildings inside the yard).</summary>
    private AreaSpec? AreaAt(Vector3 at)
    {
        System.Numerics.Vector3 p = at.ToSim();
        AreaSpec? best = null;
        foreach (AreaSpec area in _areas)
        {
            if (area.Box.Contains(p) && (best is null || area.Volume < best.Volume))
            {
                best = area;
            }
        }

        return best;
    }

    /// <summary>Crossfades to <paramref name="tone"/> (none outdoors) on the other of the two tone players.</summary>
    private void Tones(float dt, string tone, SoundSet bank)
    {
        if (tone != _toneNames[_active])
        {
            _active = 1 - _active;
            _toneNames[_active] = tone;
            if (tone.Length > 0 && ToneSounds.TryGetValue(tone, out Sfx sfx))
            {
                _tones[_active].Stream = bank.Get(sfx, 0);
                Start(_tones[_active], (float)(_random.NextDouble() * 5.0));
            }
        }

        float step = dt / _a.Fade_s;
        for (int i = 0; i < 2; i++)
        {
            bool on = i == _active && _toneNames[i].Length > 0;
            _toneLevels[i] = Mathf.MoveToward(_toneLevels[i], on ? 1f : 0f, step);
            if (_toneLevels[i] <= 0f)
            {
                if (_tones[i].Playing)
                {
                    _tones[i].Stop();
                }

                continue;
            }

            float db = _a.Tones.TryGetValue(_toneNames[i], out float level) ? level : -30f;
            _tones[i].VolumeDb = db + Mathf.LinearToDb(_toneLevels[i]);
        }
    }

    /// <summary>The world's reverb: outdoors, or by the size of the room you're in, blended over blend_s.</summary>
    private void Reverb(float dt, AreaSpec? room)
    {
        ReverbDef r = _def.Reverb;
        ReverbSettingDef target = r.Outdoor;
        float size = target.RoomSize, damping = target.Damping, wet = target.Wet;
        if (room is not null)
        {
            float t = Mathf.Clamp(MathF.Log(MathF.Max(room.Volume, 1f) / r.SmallRoom_m3) / MathF.Log(r.LargeRoom_m3 / r.SmallRoom_m3), 0f, 1f);
            size = Mathf.Lerp(r.Small.RoomSize, r.Large.RoomSize, t);
            damping = Mathf.Lerp(r.Small.Damping, r.Large.Damping, t);
            wet = Mathf.Lerp(r.Small.Wet, r.Large.Wet, t);
        }

        float k = 1f - MathF.Exp(-dt / r.Blend_s);
        _size += (size - _size) * k;
        _damping += (damping - _damping) * k;
        _wet += (wet - _wet) * k;
        if (AudioBuses.Reverb is { } reverb)
        {
            reverb.RoomSize = _size;
            reverb.Damping = _damping;
            reverb.Wet = _wet;
        }
    }

    /// <summary>A crow calls now and then while you're outside, from a bird in the air or somewhere off over the roofs.</summary>
    private void Crow(float dt, Vector3 ear, SoundSet bank)
    {
        if ((_crowIn -= dt) > 0f)
        {
            return;
        }

        _crowIn = Between(_a.CrowEvery_s);
        if (Outdoor < 0.3f)
        {
            return;
        }

        Vector3 at;
        if (_birds is null || !_birds.TryFlying(_random, out at))
        {
            float angle = (float)(_random.NextDouble() * Math.Tau);
            at = ear + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * (40f + 40f * (float)_random.NextDouble()) + Vector3.Up * 18f;
        }

        _crow.Stream = bank.Get(Sfx.Caw, _random.Next());
        _crow.GlobalPosition = at;
        _crow.VolumeDb = _a.Crow_db;
        _crow.PitchScale = 0.94f + 0.12f * (float)_random.NextDouble();
        Start(_crow);
        Crows++;
    }

    /// <summary>Birds put up off a perch: their wings, and a call.</summary>
    private void Wings(float dt, SoundSet bank)
    {
        _wingsIn -= dt;
        if (_flushedAt is not { } at || _wingsIn > 0f)
        {
            return;
        }

        _flushedAt = null;
        _wingsIn = 1.5f;
        _wings.Stream = bank.Get(Sfx.WingFlap, _random.Next());
        _wings.GlobalPosition = at;
        _wings.VolumeDb = _a.Crow_db;
        Start(_wings);
        if (!_crow.Playing)
        {
            _crow.Stream = bank.Get(Sfx.Caw, _random.Next());
            _crow.GlobalPosition = at + Vector3.Up * 2f;
            _crow.VolumeDb = _a.Crow_db;
            Start(_crow);
            Crows++;
        }
    }

    /// <summary>A train far off: it runs past along a line trainDistance_m away from the level, the sound moving with it.</summary>
    private void Train(float dt, Vector3 ear, SoundSet bank)
    {
        if (_train.Playing)
        {
            _trainTime += dt;
            float length = (float)_train.Stream.GetLength();
            _train.GlobalPosition = _trainFrom.Lerp(_trainTo, Mathf.Clamp(_trainTime / length, 0f, 1f));
            return;
        }

        if ((_trainIn -= dt) > 0f)
        {
            return;
        }

        _trainIn = Between(_a.TrainEvery_s);
        float angle = (float)(_random.NextDouble() * Math.Tau);
        var away = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
        var along = new Vector3(-away.Z, 0f, away.X) * 600f;
        Vector3 middle = (_areas.Count > 0 ? _centre : ear) + away * _a.TrainDistance_m;
        _trainFrom = middle - along;
        _trainTo = middle + along;
        _trainTime = 0f;
        _train.Stream = bank.Get(Sfx.Train, 0);
        _train.GlobalPosition = _trainFrom;
        _train.VolumeDb = _a.Train_db;
        Start(_train);
        Trains++;
    }

    private float Between(float[] range) => range[0] + (range[1] - range[0]) * (float)_random.NextDouble();

    private static void Start(AudioStreamPlayer player, float from = 0f)
    {
        if (!AudioBuses.Silent)
        {
            player.Play(from);
        }
    }

    private static void Start(AudioStreamPlayer3D player)
    {
        if (!AudioBuses.Silent)
        {
            player.Play();
        }
    }

    /// <summary>Stops every sound and lets go of their streams (the scene is going).</summary>
    public void Stop()
    {
        foreach (AudioStreamPlayer player in new[] { _wind, _traffic, _tones[0], _tones[1] })
        {
            if (player is not null)
            {
                player.Stop();
                player.Stream = null;
            }
        }

        foreach (AudioStreamPlayer3D player in new[] { _crow, _wings, _train })
        {
            if (player is not null)
            {
                player.Stop();
                player.Stream = null;
            }
        }
    }

    private AudioStreamPlayer Loop(StringName bus)
    {
        var player = new AudioStreamPlayer { Bus = bus, VolumeDb = -80f };
        AddChild(player);
        return player;
    }

    private AudioStreamPlayer3D Player3D(float unitSize, float maxDistance)
    {
        var player = new AudioStreamPlayer3D
        {
            Bus = AudioBuses.Outside,
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
