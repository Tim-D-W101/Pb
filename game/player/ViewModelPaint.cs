using System;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Collision;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Players;

namespace Pb.Game.Player;

/// <summary>
/// Paint on your own marker in first person (Phase 5; spec §1.3, "the first-person marker"): a ball that puts you out by
/// your marker, loader or tank leaves a splat on that part, and one breaking on cover close in front of you spatters the
/// marker with drops. The paint is drawn by the view model's shader at spots on the marker (presentation.jsonc
/// "feedback" → "viewModelPaint" → "spots"), and runs down as it ages. It stays for the round; past the most it holds, the
/// oldest goes. Presentation only.
/// </summary>
public partial class ViewModelPaint : Node, ISimEventListener
{
    /// <summary>How many splats the shader holds.</summary>
    public const int Slots = 8;

    private readonly Vector4[] _at = new Vector4[Slots];
    private readonly Vector4[] _colour = new Vector4[Slots];
    private readonly float[] _age = new float[Slots];
    private readonly RandomNumberGenerator _rng = new() { Seed = 5151 };
    private ViewModel _view = null!;
    private PlayerState _player = null!;
    private ViewModelPaintDef _def = null!;
    private Color[] _teams = Array.Empty<Color>();
    private Vector3[] _spots = Array.Empty<Vector3>();
    private string[] _parts = Array.Empty<string>();
    private int _next;
    private bool _dirty;

    /// <summary>Splats put on the marker so far (for the smoke tests' report).</summary>
    public int Count { get; private set; }

    public void Initialize(ViewModel view, PlayerState player, PresentationDef presentation)
    {
        _view = view;
        _player = player;
        _def = presentation.Feedback.ViewModelPaint;
        _teams = Array.ConvertAll(presentation.TeamColors, c => Color.FromHtml(c));
        _spots = new Vector3[_def.Spots.Length];
        _parts = new string[_def.Spots.Length];
        for (int i = 0; i < _spots.Length; i++)
        {
            _spots[i] = ViewModel.MarkerPoint(Validator.ToVector3(_def.Spots[i].At_m));
            _parts[i] = _def.Spots[i].Part;
        }
    }

    public void OnSimEvent(in SimEvent e)
    {
        if (e.Type != SimEventType.BallBroke || _def is null || _spots.Length == 0 || _def.Max == 0)
        {
            return;
        }

        Color paint = _teams[e.Team % _teams.Length];
        if (PlayerHitboxes.IsPlayer(e.TargetId) && PlayerHitboxes.PlayerIdOf(e.TargetId) == _player.Id)
        {
            // On you: a splat on the part it hit, if it's one of your gear's.
            string? part = (HitboxPart)e.Extra switch
            {
                HitboxPart.Marker => "barrel",
                HitboxPart.Loader => "loader",
                HitboxPart.Tank => "tank",
                _ => null,
            };
            if (part is not null && e.Lethal)
            {
                Put(Spot(part), _def.LethalSize_m, paint);
            }

            return;
        }

        // On something close in front of you: drops of it on the marker.
        if (!_player.Alive || e.PlayerId == _player.Id)
        {
            return;
        }

        System.Numerics.Vector3 to = e.Position - _player.EyePosition;
        float distance = to.Length();
        System.Numerics.Vector3 forward = Pb.Sim.Core.ViewAngles.Forward(_player.Yaw, _player.Pitch);
        if (distance > _def.Reach_m || distance < 1e-3f || System.Numerics.Vector3.Dot(to / distance, forward) < 0.35f)
        {
            return;
        }

        int drops = 1 + (int)((1f - distance / _def.Reach_m) * 3f);
        for (int i = 0; i < drops; i++)
        {
            Put(_rng.RandiRange(0, _spots.Length - 1), _rng.RandfRange(_def.Size_m[0], _def.Size_m[1]), paint);
        }
    }

    /// <summary>Clears the marker (a new round).</summary>
    public void Clear()
    {
        Array.Clear(_at);
        Array.Clear(_colour);
        _dirty = true;
    }

    public override void _Process(double delta)
    {
        if (_def is null)
        {
            return;
        }

        float dt = (float)delta;
        for (int i = 0; i < Slots; i++)
        {
            if (_at[i].W > 0f && _colour[i].W < 1f)
            {
                _age[i] += dt;
                // Runs quickly at first, then slows as it dries.
                float t = Mathf.Clamp(_age[i] / _def.Run_s, 0f, 1f);
                _colour[i].W = 1f - (1f - t) * (1f - t);
                _dirty = true;
            }
        }

        if (_dirty)
        {
            _view.SetPaint(_at, _colour);
            _dirty = false;
        }
    }

    /// <summary>A spot on <paramref name="part"/> (any spot if none is that part's).</summary>
    private int Spot(string part)
    {
        int found = 0, pick = -1;
        for (int i = 0; i < _parts.Length; i++)
        {
            if (_parts[i] == part && _rng.RandiRange(0, found++) == 0)
            {
                pick = i;
            }
        }

        return pick >= 0 ? pick : _rng.RandiRange(0, _spots.Length - 1);
    }

    private void Put(int spot, float size, Color paint)
    {
        int slot = _next;
        _next = (_next + 1) % Math.Min(Slots, _def.Max);
        Vector3 jitter = new Vector3(_rng.Randf() - 0.5f, _rng.Randf() - 0.5f, _rng.Randf() - 0.5f) * 0.02f;
        Vector3 at = _spots[spot] + jitter;
        _at[slot] = new Vector4(at.X, at.Y, at.Z, size * 0.5f);
        _colour[slot] = new Vector4(paint.R, paint.G, paint.B, 0f);
        _age[slot] = 0f;
        _dirty = true;
        Count++;
    }
}
