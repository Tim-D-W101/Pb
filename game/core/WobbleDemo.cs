using Godot;
using Pb.Game.Player;
using Pb.Game.Ui;
using Pb.Game.World;
using Pb.Sim;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Level;
using Pb.Sim.Players;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Core;

/// <summary>
/// <c>-- --wobble-demo</c> (on a level with inflatables: <c>--level=sports_ground</c>): you stand a few metres off the
/// inflatable nearest your start and shoot it, a ball every <see cref="Every"/> ticks at the middle of the side facing
/// you, while a camera beside you watches its fabric dent in and shiver out where each one strikes
/// (<see cref="InflatableWobble"/>). It prints the tick of each shot, so a <c>--write-movie</c> run shows the ripples,
/// and how many hits the wobble took; then it quits.
/// </summary>
public partial class WobbleDemo : Node, ICommandSource
{
    /// <summary>Ticks between shots, how many, and how long after the last it waits for the ripple to settle.</summary>
    private const int Every = 90, Shots = 4, Settle = 120;

    /// <summary>How far off the inflatable you stand (m).</summary>
    private const float Off = 4f;

    private SimWorld _sim = null!;
    private InflatableWobble _wobble = null!;
    private SVector3 _target;
    private int _ticks;
    private int _fired;

    public void Start(SimWorld sim, InflatableWobble wobble, PlayerController player, Hud hud, float farClip)
    {
        _sim = sim;
        _wobble = wobble;
        SurfaceId surface = sim.Config.Surfaces.Get(InflatableWobble.Surface);
        SVector3 start = player.State.Position;
        LevelPrimitive? nearest = null;
        float best = float.MaxValue;
        foreach (LevelPrimitive p in sim.Level?.Primitives ?? System.Array.Empty<LevelPrimitive>())
        {
            float d = SVector3.Distance(new SVector3(p.Center.X, 0f, p.Center.Z), new SVector3(start.X, 0f, start.Z));
            if (p.Surface == surface && p.Bounds.Min.Y < 0.1f && d < best)
            {
                best = d;
                nearest = p;
            }
        }

        if (nearest is null)
        {
            GD.PushError("WOBBLE DEMO needs a level with inflatables (--level=sports_ground); quitting");
            GetTree().Quit(1);
            return;
        }

        // Its side facing your start, a little over halfway up: where you aim, and the camera looks.
        SVector3 toward = start - nearest.Center;
        toward.Y = 0f;
        toward = toward.LengthSquared() > 1e-4f ? SVector3.Normalize(toward) : new SVector3(0f, 0f, 1f);
        float radius = System.MathF.Max(nearest.HalfExtents.X, nearest.HalfExtents.Z);
        float height = nearest.Bounds.Max.Y - nearest.Bounds.Min.Y;
        _target = new SVector3(nearest.Center.X, nearest.Bounds.Min.Y + height * 0.55f, nearest.Center.Z);
        SVector3 stand = nearest.Center + toward * (radius + Off);
        stand.Y = 0f;
        player.Teleport(stand, ScenePositions.Facing(stand, _target));
        player.AutoPilot = this;
        player.ViewModel.Visible = false;
        hud.Visible = false;

        // Beside you and closer in, at a slant to the face, so the dents catch the light.
        SVector3 side = new(-toward.Z, 0f, toward.X);
        SVector3 face = _target + toward * radius;
        var camera = new Camera3D { Name = "WobbleCamera", Fov = 40f, Far = farClip, Near = 0.03f };
        AddChild(camera);
        camera.MakeCurrent();
        camera.LookAtFromPosition((face + toward * 1.9f + side * 1.5f + new SVector3(0f, 0.25f, 0f)).ToGodot(), face.ToGodot(), Vector3.Up);
        GD.Print($"WOBBLE DEMO the {sim.Level!.Owners[nearest.Owner]} at {nearest.Center}, {Shots} shots from {stand}, one every {Every} ticks");
    }

    public InputCommand Next(int tick, PlayerState state)
    {
        _ticks++;
        if (_ticks > Shots * Every + Settle)
        {
            GD.Print($"WOBBLE DEMO done: {_fired} shots, {_wobble.Hits} hits on inflatables");
            GetTree().Quit();
            return default;
        }

        (float yaw, float pitch) = ViewAngles.FromDirection(_target - state.EyePosition);
        bool fire = _ticks % Every == Every / 2 && _fired < Shots;
        if (fire)
        {
            _fired++;
            GD.Print($"WOBBLE DEMO shot {_fired} at tick {_ticks}");
        }

        return new InputCommand { Tick = tick, Yaw = yaw, Pitch = pitch, Buttons = fire ? InputButtons.Fire : InputButtons.None };
    }
}
