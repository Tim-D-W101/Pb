using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim;
using Pb.Sim.Data;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;

namespace Pb.Game.World;

/// <summary>
/// The objective in the world, looks only (presentation.jsonc "objectives"). Retrieve: the case, a hard case built in
/// code, lying where the sim says (with a small blinking light) or hanging at its carrier's side, smoothly between ticks;
/// and a beam of light over each way out. Hold: the room's outline on its floor. Capture the flag: each side's flag on its
/// pole in its colour, standing at its base (a ring on the ground in its colour, there whether the flag is or not),
/// strapped to its carrier's back, or fallen where they were hit. VIP escort: a marker over the VIP's head, and the
/// beams over the ways out that count. The sim decides everything: who carries what, where it lies, who holds the room.
/// </summary>
public partial class ObjectiveViews : Node3D
{
    private SimWorld _sim = null!;
    private ObjectiveState? _objective;
    private ObjectivesViewDef _view = null!;
    private Node3D? _case;
    private OmniLight3D? _light;
    private Transform3D _previous;
    private Transform3D _current;
    private float _blink;
    private Node3D[] _flags = Array.Empty<Node3D>();
    private Transform3D[] _flagPrevious = Array.Empty<Transform3D>();
    private Transform3D[] _flagCurrent = Array.Empty<Transform3D>();
    private Node3D? _vipMarker;

    public void Build(SimWorld sim, ObjectivesViewDef view, string[] teamColors)
    {
        _sim = sim;
        _view = view;
        _objective = sim.Match?.Objective;
        if (_objective is null)
        {
            return;
        }

        var colour = Color.FromHtml(view.Color);
        if (_objective.Kind == ObjectiveKind.Flag)
        {
            int n = _objective.Flags.Count;
            _flags = new Node3D[n];
            _flagPrevious = new Transform3D[n];
            _flagCurrent = new Transform3D[n];
            for (int i = 0; i < n; i++)
            {
                FlagState flag = _objective.Flags[i];
                var team = Color.FromHtml(teamColors[flag.Team % teamColors.Length]);
                _flags[i] = BuildFlag(view, team);
                AddChild(_flags[i]);
                AddChild(BaseRing(flag, team, view));
            }
        }
        else if (_objective.Kind == ObjectiveKind.Escort)
        {
            _vipMarker = BuildVipMarker(colour, view);
            AddChild(_vipMarker);
            foreach (int i in _objective.CountingExits)
            {
                AddChild(Beam(_objective.Level.Exits[i], colour, view));
            }
        }
        else if (_objective.Kind == ObjectiveKind.Retrieve)
        {
            _case = BuildCase(view);
            AddChild(_case);
            _light = new OmniLight3D
            {
                Name = "CaseLight", LightColor = Color.FromHtml(view.CaseLightColor), OmniRange = view.CaseLightRange_m, LightEnergy = 0.8f,
                ShadowEnabled = false, Position = new Vector3(0f, 0.35f, 0f),
            };
            AddChild(_light);
            foreach (int i in _objective.CountingExits)
            {
                AddChild(Beam(_objective.Level.Exits[i], colour, view));
            }
        }
        else if (_objective.Room is { } room)
        {
            AddChild(Outline(room, colour, view));
        }

        Capture();
        Capture();
    }

    /// <summary>After each sim step: where the case (the flags) will be drawn towards.</summary>
    public void Capture()
    {
        if (_objective is null)
        {
            return;
        }

        for (int i = 0; i < _flags.Length; i++)
        {
            _flagPrevious[i] = _flagCurrent[i];
            _flagCurrent[i] = FlagPose(_objective.Flags[i]);
        }

        if (_case is null)
        {
            return;
        }

        _previous = _current;
        _current = CasePose(_objective);
    }

    public override void _Process(double delta)
    {
        if (_objective is null)
        {
            return;
        }

        float alpha = (float)Engine.GetPhysicsInterpolationFraction();
        for (int i = 0; i < _flags.Length; i++)
        {
            _flags[i].GlobalTransform = _flagPrevious[i].InterpolateWith(_flagCurrent[i], alpha);
        }

        if (_vipMarker is not null)
        {
            // Over the VIP's head, turning slowly; gone once they're out (either way).
            _blink += (float)delta;
            bool shown = _objective.Vip >= 0 && !_objective.Done && !_objective.VipOut && _sim.FindPlayer(_objective.Vip) is { Alive: true };
            _vipMarker.Visible = shown;
            if (shown && _sim.FindPlayer(_objective.Vip) is { } vip)
            {
                Vector3 head = vip.EyePosition.ToGodot();
                _vipMarker.GlobalTransform = new Transform3D(new Basis(Vector3.Up, _blink * 1.5f),
                    head + new Vector3(0f, _view.VipMarkerAbove_m + 0.05f * Mathf.Sin(_blink * 3f), 0f));
            }
        }

        if (_case is null)
        {
            return;
        }

        Transform3D pose = _previous.InterpolateWith(_current, alpha);
        _case.GlobalTransform = pose;
        _case.Visible = !_objective.Done;

        // The light blinks while it lies about, and goes out once someone has it.
        bool lying = _objective.Carrier < 0 && !_objective.Done;
        _blink += (float)delta;
        if (_light is not null)
        {
            _light.Visible = lying;
            _light.GlobalPosition = pose.Origin + new Vector3(0f, 0.35f, 0f);
            _light.LightEnergy = 0.25f + 0.75f * (0.5f + 0.5f * Mathf.Cos(_blink * Mathf.Tau / _view.CaseLightBlink_s));
        }
    }

    /// <summary>
    /// Standing at its base or wherever it was sent home to; strapped to the carrier's back, leaning back a little; or
    /// fallen where its carrier was hit, lying over on its side.
    /// </summary>
    private Transform3D FlagPose(FlagState flag)
    {
        if (flag.Carrier >= 0 && _sim.FindPlayer(flag.Carrier) is { } carrier)
        {
            float yaw = carrier.Yaw;
            var back = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            Vector3 at = carrier.Position.ToGodot() + back * 0.2f + Vector3.Up * 0.75f;
            return new Transform3D(new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, -0.25f), at);
        }

        Vector3 ground = flag.Position.ToGodot();
        return flag.AtHome
            ? new Transform3D(Basis.Identity, ground)
            : new Transform3D(new Basis(Vector3.Up, 0.7f) * new Basis(Vector3.Forward, 1.35f), ground + new Vector3(0f, 0.05f, 0f));
    }

    /// <summary>A flag on its pole, the pole's foot at the origin: a cloth in its side's colour near the top.</summary>
    private static Node3D BuildFlag(ObjectivesViewDef view, Color team)
    {
        var root = new Node3D { Name = "Flag" };
        float height = view.FlagPoleHeight_m;
        var pole = new StandardMaterial3D { AlbedoColor = new Color(0.82f, 0.8f, 0.74f), Roughness = 0.4f, Metallic = 0.5f };
        var cloth = new StandardMaterial3D { AlbedoColor = team, Roughness = 0.8f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.018f, BottomRadius = 0.022f, Height = height, RadialSegments = 8, Material = pole },
            Position = new Vector3(0f, height * 0.5f, 0f),
        });
        float w = view.FlagSize_m[0], h = view.FlagSize_m[1];
        root.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(w, h, 0.01f), Material = cloth },
            Position = new Vector3(w * 0.5f + 0.02f, height - h * 0.5f - 0.05f, 0f),
        });
        root.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.04f, Height = 0.08f, RadialSegments = 8, Rings = 4, Material = pole },
            Position = new Vector3(0f, height, 0f),
        });
        return root;
    }

    /// <summary>A side's base: a ring on the ground round its flag's home, in its colour.</summary>
    private static MeshInstance3D BaseRing(FlagState flag, Color team, ObjectivesViewDef view)
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = team with { A = 0.8f },
        };
        float r = view.FlagBaseRadius_m;
        return new MeshInstance3D
        {
            Name = $"Base {flag.Team}",
            Mesh = new TorusMesh { InnerRadius = r - 0.06f, OuterRadius = r, Rings = 32, RingSegments = 4, Material = material },
            Position = flag.Home.ToGodot() + new Vector3(0f, 0.03f, 0f), Scale = new Vector3(1f, 0.2f, 1f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    /// <summary>The VIP's marker: a small point-down pyramid in the objective's colour, lit by itself.</summary>
    private static MeshInstance3D BuildVipMarker(Color colour, ObjectivesViewDef view)
    {
        var material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = colour };
        return new MeshInstance3D
        {
            Name = "VipMarker",
            Mesh = new CylinderMesh { TopRadius = view.VipMarkerSize_m, BottomRadius = 0f, Height = view.VipMarkerSize_m * 1.4f, RadialSegments = 4, Material = material },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    /// <summary>Lying flat where it is, or upright at the carrier's right side, handle up, about hip high.</summary>
    private Transform3D CasePose(ObjectiveState objective)
    {
        float thickness = _view.CaseSize_m[2];
        if (objective.Carrier >= 0 && _sim.FindPlayer(objective.Carrier) is { } carrier)
        {
            float yaw = carrier.Yaw;
            var right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
            var forward = new Vector3(-Mathf.Sin(yaw), 0f, -Mathf.Cos(yaw));
            Vector3 at = carrier.Position.ToGodot() + right * 0.32f + Vector3.Up * 0.62f + forward * 0.05f;
            var basis = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Up, Mathf.Pi * 0.5f) * new Basis(Vector3.Right, -Mathf.Pi * 0.5f);
            return new Transform3D(basis, at);
        }

        return new Transform3D(new Basis(Vector3.Up, 0.6f), objective.CasePosition.ToGodot() + new Vector3(0f, thickness * 0.5f, 0f));
    }

    /// <summary>
    /// A hard case lying flat, centred: x along its length, y its thickness, z across, with ribs on its lid, a stripe in
    /// the objective's colour, two latches and a carrying handle on its front (+z) edge.
    /// </summary>
    private static Node3D BuildCase(ObjectivesViewDef view)
    {
        float l = view.CaseSize_m[0], d = view.CaseSize_m[1], t = view.CaseSize_m[2];
        var root = new Node3D { Name = "Case" };
        var shell = new StandardMaterial3D { AlbedoColor = Color.FromHtml(view.CaseColor), Roughness = 0.55f };
        var dark = new StandardMaterial3D { AlbedoColor = Color.FromHtml(view.CaseColor).Darkened(0.5f), Roughness = 0.4f, Metallic = 0.3f };
        var stripe = new StandardMaterial3D { AlbedoColor = Color.FromHtml(view.Color), Roughness = 0.5f };

        void Part(Material material, Vector3 centre, Vector3 size) =>
            root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size, Material = material }, Position = centre });

        Part(shell, Vector3.Zero, new Vector3(l, t, d));
        foreach (float z in new[] { -d * 0.25f, d * 0.25f })
        {
            Part(shell, new Vector3(0f, t * 0.5f + 0.006f, z), new Vector3(l * 0.9f, 0.012f, 0.03f));
        }

        Part(stripe, new Vector3(0f, t * 0.5f + 0.004f, 0f), new Vector3(l * 0.9f, 0.008f, 0.04f));
        foreach (float x in new[] { -l * 0.32f, l * 0.32f })
        {
            Part(dark, new Vector3(x, 0f, d * 0.5f + 0.008f), new Vector3(0.05f, 0.05f, 0.016f));
        }

        foreach (float x in new[] { -0.07f, 0.07f })
        {
            Part(dark, new Vector3(x, 0f, d * 0.5f + 0.025f), new Vector3(0.018f, 0.03f, 0.05f));
        }

        Part(dark, new Vector3(0f, 0f, d * 0.5f + 0.05f), new Vector3(0.17f, 0.03f, 0.022f));
        return root;
    }

    /// <summary>A soft column of light over a way out, fading upwards.</summary>
    private static MeshInstance3D Beam(ExitSpec exit, Color colour, ObjectivesViewDef view)
    {
        var gradient = new Gradient();
        gradient.SetColor(0, colour with { A = view.BeamAlpha });
        gradient.SetColor(1, colour with { A = 0f });
        var fade = new GradientTexture2D { Gradient = gradient, FillFrom = new Vector2(0f, 1f), FillTo = new Vector2(0f, 0f), Width = 4, Height = 64 };
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled, AlbedoTexture = fade, AlbedoColor = Colors.White, NoDepthTest = false,
            // Gone when you're standing in it (you may come in at a way out), there from a few metres off.
            DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha, DistanceFadeMinDistance = 1.5f, DistanceFadeMaxDistance = 6f,
        };
        var mesh = new CylinderMesh
        {
            TopRadius = view.BeamRadius_m, BottomRadius = view.BeamRadius_m, Height = view.BeamHeight_m, CapTop = false, CapBottom = false,
            RadialSegments = 16, Material = material,
        };
        return new MeshInstance3D
        {
            Name = $"WayOut {exit.Name}", Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = exit.Position.ToGodot() + new Vector3(0f, view.BeamHeight_m * 0.5f, 0f),
        };
    }

    /// <summary>The room's outline on its floor: a thin strip a little inside each of its parts' edges.</summary>
    private Node3D Outline(HoldRoom room, Color colour, ObjectivesViewDef view)
    {
        var root = new Node3D { Name = $"Room {room.Name}" };
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = colour with { A = 0.75f },
        };
        const float Inset = 0.3f;
        float w = view.RoomOutlineWidth_m;
        foreach (Pb.Sim.Collision.Aabb box in room.Boxes)
        {
            float floor = FloorIn(box);
            float x0 = box.Min.X + Inset, x1 = box.Max.X - Inset, z0 = box.Min.Z + Inset, z1 = box.Max.Z - Inset;
            var strips = new List<(Vector3 Centre, Vector3 Size)>
            {
                (new Vector3((x0 + x1) * 0.5f, floor, z0), new Vector3(x1 - x0, 0.01f, w)),
                (new Vector3((x0 + x1) * 0.5f, floor, z1), new Vector3(x1 - x0, 0.01f, w)),
                (new Vector3(x0, floor, (z0 + z1) * 0.5f), new Vector3(w, 0.01f, z1 - z0)),
                (new Vector3(x1, floor, (z0 + z1) * 0.5f), new Vector3(w, 0.01f, z1 - z0)),
            };
            foreach ((Vector3 centre, Vector3 size) in strips)
            {
                root.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = size, Material = material }, Position = centre,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
            }
        }

        return root;
    }

    /// <summary>The floor's height in a part of the room, a hair above it: the lowest of what's under its corners and middle.</summary>
    private float FloorIn(Pb.Sim.Collision.Aabb box)
    {
        float top = MathF.Min(box.Max.Y, box.Min.Y + 3f);
        float lowest = float.MaxValue;
        foreach ((float fx, float fz) in new[] { (0.5f, 0.5f), (0.1f, 0.1f), (0.9f, 0.1f), (0.1f, 0.9f), (0.9f, 0.9f) })
        {
            var start = new System.Numerics.Vector3(box.Min.X + (box.Max.X - box.Min.X) * fx, top, box.Min.Z + (box.Max.Z - box.Min.Z) * fz);
            System.Numerics.Vector3 end = start with { Y = box.Min.Y - 0.5f };
            if (_sim.Collision.SweepSphere(start, end, 0f, out Pb.Sim.Collision.SweepHit hit, includeDynamic: false))
            {
                lowest = MathF.Min(lowest, start.Y + (end.Y - start.Y) * hit.T);
            }
        }

        return (lowest < float.MaxValue ? lowest : MathF.Max(box.Min.Y, 0f)) + 0.02f;
    }
}
