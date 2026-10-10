using System.Collections.Generic;
using System.Linq;
using Godot;
using Pb.Game.Player;
using Pb.Game.Ui;
using Pb.Sim;
using Pb.Sim.Core;
using Pb.Sim.Gear;
using Pb.Sim.Players;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --gear-demo</c>: opponents stand in a row on clear ground, each in one brand's whole kit in its own colours,
/// and a camera goes round them: the row from in front, from the side and from behind, then each one's marker and head
/// close up; then your own marker in first person in each brand's kit. <c>--gear-demo=masks</c> puts every brand's mask
/// on every character (it wants <c>--size=9</c>), their hands free of the marker, and looks at each head from eight
/// directions. Each view is held for <c>--gear-hold=N</c> frames (8) and printed as it comes, so a <c>--write-movie</c>
/// run gives a still of each; it quits after the last. <c>--gear-side</c> dresses them in their side's colour, as in a
/// round with sides; <c>--gear-zones</c> shows the characters' zones instead of their clothes.
/// </summary>
public partial class GearDemo : Node, ICommandSource
{
    private readonly List<(string Name, SVector3 From, SVector3 At, int FirstPerson, int Head, float Around)> _shots = new();
    private readonly List<OpponentPawn> _row = new();
    private SimWorld _sim = null!;
    private PlayerController _player = null!;
    private Camera3D _camera = null!;
    private Color _paint;
    private GearCatalog _gear = null!;
    private float _yaw;
    private int _hold = 8;
    private int _frame;
    private int _shown = -1;

    public void Start(SimWorld sim, GearCatalog gear, PlayerController player, IReadOnlyList<OpponentPawn> opponents, Hud hud, float farClip, SVector3 towardSun,
        System.Func<int, Color> sideColour)
    {
        _sim = sim;
        _gear = gear;
        _player = player;
        _hold = Args.Ticks("--gear-hold", 8) ?? 8;
        hud.Visible = false;
        bool masks = Args.Value("--gear-demo") == "masks";
        var dresses = new List<(string Name, Loadout Loadout)>();
        if (masks)
        {
            foreach (int mask in gear.ItemsIn(GearSlot.Mask).Where(i => !MaskRecipes.IsOwn(gear.Items[i].Shape)))
            {
                for (int look = 0; look < 3; look++)
                {
                    Loadout loadout = gear.Default(look);
                    loadout[GearSlot.Mask] = new GearChoice(mask, gear.Items[mask].Colours);
                    dresses.Add(($"{gear.Items[mask].Brand.DisplayName} {gear.Items[mask].DisplayName} on character {look + 1}", loadout));
                }
            }
        }
        else
        {
            for (int i = 0; i < gear.Brands.Count; i++)
            {
                dresses.Add((gear.Brands[i].DisplayName, Kit.Brand(gear, gear.Brands[i].Id, i) ?? gear.Default(i)));
            }
        }

        float spacing = masks ? 1.1f : 1.3f;
        if (opponents.Count < dresses.Count || !FindRoom(opponents[0].State.Position, towardSun, dresses.Count * spacing * 0.5f + 1.5f, out SVector3 centre, out _yaw))
        {
            GD.PushError($"GEAR DEMO needs {dresses.Count} opponents and a clear patch of ground; quitting");
            GetTree().Quit(1);
            return;
        }

        // They face the camera, which stands ahead of them; the row runs across.
        SVector3 ahead = ViewAngles.FlatForward(_yaw);
        SVector3 across = new(-ahead.Z, 0f, ahead.X);
        for (int i = 0; i < opponents.Count; i++)
        {
            OpponentPawn pawn = opponents[i];
            if (i >= dresses.Count)
            {
                pawn.Teleport(centre - ahead * 40f + across * (i * 3f), _yaw);
                pawn.Visible = false;
                continue;
            }

            SVector3 at = centre + across * ((i - (dresses.Count - 1) * 0.5f) * spacing);
            pawn.Teleport(at, _yaw);
            pawn.Steer(this);
            pawn.Redress(new Kit(gear, dresses[i].Loadout, Args.Has("--gear-side") ? sideColour(pawn.State.Team) : null));
            pawn.Visual.HoldsMarker = !masks;
            _row.Add(pawn);
        }

        _paint = sideColour(player.State.Team);
        SVector3 up = new(0f, 1f, 0f);
        SVector3 middle = centre + up * 1.05f;
        float wide = dresses.Count * spacing * 0.55f;
        _shots.Add(("the row from in front", middle + ahead * (wide + 2.4f) + up * 0.35f, middle, -1, -1, 0f));
        if (masks)
        {
            for (int i = 0; i < _row.Count; i++)
            {
                for (int k = 0; k < 8; k++)
                {
                    _shots.Add(($"{dresses[i].Name}, from {k * 45}°", default, default, -1, i, k * Mathf.Pi / 4f));
                }
            }
        }
        else
        {
            _shots.Add(("the row from the right", middle + ahead * 3.2f + across * 3.4f + up * 0.3f, middle, -1, -1, 0f));
            _shots.Add(("the row from behind", middle - ahead * 3.4f - across * 2.2f + up * 0.5f, middle, -1, -1, 0f));
            for (int i = 0; i < _row.Count; i++)
            {
                SVector3 at = _row[i].State.Position;
                string brand = dresses[i].Name;
                _shots.Add(($"{brand}: marker, loader and tank", at + across * 1.05f + ahead * 0.55f + up * 1.42f, at + up * 1.28f + ahead * 0.25f, -1, -1, 0f));
                _shots.Add(($"{brand}: from the other side", at - across * 1.05f + ahead * 0.35f + up * 1.38f, at + up * 1.25f + ahead * 0.2f, -1, -1, 0f));
                _shots.Add(($"{brand}: head and mask", default, default, -1, i, -0.6f));
            }

            for (int i = 0; i < gear.Brands.Count; i++)
            {
                _shots.Add(($"{gear.Brands[i].DisplayName} in first person", SVector3.Zero, SVector3.Zero, i, -1, 0f));
            }
        }

        _camera = new Camera3D { Name = "GearCamera", Fov = 40f, Far = farClip, Near = 0.03f };
        AddChild(_camera);
        _camera.MakeCurrent();
        _player.ViewModel.Visible = false;
        GD.Print($"GEAR DEMO {_row.Count} kits at {centre}, {_shots.Count} views × {_hold} frames");
    }

    public override void _Process(double delta)
    {
        if (_shots.Count == 0)
        {
            return;
        }

        int index = _frame / _hold;
        if (index >= _shots.Count)
        {
            GD.Print("GEAR DEMO done");
            GetTree().Quit();
            return;
        }

        if (index != _shown)
        {
            _shown = index;
            (string name, SVector3 from, SVector3 at, int firstPerson, int head, float around) = _shots[index];
            if (firstPerson >= 0)
            {
                Loadout loadout = Kit.Brand(_gear, _gear.Brands[firstPerson].Id, 0) ?? _gear.Default(0);
                _player.Redress(new Kit(_gear, loadout), _paint);
                _player.Camera.MakeCurrent();
            }
            else
            {
                if (head >= 0)
                {
                    // Round the head at its own height, from the front (0) turning to the right.
                    Vector3 middle = _row[head].Visual.Model?.Attachment("Head").GlobalPosition + Vector3.Up * 0.06f
                                     ?? (_row[head].State.Position + new SVector3(0f, 1.55f, 0f)).ToGodot();
                    SVector3 ahead = ViewAngles.FlatForward(_yaw);
                    var forward = new Vector3(ahead.X, 0f, ahead.Z);
                    Vector3 offset = forward.Rotated(Vector3.Up, -around) * 0.6f + Vector3.Up * 0.04f;
                    _camera.GlobalPosition = middle + offset;
                    _camera.LookAt(middle, Vector3.Up);
                }
                else
                {
                    _camera.GlobalPosition = from.ToGodot();
                    _camera.LookAt(at.ToGodot(), Vector3.Up);
                }

                _camera.MakeCurrent();
            }

            GD.Print($"GEAR DEMO view {index}: {name} (frames {_frame}–{_frame + _hold - 1})");
        }

        _frame++;
    }

    /// <summary>Standing still, facing the camera, the marker held level.</summary>
    public InputCommand Next(int tick, PlayerState me) => new() { Tick = tick, Yaw = _yaw, Pitch = 0f };

    /// <summary>
    /// Clear ground near <paramref name="near"/> for the row (<paramref name="half"/> either side) and the cameras round
    /// it (searched outwards on a 2 m grid), with the row facing the sun's side so it's lit.
    /// </summary>
    private bool FindRoom(SVector3 near, SVector3 towardSun, float half, out SVector3 centre, out float yaw)
    {
        yaw = Mathf.Atan2(-towardSun.X, -towardSun.Z);
        for (int ring = 0; ring <= 40; ring++)
        {
            for (int x = -ring; x <= ring; x++)
            {
                for (int z = -ring; z <= ring; z++)
                {
                    if (System.Math.Max(System.Math.Abs(x), System.Math.Abs(z)) != ring)
                    {
                        continue;
                    }

                    SVector3 probe = near + new SVector3(x * 2f, 0f, z * 2f);
                    if (_sim.Collision.SweepSphere(probe + new SVector3(0f, 2f, 0f), probe - new SVector3(0f, 2f, 0f), 0f, out var floor) &&
                        Clear(floor.Point, yaw, half))
                    {
                        centre = floor.Point;
                        return true;
                    }
                }
            }
        }

        centre = near;
        return false;
    }

    /// <summary>Room to stand and level ground from 4 m behind the row to 5 m ahead of it, <paramref name="half"/> either side.</summary>
    private bool Clear(SVector3 from, float yaw, float half)
    {
        SVector3 ahead = ViewAngles.FlatForward(yaw);
        SVector3 side = new(-ahead.Z, 0f, ahead.X);
        for (float d = -4f; d <= 5f; d += 0.75f)
        {
            for (float s = -half; s <= half; s += 1f)
            {
                SVector3 at = from + ahead * d + side * s;
                bool room = !_sim.Collision.SweepSphere(at + new SVector3(0f, 0.45f, 0f), at + new SVector3(0f, 2.2f, 0f), 0.35f, out _);
                bool ground = _sim.Collision.SweepSphere(at + new SVector3(0f, 0.3f, 0f), at - new SVector3(0f, 0.3f, 0f), 0f, out var floor) &&
                              System.MathF.Abs(floor.Point.Y - from.Y) < 0.1f;
                if (!room || !ground || !(_sim.Level?.Bounds.Contains(at + new SVector3(0f, 1f, 0f)) ?? true))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
