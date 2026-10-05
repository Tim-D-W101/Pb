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
/// and a beam of light over each way out. Hold: the room's outline on its floor. The sim decides everything: who carries
/// the case, where it lies, who holds the room.
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

    public void Build(SimWorld sim, ObjectivesViewDef view)
    {
        _sim = sim;
        _view = view;
        _objective = sim.Match?.Objective;
        if (_objective is null)
        {
            return;
        }

        var colour = Color.FromHtml(view.Color);
        if (_objective.Kind == ObjectiveKind.Retrieve)
        {
            _case = BuildCase(view);
            AddChild(_case);
            _light = new OmniLight3D
            {
                Name = "CaseLight", LightColor = Color.FromHtml(view.CaseLightColor), OmniRange = view.CaseLightRange_m, LightEnergy = 0.8f,
                ShadowEnabled = false, Position = new Vector3(0f, 0.35f, 0f),
            };
            AddChild(_light);
            foreach (ExitSpec exit in _objective.Level.Exits)
            {
                AddChild(Beam(exit, colour, view));
            }
        }
        else if (_objective.Room is { } room)
        {
            AddChild(Outline(room, colour, view));
        }

        Capture();
        Capture();
    }

    /// <summary>After each sim step: where the case will be drawn towards.</summary>
    public void Capture()
    {
        if (_objective is null || _case is null)
        {
            return;
        }

        _previous = _current;
        _current = CasePose(_objective);
    }

    public override void _Process(double delta)
    {
        if (_objective is null || _case is null)
        {
            return;
        }

        float alpha = (float)Engine.GetPhysicsInterpolationFraction();
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
