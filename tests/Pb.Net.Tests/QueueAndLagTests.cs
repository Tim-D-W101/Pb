using Pb.Net.Server;
using Pb.Net.Transport;
using Pb.Sim.Players;
using Pb.Sim.Tests;

namespace Pb.Net.Tests;

/// <summary>Phase 4 (M4.2): the pretend bad connection, and the server's queue of each player's commands.</summary>
public class QueueAndLagTests
{
    private static NetSettings Settings => NetSettings.Load(TestData.Source);

    private static InputCommand Press(int seq) => new() { Tick = seq, Yaw = seq * 0.01f, Buttons = seq % 2 == 0 ? InputButtons.Fire : InputButtons.None };

    [Fact]
    public void A_lagged_connection_delays_each_way_and_loses_only_unreliable_packets()
    {
        double now = 0;
        var net = new LoopbackNetwork();
        ITransport server = net.Listen();
        var client = new LaggedTransport(net.Connect(), new LagSettings { RoundTrip_ms = 100f, Loss_pct = 10f }, () => now, 3);
        Assert.True(server.Poll(out TransportEvent connected) && connected.Kind == TransportEventKind.Connected);

        for (int i = 0; i < 1000; i++)
        {
            client.Send(0, NetChannel.Unreliable, new[] { (byte)(i & 0xFF) });
            client.Send(0, NetChannel.Reliable, BitConverter.GetBytes(i));
        }

        // Nothing for 50 ms: half the round trip.
        now = 0.049;
        client.Flush();
        Assert.False(server.Poll(out _));
        now = 0.051;
        client.Flush();
        int unreliable = 0;
        var reliable = new List<int>();
        while (server.Poll(out TransportEvent e))
        {
            if (e.Channel == NetChannel.Unreliable)
            {
                unreliable++;
            }
            else
            {
                reliable.Add(BitConverter.ToInt32(e.Data.Span));
            }
        }

        Assert.Equal(Enumerable.Range(0, 1000), reliable);
        Assert.InRange(unreliable, 850, 950);

        // And on the way back too.
        server.Send(1, NetChannel.Reliable, new byte[] { 7 });
        now = 0.1;
        Assert.False(client.Poll(out TransportEvent early) && early.Kind == TransportEventKind.Data);
        now = 0.152;
        bool got = false;
        while (client.Poll(out TransportEvent e))
        {
            got |= e.Kind == TransportEventKind.Data && e.Data.Span[0] == 7;
        }

        Assert.True(got);
    }

    [Fact]
    public void Jitter_puts_unreliable_packets_out_of_order_but_never_reliable_ones()
    {
        double now = 0;
        var net = new LoopbackNetwork();
        ITransport server = net.Listen();
        var client = new LaggedTransport(net.Connect(), new LagSettings { RoundTrip_ms = 60f, Jitter_ms = 20f }, () => now, 5);
        server.Poll(out _);
        for (int i = 0; i < 200; i++)
        {
            client.Send(0, NetChannel.Unreliable, BitConverter.GetBytes(i));
            client.Send(0, NetChannel.Reliable, BitConverter.GetBytes(i));
            now += 0.002;
        }

        now += 1;
        client.Flush();
        var unreliable = new List<int>();
        var reliable = new List<int>();
        while (server.Poll(out TransportEvent e))
        {
            (e.Channel == NetChannel.Unreliable ? unreliable : reliable).Add(BitConverter.ToInt32(e.Data.Span));
        }

        Assert.Equal(Enumerable.Range(0, 200), reliable);
        Assert.Equal(200, unreliable.Count);
        Assert.NotEqual(Enumerable.Range(0, 200), unreliable);
    }

    [Fact]
    public void Commands_fill_a_little_then_run_one_a_tick_in_order()
    {
        var q = new CommandQueue(Settings, NetRig.Dt);
        Assert.False(q.TryNext(out _, out _));
        q.Receive(10, Press(10), 3);
        Assert.False(q.TryNext(out _, out _), "it waits to fill to its depth");
        q.Receive(11, Press(11), 4);
        q.Receive(12, Press(12), 5);
        // The packets repeat commands: a repeat changes nothing.
        q.Receive(11, Press(11), 4);
        for (int seq = 10; seq <= 12; seq++)
        {
            Assert.True(q.TryNext(out InputCommand c, out int view));
            Assert.Equal(Press(seq).Yaw, c.Yaw);
            Assert.Equal(seq - 7, view);
            Assert.Equal(seq, q.LastRun);
        }

        Assert.Equal(0, q.Late);
        Assert.Equal(0, q.Missing);
    }

    /// <summary>A queue with its first <see cref="NetSettings.CommandQueueTicks"/> commands in, all run: the next due is that many.</summary>
    private static (CommandQueue Queue, int Next) Started(Func<int, InputCommand>? command = null)
    {
        NetSettings settings = Settings;
        var q = new CommandQueue(settings, NetRig.Dt);
        int depth = Math.Max(1, settings.CommandQueueTicks);
        for (int seq = 0; seq < depth; seq++)
        {
            q.Receive(seq, (command ?? Press)(seq), 0);
        }

        for (int seq = 0; seq < depth; seq++)
        {
            Assert.True(q.TryNext(out _, out _));
        }

        return (q, depth);
    }

    [Fact]
    public void A_command_that_isnt_there_in_time_is_replaced_by_the_last_and_dropped_when_it_comes()
    {
        (CommandQueue q, int next) = Started();
        // The next hasn't come: the last again in its place.
        Assert.True(q.TryNext(out InputCommand c, out _));
        Assert.Equal(Press(next - 1).Yaw, c.Yaw);
        Assert.Equal(next, q.LastRun);
        Assert.Equal(1, q.Missing);
        q.Receive(next, Press(next), 0);
        Assert.Equal(1, q.Late);
        q.Receive(next + 1, Press(next + 1), 0);
        Assert.True(q.TryNext(out c, out _));
        Assert.Equal(Press(next + 1).Yaw, c.Yaw);
    }

    [Fact]
    public void Sending_faster_than_time_gets_no_more_moves()
    {
        NetSettings settings = Settings;
        var q = new CommandQueue(settings, NetRig.Dt);
        int sent = 0, run = 0;
        // Two commands a tick for five seconds: the extra ones only make the queue (their own delay) longer.
        for (int tick = 0; tick < 600; tick++)
        {
            q.Receive(sent, Press(sent), 0);
            sent++;
            q.Receive(sent, Press(sent), 0);
            sent++;
            run += q.TryNext(out _, out _) ? 1 : 0;
        }

        Assert.True(run <= 600, "never more than a command a tick");
        Assert.True(q.Merged > 100, "a queue that stays long runs two as one, every tick");
        Assert.Equal(0, q.TooFarAhead);

        // A command beyond the queue's reach: it skips on to it (the ones in between never run) and runs it in its turn.
        int far = q.LastRun + settings.CommandAheadTicks + 50;
        q.Receive(far, Press(far), 0);
        Assert.Equal(1, q.TooFarAhead);
        Assert.True(q.Skipped > 0);
        for (int i = 0; i < Math.Max(1, settings.CommandQueueTicks); i++)
        {
            Assert.True(q.TryNext(out _, out _));
        }

        Assert.Equal(far, q.LastRun);
    }

    [Fact]
    public void After_a_stall_longer_than_the_queue_reaches_the_player_is_back_at_once_not_shut_out()
    {
        NetSettings settings = Settings;
        var q = new CommandQueue(settings, NetRig.Dt);
        int sent = 0;
        void Send()
        {
            q.Receive(sent, Press(sent), 0);
            sent++;
        }

        // A second of play, a command a tick; then the host stalls for four seconds (no ticks run there) while the copy
        // goes on sending.
        for (int tick = 0; tick < 120; tick++)
        {
            Send();
            q.TryNext(out _, out _);
        }

        for (int tick = 0; tick < 480; tick++)
        {
            Send();
        }

        // Then both go on: the queue is already running the copy's latest commands, never more than one a tick.
        int run = 0, missing = q.Missing;
        for (int tick = 0; tick < 120; tick++)
        {
            Send();
            run += q.TryNext(out _, out _) ? 1 : 0;
        }

        Assert.Equal(120, run);
        Assert.True(q.TooFarAhead >= 1);
        Assert.Equal(missing, q.Missing);
        Assert.True(sent - 1 - q.LastRun <= settings.CommandQueueMostTicks, $"{sent - 1 - q.LastRun} commands behind");
    }

    [Fact]
    public void A_copy_running_twice_as_fast_as_a_struggling_host_gains_nothing_and_still_plays()
    {
        // The host manages four ticks in five (a busy computer) while the copy sends two commands a tick: running two in a
        // tick can't keep up, so now and then the queue skips on. The player's commands keep running, one a tick at most.
        NetSettings settings = Settings;
        var q = new CommandQueue(settings, NetRig.Dt);
        int sent = 0, run = 0, ticks = 0;
        for (int step = 0; step < 2400; step++)
        {
            for (int k = 0; k < 2; k++)
            {
                q.Receive(sent, Press(sent), 0);
                sent++;
            }

            if (step % 5 != 4)
            {
                ticks++;
                run += q.TryNext(out _, out _) ? 1 : 0;
            }
        }

        Assert.True(run <= ticks, "never more than a command a tick");
        Assert.True(q.Merged > 100);
        Assert.True(q.TooFarAhead >= 1);
        Assert.True(q.Missing <= Math.Max(1, settings.CommandQueueTicks), $"{q.Missing} of {ticks} ticks without a command");
        Assert.True(sent - 1 - q.LastRun <= settings.CommandAheadTicks, $"{sent - 1 - q.LastRun} commands behind");
    }

    [Fact]
    public void A_copy_whose_clock_runs_fast_is_kept_to_its_depth()
    {
        NetSettings settings = Settings;
        var q = new CommandQueue(settings, NetRig.Dt);
        int sent = 0;
        // One command more every hundred ticks, for twenty seconds.
        for (int tick = 0; tick < 2400; tick++)
        {
            int count = tick % 100 == 99 ? 2 : 1;
            for (int k = 0; k < count; k++)
            {
                q.Receive(sent, Press(sent), 0);
                sent++;
            }

            q.TryNext(out _, out _);
        }

        Assert.True(q.Merged >= 1);
        Assert.True(q.Depth <= settings.CommandQueueMostTicks + 1, $"{q.Depth} waiting");
    }

    [Fact]
    public void A_player_whose_commands_stop_coming_soon_stands_still()
    {
        NetSettings settings = Settings;
        var running = new InputCommand { Move = new System.Numerics.Vector2(0f, 1f), Yaw = 1f, Buttons = InputButtons.Sprint };
        (CommandQueue q, _) = Started(_ => running);
        int repeats = 0;
        for (int tick = 0; tick < 60; tick++)
        {
            Assert.True(q.TryNext(out InputCommand c, out _));
            Assert.Equal(1f, c.Yaw);
            repeats += c.Move.Y > 0f ? 1 : 0;
        }

        Assert.InRange(repeats, 1, (int)MathF.Ceiling(settings.RepeatMissing / NetRig.Dt) + 1);
    }

    [Fact]
    public void Commands_that_keep_coming_late_make_it_wait_for_them()
    {
        (CommandQueue q, int next) = Started();
        int lateBefore = 0;
        // From here the player's copy runs behind what the queue needs: each command comes as its turn has gone.
        for (int tick = 0; tick < 900; tick++)
        {
            q.TryNext(out _, out _);
            int seq = next + tick - 1;
            if (seq >= next)
            {
                q.Receive(seq, Press(seq), 0);
            }

            if (tick == 700)
            {
                lateBefore = q.Late;
            }
        }

        Assert.True(q.Waited > 0, "late commands should make it wait for them");
        Assert.Equal(lateBefore, q.Late);
    }
}
