using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Core;
using Pb.Sim.Collision;
using Pb.Sim.Level;
using SVector3 = System.Numerics.Vector3;

namespace Pb.Game.World;

/// <summary>
/// The edge of a place smaller than its whole area (presentation.jsonc "placeBoundary"). The round's
/// invisible walking walls stand on it, so wherever a walk could cross it (open ground, a doorway, a gap
/// between crates) it's taped off in red and white: on posts at the corners, at loose ends and along long
/// runs, tied to whatever it meets. The edge is surveyed against the walking geometry: a gap is taped on
/// a floor (the ground, a building's floor, stairs) where there's room to walk through, never across a
/// window or over a roof. One mesh for the tape, one MultiMesh for the posts; looks only.
/// </summary>
public partial class PlaceBoundary : Node3D
{
    /// <summary>How far apart two samples' floors may be and still be one run of tape (m).</summary>
    private const float SameFloor = 0.3f;

    private readonly record struct Anchor(Vector2 At, float Floor, bool Post);

    private PlaceBoundaryDef _def = null!;
    private CollisionWorld _walk = null!;
    private readonly List<bool> _floorLike = new();

    public int PostCount { get; private set; }

    public float Length_m { get; private set; }

    public void Build(LevelLayout level, PlaceBoundaryDef def, Vector2 wind)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        PostCount = 0;
        Length_m = 0f;
        if (level.Place is not { Bounds: { } bounds })
        {
            return;
        }

        _def = def;
        BuildWalkWorld(level);
        var corners = new[]
        {
            new Vector2(bounds.Min.X, bounds.Min.Z), new Vector2(bounds.Max.X, bounds.Min.Z),
            new Vector2(bounds.Max.X, bounds.Max.Z), new Vector2(bounds.Min.X, bounds.Max.Z),
        };

        // Round the edge, corner to corner: each sample's floors with room to walk through.
        var at = new List<Vector2>();
        var corner = new List<bool>();
        for (int e = 0; e < 4; e++)
        {
            Vector2 a = corners[e], b = corners[(e + 1) % 4];
            int n = Math.Max(1, (int)MathF.Ceiling(a.DistanceTo(b) / def.Sample_m));
            for (int k = 0; k < n; k++)
            {
                at.Add(a.Lerp(b, (float)k / n));
                corner.Add(k == 0);
            }
        }

        int count = at.Count;
        float top = level.Bounds.Max.Y;
        var floors = new List<float>();
        var open = new List<float>[count];
        for (int i = 0; i < count; i++)
        {
            FloorsAt(at[i], top, floors);
            open[i] = floors.FindAll(h => Clear(at[i], h));
        }

        var anchors = new List<List<Anchor>>();
        foreach ((List<(int I, float H)> run, bool loop) in Runs(open))
        {
            List<Anchor>? strung = String(run, loop, at, corner);
            if (strung is not null)
            {
                anchors.Add(strung);
            }
        }

        Draw(anchors, wind);
    }

    /// <summary>What a walk bumps into, but not the place's own walls: the ground and every walking primitive, and the stairs.</summary>
    private void BuildWalkWorld(LevelLayout level)
    {
        _walk = new CollisionWorld();
        _floorLike.Clear();
        _walk.Add(new PlaneShape(SVector3.UnitY, 0f), level.GroundMaterial.Surface, "ground");
        _floorLike.Add(true);
        foreach (LevelPrimitive p in level.Primitives)
        {
            if (p.Role == PrimitiveRole.Boundary || !(p.Has(PrimitiveFlags.Walk) || p.Role == PrimitiveRole.Stair))
            {
                continue;
            }

            _walk.Add(p.CreateShape(), p.Surface, "");
            _floorLike.Add(p.Role is PrimitiveRole.Floor or PrimitiveRole.Stair or PrimitiveRole.Ramp or PrimitiveRole.GroundPatch);
        }

        _walk.Build();
    }

    /// <summary>The floors at <paramref name="plan"/>, top down: each surface a drop lands on that's ground, a floor or stairs.</summary>
    private void FloorsAt(Vector2 plan, float top, List<float> floors)
    {
        floors.Clear();
        float y = top;
        for (int guard = 0; guard < 400 && y > -0.5f && floors.Count < 4; guard++)
        {
            if (!_walk.SweepSphere(new SVector3(plan.X, y, plan.Y), new SVector3(plan.X, -1f, plan.Y), 0.02f, out SweepHit hit))
            {
                return;
            }

            if (hit.T <= 0f)
            {
                y -= 0.1f; // started inside something: down through it
                continue;
            }

            float h = hit.Point.Y - 0.02f;
            if (hit.Normal.Y > 0.6f && _floorLike[hit.ColliderId])
            {
                floors.Add(h);
            }

            if (hit.ColliderId == 0)
            {
                return; // the ground: nothing under it
            }

            y = h - 0.05f;
        }
    }

    /// <summary>The floor at <paramref name="plan"/> nearest the height <paramref name="near"/> (that height if there's none).</summary>
    private float FloorNear(Vector2 plan, float near)
    {
        var floors = new List<float>();
        FloorsAt(plan, near + 2f, floors);
        float best = float.NaN;
        foreach (float h in floors)
        {
            if (float.IsNaN(best) || MathF.Abs(h - near) < MathF.Abs(best - near))
            {
                best = h;
            }
        }

        return !float.IsNaN(best) && MathF.Abs(best - near) < SameFloor ? best : near;
    }

    /// <summary>Whether there's room to walk through <paramref name="plan"/> on the floor at <paramref name="floor"/>.</summary>
    private bool Clear(Vector2 plan, float floor)
    {
        foreach (float h in _def.ClearHeights_m)
        {
            var p = new SVector3(plan.X, floor + h, plan.Y);
            if (_walk.SweepSphere(p, p, _def.ClearRadius_m, out _))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether the tape at <paramref name="plan"/> on <paramref name="floor"/> meets something it can be tied to.</summary>
    private bool Solid(Vector2 plan, float floor)
    {
        foreach (float h in _def.Strands_m)
        {
            var p = new SVector3(plan.X, floor + h, plan.Y);
            if (!_walk.SweepSphere(p, p, _def.ClearRadius_m + 0.03f, out _))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The open stretches round the edge, sample by sample with their floors: a stretch follows a floor
    /// from one sample to the next while it stays within <see cref="SameFloor"/>. Loop: open all the way round.
    /// </summary>
    private static List<(List<(int I, float H)> Run, bool Loop)> Runs(List<float>[] open)
    {
        int count = open.Length;
        var runs = new List<List<(int I, float H)>>();
        var going = new List<List<(int I, float H)>>();
        for (int i = 0; i < count; i++)
        {
            var next = new List<List<(int I, float H)>>();
            foreach (float h in open[i])
            {
                List<(int I, float H)>? run = going.Find(r => MathF.Abs(r[^1].H - h) < SameFloor && !next.Contains(r));
                if (run is null)
                {
                    run = new List<(int I, float H)>();
                    runs.Add(run);
                }

                run.Add((i, h));
                next.Add(run);
            }

            going = next;
        }

        // Round the last corner: a stretch still open at the end carries on into one open from the start.
        var loops = new HashSet<List<(int I, float H)>>();
        foreach (List<(int I, float H)> tail in going)
        {
            if (tail.Count == count)
            {
                loops.Add(tail);
                continue;
            }

            List<(int I, float H)>? head = runs.Find(r => r != tail && r[0].I == 0 && MathF.Abs(r[0].H - tail[^1].H) < SameFloor);
            if (head is not null)
            {
                tail.AddRange(head);
                runs.Remove(head);
            }
        }

        return runs.ConvertAll(r => (r, loops.Contains(r)));
    }

    /// <summary>
    /// Where one open stretch's tape is held: its ends (tied to what closes it, or on a post if nothing's
    /// there at the tape's height), posts at the corners it turns, and more along it at most the spacing
    /// apart. Null for a gap too narrow to bother with.
    /// </summary>
    private List<Anchor>? String(List<(int I, float H)> run, bool loop, List<Vector2> at, List<bool> corner)
    {
        int count = at.Count;
        var held = new List<Anchor>();
        if (!loop)
        {
            held.Add(End(at[(run[0].I - 1 + count) % count], at[run[0].I], run[0].H));
        }

        foreach ((int i, float h) in run)
        {
            if (corner[i])
            {
                held.Add(new Anchor(at[i], h, true));
            }
        }

        if (loop)
        {
            held.Add(held[0]);
        }
        else
        {
            held.Add(End(at[(run[^1].I + 1) % count], at[run[^1].I], run[^1].H));
        }

        float length = 0f;
        for (int k = 1; k < held.Count; k++)
        {
            length += held[k - 1].At.DistanceTo(held[k].At);
        }

        if (held.Count < 2 || length < _def.MinGap_m)
        {
            return null;
        }

        // Posts along the long spans, evenly.
        var strung = new List<Anchor> { held[0] };
        for (int k = 1; k < held.Count; k++)
        {
            Anchor a = held[k - 1], b = held[k];
            int spans = Math.Max(1, (int)MathF.Ceiling(a.At.DistanceTo(b.At) / _def.PostSpacing_m));
            for (int s = 1; s < spans; s++)
            {
                float f = (float)s / spans;
                Vector2 post = a.At.Lerp(b.At, f);
                strung.Add(new Anchor(post, FloorNear(post, Mathf.Lerp(a.Floor, b.Floor, f)), true));
            }

            strung.Add(b);
        }

        return strung;
    }

    /// <summary>
    /// One end of a stretch, between its last open sample and the closed one past it: found to a few
    /// millimetres, then tied to what closes it, or on a post if there's nothing at the tape's height.
    /// </summary>
    private Anchor End(Vector2 closed, Vector2 open, float floor)
    {
        Vector2 inside = open, outside = closed;
        for (int k = 0; k < 7; k++)
        {
            Vector2 mid = (inside + outside) * 0.5f;
            if (Clear(mid, floor))
            {
                inside = mid;
            }
            else
            {
                outside = mid;
            }
        }

        Vector2 toward = (closed - open).Normalized();
        Vector2 tie = inside + toward * (_def.ClearRadius_m - 0.01f);
        return Solid(inside, floor) ? new Anchor(tie, floor, false) : new Anchor(inside, floor, true);
    }

    private void Draw(List<List<Anchor>> runs, Vector2 wind)
    {
        var tape = new SurfaceTool();
        tape.Begin(Mesh.PrimitiveType.Triangles);
        var posts = new List<Transform3D>();
        float half = _def.TapeWidth_m * 0.5f;
        foreach (List<Anchor> run in runs)
        {
            foreach (Anchor a in run)
            {
                if (a.Post && !posts.Exists(t => new Vector2(t.Origin.X, t.Origin.Z).DistanceTo(a.At) < 0.05f))
                {
                    posts.Add(new Transform3D(Basis.FromScale(new Vector3(_def.PostWidth_m, _def.PostHeight_m, _def.PostWidth_m)),
                        new Vector3(a.At.X, a.Floor + _def.PostHeight_m * 0.5f, a.At.Y)));
                }
            }

            foreach (float strand in _def.Strands_m)
            {
                float along = 0f;
                for (int k = 1; k < run.Count; k++)
                {
                    Anchor a = run[k - 1], b = run[k];
                    var from = new Vector3(a.At.X, a.Floor + strand, a.At.Y);
                    var to = new Vector3(b.At.X, b.Floor + strand, b.At.Y);
                    float span = from.DistanceTo(to);
                    if (span < 0.01f)
                    {
                        continue;
                    }

                    Vector3 dir = (to - from) / span;
                    var normal = new Vector3(-dir.Z, 0f, dir.X).Normalized();
                    float sag = _def.Sag_m * span / 2.5f;
                    int pieces = Math.Max(2, (int)MathF.Ceiling(span / 0.25f));
                    for (int s = 0; s < pieces; s++)
                    {
                        float f0 = (float)s / pieces, f1 = (float)(s + 1) / pieces;
                        Vector3 p0 = from.Lerp(to, f0) + Vector3.Down * (sag * 4f * f0 * (1f - f0));
                        Vector3 p1 = from.Lerp(to, f1) + Vector3.Down * (sag * 4f * f1 * (1f - f1));
                        float u0 = along + span * f0, u1 = along + span * f1;
                        float slack0 = 4f * f0 * (1f - f0), slack1 = 4f * f1 * (1f - f1);
                        Quad(tape, p0, p1, half, normal, u0, u1, slack0, slack1);
                    }

                    along += span;
                    Length_m += span;
                }
            }
        }

        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/boundary_tape.gdshader") };
        material.SetShaderParameter("color_a", Color.FromHtml(_def.Colors[0]));
        material.SetShaderParameter("color_b", Color.FromHtml(_def.Colors[1]));
        material.SetShaderParameter("stripe", _def.Stripe_m);
        material.SetShaderParameter("width", _def.TapeWidth_m);
        material.SetShaderParameter("flutter", _def.Flutter_m);
        material.SetShaderParameter("wind", new Vector3(wind.X, 0f, wind.Y).Normalized());
        if (Length_m > 0f)
        {
            tape.SetMaterial(material);
            AddChild(new MeshInstance3D { Name = "Tape", Mesh = tape.Commit(), CastShadow = GeometryInstance3D.ShadowCastingSetting.On });
        }

        PostCount = posts.Count;
        if (posts.Count == 0)
        {
            return;
        }

        var postMaterial = (ShaderMaterial)material.Duplicate();
        postMaterial.SetShaderParameter("posts", true);
        postMaterial.SetShaderParameter("band", _def.PostBand_m);
        var multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 1, Material = postMaterial },
            InstanceCount = posts.Count,
        };
        for (int i = 0; i < posts.Count; i++)
        {
            multi.SetInstanceTransform(i, posts[i]);
        }

        AddChild(new MultiMeshInstance3D { Name = "Posts", Multimesh = multi });
    }

    /// <summary>One piece of tape: an upright ribbon from <paramref name="p0"/> to <paramref name="p1"/>, <paramref name="half"/> either side of them.</summary>
    private static void Quad(SurfaceTool st, Vector3 p0, Vector3 p1, float half, Vector3 normal, float u0, float u1, float slack0, float slack1)
    {
        void V(Vector3 p, float u, float v, float slack)
        {
            st.SetNormal(normal);
            st.SetUV(new Vector2(u, v));
            st.SetColor(new Color(1f, 1f, 1f, slack));
            st.AddVertex(p);
        }

        Vector3 up = Vector3.Up * half;
        V(p0 - up, u0, 0f, slack0);
        V(p1 - up, u1, 0f, slack1);
        V(p1 + up, u1, 1f, slack1);
        V(p0 - up, u0, 0f, slack0);
        V(p1 + up, u1, 1f, slack1);
        V(p0 + up, u0, 1f, slack0);
    }
}
