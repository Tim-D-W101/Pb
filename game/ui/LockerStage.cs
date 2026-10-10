using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Game.Player;
using Pb.Game.World;
using Pb.Sim;
using Pb.Sim.Collision;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Pb.Sim.Players;

namespace Pb.Game.Ui;

/// <summary>
/// The gear locker's room, in a view of its own (<see cref="LockerView"/>): the works' changing room from its level
/// file (levels/locker_room.jsonc), dressed as the areas' rooms are and lit by its windows and a lamp hanging over the
/// turntable in its middle, where you stand in your kit. A sim of its own holds the room's collision, for your feet on
/// the turntable and for the camera to keep out of the walls; it never steps. It's built in two pieces a frame apart
/// (<see cref="Build"/>), so the locker's panels show at once.
/// </summary>
public partial class LockerStage : Node3D
{
    private SimWorld _sim = null!;
    private PlayerState _state = null!;
    private PresentationDef _view = null!;
    private Color _paint;
    private CharacterVisual? _visual;

    /// <summary>The top of the turntable, under your feet.</summary>
    public Vector3 Centre { get; private set; }

    /// <summary>Which way you face (yaw: 0 faces −Z, positive turns left).</summary>
    public float Facing { get; private set; }

    /// <summary>You, as drawn (null until <see cref="Dress"/>).</summary>
    public CharacterVisual? Visual => _visual;

    public LockerCamera Camera { get; private set; } = null!;

    /// <summary>
    /// The room's pieces, to build one a frame (the room and its dressing, then the light, the turntable, the lamp and
    /// the camera); <paramref name="paint"/> is your paint colour (the armbands, a see-through loader's balls).
    /// </summary>
    public System.Action[] Build(GameData data, PresentationDef view, GraphicsPresetDef preset, Viewport viewport, Color paint)
    {
        _view = view;
        _paint = paint;
        LockerDef def = view.Locker;
        LevelLayout level = data.LockerRoom;
        var world = new LevelBuilder { Name = "Room" };
        return new System.Action[]
        {
            () =>
            {
                _sim = new SimWorld(data.Config);
                _sim.LoadLevel(level);
                AddChild(world);
                world.Build(level, new MaterialLibrary(level.Materials), preset.AmbientProbes, view.Horizon, view.Woods);
                var doors = new DoorViews { Name = "Doors" };
                AddChild(doors);
                doors.Build(_sim, world.Materials);
                if (preset.GroundDetail)
                {
                    var debris = new FloorDebris { Name = "FloorDebris" };
                    AddChild(debris);
                    debris.Build(level, _sim.Collision, view.FloorDebris);
                }

                var markings = new Markings { Name = "Markings" };
                AddChild(markings);
                markings.Build(level, view.Markings);
                var cobwebs = new Cobwebs { Name = "Cobwebs" };
                AddChild(cobwebs);
                cobwebs.Build(level, view.Cobwebs);
                var hangings = new WallHangings { Name = "WallHangings" };
                AddChild(hangings);
                hangings.Build(level, view.WallHangings);
                var damp = new Damp { Name = "Damp" };
                AddChild(damp);
                damp.Build(level, _sim.Collision, view.Damp);
                if (!preset.Ssao)
                {
                    var contact = new ContactShadows { Name = "ContactShadows" };
                    AddChild(contact);
                    contact.Build(level, view.ContactShadows);
                }

                var shafts = new LightShafts { Name = "LightShafts" };
                AddChild(shafts);
                shafts.Build(level, _sim.Collision, view.Lighting, view.Shafts, view.Dust, view.WindowLight);
            },
            () =>
            {
                var environment = new WorldEnvironment { Name = "WorldEnvironment", Environment = new Godot.Environment() };
                var sun = new DirectionalLight3D { Name = "Sun" };
                AddChild(environment);
                AddChild(sun);
                Atmosphere.ApplyLighting(environment, sun, view.Lighting);
                Atmosphere.ApplyPreset(environment, sun, viewport, preset);
                environment.Environment.TonemapExposure *= def.Exposure;

                // The turntable on the floor where the room's one spawn is, its top under your feet, which face the spawn's way.
                SpawnPoint spawn = level.PlayerSpawns[0];
                Vector3 floor = Floor(spawn.Position.ToGodot());
                Centre = floor + Vector3.Up * def.TurntableHeight_m;
                Facing = spawn.Yaw;
                float half = def.TurntableHeight_m * 0.5f;
                _sim.Collision.Add(new CylinderShape((floor + Vector3.Up * half).ToSim(), System.Numerics.Vector3.UnitY, half, def.TurntableRadius_m),
                    data.Config.Surfaces.Get("rubber"), "turntable");
                AddChild(Turntable(def, floor, world.Materials));
                AddLamp(def, floor, world.Materials, preset.Shadows);

                _state = _sim.AddPlayer(0, 0, Centre.ToSim(), Facing);
                _state.Pitch = Mathf.DegToRad(def.HoldPitch_deg);
                Camera = new LockerCamera { Name = "Camera", Current = true };
                AddChild(Camera);
                Camera.Initialize(def, Centre, Facing, Clear);
                // A soft light from beside the camera, on you.
                var fill = new SpotLight3D
                {
                    Name = "Fill", Position = new Vector3(0.45f, 0.35f, 0f), LightEnergy = def.FillEnergy, LightColor = Color.FromHtml(def.FillColor),
                    SpotAngle = 24f, SpotRange = def.MaxDistance_m + 2f, ShadowEnabled = false, LightSpecular = 0.3f,
                };
                Camera.AddChild(fill);
            },
        };
    }

    /// <summary>You in <paramref name="kit"/>, built afresh (another item or character).</summary>
    public void Dress(Kit kit)
    {
        if (_state is null)
        {
            return;
        }

        if (_visual is not null)
        {
            RemoveChild(_visual);
            _visual.QueueFree();
        }

        _visual = new CharacterVisual { Name = "You" };
        AddChild(_visual);
        _visual.Build(_sim, _state, _paint, kit, _view.Characters, 0, _view.MarkerModel);
        if (_visual.Model is { } model)
        {
            // The marker held low, the head up and still but for turning to the camera: you look at it over the marker.
            model.HoldIdle();
            model.Poser.HeadPitch = 0f;
            model.Poser.ChestPitch = 0.12f;
        }
    }

    public override void _Process(double delta)
    {
        if (_visual is null || Camera is null)
        {
            return;
        }

        // The head turns to the camera, as far as a neck goes, easing round.
        Vector3 to = Camera.GlobalPosition - Centre;
        float bearing = Mathf.Atan2(-to.X, -to.Z);
        float wanted = Mathf.Clamp(Mathf.Wrap(bearing - Facing, -Mathf.Pi, Mathf.Pi), -Mathf.DegToRad(_view.Locker.HeadTurn_deg), Mathf.DegToRad(_view.Locker.HeadTurn_deg));
        _state.HeadYaw = Mathf.Lerp(_state.HeadYaw, wanted, 1f - Mathf.Exp(-(float)delta * 4f));
        _visual.Capture();
    }

    /// <summary>
    /// The point <paramref name="framing"/> looks at: over the middle of the part of you it names (your head as drawn, for
    /// the head), or over the turntable.
    /// </summary>
    public Vector3 Target(LockerFramingDef framing)
    {
        if (framing.Part.Length > 0 && Enum.TryParse(framing.Part, ignoreCase: true, out HitboxPart part))
        {
            if (part is HitboxPart.Head or HitboxPart.Mask && _visual?.Model is { } model)
            {
                return model.Attachment("Head").GlobalPosition + Vector3.Up * framing.Height_m;
            }

            Span<PosedBox> boxes = stackalloc PosedBox[HitboxRig.PartCount];
            _sim.PlayerHits.PoseNow(_state, boxes);
            foreach (PosedBox box in boxes)
            {
                if (box.Part == part)
                {
                    return box.Center.ToGodot() + Vector3.Up * framing.Height_m;
                }
            }
        }

        return Centre + Vector3.Up * framing.Height_m;
    }

    /// <summary>What you wear in <paramref name="kit"/>'s colours, the items as dressed.</summary>
    public void Recolour(Kit kit) => _visual?.Recolour(kit);

    /// <summary>How far from <paramref name="from"/> towards <paramref name="to"/> the camera can go before the room's in the way (m).</summary>
    private float Clear(Vector3 from, Vector3 to)
    {
        const float radius = 0.12f;
        float length = from.DistanceTo(to);
        return _sim.Collision.SweepSphere(from.ToSim(), to.ToSim(), radius, out SweepHit hit) ? length * hit.T : length;
    }

    /// <summary>The floor under <paramref name="at"/>.</summary>
    private Vector3 Floor(Vector3 at) =>
        _sim.Collision.SweepSphere((at + Vector3.Up * 1.5f).ToSim(), (at - Vector3.Up * 1.5f).ToSim(), 0f, out SweepHit hit) ? hit.Point.ToGodot() : at;

    /// <summary>A kit material by id (the first that's there, else the first of all).</summary>
    private static int Material(MaterialLibrary materials, params string[] ids)
    {
        foreach (string id in ids)
        {
            if (materials.Find(id) is >= 0 and var index)
            {
                return index;
            }
        }

        return 0;
    }

    /// <summary>A steel drum with a rounded lip and a rubber top let into it.</summary>
    private static MeshInstance3D Turntable(LockerDef def, Vector3 floor, MaterialLibrary materials)
    {
        float r = def.TurntableRadius_m, h = def.TurntableHeight_m;
        int drum = Material(materials, def.TurntableMaterial, "steel_painted");
        int top = Material(materials, def.TurntableTopMaterial, "rubber_plain", def.TurntableMaterial);
        var shape = new ShapeMesh();
        float lip = Mathf.Min(0.012f, h * 0.3f);
        shape.Lathe(drum, floor, Basis.Identity, new List<Vector2>
        {
            new(r - lip, 0f), new(r, lip * 0.6f), new(r, h - lip), new(r - lip * 0.7f, h - lip * 0.15f), new(r - 0.03f, h - lip * 0.15f),
        }, 64);
        shape.Lathe(top, floor, Basis.Identity, new List<Vector2> { new(r - 0.03f, h - lip * 0.15f), new(r - 0.034f, h), new(0f, h) }, 64);
        var mesh = new ArrayMesh();
        shape.Commit(mesh, m => materials[m]);
        return new MeshInstance3D { Name = "Turntable", Mesh = mesh };
    }

    /// <summary>
    /// The lamp: an enamel shade on its flex from a rose in the ceiling, a bulb in it, and its light down on the turntable.
    /// </summary>
    private void AddLamp(LockerDef def, Vector3 floor, MaterialLibrary materials, bool shadows)
    {
        Vector3 mouth = floor + Vector3.Up * def.LampHeight_m;
        float ceiling = _sim.Collision.SweepSphere(mouth.ToSim(), (mouth + Vector3.Up * 4f).ToSim(), 0f, out SweepHit up) ? up.Point.Y : mouth.Y + 0.4f;
        int enamel = Material(materials, def.LampMaterial, "enamel_white");
        int flex = Material(materials, "plastic_black", def.LampMaterial);
        var shape = new ShapeMesh();
        // Outside walked bottom to top, inside back down a little within it, so both face out of the enamel.
        var outside = new List<Vector2> { new(0.2f, 0f), new(0.188f, 0.025f), new(0.125f, 0.1f), new(0.055f, 0.15f), new(0.034f, 0.175f), new(0.034f, 0.2f) };
        var inside = new List<Vector2>();
        for (int i = outside.Count - 2; i >= 0; i--)
        {
            inside.Add(new Vector2(outside[i].X - 0.004f, outside[i].Y + (i == 0 ? 0f : 0.002f)));
        }

        shape.Lathe(enamel, mouth, Basis.Identity, outside, 36);
        shape.Lathe(enamel, mouth, Basis.Identity, inside, 36);
        Vector3 top = mouth + Vector3.Up * 0.2f;
        if (ceiling > top.Y + 0.05f)
        {
            shape.Rod(flex, top, new Vector3(mouth.X, ceiling - 0.02f, mouth.Z), 0.004f);
            shape.Cylinder(enamel, new Vector3(mouth.X, ceiling - 0.012f, mouth.Z), Basis.Identity, 0.045f, 0.024f, 20);
        }

        var mesh = new ArrayMesh();
        shape.Commit(mesh, m => materials[m]);
        AddChild(new MeshInstance3D { Name = "Lamp", Mesh = mesh });

        Color colour = Color.FromHtml(def.LampColor);
        var bulb = new MeshInstance3D
        {
            Name = "Bulb", Mesh = new SphereMesh { Radius = 0.032f, Height = 0.064f, RadialSegments = 16, Rings = 8 },
            Position = mouth + Vector3.Up * 0.075f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = colour, EmissionEnabled = true, Emission = colour,
                EmissionEnergyMultiplier = 3f,
            },
        };
        AddChild(bulb);
        var light = new SpotLight3D
        {
            Name = "LampLight", Position = mouth + Vector3.Up * 0.06f, RotationDegrees = new Vector3(-90f, 0f, 0f), LightEnergy = def.LampEnergy,
            LightColor = colour, SpotAngle = def.LampAngle_deg, SpotRange = def.LampHeight_m + 1.5f, SpotAngleAttenuation = 0.8f,
            ShadowEnabled = shadows, ShadowBlur = 1.5f,
        };
        AddChild(light);
    }
}
