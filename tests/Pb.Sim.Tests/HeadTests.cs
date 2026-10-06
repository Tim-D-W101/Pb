using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Events;
using Pb.Sim.Players;
using Xunit;
using Xunit.Abstractions;

namespace Pb.Sim.Tests;

/// <summary>
/// The head (M3.14): it turns from the aim up to movement's maxHeadTurn with its hitboxes, the marker staying on the aim;
/// a bot sees where its head looks, looks ahead of its turns, glances round when nothing's going on and sweeps a post a
/// look at a time.
/// </summary>
[Collection(BotArenaCollection.Name)]
public class HeadTests
{
    private const int Second = 120;

    private readonly ITestOutputHelper _out;

    public HeadTests(ITestOutputHelper output)
    {
        _out = output;
    }

    [Fact]
    public void The_head_and_mask_turn_with_the_head_and_the_marker_stays_on_the_aim()
    {
        HitboxParams rig = TestData.Config.Hitboxes;
        float pivot = TestData.Config.Movement.LeanPivotBelowEye;
        var straight = new HitboxPose(new Vector3(2f, 0f, -3f), 0.3f, 0.1f, 1.6f, 0f, 1f, true, true);
        PosedBox[] a = Posed(straight with { }, rig, pivot);
        PosedBox[] b = Posed(straight with { HeadYaw = 1f }, rig, pivot);

        // The head turns in place; the mask goes round it by the turn and faces where the head does.
        AssertNear(Part(a, HitboxPart.Head).Center, Part(b, HitboxPart.Head).Center);
        Vector3 from = Part(a, HitboxPart.Mask).Center - Part(a, HitboxPart.Head).Center;
        Vector3 to = Part(b, HitboxPart.Mask).Center - Part(b, HitboxPart.Head).Center;
        Assert.Equal(1f, FlatTurn(from, to), 3);
        Assert.Equal(1f, FlatTurn(Part(a, HitboxPart.Mask).AxisZ, Part(b, HitboxPart.Mask).AxisZ), 3);
        Assert.Equal(1f, FlatTurn(Part(a, HitboxPart.Head).AxisZ, Part(b, HitboxPart.Head).AxisZ), 3);

        // The marker, the arms and the rest of the body stay where they were.
        foreach (HitboxPart part in new[] { HitboxPart.Marker, HitboxPart.Arms, HitboxPart.Loader, HitboxPart.Tank, HitboxPart.Torso, HitboxPart.Legs })
        {
            AssertNear(Part(a, part).Center, Part(b, part).Center);
            AssertNear(Part(a, part).AxisZ, Part(b, part).AxisZ);
        }

        // Out of the round, the head's straight whatever the pose says.
        PosedBox[] outStraight = Posed(straight with { Alive = false }, rig, pivot);
        PosedBox[] outTurned = Posed(straight with { Alive = false, HeadYaw = 1f }, rig, pivot);
        AssertNear(Part(outStraight, HitboxPart.Mask).Center, Part(outTurned, HitboxPart.Mask).Center);
    }

    [Fact]
    public void The_sim_keeps_the_head_within_its_turn_and_straightens_it_once_out()
    {
        var sim = new SimWorld(TestData.Config);
        sim.Collision.Add(new PlaneShape(Vector3.UnitY, 0f), TestData.Config.Surfaces.Get("turf"), "ground");
        PlayerState player = sim.AddPlayer(0, 0, Vector3.Zero, 0f);
        var commands = new[] { new InputCommand { HeadYaw = 3f } };
        sim.Step(commands);
        Assert.Equal(TestData.Config.Movement.MaxHeadTurn, player.HeadYaw, 4);
        Assert.Equal(player.HeadYaw, HitboxPose.Of(player).HeadYaw);

        commands[0].HeadYaw = -0.5f;
        sim.Step(commands);
        Assert.Equal(-0.5f, player.HeadYaw, 4);

        player.Alive = false;
        sim.Step(commands);
        Assert.Equal(0f, player.HeadYaw);
    }

    [Fact]
    public void A_bot_sees_where_its_head_looks()
    {
        // Someone well round to the bot's left, beyond the edge of its view with the head straight, is seen once it
        // turns its head that way.
        float Meter(float headYaw)
        {
            var sim = new SimWorld(TestData.Config);
            sim.Collision.Add(new PlaneShape(Vector3.UnitY, 0f), TestData.Config.Surfaces.Get("turf"), "ground");
            PlayerState bot = sim.AddPlayer(1, 1, Vector3.Zero, 0f);
            float half = TestData.Data.Bots.Senses.HalfFieldOfView;
            float bearing = half + 0.35f; // positive yaw is to the left; forward at yaw 0 is −Z
            sim.AddPlayer(0, 0, new Vector3(-MathF.Sin(bearing), 0f, -MathF.Cos(bearing)) * 8f, 0f);
            var senses = new BotSenses(sim, bot, TestData.Data.Bots.Senses, TestData.Data.Bots.Difficulty["hard"], 7);
            bot.HeadYaw = headYaw;
            for (int t = 0; t < Second; t++)
            {
                senses.Update(sim.Dt, ReadOnlySpan<SimEvent>.Empty);
            }

            return senses.For(0)?.Meter ?? 0f;
        }

        Assert.Equal(0f, Meter(0f));
        Assert.True(Meter(TestData.Config.Movement.MaxHeadTurn) > 0.2f, "turning its head that way, the bot should notice them");
        Assert.Equal(0f, Meter(-TestData.Config.Movement.MaxHeadTurn));
    }

    [Fact]
    public void With_nothing_going_on_a_bot_glances_round_now_and_then()
    {
        BotArena arena = BotArena.Create("hard");
        BotBrain bot = arena.AddBot("yard_east");
        arena.Start();
        arena.PlaceHero(HiddenFar(arena, bot), bot.Self.Position);
        float most = 0f;
        int straight = 0, ticks = 0;
        var glances = new List<float>();
        bool turned = false;
        arena.Run(30 * Second, () =>
        {
            float head = MathF.Abs(bot.Self.HeadYaw);
            most = MathF.Max(most, head);
            ticks++;
            straight += head < 0.05f ? 1 : 0;
            bool now = head > 0.3f;
            if (now && !turned)
            {
                glances.Add(arena.Sim.Tick / (float)Second);
            }

            turned = now;
            return false;
        });

        _out.WriteLine($"most {most * Units.RadiansToDegrees:0}°, straight {100f * straight / ticks:0}% of the time, glances at {string.Join(", ", glances.Select(g => $"{g:0.0}"))} s");
        Assert.Equal(BotMode.Idle, bot.Mode);
        Assert.True(glances.Count >= 3, $"only {glances.Count} glances in 30 s");
        Assert.True(most <= TestData.Config.Movement.MaxHeadTurn + 1e-4f);
        Assert.True(straight > ticks / 4, "the head should be straight more often than not between glances");
    }

    [Fact]
    public void A_bot_looks_at_a_noise_before_its_body_comes_round()
    {
        BotArena arena = BotArena.Create("hard");
        BotBrain bot = arena.AddBot("yard_east");
        arena.Start();
        // Someone out of sight behind it fires a shot into the ground.
        Vector3 behind = HiddenFar(arena, bot);
        arena.PlaceHero(behind, bot.Self.Position);
        arena.Run(3 * Second);
        float toNoise = MathF.Atan2(-(behind.X - bot.Self.Position.X), -(behind.Z - bot.Self.Position.Z));
        arena.HeroScript = (t, p) => new InputCommand { Tick = t, Yaw = p.Yaw, Pitch = -0.6f, Buttons = t % 30 == 0 ? InputButtons.Fire : InputButtons.None };
        bool headFirst = false;
        arena.Run(3 * Second, () =>
        {
            float body = MathF.Abs(BotAim.Wrap(toNoise - bot.Self.Yaw));
            float head = MathF.Abs(BotAim.Wrap(toNoise - (bot.Self.Yaw + bot.Self.HeadYaw)));
            // The head nearer the noise than the body by a good way, while the body still has far to turn.
            headFirst |= body > 0.6f && head < body - 0.5f;
            return headFirst;
        });

        Assert.True(headFirst, "the head should turn towards the noise before the body comes round");
    }

    [Fact]
    public void A_post_is_swept_a_look_at_a_time()
    {
        BotArena arena = BotArena.Create("hard");
        BotBrain bot = arena.AddBot("yard_east");
        arena.Start();
        arena.PlaceHero(HiddenFar(arena, bot), bot.Self.Position);
        float facing = bot.HomeYaw;
        float scan = TestData.Data.Bots.Brain.ScanAngle;
        float left = 0f, right = 0f, still = 0f, longest = 0f, last = bot.Self.Yaw;
        arena.Run(25 * Second, () =>
        {
            float off = BotAim.Wrap(bot.Self.Yaw - facing);
            left = MathF.Max(left, off);
            right = MathF.Min(right, off);
            still = MathF.Abs(BotAim.Wrap(bot.Self.Yaw - last)) < 1e-4f ? still + arena.Sim.Dt : 0f;
            longest = MathF.Max(longest, still);
            last = bot.Self.Yaw;
            return false;
        });

        _out.WriteLine($"swept {left * Units.RadiansToDegrees:0}° left and {-right * Units.RadiansToDegrees:0}° right, held still up to {longest:0.0} s");
        Assert.InRange(left, scan - 0.05f, scan + 0.05f);
        Assert.InRange(-right, scan - 0.05f, scan + 0.05f);
        Assert.True(longest >= TestData.Data.Bots.Brain.ScanHoldMin * 0.6f, $"each look should be held, not a steady swing (longest still {longest:0.00} s)");
    }

    /// <summary>A walkable spot well away from the bot, behind it and out of its sight.</summary>
    private static Vector3 HiddenFar(BotArena arena, BotBrain bot)
    {
        Vector3 eye = bot.Self.EyePosition;
        Vector3 back = -ViewAngles.FlatForward(bot.Self.Yaw);
        for (int ring = 0; ring < 8; ring++)
        {
            float d = 16f + ring * 3f;
            for (int k = -6; k <= 6; k++)
            {
                Vector3 dir = Vector3.Transform(back, Quaternion.CreateFromAxisAngle(Vector3.UnitY, k * 0.12f));
                Vector3 at = bot.Self.Position + dir * d;
                if (arena.Squad.Grid.TrySnap(at, out Vector3 spot) && MathF.Abs(spot.Y - bot.Self.Position.Y) < 0.3f &&
                    arena.Sim.Collision.SweepSphere(spot + new Vector3(0f, 1.6f, 0f), eye, 0f, out _))
                {
                    return spot;
                }
            }
        }

        throw new InvalidOperationException($"nowhere out of sight behind {bot.Self.Name}");
    }

    private static PosedBox[] Posed(in HitboxPose pose, HitboxParams rig, float pivot)
    {
        var parts = new PosedBox[HitboxRig.PartCount];
        HitboxRig.Pose(pose, rig, pivot, parts);
        return parts;
    }

    private static PosedBox Part(PosedBox[] parts, HitboxPart part) => parts.First(p => p.Part == part);

    /// <summary>How far <paramref name="to"/> is turned from <paramref name="from"/> about the vertical (rad, positive to the left).</summary>
    private static float FlatTurn(Vector3 from, Vector3 to) =>
        BotAim.Wrap(MathF.Atan2(-to.X, -to.Z) - MathF.Atan2(-from.X, -from.Z));

    private static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");
}
