using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Game.Player;
using Pb.Sim.Events;

namespace Pb.Game.Ballistics;

/// <summary>
/// Fresh paint running down the walls (presentation.jsonc "paintDrips"): where a ball breaks on a steep
/// face of the world (not on a player or a target), a few runs creep down from the splat, quickly at first
/// and slowing as the paint gives out, each ending in a bead. Decals in the paint's colour, like the splats
/// (<see cref="SplatSystem"/>), from a pool with a cap: once it's full the oldest runs are reused. Looks only.
/// </summary>
public partial class PaintDrips : Node3D, ISimEventListener
{
    private static ImageTexture[]? _textures;

    private readonly List<Decal> _decals = new();
    private readonly List<Run> _running = new();
    private readonly RandomNumberGenerator _rng = new();
    private Color[] _teamColors = Array.Empty<Color>();
    private PaintDripsDef _def = null!;
    private SplatDef _splat = null!;
    private int _next;
    private double _now;

    private readonly record struct Run(Decal Decal, Vector3 Top, Basis Basis, float Length, double Start, float Time);

    public void Initialize(PresentationDef view)
    {
        _rng.Seed = 4321;
        _def = view.PaintDrips;
        _splat = view.Splat;
        _teamColors = new Color[view.TeamColors.Length];
        for (int i = 0; i < _teamColors.Length; i++)
        {
            _teamColors[i] = Color.FromHtml(view.TeamColors[i]);
        }

        _textures ??= new[] { DripTexture(1), DripTexture(2), DripTexture(3) };
    }

    public void OnSimEvent(in SimEvent e)
    {
        if (e.Type != SimEventType.BallBroke || e.TargetId >= 0 || _def.Cap == 0 || MathF.Abs(e.Normal.Y) > _def.Steepest || _rng.Randf() > _def.Share)
        {
            return;
        }

        Vector3 normal = e.Normal.ToGodot().Normalized();
        // Down the face: gravity with the part into the wall taken out.
        Vector3 down = (Vector3.Down - normal * Vector3.Down.Dot(normal)).Normalized();
        Vector3 across = normal.Cross(down);
        var basis = new Basis(across, normal, down);
        Color color = _teamColors[e.Team % _teamColors.Length];
        int runs = _rng.RandiRange(_def.Count[0], _def.Count[1]);
        for (int k = 0; k < runs; k++)
        {
            // From the lower half of the splat, spread across it.
            Vector3 top = e.Position.ToGodot() + down * _rng.RandfRange(0.01f, 0.05f) + across * _rng.RandfRange(-0.05f, 0.05f);
            Start(top, basis, color);
        }
    }

    public override void _Process(double delta)
    {
        _now += delta;
        for (int i = _running.Count - 1; i >= 0; i--)
        {
            Run r = _running[i];
            float t = Mathf.Clamp((float)((_now - r.Start) / r.Time), 0f, 1f);
            // Quickly at first, slowing as the paint gives out.
            float length = r.Length * (1f - (1f - t) * (1f - t) * (1f - t));
            Place(r.Decal, r.Top, r.Basis, MathF.Max(length, 0.01f));
            if (t >= 1f)
            {
                _running.RemoveAt(i);
            }
        }
    }

    public void ClearAll()
    {
        foreach (Decal decal in _decals)
        {
            decal.QueueFree();
        }

        _decals.Clear();
        _running.Clear();
        _next = 0;
    }

    private void Start(Vector3 top, Basis basis, Color color)
    {
        Decal decal;
        if (_decals.Count < _def.Cap)
        {
            decal = new Decal
            {
                CullMask = 0xFFFFF & ~ViewModel.RenderLayer,
                NormalFade = _splat.NormalFade,
                UpperFade = 0.3f,
                LowerFade = 0.3f,
                DistanceFadeEnabled = true,
                DistanceFadeBegin = _splat.DistanceFadeBegin_m,
                DistanceFadeLength = _splat.DistanceFadeLength_m,
            };
            AddChild(decal);
            _decals.Add(decal);
        }
        else
        {
            decal = _decals[_next];
            _next = (_next + 1) % _decals.Count;
            for (int i = _running.Count - 1; i >= 0; i--)
            {
                if (_running[i].Decal == decal)
                {
                    _running.RemoveAt(i);
                }
            }
        }

        float width = _rng.RandfRange(_def.Width_m[0], _def.Width_m[1]);
        decal.TextureAlbedo = _textures![_rng.RandiRange(0, _textures.Length - 1)];
        decal.Modulate = color;
        decal.Size = new Vector3(width, _splat.Depth_m, 0.01f);
        var run = new Run(decal, top, basis, _rng.RandfRange(_def.Length_m[0], _def.Length_m[1]), _now, _rng.RandfRange(_def.Run_s[0], _def.Run_s[1]));
        Place(decal, top, basis, 0.01f);
        _running.Add(run);
    }

    /// <summary>A run <paramref name="length"/> long hanging from <paramref name="top"/> down the face.</summary>
    private static void Place(Decal decal, Vector3 top, Basis basis, float length)
    {
        Vector3 size = decal.Size;
        decal.Size = new Vector3(size.X, size.Y, length);
        decal.GlobalTransform = new Transform3D(basis, top + basis.Z * (length * 0.5f));
    }

    /// <summary>
    /// A run of paint, white on clear, top at the top: thick where it leaves the splat, thinning and
    /// wavering as it goes, ending in a bead.
    /// </summary>
    private static ImageTexture DripTexture(int seed)
    {
        const int width = 32, height = 128;
        var random = new Random(seed * 7919);
        float a = (float)random.NextDouble() * Mathf.Tau, b = (float)random.NextDouble() * Mathf.Tau;
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            float centre = 0.5f + 0.06f * MathF.Sin(v * 9f + a) + 0.03f * MathF.Sin(v * 23f + b);
            float half = Mathf.Lerp(0.42f, 0.18f, MathF.Sqrt(v));
            // The bead at the end.
            float bead = v > 0.86f ? MathF.Sqrt(MathF.Max(0f, 1f - MathF.Pow((v - 0.93f) / 0.07f, 2f))) * 0.34f : 0f;
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                float edge = MathF.Max(half - MathF.Abs(u - centre), bead - MathF.Abs(u - centre));
                if (v > 0.995f)
                {
                    edge = 0f;
                }

                int i = (y * width + x) * 4;
                rgba[i] = rgba[i + 1] = rgba[i + 2] = 255;
                rgba[i + 3] = (byte)(Mathf.Clamp(edge * 30f, 0f, 1f) * 255f);
            }
        }

        Image image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }
}
