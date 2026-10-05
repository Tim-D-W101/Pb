using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Level;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>Cover points generated from the compound's geometry.</summary>
public class CoverSetTests
{
    private static readonly Lazy<(LevelLayout Level, NavGrid Grid, CoverSet Cover, CollisionWorld World)> Built = new(() =>
    {
        LevelLayout level = TestData.Data.Levels["oxbarrow_works"];
        NavGrid grid = NavGrid.Build(level, TestData.Data.Bots.Navigation);
        var world = new CollisionWorld();
        level.BuildCollision(world);
        return (level, grid, Build(level, grid), world);
    });

    private readonly ITestOutputHelper _out;

    public CoverSetTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static CoverSet Cover => Built.Value.Cover;

    private static CoverSet Build(LevelLayout level, NavGrid grid) =>
        CoverSet.Build(level, grid, TestData.Config.Movement.StandEyeHeight, TestData.Config.Movement.CrouchEyeHeight);

    [Fact]
    public void The_compound_has_cover_of_both_heights_where_bots_can_stand()
    {
        int full = Cover.Points.Count(p => p.Height == CoverHeight.Full);
        int half = Cover.Points.Count(p => p.Height == CoverHeight.Half);
        int edges = Cover.Points.Count(p => p.CanShoot);
        _out.WriteLine($"{Cover.Points.Count} cover points: {full} full, {half} half, {edges} to shoot from (over the top or round an edge)");
        Assert.True(full > 100);
        Assert.True(half > 20);
        Assert.True(edges > 100);
        foreach (CoverPoint p in Cover.Points)
        {
            int span = Built.Value.Grid.SpanAt(p.Position);
            Assert.True(span >= 0, $"cover point at {p.Position} is off the navigation grid");
        }
    }

    [Fact]
    public void Cover_hides_you_from_someone_on_the_far_side()
    {
        // Someone 12 m behind the cover (straight back through its face) mustn't see the eyes of a bot
        // crouched there (or standing, for full cover).
        CollisionWorld world = Built.Value.World;
        int checkedPoints = 0;
        int hidden = 0;
        foreach (CoverPoint p in Cover.Points)
        {
            Vector3 threatEye = p.Position - p.Normal * 12f + new Vector3(0f, 1.6f, 0f);
            if (!Built.Value.Level.Bounds.Contains(threatEye))
            {
                continue;
            }

            float headHeight = (p.Height == CoverHeight.Full ? TestData.Config.Movement.StandEyeHeight : TestData.Config.Movement.CrouchEyeHeight) + 0.05f;
            checkedPoints++;
            if (world.SweepSphere(threatEye, p.Position + new Vector3(0f, headHeight, 0f), 0f, out _))
            {
                hidden++;
            }
        }

        _out.WriteLine($"{hidden} of {checkedPoints} points hidden from straight behind");
        Assert.True(hidden >= checkedPoints * 0.97, $"only {hidden} of {checkedPoints} cover points hide a head from straight behind");
    }

    [Fact]
    public void Edges_leave_room_to_step_out_and_shoot()
    {
        NavGrid grid = Built.Value.Grid;
        int full = 0;
        int ok = 0;
        foreach (CoverPoint p in Cover.Points.Where(p => p.HasEdge && p.Height == CoverHeight.Full))
        {
            full++;
            if (grid.SpanAt(p.Position + p.PeekDirection * CoverSet.PeekStep) >= 0)
            {
                ok++;
            }
        }

        Assert.True(ok >= full * 0.9, $"only {ok} of {full} full-height edges have room to step out");
    }

    [Fact]
    public void Claims_keep_two_bots_out_of_one_spot()
    {
        CoverSet cover = Build(Built.Value.Level, Built.Value.Grid);
        Assert.True(cover.Claim(3, who: 1));
        Assert.False(cover.Claim(3, who: 2));
        Assert.Equal(1, cover.ClaimedBy(3));
        Assert.True(cover.Claim(4, who: 1)); // moving on frees the old spot
        Assert.Equal(-1, cover.ClaimedBy(3));
        Assert.True(cover.Claim(3, who: 2));
        cover.Release(2);
        Assert.Equal(-1, cover.ClaimedBy(3));
    }

    [Fact]
    public void Nearby_points_are_found_by_distance()
    {
        var found = new List<int>();
        Vector3 yard = Built.Value.Level.OpponentSpawns.First(s => s.Id == "yard_containers").Position;
        Cover.Near(yard, 12f, found);
        Assert.NotEmpty(found);
        Assert.All(found, i => Assert.True(Vector3.Distance(Cover.Points[i].Position with { Y = 0f }, yard with { Y = 0f }) <= 12.01f));
        int brute = Cover.Points.Count(p => Vector3.Distance(p.Position with { Y = 0f }, yard with { Y = 0f }) <= 12f && MathF.Abs(p.Position.Y - yard.Y) <= 3f);
        Assert.Equal(brute, found.Count);
    }
}
