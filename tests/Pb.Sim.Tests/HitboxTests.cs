using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Events;
using Pb.Sim.Players;

namespace Pb.Sim.Tests;

/// <summary>Phase 2 hitbox rig, eliminations (spec §1.2) and mask spray (spec §1.3).</summary>
public class HitboxTests
{
    private static SimConfig Config => TestData.Config;

    private static float BallRadius => Config.Projectile.Radius;

    /// <summary>A world with a single player standing at the origin facing −Z.</summary>
    private static (SimWorld Sim, PlayerState Player) OnePlayer()
    {
        var sim = new SimWorld(Config);
        PlayerState player = sim.AddPlayer(1, 1, Vector3.Zero, 0f);
        return (sim, player);
    }

    /// <summary>The part a ball flying straight at the player along +Z (from the front) would hit first, or null.</summary>
    private static HitboxPart? FromTheFront(SimWorld sim, float x, float y) =>
        sim.PlayerHits.SweepSphere(new Vector3(x, y, -5f), new Vector3(x, y, 5f), BallRadius, sim.Tick, -1, out HitboxHit hit)
            ? hit.Part
            : null;

    [Fact]
    public void Standing_rig_puts_each_part_where_a_player_carries_it()
    {
        (SimWorld sim, _) = OnePlayer();
        Assert.Equal(HitboxPart.Legs, FromTheFront(sim, 0f, 0.5f));
        Assert.Equal(HitboxPart.Torso, FromTheFront(sim, 0f, 1.15f));
        Assert.Equal(HitboxPart.Mask, FromTheFront(sim, 0f, 1.58f));
        Assert.Equal(HitboxPart.Marker, FromTheFront(sim, 0.13f, 1.45f));
        Assert.Null(FromTheFront(sim, 0.5f, 0.5f));
        Assert.Null(FromTheFront(sim, 0f, 2.0f));

        // From behind at eye height, the back of the head comes first.
        Assert.True(sim.PlayerHits.SweepSphere(new Vector3(0f, 1.62f, 5f), new Vector3(0f, 1.62f, -5f), BallRadius, sim.Tick, -1,
            out HitboxHit back));
        Assert.Equal(HitboxPart.Head, back.Part);
        Assert.Equal(PlayerHitboxes.ReceiverIdBase + 1, back.ReceiverId);
        Assert.Equal(Config.Hitboxes.Surface, back.Surface);
    }

    [Fact]
    public void Crouching_ducks_the_head_and_leaning_moves_it()
    {
        (SimWorld sim, PlayerState player) = OnePlayer();
        player.EyeHeight = Config.Movement.CrouchEyeHeight;
        Assert.Null(FromTheFront(sim, 0f, 1.58f));
        Assert.Equal(HitboxPart.Mask, FromTheFront(sim, 0f, Config.Movement.CrouchEyeHeight - 0.04f));

        player.EyeHeight = Config.Movement.StandEyeHeight;
        player.LeanRoll = Config.Movement.LeanAngle; // fully right
        player.LeanOffset = PlayerPose.LeanOffset(player.LeanRoll, 0f, Config.Movement.LeanPivotBelowEye);
        float eyeX = player.LeanOffset.X;
        float eyeY = Config.Movement.StandEyeHeight + player.LeanOffset.Y;
        Assert.Equal(HitboxPart.Mask, FromTheFront(sim, eyeX, eyeY - 0.04f));
        Assert.Null(FromTheFront(sim, -0.08f, eyeY - 0.04f));
    }

    [Fact]
    public void Shoulder_swap_carries_the_gear_to_the_other_side()
    {
        (SimWorld sim, PlayerState player) = OnePlayer();
        Assert.Equal(HitboxPart.Marker, FromTheFront(sim, 0.13f, 1.45f));
        Assert.Null(FromTheFront(sim, -0.13f, 1.45f));

        player.Shoulder = player.ShoulderTarget = -1f;
        Assert.Equal(HitboxPart.Marker, FromTheFront(sim, -0.13f, 1.45f));
        Assert.Null(FromTheFront(sim, 0.13f, 1.45f));
    }

    [Fact]
    public void A_break_on_a_player_eliminates_them_once_and_credits_the_shooter()
    {
        (SimWorld sim, PlayerState shooter, PlayerState victim) = Duel(distance: 10f);
        List<SimEvent> events = FireAndWait(sim, shooter);

        SimEvent out1 = Assert.Single(events, e => e.Type == SimEventType.PlayerEliminated);
        Assert.Equal(shooter.Id, out1.PlayerId);
        Assert.Equal(victim.Id, out1.TargetId);
        Assert.False(victim.Alive);
        Assert.True(victim.Present, "an eliminated player is still on the field until they walk off");
        Assert.Equal(shooter.Id, victim.EliminatedBy);
        Assert.Equal(1, shooter.Eliminations);
        SimEvent broke = Assert.Single(events, e => e.Type == SimEventType.BallBroke);
        Assert.True(broke.Lethal);
        Assert.Equal(PlayerHitboxes.ReceiverIdOf(victim), broke.TargetId);

        // A second ball still breaks on them but changes nothing; and they can't shoot back.
        events = FireAndWait(sim, shooter);
        Assert.DoesNotContain(events, e => e.Type == SimEventType.PlayerEliminated);
        Assert.Contains(events, e => e.Type == SimEventType.BallBroke && e.TargetId == PlayerHitboxes.ReceiverIdOf(victim) && !e.Lethal);
        Assert.Equal(1, shooter.Eliminations);
        Assert.DoesNotContain(FireAndWait(sim, victim), e => e.Type == SimEventType.ShotFired);
    }

    [Fact]
    public void Balls_in_flight_still_count_after_their_shooter_is_out_unless_the_rules_say_no()
    {
        foreach (bool ballsInFlightCount in new[] { true, false })
        {
            (SimWorld sim, PlayerState shooter, PlayerState victim) = Duel(distance: 20f, ballsInFlightCount);
            List<SimEvent> events = FireAndWait(sim, shooter, afterShot: () => shooter.Alive = false);
            Assert.Equal(ballsInFlightCount, !victim.Alive);
            Assert.Equal(ballsInFlightCount, events.Any(e => e.Type == SimEventType.PlayerEliminated));
            Assert.Contains(events, e => e.Type == SimEventType.BallBroke && e.TargetId == PlayerHitboxes.ReceiverIdOf(victim));
        }
    }

    [Fact]
    public void Hitbox_history_answers_for_past_ticks()
    {
        (SimWorld sim, PlayerState player) = OnePlayer();
        var commands = new InputCommand[1];
        sim.Step(commands); // records the pose at x = 0 for tick 0
        int then = sim.Tick - 1;
        player.Position = new Vector3(3f, 0f, 0f);
        sim.Step(commands);

        bool HitsAt(float x, int tick) =>
            sim.PlayerHits.SweepSphere(new Vector3(x, 1.15f, -5f), new Vector3(x, 1.15f, 5f), BallRadius, tick, -1, out _);

        Assert.True(HitsAt(0f, then));
        Assert.False(HitsAt(3f, then));
        Assert.True(HitsAt(3f, sim.Tick - 1));
        Assert.False(HitsAt(0f, sim.Tick - 1));
    }

    [Fact]
    public void A_break_near_the_face_sprays_the_mask_harder_the_closer_it_is()
    {
        foreach ((float wallZ, bool sprayed) in new[] { (-0.3f, true), (-0.8f, false) })
        {
            var sim = new SimWorld(Config);
            // A wall whose front face is at wallZ, right in front of the player's face.
            sim.Collision.Add(new BoxShape(new Vector3(0f, 1.5f, wallZ - 0.05f), Quaternion.Identity, new Vector3(1f, 1.5f, 0.05f)),
                Config.Surfaces.Get("concrete"), "wall");
            sim.Collision.Add(new PlaneShape(Vector3.UnitY, 0f), Config.Surfaces.Get("turf"), "ground");
            PlayerState player = sim.AddPlayer(1, 0, new Vector3(0f, 0f, 0f), 0f);

            // Someone else's ball breaks on the wall at face height, coming from the far side.
            sim.Ballistics.Spawn(new Vector3(0f, 1.62f, -3f), new Vector3(0f, 0f, 1f) * 80f, 7, 1, 1, new Pcg32(5), Config.Dt, 0, sim.Events);
            var events = new List<SimEvent>();
            var commands = new InputCommand[1];
            for (int i = 0; i < 20; i++)
            {
                sim.Step(commands);
                events.AddRange(sim.Events.Items.ToArray());
                sim.Events.Clear();
            }

            Assert.Contains(events, e => e.Type == SimEventType.BallBroke);
            SimEvent[] spray = events.Where(e => e.Type == SimEventType.MaskSprayed).ToArray();
            Assert.Equal(sprayed, spray.Length == 1);
            if (sprayed)
            {
                Assert.Equal(player.Id, spray[0].TargetId);
                Assert.Equal(1, spray[0].Team);
                float distance = Vector3.Distance(player.EyePosition, spray[0].Position);
                Assert.Equal(1f - distance / Config.Hitboxes.MaskSprayRadius, spray[0].Value, 3);
            }
        }
    }

    /// <summary>Two players facing each other across open ground: the shooter (id 0, team 0) and the victim (id 1, team 1).</summary>
    private static (SimWorld Sim, PlayerState Shooter, PlayerState Victim) Duel(float distance, bool ballsInFlightCount = true)
    {
        SimConfig c = Config;
        SimConfig config = ballsInFlightCount ? c : new SimConfig
        {
            TickRate = c.TickRate, MatchSeed = c.MatchSeed, BallPoolCapacity = c.BallPoolCapacity, Surfaces = c.Surfaces,
            Projectile = c.Projectile, BreakModel = c.BreakModel, Shot = c.Shot, Fire = c.Fire, Loader = c.Loader, Air = c.Air,
            Movement = c.Movement, Hitboxes = WithBallsInFlight(c.Hitboxes, false),
        };
        var sim = new SimWorld(config);
        sim.Collision.Add(new PlaneShape(Vector3.UnitY, 0f), c.Surfaces.Get("turf"), "ground");
        PlayerState shooter = sim.AddPlayer(0, 0, Vector3.Zero, 0f);
        PlayerState victim = sim.AddPlayer(1, 1, new Vector3(0f, 0f, -distance), MathF.PI);
        return (sim, shooter, victim);
    }

    private static HitboxParams WithBallsInFlight(HitboxParams h, bool count) => new()
    {
        Surface = h.Surface, LegsWidth = h.LegsWidth, LegsDepth = h.LegsDepth, TorsoWidth = h.TorsoWidth, TorsoDepth = h.TorsoDepth,
        TorsoTopBelowEye = h.TorsoTopBelowEye, Head = h.Head, Mask = h.Mask, Arms = h.Arms, Marker = h.Marker, Loader = h.Loader,
        Tank = h.Tank, EliminatedRaise = h.EliminatedRaise, EliminatedPitch = h.EliminatedPitch, LethalParts = h.LethalParts,
        BallsInFlightCount = count, MaskSprayRadius = h.MaskSprayRadius,
    };

    /// <summary>
    /// The given player aims at the other one's chest and fires a single ball; returns every event
    /// until it has landed. <paramref name="afterShot"/> runs right after the shot leaves.
    /// </summary>
    private static List<SimEvent> FireAndWait(SimWorld sim, PlayerState shooter, Action? afterShot = null)
    {
        PlayerState target = sim.Players.First(p => p != shooter);
        Vector3 chest = target.Position + new Vector3(0f, 1.15f, 0f);
        (float yaw, float pitch) = ViewAngles.FromDirection(chest - shooter.EyePosition);
        var commands = new InputCommand[sim.Players.Count];
        int index = sim.Players.ToList().IndexOf(shooter);
        var events = new List<SimEvent>();
        for (int i = 0; i < 90; i++)
        {
            commands[index] = new InputCommand { Yaw = yaw, Pitch = pitch, Buttons = i == 30 ? InputButtons.Fire : InputButtons.None };
            sim.Step(commands);
            events.AddRange(sim.Events.Items.ToArray());
            sim.Events.Clear();
            if (i == 30)
            {
                afterShot?.Invoke();
            }
        }

        return events;
    }
}
