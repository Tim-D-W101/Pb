using System.Numerics;
using Pb.Net.Protocol;
using Pb.Net.Server;
using Pb.Sim;
using Pb.Sim.Events;
using Pb.Sim.Players;

namespace Pb.Net.Client;

/// <summary>A joining copy's own body: put back where the server says, and moved one tick by the movement rules.</summary>
public interface IPredictedBody
{
    /// <summary>Puts the body where the state says (the server's, after a correction): position, velocity, on the ground.</summary>
    void Reset(in PredictedState state);

    /// <summary>Moves the body one tick by the movement rules, as the host does before each step.</summary>
    void Move(in InputCommand command, float dt);
}

/// <summary>
/// A round on a joining copy, without the engine. Each tick the host calls <see cref="BeginTick"/> (takes in what has
/// arrived, corrects its own player if the server disagrees, poses everyone else between two snapshots), then moves its
/// own body with <see cref="Predict"/>'s command and steps the sim (in <see cref="SimRole.Client"/>), then calls
/// <see cref="EndTick"/> (keeps the prediction, sends the command, and puts the server's events that are due into the
/// sim's events, after its own, for sound, splats and the HUD).
/// </summary>
public sealed class ClientSession
{
    private const int History = 256;
    private const int OwnHits = 64;

    private readonly IPredictedBody? _body;
    private readonly InputCommand[] _commands = new InputCommand[History];
    private readonly double[] _t0 = new double[History];
    private readonly PredictedState[] _states = new PredictedState[History];
    private readonly int[] _seqs = new int[History];
    private readonly (uint Seq, int Target)[] _ownHits = new (uint, int)[OwnHits];
    private int _ownHitNext;
    private InputCommand _pending;
    private bool _predicted;
    private uint _predictedShots;
    private double _tickStart;

    public ClientSession(NetClient client, SimWorld sim, int round, int localPlayerId, IPredictedBody? body)
    {
        Client = client;
        Sim = sim;
        _body = body;
        sim.Role = SimRole.Client;
        sim.LocalPlayerId = localPlayerId;
        Local = sim.FindPlayer(localPlayerId);
        Fields = new WorldFields(sim.Players.Count, sim.Doors.Count, NetServer.Grid(sim));
        Array.Fill(_seqs, -1);
        client.BeginRound(round, Fields);
    }

    public NetClient Client { get; }

    public SimWorld Sim { get; }

    public WorldFields Fields { get; }

    /// <summary>This copy's own player (null when only watching).</summary>
    public PlayerState? Local { get; }

    /// <summary>Times the server's state was put back and the presses since replayed.</summary>
    public int Corrections { get; private set; }

    /// <summary>How far the last correction moved this copy's own player (m).</summary>
    public float LastCorrection { get; private set; }

    /// <summary>This copy's own player is out: it's shown where the server walks it, no longer predicted.</summary>
    public bool Following { get; private set; }

    /// <summary>
    /// Where the last small correction moved this copy's own player from, less where it moved it to: the camera and body
    /// are drawn this far off and eased back, so a small correction never shows as a jump. Zero after a big one.
    /// </summary>
    public Vector3 CorrectionOffset { get; private set; }

    /// <summary>Before anyone moves this tick.</summary>
    public void BeginTick(float dt)
    {
        Client.Poll();
        Client.AdvanceClock(dt);
        Reconcile(dt);
        PoseOthers();
        _tickStart = Sim.Time;
    }

    /// <summary>
    /// This tick's command as the server will read it (quantised, numbered by this copy's tick): move the body with it and
    /// give it to the step.
    /// </summary>
    public InputCommand Predict(in InputCommand raw)
    {
        InputCommand cmd = CommandCodec.Quantize(raw);
        cmd.Tick = Sim.Tick;
        cmd.Rewind = 0;
        if (!Sim.IsLive && Local is { Alive: true })
        {
            // Before the round goes live nobody moves or acts: the command says so, so the server runs it the same way
            // even once it has gone live there.
            cmd.Move = Vector2.Zero;
            cmd.Buttons = InputButtons.None;
        }

        _pending = cmd;
        _predicted = true;
        return cmd;
    }

    /// <summary>After the step, before its events go to sound, splats and the HUD.</summary>
    public void EndTick(float dt)
    {
        int seq = Sim.Tick - 1;
        if (Local is not null && _predicted)
        {
            int slot = seq & (History - 1);
            _commands[slot] = _pending;
            _t0[slot] = _tickStart;
            _states[slot] = PredictedState.Capture(Local, Sim.Time);
            _seqs[slot] = seq;
            Client.SendCommand(seq, _pending, (int)Math.Floor(Client.RenderTick));
            _predictedShots = Math.Max(_predictedShots, Local.Marker.ShotSequence);
            NoteOwnHits();
        }

        _predicted = false;
        DeliverEvents();
        float ease = Client.Settings.CorrectionEase;
        CorrectionOffset = ease <= 0f ? Vector3.Zero : CorrectionOffset * MathF.Max(0f, 1f - dt / ease);
    }

    private void Reconcile(float dt)
    {
        if (Local is null || !Client.TakeOwn(out int lastRun, out ReadOnlySpan<uint> fields))
        {
            return;
        }

        PredictedState server = OwnFields.Read(fields);
        if (!server.Alive)
        {
            // Out: the server walks this copy's player off the field, so it's shown where the server has it, not predicted.
            Following = true;
            CorrectionOffset = Vector3.Zero;
            server.Restore(Local, Sim.Time);
            _body?.Reset(server);
            return;
        }

        int slot = lastRun & (History - 1);
        if (lastRun < 0 || _seqs[slot] != lastRun)
        {
            return;
        }

        if (_states[slot].Matches(server, Client.Settings.CorrectionTolerance))
        {
            return;
        }

        // The server saw it differently: its state back, then this copy's presses since, again.
        Vector3 was = Local.Position;
        server.Restore(Local, _t0[slot] + dt);
        _states[slot] = server;
        _body?.Reset(server);
        for (int seq = lastRun + 1; ; seq++)
        {
            int s = seq & (History - 1);
            if (_seqs[s] != seq)
            {
                break;
            }

            _body?.Move(_commands[s], dt);
            Sim.ReplayLocal(Local, _commands[s], _t0[s]);
            _states[s] = PredictedState.Capture(Local, _t0[s] + dt);
        }

        Corrections++;
        Vector3 moved = was - Local.Position;
        LastCorrection = moved.Length();
        CorrectionOffset = LastCorrection < Client.Settings.CorrectionSnap ? CorrectionOffset + moved : Vector3.Zero;
    }

    private void PoseOthers()
    {
        if (!Client.Bracket(Client.RenderTick, out ReceivedSnapshot a, out ReceivedSnapshot b, out float t))
        {
            return;
        }

        IReadOnlyList<PlayerState> players = Sim.Players;
        MovementParams move = Sim.Config.Movement;
        for (int i = 0; i < players.Count && i < Fields.Players; i++)
        {
            if (players[i].Id != Sim.LocalPlayerId)
            {
                PuppetFields.Pose(players[i], Fields.Puppet(a.World, i), Fields.Puppet(b.World, i), t, Fields.Grid, move);
            }
            else if (Client.Newest is { } latest)
            {
                // This copy's own numbers come from the server too (it never decides a hit itself).
                PuppetFields.Scores(players[i], Fields.Puppet(latest.World, i));
            }
        }

        Fields.ApplyDoors(Sim, (t < 0.5f ? a : b).World);
        if (Client.Newest is { } newest)
        {
            Fields.ApplyRound(Sim, newest.World);
        }
    }

    /// <summary>The server's events whose time has come on the display (or that are about this copy's own player).</summary>
    private void DeliverEvents()
    {
        List<(uint Seq, SimEvent Event)> events = Client.Events;
        double render = Client.RenderTick;
        int kept = 0;
        for (int i = 0; i < events.Count; i++)
        {
            SimEvent e = events[i].Event;
            if (e.Tick <= render || AboutMe(e))
            {
                Apply(e);
            }
            else
            {
                events[kept++] = events[i];
            }
        }

        events.RemoveRange(kept, events.Count - kept);
    }

    private bool AboutMe(in SimEvent e) =>
        Local is not null && (e.Type == SimEventType.PlayerEliminated && e.TargetId == Local.Id || e.Type == SimEventType.MaskSprayed ||
                              e.PlayerId == Local.Id && e.Type is SimEventType.BallBroke or SimEventType.BallBounced or SimEventType.BallDespawned
                                  or SimEventType.ShotFired);

    private void Apply(in SimEvent e)
    {
        bool own = Local is not null && e.PlayerId == Local.Id;
        switch (e.Type)
        {
            case SimEventType.ShotFired:
                // Someone else's shot, or one of ours this copy didn't fire (its prediction was off): fly it for show.
                if (!own || e.ShotSequence > _predictedShots)
                {
                    Sim.SpawnRemoteShot(e.PlayerId, e.ShotSequence, e.Team, e.Position, e.Velocity, e.Value, e.Extra);
                }

                break;
            case SimEventType.BallBounced or SimEventType.BallBroke or SimEventType.BallDespawned:
                ResolveBall(e, own);
                break;
            default:
                Inject(e);
                break;
        }
    }

    private void ResolveBall(in SimEvent e, bool own)
    {
        int index = Sim.Ballistics.Pool.Find(e.PlayerId, e.ShotSequence);
        if (index >= 0)
        {
            if (e.Type == SimEventType.BallBounced)
            {
                Sim.Ballistics.Redirect(index, e.Position + e.Normal * (Sim.Config.Projectile.Radius + 1e-3f), e.Velocity);
                Inject(e);
            }
            else
            {
                Sim.Ballistics.Remove(index);
                if (e.Type == SimEventType.BallBroke)
                {
                    Inject(e);
                }
            }

            return;
        }

        // Gone here already. Our own ball ended where this copy flew it, but a player it didn't see hit is the server's to
        // show; someone else's this copy never had (it came before the round, or the air was full) shows where it broke.
        bool onPlayer = e.Type == SimEventType.BallBroke && PlayerHitboxes.IsPlayer(e.TargetId);
        if (own ? onPlayer && !HitHere(e.ShotSequence, e.TargetId) : e.Type == SimEventType.BallBroke)
        {
            Inject(e);
        }
    }

    private void Inject(in SimEvent e)
    {
        SimEvent copy = e;
        copy.Tick = Sim.Tick;
        Sim.Events.Add(copy);
    }

    /// <summary>Remembers which players this copy's own balls broke on, so the server's word on them isn't drawn twice.</summary>
    private void NoteOwnHits()
    {
        ReadOnlySpan<SimEvent> events = Sim.Events.Items;
        for (int i = 0; i < events.Length; i++)
        {
            ref readonly SimEvent e = ref events[i];
            if (e.Type == SimEventType.BallBroke && e.PlayerId == Local!.Id && PlayerHitboxes.IsPlayer(e.TargetId))
            {
                _ownHits[_ownHitNext] = (e.ShotSequence, e.TargetId);
                _ownHitNext = (_ownHitNext + 1) % OwnHits;
            }
        }
    }

    private bool HitHere(uint seq, int target)
    {
        foreach ((uint s, int t) in _ownHits)
        {
            if (s == seq && t == target)
            {
                return true;
            }
        }

        return false;
    }
}
