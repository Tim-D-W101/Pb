using System.Numerics;
using Pb.Net.Client;
using Pb.Net.Protocol;
using Pb.Net.Server;
using Pb.Net.Transport;
using Pb.Sim;
using Pb.Sim.Collision;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Players;
using Pb.Sim.Tests;

namespace Pb.Net.Tests;

/// <summary>
/// Moves a player over flat open ground (y = 0) by the movement rules, without an engine. The server and the joining
/// copies in these tests all move bodies this way, so a copy's prediction can come out exactly as the server's.
/// </summary>
internal sealed class OpenGroundBody : IPredictedBody
{
    private readonly SimWorld _sim;
    private readonly PlayerState _player;

    public OpenGroundBody(SimWorld sim, PlayerState player)
    {
        _sim = sim;
        _player = player;
    }

    public int Resets { get; private set; }

    public void Reset(in PredictedState state) => Resets++;

    public void Move(in InputCommand command, float dt) => Step(_sim, _player, command, dt);

    public static void Step(SimWorld sim, PlayerState p, in InputCommand command, float dt)
    {
        InputCommand cmd = command;
        if (!sim.IsLive && p.Alive)
        {
            cmd.Move = Vector2.Zero;
            cmd.Buttons = InputButtons.None;
        }

        bool grounded = p.Position.Y <= 1e-4f;
        MovementResult r = MovementModel.Step(p, cmd, sim.Config.Movement, dt, grounded, sim.Collision, sim.Ladders);
        float vy = r.JumpVelocity > 0f ? r.JumpVelocity : grounded ? 0f : p.Velocity.Y - sim.Config.Movement.Gravity * dt;
        var v = new Vector3(r.HorizontalVelocity.X, vy, r.HorizontalVelocity.Z);
        Vector3 at = p.Position + v * dt;
        if (at.Y < 0f)
        {
            at.Y = 0f;
            v.Y = 0f;
        }

        p.Position = at;
        p.Velocity = v;
        p.Grounded = at.Y <= 1e-4f;
    }
}

/// <summary>
/// A server and any number of joining copies over the in-memory network, on open ground with a wall 30 m north, all on a
/// shared clock that moves a tick at a time. The server's own players (bots, or the host) follow <see cref="BotScript"/>;
/// each copy's player follows its own script, through prediction like the game.
/// </summary>
internal sealed class NetRig
{
    public const float Dt = 1f / 120f;

    private InputCommand[] _commands = Array.Empty<InputCommand>();
    private bool _overSent;

    public NetRig(string password = "", NetSettings? settings = null)
    {
        Settings = settings ?? NetSettings.Load(TestData.Source);
        Server = new NetServer(Network.Listen(), Settings, new ServerIdentity("Test yard", "build-1", "data-1", password), () => Now, Dt);
    }

    public double Now { get; private set; }

    public NetSettings Settings { get; }

    public LoopbackNetwork Network { get; } = new();

    public NetServer Server { get; }

    public SimWorld? Sim { get; private set; }

    public List<RigClient> Clients { get; } = new();

    /// <summary>Commands for the server's own players (null: they stand still).</summary>
    public Func<int, PlayerState, InputCommand>? BotScript { get; set; }

    /// <summary>Called after each server step, before the snapshots go (to put things right in tests).</summary>
    public Action<SimWorld>? AfterServerStep { get; set; }

    /// <summary>Keep every event the server's sim makes.</summary>
    public bool KeepEvents { get; set; } = true;

    public List<SimEvent> ServerEvents { get; } = new();

    public static void Ground(SimWorld sim)
    {
        sim.Collision.Add(new PlaneShape(Vector3.UnitY, 0f), TestData.Config.Surfaces.Get("turf"), "ground");
        sim.Collision.Add(new BoxShape(new Vector3(0f, 2f, -30.5f), Quaternion.Identity, new Vector3(40f, 2f, 0.5f)),
            TestData.Config.Surfaces.Get("concrete"), "north wall");
    }

    public RigClient Join(string name, LagSettings? lag = null, string build = "build-1", string data = "data-1", string password = "",
        ulong seed = 1)
    {
        ITransport end = Network.Connect();
        if (lag is { Any: true })
        {
            end = new LaggedTransport(end, lag, () => Now, seed);
        }

        var client = new NetClient(end, Settings, () => Now,
            new HelloMessage { Build = build, DataHash = data, Name = name, Password = password }, TestData.Config.TickRate);
        client.Join();
        var rc = new RigClient(this, client);
        Clients.Add(rc);
        return rc;
    }

    /// <summary>A round with these players ((id, team, person, x, z) facing north), its people played by the copies that joined, in order.</summary>
    public RoundSetupMessage StartRound(string mode = "teams", float timeLimit = 900f, params (int Id, byte Team, bool Person, float X, float Z)[] roster)
    {
        var setup = new RoundSetupMessage
        {
            Round = Server.Round + 1, LevelId = "", ModeId = mode, Size = roster.Length, TierId = "normal", Seed = 7, TimeLimit = timeLimit,
            StartPods = 3, BotPods = 2, Pickups = false, Attackers = 0,
        };
        foreach ((int id, byte team, bool person, float x, float z) in roster)
        {
            setup.Roster.Add(new RosterEntry
            {
                PlayerId = id, Team = team, Name = person ? $"Person {id}" : $"Bot {id}", Person = person, Position = new Vector3(x, 0f, z), Yaw = 0f,
            });
        }

        Sim = RoundWorld.Build(TestData.Config, setup, null, Ground);
        _commands = new InputCommand[Sim.Players.Count];
        _overSent = false;
        int[] people = roster.Where(r => r.Person).Select(r => r.Id).ToArray();
        int next = 0;
        var given = new Dictionary<ClientLink, int>();
        Server.BeginRound(Sim, setup, link =>
        {
            if (!given.TryGetValue(link, out int id))
            {
                id = next < people.Length ? people[next++] : -1;
                given[link] = id;
            }

            return id;
        });
        return setup;
    }

    /// <summary>Runs until every copy has built the round, then starts it.</summary>
    public void GoLive(int most = 600)
    {
        for (int i = 0; i < most && !Server.AllLoaded(); i++)
        {
            Tick();
        }

        Assert.True(Server.AllLoaded(), "every copy should have built the round");
        Sim!.GoLive();
    }

    public void Run(int ticks, Func<bool>? until = null)
    {
        for (int i = 0; i < ticks; i++)
        {
            Tick();
            if (until?.Invoke() == true)
            {
                return;
            }
        }
    }

    public void Tick()
    {
        Now += Dt;
        Server.Poll();
        if (Sim is { } sim)
        {
            for (int i = 0; i < sim.Players.Count; i++)
            {
                PlayerState p = sim.Players[i];
                InputCommand cmd;
                if (Server.LinkOf(p.Id) is not null)
                {
                    if (!Server.TryCommand(p.Id, sim.Tick, out cmd))
                    {
                        cmd = new InputCommand { Tick = sim.Tick, Yaw = p.Yaw, Pitch = p.Pitch };
                    }
                }
                else
                {
                    cmd = BotScript?.Invoke(sim.Tick, p) ?? new InputCommand { Tick = sim.Tick, Yaw = p.Yaw, Pitch = p.Pitch };
                }

                OpenGroundBody.Step(sim, p, cmd, Dt);
                _commands[i] = cmd;
            }

            sim.Step(_commands);
            AfterServerStep?.Invoke(sim);
            if (KeepEvents)
            {
                foreach (SimEvent e in sim.Events.Items)
                {
                    ServerEvents.Add(e);
                }
            }

            Server.AfterStep(sim);
            sim.Events.Clear();
            if (sim.Match is { Phase: Pb.Sim.Match.MatchPhase.Ended } && !_overSent)
            {
                _overSent = true;
                Server.EndRound(sim);
            }
        }

        foreach (RigClient c in Clients)
        {
            c.Tick();
        }
    }
}

/// <summary>A joining copy in the rig: it builds each round it's sent and plays its player through prediction.</summary>
internal sealed class RigClient
{
    private readonly NetRig _rig;
    private InputCommand[] _commands = Array.Empty<InputCommand>();
    private int _localIndex = -1;

    public RigClient(NetRig rig, NetClient net)
    {
        _rig = rig;
        Net = net;
    }

    public NetClient Net { get; }

    public ClientSession? Session { get; private set; }

    public SimWorld? Sim { get; private set; }

    public OpenGroundBody? Body { get; private set; }

    /// <summary>What this copy's player presses (null: nothing, looking where they look).</summary>
    public Func<int, PlayerState, InputCommand>? Script { get; set; }

    /// <summary>
    /// Instead of playing through prediction, sends made-up commands of its own (cheat tests): true skips this tick's
    /// prediction and step.
    /// </summary>
    public Func<RigClient, bool>? Raw { get; set; }

    public bool KeepEvents { get; set; } = true;

    public List<SimEvent> Events { get; } = new();

    /// <summary>Does nothing at all (a copy busy building a level, or one that has hung).</summary>
    public bool Silent { get; set; }

    public void Tick()
    {
        if (Silent)
        {
            return;
        }

        if (Session is null)
        {
            Net.Poll();
            if (Net.TakeRoundSetup() is { } setup)
            {
                Build(setup);
            }

            return;
        }

        SimWorld sim = Sim!;
        Session.BeginTick(NetRig.Dt);
        if (Net.TakeRoundSetup() is { } next)
        {
            Build(next);
            return;
        }

        Array.Clear(_commands);
        if (Raw?.Invoke(this) == true)
        {
            Session.EndTick(NetRig.Dt);
            sim.Events.Clear();
            return;
        }

        if (Session.Local is { } me)
        {
            InputCommand raw = Script?.Invoke(sim.Tick, me) ?? new InputCommand { Yaw = me.Yaw, Pitch = me.Pitch };
            InputCommand cmd = Session.Predict(raw);
            Body!.Move(cmd, NetRig.Dt);
            _commands[_localIndex] = cmd;
        }

        sim.Step(_commands);
        Session.EndTick(NetRig.Dt);
        if (KeepEvents)
        {
            foreach (SimEvent e in sim.Events.Items)
            {
                Events.Add(e);
            }
        }

        sim.Events.Clear();
    }

    private void Build(RoundSetupMessage setup)
    {
        Sim = RoundWorld.Build(TestData.Config, setup, null, NetRig.Ground);
        _commands = new InputCommand[Sim.Players.Count];
        PlayerState? me = Sim.FindPlayer(setup.YourPlayerId);
        _localIndex = me is null ? -1 : Sim.Players.ToList().IndexOf(me);
        Body = me is null ? null : new OpenGroundBody(Sim, me);
        Session = new ClientSession(Net, Sim, setup.Round, setup.YourPlayerId, Body);
        Net.SendLoaded(setup.Round);
    }
}
