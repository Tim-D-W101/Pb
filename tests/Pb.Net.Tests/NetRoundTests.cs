using System.Numerics;
using Pb.Net.Client;
using Pb.Net.Protocol;
using Pb.Net.Server;
using Pb.Net.Transport;
using Pb.Sim;
using Pb.Sim.Core;
using Pb.Sim.Events;
using Pb.Sim.Match;
using Pb.Sim.Players;
using Pb.Sim.Tests;
using Xunit.Abstractions;

namespace Pb.Net.Tests;

/// <summary>Phase 4 (M4.2): whole rounds over the network without the engine, at the latencies the spec asks about.</summary>
public class NetRoundTests
{
    private const int Second = 120;
    private static readonly LagSettings Ping100 = new() { RoundTrip_ms = 100f };

    private readonly ITestOutputHelper _out;

    public NetRoundTests(ITestOutputHelper output)
    {
        _out = output;
    }

    [Fact]
    public void Players_get_in_with_the_same_build_and_password_and_are_turned_away_otherwise()
    {
        var rig = new NetRig(password: "pw");
        RigClient ada = rig.Join("Ada", password: "pw");
        RigClient twin = rig.Join("Ada", password: "pw");
        RigClient old = rig.Join("Old", build: "build-0", password: "pw");
        RigClient guess = rig.Join("Guess", password: "nope");
        rig.Run(5);
        Assert.Equal(ClientState.Joined, ada.Net.State);
        Assert.Equal("Ada", ada.Net.Welcome!.Name);
        Assert.Equal("Ada 2", twin.Net.Welcome!.Name);
        Assert.Equal("Test yard", ada.Net.Welcome.ServerName);
        Assert.Equal(ClientState.Refused, old.Net.State);
        Assert.Equal(RefusedReason.Version, old.Net.Refusal!.Reason);
        Assert.Contains("build-1", old.Net.Refusal.Text);
        Assert.Equal(RefusedReason.Password, guess.Net.Refusal!.Reason);
        Assert.Equal(2, rig.Server.Clients.Count(c => c.Welcomed));
    }

    [Fact]
    public void A_round_is_built_the_same_on_every_copy_and_others_are_drawn_where_the_server_had_them()
    {
        var rig = new NetRig();
        RigClient a = rig.Join("A", Ping100);
        RigClient b = rig.Join("B");
        rig.Run(30);
        rig.StartRound("teams", 900f, (0, 0, true, 0f, 0f), (1, 1, true, 5f, -20f), (2, 1, false, -5f, -20f));
        rig.GoLive();
        Assert.Equal(0, a.Session!.Local!.Id);
        Assert.Equal(1, b.Session!.Local!.Id);
        foreach (RigClient c in new[] { a, b })
        {
            Assert.Equal(rig.Sim!.Players.Select(p => (p.Id, p.Team, p.Name)), c.Sim!.Players.Select(p => (p.Id, p.Team, p.Name)));
        }

        // The bot walks a circle; where each copy draws it is where the server had it at that copy's display tick.
        var history = new Dictionary<int, Vector3>();
        rig.BotScript = (t, p) => new InputCommand { Tick = t, Move = new Vector2(0f, 1f), Yaw = t * 0.01f, Buttons = InputButtons.Walk };
        rig.AfterServerStep = sim => history[sim.Tick - 1] = sim.FindPlayer(2)!.Position;
        float worst = 0f;
        for (int t = 0; t < 4 * Second; t++)
        {
            rig.Tick();
            if (t < Second)
            {
                continue;
            }

            foreach (RigClient c in new[] { a, b })
            {
                double r = c.Net.RenderTick;
                int lo = (int)Math.Floor(r);
                if (!history.TryGetValue(lo, out Vector3 p0) || !history.TryGetValue(lo + 1, out Vector3 p1))
                {
                    continue;
                }

                Vector3 truth = Vector3.Lerp(p0, p1, (float)(r - lo));
                worst = MathF.Max(worst, Vector3.Distance(truth, c.Sim!.FindPlayer(2)!.Position));
            }
        }

        _out.WriteLine($"others drawn within {worst * 1000f:0.0} mm of where the server had them at the display tick");
        Assert.True(worst < 0.03f, $"drawn {worst:0.000} m off");
    }

    /// <summary>A busy few seconds: running, strafing, sprinting, jumping, crouching, leaning, swapping shoulders and firing.</summary>
    private static InputCommand Busy(int tick, PlayerState me)
    {
        int phase = tick / 60 % 8;
        var c = new InputCommand { Tick = tick, Yaw = MathF.Sin(tick * 0.02f) * 0.6f, Pitch = 0.05f };
        switch (phase)
        {
            case 0:
                c.Move = new Vector2(0f, 1f);
                break;
            case 1:
                c.Move = new Vector2(1f, 0.3f);
                c.Buttons = InputButtons.LeanRight;
                break;
            case 2:
                c.Move = new Vector2(0f, 1f);
                c.Buttons = InputButtons.Sprint;
                break;
            case 3:
                c.Move = new Vector2(-0.5f, -0.5f);
                c.Buttons = tick % 60 == 10 ? InputButtons.Jump : InputButtons.None;
                break;
            case 4:
                c.Move = new Vector2(0.7f, 0.7f);
                c.Buttons = InputButtons.Crouch | (tick % 20 < 10 ? InputButtons.Fire : InputButtons.None);
                break;
            case 5:
                c.Buttons = (tick % 60 == 5 ? InputButtons.SwapShoulder : InputButtons.None) | InputButtons.LeanLeft;
                break;
            case 6:
                c.Move = new Vector2(0f, -1f);
                c.Buttons = tick % 6 < 3 ? InputButtons.Fire : InputButtons.None;
                break;
            default:
                c.Buttons = tick % 120 == 0 ? InputButtons.Refill : InputButtons.None;
                break;
        }

        return c;
    }

    [Fact]
    public void Your_prediction_comes_out_as_the_server_had_it_at_100_ms()
    {
        var rig = new NetRig();
        RigClient me = rig.Join("Me", Ping100);
        rig.Run(30);
        rig.StartRound("teams", 900f, (0, 0, true, 0f, 0f), (1, 1, false, 20f, -25f));
        rig.GoLive();
        // Going live while commands are on their way costs a correction or two at the start.
        rig.Run(Second);
        int before = me.Session!.Corrections;
        me.Script = Busy;
        Vector3 from = me.Session.Local!.Position;
        rig.Run(8 * Second);
        _out.WriteLine($"corrections at the start {before}, after {me.Session.Corrections - before}; moved {Vector3.Distance(from, me.Session.Local.Position):0.0} m; " +
                       $"server queue missing {rig.Server.Clients[0].Commands.Missing}, merged {rig.Server.Clients[0].Commands.Merged}, waited {rig.Server.Clients[0].Commands.Waited}");
        Assert.Equal(before, me.Session.Corrections);
        Assert.True(Vector3.Distance(from, me.Session.Local.Position) > 10f);
        Assert.True(rig.ServerEvents.Count(e => e.Type == SimEventType.ShotFired && e.PlayerId == 0) > 10);
        PlayerState server = rig.Sim!.FindPlayer(0)!;
        Assert.Equal(server.Marker.ShotSequence, me.Session.Local.Marker.ShotSequence);
    }

    [Fact]
    public void A_correction_puts_the_servers_state_back_and_replays_from_it()
    {
        var rig = new NetRig();
        RigClient me = rig.Join("Me", Ping100);
        rig.Run(30);
        rig.StartRound("teams", 900f, (0, 0, true, 0f, 0f), (1, 1, false, 20f, -25f));
        rig.GoLive();
        me.Script = (t, p) => new InputCommand { Tick = t, Move = new Vector2(0f, 1f) };
        rig.Run(Second);
        int before = me.Session!.Corrections;
        int pushAt = rig.Sim!.Tick + 10;
        rig.AfterServerStep = sim =>
        {
            if (sim.Tick - 1 == pushAt)
            {
                sim.FindPlayer(0)!.Position += new Vector3(1f, 0f, 0f);
            }
        };
        rig.Run(2 * Second);
        Assert.Equal(before + 1, me.Session.Corrections);
        Assert.InRange(me.Session.LastCorrection, 0.95f, 1.05f);
        Assert.InRange(me.Session.Local!.Position.X, 0.99f, 1.01f);
    }

    [Fact]
    public void Others_move_smoothly_through_jitter_and_loss()
    {
        var rig = new NetRig();
        RigClient me = rig.Join("Me", new LagSettings { RoundTrip_ms = 100f, Jitter_ms = 30f, Loss_pct = 2f });
        rig.Run(30);
        rig.StartRound("teams", 900f, (0, 0, true, 0f, 0f), (1, 1, false, 0f, -15f));
        rig.GoLive();
        rig.BotScript = (t, p) => new InputCommand { Tick = t, Move = new Vector2(0f, 1f), Yaw = t * 0.015f };
        rig.Run(Second);
        PlayerState shown = me.Sim!.FindPlayer(1)!;
        Vector3 last = shown.Position;
        int stalls = 0;
        float fastest = 0f;
        for (int t = 0; t < 6 * Second; t++)
        {
            rig.Tick();
            float moved = Vector3.Distance(last, shown.Position);
            stalls += moved < 1e-4f ? 1 : 0;
            fastest = MathF.Max(fastest, moved);
            last = shown.Position;
        }

        float runStep = TestData.Config.Movement.RunSpeed * NetRig.Dt;
        _out.WriteLine($"stalls {stalls}, the biggest step {fastest / runStep:0.00} × a run's");
        Assert.True(stalls <= 2, $"{stalls} ticks with the bot stuck");
        Assert.True(fastest < runStep * 2f, $"a jump of {fastest:0.000} m in a tick");
    }

    [Fact]
    public void Every_event_arrives_once_and_in_order_through_loss_duplicates_and_jitter()
    {
        var rig = new NetRig();
        RigClient me = rig.Join("Me", new LagSettings { RoundTrip_ms = 80f, Jitter_ms = 15f, Loss_pct = 5f, Duplicate_pct = 2f }, seed: 9);
        rig.Run(30);
        rig.StartRound("teams", 900f, (0, 0, true, 10f, 0f), (1, 1, false, 0f, -5f));
        rig.GoLive();
        // The bot fires at the north wall, on and off, for five seconds.
        rig.BotScript = (t, p) => new InputCommand { Tick = t, Pitch = 0.05f, Buttons = t % 10 < 5 ? InputButtons.Fire : InputButtons.None };
        rig.Run(5 * Second);
        rig.BotScript = null;
        rig.Run(2 * Second);
        static IEnumerable<(SimEventType, uint)> Of(IEnumerable<SimEvent> events) =>
            events.Where(e => e.PlayerId == 1 && e.Type is SimEventType.BallBroke or SimEventType.BallBounced).Select(e => (e.Type, e.ShotSequence));
        var server = Of(rig.ServerEvents).ToList();
        var copy = Of(me.Events).ToList();
        _out.WriteLine($"{server.Count} breaks and bounces on the server, {copy.Count} on the copy");
        Assert.True(server.Count > 40);
        Assert.Equal(server, copy);
        Assert.Equal(rig.ServerEvents.Count(e => e.Type == SimEventType.ShotFired && e.PlayerId == 1),
            me.Events.Count(e => e.Type == SimEventType.ShotFired && e.PlayerId == 1));
    }

    /// <summary>
    /// You (a copy at <paramref name="lag"/>) shoot at a bot running across 20 m in front, leading it as you see it, a shot
    /// every quarter second for twenty seconds. Returns the share that hit it on the server. The bot can't be put out.
    /// </summary>
    private static float HitRate(LagSettings lag, bool compensate)
    {
        var rig = new NetRig();
        rig.Server.LagCompensation = compensate;
        RigClient me = rig.Join("Me", lag);
        rig.Run(30);
        rig.StartRound("teams", 900f, (0, 0, true, 0f, 0f), (1, 1, false, -6f, -20f));
        rig.GoLive();
        rig.Run(Second);
        // Back and forth across, at a run.
        rig.BotScript = (t, p) =>
        {
            float x = p.Position.X;
            float yaw = (t / 260) % 2 == 0 ? -MathF.PI / 2f : MathF.PI / 2f;
            return new InputCommand { Tick = t, Move = new Vector2(0f, 1f), Yaw = yaw, Pitch = 0f };
        };
        rig.AfterServerStep = sim =>
        {
            PlayerState bot = sim.FindPlayer(1)!;
            bot.Alive = true;
            bot.Present = true;
        };
        float speed = TestData.Config.Shot.MuzzleVelocity;
        me.Script = (t, p) =>
        {
            PlayerState target = me.Sim!.FindPlayer(1)!;
            Vector3 chest = target.Position + new Vector3(0f, 1.2f, 0f);
            Vector3 eye = p.EyePosition;
            float flight = Vector3.Distance(eye, chest) / (speed * 0.93f);
            Vector3 aim = chest + new Vector3(target.Velocity.X, 0f, target.Velocity.Z) * flight + new Vector3(0f, 0.5f * 9.81f * flight * flight, 0f);
            (float yaw, float pitch) = ViewAngles.FromDirection(aim - eye);
            return new InputCommand { Tick = t, Yaw = yaw, Pitch = pitch, Buttons = t % 30 == 0 ? InputButtons.Fire : InputButtons.None };
        };
        rig.Run(20 * Second);
        rig.Run(Second / 2);
        int shots = rig.ServerEvents.Count(e => e.Type == SimEventType.ShotFired && e.PlayerId == 0);
        int hits = rig.ServerEvents.Count(e => e.Type == SimEventType.BallBroke && e.PlayerId == 0 && e.TargetId == PlayerHitboxes.ReceiverIdBase + 1);
        return shots == 0 ? 0f : hits / (float)shots;
    }

    [Fact]
    public void Hits_count_where_the_shooter_saw_the_target_at_100_ms()
    {
        float near = HitRate(LagSettings.None, compensate: true);
        float far = HitRate(Ping100, compensate: true);
        float uncompensated = HitRate(Ping100, compensate: false);
        _out.WriteLine($"hit rate leading a runner at 20 m: {near:P0} next to the server, {far:P0} at 100 ms, {uncompensated:P0} at 100 ms without lag compensation");
        Assert.True(near > 0.3f, $"the shots should often hit next to the server ({near:P0})");
        Assert.True(MathF.Abs(far - near) <= 0.05f, $"at 100 ms {far:P0} against {near:P0}");
        Assert.True(uncompensated < near - 0.2f, $"without lag compensation {uncompensated:P0}");
    }

    [Fact]
    public void The_server_holds_a_cheat_to_the_rules()
    {
        var rig = new NetRig();
        RigClient cheat = rig.Join("Cheat", Ping100);
        rig.Run(30);
        rig.StartRound("teams", 900f, (0, 0, true, 0f, 0f), (1, 1, false, 30f, -25f));
        rig.GoLive();
        // Two commands a tick, sprinting forward with the trigger flipped on every one.
        int seq = 0;
        cheat.Raw = c =>
        {
            for (int k = 0; k < 2; k++, seq++)
            {
                var cmd = new InputCommand { Tick = seq, Move = new Vector2(0f, 1f), Yaw = MathF.PI, Buttons = InputButtons.Sprint | (seq % 2 == 0 ? InputButtons.Fire : 0) };
                c.Net.SendCommand(seq, CommandCodec.Quantize(cmd), (int)c.Net.RenderTick);
            }

            return true;
        };
        rig.Run(Second);
        PlayerState player = rig.Sim!.FindPlayer(0)!;
        Vector3 from = player.Position;
        int shotsFrom = rig.ServerEvents.Count(e => e.Type == SimEventType.ShotFired && e.PlayerId == 0);
        rig.Run(5 * Second);
        float distance = Vector3.Distance(from, player.Position);
        int shots = rig.ServerEvents.Count(e => e.Type == SimEventType.ShotFired && e.PlayerId == 0) - shotsFrom;
        ClientLink link = rig.Server.Clients.Single();
        _out.WriteLine($"in 5 s the cheat moved {distance:0.0} m and fired {shots} (merged {link.Commands.Merged})");
        Assert.True(distance <= TestData.Config.Movement.SprintSpeed * 5f + 0.5f, $"moved {distance:0.0} m in 5 s");
        Assert.True(link.Commands.Merged > 0);

        // A command a tick again, the trigger flipped on every one, standing still: held to the rate cap. (Its doubled
        // commands were run two as one, so their flips merged into a held trigger; it waits for the queue to drain.)
        cheat.Raw = c =>
        {
            var cmd = new InputCommand { Tick = seq, Yaw = MathF.PI, Pitch = 0.05f, Buttons = seq % 2 == 0 ? InputButtons.Fire : InputButtons.None };
            c.Net.SendCommand(seq, CommandCodec.Quantize(cmd), (int)c.Net.RenderTick);
            seq++;
            return true;
        };
        rig.Run(3 * Second);
        shotsFrom = rig.ServerEvents.Count(e => e.Type == SimEventType.ShotFired && e.PlayerId == 0);
        rig.Run(5 * Second);
        shots = rig.ServerEvents.Count(e => e.Type == SimEventType.ShotFired && e.PlayerId == 0) - shotsFrom;
        _out.WriteLine($"flipping the trigger every command, it fired {shots} in 5 s");
        Assert.True(shots <= TestData.Config.Fire.RateCap * 5f + 1, $"{shots} shots in 5 s");
        Assert.True(shots > 20);
    }

    [Fact]
    public void Every_copy_ends_the_round_with_the_servers_result_and_numbers()
    {
        var rig = new NetRig();
        RigClient a = rig.Join("A", Ping100);
        RigClient b = rig.Join("B", new LagSettings { RoundTrip_ms = 60f, Jitter_ms = 10f, Loss_pct = 2f });
        rig.Run(30);
        rig.StartRound("teams", 4f, (0, 0, true, 0f, 0f), (1, 1, true, 5f, -5f), (2, 1, false, -5f, -5f));
        rig.GoLive();
        a.Script = (t, p) => new InputCommand { Tick = t, Pitch = 0.05f, Buttons = t % 20 < 10 ? InputButtons.Fire : 0 };
        b.Script = (t, p) => new InputCommand { Tick = t, Yaw = 0.3f, Pitch = 0.05f, Buttons = t % 40 < 20 ? InputButtons.Fire : 0 };
        rig.Run(6 * Second);
        MatchState server = rig.Sim!.Match!;
        Assert.Equal(MatchPhase.Ended, server.Phase);
        foreach (RigClient c in new[] { a, b })
        {
            RoundOverMessage over = c.Net.TakeRoundOver()!;
            Assert.NotNull(over);
            Assert.Equal(server.Result, over.Result);
            Assert.Equal(server.Stats.Select(s => (s.PlayerId, s.Shots, s.Hits, s.Eliminations)),
                over.Stats.Select(s => (s.PlayerId, s.Shots, s.Hits, s.Eliminations)));
            Assert.Equal(MatchPhase.Ended, c.Sim!.Match!.Phase);
            Assert.Equal(server.Result, c.Sim.Match.Result);
        }

        Assert.True(server.StatsFor(0)!.Shots > 5 && server.StatsFor(1)!.Shots > 5);
    }

    [Fact]
    public void Someone_who_goes_quiet_in_a_round_is_dropped_and_counts_as_out_but_not_while_building_it()
    {
        var rig = new NetRig();
        RigClient a = rig.Join("A");
        RigClient b = rig.Join("B");
        rig.Run(30);
        rig.Server.Left += link => rig.Sim?.Withdraw(link.PlayerId);

        // B takes its time building the round: nothing comes from it for twice the drop time, and it stays in.
        b.Silent = true;
        rig.StartRound("teams", 900f, (0, 0, true, 0f, 0f), (1, 1, true, 5f, -20f), (2, 1, false, -5f, -20f));
        rig.Run((int)(rig.Settings.DropAfter * 2f * Second));
        Assert.Equal(2, rig.Server.Clients.Count(c => c.Welcomed));
        b.Silent = false;
        rig.GoLive();
        rig.Run(Second);

        // Then it hangs mid-round: dropped once the drop time has passed, and out, put out by nobody.
        b.Silent = true;
        rig.Run((int)((rig.Settings.DropAfter + 0.5f) * Second));
        Assert.Single(rig.Server.Clients, c => c.Welcomed);
        PlayerState gone = rig.Sim!.FindPlayer(1)!;
        Assert.False(gone.Alive);
        Assert.False(gone.Present);
        Assert.Contains(a.Events, e => e.Type == SimEventType.PlayerEliminated && e.TargetId == 1 && e.PlayerId == -1 && e.Extra == -1);
        Assert.False(a.Sim!.FindPlayer(1)!.Present);
    }

    [Fact]
    public void Once_out_your_copy_shows_you_where_the_server_walks_you()
    {
        var rig = new NetRig();
        RigClient a = rig.Join("A", Ping100);
        rig.Run(30);
        rig.StartRound("teams", 900f, (0, 0, false, 0f, 0f), (1, 1, true, 5f, -20f));
        rig.GoLive();
        rig.Run(Second);

        // The server puts A out and walks them off (the host's walk-off pilot; here, a centimetre a tick).
        PlayerState onServer = rig.Sim!.FindPlayer(1)!;
        onServer.Alive = false;
        rig.AfterServerStep = sim => onServer.Position += new Vector3(0.01f, 0f, 0f);
        rig.Run(Second / 4);
        int corrections = a.Session!.Corrections;
        rig.Run(Second);
        PlayerState me = a.Session.Local!;
        Assert.True(a.Session.Following);
        Assert.False(me.Alive);
        Assert.Equal(corrections, a.Session.Corrections);
        float behind = Vector3.Distance(me.Position, onServer.Position);
        Assert.True(behind < 0.2f, $"{behind * 100f:0} cm behind the server");
    }

    [Fact]
    public void Network_ticks_allocate_nothing()
    {
        var rig = new NetRig { KeepEvents = false };
        RigClient a = rig.Join("A", new LagSettings { RoundTrip_ms = 100f, Jitter_ms = 10f, Loss_pct = 1f });
        RigClient b = rig.Join("B");
        a.KeepEvents = b.KeepEvents = false;
        rig.Run(30);
        rig.StartRound("teams", 900f, (0, 0, true, 0f, 0f), (1, 1, true, 5f, -10f), (2, 1, false, -5f, -10f));
        rig.GoLive();
        a.Script = Busy;
        b.Script = Busy;
        rig.BotScript = (t, p) => new InputCommand { Tick = t, Move = new Vector2(0f, 1f), Yaw = t * 0.01f, Buttons = t % 20 < 10 ? InputButtons.Fire : 0 };
        rig.Run(4 * Second);
        long bytes = Allocations.During(() => rig.Run(Second));
        _out.WriteLine($"{bytes} bytes allocated in a second of network ticks");
        Assert.Equal(0, bytes);
    }
}
