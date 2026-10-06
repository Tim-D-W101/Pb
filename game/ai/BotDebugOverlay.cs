using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.AI;
using Pb.Sim.Core;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.Ai;

/// <summary>
/// F3 in a level: what the bots are thinking, drawn over the world (through walls). For each bot, its
/// sight cone coloured by its detection meter (green → amber when suspicious → red once it has spotted
/// you), its path, the cover point it holds, and a label with its mode and meter. Every cover point in
/// the level shows as a dot: blue for full height, amber for half.
/// </summary>
public partial class BotDebugOverlay : Node3D
{
    /// <summary>How far the sight cone is drawn (the real sight range is longer; this keeps the view readable).</summary>
    private const float ConeLength = 7f;

    private static readonly Color Calm = new(0.1f, 0.95f, 0.3f);
    private static readonly Color Wary = new(1f, 0.7f, 0f);
    private static readonly Color Alarmed = new(1f, 0.1f, 0.1f);
    private static readonly Color PathColour = new(0.2f, 0.6f, 1f);
    private static readonly Color CoverColour = new(1f, 1f, 1f);

    private readonly List<Label3D> _labels = new();
    private BotSquad _squad = null!;
    private ImmediateMesh _lines = null!;
    private StandardMaterial3D _material = null!;

    /// <summary>A player whose label isn't drawn (a scripted camera looking over their shoulder), or -1.</summary>
    public int Unlabelled { get; set; } = -1;

    public void Initialize(BotSquad squad)
    {
        _squad = squad;
        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            NoDepthTest = true,
            RenderPriority = 10,
        };
        _lines = new ImmediateMesh();
        AddChild(new MeshInstance3D { Name = "Lines", Mesh = _lines, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        AddChild(CoverDots(squad.Cover));
        foreach (BotBrain _ in squad.Bots)
        {
            var label = new Label3D
            {
                FontSize = 64, PixelSize = 0.008f, OutlineSize = 16, NoDepthTest = true, FixedSize = false,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = Colors.White,
            };
            AddChild(label);
            _labels.Add(label);
        }

        Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!Visible || _squad is null)
        {
            return;
        }

        _lines.ClearSurfaces();
        _lines.SurfaceBegin(Mesh.PrimitiveType.Lines, _material);
        bool any = false;
        for (int i = 0; i < _squad.Bots.Count; i++)
        {
            BotBrain bot = _squad.Bots[i];
            Label3D label = _labels[i];
            label.Visible = bot.Self.Present && bot.Self.Id != Unlabelled;
            if (!bot.Self.Present)
            {
                continue;
            }

            any = true;
            float meter = bot.Senses.HighestMeter;
            bool spotted = bot.Senses.Focus is { Spotted: true };
            Color colour = spotted ? Alarmed : meter >= _squad.Config.Senses.SuspiciousAt ? Wary : Calm;
            if (bot.Self.Alive)
            {
                Cone(bot, colour);
            }

            Path(bot);
            if (bot.CoverIndex >= 0)
            {
                Vector3 at = _squad.Cover.Points[bot.CoverIndex].Position.ToGodot();
                Line(bot.Self.Position.ToGodot() + Vector3.Up * 0.1f, at + Vector3.Up * 0.1f, CoverColour);
                Line(at, at + Vector3.Up * 2f, CoverColour);
            }

            label.GlobalPosition = (bot.Self.Position + new SVector3(0f, bot.Self.EyeHeight + 0.8f, 0f)).ToGodot();
            label.Text = bot.Self.Alive ? $"{bot.Label}\n{meter * 100f:0}%" : "OUT";
            label.Modulate = colour;
        }

        if (!any)
        {
            Line(Vector3.Zero, Vector3.Zero, Calm); // a surface needs at least one vertex
        }

        _lines.SurfaceEnd();
    }

    private void Cone(BotBrain bot, Color colour)
    {
        SVector3 eye = bot.Self.EyePosition;
        float half = _squad.Config.Senses.HalfFieldOfView;
        float reach = MathF.Min(ConeLength, bot.Tier.SightRange);
        Vector3 origin = eye.ToGodot();
        Vector3 previous = Vector3.Zero;
        const int segments = 12;
        for (int k = 0; k <= segments; k++)
        {
            // Where it sees is where its head looks.
            float yaw = bot.Self.Yaw + bot.Self.HeadYaw - half + 2f * half * k / segments;
            Vector3 edge = origin + (ViewAngles.FlatForward(yaw) * reach).ToGodot();
            if (k == 0 || k == segments)
            {
                Line(origin, edge, colour);
            }

            if (k > 0)
            {
                Line(previous, edge, colour);
            }

            previous = edge;
        }
    }

    private void Path(BotBrain bot)
    {
        IReadOnlyList<SVector3> path = bot.Path;
        Vector3 from = bot.Self.Position.ToGodot() + Vector3.Up * 0.15f;
        for (int i = bot.NextWaypoint; i < path.Count; i++)
        {
            Vector3 to = path[i].ToGodot() + Vector3.Up * 0.15f;
            Line(from, to, PathColour);
            from = to;
        }
    }

    private void Line(Vector3 a, Vector3 b, Color colour)
    {
        _lines.SurfaceSetColor(colour);
        _lines.SurfaceAddVertex(a);
        _lines.SurfaceSetColor(colour);
        _lines.SurfaceAddVertex(b);
    }

    private static MultiMeshInstance3D CoverDots(CoverSet cover)
    {
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new BoxMesh
            {
                Size = new Vector3(0.14f, 0.14f, 0.14f),
                // Depth-tested, unlike the lines: dots inside buildings would otherwise litter their walls.
                Material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true },
            },
            InstanceCount = cover.Points.Count,
        };
        for (int i = 0; i < cover.Points.Count; i++)
        {
            CoverPoint p = cover.Points[i];
            multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, p.Position.ToGodot() + Vector3.Up * 0.1f));
            multimesh.SetInstanceColor(i, p.Height == CoverHeight.Full ? new Color(0.3f, 0.6f, 1f) : new Color(1f, 0.75f, 0.2f));
        }

        return new MultiMeshInstance3D { Name = "CoverDots", Multimesh = multimesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }
}
