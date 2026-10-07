using System.Numerics;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Gear;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;

namespace Pb.Sim.AI;

/// <summary>What a bot is doing, broadly.</summary>
public enum BotMode : byte
{
    /// <summary>Nothing going on: holding the post or walking the patrol.</summary>
    Idle,

    /// <summary>Saw or heard something: stopped, looking that way.</summary>
    Suspicious,

    /// <summary>Going to have a look at a lead.</summary>
    Investigate,

    /// <summary>Fighting someone they've spotted: from cover, or rushing them.</summary>
    Engage,

    /// <summary>Working round to a new angle on someone who's out of sight.</summary>
    Flank,

    /// <summary>Pushing straight at someone's last known position.</summary>
    Push,

    /// <summary>Hunting around a last known position.</summary>
    Search,

    /// <summary>Out of paint: going for a pod lying around.</summary>
    Resupply,

    /// <summary>Heading back to the post or the patrol.</summary>
    Return,

    /// <summary>Eliminated: walking off the field.</summary>
    Out,
}

/// <summary>What a bot shouts. The game shows it as a subtitle to anyone close enough to hear.</summary>
public enum CalloutKind : byte
{
    None,

    /// <summary>Spotted an enemy.</summary>
    Spotted,

    /// <summary>Lost sight of the enemy they were fighting.</summary>
    Lost,

    /// <summary>Balls breaking around them.</summary>
    UnderFire,

    /// <summary>Refilling the loader in a fight.</summary>
    Refill,

    /// <summary>Eliminated: the paintball "Hit!" call.</summary>
    Hit,

    /// <summary>Working round to the side of an enemy a teammate called out.</summary>
    Flanking,

    /// <summary>Pushing straight at where the enemy was.</summary>
    Pushing,

    /// <summary>Leaving a spot that's been shot at (or shot from) too long for another.</summary>
    Moving,

    /// <summary>A teammate went out close by.</summary>
    ManDown,

    /// <summary>Picked up the case.</summary>
    CaseTaken,

    /// <summary>The teammate carrying the case went out close by.</summary>
    CaseDown,

    /// <summary>Defending: the case is on the move.</summary>
    CaseAlarm,

    /// <summary>Defending: the other side is in the room.</summary>
    RoomAlarm,
}

/// <summary>Where a bot fighting from cover is in its cycle.</summary>
public enum CoverPhase : byte
{
    None,

    /// <summary>Shooting from where they stood on spotting you, before moving to cover.</summary>
    OpenFire,

    /// <summary>On the way to a cover point.</summary>
    Moving,

    /// <summary>Behind cover: crouched or tucked in, refilling if need be.</summary>
    Hiding,

    /// <summary>Exposed: standing up over low cover or stepped out past an edge, shooting.</summary>
    Peeking,
}

/// <summary>
/// One bot. Every tick it reads its senses and produces an <see cref="InputCommand"/>, exactly like a
/// human's: the same movement, marker, paint and rules apply. It decides what to do every few tenths of
/// a second (the tier's decision interval): idle (post or patrol) → suspicious → investigate → engage
/// (from cover, peeking and snap-shooting, refilling behind it; or rushing) → flank or push → search →
/// return. Eliminated, it walks off. All randomness comes from a per-bot stream seeded from the match
/// seed, so a round replays identically from the same inputs. Steady-state ticks don't allocate.
/// </summary>
public sealed class BotBrain
{
    private const float ArriveRadius = 0.35f;
    private const float TriggerTicks = 1;

    /// <summary>Samples along a walk when judging how much of it an enemy would see.</summary>
    private const int ExposureSamples = 6;

    private readonly BotSquad _squad;
    private readonly SimWorld _sim;
    private readonly BrainParams _b;
    private readonly List<Vector3> _path = new(128);
    private readonly List<int> _near = new(128);
    private Pcg32 _rng;
    private InputCommand _cmd;
    private int _tick;

    // Movement.
    private Vector3 _goal;
    private bool _hasGoal;
    private bool _needPath;
    private bool _arrived;
    private int _waypoint;
    private BotGait _gait;
    private bool _crouchMove;
    private float _stuckFor;
    private float _bestDistance;
    private int _repaths;

    // Looking and aiming.
    private float _yaw;
    private float _pitch;
    private float _wantYaw;
    private float _wantPitch;
    private float _turnSpeed;
    private float _errorYaw;
    private float _errorPitch;
    private float _steady;
    private bool _wasVisible;
    private float _scanPhase;

    // The head (its own random numbers, so glances never change what else the bot decides) and the sweep of a post.
    private Pcg32 _lookRng;
    private float _headYaw;
    private float _glanceIn;
    private float _glanceLeft;
    private float _glanceYaw;
    private float _sweepHold;
    private int _sweepStep;

    // Combat.
    private int _cover = -1;
    private float _phaseTime;
    private float _phaseLength;
    private float _unseenInPeek;
    private int _peeksWithoutSight;
    private float _sincePull;
    private float _nextPull;
    private int _triggerHeld;
    private bool _wantLeft;
    private bool _swapPressed;

    // Modes.
    private float _modeTime;
    private float _decideIn;
    private int _patrolIndex;
    private int _patrolStep = 1;
    private float _pauseLeft;
    private Vector3 _searchCentre;
    private float _searchLeft;
    private int _hopsLeft;
    private float _lookAround;
    private float _outFor;
    private float _sinceCallout = float.MaxValue;
    private bool[] _huntVisited = Array.Empty<bool>();
    private bool _hunting;
    private float _quietFor;

    // Teamwork, vantage and flanking.
    private float _sinceShare = float.MaxValue;
    private int _vantage = -1;
    private bool _vantageTried;
    private int _shotsHere;
    private bool _relocating;

    // Objectives.
    private int _lastCarrier = -1;
    private Vector3? _dutyPost;
    private float _dutyYaw;
    private bool _onAlarm;
    private int _roomSpot = -1;
    private Vector3 _roomGoal;
    private int _roomFor = -1;

    // Doors on the way.
    private int _doorLeaf = -1;
    private float _doorTime;
    private int _doorTaps;
    private Vector3 _doorFace;

    public BotBrain(BotSquad squad, PlayerState self, ArchetypeParams archetype, DifficultyParams tier, OpponentSpawn spawn)
    {
        _squad = squad;
        _sim = squad.Sim;
        _b = squad.Config.Brain;
        Self = self;
        Archetype = archetype;
        Tier = tier;
        Home = spawn.Position;
        HomeYaw = spawn.Yaw;
        Route = archetype.Idle == BotIdle.Patrol ? spawn.Patrol : null;
        ulong seed = SeedHash.Combine(_sim.MatchSeed, (ulong)(0xB07 + self.Id * 7919));
        _rng = new Pcg32(seed);
        Senses = new BotSenses(_sim, self, squad.Config.Senses, tier, seed ^ 0x5EED5EED);
        _yaw = _wantYaw = self.Yaw;
        _scanPhase = _rng.NextFloat() * MathF.Tau;
        _lookRng = new Pcg32(SeedHash.Combine(_sim.MatchSeed, (ulong)(0x10CC + self.Id * 104729)));
        _glanceIn = Between(_b.GlanceEveryMin, _b.GlanceEveryMax);
        _sweepStep = (int)(_lookRng.NextUInt() & 3);
        _sweepHold = Between(_b.ScanHoldMin, _b.ScanHoldMax);
        _nextPull = tier.PullInterval;
        _sincePull = tier.PullInterval;
        if (Route is not null)
        {
            _patrolIndex = NearestRoutePoint(Route, self.Position);
        }
    }

    public PlayerState Self { get; }

    public ArchetypeParams Archetype { get; }

    public DifficultyParams Tier { get; }

    public BotSenses Senses { get; }

    /// <summary>The spawn: where a post-holder stands, and where everyone returns to.</summary>
    public Vector3 Home { get; }

    public float HomeYaw { get; }

    public PatrolRoute? Route { get; }

    /// <summary>A passive bot notices nothing and stands still (screenshot tours, scripted tests); it still walks off when hit.</summary>
    public bool Passive { get; set; }

    /// <summary>
    /// After this long with nothing to go on (idle, no lead), the bot starts hunting for the rest of the
    /// round, whatever its behaviour, so free-for-all and team rounds don't stall (s; 0 = never, as in solo).
    /// </summary>
    public float RestlessAfter { get; set; }

    /// <summary>Hunting because it had nothing to go on for <see cref="RestlessAfter"/>.</summary>
    public bool Restless { get; private set; }

    /// <summary>Hunts while idle: a hunter, or restless (unless the objective gives it something better to do).</summary>
    private bool Hunts => (Archetype.Idle == BotIdle.Hunt || Restless) && _dutyPost is null && !Attacking;

    /// <summary>On the side playing for the round's objective, while it's still to do.</summary>
    private bool Attacking => _sim.Match?.Objective is { Done: false } objective && Self.Team == objective.Attackers;

    /// <summary>
    /// Carrying the case, or in the room it's holding: nothing short of an enemy in sight takes it off the objective
    /// (no investigating sounds, no searching).
    /// </summary>
    private bool OnTask => _sim.Match?.Objective is { Done: false } objective && Self.Team == objective.Attackers &&
                           (objective.Carrier == Self.Id ||
                            (objective.Room is { } room && room.Contains(Self.Position + new Vector3(0f, 0.1f, 0f))));

    /// <summary>The objective post this defender has been sent to guard, if any.</summary>
    public Vector3? DutyPost => _dutyPost;

    public BotMode Mode { get; private set; }

    public CoverPhase Phase { get; private set; }

    /// <summary>The cover point being used (index into the squad's cover set), or −1.</summary>
    public int CoverIndex => _cover;

    /// <summary>The current path (for the debug overlay); <see cref="NextWaypoint"/> is the next one to reach.</summary>
    public IReadOnlyList<Vector3> Path => _path;

    public int NextWaypoint => _waypoint;

    /// <summary>The last thing this bot shouted, and on which tick (−1 = nothing yet).</summary>
    public CalloutKind Callout { get; private set; }

    public int CalloutTick { get; private set; } = -1;

    /// <summary>A short description for the debug overlay.</summary>
    public string Label => Phase == CoverPhase.None ? Mode.ToString() : $"{Mode} · {Phase}";

    public InputCommand Think(int tick)
    {
        _tick = tick;
        float dt = _sim.Dt;
        ReadOnlySpan<Events.SimEvent> heard = _squad.HeardFor(tick);
        _cmd = new InputCommand { Tick = tick };
        if (!Self.Alive)
        {
            return WalkOff(dt);
        }

        bool live = _sim.IsLive && !Passive;
        Senses.Enabled = live;
        Senses.Update(dt, heard, _squad.ContactsFor(tick), _b.CalloutRange, _b.ContactError, _b.ContactMemory);
        if (live)
        {
            NoticeTeammatesOut(heard);
            NoticeCase();
        }

        if (!live)
        {
            _cmd.Yaw = _yaw;
            _cmd.Pitch = _pitch;
            TurnHead(dt, calm: true);
            return _cmd;
        }

        _modeTime += dt;
        _phaseTime += dt;
        _sincePull += dt;
        _sinceCallout += dt;
        _sinceShare += dt;
        _decideIn -= dt;
        bool quiet = Mode is BotMode.Idle or BotMode.Return && Senses.Focus is not { HasLead: true };
        _quietFor = quiet ? _quietFor + dt : 0f;
        if (RestlessAfter > 0f && _quietFor >= RestlessAfter)
        {
            Restless = true;
        }

        if (_decideIn <= 0f || Urgent())
        {
            Decide();
            _decideIn = Tier.DecisionInterval * (0.8f + 0.4f * _rng.NextFloat());
        }

        _turnSpeed = _b.LookTurnSpeed;
        Act(dt);
        if (_doorLeaf >= 0)
        {
            // Waiting at a door: look at it (whatever the mode wanted to look at), so interact finds it.
            LookToward(_doorFace, slow: false);
        }

        Look(dt);
        _cmd.Yaw = _yaw;
        _cmd.Pitch = _pitch;
        TurnHead(dt, Mode is BotMode.Idle or BotMode.Return && Senses.Focus is not { HasLead: true });
        return _cmd;
    }

    /// <summary>
    /// The head: ahead of the body as it turns, towards what it's turning to look at; and with nothing going on
    /// (<paramref name="calm"/>), a glance round now and then.
    /// </summary>
    private void TurnHead(float dt, bool calm)
    {
        float glance = 0f;
        if (!calm)
        {
            _glanceLeft = 0f;
        }
        else if (_glanceLeft > 0f)
        {
            _glanceLeft -= dt;
            glance = _glanceYaw;
        }
        else if ((_glanceIn -= dt) <= 0f)
        {
            _glanceYaw = Between(_b.GlanceAngleMin, _b.GlanceAngleMax) * (_lookRng.NextFloat() < 0.5f ? -1f : 1f);
            _glanceLeft = Between(_b.GlanceHoldMin, _b.GlanceHoldMax);
            _glanceIn = Between(_b.GlanceEveryMin, _b.GlanceEveryMax);
            glance = _glanceYaw;
        }

        float most = _sim.Config.Movement.MaxHeadTurn;
        float want = Math.Clamp(BotAim.Wrap(_wantYaw - _yaw) + glance, -most, most);
        _headYaw = BotAim.TurnTowards(_headYaw, want, _b.HeadTurnSpeed * dt);
        _cmd.HeadYaw = _headYaw;
    }

    /// <summary>
    /// Sweeping a view about <paramref name="facing"/> as a person does: a look one way, back to the middle, the
    /// other way and back, each held a while, the body turning between them (the head there first).
    /// </summary>
    private float Sweep(float facing, float dt)
    {
        _sweepHold -= dt;
        if (_sweepHold <= 0f)
        {
            _sweepStep = (_sweepStep + 1) & 3;
            _sweepHold = Between(_b.ScanHoldMin, _b.ScanHoldMax);
        }

        float side = _sweepStep == 1 ? 1f : _sweepStep == 3 ? -1f : 0f;
        return facing + _b.ScanAngle * side;
    }

    private float Between(float min, float max) => min + (max - min) * _lookRng.NextFloat();

    /// <summary>
    /// Something that can't wait for the next decision: an enemy just spotted, shots landing close, or a
    /// fight with nobody left in it (they're out and forgotten since the last decision; acting on it
    /// would have no one to fight).
    /// </summary>
    private bool Urgent()
    {
        Awareness? f = Senses.Focus;
        if (f is null)
        {
            return Mode == BotMode.Engage;
        }

        bool spottedNow = f.Spotted && f.Visible && Mode != BotMode.Engage;
        bool shotAt = f.SinceShotAt < 0.05f && Mode is BotMode.Idle or BotMode.Suspicious or BotMode.Return;
        if (shotAt)
        {
            Shout(CalloutKind.UnderFire);
        }

        return spottedNow || shotAt;
    }

    private void Decide()
    {
        Awareness? f = Senses.Focus;
        bool seen = f is { Spotted: true, Visible: true };
        bool fighting = f is { Spotted: true } && f.SinceSeen < _b.GiveUpTime;

        if (OutOfPaint() && Mode != BotMode.Resupply && TryFindPod(out Vector3 pod))
        {
            SetMode(BotMode.Resupply);
            GoTo(pod, BotGait.Run);
            return;
        }

        if (Mode == BotMode.Resupply && !OutOfPaint())
        {
            GoBack();
            return;
        }

        if (seen)
        {
            if (Mode == BotMode.Flank && HoldingFire(f!))
            {
                return; // not noticed yet: get round to the side first
            }

            if (Mode != BotMode.Engage)
            {
                StartEngage(f!);
            }
            else
            {
                CheckCover(f!);
            }

            return;
        }

        if (OnTask)
        {
            // Carrying the case or holding the room: back to it as soon as nobody's in sight.
            if (Mode is not (BotMode.Idle or BotMode.Resupply))
            {
                SetMode(BotMode.Idle);
                Stop();
            }

            return;
        }

        // A teammate called someone out: a flanker works round to the side, anyone else comes to help.
        if (f is { HasLead: true, FromContact: true } && Mode is BotMode.Idle or BotMode.Return or BotMode.Suspicious)
        {
            if (Archetype.FlankOnContact && TryFlankAround(f))
            {
                SetMode(BotMode.Flank);
                Shout(CalloutKind.Flanking);
            }
            else
            {
                SetMode(BotMode.Investigate);
                GoTo(f.LastKnown, Archetype.MoveGait);
            }

            return;
        }

        if (fighting && Mode is BotMode.Engage or BotMode.Flank or BotMode.Push)
        {
            if (Mode == BotMode.Engage && f!.SinceSeen >= _b.LostSightTime && _peeksWithoutSight >= 1)
            {
                float aggression = Tier.Aggression * 2f;
                float roll = _rng.NextFloat();
                Shout(CalloutKind.Lost);
                if (roll < Archetype.PushChance * aggression || !Archetype.UseCover)
                {
                    SetMode(BotMode.Push);
                    Shout(CalloutKind.Pushing);
                    GoTo(f.LastKnown, Archetype.MoveGait);
                }
                else if (roll < (Archetype.PushChance + Archetype.FlankChance) * aggression && TryFlank(f))
                {
                    SetMode(BotMode.Flank);
                    Shout(CalloutKind.Flanking);
                }
            }
            else if (Mode == BotMode.Flank && (_arrived || !_hasGoal))
            {
                LookOutOrSearch(f!);
            }
            else if (Mode == BotMode.Push && (_arrived || !_hasGoal))
            {
                StartSearch(f!.LastKnown);
            }

            return;
        }

        if (f is { HasLead: true })
        {
            switch (Mode)
            {
                case BotMode.Idle:
                case BotMode.Return:
                    SetMode(BotMode.Suspicious);
                    Stop();
                    break;
                case BotMode.Suspicious when _modeTime >= _b.SuspiciousTime:
                    SetMode(BotMode.Investigate);
                    GoTo(f.LastKnown, BotGait.Walk);
                    break;
                case BotMode.Investigate:
                    if (Vector3.Distance(_goal, f.LastKnown) > 3f)
                    {
                        GoTo(f.LastKnown, BotGait.Walk);
                    }
                    else if (_arrived || !_hasGoal)
                    {
                        StartSearch(f.LastKnown);
                    }

                    break;
                case BotMode.Engage:
                    StartSearch(f.LastKnown);
                    break;
                case BotMode.Flank:
                    if (_arrived || !_hasGoal)
                    {
                        LookOutOrSearch(f);
                    }

                    break;
                case BotMode.Push:
                    // Get there first, then look around.
                    if (_arrived || !_hasGoal)
                    {
                        StartSearch(f.LastKnown);
                    }

                    break;
            }

            if (Mode is BotMode.Investigate or BotMode.Search && !Restless && !_onAlarm && FlatDistance(Self.Position, Home) > Archetype.Leash)
            {
                GoBack();
            }

            if (Mode != BotMode.Search)
            {
                return;
            }
        }

        switch (Mode)
        {
            case BotMode.Search:
                if (_searchLeft <= 0f || (_hopsLeft <= 0 && (_arrived || !_hasGoal) && _lookAround <= 0f))
                {
                    GoBack();
                }

                break;
            case BotMode.Return:
                if (_arrived || !_hasGoal)
                {
                    SetMode(BotMode.Idle);
                    Stop();
                }

                break;
            case BotMode.Idle:
                break;
            case BotMode.Resupply:
                if (_arrived || !_hasGoal)
                {
                    GoBack();
                }

                break;
            default:
                GoBack();
                break;
        }
    }

    private void Act(float dt)
    {
        Awareness? f = Senses.Focus;
        PlayerState? target = f is null ? null : _sim.FindPlayer(f.TargetId);
        bool visible = f is { Spotted: true, Visible: true } && target is { Alive: true };
        _crouchMove = false;
        switch (Mode)
        {
            case BotMode.Idle:
                ActIdle(dt);
                break;
            case BotMode.Suspicious:
                LookToward(f?.LastKnown ?? Self.Position + ViewAngles.FlatForward(_yaw), slow: true);
                break;
            case BotMode.Investigate:
                _crouchMove = f is { HasLead: true } && FlatDistance(Self.Position, f.LastKnown) < _b.CrouchNearLead;
                FollowPath(dt);
                LookToward(f?.LastKnown ?? _goal, slow: true);
                break;
            case BotMode.Engage:
                ActEngage(f!, target, visible, dt);
                break;
            case BotMode.Flank when Phase == CoverPhase.Peeking:
                // At its flanking spot, looking out from it.
                HoldCover(peek: true);
                LookToward(visible ? target!.Position : f?.LastKnown ?? _goal, slow: false);
                break;
            case BotMode.Flank:
                // The last stretch to a flanking spot, crouched and quiet.
                _crouchMove = Archetype.StealthWithin > 0f && _hasGoal && FlatDistance(Self.Position, _goal) < Archetype.StealthWithin;
                FollowPath(dt);
                if (visible)
                {
                    LookToward(target!.Position, slow: false);
                }
                else
                {
                    LookAlongPath();
                }

                break;
            case BotMode.Push:
            case BotMode.Resupply:
            case BotMode.Return:
                FollowPath(dt);
                LookAlongPath();
                break;
            case BotMode.Search:
                ActSearch(dt);
                break;
        }

        if (visible)
        {
            Aim(target!, f!, dt, mayFire: !(Mode == BotMode.Flank && HoldingFire(f!)));
            if (Mode == BotMode.Engage && _sinceShare >= _b.ShareInterval)
            {
                ShareContact(target!);
            }
        }
        else
        {
            // Out of sight their aim unsettles, slowly: back on a target they were tracking, they're quicker.
            _steady = MathF.Max(0f, _steady - dt * 0.5f);
            Release();
            MaybeRefill(force: false);
        }

        _wasVisible = visible;

        if (Self.Marker.Paint.Loader == 0)
        {
            MaybeRefill(force: true);
        }
    }

    private void ActIdle(float dt)
    {
        if (Attacking && ActObjective(dt))
        {
            return;
        }

        if (_dutyPost is { } post)
        {
            HoldPost(post, _dutyYaw, dt);
            return;
        }

        if (Archetype.Idle == BotIdle.Overwatch && !Restless && ActOverwatch(dt))
        {
            return;
        }

        if (Hunts && _sim.Level is { OpponentSpawns.Count: > 0 } level)
        {
            ActHunt(level, dt);
            return;
        }

        if (Route is { Points.Count: > 0 } route)
        {
            if (_pauseLeft > 0f)
            {
                _pauseLeft -= dt;
                Scan(route.Points[_patrolIndex], dt);
                return;
            }

            if (!_hasGoal)
            {
                GoTo(route.Points[_patrolIndex], BotGait.Stroll);
            }

            FollowPath(dt);
            LookAlongPath();
            if (_arrived)
            {
                _pauseLeft = route.Pause;
                AdvancePatrol(route);
                _hasGoal = false;
            }

            return;
        }

        HoldPost(Home, HomeYaw, dt);
    }

    /// <summary>A post: get there, then stand sweeping the view around its facing.</summary>
    private void HoldPost(Vector3 post, float yaw, float dt)
    {
        if (FlatDistance(Self.Position, post) > 1.0f)
        {
            if (!_hasGoal || FlatDistance(_goal, post) > 0.5f)
            {
                GoTo(post, _dutyPost is null ? BotGait.Stroll : BotGait.Walk);
            }

            FollowPath(dt);
            LookAlongPath();
            return;
        }

        Stop();
        _wantYaw = Sweep(yaw, dt);
        _wantPitch = 0f;
    }

    /// <summary>
    /// Playing for the objective while there's nothing more pressing. Retrieve: fetch the case where it lies; carrying
    /// it, take it to the nearest way out; with a teammate carrying it, keep within escort distance of them. Hold: take a
    /// spot in the room (a free cover point in it, else its middle) and watch from it. False when there's nothing to do.
    /// </summary>
    private bool ActObjective(float dt)
    {
        ObjectiveState objective = _sim.Match!.Objective!;
        Vector3 goal;
        float within;
        BotGait gait = Archetype.MoveGait;
        if (objective.Kind == ObjectiveKind.Retrieve)
        {
            if (objective.Carrier == Self.Id)
            {
                goal = NearestExit(objective, Self.Position);
                within = 0.5f;
            }
            else if (objective.Carrier >= 0 && _sim.FindPlayer(objective.Carrier) is { Alive: true } carrier)
            {
                goal = carrier.Position;
                within = _b.Escort;
            }
            else
            {
                goal = objective.CasePosition;
                within = 0.2f;
            }
        }
        else if (objective.Room is { } room)
        {
            goal = RoomSpot(objective, room);
            within = 0.6f;
        }
        else
        {
            return false;
        }

        if (FlatDistance(Self.Position, goal) > within || MathF.Abs(Self.Position.Y - goal.Y) > 1.5f)
        {
            if (!_hasGoal || FlatDistance(_goal, goal) > 1.5f)
            {
                GoTo(goal, gait);
            }

            FollowPath(dt);
            LookAlongPath();
            return true;
        }

        // There (or close enough to the carrier): stand and watch, out of the room or away from the carrier.
        Stop();
        float facing = objective.Kind == ObjectiveKind.Hold && objective.Room is { } held ? YawTo(held.Centre, Self.Position)
            : objective.Carrier >= 0 && objective.Carrier != Self.Id && _sim.FindPlayer(objective.Carrier) is { } escorted &&
              FlatDistance(escorted.Position, Self.Position) > 0.5f ? YawTo(escorted.Position, Self.Position)
            : HomeYaw;
        _wantYaw = Sweep(facing, dt);
        _wantPitch = 0f;
        return true;
    }

    /// <summary>Where this bot holds the room from: a free cover point inside it (claimed), else the walkable middle.</summary>
    private Vector3 RoomSpot(ObjectiveState objective, HoldRoom room)
    {
        CoverSet cover = _squad.Cover;
        if (_roomFor == objective.RoomIndex)
        {
            // Back from a fight (which took other cover): the same spot again, unless someone's taken it meanwhile.
            if (_roomSpot < 0 || cover.ClaimedBy(_roomSpot) == Self.Id || cover.Claim(_roomSpot, Self.Id))
            {
                return _roomGoal;
            }
        }

        _roomFor = objective.RoomIndex;
        _roomSpot = -1;
        cover.Near(room.Centre, 30f, _near);
        int best = -1;
        float bestScore = float.MinValue;
        for (int k = 0; k < _near.Count; k++)
        {
            int i = _near[k];
            CoverPoint p = cover.Points[i];
            int holder = cover.ClaimedBy(i);
            if ((holder != -1 && holder != Self.Id) || !room.Contains(p.Position + new Vector3(0f, 0.1f, 0f)))
            {
                continue;
            }

            float score = _rng.NextFloat() - FlatDistance(p.Position, room.Centre) * 0.05f;
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        if (best >= 0 && cover.Claim(best, Self.Id))
        {
            _roomSpot = best;
            _roomGoal = cover.Points[best].Position;
        }
        else
        {
            _roomGoal = _squad.Grid.TrySnap(room.Centre, out Vector3 middle) ? middle : room.Centre;
        }

        return _roomGoal;
    }

    private static Vector3 NearestExit(ObjectiveState objective, Vector3 from)
    {
        IReadOnlyList<ExitSpec> exits = objective.Level.Exits;
        Vector3 best = exits[0].Position;
        for (int i = 1; i < exits.Count; i++)
        {
            if (FlatDistance(exits[i].Position, from) < FlatDistance(best, from))
            {
                best = exits[i].Position;
            }
        }

        return best;
    }

    /// <summary>The objective's alarm (the case on the move, the room taken): where <paramref name="enemy"/> is, to go after.</summary>
    internal void Alarm(PlayerState enemy)
    {
        Senses.Alarm(enemy.Id, enemy.Position);
        if (!_onAlarm)
        {
            // The first alarm of a chase gets a shout; the ones that keep it going don't.
            Shout(_sim.Match?.Objective?.Kind == ObjectiveKind.Hold ? CalloutKind.RoomAlarm : CalloutKind.CaseAlarm);
        }

        _onAlarm = true;
    }

    /// <summary>
    /// Objective duty: hold <paramref name="post"/>, facing <paramref name="yaw"/>, instead of the usual post, patrol or hunt
    /// (a way out to cut off, or the case where it fell). An idle bot sets off at once; a busy one goes when it's done.
    /// </summary>
    internal void Guard(Vector3 post, float yaw)
    {
        if (_dutyPost is { } current && FlatDistance(current, post) < 1f)
        {
            return;
        }

        if (_dutyPost is null)
        {
            Shout(CalloutKind.CaseAlarm);
        }

        _dutyPost = post;
        _dutyYaw = yaw;
        if (Mode is BotMode.Idle or BotMode.Return)
        {
            SetMode(BotMode.Return);
            GoTo(post, Archetype.MoveGait);
        }
    }

    /// <summary>
    /// Hunting: walk to one of the few nearest opponent spawns not yet visited (picked at random, so hunters
    /// don't all sweep the same way), look around there, move on; start again when all are done.
    /// </summary>
    private void ActHunt(LevelLayout level, float dt)
    {
        if (_huntVisited.Length != level.OpponentSpawns.Count)
        {
            _huntVisited = new bool[level.OpponentSpawns.Count];
        }

        if (_pauseLeft > 0f)
        {
            _pauseLeft -= dt;
            Stop();
            _wantYaw = _yaw + 1f; // turn on the spot to look around
            _wantPitch = 0f;
            return;
        }

        if (!_hasGoal || !_hunting)
        {
            int next = -1;
            for (int pass = 0; pass < 2 && next < 0; pass++)
            {
                next = PickHuntSpot(level);
                if (next < 0)
                {
                    Array.Clear(_huntVisited); // all visited: start the sweep again
                }
            }

            if (next < 0)
            {
                Stop();
                return;
            }

            _huntVisited[next] = true;
            _hunting = true;
            GoTo(level.OpponentSpawns[next].Position, BotGait.Walk);
        }

        FollowPath(dt);
        LookAlongPath();
        if (_arrived)
        {
            _pauseLeft = _b.HuntLookAround;
            _hunting = false;
            _hasGoal = false;
        }
    }

    /// <summary>One of the <see cref="BrainParams.HuntChoices"/> nearest unvisited spawns at random, or −1 when all are visited.</summary>
    private int PickHuntSpot(LevelLayout level)
    {
        const int MaxChoices = 8;
        Span<int> nearest = stackalloc int[MaxChoices];
        Span<float> distances = stackalloc float[MaxChoices];
        int choices = Math.Clamp(_b.HuntChoices, 1, MaxChoices);
        int found = 0;
        for (int i = 0; i < level.OpponentSpawns.Count; i++)
        {
            float d = FlatDistance(Self.Position, level.OpponentSpawns[i].Position);
            if (_huntVisited[i] || d <= 2f)
            {
                continue;
            }

            // Insertion into the short list of the nearest so far.
            int at = found < choices ? found++ : choices;
            while (at > 0 && distances[at - 1] > d)
            {
                if (at < choices)
                {
                    nearest[at] = nearest[at - 1];
                    distances[at] = distances[at - 1];
                }

                at--;
            }

            if (at < choices)
            {
                nearest[at] = i;
                distances[at] = d;
            }
        }

        return found == 0 ? -1 : nearest[(int)(_rng.NextUInt() % (uint)found)];
    }

    private void Scan(Vector3 around, float dt)
    {
        float baseYaw = HomeYaw;
        if (Route is { } route && route.Points.Count > 1)
        {
            Vector3 next = route.Points[_patrolIndex];
            if (FlatDistance(next, around) > 0.5f)
            {
                baseYaw = YawTo(around, next);
            }
        }

        _wantYaw = Sweep(baseYaw, dt);
        _wantPitch = 0f;
    }

    private void AdvancePatrol(PatrolRoute route)
    {
        if (route.Loop)
        {
            _patrolIndex = (_patrolIndex + 1) % route.Points.Count;
            return;
        }

        if (_patrolIndex + _patrolStep < 0 || _patrolIndex + _patrolStep >= route.Points.Count)
        {
            _patrolStep = -_patrolStep;
        }

        _patrolIndex += _patrolStep;
    }

    /// <summary>
    /// Overwatch (marksmen): take the best vantage within reach of the start, then watch the open ground from it,
    /// sweeping slowly across the side it faces. False when there's no vantage to be had (it holds its post instead).
    /// </summary>
    private bool ActOverwatch(float dt)
    {
        CoverSet cover = _squad.Cover;
        if (!_vantageTried)
        {
            _vantageTried = true;
            _vantage = _squad.Vantage.Best(cover, Home, Archetype.OverwatchReach, Self.Id, _near);
            if (_vantage >= 0 && !cover.Claim(_vantage, Self.Id))
            {
                _vantage = -1;
            }
        }

        // Back from a fight: the vantage again, or another if someone's taken it meanwhile.
        if (_vantage >= 0 && cover.ClaimedBy(_vantage) != Self.Id && !cover.Claim(_vantage, Self.Id))
        {
            _vantage = _squad.Vantage.Best(cover, Home, Archetype.OverwatchReach, Self.Id, _near);
            if (_vantage >= 0 && !cover.Claim(_vantage, Self.Id))
            {
                _vantage = -1;
            }
        }

        if (_vantage < 0)
        {
            return false;
        }

        CoverPoint p = cover.Points[_vantage];
        if (FlatDistance(Self.Position, p.Position) > 1.2f)
        {
            if (!_hasGoal || Vector3.Distance(_goal, p.Position) > 0.5f)
            {
                GoTo(p.Position, BotGait.Walk);
            }

            FollowPath(dt);
            LookAlongPath();
            return true;
        }

        // On the spot: eyes over low cover, or stepped out past the edge and leaning, watching its most open view and
        // sweeping slowly either side of it (never further round than the side the cover faces).
        _cover = _vantage;
        HoldCover(peek: true);
        _scanPhase += dt * MathF.Tau / (_b.ScanPeriod * 1.6f);
        float middle = YawOf(-p.Normal);
        float view = middle + BotAim.Wrap(_squad.Vantage.FacingYaw(_vantage) - middle);
        float half = _b.VantageArc * 0.5f;
        _wantYaw = Math.Clamp(view + _b.VantageArc * 0.25f * MathF.Sin(_scanPhase), middle - half, middle + half);
        _wantPitch = -0.03f;
        return true;
    }

    private void StartEngage(Awareness f)
    {
        SetMode(BotMode.Engage);
        Shout(CalloutKind.Spotted);
        if (_sim.FindPlayer(f.TargetId) is { Alive: true } seen)
        {
            ShareContact(seen);
        }

        _peeksWithoutSight = 0;
        PlayerState? target = _sim.FindPlayer(f.TargetId);
        float distance = target is null ? 0f : Vector3.Distance(Self.Position, target.Position);
        bool underFire = f.SinceShotAt < 1.5f;
        if (!Archetype.UseCover)
        {
            SetPhase(CoverPhase.None, 0f);
            return; // rushers just come at you
        }

        if (underFire || distance < _b.OpenFireMinDistance)
        {
            ChooseCover(f);
            return;
        }

        // Someone in the open: shoot from here first.
        SetPhase(CoverPhase.OpenFire, _b.OpenFireTime * (0.8f + 0.4f * _rng.NextFloat()));
        Stop();
    }

    private void ActEngage(Awareness f, PlayerState? target, bool visible, float dt)
    {
        if (!Archetype.UseCover)
        {
            // Rushers close in, shooting on the move, and stop at their fighting range.
            float distance = FlatDistance(Self.Position, f.LastKnown);
            if (!visible || distance > Archetype.EngageRange)
            {
                if (!_hasGoal || Vector3.Distance(_goal, f.LastKnown) > 2f)
                {
                    GoTo(f.LastKnown, Archetype.MoveGait);
                }

                FollowPath(dt);
            }
            else
            {
                Stop();
            }

            if (!visible)
            {
                LookAlongPath();
            }

            return;
        }

        switch (Phase)
        {
            case CoverPhase.OpenFire:
                Stop();
                if (!visible)
                {
                    LookToward(f.LastKnown, slow: false);
                }

                if (_phaseTime >= _phaseLength || f.SinceShotAt < 0.05f)
                {
                    ChooseCover(f);
                }

                break;
            case CoverPhase.Moving:
                FollowPath(dt);
                if (!visible)
                {
                    LookToward(f.LastKnown, slow: false);
                }

                if (_arrived)
                {
                    SetPhase(CoverPhase.Hiding, Archetype.HideTime * (0.8f + 0.5f * _rng.NextFloat()));
                }

                break;
            case CoverPhase.Hiding:
                HoldCover(peek: false);
                LookToward(f.LastKnown, slow: false);
                if (ShouldRelocate(f))
                {
                    Shout(CalloutKind.Moving, force: true); // once a spot, and worth hearing even straight after "under fire"
                    _relocating = true;
                    ChooseCover(f);
                    _relocating = false;
                    break;
                }

                if (_phaseTime >= _phaseLength && Self.Marker.Paint.Loader > 0 && !Self.Marker.Refill.Active)
                {
                    SetPhase(CoverPhase.Peeking, Archetype.PeekTime * (0.8f + 0.4f * _rng.NextFloat()));
                    _unseenInPeek = 0f;
                }

                break;
            case CoverPhase.Peeking:
                HoldCover(peek: true);
                if (!visible)
                {
                    LookToward(f.LastKnown, slow: false);
                    _unseenInPeek += dt;
                }

                if (_phaseTime >= _phaseLength || _unseenInPeek >= _b.PeekGiveUp)
                {
                    _peeksWithoutSight = _unseenInPeek >= _b.PeekGiveUp ? _peeksWithoutSight + 1 : 0;
                    SetPhase(CoverPhase.Hiding, Archetype.HideTime * (0.8f + 0.5f * _rng.NextFloat()));
                }

                break;
            default:
                // No cover worth having: fight from here, crouched while not shooting.
                Stop();
                if (!visible)
                {
                    LookToward(f.LastKnown, slow: false);
                    _cmd.Buttons |= InputButtons.Crouch;
                }

                break;
        }
    }

    /// <summary>At the cover point: tucked in, or exposed to shoot (stand over low cover, step out past an edge and lean).</summary>
    private void HoldCover(bool peek)
    {
        if (_cover < 0)
        {
            Stop();
            return;
        }

        CoverPoint point = _squad.Cover.Points[_cover];
        Vector3 spot = point.Position;
        if (peek && point.Height == CoverHeight.Full && point.HasEdge)
        {
            Vector3 side = PeekSide(point);
            spot += side * CoverSet.PeekStep;
            // Lean and carry the marker on the side of the edge, as the muzzle-in-cover rule demands.
            bool left = Vector3.Dot(side, ViewAngles.Right(_yaw)) < 0f;
            _cmd.Buttons |= left ? InputButtons.LeanLeft : InputButtons.LeanRight;
            WantShoulder(left);
        }

        if (!peek && point.Height == CoverHeight.Half)
        {
            _cmd.Buttons |= InputButtons.Crouch;
        }

        if (!peek && point.Height == CoverHeight.Full && !point.HasEdge)
        {
            _cmd.Buttons |= InputButtons.Crouch;
        }

        MoveTowards(spot, BotGait.Walk, 0.12f);
    }

    /// <summary>Where the eyes are when shooting from a cover point: standing over low cover, or stepped out past the edge.</summary>
    private Vector3 ShootingEye(CoverPoint p, Vector3 threat)
    {
        Vector3 at = p.Position;
        if (p.Height == CoverHeight.Full && p.HasEdge)
        {
            Vector3 side = p.TwoSided && Vector3.Dot(threat - p.Position, p.PeekDirection) < 0f ? -p.PeekDirection : p.PeekDirection;
            at += side * CoverSet.PeekStep;
        }

        return at + new Vector3(0f, _sim.Config.Movement.StandEyeHeight, 0f);
    }

    /// <summary>Which way to step out from a cover point: toward the threat's side for two-sided cover.</summary>
    private Vector3 PeekSide(CoverPoint point)
    {
        if (!point.TwoSided)
        {
            return point.PeekDirection;
        }

        Awareness? f = Senses.Focus;
        Vector3 toThreat = f is null ? -point.Normal : f.LastKnown - point.Position;
        return Vector3.Dot(toThreat, point.PeekDirection) >= 0f ? point.PeekDirection : -point.PeekDirection;
    }

    private void WantShoulder(bool left)
    {
        bool onLeft = Self.ShoulderTarget < 0f;
        if (onLeft != left && !_swapPressed)
        {
            _cmd.Buttons |= InputButtons.SwapShoulder;
            _swapPressed = true;
        }
        else
        {
            _swapPressed = false;
        }

        _wantLeft = left;
    }

    /// <summary>
    /// Picks the best cover point against <paramref name="f"/>: near, hidden from them, ideally with a way
    /// to shoot back, around the behaviour's fighting range, not claimed by a teammate.
    /// </summary>
    private void ChooseCover(Awareness f)
    {
        PlayerState? target = _sim.FindPlayer(f.TargetId);
        Vector3 threatEye = target is { Alive: true } ? target.EyePosition : f.LastKnown + new Vector3(0f, 1.6f, 0f);
        Vector3 threatChest = threatEye - new Vector3(0f, 0.45f, 0f);
        CoverSet cover = _squad.Cover;
        float radius = _b.CoverSearchRadius * (Archetype.Vantage > 0f ? 1.6f : 1f);
        cover.Near(Self.Position, radius, _near);
        int best = -1;
        float bestScore = float.MinValue;
        MovementParams move = _sim.Config.Movement;
        Vector3 leaving = _cover >= 0 ? cover.Points[_cover].Position : Self.Position;
        for (int k = 0; k < _near.Count; k++)
        {
            int i = _near[k];
            int holder = cover.ClaimedBy(i);
            if (holder != -1 && holder != Self.Id)
            {
                continue;
            }

            CoverPoint p = cover.Points[i];
            if (_relocating && FlatDistance(p.Position, leaving) < _b.RelocateDistance)
            {
                continue; // moving means somewhere else
            }
            float toThreat = FlatDistance(p.Position, threatEye);
            if (toThreat < _b.MinThreatDistance || Vector3.Dot(threatEye - p.Position, p.Normal) > 0f)
            {
                continue; // too close, or the threat's on the open side
            }

            float head = p.Height == CoverHeight.Full ? move.StandEyeHeight : move.CrouchEyeHeight;
            if (Clear(threatEye, p.Position + new Vector3(0f, head + 0.05f, 0f)))
            {
                continue; // doesn't hide you from them
            }

            bool canShoot = p.CanShoot && Clear(ShootingEye(p, threatEye), threatChest);

            float travel = FlatDistance(Self.Position, p.Position);
            float score = -travel * 1.2f - MathF.Abs(toThreat - Archetype.EngageRange) * 0.4f + (canShoot ? 12f : 0f) + (p.Height == CoverHeight.Full ? 1.5f : 0f);
            if (Archetype.Vantage > 0f)
            {
                score += Archetype.Vantage * _squad.Vantage[i] * 10f;
            }
            if (FlatDistance(p.Position, threatEye) < FlatDistance(Self.Position, threatEye) - 4f)
            {
                score -= 6f; // running at them to get there
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        if (best >= 0 && cover.Claim(best, Self.Id))
        {
            _cover = best;
            _shotsHere = 0;
            GoTo(cover.Points[best].Position, Archetype.MoveGait);
            SetPhase(CoverPhase.Moving, 0f);
        }
        else
        {
            cover.Release(Self.Id);
            _cover = -1;
            SetPhase(CoverPhase.None, 0f);
            Stop();
        }
    }

    /// <summary>Their cover no longer hides them (the enemy moved): find another.</summary>
    private void CheckCover(Awareness f)
    {
        if (!Archetype.UseCover || Phase != CoverPhase.Hiding || _cover < 0)
        {
            return;
        }

        PlayerState? target = _sim.FindPlayer(f.TargetId);
        if (target is null)
        {
            return;
        }

        CoverPoint p = _squad.Cover.Points[_cover];
        float head = p.Height == CoverHeight.Full ? _sim.Config.Movement.StandEyeHeight : _sim.Config.Movement.CrouchEyeHeight;
        if (Clear(target.EyePosition, p.Position + new Vector3(0f, head + 0.05f, 0f)))
        {
            ChooseCover(f);
        }
    }

    /// <summary>A cover point that sees the last known position from a new angle.</summary>
    private bool TryFlank(Awareness f)
    {
        CoverSet cover = _squad.Cover;
        cover.Near(f.LastKnown, _b.CoverSearchRadius * 1.5f, _near);
        Vector3 current = Vector3.Normalize((Self.Position - f.LastKnown) with { Y = 0f } + new Vector3(1e-4f, 0f, 0f));
        int best = -1;
        float bestScore = float.MinValue;
        Vector3 lastChest = f.LastKnown + new Vector3(0f, 1.2f, 0f);
        for (int k = 0; k < _near.Count; k++)
        {
            int i = _near[k];
            if (cover.ClaimedBy(i) is int holder && holder != -1 && holder != Self.Id)
            {
                continue;
            }

            CoverPoint p = cover.Points[i];
            Vector3 dir = (p.Position - f.LastKnown) with { Y = 0f };
            float distance = dir.Length();
            if (distance < _b.MinThreatDistance || distance > _b.CoverSearchRadius * 1.5f)
            {
                continue;
            }

            float angle = MathF.Acos(Math.Clamp(Vector3.Dot(dir / distance, current), -1f, 1f));
            if (angle < 0.6f || !p.CanShoot)
            {
                continue; // not a new angle
            }

            float score = angle * 4f - FlatDistance(Self.Position, p.Position) * 0.3f + _rng.NextFloat();
            if (Archetype.FlankOnContact)
            {
                score -= Exposure(Self.Position, p.Position, f.LastKnown) * 8f;
            }

            if (score > bestScore && Clear(ShootingEye(p, lastChest), lastChest))
            {
                bestScore = score;
                best = i;
            }
        }

        if (best < 0 || !cover.Claim(best, Self.Id))
        {
            return false;
        }

        _cover = best;
        GoTo(cover.Points[best].Position, Archetype.MoveGait);
        SetPhase(CoverPhase.None, 0f);
        return true;
    }

    /// <summary>
    /// A flanking spot on a teammate's contact: cover that sees the enemy's position from well off the line between them
    /// and the teammate who called it (as near a right angle as can be had), hidden from them, reached by a walk they'd see
    /// as little of as possible.
    /// </summary>
    private bool TryFlankAround(Awareness f)
    {
        int best = FlankSpot(f.LastKnown, f.ContactFrom, jitter: true);
        if (best < 0 || !_squad.Cover.Claim(best, Self.Id))
        {
            return false;
        }

        _cover = best;
        GoTo(_squad.Cover.Points[best].Position, Archetype.MoveGait);
        SetPhase(CoverPhase.None, 0f);
        return true;
    }

    /// <summary>
    /// The cover point this bot would flank to if a teammate at <paramref name="caller"/> called out an enemy at
    /// <paramref name="enemy"/>, or -1 if there's none (see <see cref="TryFlankAround"/>). Without
    /// <paramref name="jitter"/> it leaves out the small random tie-break, so it can be asked from outside the sim (the
    /// role demo picks where to stand with it) without touching the brain's random numbers.
    /// </summary>
    public int FlankSpot(Vector3 enemy, Vector3 caller, bool jitter = false)
    {
        CoverSet cover = _squad.Cover;
        float reach = MathF.Max(Archetype.EngageRange * 1.4f, 12f);
        cover.Near(enemy, reach, _near);
        Vector3 line = (caller - enemy) with { Y = 0f };
        if (line.LengthSquared() < 1e-4f)
        {
            line = (Self.Position - enemy) with { Y = 0f };
        }

        line = Vector3.Normalize(line + new Vector3(1e-4f, 0f, 0f));
        Vector3 threatEye = enemy + new Vector3(0f, _sim.Config.Movement.StandEyeHeight, 0f);
        Vector3 lastChest = enemy + new Vector3(0f, 1.2f, 0f);
        float head = _sim.Config.Movement.CrouchEyeHeight;
        int best = -1;
        float bestScore = float.MinValue;
        for (int k = 0; k < _near.Count; k++)
        {
            int i = _near[k];
            if (cover.ClaimedBy(i) is int holder && holder != -1 && holder != Self.Id)
            {
                continue;
            }

            CoverPoint p = cover.Points[i];
            Vector3 dir = (p.Position - enemy) with { Y = 0f };
            float distance = dir.Length();
            if (distance < _b.MinThreatDistance || !p.CanShoot)
            {
                continue;
            }

            float angle = MathF.Acos(Math.Clamp(Vector3.Dot(dir / distance, line), -1f, 1f));
            if (angle < _b.FlankMinAngle || angle > MathF.PI - 0.5f)
            {
                continue; // not off to the side
            }

            if (Clear(threatEye, p.Position + new Vector3(0f, head + 0.05f, 0f)) || !Clear(ShootingEye(p, lastChest), lastChest))
            {
                continue; // they'd see it coming, or there's no shot from it
            }

            float score = -MathF.Abs(angle - MathF.PI * 0.5f) * 3f - FlatDistance(Self.Position, p.Position) * 0.15f -
                          MathF.Abs(distance - Archetype.EngageRange) * 0.2f - Exposure(Self.Position, p.Position, enemy) * 8f +
                          (jitter ? _rng.NextFloat() * 0.5f : 0f);
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// A flanker at its spot: first it looks out from it for a while (the spot was picked for its view of where they
    /// were, so seeing them there means a shot from the side), then it searches from there.
    /// </summary>
    private void LookOutOrSearch(Awareness f)
    {
        if (Phase == CoverPhase.None && _cover >= 0 && _b.FlankLook > 0f)
        {
            SetPhase(CoverPhase.Peeking, _b.FlankLook);
        }
        else if (Phase != CoverPhase.Peeking || _phaseTime >= _phaseLength)
        {
            StartSearch(f.LastKnown);
        }
    }

    /// <summary>Share of a walk from <paramref name="from"/> to <paramref name="to"/> in sight of someone at <paramref name="threat"/> (0..1).</summary>
    private float Exposure(Vector3 from, Vector3 to, Vector3 threat)
    {
        Vector3 eye = threat + new Vector3(0f, _sim.Config.Movement.StandEyeHeight, 0f);
        int seen = 0;
        for (int k = 1; k <= ExposureSamples; k++)
        {
            Vector3 at = Vector3.Lerp(from, to, k / (float)(ExposureSamples + 1)) + new Vector3(0f, 1.2f, 0f);
            if (Clear(eye, at))
            {
                seen++;
            }
        }

        return seen / (float)ExposureSamples;
    }

    /// <summary>
    /// Flanking with held fire: keep quiet while the enemy hasn't noticed (they're facing well away) and isn't close; once
    /// they turn this way, come close or the flanker is under fire, it opens up.
    /// </summary>
    private bool HoldingFire(Awareness f)
    {
        if (!Archetype.HoldFireWhileFlanking || _arrived || f.SinceShotAt < 1f || _sim.FindPlayer(f.TargetId) is not { } target)
        {
            return false;
        }

        Vector3 toMe = (Self.Position - target.Position) with { Y = 0f };
        float distance = toMe.Length();
        if (distance < Archetype.CloseRange)
        {
            return false;
        }

        float facing = Vector3.Dot(ViewAngles.FlatForward(target.Yaw), toMe / MathF.Max(distance, 1e-3f));
        return facing < MathF.Cos(_b.NoticedAngle);
    }

    /// <summary>A careful shooter has fired enough from here, or balls are landing round it: time to move.</summary>
    private bool ShouldRelocate(Awareness f)
    {
        if (_cover < 0 || _phaseTime < 0.4f)
        {
            return false;
        }

        return (Archetype.RelocateAfterShots > 0 && _shotsHere >= Archetype.RelocateAfterShots) ||
               (Archetype.RelocateWhenShotAt && f.SinceShotAt < 0.3f);
    }

    /// <summary>Tells teammates within earshot where <paramref name="target"/> is.</summary>
    private void ShareContact(PlayerState target)
    {
        _squad.Share(Self, target.Id, target.Position);
        _sinceShare = 0f;
    }

    /// <summary>On the case's side: a shout when it picks the case up, or when the teammate carrying it goes out close by.</summary>
    private void NoticeCase()
    {
        if (_sim.Match?.Objective is not { Kind: ObjectiveKind.Retrieve } objective || Self.Team != objective.Attackers)
        {
            return;
        }

        int carrier = objective.Carrier;
        if (carrier != _lastCarrier)
        {
            if (carrier == Self.Id)
            {
                Shout(CalloutKind.CaseTaken, force: true);
            }
            else if (carrier < 0 && _lastCarrier != Self.Id && !objective.Done &&
                     Vector3.DistanceSquared(objective.CasePosition, Self.Position) < _b.CalloutRange * _b.CalloutRange)
            {
                Shout(CalloutKind.CaseDown, force: true);
            }

            _lastCarrier = carrier;
        }
    }

    /// <summary>A teammate going out close by gets a shout.</summary>
    private void NoticeTeammatesOut(ReadOnlySpan<Events.SimEvent> heard)
    {
        for (int i = 0; i < heard.Length; i++)
        {
            ref readonly Events.SimEvent e = ref heard[i];
            if (e.Type == Events.SimEventType.PlayerEliminated && e.TargetId != Self.Id && _sim.FindPlayer(e.TargetId) is { } mate &&
                mate.Team == Self.Team && Vector3.DistanceSquared(mate.Position, Self.Position) < _b.CalloutRange * _b.CalloutRange)
            {
                Shout(CalloutKind.ManDown);
            }
        }
    }

    private static float YawOf(Vector3 direction) => MathF.Atan2(-direction.X, -direction.Z);

    private void StartSearch(Vector3 centre)
    {
        SetMode(BotMode.Search);
        _searchCentre = centre;
        _searchLeft = Archetype.SearchTime;
        _hopsLeft = _b.SearchHops;
        _lookAround = 0f;
        GoTo(centre, BotGait.Walk);
    }

    private void ActSearch(float dt)
    {
        _searchLeft -= dt;
        if (_lookAround > 0f)
        {
            // Turn on the spot to look around.
            _lookAround -= dt;
            Stop();
            _wantYaw = _yaw + 1.8f;
            _wantPitch = 0f;
            return;
        }

        if (_arrived || !_hasGoal)
        {
            if (_hopsLeft <= 0)
            {
                Stop();
                return;
            }

            _hopsLeft--;
            _lookAround = 1.5f;
            Vector3 offset = new(_rng.Symmetric(_b.SearchRadius), 0f, _rng.Symmetric(_b.SearchRadius));
            if (_squad.Grid.TrySnap(_searchCentre + offset, out Vector3 spot))
            {
                GoTo(spot, BotGait.Walk);
            }

            return;
        }

        FollowPath(dt);
        LookAlongPath();
    }

    /// <summary>Aims at the target's chest (or head, if that's all that shows), leading and holding over, and fires when it's right.</summary>
    private void Aim(PlayerState target, Awareness f, float dt, bool mayFire = true)
    {
        Vector3 eye = Self.EyePosition;
        Vector3 chest = target.Position + new Vector3(0f, target.EyeHeight * 0.72f, 0f) + target.LeanOffset * 0.6f;
        Vector3 point = Clear(eye, chest) ? chest : target.EyePosition;
        (float yaw, float pitch) = BotAim.Solve(_sim.Config, eye, point, f.EstimatedVelocity);

        // The error starts anywhere within the bound when the target (re)appears (a snap shot), then wanders
        // within a bound that shrinks while they keep a steady target in their sights.
        _steady = target.HorizontalSpeed > 2.5f ? MathF.Max(0f, _steady - dt * 2f) : _steady + dt;
        float bound = Tier.AimError * (1f - 2f / 3f * MathF.Min(1f, _steady / _b.AimSettleTime));
        if (!_wasVisible)
        {
            _errorYaw = _rng.Symmetric(bound);
            _errorPitch = _rng.Symmetric(bound);
        }

        _errorYaw = Math.Clamp(_errorYaw + _rng.Symmetric(bound) * 4f * dt, -bound, bound);
        _errorPitch = Math.Clamp(_errorPitch + _rng.Symmetric(bound) * 4f * dt, -bound, bound);
        _wantYaw = yaw + _errorYaw;
        _wantPitch = pitch + _errorPitch;
        _turnSpeed = Tier.TurnSpeed;

        if (_triggerHeld > 0)
        {
            _triggerHeld--;
            _cmd.Buttons |= InputButtons.Fire;
            return;
        }

        bool reacted = f.SpottedFor >= Tier.ReactionTime * f.ReactionScale;
        bool onTarget = MathF.Abs(BotAim.Wrap(_wantYaw - _yaw)) < _b.AimTolerance && MathF.Abs(_wantPitch - _pitch) < _b.AimTolerance;
        bool ready = Self.MarkerReady && !Self.Sprinting && Self.Marker.Paint.Loader > 0 && Self.Marker.Air.CanFire;
        bool steadyEnough = Archetype.FireOnMove || Self.HorizontalSpeed < 0.8f;
        float range = Vector3.Distance(eye, point);
        // A careful shooter at range waits for its aim to settle on the target.
        bool settled = !Archetype.SteadyShots || range <= Archetype.CloseRange || _steady >= _b.AimSettleTime;
        if (!mayFire || !reacted || !onTarget || !ready || !steadyEnough || !settled || _sincePull < _nextPull)
        {
            return;
        }

        ShotSolution shot = _sim.SolveShot(Self);
        if (shot.MuzzleBlocked || Vector3.Distance(eye, shot.AimPoint) < range - 1.0f || TeammateInTheWay(eye, point))
        {
            return; // the barrel's behind cover, the line's blocked, or a teammate's in the way
        }

        _cmd.Buttons |= InputButtons.Fire;
        _triggerHeld = (int)TriggerTicks - 1;
        _sincePull = 0f;
        _nextPull = Tier.PullInterval * Archetype.PullScale * (0.85f + 0.4f * _rng.NextFloat());
        _shotsHere++;
    }

    private void Release() => _triggerHeld = 0;

    private bool TeammateInTheWay(Vector3 from, Vector3 to)
    {
        Vector3 line = to - from;
        float length2 = line.LengthSquared();
        IReadOnlyList<PlayerState> players = _sim.Players;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerState p = players[i];
            if (p == Self || p.Team != Self.Team || !p.Alive || !p.Present)
            {
                continue;
            }

            Vector3 chest = p.Position + new Vector3(0f, p.EyeHeight * 0.7f, 0f);
            float t = Vector3.Dot(chest - from, line) / MathF.Max(1e-4f, length2);
            if (t <= 0f || t >= 1f)
            {
                continue;
            }

            if (Vector3.DistanceSquared(from + line * t, chest) < _b.TeammateClearance * _b.TeammateClearance)
            {
                return true;
            }
        }

        return false;
    }

    private void MaybeRefill(bool force)
    {
        PaintSupply paint = Self.Marker.Paint;
        if (Self.Marker.Refill.Active || paint.PodsRemaining == 0 || paint.LoaderFull)
        {
            return;
        }

        if (force || paint.Loader < paint.Params.Capacity * _b.RefillBelow)
        {
            _cmd.Buttons |= InputButtons.Refill;
            if (Mode == BotMode.Engage)
            {
                Shout(CalloutKind.Refill);
            }
        }
    }

    private bool OutOfPaint() => Self.Marker.Paint.Loader == 0 && Self.Marker.Paint.PodsRemaining == 0 && !Self.Marker.Refill.Active;

    private bool TryFindPod(out Vector3 at)
    {
        at = default;
        Match.PickupSet pickups = _sim.Pickups;
        if (!pickups.Active)
        {
            return false;
        }

        float best = float.MaxValue;
        for (int i = 0; i < pickups.Items.Count; i++)
        {
            if (pickups.IsTaken(i) || pickups.Items[i].Kind != PickupKind.Pod)
            {
                continue;
            }

            float d = FlatDistance(Self.Position, pickups.Items[i].Position);
            if (d < best)
            {
                best = d;
                at = pickups.Items[i].Position;
            }
        }

        return best < float.MaxValue;
    }

    /// <summary>Eliminated: walk to the dead zone, marker up, and leave the field.</summary>
    private InputCommand WalkOff(float dt)
    {
        if (Mode != BotMode.Out)
        {
            Shout(CalloutKind.Hit, force: true);
            SetMode(BotMode.Out);
            _squad.Cover.Release(Self.Id);
            _cover = -1;
            GoTo(_sim.Level?.DeadZone ?? Home, BotGait.Stroll);
        }

        _outFor += dt;
        if (_outFor >= _b.WalkOffTime || (_hasGoal && _arrived) || (!_hasGoal && !_needPath))
        {
            Self.Present = false;
            return new InputCommand { Tick = _tick, Yaw = _yaw };
        }

        FollowPath(dt);
        LookAlongPath();
        Look(dt);
        _cmd.Yaw = _yaw;
        _cmd.Pitch = 0f;
        return _cmd;
    }

    /// <summary>Done with a lead or a fight: back to the post or patrol (hunters just carry on hunting).</summary>
    private void GoBack()
    {
        _onAlarm = false;
        if (Hunts || Attacking)
        {
            SetMode(BotMode.Idle);
            _hunting = false;
            Stop();
            return;
        }

        SetMode(BotMode.Return);
        GoHome();
    }

    private void GoHome()
    {
        Vector3 home = _dutyPost ?? (Route is { Points.Count: > 0 } route ? route.Points[_patrolIndex = NearestRoutePoint(route, Self.Position)] : Home);
        GoTo(home, _dutyPost is null ? BotGait.Stroll : BotGait.Walk);
    }

    private void GoTo(Vector3 goal, BotGait gait)
    {
        _goal = goal;
        _gait = gait;
        _hasGoal = true;
        _needPath = true;
        _arrived = false;
        _repaths = 0;
        _path.Clear();
        _waypoint = 0;
        _stuckFor = 0f;
        _bestDistance = float.MaxValue;
        _doorLeaf = -1;
    }

    private void Stop()
    {
        _hasGoal = false;
        _needPath = false;
        _path.Clear();
        _waypoint = 0;
        _cmd.Move = Vector2.Zero;
        _doorLeaf = -1;
    }

    /// <summary>
    /// A shut (or barely open) door between here and <paramref name="next"/>: stand clear of its swing on this side, face it,
    /// tap interact, and wait until it's open enough to walk through. True while dealing with one (don't walk on).
    /// </summary>
    private bool AtShutDoor(Vector3 next, float dt)
    {
        Level.DoorSet doors = _sim.Doors;
        if (doors.Count == 0)
        {
            return false;
        }

        float pass = _sim.Config.Rules.Doors.BotPassOpen;
        int leaf = doors.ShutOnPath(Self.Position, next, pass, _squad.Config.Navigation.AgentRadius);
        if (leaf < 0)
        {
            _doorLeaf = -1;
            return false;
        }

        if (leaf != _doorLeaf)
        {
            _doorLeaf = leaf;
            _doorTime = 0f;
            _doorTaps = 0;
        }

        _doorTime += dt;
        Level.DoorSpec s = doors[leaf];
        Vector3 c = s.ShutCenter;
        float side = Vector3.Dot(Self.Position - c, s.Side) >= 0f ? 1f : -1f;
        bool towardUs = !s.Sliding && !s.BothWays && side > 0f;
        Vector3 stand = towardUs ? ClearOfSwing(s, c, side) : c + s.Side * (side * 0.55f);
        stand.Y = Self.Position.Y;
        // Look at the leaf about chest high: its nearest point is then in reach and in the middle of the view.
        _doorFace = c + new Vector3(0f, MathF.Min(1.2f, s.Height * 0.6f), 0f);
        _stuckFor = 0f;
        _bestDistance = float.MaxValue;
        if (FlatDistance(Self.Position, stand) > 0.3f)
        {
            // The leaf in the way (a door standing ajar this way): step straight out from the doorway first, clear of it.
            Vector3 via = stand;
            if (doors.Blocks(Self.Position, stand, _squad.Config.Navigation.AgentRadius))
            {
                via = c + s.Side * (side * (s.Width + 0.5f));
                via.Y = Self.Position.Y;
            }

            MoveTowards(via, BotGait.Walk, 0f);
            return true;
        }

        _cmd.Move = Vector2.Zero;
        bool heading = doors.Target(leaf) >= pass;
        if (!heading && _doorTaps < 3 && _doorTime >= _doorTaps * 1.5f &&
            doors.FindTarget(Self.EyePosition, ViewAngles.Forward(_yaw, _pitch)) == leaf)
        {
            _cmd.Buttons |= InputButtons.Interact; // a one-tick press: let go next tick, so it swings all the way
            _doorTaps++;
        }

        if (_doorTime > 6f)
        {
            // Won't open (someone's holding it, or in its way): give this goal up.
            _doorLeaf = -1;
            _hasGoal = false;
            _arrived = true;
        }

        return true;
    }

    /// <summary>
    /// Where to stand to open a door that swings this way: beside the doorway, out of the swing but within reach: on the
    /// latch side of a single leaf (a pair meets in the middle, so beyond the hinge), else straight out as far as reach allows.
    /// </summary>
    private Vector3 ClearOfSwing(Level.DoorSpec s, Vector3 c, float side)
    {
        float reach = _sim.Config.Rules.Doors.Reach;
        float along = s.Partner >= 0 ? -(s.Width * 0.5f + 0.55f) : s.Width * 0.5f + 0.45f;
        Vector3 beside = c + s.Across * along + s.Side * (side * 0.8f);
        if (_squad.Grid.TrySnap(beside, out Vector3 snapped) && FlatDistance(snapped, beside) < 0.25f)
        {
            return beside;
        }

        return c + s.Side * (side * MathF.Min(s.Width + 0.35f, reach - 0.4f));
    }

    /// <summary>Walks the current path toward the goal, planning it when the squad's search budget allows.</summary>
    private void FollowPath(float dt)
    {
        if (!_hasGoal)
        {
            return;
        }

        if (_needPath)
        {
            if (!_squad.TryReserveSearch(_tick))
            {
                return; // wait for a search slot
            }

            _needPath = false;
            _waypoint = 0;
            if (!_squad.Grid.FindPath(Self.Position, _goal, _path))
            {
                _hasGoal = false; // unreachable: give up on it
                _arrived = true;
                return;
            }
        }

        if (_waypoint >= _path.Count)
        {
            _arrived = true;
            return;
        }

        Vector3 next = _path[_waypoint];
        bool last = _waypoint == _path.Count - 1;
        float distance = FlatDistance(Self.Position, next);
        float radius = last ? ArriveRadius : _squad.Config.Navigation.WaypointRadius;
        if (distance <= radius)
        {
            _waypoint++;
            _stuckFor = 0f;
            _bestDistance = float.MaxValue;
            if (_waypoint >= _path.Count)
            {
                _arrived = true;
                _cmd.Move = Vector2.Zero;
                return;
            }

            next = _path[_waypoint];
            distance = FlatDistance(Self.Position, next);
        }

        if (AtShutDoor(next, dt))
        {
            return;
        }

        // Stuck: no progress toward the waypoint for a while means plan again (a few times at most).
        if (distance < _bestDistance - 0.1f)
        {
            _bestDistance = distance;
            _stuckFor = 0f;
        }
        else if ((_stuckFor += dt) >= _squad.Config.Navigation.StuckTime)
        {
            _stuckFor = 0f;
            _bestDistance = float.MaxValue;
            if (++_repaths > 3)
            {
                _hasGoal = false;
                _arrived = true;
                return;
            }

            _needPath = true;
            return;
        }

        MoveTowards(next, _crouchMove ? BotGait.Walk : _gait, 0f);
        if (_crouchMove)
        {
            _cmd.Buttons |= InputButtons.Crouch;
        }
    }

    /// <summary>Sets the move input toward <paramref name="spot"/> relative to the current facing (so bots strafe while aiming).</summary>
    private void MoveTowards(Vector3 spot, BotGait gait, float deadZone)
    {
        Vector3 d = spot - Self.Position;
        d.Y = 0f;
        float length = d.Length();
        if (length <= MathF.Max(deadZone, 0.02f))
        {
            _cmd.Move = Vector2.Zero;
            return;
        }

        d /= length;
        float scale = MathF.Min(1f, length / 0.25f + 0.2f);
        if (gait == BotGait.Stroll)
        {
            scale *= _b.StrollPace;
        }

        _cmd.Move = new Vector2(Vector3.Dot(d, ViewAngles.Right(_yaw)), Vector3.Dot(d, ViewAngles.FlatForward(_yaw))) * scale;
        if (gait is BotGait.Walk or BotGait.Stroll)
        {
            _cmd.Buttons |= InputButtons.Walk;
        }
    }

    private void LookAlongPath()
    {
        if (_hasGoal && _waypoint < _path.Count)
        {
            LookToward(_path[_waypoint], slow: true);
        }
    }

    private void LookToward(Vector3 at, bool slow)
    {
        Vector3 eye = Self.EyePosition;
        Vector3 d = at + new Vector3(0f, 1.3f, 0f) - eye;
        if (d.X * d.X + d.Z * d.Z < 0.01f)
        {
            return;
        }

        (float yaw, float pitch) = ViewAngles.FromDirection(d);
        _wantYaw = yaw;
        _wantPitch = Math.Clamp(pitch, -0.6f, 0.6f);
        if (!slow)
        {
            _turnSpeed = Tier.TurnSpeed;
        }
    }

    private void Look(float dt)
    {
        float step = _turnSpeed * dt;
        _yaw = BotAim.Wrap(BotAim.TurnTowards(_yaw, _wantYaw, step));
        _pitch = Math.Clamp(BotAim.TurnTowards(_pitch, _wantPitch, step), -_sim.Config.Movement.MaxPitch, _sim.Config.Movement.MaxPitch);
    }

    private void SetMode(BotMode mode)
    {
        if (mode == Mode)
        {
            return;
        }

        if (Mode == BotMode.Engage || mode != BotMode.Engage)
        {
            SetPhase(CoverPhase.None, 0f);
        }

        if (mode is not (BotMode.Engage or BotMode.Flank))
        {
            _squad.Cover.Release(Self.Id);
            _cover = -1;
        }

        Mode = mode;
        _modeTime = 0f;
    }

    private void Shout(CalloutKind kind, bool force = false)
    {
        if (!force && _sinceCallout < _b.CalloutCooldown)
        {
            return;
        }

        Callout = kind;
        CalloutTick = _tick;
        _sinceCallout = 0f;
    }

    private void SetPhase(CoverPhase phase, float length)
    {
        Phase = phase;
        _phaseTime = 0f;
        _phaseLength = length;
    }

    private bool Clear(Vector3 from, Vector3 to) => !_sim.Collision.SweepSphere(from, to, 0f, out _);

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private static float YawTo(Vector3 from, Vector3 to) => MathF.Atan2(-(to.X - from.X), -(to.Z - from.Z));

    private static int NearestRoutePoint(PatrolRoute route, Vector3 position)
    {
        int best = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < route.Points.Count; i++)
        {
            float d = FlatDistance(route.Points[i], position);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }

        return best;
    }
}
