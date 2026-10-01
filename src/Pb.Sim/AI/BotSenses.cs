using System.Numerics;
using Pb.Sim.Core;
using Pb.Sim.Events;
using Pb.Sim.Players;

namespace Pb.Sim.AI;

/// <summary>What one bot knows about one enemy.</summary>
public sealed class Awareness
{
    public Awareness(int targetId)
    {
        TargetId = targetId;
    }

    public int TargetId { get; }

    /// <summary>Detection meter, 0..1. It fills while they can see you and empties when they can't.</summary>
    public float Meter { get; internal set; }

    /// <summary>In sight this tick (any of head, chest or hips).</summary>
    public bool Visible { get; internal set; }

    /// <summary>The meter has filled: they know you're there and where, while you stay in sight.</summary>
    public bool Spotted { get; internal set; }

    /// <summary>How long you've been continuously spotted and in sight (reaction time counts from here).</summary>
    public float SpottedFor { get; internal set; }

    /// <summary>Share of the tier's reaction time needed before shooting: less when you've just reappeared.</summary>
    public float ReactionScale { get; internal set; } = 1f;

    /// <summary>Where they last saw or heard you (feet), if <see cref="HasLead"/>.</summary>
    public Vector3 LastKnown { get; internal set; }

    /// <summary>Whether <see cref="LastKnown"/> came from a sighting (true) or a sound (false).</summary>
    public bool LastKnownSeen { get; internal set; }

    /// <summary>Seconds since they last saw or heard anything of you.</summary>
    public float SinceLead { get; internal set; } = float.MaxValue;

    /// <summary>Seconds since they last had you in sight.</summary>
    public float SinceSeen { get; internal set; } = float.MaxValue;

    /// <summary>There's somewhere worth looking: a sighting or sound within memory.</summary>
    public bool HasLead { get; internal set; }

    /// <summary>Their running estimate of your velocity, lagging the truth by the tier's tracking lag.</summary>
    public Vector3 EstimatedVelocity { get; internal set; }

    /// <summary>Under fire recently: a ball of yours broke close to them.</summary>
    public float SinceShotAt { get; internal set; } = float.MaxValue;
}

/// <summary>
/// One bot's sight, hearing and memory. Sight runs a detection meter per enemy: it fills while the
/// enemy is in the field of view with a clear line to their head, chest or hips, faster up close, in
/// the light, standing, moving and in the middle of the view; at full the enemy is spotted. Hearing
/// picks up shots, breaks nearby and footsteps (each carries its own range), muffled by walls, and a
/// sound gives a lead to investigate. Leads are forgotten after the memory time. Deterministic.
/// </summary>
public sealed class BotSenses
{
    private readonly SimWorld _sim;
    private readonly PlayerState _self;
    private readonly SenseParams _p;
    private readonly DifficultyParams _tier;
    private readonly List<Awareness> _known = new();
    private Pcg32 _rng;

    public BotSenses(SimWorld sim, PlayerState self, SenseParams senses, DifficultyParams tier, ulong seed)
    {
        _sim = sim;
        _self = self;
        _p = senses;
        _tier = tier;
        _rng = new Pcg32(seed);
    }

    /// <summary>Everyone this bot has an opinion about (enemies only).</summary>
    public IReadOnlyList<Awareness> Known => _known;

    /// <summary>When false the bot notices nothing (screenshot tours, scripted tests).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The most pressing enemy: spotted and in sight first, then the freshest lead.</summary>
    public Awareness? Focus { get; private set; }

    /// <summary>The highest meter among enemies (for the debug overlay and suspicion).</summary>
    public float HighestMeter { get; private set; }

    public Awareness? For(int targetId)
    {
        foreach (Awareness a in _known)
        {
            if (a.TargetId == targetId)
            {
                return a;
            }
        }

        return null;
    }

    /// <summary>Forget everything (a new round).</summary>
    public void Reset() => _known.Clear();

    /// <summary>
    /// One tick: look at every enemy, listen to the last step's events (<paramref name="heard"/>), age the
    /// leads and pick the focus.
    /// </summary>
    public void Update(float dt, ReadOnlySpan<SimEvent> heard)
    {
        IReadOnlyList<PlayerState> players = _sim.Players;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerState p = players[i];
            if (p.Team != _self.Team && p.Present)
            {
                Look(Ensure(p.Id), p, dt);
            }
        }

        if (Enabled && _self.Alive)
        {
            Listen(heard);
        }

        Awareness? focus = null;
        float highest = 0f;
        foreach (Awareness a in _known)
        {
            a.SinceLead += dt;
            a.SinceShotAt += dt;
            if (a.SinceLead > _p.Memory)
            {
                a.HasLead = false;
                a.Spotted = false;
            }

            highest = MathF.Max(highest, a.Meter);
            if (focus is null || Pressing(a) > Pressing(focus))
            {
                focus = a;
            }
        }

        HighestMeter = highest;
        Focus = focus is not null && (focus.HasLead || focus.Meter > 0f) ? focus : null;
    }

    private static float Pressing(Awareness a) =>
        (a.Spotted && a.Visible ? 3f : 0f) + (a.Visible ? 1f : 0f) + (a.HasLead ? 1f / (1f + a.SinceLead) : 0f) + a.Meter;

    private Awareness Ensure(int id)
    {
        Awareness? a = For(id);
        if (a is null)
        {
            a = new Awareness(id);
            _known.Add(a);
        }

        return a;
    }

    private void Look(Awareness a, PlayerState target, float dt)
    {
        float rate = 0f;
        int visibleParts = Enabled && _self.Alive && target.Alive ? VisibleParts(target, out rate) : 0;
        bool wasVisible = a.Visible;
        a.Visible = visibleParts > 0;
        if (a.Visible && !wasVisible)
        {
            a.ReactionScale = a.Spotted && a.SinceSeen <= _p.ReacquireWithin ? _p.ReacquireFactor : 1f;
        }

        if (a.Visible)
        {
            a.Meter = MathF.Min(1f, a.Meter + rate * dt);
            a.SinceSeen = 0f;
            if (a.Meter >= 1f)
            {
                a.Spotted = true;
            }

            if (a.Spotted || a.Meter >= _p.SuspiciousAt)
            {
                // A sighting: where you are now (a glimpse is enough to know where to look).
                a.LastKnown = target.Position;
                a.LastKnownSeen = true;
                a.SinceLead = 0f;
                a.HasLead = true;
            }

            // The velocity estimate catches up with the truth over the tracking lag.
            float blend = MathF.Min(1f, dt / _tier.TrackingLag);
            a.EstimatedVelocity += (target.Velocity - a.EstimatedVelocity) * blend;
            a.SpottedFor = a.Spotted ? a.SpottedFor + dt : 0f;
        }
        else
        {
            a.SinceSeen += dt;
            a.SpottedFor = 0f;
            a.Meter = MathF.Max(0f, a.Meter - _p.DecayRate * dt);
            a.EstimatedVelocity *= MathF.Max(0f, 1f - dt / _tier.TrackingLag);
            if (!target.Alive)
            {
                a.Spotted = false;
                a.HasLead = false;
            }
        }
    }

    /// <summary>How many of head, chest and hips this bot can see, and the meter fill rate if any.</summary>
    private int VisibleParts(PlayerState target, out float rate)
    {
        rate = 0f;
        Vector3 eye = _self.EyePosition;
        Vector3 chest = target.Position + new Vector3(0f, target.EyeHeight * 0.72f, 0f) + target.LeanOffset * 0.6f;
        Vector3 toChest = chest - eye;
        float distance = toChest.Length();
        if (distance > _tier.SightRange || distance < 1e-3f)
        {
            return 0;
        }

        Vector3 forward = ViewAngles.Forward(_self.Yaw, _self.Pitch);
        float angle = MathF.Acos(Math.Clamp(Vector3.Dot(forward, toChest / distance), -1f, 1f));
        if (angle > _p.HalfFieldOfView)
        {
            return 0;
        }

        int parts = 0;
        if (Clear(eye, target.EyePosition))
        {
            parts++;
        }

        if (Clear(eye, chest))
        {
            parts++;
        }

        if (Clear(eye, target.Position + new Vector3(0f, target.EyeHeight * 0.45f, 0f)))
        {
            parts++;
        }

        if (parts == 0)
        {
            return 0;
        }

        float focus = angle <= _p.HalfFocusAngle
            ? 1f
            : Lerp(1f, _p.PeripheralRate, (angle - _p.HalfFocusAngle) / MathF.Max(1e-3f, _p.HalfFieldOfView - _p.HalfFocusAngle));
        float near = 1f - (1f - _p.MinDistanceFactor) * Math.Clamp(distance / _tier.SightRange, 0f, 1f);
        float light = Lerp(_p.DarkFactor, 1f, _sim.Level?.AreaAt(target.Position)?.Light ?? 1f);
        float stance = target.Stance == Stance.Crouching ? _p.CrouchFactor : 1f;
        float moving = 1f + _p.MovingFactorPerMps * target.HorizontalSpeed;
        float partial = parts switch
        {
            1 => _p.PartialFactor,
            2 => (1f + _p.PartialFactor) * 0.5f,
            _ => 1f,
        };
        rate = _p.FillRate * _tier.DetectionScale * focus * near * light * stance * moving * partial;
        return parts;
    }

    private void Listen(ReadOnlySpan<SimEvent> events)
    {
        Vector3 ear = _self.EyePosition;
        float scale = _tier.HearingScale;
        for (int i = 0; i < events.Length; i++)
        {
            ref readonly SimEvent e = ref events[i];
            if (e.PlayerId == _self.Id || !IsEnemy(e.PlayerId, out PlayerState? source))
            {
                continue;
            }

            switch (e.Type)
            {
                case SimEventType.ShotFired:
                    if (Hears(e.Position, ear, _p.ShotHearing * scale))
                    {
                        Heard(source!, e.Position - new Vector3(0f, source!.EyeHeight, 0f));
                    }

                    break;
                case SimEventType.BallBroke:
                    if (Vector3.DistanceSquared(e.Position, ear) <= _p.BreakHearing * _p.BreakHearing * scale * scale)
                    {
                        // Under fire: they can tell roughly where it's coming from, a few metres out.
                        Awareness a = Ensure(source!.Id);
                        a.SinceShotAt = 0f;
                        Vector3 guess = source.Position + new Vector3(_rng.Symmetric(4f), 0f, _rng.Symmetric(4f));
                        Heard(source, guess);
                    }

                    break;
                case SimEventType.Footstep:
                    if (Hears(e.Position + new Vector3(0f, 0.3f, 0f), ear, e.Value * scale))
                    {
                        Heard(source!, e.Position);
                    }

                    break;
            }
        }
    }

    private bool IsEnemy(int playerId, out PlayerState? player)
    {
        player = _sim.FindPlayer(playerId);
        return player is not null && player.Team != _self.Team && player.Alive;
    }

    /// <summary>Within range, or within the muffled range when there's no clear line.</summary>
    private bool Hears(Vector3 source, Vector3 ear, float range)
    {
        float d2 = Vector3.DistanceSquared(source, ear);
        if (d2 > range * range)
        {
            return false;
        }

        float muffled = range * _p.WallMuffle;
        return d2 <= muffled * muffled || Clear(source, ear);
    }

    private void Heard(PlayerState source, Vector3 where)
    {
        Awareness a = Ensure(source.Id);
        if (a.Visible && a.Spotted)
        {
            return; // already watching them
        }

        a.LastKnown = where;
        a.LastKnownSeen = false;
        a.SinceLead = 0f;
        a.HasLead = true;
        a.Meter = MathF.Max(a.Meter, _p.SuspiciousAt);
    }

    private bool Clear(Vector3 from, Vector3 to) => !_sim.Collision.SweepSphere(from, to, 0f, out _);

    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);
}
