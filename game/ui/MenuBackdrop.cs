using System;
using Godot;
using Pb.Game.Core;
using Pb.Game.World;
using Pb.Sim.Collision;
using Pb.Sim.Data;
using Pb.Sim.Level;

namespace Pb.Game.Ui;

/// <summary>
/// The level behind the main menu (presentation.jsonc "menuBackdrop"): its buildings, props, weeds,
/// things on the ground, ivy, painted markings and crows overhead under the level's light, at the saved graphics preset, seen from a
/// camera drifting slowly from one point to another and back. No sim, no bots: just the place. It's
/// built a piece a frame after the menu first shows, so the menu never waits for it, and says when
/// it's ready (<see cref="Shown"/>) so the menu can fade it in.
/// </summary>
public partial class MenuBackdrop : Node3D
{
    private Camera3D? _camera;
    private MenuBackdropDef _def = null!;
    private BackdropShotDef _shot = null!;
    private Action[] _steps = Array.Empty<Action>();
    private int _step = -2;
    private double _time;

    /// <summary>Called once the whole level is built and the camera is on it.</summary>
    public event Action? Shown;

    /// <summary>
    /// Queues the build of <paramref name="levelId"/>'s shot (or the first shot if it has none); it starts a couple of
    /// frames later, one piece a frame.
    /// </summary>
    public void Build(GameData data, PresentationDef view, GameSettings settings, string? levelId)
    {
        _def = view.MenuBackdrop;
        _shot = _def.ShotFor(levelId);
        if (!data.Levels.TryGetValue(_shot.Level, out LevelLayout? level))
        {
            GD.PushWarning($"presentation.jsonc menuBackdrop: no level '{_shot.Level}'; the menu has no backdrop");
            return;
        }

        GraphicsPresetDef preset = view.Graphics.Effective(settings.GraphicsPreset, settings.Graphics);
        var environment = new WorldEnvironment { Name = "WorldEnvironment", Environment = new Godot.Environment() };
        var sun = new DirectionalLight3D { Name = "Sun", ShadowEnabled = true };
        var collision = new CollisionWorld();
        var watch = new System.Diagnostics.Stopwatch();
        var world = new LevelBuilder { Name = "World" };
        _steps = new Action[]
        {
            () =>
            {
                watch.Start();
                AddChild(world);
                world.Build(level, new MaterialLibrary(level.Materials), preset.AmbientProbes, view.Horizon, view.Woods);
                level.BuildCollision(collision);
            },
            () =>
            {
                var paths = new WornPaths { Name = "WornPaths" };
                AddChild(paths);
                paths.Plan(level, collision, view.WornPaths);
                var weeds = new WeedField { Name = "Weeds", Wind = view.GroundWind };
                AddChild(weeds);
                weeds.Build(level, collision, view.Weeds, paths.Clear);
                paths.Draw(level, collision, view.WornPaths);
                weeds.ApplyPreset(preset);
                var cracks = new Cracks { Name = "Cracks" };
                AddChild(cracks);
                cracks.Build(level, collision, view.Weeds, view.Cracks);
                var fittings = new YardFittings { Name = "YardFittings" };
                AddChild(fittings);
                fittings.Build(level, collision, view.YardFittings, world.Materials);
            },
            () =>
            {
                if (preset.GroundDetail)
                {
                    var ground = new GroundDetail { Name = "GroundDetail" };
                    AddChild(ground);
                    ground.Build(level, collision, view.GroundDetail);
                    var debris = new FloorDebris { Name = "FloorDebris" };
                    AddChild(debris);
                    debris.Build(level, collision, view.FloorDebris);
                }
            },
            () =>
            {
                var creepers = new Creepers { Name = "Creepers" };
                AddChild(creepers);
                creepers.Build(level, collision, view.Creepers);
                var runOff = new RunOff { Name = "RunOff" };
                AddChild(runOff);
                runOff.Build(level, collision, world.Drips, view.RunOff);
                var markings = new Markings { Name = "Markings" };
                AddChild(markings);
                markings.Build(level, view.Markings);
                var graffiti = new Graffiti { Name = "Graffiti" };
                AddChild(graffiti);
                graffiti.Build(level, view.Graffiti, world.Piers);
                var bags = new SnaggedBags { Name = "SnaggedBags" };
                AddChild(bags);
                bags.Build(level, world.Strands, view.SnaggedBags, view.GroundWind);
                var tatters = new RoofTatters { Name = "RoofTatters" };
                AddChild(tatters);
                tatters.Build(level, view.RoofTatters, view.GroundWind);
                var litter = new BlowingLitter { Name = "BlowingLitter" };
                AddChild(litter);
                litter.Build(level, collision, view.BlowingLitter, view.GroundWind);
                if (!preset.Ssao)
                {
                    var contact = new ContactShadows { Name = "ContactShadows" };
                    AddChild(contact);
                    contact.Build(level, view.ContactShadows);
                }
                var birds = new Birds { Name = "Birds" };
                AddChild(birds);
                Pb.Sim.Collision.Aabb bounds = level.Bounds;
                birds.Build(new Vector3((bounds.Min.X + bounds.Max.X) * 0.5f, 0f, (bounds.Min.Z + bounds.Max.Z) * 0.5f), LevelBuilder.StableHash(level.Id), view.Birds, level, collision);
            },
            () =>
            {
                AddChild(environment);
                AddChild(sun);
                Atmosphere.ApplyLighting(environment, sun, view.Lighting);
                Atmosphere.ApplyPreset(environment, sun, GetViewport(), preset);
                Atmosphere.ApplyRenderScale(GetViewport(), settings.RenderScale, view.Graphics);
                _camera = new Camera3D { Name = "Camera", Fov = _def.Fov_deg, Near = 0.1f, Far = view.Camera.FarClip_m };
                AddChild(_camera);
                _camera.MakeCurrent();
                Place(0f);
                GD.Print($"Menu backdrop: {level.Id} at preset {preset.Name}, built in {watch.Elapsed.TotalMilliseconds:0} ms over {_steps.Length} frames");
                Shown?.Invoke();
            },
        };
    }

    public override void _Process(double delta)
    {
        // Let the menu draw first, then a piece of the level a frame.
        if (_step < _steps.Length)
        {
            // On to the next piece first: one that throws (Godot logs it) is left out, not built again
            // every frame on top of what it had already added.
            int step = _step++;
            if (step >= 0)
            {
                _steps[step]();
            }

            return;
        }

        if (_camera is null)
        {
            return;
        }

        _time += delta;
        // There and back, easing at each end.
        Place(0.5f - 0.5f * Mathf.Cos((float)(_time / _def.Period_s * Mathf.Tau)));
    }

    private void Place(float t)
    {
        CameraPointDef a = _shot.From, b = _shot.To;
        Vector3 position = Validator.ToVector3(a.Position_m).ToGodot().Lerp(Validator.ToVector3(b.Position_m).ToGodot(), t);
        float yaw = Mathf.DegToRad(Mathf.Lerp(a.Yaw_deg, b.Yaw_deg, t));
        float pitch = Mathf.DegToRad(Mathf.Lerp(a.Pitch_deg, b.Pitch_deg, t));
        _camera!.GlobalTransform = new Transform3D(Basis.FromEuler(new Vector3(pitch, yaw, 0f)), position);
    }
}
