using System.Numerics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;

namespace Pb.Sim.Tests;

/// <summary>Phase 4 (M4.1): rounds with several people, results told from each side, lag compensation and a joining copy's step.</summary>
public class SeveralPlayersTests
{
    private const int Second = 120;

    private static SimConfig Config => TestData.Config;

    /// <summary>Open ground with the players given as (id, team, x, z), all facing −Z.</summary>
    private static SimWorld Ground(params (int Id, byte Team, float X, float Z)[] players)
    {
        var sim = new SimWorld(Config);
        sim.Collision.Add(new PlaneShape(Vector3.UnitY, 0f), Config.Surfaces.Get("turf"), "ground");
        foreach ((int id, byte team, float x, float z) in players)
        {
            sim.AddPlayer(id, team, new Vector3(x, 0f, z), 0f);
        }

        return sim;
    }

    private static MatchSetup Setup(MatchModeKind mode, int[] people, bool endWhenPeopleOut = false,
        ObjectiveKind objective = ObjectiveKind.Eliminate, byte? attackers = null, float timeLimit = 900f) => new()
    {
        People = people, Mode = mode, TimeLimit = timeLimit, StartPods = 3, BotPods = 1, Pickups = false,
        EndWhenPeopleOut = endWhenPeopleOut, Objective = objective, Attackers = attackers,
    };

    /// <summary>Steps the sim with one command per player (missing ones idle) and collects the events.</summary>
    private static List<SimEvent> Run(SimWorld sim, int ticks, Func<int, PlayerState, InputCommand>? command = null, Action<int>? before = null)
    {
        var events = new List<SimEvent>();
        var commands = new InputCommand[sim.Players.Count];
        for (int t = 0; t < ticks; t++)
        {
            before?.Invoke(t);
            for (int i = 0; i < commands.Length; i++)
            {
                commands[i] = command?.Invoke(t, sim.Players[i]) ?? default;
            }

            sim.Step(commands);
            events.AddRange(sim.Events.Items.ToArray());
            sim.Events.Clear();
        }

        return events;
    }

    private static void Out(SimWorld sim, int id) => sim.FindPlayer(id)!.Alive = false;

    private static int PodsOf(SimWorld sim, int id) => sim.FindPlayer(id)!.Marker.Paint.PodsRemaining;

    [Fact]
    public void Co_op_puts_everyone_who_joined_on_one_side_to_win_or_lose_together()
    {
        SimWorld sim = Ground((0, 0, 0f, 0f), (1, 0, 3f, 0f), (2, 1, 0f, -30f), (3, 1, 3f, -30f));
        MatchState match = sim.StartMatch(Setup(MatchModeKind.Solo, new[] { 0, 1 }));
        sim.GoLive();
        int pod = Config.Loader.PodCapacity;
        Assert.Equal(3 * pod, PodsOf(sim, 0));
        Assert.Equal(3 * pod, PodsOf(sim, 1));
        Assert.Equal(1 * pod, PodsOf(sim, 2));

        // One of you out: the other plays on.
        Out(sim, 0);
        Run(sim, 2);
        Assert.Equal(MatchPhase.Live, match.Phase);

        Out(sim, 2);
        Out(sim, 3);
        Run(sim, 2);
        Assert.Equal(MatchPhase.Ended, match.Phase);
        Assert.Equal(new MatchResult(RoundEnd.LastStanding, 0), match.Result);
        Assert.Equal(RoundOutcome.Cleared, match.OutcomeFor(0));
        Assert.Equal(RoundOutcome.Cleared, match.Outcome);
        Assert.Equal(RoundOutcome.Eliminated, match.OutcomeFor(1));
    }

    [Fact]
    public void Player_against_player_tells_each_side_its_own_result()
    {
        // People 0 and 1 on opposite teams, a bot on each.
        SimWorld sim = Ground((0, 0, 0f, 0f), (1, 1, 0f, -30f), (2, 0, 3f, 0f), (3, 1, 3f, -30f));
        MatchState match = sim.StartMatch(Setup(MatchModeKind.Teams, new[] { 0, 1 }));
        sim.GoLive();
        Assert.Equal(PodsOf(sim, 0), PodsOf(sim, 1));
        Assert.True(PodsOf(sim, 1) > PodsOf(sim, 3));

        Out(sim, 1);
        Run(sim, 2);
        Assert.Equal(MatchPhase.Live, match.Phase);
        Out(sim, 3);
        List<SimEvent> events = Run(sim, 2);
        SimEvent ended = Assert.Single(events, e => e.Type == SimEventType.RoundEnded);
        Assert.Equal((int)RoundEnd.LastStanding, ended.Extra);
        Assert.Equal(0, ended.TargetId);
        Assert.Equal(RoundOutcome.Cleared, match.OutcomeFor(0));
        Assert.Equal(RoundOutcome.Eliminated, match.OutcomeFor(1));
        Assert.True(match.OutcomeFor(0).IsWin());
        Assert.False(match.OutcomeFor(1).IsWin());
    }

    [Fact]
    public void Free_for_all_with_several_people_plays_on_to_the_last_one_standing()
    {
        SimWorld sim = Ground((0, 0, 0f, 0f), (1, 1, 20f, 0f), (2, 2, 0f, -20f), (3, 3, 20f, -20f));
        MatchState match = sim.StartMatch(Setup(MatchModeKind.FreeForAll, new[] { 0, 1, 2 }));
        sim.GoLive();
        Out(sim, 0);
        Run(sim, 2);
        Out(sim, 3);
        Run(sim, 2);
        Assert.Equal(MatchPhase.Live, match.Phase);
        Out(sim, 1);
        Run(sim, 2);
        Assert.Equal(new MatchResult(RoundEnd.LastStanding, 2), match.Result);
        Assert.Equal(RoundOutcome.Cleared, match.OutcomeFor(2));
        Assert.Equal(RoundOutcome.Eliminated, match.OutcomeFor(0));
        Assert.Equal(RoundOutcome.Eliminated, match.OutcomeFor(1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Online_a_round_ends_once_no_person_is_still_in(bool endWhenPeopleOut)
    {
        // You and a bot teammate against two bots: you go out, and only bots are left fighting.
        SimWorld sim = Ground((0, 0, 0f, 0f), (1, 0, 3f, 0f), (2, 1, 0f, -30f), (3, 1, 3f, -30f));
        MatchState match = sim.StartMatch(Setup(MatchModeKind.Teams, new[] { 0 }, endWhenPeopleOut));
        sim.GoLive();
        Out(sim, 0);
        Run(sim, 2);
        if (!endWhenPeopleOut)
        {
            Assert.Equal(MatchPhase.Live, match.Phase);
            return;
        }

        // The side with more still in wins.
        Assert.Equal(new MatchResult(RoundEnd.PeopleOut, 1), match.Result);
        Assert.Equal(RoundOutcome.Eliminated, match.OutcomeFor(0));
        Assert.Equal(RoundOutcome.Cleared, match.OutcomeFor(1));
    }

    [Fact]
    public void With_nobody_left_to_play_for_an_even_round_has_no_winner()
    {
        SimWorld sim = Ground((0, 0, 0f, 0f), (1, 0, 3f, 0f), (2, 1, 0f, -30f), (3, 1, 3f, -30f));
        MatchState match = sim.StartMatch(Setup(MatchModeKind.Teams, new[] { 0, 2 }, endWhenPeopleOut: true));
        sim.GoLive();
        Out(sim, 0);
        Out(sim, 2);
        Run(sim, 2);
        Assert.Equal(new MatchResult(RoundEnd.PeopleOut, -1), match.Result);
        Assert.Equal(RoundOutcome.Eliminated, match.OutcomeFor(0));
        Assert.Equal(RoundOutcome.Eliminated, match.OutcomeFor(1));
    }

    /// <summary>A live round on Oxbarrow Works: person 0 (team 0) outside the gate, person 1 (team 1) in the yard.</summary>
    private static SimWorld Oxbarrow(MatchSetup setup)
    {
        var sim = new SimWorld(Config, 1);
        sim.LoadLevel(TestData.Data.Levels["oxbarrow_works"]);
        sim.AddPlayer(0, 0, new Vector3(8f, 0f, 45f), 0f);
        sim.AddPlayer(1, 1, new Vector3(45f, 0f, 20f), 0f);
        sim.StartMatch(setup);
        sim.GoLive();
        return sim;
    }

    [Fact]
    public void The_side_the_setup_names_attacks_the_objective()
    {
        SimWorld sim = Oxbarrow(Setup(MatchModeKind.Teams, new[] { 0, 1 }, objective: ObjectiveKind.Hold, attackers: 1));
        MatchState match = sim.Match!;
        ObjectiveState objective = match.Objective!;
        Assert.Equal(1, match.Attackers);
        Assert.Equal(1, objective.Attackers);

        // Person 0 is in the room alone: it isn't theirs to hold.
        sim.FindPlayer(0)!.Position = objective.Room!.Centre;
        Run(sim, 5 * Second);
        Assert.Equal(HoldStatus.Theirs, objective.Status);
        Assert.Equal(0f, objective.Held);

        sim.FindPlayer(0)!.Position = new Vector3(8f, 0f, 45f);
        sim.FindPlayer(1)!.Position = objective.Room.Centre;
        Run(sim, (int)(objective.HoldRules.HoldTime * Second) + 2);
        Assert.Equal(new MatchResult(RoundEnd.Held, 1), match.Result);
        Assert.Equal(RoundOutcome.Held, match.OutcomeFor(1));
        Assert.Equal(RoundOutcome.RoomLost, match.OutcomeFor(0));
    }

    [Fact]
    public void Defenders_who_last_out_the_clock_hold_the_attackers_off()
    {
        SimWorld sim = Oxbarrow(Setup(MatchModeKind.Teams, new[] { 0, 1 }, objective: ObjectiveKind.Retrieve, timeLimit: 1f));
        Run(sim, Second + 2);
        MatchState match = sim.Match!;
        Assert.Equal(new MatchResult(RoundEnd.TimeUp, 1), match.Result);
        Assert.Equal(RoundOutcome.TimeUp, match.OutcomeFor(0));
        Assert.Equal(RoundOutcome.HeldOff, match.OutcomeFor(1));
        Assert.True(RoundOutcome.HeldOff.IsWin());

        // Without an objective, time up wins nobody anything.
        SimWorld plain = Oxbarrow(Setup(MatchModeKind.Teams, new[] { 0, 1 }, timeLimit: 1f));
        Run(plain, Second + 2);
        Assert.Equal(new MatchResult(RoundEnd.TimeUp, -1), plain.Match!.Result);
        Assert.Equal(RoundOutcome.TimeUp, plain.Match.OutcomeFor(0));
        Assert.Equal(RoundOutcome.TimeUp, plain.Match.OutcomeFor(1));
    }

    [Fact]
    public void Retrieve_tells_the_defenders_they_lost_the_case()
    {
        SimWorld sim = Oxbarrow(Setup(MatchModeKind.Teams, new[] { 0, 1 }, objective: ObjectiveKind.Retrieve));
        MatchState match = sim.Match!;
        ObjectiveState objective = match.Objective!;
        PlayerState carrier = sim.FindPlayer(0)!;
        carrier.Position = objective.CasePosition;
        Run(sim, 2);
        Assert.Equal(0, objective.Carrier);
        carrier.Position = objective.Level.Exits[0].Position;
        Run(sim, 2);
        Assert.Equal(new MatchResult(RoundEnd.Extracted, 0), match.Result);
        Assert.Equal(RoundOutcome.Extracted, match.OutcomeFor(0));
        Assert.Equal(RoundOutcome.CaseLost, match.OutcomeFor(1));
    }

    /// <summary>
    /// The shooter (id 0) at the origin, the target (id 1) 10 m ahead until tick 30, then 2 m to the side. On tick 32 the
    /// shooter fires at where the target was, as a shooter seeing 20 ticks behind would, with <paramref name="rewind"/>.
    /// </summary>
    private static (bool Hit, List<SimEvent> Events) ShootWhereItWas(byte rewind)
    {
        SimWorld sim = Ground((0, 0, 0f, 0f), (1, 1, 0f, -10f));
        PlayerState shooter = sim.FindPlayer(0)!;
        PlayerState target = sim.FindPlayer(1)!;
        Vector3 seen = target.Position;
        (float yaw, float pitch) = ViewAngles.FromDirection(seen + new Vector3(0f, 1.15f, 0f) - shooter.EyePosition);
        List<SimEvent> events = Run(sim, 80,
            (t, p) => p == shooter
                ? new InputCommand { Yaw = yaw, Pitch = pitch, Rewind = rewind, Buttons = t == 32 ? InputButtons.Fire : InputButtons.None }
                : new InputCommand { Yaw = MathF.PI },
            before: t =>
            {
                if (t == 30)
                {
                    target.Position = seen + new Vector3(2f, 0f, 0f);
                }
            });
        return (events.Any(e => e.Type == SimEventType.PlayerEliminated && e.TargetId == 1), events);
    }

    [Fact]
    public void A_shot_is_checked_against_where_its_shooter_saw_the_others()
    {
        (bool rewound, List<SimEvent> events) = ShootWhereItWas(20);
        Assert.Contains(events, e => e.Type == SimEventType.ShotFired && e.PlayerId == 0);
        Assert.True(rewound, "fired at where the shooter saw it, the target should be hit");

        // Without lag compensation the same shot finds the target gone.
        (bool now, _) = ShootWhereItWas(0);
        Assert.False(now, "fired at where the target no longer is, nothing should be hit");

        // Further back than the history keeps is the history's limit (23 ticks), which still sees the target there.
        (bool capped, _) = ShootWhereItWas(255);
        Assert.True(capped);
    }

    [Fact]
    public void A_joining_copy_runs_only_its_own_marker_and_never_puts_anyone_out()
    {
        SimWorld sim = Ground((0, 0, 0f, 0f), (1, 1, 0f, -10f));
        sim.Role = SimRole.Client;
        sim.LocalPlayerId = 0;
        PlayerState you = sim.FindPlayer(0)!;
        PlayerState them = sim.FindPlayer(1)!;
        (float yaw, float pitch) = ViewAngles.FromDirection(them.Position + new Vector3(0f, 1.15f, 0f) - you.EyePosition);
        (float backYaw, float backPitch) = ViewAngles.FromDirection(you.Position + new Vector3(0f, 1.15f, 0f) - them.EyePosition);
        List<SimEvent> events = Run(sim, 60, (t, p) => p == you
            ? new InputCommand { Yaw = yaw, Pitch = pitch, Buttons = t == 5 ? InputButtons.Fire : InputButtons.None }
            : new InputCommand { Yaw = backYaw, Pitch = backPitch, Buttons = t == 5 ? InputButtons.Fire : InputButtons.None });

        // Their trigger is the server's business; your ball breaks on them, and only the server could put them out.
        Assert.DoesNotContain(events, e => e.Type == SimEventType.ShotFired && e.PlayerId == 1);
        Assert.Contains(events, e => e.Type == SimEventType.ShotFired && e.PlayerId == 0);
        Assert.Contains(events, e => e.Type == SimEventType.BallBroke && e.TargetId == PlayerHitboxes.ReceiverIdOf(them));
        Assert.DoesNotContain(events, e => e.Type == SimEventType.PlayerEliminated);
        Assert.True(them.Alive);
    }

    [Fact]
    public void A_ball_flown_for_show_passes_through_players()
    {
        // Someone the server says fired (id 2) from beside you, through a player 10 m ahead, at a wall 20 m ahead.
        SimWorld sim = Ground((0, 0, 5f, 0f), (1, 1, 0f, -10f), (2, 2, 0f, 0f));
        Collider wall = sim.Collision.Add(new BoxShape(new Vector3(0f, 1.5f, -20.5f), Quaternion.Identity, new Vector3(3f, 1.5f, 0.5f)),
            Config.Surfaces.Get("concrete"), "wall");
        sim.Role = SimRole.Client;
        sim.LocalPlayerId = 0;
        Assert.True(sim.SpawnRemoteShot(2, 0, 2, new Vector3(0f, 1.15f, 0f), new Vector3(0f, 0f, -88f), Config.Dt, 3));
        List<SimEvent> events = Run(sim, 60);
        Assert.DoesNotContain(events, e => e.Type is SimEventType.BallBroke or SimEventType.BallBounced && PlayerHitboxes.IsPlayer(e.TargetId));
        Assert.Contains(events, e => e.Type is SimEventType.BallBroke or SimEventType.BallBounced && e.ColliderId == wall.Id);
    }

    [Fact]
    public void A_ball_flown_for_show_flies_and_breaks_as_the_servers_does()
    {
        // The same wall and ground on a server and on a joining copy; the server's shot glances off the ground first.
        static SimWorld World(SimRole role)
        {
            SimWorld sim = Ground((0, 0, 0f, 0f), (1, 1, 30f, 0f));
            sim.Collision.Add(new BoxShape(new Vector3(0f, 1.5f, -25.5f), Quaternion.Identity, new Vector3(8f, 1.5f, 0.5f)),
                Config.Surfaces.Get("concrete"), "wall");
            sim.Role = role;
            sim.LocalPlayerId = 1;
            return sim;
        }

        SimWorld server = World(SimRole.Authority);
        SimWorld client = World(SimRole.Client);
        var serverBalls = new List<Vector3>();
        var clientBalls = new List<Vector3>();
        var serverEnds = new List<(SimEventType, int, Vector3)>();
        var clientEnds = new List<(SimEventType, int, Vector3)>();
        var commands = new InputCommand[2];
        var idle = new InputCommand[2];
        for (int t = 0; t < 90; t++)
        {
            commands[0] = new InputCommand { Pitch = -0.08f, Buttons = t == 3 ? InputButtons.Fire : InputButtons.None };
            server.Step(commands);
            foreach (SimEvent e in server.Events.Items)
            {
                if (e.Type == SimEventType.ShotFired)
                {
                    // What the server sends: the copy flies it from the start of its own step for that tick.
                    client.SpawnRemoteShot(e.PlayerId, e.ShotSequence, e.Team, e.Position, e.Velocity, e.Value, e.Extra);
                }
                else if (e.Type is SimEventType.BallBounced or SimEventType.BallBroke or SimEventType.BallDespawned)
                {
                    serverEnds.Add((e.Type, e.Tick, e.Position));
                }
            }

            server.Events.Clear();
            client.Step(idle);
            foreach (SimEvent e in client.Events.Items)
            {
                if (e.Type is SimEventType.BallBounced or SimEventType.BallBroke or SimEventType.BallDespawned)
                {
                    clientEnds.Add((e.Type, e.Tick, e.Position));
                }
            }

            client.Events.Clear();
            serverBalls.Add(server.Ballistics.Pool.Count > 0 ? server.Ballistics.Pool.Position[0] : Vector3.Zero);
            clientBalls.Add(client.Ballistics.Pool.Count > 0 ? client.Ballistics.Pool.Position[0] : Vector3.Zero);
        }

        Assert.NotEmpty(serverEnds);
        Assert.Equal(serverEnds, clientEnds);
        Assert.Equal(serverBalls, clientBalls);
    }

    [Fact]
    public void The_round_stands_still_on_a_joining_copy()
    {
        SimWorld sim = Ground((0, 0, 0f, 0f), (1, 1, 0f, -30f));
        sim.Role = SimRole.Client;
        sim.LocalPlayerId = 0;
        MatchState match = sim.StartMatch(Setup(MatchModeKind.Solo, new[] { 0 }));
        sim.GoLive();
        Out(sim, 1);
        List<SimEvent> events = Run(sim, Second);
        Assert.Equal(MatchPhase.Live, match.Phase);
        Assert.Equal(0f, match.Elapsed);
        Assert.DoesNotContain(events, e => e.Type == SimEventType.RoundEnded);
    }
}
