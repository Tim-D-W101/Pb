using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Players;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>Phase 5 (M5.5): the Sports Ground's field, its layouts of inflatable bunkers, and its nets.</summary>
public class FieldTests
{
    private readonly ITestOutputHelper _out;

    public FieldTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static LevelLayout Ground => TestData.Data.Levels["sports_ground"];

    public static IEnumerable<object[]> Layouts() => Ground.Field!.Layouts.Select(l => new object[] { l.Id });

    /// <summary>The field as a round in <paramref name="layout"/> plays it (the place that names it).</summary>
    private static LevelLayout In(string layout) => Ground.ForPlace(Ground.Places.First(p => Ground.Field!.Layout(p.Layout).Id == layout));

    [Fact]
    public void Each_place_plays_its_layout_and_only_its_bunkers()
    {
        FieldSpec field = Ground.Field!;
        Assert.Equal(field.Layouts.Count, Ground.Places.Count);
        foreach (PlaceSpec place in Ground.Places)
        {
            LevelLayout level = Ground.ForPlace(place);
            Assert.Equal(place.Layout ?? field.Layouts[0].Id, level.FieldLayout!.Id);
            Assert.Equal(field.BasePrimitives + level.FieldLayout.Primitives.Count, level.Primitives.Count);
            Assert.Equal(field.BaseProps + level.FieldLayout.Props.Count, level.Props.Count);
            // Every bunker's prop names its own pieces, after the level's own.
            foreach (FieldBunker bunker in level.FieldLayout.Bunkers)
            {
                PropInstance prop = level.Props[bunker.PropIndex];
                Assert.Same(bunker.Prop, prop.Type);
                Assert.True(prop.FirstPrimitive >= field.BasePrimitives);
                Assert.True(prop.FirstPrimitive + prop.PrimitiveCount <= level.Primitives.Count);
                Assert.True(Vector3.Distance(bunker.Position, prop.Position) < 1e-4f);
            }
        }

        Assert.NotEqual(In("classic").Primitives.Count, In("crossfire").Primitives.Count);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Each_layout_is_made_whole_and_every_bunker_has_its_twin(string layoutId)
    {
        FieldLayoutSpec layout = Ground.Field!.Layout(layoutId);
        bool mirror = layout.Symmetry == FieldSymmetry.Mirror;
        Vector3 Twin(Vector3 p) => mirror ? new Vector3(p.X, p.Y, -p.Z) : new Vector3(-p.X, p.Y, -p.Z);
        List<LevelPrimitive> pieces = layout.Primitives.Where(p => p.Has(PrimitiveFlags.Paint)).ToList();
        Assert.NotEmpty(pieces);
        foreach (LevelPrimitive p in pieces)
        {
            // Its twin: the same piece on the twin's spot, its box the mirror (or the half turn) of this one's.
            Aabb b = p.Bounds;
            Vector3 min = mirror ? new Vector3(b.Min.X, b.Min.Y, -b.Max.Z) : new Vector3(-b.Max.X, b.Min.Y, -b.Max.Z);
            Vector3 max = mirror ? new Vector3(b.Max.X, b.Max.Y, -b.Min.Z) : new Vector3(-b.Min.X, b.Max.Y, -b.Min.Z);
            Assert.Contains(pieces, q => q.Kind == p.Kind && Vector3.Distance(q.HalfExtents, p.HalfExtents) < 1e-4f &&
                                         Vector3.Distance(q.Center, Twin(p.Center)) < 1e-3f &&
                                         Vector3.Distance(q.Bounds.Min, min) < 2e-3f && Vector3.Distance(q.Bounds.Max, max) < 2e-3f);
        }

        // Side 0's bunkers on its half, their twins on the other, and the rest (their own twins) on the halfway line.
        foreach (FieldBunker bunker in layout.Bunkers)
        {
            Assert.True(bunker.Side switch
            {
                0 => bunker.Position.Z >= 0f,
                1 => bunker.Position.Z <= 0f,
                _ => MathF.Abs(bunker.Position.Z) < 0.01f,
            }, $"{bunker.Prop.Id} at {bunker.Position} on side {bunker.Side}");
            Assert.Contains(bunker.Tags, t => t is "back" or "mid" or "front");
        }

        Assert.Equal(layout.Bunkers.Count(b => b.Side == 0), layout.Bunkers.Count(b => b.Side == 1));
        Assert.Equal(layout.Lanes.Count(l => l.Side == 0), layout.Lanes.Count(l => l.Side == 1));
        _out.WriteLine($"{layoutId}: {layout.Bunkers.Count} bunkers, {pieces.Count} pieces, {layout.Lanes.Count} lanes");
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Every_bunker_stops_paint_and_feet_and_gives_cover(string layoutId)
    {
        LevelLayout level = In(layoutId);
        NavGrid grid = NavGrid.Build(level, TestData.Data.Bots.Navigation);
        CoverSet cover = CoverSet.Build(level, grid, TestData.Config.Movement.StandEyeHeight, TestData.Config.Movement.CrouchEyeHeight);
        var world = new CollisionWorld();
        level.BuildCollision(world);
        foreach (FieldBunker bunker in level.FieldLayout!.Bunkers)
        {
            PropInstance prop = level.Props[bunker.PropIndex];
            float top = 0f;
            for (int i = prop.FirstPrimitive; i < prop.FirstPrimitive + prop.PrimitiveCount; i++)
            {
                LevelPrimitive p = level.Primitives[i];
                Assert.True(p.Has(PrimitiveFlags.Paint) && p.Has(PrimitiveFlags.Walk) && p.Has(PrimitiveFlags.Cover), $"{bunker.Prop.Id}: {p.Flags}");
                top = MathF.Max(top, p.Bounds.Max.Y);
                // Paint meets it where it stands: a ball dropped on its middle lands on it, not the ground.
                Vector3 middle = p.Center;
                Assert.True(world.SweepSphere(new Vector3(middle.X, 6f, middle.Z), new Vector3(middle.X, -0.5f, middle.Z), 0.0086f, out SweepHit hit) &&
                            hit.Point.Y > 0.5f, $"{bunker.Prop.Id} at {bunker.Position}: a ball from above lands at {hit.Point}");
                // Feet can't: the grid has nowhere to stand on the ground at its middle.
                Assert.True(grid.SpanAt(new Vector3(middle.X, 0f, middle.Z)) < 0, $"{bunker.Prop.Id} at {bunker.Position}: walkable inside it");
            }

            // Somewhere to hide behind it: a cover point by it (within a metre of its footprint), as tall as it hides you.
            bool By(Vector3 at)
            {
                for (int i = prop.FirstPrimitive; i < prop.FirstPrimitive + prop.PrimitiveCount; i++)
                {
                    Aabb b = level.Primitives[i].Bounds;
                    if (at.X > b.Min.X - 1f && at.X < b.Max.X + 1f && at.Z > b.Min.Z - 1f && at.Z < b.Max.Z + 1f)
                    {
                        return true;
                    }
                }

                return false;
            }

            List<CoverPoint> by = cover.Points.Where(c => By(c.Position)).ToList();
            Assert.True(by.Count > 0, $"no cover by the {bunker.Prop.Id} at {bunker.Position}");
            CoverHeight expected = top >= TestData.Config.Movement.StandEyeHeight + 0.1f ? CoverHeight.Full : CoverHeight.Half;
            Assert.Contains(by, c => c.Height == expected);
        }
    }

    [Fact]
    public void A_ball_leaving_the_nets_is_gone()
    {
        var sim = new SimWorld(TestData.Config, 7);
        sim.LoadLevel(Ground);
        Aabb nets = Ground.Bounds;
        // From the middle of the field, over the south net and the east one; and up, over the top.
        var shots = new[] { new Vector3(0f, 6f, 85f), new Vector3(85f, 4f, 3f), new Vector3(2f, 90f, 1f) };
        var gone = new List<SimEvent>();
        for (int i = 0; i < shots.Length; i++)
        {
            Assert.True(sim.SpawnRemoteShot(-1, (uint)(i + 1), 0, new Vector3(0f, 1.5f, 3f), shots[i], 0f, 0));
        }

        for (int t = 0; t < 600 && gone.Count < shots.Length; t++)
        {
            sim.Step(ReadOnlySpan<InputCommand>.Empty);
            gone.AddRange(sim.Events.Items.ToArray().Where(e => e.Type == SimEventType.BallDespawned));
            Assert.DoesNotContain(sim.Events.Items.ToArray(), e => e.Type is SimEventType.BallBroke or SimEventType.BallBounced);
            sim.Events.Clear();
        }

        Assert.Equal(shots.Length, gone.Count);
        Assert.All(gone, e =>
        {
            Assert.Equal((int)DespawnReason.OutOfBounds, e.Extra);
            Assert.False(nets.Contains(e.Position) && e.Position.Y < nets.Max.Y - 0.5f, $"gone at {e.Position}, inside the nets");
        });
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void The_bots_grid_reaches_every_bunker_from_both_starts(string layoutId)
    {
        LevelLayout level = In(layoutId);
        NavGrid grid = NavGrid.Build(level, TestData.Data.Bots.Navigation);
        FieldSpec field = level.Field!;
        var path = new List<Vector3>();
        for (int side = 0; side < 2; side++)
        {
            Aabb box = field.StartBoxOf(side, 0f, 2f);
            Vector3 start = new((box.Min.X + box.Max.X) * 0.5f, 0f, (box.Min.Z + box.Max.Z) * 0.5f);
            Assert.True(grid.SpanAt(start) >= 0, $"side {side}'s start box has nowhere to stand at {start}");
            foreach (FieldBunker bunker in level.FieldLayout!.Bunkers)
            {
                // Just off it, on the side towards this side's start.
                float off = bunker.Prop.Colliders.Max(c => MathF.Max(c.HalfExtents.X, c.HalfExtents.Z)) + 0.7f;
                Vector3 toward = Vector3.Normalize(new Vector3(start.X - bunker.Position.X, 0f, start.Z - bunker.Position.Z));
                Vector3? goal = null;
                for (int k = 0; k < 8 && goal is null; k++)
                {
                    Vector3 way = Vector3.Transform(toward, Quaternion.CreateFromAxisAngle(Vector3.UnitY, k * MathF.PI / 4f));
                    Vector3 at = bunker.Position + way * off;
                    if (grid.SpanAt(at) >= 0)
                    {
                        goal = at;
                    }
                }

                Assert.True(goal is not null, $"nowhere to stand round the {bunker.Prop.Id} at {bunker.Position}");
                Assert.True(grid.FindPath(start, goal!.Value, path), $"no path from side {side}'s start to the {bunker.Prop.Id} at {bunker.Position}");
                Assert.True(Vector3.Distance(path[^1], goal.Value) < 1.5f, $"the path to the {bunker.Prop.Id} at {bunker.Position} stops short at {path[^1]}");
            }
        }
    }
}
