using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Pb.Game.Ai;
using Pb.Game.Audio;
using Pb.Game.Ballistics;
using Pb.Game.Net;
using Pb.Game.Player;
using Pb.Game.Ui;
using Pb.Game.World;
using Pb.Net;
using Pb.Net.Client;
using Pb.Net.Lobby;
using Pb.Net.Protocol;
using Pb.Net.Server;
using Pb.Sim;
using Pb.Sim.AI;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;

namespace Pb.Game.Core;

/// <summary>
/// Composition root of a compound level: loads data, builds the sim and the level, wires every
/// presentation system to them, and runs the round (briefing card → live → summary). The level, mode,
/// size and difficulty come from the menus (<see cref="GameSession"/>) or, run directly, from user args
/// after "--":
///   --level=ID            which area to load (default: the first in levels/areas.jsonc); given to the
///                         main scene, it skips the menu once (exported builds always start there)
///   --place=ID            where in it (its level's "places"; default: the first, the whole area)
///   --mode=ID             which mode (rules.jsonc "modes": solo, ffa, teams; default: the first)
///   --size=N              how many: opponents (solo), players (free-for-all) or players a side (teams);
///                         default: the mode's default size
///   --tier=ID             which difficulty tier (default: "normal", or the level's first)
///   --smoke-test[=ticks]  headless CI check: walk in through the gate firing, exit code 0/1
///   --shots               camera tour of the level's viewpoints (screenshots with --write-movie); --views=…
///                         gives your own instead ("x,y,z,yaw,pitch" or "x,y,z>tx,ty,tz", separated by ";")
///   --posture-demo        scripted lean / shoulder swap / muzzle-in-cover sequence at a wall corner
///   --splat-demo          shots at the nearest wall at a glance (streaks) and square on (round splats), then a look at them
///   --duel-demo           scripted elimination of an opponent, then of you (mask spray, spectator view)
///   --duel-distance=M     how far from the bot the duel starts (default 6 m; 2 for a close-up)
///   --round-tour          the round's screens in order: briefing, pause menu, a duel, spectator view, summary
///   --role-demo=ROLE      a Marksman ("marksman") or a Flanker ("flanker") at work, with the bot overlay
///   --bot-demo            bots fighting you from cover, seen from above with the F3 overlay, then through your eyes
///   --gait-demo           an opponent walks, runs, sprints, pulls up, strafes, backs off, walks crouched and looks round
///   --gait-only=NAME      with --gait-demo, only the moves whose names start with NAME (e.g. "look", "sprint")
///   --cover-demo          an opponent tucks in behind low cover, stands to shoot over it and tucks in again, from the side
///   --cover-at=X,Z        with --cover-demo, the low cover nearest that point (else the nearest out in the open)
///   --ladder-demo         an opponent climbs a ladder, steps off at the top, turns round and climbs down, from the side
///   --ladder=N            with --ladder-demo, the level's Nth ladder (else the tallest)
///   --place-stills=DIR    takes the menu's picture of each of the level's places into DIR (res://ui/places), then quits
///   --bot-match           CI: a bot plays your slot (it hunts round the opponent spawns) until the round ends
///   --time-limit=SECONDS  overrides the tier's time limit (keeps the bot match short in CI)
///   --preset=NAME         uses that graphics preset instead of the saved one (for comparing their cost)
///   --render-scale=S      draws the 3D view at this share of the screen's resolution instead of the saved one
///   --seed=N              replays the round seed N deals, exactly: its starts and randomness, not moved off the last
///                         round's (rounds and bot matches normally get a new random seed and start away from the last
///                         round's starts, printed as "Starts (seed N, ...)"; other scripted runs use the data's seed and,
///                         in solo, the level's roster, so they play out the same every time)
///   --random-spawns       deals random starts in a scripted solo run too (CI's bot match; other modes always do)
///   --record              the bot match goes in your records as a round of yours (CI, to exercise the profile)
///   --max-pop=CM          the bot match fails if any character's head or hands jump further than this in a frame
///   --min-contact-marks=N the bot match fails with fewer than N bot teammates' contacts marked on your HUD
///   --fast                no cap on sim ticks a frame, so a low --fixed-fps runs the round in few frames (to film a
///                         whole bot match with --write-movie: --fixed-fps 1 is a second of the round a frame)
///   --show-summary        the bot match ends on its summary, up for three seconds (for a screenshot)
/// Scripted runs skip the briefing and the summary, and keep the bots passive until a script wakes
/// them. In solo, bots play their spawn's behaviour; in free-for-all and teams, one dealt from the
/// mode's chances. All play at the tier's difficulty; F3 shows what they're thinking.
/// </summary>
public partial class LevelMain : Node3D, ISimEventListener
{
    private GameData _data = null!;
    private PresentationDef _view = null!;
    private GameSettings _settings = null!;
    private LevelLayout _level = null!;
    private AreaEntryDef _entry = null!;
    private TierDef _tier = null!;
    private GameMode _mode = null!;
    private int _size;
    private ObjectiveChoice _objective = null!;
    private RoundInfo _round = null!;
    private MatchState _match = null!;
    private PauseMenu _pause = null!;
    private CanvasLayer _overlays = null!;
    private Control? _overlay;
    private SimWorld _sim = null!;
    private SimDriver _driver = null!;
    private LevelBuilder _world = null!;
    private PlayerController _player = null!;
    private BallRenderer _balls = null!;
    private SplatSystem _splats = null!;
    private ArcPreview _arc = null!;
    private Hud _hud = null!;
    private WorldEnvironment _environment = null!;
    private DirectionalLight3D _sun = null!;
    /// <summary>Every bot in the round: opponents and, in teams, your teammates.</summary>
    private readonly List<OpponentPawn> _pawns = new();
    private readonly List<BotBrain> _bots = new();
    private int[] _calloutsSeen = Array.Empty<int>();
    private int _contactsMarked;
    private int _contactsSent;
    private BotSquad _squad = null!;
    private BotDebugOverlay _botDebug = null!;
    private LevelSmokeTest? _smoke;
    private SpectatorView? _spectator;
    private HitMoment _hitMoment = null!;
    private System.Numerics.Vector3? _lastHitTravel;
    private ViewModelPaint _markerPaint = null!;
    private MaskSprayOverlay _maskSpray = null!;
    private FlybyWatch _flybys = null!;
    private PickupVisuals _pickups = null!;
    private WeedField _weeds = null!;
    private FootDust _dust = null!;
    private PuddleRipples _ripples = null!;
    private RoofDrips? _roofDrips;
    private Footprints _prints = null!;
    private PaintDrips _drips = null!;
    private Birds _birds = null!;
    private AudioDirector _audio = null!;
    private RefereeCalls _referee = null!;
    private System.Runtime.GCLatencyMode _latencyBefore = System.Runtime.GCSettings.LatencyMode;
    private readonly int[] _collectionsAtLoad = new int[3];

    /// <summary>The sound (for the smoke test's report).</summary>
    public AudioDirector Audio => _audio;

    /// <summary>The referee (for the smoke test's report).</summary>
    public RefereeCalls Referee => _referee;
    private GroundDetail _groundDetail = null!;
    private FloorDebris _floorDebris = null!;
    private OldPaint _oldPaint = null!;
    private ContactShadows _contact = null!;
    private Creepers _creepers = null!;
    private LightShafts _shafts = null!;
    private DoorViews _doors = null!;
    private string? _hitBy;
    private bool _scripted;
    private bool _botMatch;
    private SpawnPlan? _starts;
    private SpawnPoint _start;
    private Color _teamColor;
    private bool _summaryShown;
    private bool _summaryEarly;
    private bool _ready;
    /// <summary>A real round (not a scripted run or a tour): it goes in your records when it ends.</summary>
    private bool _counts;

    /// <summary>Playing with others: the session (null offline).</summary>
    private NetSession? _net;

    /// <summary>The round's setup when playing with others: cast here when hosting, as sent when joined.</summary>
    private RoundSetupMessage? _netSetup;

    /// <summary>Hosting: the round as cast (each bot's start), and who plays which person in it, in order after you.</summary>
    private CastRound? _cast;
    private readonly List<ClientLink> _netPeople = new();

    /// <summary>Hosting: the lobby member each person in the round is, in the roster's order (you first).</summary>
    private readonly List<int> _netMembers = new();

    /// <summary>Joined: the round on this copy (prediction, everyone else posed from snapshots, the server's events).</summary>
    private ClientSession? _session;

    /// <summary>Hosting: how long the round has waited for everyone to build it, and the countdown once they have (s).</summary>
    private double _netWaited;
    private double _netCountdown = -1;
    private bool _netOverSent;
    private bool _netGone;
    private bool _netLeft;
    private bool _netScored;
    private NetServer? _listening;

    /// <summary>The summary: the lobby's version it was shown with (shown again when the score comes), and, hosting, the time left before going back to the lobby by itself.</summary>
    private int _summaryLobby;
    private double _backIn = -1;

    /// <summary>The scoreboard (held on Tab), teammates' callout marks, and playing with others the chat.</summary>
    private Scoreboard _scoreboard = null!;
    private CalloutMarks _marks = null!;
    private ChatBox? _chat;
    private double _sinceSnapshotCheck;
    private int _callouts;
    private double _snapAt = -1;
    private int _snaps;

    /// <summary>Bot matches played with others so far (--rounds=N), and how many of them failed.</summary>
    private static int _roundsReported;
    private static int _failedRounds;

    /// <summary>Joined: the server's result and numbers have come; the bot match waits for them to report.</summary>
    private bool _netOver;
    private bool _reportWhenOver;
    private bool _reported;
    private int[] _calloutsSent = Array.Empty<int>();

    /// <summary>True once you've been eliminated and the spectator view is showing.</summary>
    public bool Spectating => _spectator is not null;

    public override void _Ready()
    {
        _hud = GetNode<Hud>("Hud");
        _driver = GetNode<SimDriver>("SimDriver");
        _world = GetNode<LevelBuilder>("World");
        _player = GetNode<PlayerController>("Player");
        _balls = GetNode<BallRenderer>("Balls");
        _splats = GetNode<SplatSystem>("Splats");
        _arc = GetNode<ArcPreview>("ArcPreview");
        _environment = GetNode<WorldEnvironment>("WorldEnvironment");
        _sun = GetNode<DirectionalLight3D>("Sun");

        try
        {
            var source = new GodotDataSource();
            _data = GameData.Load(source);
            _view = Jsonc.Load<PresentationDef>(source, PresentationDef.File);
            // The sounds are synthesised on a worker thread: start now, so they're ready by the time the level is built and
            // the round goes live (its start cue would otherwise wait for them).
            Pb.Game.Audio.SoundBank.Warm(_view.Audio.Variations);
            InputSetup.Apply(Jsonc.Load<InputDef>(source, InputDef.File));
            // Joined to a host, the round is the one it sent; otherwise the menus' choice (or the command line's).
            _net = NetSession.Current;
            _netSetup = _net?.Client is not null
                ? _net.Round ?? throw new InvalidOperationException("Joined to a host, but it hasn't sent a round to play.")
                : null;
            if (_net is not null)
            {
                _net.RoundWaiting = false;
            }

            // Hosting, it's the lobby's choice.
            LobbyChoices? hostChoice = _net?.Lobby?.State.Choices;
            (_entry, _tier, _mode, _size, _objective) = _netSetup is { } sent ? JoinedRound(_data, sent)
                : hostChoice is not null ? HostedRound(_data, hostChoice)
                : PickRound(_data);
            // The whole area, or the part of it chosen: walled in, with its own entries, starts, pickups and objectives.
            LevelLayout area = _data.Levels[_entry.Id];
            string? placeId = _netSetup is not null ? _netSetup.PlaceId
                : hostChoice is not null ? RoundChoices.Resolve(_data, hostChoice).Place.Id
                : GameSession.LevelId is not null ? GameSession.PlaceId : Args.Value("--place");
            if (placeId is not null && area.Places.All(p => p.Id != placeId))
            {
                throw new InvalidOperationException($"{area.DisplayName} has no place '{placeId}' (known: {string.Join(", ", area.Places.Select(p => p.Id))})");
            }

            _level = area.ForPlace(area.PlaceOf(placeId));
            if (!_level.Objectives.Offers(_objective.Kind))
            {
                throw new InvalidOperationException($"{area.DisplayName}: {_level.Place?.DisplayName} has no places for {_objective.DisplayName}");
            }

            _round = new RoundInfo(_level, _tier, _mode, _size, _objective);
        }
        catch (Exception ex) when (ex is DataException or InvalidOperationException)
        {
            GD.PushError(ex.Message);
            _hud.ShowFatal(ex.Message);
            // Headless, nobody can read the message and leave, so the run would wait for ever.
            if (Args.Has("--smoke-test") || DisplayServer.GetName() == "headless")
            {
                GetTree().QuitCleanly(1);
            }

            return;
        }

        _settings = GameSettings.Load(_view);
        InputSetup.Apply(_settings.Bindings);
        _view.UseTeamColors(_settings.TeamColors);
        Pb.Game.Audio.UiSounds.Volume_db = _view.Audio.Volume_db + _view.Audio.Mix.Menu;
        Pb.Game.Audio.UiSounds.Variations = _view.Audio.Variations;
        _settings.ApplyVolume();
        _settings.ApplyWindow();

        // Quiet opponents for the screenshot tours; the smoke test turns them hostile when it's ready.
        bool botDemo = Args.Has("--bot-demo");
        string? roleDemo = Args.Value("--role-demo");
        bool gaitDemo = Args.Has("--gait-demo");
        bool coverDemo = Args.Has("--cover-demo");
        bool ladderDemo = Args.Has("--ladder-demo");
        _botMatch = Args.Has("--bot-match");
        // Headless bot matches with the art on measure the characters' pops (CharacterPoser), printed with the result.
        Pb.Game.Player.CharacterPoser.TrackPops = _botMatch;
        _scripted = Args.Has("--shots") || Args.Has("--place-stills") || Args.Has("--posture-demo") || Args.Has("--splat-demo") || Args.Has("--duel-demo") || Args.Has("--smoke-test") || botDemo ||
                    roleDemo is not null || gaitDemo || coverDemo || ladderDemo || _botMatch || Args.Has("--objective-demo");
        bool roundTour = Args.Has("--round-tour");
        // Rounds played with others don't go in your records: they stay your bests alone.
        _counts = !_scripted && !roundTour && _net is null;

        // Every round deals a new seed and random starts away from the last round's, so you can't learn where everyone
        // is; so does a bot match, which plays like one of your rounds. Other scripted runs keep the data's seed and, in
        // solo, the level's roster, so they play out the same every time. --seed=N replays a round exactly (its starts
        // dealt from the seed alone, not moved off the last round's).
        bool repeatable = (_scripted && !_botMatch) || roundTour;
        bool seeded = ulong.TryParse(Args.Value("--seed"), out ulong given);
        bool remember = !repeatable && !seeded;
        ulong seed = _netSetup is { } joined ? joined.Seed
            : seeded ? given
            : repeatable && _net is null ? _data.Config.MatchSeed
            : (ulong)System.Random.Shared.NextInt64();
        _sim = new SimWorld(_data.Config, seed);
        _sim.LoadLevel(_level);
        // Everything built once from the level as it stands (cover, starts, weeds, old paint, light) leaves the doors
        // out: they'll move. The flag comes off once the level is built.
        _sim.Collision.SkipDynamic = true;
        var navWatch = Stopwatch.StartNew();
        _squad = BotSquad.ForLevel(_sim, _data.Bots, _level);
        double navMs = navWatch.Elapsed.TotalMilliseconds;
        // The sim's, the network's and the game's own methods compiled now, while the level loads, rather than on
        // their first call mid-round (a bot's first flank, the first elimination): each was a stall of a few ms.
        (int warmed, double warmMs) = WarmUp();
        // With an objective the opponents gather round it, so the starts are always dealt; so they are in part of an area.
        ObjectiveFocus? focus = ObjectiveFocus.For(_objective.Kind, _level.Objectives, _data.Config.Rules.Objectives, seed);
        if (_net is { Hosting: true })
        {
            CastWithEveryone(seed);
        }

        _starts = _net is not null ? null
            : !repeatable || Args.Has("--random-spawns") || _mode.Kind != MatchModeKind.Solo || focus is not null || _level.Place is { Whole: false }
            ? SpawnPlanner.Plan(_level, _squad.Cover, _sim.Collision, _data.Config.Rules.Spawning, _data.Bots,
                RoundShape.Of(_mode, _size, focus), _data.Config.Movement.StandEyeHeight, seed,
                // Scripted runs play out the same every time; your own rounds start away from where the last one did.
                remember ? SpawnHistory.For(_level.Id, _level.Place?.Id) : null)
            : null;
        if (_starts is not null && remember)
        {
            SpawnHistory.Remember(_level.Id, _level.Place?.Id, SpawnMemory.Of(_starts));
        }
        SpawnPoint start = _starts?.You ?? _level.PlayerSpawns[0];
        if (_starts is not null)
        {
            static string Describe(OpponentSpawn o) => $"{o.Id} {o.Roles[0]}{(o.Patrol is null ? "" : " on " + o.Patrol.Id)}";
            GD.Print($"Starts (seed {seed}, {_round.Line}): you at {start.Position}" +
                     (_starts.Teammates.Count > 0 ? "; with you: " + string.Join(", ", _starts.Teammates.Select(Describe)) : "") +
                     "; against you: " + string.Join(", ", _starts.Opponents.Select(Describe)));
        }

        PlayerState state;
        if (_netSetup is { } round)
        {
            // Everyone in the round's order, the same on every copy; you're player 0 when hosting.
            foreach (RosterEntry e in round.Roster)
            {
                PlayerState p = _sim.AddPlayer(e.PlayerId, e.Team, e.Position, e.Yaw);
                p.Name = e.Name;
            }

            int me = _net!.Client is not null ? round.YourPlayerId : 0;
            state = _sim.FindPlayer(me) ?? throw new InvalidOperationException("The round has no player for this copy.");
            start = new SpawnPoint(state.Position, state.Yaw);
            GD.Print($"NET round {round.Round} ({_round.Line}, seed {seed}): {string.Join(", ", round.Roster.Select(e => $"{e.Name}{(e.Person ? "" : " (bot)")} on {e.Team}"))}");
        }
        else
        {
            state = _sim.AddPlayer(0, 0, start.Position, start.Yaw);
            state.Name = "You";
        }

        _start = start;
        if (_view.TeamColors.Length < _mode.PlayersFor(_size) && _mode.Kind == MatchModeKind.FreeForAll)
        {
            GD.PushWarning($"presentation.jsonc has {_view.TeamColors.Length} team colours for a {_mode.PlayersFor(_size)}-player free-for-all: some players share one");
        }

        Color teamColor = TeamColor(state.Team);
        _teamColor = teamColor;

        GraphicsPresetDef preset = _view.Graphics.Effective(Args.Value("--preset") ?? _settings.GraphicsPreset, _settings.Graphics);
        _world.Build(_level, new MaterialLibrary(_level.Materials), preset.AmbientProbes, _view.Horizon, _view.Woods);
        _doors = new DoorViews { Name = "Doors" };
        AddChild(_doors);
        _doors.Build(_sim, _world.Materials);
        var tracks = new TrackViews { Name = "Tracks" };
        AddChild(tracks);
        tracks.Build(_level, _world.Materials);
        var dressWatch = Stopwatch.StartNew();
        // The worn paths first: the weeds keep off them.
        var paths = new WornPaths { Name = "WornPaths" };
        AddChild(paths);
        paths.Plan(_level, _sim.Collision, _view.WornPaths, _squad.Cover.Points);
        _weeds = new WeedField { Name = "Weeds", Wind = _view.GroundWind };
        AddChild(_weeds);
        _weeds.Build(_level, _sim.Collision, _view.Weeds, paths.Clear);
        _weeds.Follow(_sim);
        paths.Draw(_level, _sim.Collision, _view.WornPaths);
        var cracks = new Cracks { Name = "Cracks" };
        AddChild(cracks);
        cracks.Build(_level, _sim.Collision, _view.Weeds, _view.Cracks);
        _groundDetail = new GroundDetail { Name = "GroundDetail" };
        AddChild(_groundDetail);
        _groundDetail.Build(_level, _sim.Collision, _view.GroundDetail);
        var fittings = new YardFittings { Name = "YardFittings" };
        AddChild(fittings);
        fittings.Build(_level, _sim.Collision, _view.YardFittings, _world.Materials);
        _floorDebris = new FloorDebris { Name = "FloorDebris" };
        AddChild(_floorDebris);
        _floorDebris.Build(_level, _sim.Collision, _view.FloorDebris);
        _oldPaint = new OldPaint { Name = "OldPaint" };
        AddChild(_oldPaint);
        _oldPaint.Build(_level, _sim.Collision, _squad.Cover.Points, _view.OldPaint);
        _creepers = new Creepers { Name = "Creepers" };
        AddChild(_creepers);
        _creepers.Build(_level, _sim.Collision, _view.Creepers);
        var runOff = new RunOff { Name = "RunOff" };
        AddChild(runOff);
        runOff.Build(_level, _sim.Collision, _world.Drips, _view.RunOff);
        var markings = new Markings { Name = "Markings" };
        AddChild(markings);
        markings.Build(_level, _view.Markings);
        _contact = new ContactShadows { Name = "ContactShadows" };
        AddChild(_contact);
        _contact.Build(_level, _view.ContactShadows);
        var cobwebs = new Cobwebs { Name = "Cobwebs" };
        AddChild(cobwebs);
        cobwebs.Build(_level, _view.Cobwebs);
        var hangings = new WallHangings { Name = "WallHangings" };
        AddChild(hangings);
        hangings.Build(_level, _view.WallHangings);
        var damp = new Damp { Name = "Damp" };
        AddChild(damp);
        damp.Build(_level, _sim.Collision, _view.Damp);
        var graffiti = new Graffiti { Name = "Graffiti" };
        AddChild(graffiti);
        graffiti.Build(_level, _view.Graffiti, _world.Piers);
        var bags = new SnaggedBags { Name = "SnaggedBags" };
        AddChild(bags);
        bags.Build(_level, _world.Strands, _view.SnaggedBags, _view.GroundWind);
        // Round a part of the area: tape wherever a walk could cross its edge.
        var boundary = new PlaceBoundary { Name = "PlaceBoundary" };
        AddChild(boundary);
        boundary.Build(_level, _view.PlaceBoundary, _view.GroundWind);
        var tatters = new RoofTatters { Name = "RoofTatters" };
        AddChild(tatters);
        tatters.Build(_level, _view.RoofTatters, _view.GroundWind);
        var litter = new BlowingLitter { Name = "BlowingLitter" };
        AddChild(litter);
        litter.Build(_level, _sim.Collision, _view.BlowingLitter, _view.GroundWind);
        _birds = new Birds { Name = "Birds" };
        AddChild(_birds);
        Pb.Sim.Collision.Aabb bounds = _level.Bounds;
        _birds.Build(new Vector3((bounds.Min.X + bounds.Max.X) * 0.5f, 0f, (bounds.Min.Z + bounds.Max.Z) * 0.5f), LevelBuilder.StableHash(_level.Id), _view.Birds, _level, _sim.Collision);
        _shafts = new LightShafts { Name = "LightShafts" };
        AddChild(_shafts);
        _shafts.Build(_level, _sim.Collision, _view.Lighting, _view.Shafts, _view.Dust, _view.WindowLight);
        GD.Print($"Level dressing: {_weeds.TuftCount} weed tufts, {paths.Count} worn paths ({paths.Length_m:0} m), {cracks.Count} cracks ({cracks.Length_m:0} m), {_groundDetail.CardCount} things on the ground, {_floorDebris.Count} on the floors indoors, {fittings.Count} manholes and drains, {_oldPaint.SplatCount} old paint splats, {_creepers.PatchCount} creepers, {runOff.Count} run-off streaks, {markings.CardCount} marking cards, {_contact.Count} contact shadows, {cobwebs.Count} cobwebs, {hangings.Count} things on the walls, {damp.Count} damp patches, {graffiti.Count} graffiti, {bags.Count} bags on the wire, {boundary.Length_m:0} m of tape on {boundary.PostCount} posts round the place, {litter.Count} bits of litter blowing about, {tatters.Count} tatters under the roof holes, {_birds.Count} birds, {_shafts.BeamCount} sunbeams, {_shafts.LightCount} window and bounce lights " +
                 $"in {dressWatch.Elapsed.TotalMilliseconds:0} ms");
        Atmosphere.ApplyLighting(_environment, _sun, _view.Lighting);
        ApplyGraphics(preset);

        _player.Initialize(_sim, state, _view, _settings, teamColor);
        _player.BuildBody(_view.Characters, teamColor, look: _netSetup?.Roster.FirstOrDefault(e => e.PlayerId == state.Id)?.Look ?? 0);
        if (_netSetup is not null)
        {
            SpawnOthers();
        }
        else
        {
            SpawnBots(hostile: botDemo || roleDemo is not null || _botMatch || (!_scripted && !roundTour));
        }
        _botDebug = new BotDebugOverlay { Name = "BotDebug" };
        AddChild(_botDebug);
        _botDebug.Initialize(_squad);
        _balls.Initialize(_sim.Ballistics, _view, state.Id, _player.VisualMuzzlePosition, RenderBounds());
        _splats.Initialize(_view, SplatParent, _doors.AnchorOf);
        var fx = GetNode<ImpactFx>("ImpactFx");
        fx.Initialize(_view);
        _dust = new FootDust { Name = "FootDust" };
        AddChild(_dust);
        _dust.Initialize(_sim, _view.FootDust, _view.GroundWind);
        _ripples = new PuddleRipples { Name = "PuddleRipples", Visible = _groundDetail.Visible };
        AddChild(_ripples);
        _ripples.Initialize(_groundDetail.Puddles, _view.Ripples);
        fx.Water = _ripples.InWater;
        _roofDrips = new RoofDrips { Name = "RoofDrips", Visible = _groundDetail.Visible };
        AddChild(_roofDrips);
        _roofDrips.Build(_level, _sim.Collision, _ripples, _view.RoofDrips, _sim.Config.Projectile.Gravity);
        _drips = new PaintDrips { Name = "PaintDrips", Moving = id => _sim.Doors.LeafOfCollider(id) >= 0 };
        AddChild(_drips);
        _drips.Initialize(_view);
        _prints = new Footprints { Name = "Footprints" };
        AddChild(_prints);
        _prints.Initialize(_sim, _view.Footprints, _view.TeamColors, _ripples.InWater);
        _arc.Initialize(_sim, state, _view);
        _arc.Enabled = _view.ArcPreview.EnabledOnStart;

        // Headless runs (CI) play through Godot's dummy driver: nothing is heard, but every sound is made and counted.
        _audio = GetNode<AudioDirector>("Audio");
        _audio.Initialize(_sim, state, _view, _level, _ripples.InWater, _birds);

        _hud.Initialize(_sim, state, _driver, _settings, _view, () => (_splats.ActiveCount, _splats.Capacity), () => _arc.Summary);
        _hud.InitializeMatch(_view, MatchClock);
        _hud.ShowHelp = false;
        MatchSetup setup = _netSetup is { } cast ? RoundWorld.MatchSetupOf(_data.Config, cast) : MatchSetup.From(_tier, state.Id, _mode.Kind, _objective.Kind);
        if (_netSetup is null &&
            float.TryParse(Args.Value("--time-limit"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float limit))
        {
            setup = new MatchSetup
            {
                HeroId = setup.HeroId, Mode = setup.Mode, TimeLimit = limit, StartPods = setup.StartPods, BotPods = setup.BotPods, Pickups = setup.Pickups,
                Objective = setup.Objective,
            };
        }

        _match = _sim.StartMatch(setup);
        _hud.InitializeObjective(_view.Objectives);
        BuildOthersHud(state);
        var objectiveViews = new ObjectiveViews { Name = "Objective" };
        AddChild(objectiveViews);
        objectiveViews.Build(_sim, _view.Objectives, _view.TeamColors);
        _pickups = new PickupVisuals { Name = "Pickups" };
        AddChild(_pickups);
        _pickups.Build(_sim.Pickups);
        _overlays = new CanvasLayer { Name = "Overlays", Layer = 8 };
        AddChild(_overlays);
        _pause = new PauseMenu { Name = "Pause" };
        AddChild(_pause);
        _pause.Build(_settings, _view, s => ApplyGraphics(_view.Graphics.Effective(s.GraphicsPreset, s.Graphics)),
            restart: _net is null ? () => GetTree().ReloadCurrentScene() : null, hudChanged: _ => _hud.ApplySettings(),
            quit: _net is null ? null : LeaveGame);

        _maskSpray = new MaskSprayOverlay { Name = "MaskSpray" };
        AddChild(_maskSpray);
        _maskSpray.Initialize(_view, state.Id, _settings);

        // Hit feedback (Phase 5): the moment you're hit, and balls whizzing past your head (heard, and an arc on the HUD).
        _hitMoment = new HitMoment { Name = "HitMoment" };
        AddChild(_hitMoment);
        _hitMoment.Initialize(_view.Feedback.HitMoment);
        _hud.ViewYaw = () => _player.Yaw;
        _flybys = new FlybyWatch { Name = "Flybys" };
        AddChild(_flybys);
        _flybys.Initialize(_sim, state, _view.Feedback, _driver);
        _markerPaint = new ViewModelPaint { Name = "MarkerPaint" };
        AddChild(_markerPaint);
        _markerPaint.Initialize(_player.ViewModel, state, _view);
        _driver.AddListener(_markerPaint);
        _flybys.Passed += flyby =>
        {
            _audio.Whizz(flyby.Point.ToGodot(), flyby.Velocity.Length(), flyby.Distance, _flybys.Radius);
            _hud.Flyby(flyby.Velocity, flyby.Distance);
        };

        _sim.Collision.SkipDynamic = false;
        _driver.Initialize(_sim);
        _driver.Ticked += _ =>
        {
            _doors.Capture();
            objectiveViews.Capture();
        };
        // One driver for each player, in the sim's order: its step takes their commands in that order.
        foreach (PlayerState p in _sim.Players)
        {
            _driver.AddDriver(p == state ? _player : _pawns.First(o => o.State == p));
        }

        _driver.AddListener(_maskSpray);
        _driver.AddListener(_pickups);
        _driver.AddListener(_balls);
        _driver.AddListener(_splats);
        _driver.AddListener(fx);
        _driver.AddListener(_dust);
        _driver.AddListener(_ripples);
        _driver.AddListener(_prints);
        _driver.AddListener(_drips);
        _driver.AddListener(_birds);
        _driver.AddListener(_audio);
        _referee = new RefereeCalls { Name = "Referee" };
        AddChild(_referee);
        _referee.Initialize(_sim, state, _view.Hud.Referee, _hud, _audio, _view.Hud.RefereePacing);
        _driver.AddListener(_referee);
        _driver.AddListener(_hud);
        _driver.AddListener(this);
        if (_net is not null)
        {
            WireNetwork(state);
        }

        if (_scripted && _net is null)
        {
            _sim.GoLive();
        }

        if (Args.Ticks("--smoke-test", 1800) is { } ticks)
        {
            _smoke = new LevelSmokeTest(this, _sim, _driver, _player, _world, ticks, _pawns, _bots);
            _player.AutoPilot = _smoke.Pilot;
        }
        else if (Args.Has("--splat-demo"))
        {
            var demo = new SplatDemo(this, _sim);
            demo.Setup(_player);
            _player.AutoPilot = demo;
            _hud.ShowHelp = false;
            _hud.ShowPerf = false;
        }
        else if (Args.Has("--posture-demo"))
        {
            _player.Teleport(PostureDemo.Position, PostureDemo.Yaw);
            _player.AutoPilot = new PostureDemo(this, _sim);
            _hud.ShowHelp = false;
            _hud.ShowPerf = false;
        }
        else if (Args.Has("--objective-demo"))
        {
            _player.AutoPilot = new ObjectiveDemo(this, _sim, _player, _squad.Grid);
            _hud.ShowHelp = false;
            _hud.ShowPerf = false;
        }
        else if (Args.Has("--duel-demo"))
        {
            var demo = new DuelDemo(this, _sim, _pawns);
            demo.Setup(_player);
            _player.AutoPilot = demo;
            _hud.ShowHelp = false;
            _hud.ShowPerf = false;
        }
        else if (Args.Value("--place-stills") is { Length: > 0 } stills)
        {
            var take = new PlaceStills { Name = "PlaceStills" };
            AddChild(take);
            take.Start(_data.Levels[_entry.Id], stills, _hud, _player.ViewModel, _pawns.Select(p => (Node3D)p), _view.Camera.FarClip_m);
        }
        else if (Args.Has("--shots"))
        {
            var tour = new ViewpointTour { Name = "ViewpointTour" };
            AddChild(tour);
            tour.Start(_level.Viewpoints, _hud, _player.ViewModel, _view.Camera.FarClip_m);
        }
        else if (_botMatch)
        {
            var you = new OpponentSpawn { Id = "you", Position = start.Position, Yaw = start.Yaw, Roles = new[] { "hunter" } };
            BotBrain brain = _squad.Add(state, _data.Bots.Archetypes["hunter"], _data.Bots.Difficulty[_tier.Bots], you);
            _player.AutoPilot = Args.Has("--call-outs") ? new CallingPilot(brain) : new BotPilot(brain);
            _hud.ShowPerf = false;
            if (Args.Has("--fast"))
            {
                // The tick rate must stay as it is (walking moves by Godot's physics step), so a faster round is more
                // ticks a frame: a low --fixed-fps asks for them, and this lifts Godot's usual cap of eight.
                Engine.MaxPhysicsStepsPerFrame = 1024;
            }

            GD.Print($"BOT MATCH a hunter bot plays your slot in {_round.Line} with {_bots.Count} {_tier.Bots} bots, {setup.TimeLimit:0} s on the clock");
            // Every 10 s of the round: where your bot is, what it's doing, and how the objective stands (for CI's log).
            int every = (int)(10f * _sim.Config.TickRate);
            _driver.Ticked += tick =>
            {
                if (_match.Phase == MatchPhase.Live && tick % every == 0)
                {
                    ObjectiveState? o = _match.Objective;
                    string Flag(FlagState f) =>
                        $"team {f.Team}'s flag {(f.Carrier >= 0 ? "carried by " + _sim.FindPlayer(f.Carrier)?.Name : f.AtHome ? "home" : "down")} at {f.Position}";
                    string objective = o is null ? ""
                        : o.Kind == ObjectiveKind.Hold ? $"; {o.Room!.Name} {o.Status}, held {o.Held:0} s"
                        : o.Kind == ObjectiveKind.Flag ? "; " + string.Join(", ", o.Flags.Select(Flag))
                        : o.Kind == ObjectiveKind.Escort ? $"; the VIP {_sim.FindPlayer(o.Vip)?.Name} at {o.CasePosition}{(o.VipOut ? " (out)" : "")}"
                        : $"; case {(o.Carrier >= 0 ? "carried by " + _sim.FindPlayer(o.Carrier)?.Name : o.CaseMoved ? "dropped" : "untouched")} at {o.CasePosition}";
                    GD.Print($"BOT MATCH {_match.Elapsed:0} s: you at {state.Position} ({brain.Label}){objective}; " +
                             $"{_sim.Players.Count(p => p.Alive && p.Team == state.Team)} of yours and {_sim.Players.Count(p => p.Alive && p.Team != state.Team)} of theirs in");
                }
            };
        }
        else if (gaitDemo)
        {
            var demo = new GaitDemo { Name = "GaitDemo", Look = _view.Characters };
            AddChild(demo);
            float azimuth = Mathf.DegToRad(_view.Lighting.SunAzimuth_deg);
            demo.Start(_sim, _pawns, _hud, _view.Camera.FarClip_m, new System.Numerics.Vector3(Mathf.Sin(azimuth), 0f, -Mathf.Cos(azimuth)));
        }
        else if (coverDemo)
        {
            var demo = new CoverDemo { Name = "CoverDemo" };
            AddChild(demo);
            float azimuth = Mathf.DegToRad(_view.Lighting.SunAzimuth_deg);
            demo.Start(_sim, _squad, _pawns, _hud, _view.Camera.FarClip_m, new System.Numerics.Vector3(Mathf.Sin(azimuth), 0f, -Mathf.Cos(azimuth)));
        }
        else if (ladderDemo)
        {
            var demo = new LadderDemo { Name = "LadderDemo" };
            AddChild(demo);
            float azimuth = Mathf.DegToRad(_view.Lighting.SunAzimuth_deg);
            demo.Start(_sim, _pawns, _hud, _view.Camera.FarClip_m, new System.Numerics.Vector3(Mathf.Sin(azimuth), 0f, -Mathf.Cos(azimuth)));
        }
        else if (botDemo)
        {
            var demo = new BotDemo { Name = "BotDemo" };
            AddChild(demo);
            demo.Start(_sim, _player, _pawns, _botDebug, _hud, _view.Camera.FarClip_m);
        }
        else if (roleDemo is not null)
        {
            var demo = new RoleDemo { Name = "RoleDemo" };
            AddChild(demo);
            demo.Start(roleDemo, _sim, _squad, _player, _pawns, _botDebug, _hud, _view.Camera.FarClip_m);
        }
        else if (roundTour)
        {
            ShowOverlay(RoundScreens.Briefing(_round, BeginRound, BackToLevelSelect, BriefingMap(), _match.Objective));
            _hud.ShowPerf = false;
            var tour = new RoundTour { Name = "RoundTour" };
            AddChild(tour);
            tour.Start(() =>
            {
                var demo = new DuelDemo(this, _sim, _pawns);
                demo.Setup(_player);
                _player.AutoPilot = demo;
                BeginRound();
            }, () => _summaryShown, _pause, () => _spectator?.Following == true, ShowSummary);
        }
        else if (_net is not null)
        {
            ShowOverlay(RoundScreens.Briefing(_round, BeginRound, LeaveGame, BriefingMap(), _match.Objective,
                waiting: _net.Hosting ? "Waiting for everyone to be ready…" : "The round starts once everyone's in."));
        }
        else
        {
            ShowOverlay(RoundScreens.Briefing(_round, BeginRound, BackToLevelSelect, BriefingMap(), _match.Objective));
        }

        GD.Print($"Level {_level.Id} ({_round.Line}, {_pawns.Count} {_tier.Bots} bots): {_level.Primitives.Count} primitives, " +
                 $"{_world.MeshCount} meshes ({_world.ShapeCount} props built in code, {_world.FramedOpenings} framed openings and {_world.DressedBuildings} buildings with gutters or trusses, {_world.DressedWalls} dressed walls, {_world.SkirtedFaces} skirted wall faces, {_world.SceneryCount} pylons and poles beyond, {_world.ShapeTriangles} triangles), " +
                 $"{_world.ColliderCount} walking colliders, {_sim.Collision.Colliders.Count} paint colliders, {_doors.Count} door leaves, " +
                 $"{_squad.Grid.SpanCount} nav spans and {_squad.Cover.Points.Count} cover points in {navMs:0} ms, {warmed} methods compiled up front in {warmMs:0} ms, preset {preset.Name}, " +
                 $"art {(ArtFiles.Disabled ? "off" : "on")} ({_pawns.Count(o => o.Visual.HasModel)} bots drawn as models)");
        _ready = true;
        if (_net is { } session)
        {
            StartNetRound(session);
        }
    }

    public void OnSimEvent(in SimEvent e)
    {
        _squad.Hear(e);
        if (e.Type == NetEventTypes.Callout)
        {
            HearCallout(e);
            return;
        }

        if (e.Type == NetEventTypes.Contact)
        {
            // Joined: a bot's contact the host passed on (hosting, or alone, they're marked straight from the squad).
            if (_net?.Client is not null && _sim.FindPlayer(e.PlayerId) is { } caller)
            {
                MarkContact(e.PlayerId, e.Team, caller.EyePosition, e.TargetId, e.Position);
            }

            return;
        }

        if (e.Type == SimEventType.MatchPhaseChanged && e.Extra == (int)MatchPhase.Live && _net is not null && _overlay is not null && !_summaryShown)
        {
            // Playing with others the round goes live for everyone at once: the briefing card goes, the mouse is yours.
            CloseOverlay();
            if (_spectator is null)
            {
                Input.MouseMode = Input.MouseModeEnum.Captured;
            }
        }

        if (e.Type == SimEventType.CalledOut)
        {
            _callouts++;
            OnCalledOut(e);
        }

        if (e.Type == SimEventType.ShotFired && e.PlayerId == _player.State.Id)
        {
            _player.ViewModel.Kick();
        }
        else if (e.Type == SimEventType.PlayerEliminated)
        {
            OnEliminated(e);
        }
        else if (e.Type == SimEventType.RoundEnded)
        {
            OnRoundEnded();
        }
        else if (e.Type is SimEventType.BallBounced or SimEventType.BallBroke && PlayerHitboxes.IsPlayer(e.TargetId))
        {
            // A ball hitting a body makes it flinch away, whether it breaks or bounces off.
            int id = PlayerHitboxes.PlayerIdOf(e.TargetId);
            if (id == _player.State.Id && e.Type == SimEventType.BallBroke && e.Lethal)
            {
                _lastHitTravel = e.Velocity;
            }

            if (id == _player.State.Id)
            {
                if (_player.State.Alive)
                {
                    _player.Jolt(-e.Normal.ToGodot(), e.Value);
                }

                _player.Body?.Flinch(-e.Normal.ToGodot(), e.Value);
            }

            foreach (OpponentPawn pawn in _pawns)
            {
                if (pawn.State.Id == id)
                {
                    pawn.Visual.Flinch(-e.Normal.ToGodot(), e.Value);
                    break;
                }
            }
        }

        _smoke?.OnSimEvent(e);
    }

    /// <summary>
    /// The bots, at the round's random starts (or, in scripted solo runs, at the level's roster spawns), each
    /// on their team: your teammates on yours (0), opponents on team 1, or in free-for-all a team each. Each
    /// plays its start's behaviour at the tier's difficulty, and wears its team's colour.
    /// </summary>
    private void SpawnBots(bool hostile)
    {
        var parent = new Node3D { Name = "Bots" };
        AddChild(parent);
        string[] callsigns = DealCallsigns(_view.Hud.Callsigns, _sim.MatchSeed);
        var starts = new List<(OpponentSpawn Spawn, byte Team)>();
        if (_starts is null)
        {
            string[] roster = _entry.Roster;
            if (roster.Length < _size)
            {
                throw new InvalidOperationException($"{_entry.Id}'s roster lists {roster.Length} spawns, not {_size}");
            }

            starts.AddRange(roster.Take(_size).Select(id => (_level.OpponentSpawns.First(s => s.Id == id), (byte)1)));
        }
        else
        {
            starts.AddRange(_starts.Teammates.Select(m => (m, (byte)0)));
            starts.AddRange(_starts.Opponents.Select((o, i) => (o, _mode.Kind == MatchModeKind.FreeForAll ? (byte)(i + 1) : (byte)1)));
        }

        for (int i = 0; i < starts.Count; i++)
        {
            (OpponentSpawn spawn, byte team) = starts[i];
            PlayerState state = _sim.AddPlayer(i + 1, team, spawn.Position, spawn.Yaw);
            state.Name = callsigns[i % callsigns.Length];
            BotBrain brain = _squad.Add(state, _data.Bots.ArchetypeFor(spawn.Roles)!, _data.Bots.Difficulty[_tier.Bots], spawn);
            brain.Passive = !hostile;
            brain.RestlessAfter = _mode.RestlessAfter;
            var pawn = new OpponentPawn { Name = $"{(team == 0 ? "Teammate" : "Opponent")}_{spawn.Id}" };
            parent.AddChild(pawn);
            pawn.Initialize(_sim, state, TeamColor(team), new BotPilot(brain), _view.Characters, i, _view.MarkerModel);
            _pawns.Add(pawn);
            _bots.Add(brain);
        }

        _calloutsSeen = Enumerable.Repeat(-1, _bots.Count).ToArray();
    }

    /// <summary>A joining copy: the area, difficulty, mode, size and objective of the round the host sent.</summary>
    private static (AreaEntryDef Entry, TierDef Tier, GameMode Mode, int Size, ObjectiveChoice Objective) JoinedRound(GameData data, RoundSetupMessage setup)
    {
        AreaEntryDef entry = data.Areas.Areas.FirstOrDefault(a => a.Id == setup.LevelId)
            ?? throw new InvalidOperationException($"The host's round is in an area this copy doesn't have ('{setup.LevelId}').");
        MatchRules rules = data.Config.Rules;
        GameMode mode = rules.FindMode(setup.ModeId)
            ?? throw new InvalidOperationException($"The host's round is a mode this copy doesn't know ('{setup.ModeId}').");
        ObjectiveChoice objective = rules.Objectives.Find(setup.Objective)
            ?? throw new InvalidOperationException($"The host's round has an objective this copy doesn't know ({setup.Objective}).");
        TierDef tier = entry.Tiers.FirstOrDefault(t => t.Id == setup.TierId) ?? entry.Tiers[0];
        return (entry, tier, mode, setup.Size, objective);
    }

    /// <summary>Hosting: the round the lobby chose.</summary>
    private static (AreaEntryDef Entry, TierDef Tier, GameMode Mode, int Size, ObjectiveChoice Objective) HostedRound(GameData data, LobbyChoices choices)
    {
        ChosenRound round = RoundChoices.Resolve(data, choices);
        return (round.Entry, round.Tier, round.Mode, round.Size, round.Objective);
    }

    /// <summary>
    /// Hosting: casts the round with everyone in the game (you first, then whoever has joined, in the order they came) and
    /// bots in the places left, everyone's start dealt from the round's seed. The round is made big enough for everyone.
    /// </summary>
    private void CastWithEveryone(ulong seed)
    {
        NetSession session = _net!;
        NetServer server = session.Server!;
        // Everyone in the lobby, in the order they came (you first), with the side and character they chose there.
        var people = new List<Person>();
        _netPeople.Clear();
        _netMembers.Clear();
        foreach (LobbyMember m in session.Lobby!.State.Members)
        {
            ClientLink? link = m.Host ? null : server.Clients.FirstOrDefault(c => c.Welcomed && c.Peer == m.Id);
            if (!m.Host && link is null)
            {
                continue;
            }

            var person = new Person(m.Name, m.Look, m.Side);
            if (m.Host)
            {
                people.Insert(0, person);
                _netMembers.Insert(0, m.Id);
                continue;
            }

            people.Add(person);
            _netMembers.Add(m.Id);
            _netPeople.Add(link!);
        }

        int largest = Math.Max(people.Count(p => p.Side == 0), people.Count(p => p.Side == 1));
        int size = RoundCasting.FitSize(_mode, _size, people.Count, _data.Config.Rules.MaxPlayers, largest);
        if (size != _size)
        {
            _size = size;
            _round = new RoundInfo(_level, _tier, _mode, _size, _objective);
        }

        float? limit = float.TryParse(Args.Value("--time-limit"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
            out float given) ? given : null;
        _cast = RoundCasting.Cast(_level, _squad.Cover, _sim.Collision, _data.Config, _data.Bots, _mode, _size, _objective, _tier, people, seed,
            session.RoundsPlayed + 1, _view.Hud.Callsigns, _entry.Id, _level.Place?.Id, limit, last: SpawnHistory.For(_level.Id, _level.Place?.Id));
        SpawnHistory.Remember(_level.Id, _level.Place?.Id, _cast.Memory);
        _netSetup = _cast.Setup;
    }

    /// <summary>
    /// Playing with others: everyone in the round but you, in the roster's order. Hosting, the bots think here and the people
    /// who joined move by the commands their copies send; joined, everyone is drawn where the server's snapshots put them.
    /// </summary>
    private void SpawnOthers()
    {
        var parent = new Node3D { Name = "Bots" };
        AddChild(parent);
        RoundSetupMessage setup = _netSetup!;
        NetServer? server = _net!.Server;
        for (int i = 0; i < setup.Roster.Count; i++)
        {
            RosterEntry entry = setup.Roster[i];
            PlayerState state = _sim.FindPlayer(entry.PlayerId)!;
            if (state == _player.State)
            {
                continue;
            }

            ICommandSource pilot;
            if (server is null)
            {
                pilot = new IdlePilot();
            }
            else if (entry.Person)
            {
                pilot = new NetCommandSource(server);
            }
            else
            {
                OpponentSpawn spawn = _cast!.BotStarts[i]!;
                BotBrain brain = _squad.Add(state, _data.Bots.ArchetypeFor(spawn.Roles)!, _data.Bots.Difficulty[_tier.Bots], spawn);
                brain.RestlessAfter = _mode.RestlessAfter;
                _bots.Add(brain);
                pilot = new BotPilot(brain);
            }

            string kind = entry.Person ? "Person" : state.Team == _player.State.Team ? "Teammate" : "Opponent";
            var pawn = new OpponentPawn { Name = $"{kind}_{entry.PlayerId}", Puppet = server is null };
            parent.AddChild(pawn);
            pawn.Initialize(_sim, state, TeamColor(state.Team), pilot, _view.Characters, entry.Look, _view.MarkerModel);
            _pawns.Add(pawn);
        }

        _calloutsSeen = Enumerable.Repeat(-1, _bots.Count).ToArray();
        _calloutsSent = Enumerable.Repeat(-1, _bots.Count).ToArray();
    }

    /// <summary>
    /// Playing with others, the network's turn round each step. Hosting: what has arrived is taken in before anyone moves,
    /// and after the step the bots' callouts and everyone's snapshot go out (and the result, once the round is over).
    /// Joined: this copy's player is predicted and put right, everyone else is posed from the snapshots, and the server's
    /// events are delivered as they come due.
    /// </summary>
    private void WireNetwork(PlayerState state)
    {
        NetSession session = _net!;
        session.InRound = true;
        _pause.PausesGame = false;
        if (session.Server is { } server)
        {
            server.LagCompensation = !Args.Has("--no-lag-compensation");
            server.Left += OnPersonLeft;
            _listening = server;
            _driver.BeforeTick = server.Poll;
            _driver.AfterStep = () =>
            {
                SendCallouts(server);
                server.AfterStep(_sim);
                if (_match.Phase == MatchPhase.Ended && !_netOverSent)
                {
                    _netOverSent = true;
                    server.EndRound(_sim);
                }
            };
            return;
        }

        NetClient client = session.Client!;
        RoundSetupMessage setup = _netSetup!;
        var round = new ClientSession(client, _sim, setup.Round, state.Id, _player);
        _session = round;

        // CI's cheating copies (--net-cheat): "fire" flips the trigger on every tick, and "fast" runs this copy's ticks twice
        // as often, sending twice the commands. The server must hold the first to the fire rate and drop and log the second.
        string cheat = Args.Value("--net-cheat") ?? "";
        _player.Predict = cheat == "fire" ? c => round.Predict(FlipTrigger(c, _sim.Tick, state)) : c => round.Predict(c);
        if (cheat == "fast")
        {
            int doubled = 2 * (int)MathF.Round(_sim.Config.TickRate);
            Callable.From(() => Engine.PhysicsTicksPerSecond = doubled).CallDeferred();
        }

        _player.CorrectionOffset = () => round.CorrectionOffset;
        _driver.BeforeTick = () => round.BeginTick(_sim.Dt);
        _driver.AfterStep = () =>
        {
            round.EndTick(_sim.Dt);
            if (client.TakeRoundOver() is { } over && over.Round == setup.Round)
            {
                // The server's numbers: the summary shows them (again, if it's up already).
                over.ApplyTo(_match);
                _netOver = true;
                if (_reportWhenOver)
                {
                    Callable.From(ReportBotMatch).CallDeferred();
                }
                else if (_summaryShown && _match.Phase == MatchPhase.Ended)
                {
                    Callable.From(RefreshSummary).CallDeferred();
                }
            }

            if (client.TakeRoundSetup() is { } next)
            {
                session.Round = next;
                session.RoundWaiting = true;
                Callable.From(NextNetRound).CallDeferred();
            }

            if (client.State is ClientState.Gone or ClientState.Refused && !_netGone)
            {
                _netGone = true;
                Callable.From(HostGone).CallDeferred();
            }
        };
    }

    /// <summary>
    /// The trigger flipped on every tick, and the loader refilled whenever it runs dry (a pull or a sprint would cancel the
    /// refill, so neither while it lasts), so it fires all round.
    /// </summary>
    private static InputCommand FlipTrigger(InputCommand c, int tick, PlayerState you)
    {
        if (you.Marker.Refill.Active || you.Marker.Paint.Loader == 0)
        {
            c.Buttons = (c.Buttons & ~(InputButtons.Fire | InputButtons.Sprint)) | (you.Marker.Refill.Active ? InputButtons.None : InputButtons.Refill);
            return c;
        }

        c.Buttons = tick % 2 == 0 ? c.Buttons | InputButtons.Fire : c.Buttons & ~InputButtons.Fire;
        return c;
    }

    /// <summary>Hosting: someone left mid-round; they count as out and the round goes on without them.</summary>
    private void OnPersonLeft(ClientLink link)
    {
        if (link.PlayerId >= 0 && _match.Phase != MatchPhase.Ended)
        {
            _sim.Withdraw(link.PlayerId);
            GD.Print($"NET {link.Name} left mid-round: player {link.PlayerId} is out");
        }

        _netPeople.Remove(link);
    }

    /// <summary>
    /// The scoreboard (held on Tab) and teammates' callout marks in every round, names over your teammates (not in
    /// free-for-all), and, playing with others, the chat (T to everyone, Y to your side).
    /// </summary>
    private void BuildOthersHud(PlayerState state)
    {
        var layer = new CanvasLayer { Name = "OthersHud", Layer = 6 };
        AddChild(layer);
        var root = new Control { Name = "Root", MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiKit.Theme };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(root);
        _marks = new CalloutMarks { Name = "CalloutMarks", Show_s = _view.Hud.CalloutMark_s };
        root.AddChild(_marks);

        _scoreboard = new Scoreboard { Name = "Scoreboard" };
        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        centre.AddChild(_scoreboard);
        root.AddChild(centre);
        _scoreboard.Initialize(_sim, _mode.Kind, state.Id, t => TeamColor(t), PingOf,
            p => _netSetup?.Roster.FirstOrDefault(r => r.PlayerId == p.Id)?.Person ?? p == state, $"{_round.Where} · {_round.Line}");

        if (_mode.Kind != MatchModeKind.FreeForAll)
        {
            foreach (OpponentPawn pawn in _pawns.Where(o => o.State.Team == state.Team))
            {
                pawn.ShowName(pawn.State.Name, TeamColor(pawn.State.Team), _view.Hud.NameAbove_m, _view.Hud.NameRange_m);
            }
        }

        if (_net is null)
        {
            return;
        }

        _chat = new ChatBox { Name = "Chat", Fades = true, ShowFor_s = _view.Hud.ChatShow_s };
        _chat.Build(620f, _view.Hud.ChatLines, teams: _mode.Kind == MatchModeKind.Teams);
        _chat.SideColor = side => side < 0 ? UiKit.Accent : TeamColor(side);
        _chat.Send = (text, team) => _net?.Say(text, team);
        _chat.Closed = () => _player.Muted = false;
        root.AddChild(_chat);
        _chat.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _chat.Position = new Vector2(24, -330);
        _chat.GrowVertical = Control.GrowDirection.Begin;
    }

    /// <summary>
    /// <c>--snap-every=S</c> (with <c>--snap-dir=DIR</c>, default user://snaps): the screen saved every S seconds of real
    /// time, for pictures of a round that runs at its real speed (filming at a fixed frame rate would slow a joining copy's
    /// clock against the host's).
    /// </summary>
    private void Snap()
    {
        if (!float.TryParse(Args.Value("--snap-every"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                out float every) || every <= 0f)
        {
            return;
        }

        double now = Time.GetTicksMsec() / 1000.0;
        if (_snapAt < 0)
        {
            _snapAt = now + every;
            return;
        }

        if (now < _snapAt)
        {
            return;
        }

        _snapAt = now + every;
        string dir = Args.Value("--snap-dir") ?? "user://snaps";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        string path = $"{dir}/snap{_snaps++:000}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"SNAP {path} at {_match.Elapsed:0.0} s ({_match.Phase})");
    }

    /// <summary>A person's ping as the lobby has it (ms; none for the host, or a bot).</summary>
    private int? PingOf(PlayerState p) =>
        _net?.LobbyView?.Members.FirstOrDefault(m => m.Name == p.Name) is { Host: false } m ? m.Ping_ms : null;

    /// <summary>
    /// Someone's callout key: "Contact!" in their character's voice from where they stand, and for their side a mark
    /// over the spot (and a subtitle when they're close).
    /// </summary>
    private void OnCalledOut(in SimEvent e)
    {
        if (_sim.FindPlayer(e.PlayerId) is not { } caller)
        {
            return;
        }

        string[] lines = _view.Hud.Callouts.For(CalloutKind.Spotted);
        string line = lines.Length > 0 ? lines[(caller.Id * 31 + e.Tick) % lines.Length] : "Contact!";
        int look = _netSetup?.Roster.FirstOrDefault(r => r.PlayerId == caller.Id)?.Look ?? 0;
        _audio.Callout(caller, look, line);
        PlayerState you = _player.State;
        bool ours = caller == you || (_mode.Kind != MatchModeKind.FreeForAll && caller.Team == you.Team);
        if (!ours)
        {
            return;
        }

        string who = caller == you ? "You" : caller.Name;
        string about = e.TargetId >= 0 && _sim.FindPlayer(e.TargetId) is { } target ? $"{who}: {target.Name}" : $"{who}: contact";
        _marks.Add(e.Position.ToGodot(), TeamColor(caller.Team), about);
        if (System.Numerics.Vector3.Distance(caller.Position, you.Position) <= _view.Hud.SubtitleRange_m)
        {
            _hud.Subtitle(caller.Name, caller.Team, line);
        }
    }

    /// <summary>Joined: a warning on the HUD when the connection's poor, or nothing has come from the host for a while.</summary>
    private void ConnectionWarning(double delta)
    {
        _sinceSnapshotCheck += delta;
        if (_net?.Client is not { } client || _session is null || _sinceSnapshotCheck < 0.25)
        {
            return;
        }

        _sinceSnapshotCheck = 0;
        NetSettings settings = _net.Settings;
        double quiet = _net.Now - client.NewestAt;
        string? warning = _match.Phase == MatchPhase.Ended ? null
            : quiet > settings.PoorSilence ? "Connection interrupted: waiting for the host…"
            : client.RoundTrip > settings.PoorRoundTrip ? $"Connection poor: {client.RoundTrip * 1000f:0} ms to the host"
            : null;
        _hud.Connection(warning);
    }

    /// <summary>Hosting: someone who's out is walked off the field the way the bots go, so nobody's left standing in the way.</summary>
    private ICommandSource WalkOffPilot(PlayerState state)
    {
        var spot = new OpponentSpawn { Id = $"out_{state.Id}", Position = state.Position, Yaw = state.Yaw, Roles = new[] { "sentry" } };
        BotBrain brain = _squad.Add(state, _data.Bots.ArchetypeFor(spot.Roles)!, _data.Bots.Difficulty[_tier.Bots], spot);
        return new BotPilot(brain);
    }

    /// <summary>Hosting: the bots' new callouts and contacts go to everyone with the step's events.</summary>
    private void SendCallouts(NetServer server)
    {
        _contactsSent = Pb.Net.Server.ContactRelay.Send(_squad, _bots, _contactsSent, server, _sim.Tick - 1);
        for (int i = 0; i < _bots.Count; i++)
        {
            BotBrain bot = _bots[i];
            if (bot.CalloutTick <= _calloutsSent[i])
            {
                continue;
            }

            _calloutsSent[i] = bot.CalloutTick;
            string[] lines = _view.Hud.Callouts.For(bot.Callout);
            if (lines.Length == 0)
            {
                continue;
            }

            // The line the host plays itself (see _Process), so everyone hears the same words.
            server.AddEvent(new SimEvent
            {
                Type = NetEventTypes.Callout, Tick = _sim.Tick - 1, PlayerId = bot.Self.Id, Extra = (int)bot.Callout,
                TargetId = (bot.Self.Id * 31 + bot.CalloutTick) % lines.Length, Position = bot.Self.EyePosition, ColliderId = -1,
            });
        }
    }

    /// <summary>Joined: a bot's callout the host sent, in its voice from where it was called, and a subtitle when close.</summary>
    private void HearCallout(in SimEvent e)
    {
        if (_netSetup is null || _net?.Client is null || _sim.FindPlayer(e.PlayerId) is not { } speaker)
        {
            return;
        }

        string[] lines = _view.Hud.Callouts.For((CalloutKind)e.Extra);
        if (lines.Length == 0)
        {
            return;
        }

        string line = lines[Math.Abs(e.TargetId) % lines.Length];
        int look = _netSetup.Roster.FirstOrDefault(r => r.PlayerId == speaker.Id)?.Look ?? 0;
        _audio.Callout(speaker, look, line);
        if (System.Numerics.Vector3.Distance(speaker.Position, _player.State.Position) <= _view.Hud.SubtitleRange_m)
        {
            _hud.Subtitle(speaker.Name, speaker.Team, line);
        }
    }

    /// <summary>
    /// The round is built here. Hosting: everyone who joined is sent its setup (and who they play), and the briefing waits
    /// for them to have it built too. Joined: the host is told this copy is ready.
    /// </summary>
    private void StartNetRound(NetSession session)
    {
        RoundSetupMessage setup = _netSetup!;
        if (session.Server is { } server)
        {
            CastRound cast = _cast!;
            server.BeginRound(_sim, setup, link =>
            {
                int at = _netPeople.IndexOf(link);
                return at >= 0 ? cast.PersonIds[at + 1] : -1;
            });
            session.Round = setup;
            session.Lobby?.RoundStarted();
            _netWaited = 0;
            GD.Print($"NET round {setup.Round} sent to {_netPeople.Count} joined: {string.Join(", ", _netPeople.Select(l => $"{l.Name} as player {l.PlayerId}"))}");
            return;
        }

        session.Client!.SendLoaded(setup.Round);
    }

    /// <summary>
    /// Hosting: the briefing waits for everyone to have the round built (or for as long as it may), counts down, and the
    /// round goes live for everyone at once.
    /// </summary>
    private void NetCountdown(double delta)
    {
        if (_net?.Server is not { } server || _match.Phase != MatchPhase.Briefing || _netSetup is null)
        {
            return;
        }

        NetSettings settings = _net.Settings;
        if (_netCountdown < 0)
        {
            _netWaited += delta;
            if (!server.AllLoaded() && _netWaited < settings.LoadTimeout)
            {
                string waiting = string.Join(", ", _netPeople.Where(l => l.PlayerId >= 0 && l.LoadedRound != server.Round).Select(l => l.Name));
                SetStatus(waiting.Length == 0 ? "Waiting for everyone…" : $"Waiting for {waiting}…");
                return;
            }

            _netCountdown = settings.Briefing;
        }

        _netCountdown -= delta;
        if (_netCountdown > 0)
        {
            SetStatus($"Starting in {Math.Ceiling(_netCountdown):0}…");
            return;
        }

        GD.Print($"NET round {_netSetup.Round} live after {_netWaited:0.0} s");
        BeginRound();
    }

    /// <summary>
    /// Playing with others, once the round's over: hosting, back to the lobby by itself after a while; joined, the summary
    /// again when the session's score comes, and back to the lobby when the host goes.
    /// </summary>
    private void NetSummary(double delta)
    {
        if (_net is not { } session || _match.Phase != MatchPhase.Ended || _netLeft)
        {
            return;
        }

        if (session.Hosting)
        {
            if (!_summaryShown || _botMatch || session.Settings.Summary <= 0f)
            {
                return;
            }

            if (_backIn < 0)
            {
                _backIn = session.Settings.Summary;
            }

            _backIn -= delta;
            SetStatus(SummaryNote(over: true));
            if (_backIn <= 0)
            {
                BackToLobby();
            }

            return;
        }

        if (session.LobbyView is { Phase: LobbyPhase.Lobby or LobbyPhase.Vote or LobbyPhase.Countdown } && (!_botMatch || _reported))
        {
            FollowToLobby();
        }
        else if (_summaryShown && session.LobbyVersion != _summaryLobby)
        {
            RefreshSummary();
        }
    }

    /// <summary>The waiting briefing's (or summary's) status line.</summary>
    private void SetStatus(string text)
    {
        if (_overlay?.FindChild("Status", true, false) is Label status && status.Text != text)
        {
            status.Text = text;
        }
    }

    /// <summary>Hosting: back to the lobby (or the vote first) for everyone, then the next round from there.</summary>
    private void BackToLobby()
    {
        if (_net?.Lobby is not { } lobby || _netLeft)
        {
            return;
        }

        _netLeft = true;
        _net.RoundsPlayed = _netSetup!.Round;
        LobbyChoices choices = lobby.State.Choices;
        IReadOnlyList<VoteOption>? options = choices.Vote ? RoundChoices.VoteOptions(_data, choices, _net.Settings.VoteOptions, _sim.MatchSeed ^ 0x5EED) : null;
        lobby.BackToLobby(options);
        StopNetwork();
        GetTree().ChangeSceneToFile(GameSession.LobbyScene);
    }

    /// <summary>Joined: the host has gone back to the lobby; so do you.</summary>
    private void FollowToLobby()
    {
        if (_netLeft || _net is null)
        {
            return;
        }

        _netLeft = true;
        StopNetwork();
        GetTree().ChangeSceneToFile(GameSession.LobbyScene);
    }

    /// <summary>Hosting: the round's result in the session's score (each person's lobby member, their player and side).</summary>
    private void ScoreRound()
    {
        if (_net?.Lobby is not { } lobby || _cast is null || _netScored)
        {
            return;
        }

        _netScored = true;
        var people = new List<PersonInRound>();
        for (int i = 0; i < _cast.PersonIds.Count && i < _netMembers.Count; i++)
        {
            int playerId = _cast.PersonIds[i];
            people.Add(new PersonInRound(_netMembers[i], playerId, _sim.FindPlayer(playerId)?.Team ?? -1));
        }

        var stats = _match.Stats.Select(p => new StatsEntry(p.PlayerId, p.Shots, p.Hits, p.Eliminations, p.Pickups, p.TimeIn, p.OutTick)).ToList();
        lobby.RoundOver(_match.Result, people, stats);
    }

    /// <summary>The summary's line playing with others: the session's score, and what happens next.</summary>
    private string SummaryNote(bool over)
    {
        NetSession session = _net!;
        string next = !over ? "The round goes on without you."
            : session.Hosting ? session.Settings.Summary > 0f ? $"Back to the lobby in {Math.Ceiling(_backIn):0} s, or now." : "Back to the lobby when everyone's ready."
            : "Back to the lobby when the host is.";
        return SessionLine() is { } score ? $"{score}\n{next}" : next;
    }

    /// <summary>"After 3 rounds: your side 2, theirs 1." (or each person's wins in free-for-all).</summary>
    private string? SessionLine()
    {
        if (_net?.LobbyView is not { RoundsPlayed: > 0 } lobby)
        {
            return null;
        }

        string after = $"After {lobby.RoundsPlayed} round{(lobby.RoundsPlayed == 1 ? "" : "s")}";
        int me = _net.MemberId;
        return _mode.Kind switch
        {
            MatchModeKind.FreeForAll => $"{after}, rounds won: " +
                string.Join(" · ", lobby.Members.OrderByDescending(m => m.RoundsWon).Select(m => $"{(m.Id == me ? "you" : m.Name)} {m.RoundsWon}")) + ".",
            MatchModeKind.Teams => $"{after}: your side {lobby.SideWins[_player.State.Team % 2]}, theirs {lobby.SideWins[1 - _player.State.Team % 2]}.",
            _ => $"{after}: you've won {lobby.SideWins[0]}, the squad {lobby.SideWins[1]}.",
        };
    }

    /// <summary>Joined: the host has started the next round: build it (or, not playing in it, wait for the one after).</summary>
    private void NextNetRound()
    {
        if (_net is null)
        {
            return;
        }

        StopNetwork();
        if (_net.Round is { YourPlayerId: >= 0 })
        {
            GetTree().ReloadCurrentScene();
        }
        else
        {
            GetTree().ChangeSceneToFile(GameSession.LobbyScene);
        }
    }

    /// <summary>The summary again, with the server's numbers.</summary>
    private void RefreshSummary()
    {
        if (!_summaryShown || _overlay is null)
        {
            return;
        }

        CloseOverlay();
        ShowSummary();
    }

    /// <summary>Joined: the host has gone (it ended the game, or the connection was lost): say so, then back to the menu.</summary>
    private void HostGone()
    {
        string why = _net?.Client?.State == ClientState.Refused ? _net.Client.Refusal?.Text ?? "The host turned you away."
            : _net?.Client?.GoneReason is { Length: > 0 } reason ? reason : "The connection to the host was lost.";
        GD.Print($"NET host gone: {why}");
        StopNetwork();
        _net?.Leave(why);
        _net = null;
        if (DisplayServer.GetName() == "headless")
        {
            // CI: the game ending after a round played is how a networked run finishes.
            GetTree().QuitCleanly(_summaryShown || _match.Phase == MatchPhase.Ended ? 0 : 1);
            return;
        }

        _spectator?.StopFollowing();
        _hud.Visible = false;
        VBoxContainer column = UiKit.Column(16);
        column.AddChild(UiKit.Title("The game is over", 40));
        Label line = UiKit.Body(why, 20, UiKit.Text, wrap: true);
        line.CustomMinimumSize = new Vector2(480, 0);
        column.AddChild(line);
        Button back = UiKit.Button("Main menu", () => GetTree().ChangeSceneToFile(GameSession.MainScene), 240);
        column.AddChild(back);
        ShowOverlay(UiKit.Overlay(UiKit.Panel(column, 560f)));
        back.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>Playing with others: leaves the game (hosting, it ends for everyone) and goes back to the menu.</summary>
    private void LeaveGame()
    {
        StopNetwork();
        _net?.Leave(_net.Hosting ? "The host ended the game." : "left");
        _net = null;
        GetTree().ChangeSceneToFile(GameSession.MainScene);
    }

    /// <summary>The network's turn comes off the tick (leaving, or going on to the next round).</summary>
    private void StopNetwork()
    {
        _driver.BeforeTick = null;
        _driver.AfterStep = null;
        _player.Predict = null;
        _player.CorrectionOffset = null;
        if (_listening is not null)
        {
            _listening.Left -= OnPersonLeft;
            _listening = null;
        }

        if (_net is not null)
        {
            _net.InRound = false;
        }
    }

    public override void _ExitTree()
    {
        StopNetwork();
        System.Runtime.GCSettings.LatencyMode = _latencyBefore;
    }

    /// <summary>
    /// Compiles the sim's, the network's and the game's methods up front (once a run) and, for as long as the level is
    /// up, has the garbage collector avoid full blocking collections (it collects the old generation in the background
    /// instead): the hitches of a first call or a full collection mid-round.
    /// </summary>
    private (int Methods, double Milliseconds) WarmUp()
    {
        _latencyBefore = System.Runtime.GCSettings.LatencyMode;
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        for (int g = 0; g < 3; g++)
        {
            _collectionsAtLoad[g] = GC.CollectionCount(g);
        }
        (int sim, double simMs) = Pb.Sim.Core.JitWarmup.PrepareSim();
        (int net, double netMs) = Pb.Sim.Core.JitWarmup.Prepare(typeof(Pb.Net.Server.NetServer).Assembly);
        (int game, double gameMs) = Pb.Sim.Core.JitWarmup.Prepare(typeof(LevelMain).Assembly);
        return (sim + net + game, simMs + netMs + gameMs);
    }

    private Color TeamColor(int team) => Color.FromHtml(_view.TeamColors[team % _view.TeamColors.Length]);

    /// <summary>The callsigns in a shuffled order for this round (the same order for the same match seed).</summary>
    private static string[] DealCallsigns(string[] callsigns, ulong seed)
    {
        string[] dealt = (string[])callsigns.Clone();
        var rng = new Pb.Sim.Core.Pcg32(seed ^ 0xCA115165);
        for (int i = dealt.Length - 1; i > 0; i--)
        {
            int j = (int)(rng.NextUInt() % (uint)(i + 1));
            (dealt[i], dealt[j]) = (dealt[j], dealt[i]);
        }

        return dealt;
    }

    private SplatAnchor? SplatParent(int receiverId, int part, Vector3 point)
    {
        if (!PlayerHitboxes.IsPlayer(receiverId))
        {
            return null;
        }

        int id = PlayerHitboxes.PlayerIdOf(receiverId);
        CharacterVisual? visual = id == _player.State.Id ? _player.Body : _pawns.FirstOrDefault(o => o.State.Id == id)?.Visual;
        return visual?.PartNode((Pb.Sim.Collision.HitboxPart)part, point);
    }

    private void OnEliminated(in SimEvent e)
    {
        PlayerState? victim = _sim.FindPlayer(e.TargetId);
        PlayerState? shooter = _sim.FindPlayer(e.PlayerId);
        if (victim is null)
        {
            return;
        }

        if (victim == _player.State)
        {
            StartSpectating(victim, shooter);
            return;
        }

        if (e.Extra < 0)
        {
            // Playing with others: they left the game (the kill feed says so).
            return;
        }

        if (_net is { Hosting: true } && _pawns.FirstOrDefault(o => o.State == victim)?.Pilot is NetCommandSource person)
        {
            person.WalkOff = WalkOffPilot(victim);
        }

        // (Your own put-outs are the hit marker's line under the crosshair: who, where, how far, how many left.)
        _pawns.FirstOrDefault(o => o.State == victim)?.CallHit();
    }

    private void StartSpectating(PlayerState victim, PlayerState? shooter)
    {
        if (_spectator is not null)
        {
            return;
        }

        string detail = "";
        if (shooter is not null)
        {
            float distance = System.Numerics.Vector3.Distance(shooter.EyePosition, victim.EyePosition);
            _hitBy = $"{shooter.Name} got you: {SpectatorView.PartName(victim.EliminatedPart)}, {distance:0} m.";
            detail = $"{shooter.Name} got you · {SpectatorView.PartName(victim.EliminatedPart)} · {distance:0} m";
        }

        // You stand for the hit, hand and marker going up, then walk off the field like everyone else who's out, once the
        // camera has eased out to watch (joined, the host walks you off; in a bot match the bot at your controls does it
        // itself).
        HitMomentDef moment = _view.Feedback.HitMoment;
        if (_player.AutoPilot is not (BotPilot or CallingPilot))
        {
            _player.AutoPilot = new IdlePilot();
            if (_net?.Client is null)
            {
                GetTree().CreateTimer(moment.Hold_s + moment.Ease_s).Timeout += () =>
                {
                    if (IsInstanceValid(this) && _player.AutoPilot is IdlePilot && !_player.State.Alive)
                    {
                        _player.AutoPilot = WalkOffPilot(victim);
                    }
                };
            }
        }

        // The moment you're hit: the screen and HIT! (Phase 5), from the side the ball came, in its paint's colour.
        Color paint = TeamColor(shooter?.Team ?? (victim.Team == 0 ? 1 : 0));
        float angle = _lastHitTravel is { } travel ? IncomingFire.ScreenAngle(IncomingFire.BearingOf(-travel), _player.Yaw) : float.NaN;
        _hitMoment.Start(paint, angle, detail, _settings.HitEffect);
        _hud.OutOfTheRound();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _spectator = new SpectatorView { Name = "Spectator" };
        AddChild(_spectator);
        // The camera holds in your eyes for the hit, then eases out to watch you walk off; once it's out of your head your
        // body is drawn and the marker in your hands goes.
        void LeftEyes()
        {
            _player.ViewModel.Hidden = true;
            _maskSpray.Clear();
            _hud.Visible = false;
            if (_player.Body is { } body)
            {
                body.ShadowOnly = false;
            }
        }

        // Once you've seen who got you: watch the players still in while the round goes on without you, or
        // the summary (scripted runs just end; the smoke test and the bot match end themselves).
        _spectator.Start(victim, shooter, _view.Spectator, _view.Camera.FarClip_m, _sim.Collision, () =>
        {
            if (_scripted)
            {
                if (_smoke is null && !_botMatch)
                {
                    GetTree().QuitCleanly();
                }
            }
            else if (_match.Phase != MatchPhase.Ended && Watchable().Count > 0)
            {
                _hitMoment.Settle();
                _spectator.Follow(Watchable, TeamColor, ShowSummary, _sim.Collision);
            }
            else
            {
                ShowSummary();
            }
        }, _player.Camera.GlobalTransform, _player.Camera.Fov, moment.Hold_s, moment.Ease_s, LeftEyes, showBanner: false);
    }

    /// <summary>Who you may watch once you're out: your teammates still in, or in free-for-all anyone still in.</summary>
    private IReadOnlyList<PlayerState> Watchable() =>
        _sim.Players.Where(p => p.Alive && p != _player.State && (_mode.Kind == MatchModeKind.FreeForAll || p.Team == _player.State.Team)).ToList();

    /// <summary>The briefing card's Start: the round goes live and the mouse is yours.</summary>
    private void BeginRound()
    {
        CloseOverlay();
        _sim.GoLive();
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void OnRoundEnded()
    {
        if (_botMatch)
        {
            FinishBotMatch();
            return;
        }

        if (_scripted)
        {
            return;
        }

        if (_counts)
        {
            RecordRound();
        }

        ScoreRound();

        // Skipped ahead to the summary: it shows the final result now.
        if (_summaryEarly)
        {
            CloseOverlay();
            ShowSummary();
            return;
        }

        // Out: the spectator view shows who got you first. Otherwise (or watching the others) a moment to take it in.
        if (_spectator is null || _spectator.Following)
        {
            GetTree().CreateTimer(1.5).Timeout += ShowSummary;
        }
    }

    /// <summary>Adds the round to your records (for where in the area it was played) and saves them.</summary>
    private void RecordRound()
    {
        RecordBook records = Profile.Load(_data.Areas);
        PlayerStats you = _match.StatsFor(_player.State.Id)!;
        records.Add(new RoundResult(_entry.Id, _mode.Id, _tier.Id, _match.Outcome, _match.Elapsed, you.Shots, you.Hits,
            you.Eliminations, RecordBook.IdOf(_match.Setup.Objective), _level.Place?.Id ?? RecordBook.WholeArea));
        Profile.Save(records);
    }

    /// <summary>The bot match is over: report how it went, pass if nothing went wrong on the way.</summary>
    private void FinishBotMatch()
    {
        if (_net?.Client is not null && !_netOver)
        {
            // Joined: the round's numbers are the server's, so it's reported once they've come (or a few seconds on without).
            _reportWhenOver = true;
            GetTree().CreateTimer(5.0).Timeout += ReportBotMatch;
            return;
        }

        ReportBotMatch();
    }

    private void ReportBotMatch()
    {
        if (_reported)
        {
            return;
        }

        _reported = true;
        if (Args.Has("--record"))
        {
            // CI: the round goes in the profile as if you'd played it, so saving it is exercised too.
            RecordRound();
        }

        PlayerStats you = _match.StatsFor(_player.State.Id)!;
        int errors = _driver.ErrorCount;
        bool ok = errors == 0 && _match.Outcome != RoundOutcome.None && (_net?.Client is null || _netOver);
        // CI with the art on: no character's head or hands may pop further than this in a frame (cm, at 30 fps and above;
        // below that, plus what the body's own smooth motion changes a frame by: CharactersDef.FrameLimit).
        if (float.TryParse(Args.Value("--max-pop"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float maxPop))
        {
            float worst = _pawns.Where(p => p.Visual.Model is not null).Select(p => p.Visual.Model!.Poser.WorstPop).DefaultIfEmpty(float.MaxValue).Max();
            float limit = _view.Characters.FrameLimit(maxPop / 100f, (float)GetProcessDeltaTime()) * 100f;
            ok &= worst <= limit;
            GD.Print($"BOT MATCH poses: worst pop {worst:0.0} cm against {limit:0.0} cm at {1.0 / GetProcessDeltaTime():0} fps");
        }

        // The round's cues (the start cue, the end whistle) were asked for and none was dropped (one asked for before the
        // sounds were ready waits for them).
        ok &= _audio.RoundCues > 0 && _audio.RoundCuesDropped == 0;
        // CI's teams round: bot teammates' contacts marked on your HUD, at least this many.
        if (int.TryParse(Args.Value("--min-contact-marks"), out int minMarks))
        {
            ok &= ContactMarks >= minMarks;
        }

        // Hit feedback (Phase 5): out by a ball, the hit moment played; and with --min-feedback=N, at least N incoming-fire
        // arcs and hit markers between them.
        bool outByBall = !_player.State.Alive && _player.State.EliminatedBy >= 0 && _lastHitTravel is not null;
        ok &= !outByBall || _hitMoment.Played > 0;
        int feedback = _hud.IncomingArcsAdded.Sum() + _hud.HitMarkersShown.Sum();
        if (int.TryParse(Args.Value("--min-feedback"), out int minFeedback))
        {
            ok &= feedback >= minFeedback;
        }

        GD.Print($"BOT MATCH feedback: {FeedbackLine()}");
        int botOnBot = _sim.Players.Count(p => !p.Alive && p != _player.State && p.EliminatedBy > 0);
        string objective = ObjectiveLine() is { } line ? $" ({line})" : "";
        GD.Print($"SMOKE {(ok ? "PASS" : "FAIL")}: bot match ({_round.Line}) {_match.Outcome}{objective} after {_match.Elapsed:0} s: you put out " +
                 $"{you.Eliminations} of {_sim.Players.Count(p => p.Team != _player.State.Team)}, {you.Shots} shots, {you.Hits} hits, " +
                 $"{you.Pickups} pickups; bots put out by bots: {botOnBot}; callouts: {_squad.Shouts} shouted aloud, {_squad.ContactsShared} contacts " +
                 $"shared and heard {_squad.ContactsHeard} times by teammates, {ContactMarks} marked on your HUD, {_callouts} callout-key presses; {_audio.RoundCueLine}; collections {CollectionsLine()}; {PopsLine()}" +
                 $"simErrors={errors} avgStepMs={_driver.AverageStepMs:0.000}");
        if (_net is { } session)
        {
            // Every copy prints the round as it ended there: the host's and each joiner's lines must match (CI compares them).
            GD.Print($"NET RESULT round {_netSetup!.Round}: {_match.Result.Reason} won by {_match.Result.Winner} at tick {_match.EndTick}; " +
                     string.Join(" ", _match.Stats.Select(p => $"{p.PlayerId}:{p.Shots}/{p.Hits}/{p.Eliminations}/{p.Pickups}/{p.OutTick}")));
            NetReport(session);
        }

        if (Args.Has("--show-summary"))
        {
            ShowSummary();
            GetTree().CreateTimer(3.0).Timeout += () => QuitBotMatch(ok);
            return;
        }

        // Playing with others, --rounds=N plays N rounds before the game ends (CI): back to the lobby for the next.
        _roundsReported++;
        if (!ok)
        {
            _failedRounds++;
        }

        if (_net is { } others && _roundsReported < (Args.Ticks("--rounds", 1) ?? 1))
        {
            if (others.Hosting)
            {
                GetTree().CreateTimer(2.0).Timeout += BackToLobby;
            }

            return;
        }

        // Hosting, the others are given a moment to have the result before the game ends.
        if (_net is { Hosting: true })
        {
            GetTree().CreateTimer(2.0).Timeout += () => QuitBotMatch(ok && _failedRounds == 0);
            return;
        }

        QuitBotMatch(ok && _failedRounds == 0);
    }

    /// <summary>What the hit feedback did this round, for the log.</summary>
    private string FeedbackLine()
    {
        int[] marks = _hud.HitMarkersShown, arcs = _hud.IncomingArcsAdded;
        return $"hit markers {marks[(int)HitMarkKind.Out]} out, {marks[(int)HitMarkKind.Bounce]} bounced, {marks[(int)HitMarkKind.Teammate]} teammates; " +
               $"incoming-fire arcs {arcs[(int)IncomingKind.Flyby]} whizzes, {arcs[(int)IncomingKind.NearBreak]} near breaks, " +
               $"{arcs[(int)IncomingKind.BounceOffYou]} bounces off you, {arcs[(int)IncomingKind.Hit]} hits; {_flybys.Count} balls whizzed past " +
               $"({_audio.Whizzes} heard); {_audio.Confirms} confirms; {_splats.Streaks} streaked splats; {_markerPaint.Count} on your marker; " +
               $"hit moment played {_hitMoment.Played} times";
    }

    private void QuitBotMatch(bool ok)
    {
        StopNetwork();
        _net?.Leave(_net.Hosting ? "The host ended the game." : "left");
        _net = null;
        GetTree().QuitCleanly(ok ? 0 : 1);
    }

    /// <summary>How the network went this round, for the log: corrections, traffic, the round trip, what the server dropped.</summary>
    private void NetReport(NetSession session)
    {
        if (session.Server is { } server)
        {
            foreach (ClientLink link in server.Clients.Where(c => c.Welcomed))
            {
                GD.Print($"NET {link.Name}: player {link.PlayerId}, round trip {link.RoundTrip * 1000f:0} ms, {link.BytesSent / 1024f:0} KiB sent " +
                         $"({link.BytesPerSecond / 1024f:0.0} KiB/s), commands missing {link.Commands.Missing}, late {link.Commands.Late}, " +
                         $"merged {link.Commands.Merged}, too far ahead {link.Commands.TooFarAhead} ({link.Commands.Skipped} skipped), violations {link.Violations}");
            }

            GD.Print($"NET host sent {server.BytesSent / 1024f:0} KiB in all");
            return;
        }

        if (session.Client is { } client && _session is { } round)
        {
            double replayMs = round.Corrections > 0 ? round.ReplayTime * 1000.0 / round.Corrections : 0.0;
            var causes = new List<string>();
            ReadOnlySpan<int> counts = round.CorrectionCauses;
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] > 0)
                {
                    causes.Add($"{(PredictionDifference)i} {counts[i]}");
                }
            }

            GD.Print($"NET joined: round trip {client.RoundTrip * 1000f:0} ms (jitter {client.Jitter * 1000f:0} ms), {client.BytesReceived / 1024f:0} KiB " +
                     $"received, {round.Corrections} corrections (last {round.LastCorrection * 100f:0.0} cm; {round.ReplayedTicks} ticks replayed, " +
                     $"{replayMs:0.000} ms a correction; first difference: {(causes.Count > 0 ? string.Join(", ", causes) : "none")}), " +
                     $"{client.Undecodable} snapshots undecodable");
        }
    }

    /// <summary>The summary, once the round is over or, when you're out, as soon as you ask (the others play on behind it).</summary>
    private void ShowSummary()
    {
        PlayerState you = _player.State;
        bool over = _match.Phase == MatchPhase.Ended;
        if ((!over && you.Alive) || _overlay is not null)
        {
            return;
        }

        _spectator?.StopFollowing();
        _hud.Visible = false;
        _summaryShown = true;
        _summaryEarly = !over;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        IReadOnlyList<PlayerState> players = _sim.Players;
        RoundOutcome outcome = _match.OutcomeFor(you.Team);
        PlayerState? winner = _mode.Kind == MatchModeKind.FreeForAll && over && outcome == RoundOutcome.Eliminated
            ? players.FirstOrDefault(p => p.Alive)
            : null;
        var facts = new SummaryFacts
        {
            Over = over,
            Outcome = outcome,
            YouAreOut = !you.Alive,
            HitBy = _hitBy,
            Opponents = players.Count(p => p.Team != you.Team),
            OursLeft = players.Count(p => p.Alive && p.Team == you.Team),
            OthersLeft = players.Count(p => p.Alive && p.Team != you.Team),
            Placing = _match.Placing(you.Id),
            Players = players.Count,
            Winner = winner?.Name,
            ObjectiveLine = ObjectiveLine(),
            ObjectiveRow = ObjectiveRow(),
        };
        // Playing with others: the host starts the next round once this one is over; everyone may leave.
        (string, Action)[]? actions = null;
        string? note = null;
        if (_net is { } session)
        {
            actions = session.Hosting && over
                ? new (string, Action)[] { ("Back to the lobby", BackToLobby), ("End the game", LeaveGame) }
                : new (string, Action)[] { ("Leave", LeaveGame) };
            note = SummaryNote(over);
            _summaryLobby = session.LobbyVersion;
        }

        ShowOverlay(RoundScreens.Summary(_round, _match, _match.StatsFor(you.Id)!, facts,
            retry: () => GetTree().ReloadCurrentScene(),
            levelSelect: BackToLevelSelect,
            mainMenu: () => GetTree().ChangeSceneToFile(GameSession.MainScene),
            actions, note));
    }

    /// <summary>How the objective went, in a sentence, for the summary's headline.</summary>
    private string? ObjectiveLine()
    {
        if (_match.Objective is not { } o)
        {
            return null;
        }

        if (o.Kind == ObjectiveKind.Hold)
        {
            return o.Done
                ? $"Your side held {ModeText.The(o.Room!.Name)} for {RoundScreens.Clock(o.HoldRules.HoldTime)}."
                : $"You held {ModeText.The(o.Room!.Name)} for {o.Held:0} of {o.HoldRules.HoldTime:0} s.";
        }

        if (o.Kind == ObjectiveKind.Flag)
        {
            int us = _player.State.Team;
            FlagState? theirs = o.TheirFlag(us), own = o.FlagOf(us);
            if (o.Done)
            {
                bool ours = o.CapturedBy == us;
                return $"{(ours ? "Your side" : "They")} carried {(ours ? "their" : "your")} flag home at {RoundScreens.Clock(_match.Elapsed)}.";
            }

            string Where(FlagState? f, string whose) => f is null ? "" : f.AtHome ? $"{whose} flag was home" : f.Carrier >= 0 ? $"{whose} flag was being carried" : $"{whose} flag was down";
            return $"{Where(theirs, "Their")}; {Where(own, "your").ToLowerInvariant()}.";
        }

        if (o.Kind == ObjectiveKind.Escort)
        {
            string vip = o.Vip == _player.State.Id ? "You" : _sim.FindPlayer(o.Vip)?.Name ?? "The VIP";
            return o.Done ? $"{vip} got out through {o.Level.Exits[o.ExitUsed].Name} at {RoundScreens.Clock(_match.Elapsed)}."
                : o.VipOut ? $"{vip} {(vip == "You" ? "were" : "was")} put out at {RoundScreens.Clock(_match.Elapsed)}."
                : $"{vip} didn't get out in time.";
        }

        string carrier = o.Carrier == _player.State.Id ? "You" : _sim.FindPlayer(o.Carrier)?.Name ?? "Your side";
        return o.Done ? $"{carrier} got it out through {o.Level.Exits[o.ExitUsed].Name} at {RoundScreens.Clock(_match.Elapsed)}."
            : o.Carrier >= 0 ? $"{carrier} still had the case."
            : o.CaseMoved ? "The case was lying where its last carrier fell."
            : $"Nobody found the case in {ModeText.The(o.Spot.Area)}.";
    }

    private (string, string)? ObjectiveRow()
    {
        if (_match.Objective is not { } o)
        {
            return null;
        }

        return o.Kind switch
        {
            ObjectiveKind.Hold => ("Held", $"{o.Held:0} of {o.HoldRules.HoldTime:0} s"),
            ObjectiveKind.Flag => ("Flag", o.Done ? (o.CapturedBy == _player.State.Team ? "captured by your side" : "captured by theirs") : "not captured"),
            ObjectiveKind.Escort => ("VIP", o.Done ? $"out through {o.Level.Exits[o.ExitUsed].Name}" : o.VipOut ? "put out" : "didn't get out"),
            _ => ("Case", o.Done ? $"out through {o.Level.Exits[o.ExitUsed].Name}" : o.Carrier >= 0 ? "carried, not out" : o.CaseMoved ? "dropped" : "not found"),
        };
    }

    private void BackToLevelSelect()
    {
        GameSession.ReturnTo = "levels";
        GetTree().ChangeSceneToFile(GameSession.MainScene);
    }

    private void ShowOverlay(Control overlay)
    {
        CloseOverlay();
        _overlay = overlay;
        _overlays.AddChild(overlay);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void CloseOverlay()
    {
        _overlay?.QueueFree();
        _overlay = null;
    }

    /// <summary>The top bar's clock: the full time limit during the briefing, then the time left.</summary>
    private string MatchClock() =>
        RoundScreens.Clock(_match.Phase == MatchPhase.Briefing ? _match.Setup.TimeLimit : _match.TimeLeft);

    /// <summary>Plays what the bots shout in their voices, and shows it as subtitles when you're close enough to hear it.</summary>
    public override void _Process(double delta)
    {
        if (!_ready)
        {
            return;
        }

        NetCountdown(delta);
        NetSummary(delta);
        ConnectionWarning(delta);
        Snap();
        _scoreboard.Visible = (Input.IsActionPressed("scoreboard") || Args.Has("--show-scoreboard")) && _overlay is null && _chat?.Typing != true && !_pause.Open;
        if (_net is not null && _chat is not null)
        {
            foreach (Pb.Net.Lobby.ChatLine line in _net.TakeChat())
            {
                _chat.Add(line);
            }
        }
        HudDef hud = _view.Hud;
        for (int i = 0; i < _bots.Count; i++)
        {
            BotBrain bot = _bots[i];
            if (bot.CalloutTick <= _calloutsSeen[i])
            {
                continue;
            }

            _calloutsSeen[i] = bot.CalloutTick;
            string[] lines = hud.Callouts.For(bot.Callout);
            if (lines.Length == 0)
            {
                continue;
            }

            // Heard in the caller's voice from where they stand (when it's been recorded), and read as a subtitle when close.
            string line = lines[(bot.Self.Id * 31 + bot.CalloutTick) % lines.Length];
            _audio.Callout(bot.Self, i, line);
            float distance = System.Numerics.Vector3.Distance(bot.Self.Position, _player.State.Position);
            if (distance <= hud.SubtitleRange_m)
            {
                _hud.Subtitle(bot.Self.Name, bot.Self.Team, line);
            }
        }

        // The bots' contacts, said aloud or not (most aren't: shouting is rationed), reach you as they reach the bots on
        // your side. Joined, they come from the host (OnSimEvent).
        for (int n = Math.Max(_contactsMarked, _squad.ContactsShared - BotSquad.RecentContacts); n < _squad.ContactsShared; n++)
        {
            if (_squad.RecentContact(n, out BotSquad.Contact c) && IsBot(c.From))
            {
                MarkContact(c.From, c.Team, c.FromEye, c.TargetId, c.At);
            }
        }

        _contactsMarked = _squad.ContactsShared;
    }

    /// <summary>Whether <paramref name="id"/> is one of the bots this copy runs (a person's callout key is marked by <see cref="OnCalledOut"/>).</summary>
    private bool IsBot(int id)
    {
        foreach (BotBrain bot in _bots)
        {
            if (bot.Self.Id == id)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Teammate contacts marked on your HUD this round (the bot match prints it).</summary>
    public int ContactMarks { get; private set; }

    /// <summary>
    /// A bot on your side telling its side where an enemy is, from where the call carries to you (as it would to a bot
    /// there: the callout range, less through walls): a mark over where it saw them, as a person's callout key makes.
    /// One mark an enemy, moved to the newest call.
    /// </summary>
    private void MarkContact(int from, byte team, System.Numerics.Vector3 fromEye, int targetId, System.Numerics.Vector3 at)
    {
        PlayerState you = _player.State;
        if (_mode.Kind == MatchModeKind.FreeForAll || team != you.Team || from == you.Id || !you.Alive ||
            _sim.FindPlayer(targetId) is not { Alive: true } target || _sim.FindPlayer(from) is not { } caller ||
            !_squad.CallCarries(fromEye, you.EyePosition))
        {
            return;
        }

        _marks.Add(at.ToGodot(), TeamColor(team), $"{caller.Name}: {target.Name}", targetId);
        if (ContactMarks++ == 0 && _botMatch)
        {
            GD.Print($"BOT MATCH {_match.Elapsed:0.0} s (frame {Engine.GetProcessFrames()}): the first teammate contact on your HUD, {caller.Name}: {target.Name}");
        }
    }

    /// <summary>The characters' worst pop (the head or a hand jerking against the body in one frame) and how often, with models drawn.</summary>
    private string PopsLine()
    {
        var posed = _pawns.Where(p => p.Visual.Model is not null).Select(p => p.Visual.Model!.Poser).ToList();
        return posed.Count == 0 ? "" : $"poses: worst pop {posed.Max(p => p.WorstPop):0.0} cm ({posed.MaxBy(p => p.WorstPop)!.WorstPopWhat}), {posed.Sum(p => p.PopFrames)} frames over 3 cm; ";
    }

    /// <summary>Garbage collections since the level loaded, by generation (the bot match's line, to catch the game collecting often).</summary>
    private string CollectionsLine() =>
        $"{GC.CollectionCount(0) - _collectionsAtLoad[0]}/{GC.CollectionCount(1) - _collectionsAtLoad[1]}/{GC.CollectionCount(2) - _collectionsAtLoad[2]}";

    /// <summary>No input: what an eliminated local player sends while spectating.</summary>
    private sealed class IdlePilot : ICommandSource
    {
        public InputCommand Next(int tick, PlayerState state) => new() { Tick = tick, Yaw = state.Yaw, Pitch = state.Pitch };
    }

    /// <summary>The bot playing your slot (<c>--call-outs</c>) that also presses the callout key whenever it spots someone.</summary>
    private sealed class CallingPilot : ICommandSource
    {
        private readonly BotPilot _pilot;
        private readonly BotBrain _brain;
        private int _called = -1;

        public CallingPilot(BotBrain brain)
        {
            _brain = brain;
            _pilot = new BotPilot(brain);
        }

        public InputCommand Next(int tick, PlayerState state)
        {
            InputCommand cmd = _pilot.Next(tick, state);
            if (_brain.Callout == CalloutKind.Spotted && _brain.CalloutTick > _called)
            {
                _called = _brain.CalloutTick;
                cmd.Buttons |= InputButtons.Callout;
            }

            return cmd;
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!_ready)
        {
            return;
        }

        if (_chat is { Typing: false } chat && _overlay is null && !_pause.Open && (e.IsActionPressed("chat") || e.IsActionPressed("chat_team")))
        {
            // Playing with others: typing, your keys don't move you.
            _player.Muted = true;
            chat.Open(teamOnly: e.IsActionPressed("chat_team"));
            GetViewport().SetInputAsHandled();
            return;
        }

        bool playing = _overlay is null && _spectator is null && _player.AutoPilot is null;
        if (playing && e is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (e.IsActionPressed("pause"))
        {
            if (playing && !_pause.Open)
            {
                _pause.Toggle();
            }
        }
        else if (e.IsActionPressed("debug_help"))
        {
            _hud.ShowHelp = !_hud.ShowHelp;
        }
        else if (e.IsActionPressed("debug_stress"))
        {
            _botDebug.Visible = !_botDebug.Visible;
            _hud.Toast(_botDebug.Visible ? "Bot debug overlay on" : "Bot debug overlay off");
        }
        else if (e.IsActionPressed("debug_arc"))
        {
            _arc.Enabled = !_arc.Enabled;
            _hud.Toast(_arc.Enabled ? "Arc preview on" : "Arc preview off");
        }
        else if (e.IsActionPressed("debug_perf"))
        {
            _hud.ShowPerf = !_hud.ShowPerf;
        }
        else if (e.IsActionPressed("toggle_crosshair"))
        {
            _settings.Crosshair = !_settings.Crosshair;
            SaveAndToast(_settings.Crosshair ? "Crosshair on" : "Crosshair off");
        }
        else if (e.IsActionPressed("toggle_head_bob"))
        {
            _settings.HeadBob = !_settings.HeadBob;
            SaveAndToast(_settings.HeadBob ? "Head-bob on" : "Head-bob off");
        }
        else if (e.IsActionPressed("toggle_vsync"))
        {
            _settings.Vsync = !_settings.Vsync;
            _settings.ApplyWindow();
            SaveAndToast(_settings.Vsync ? "V-sync on" : "V-sync off");
        }
        else if (e.IsActionPressed("cycle_graphics"))
        {
            GraphicsPresetDef[] presets = _view.Graphics.Presets;
            int index = Array.FindIndex(presets, p => p.Name == _settings.GraphicsPreset);
            GraphicsPresetDef next = presets[(index + 1) % presets.Length];
            _settings.GraphicsPreset = next.Name;
            ApplyGraphics(_view.Graphics.Effective(next.Name, _settings.Graphics));
            SaveAndToast($"Graphics: {next.Name}");
        }
        else if (e.IsActionPressed("toggle_invert_y"))
        {
            _settings.InvertY = !_settings.InvertY;
            SaveAndToast(_settings.InvertY ? "Invert Y on" : "Invert Y off");
        }
        else if (e.IsActionPressed("toggle_fullscreen"))
        {
            _settings.WindowMode = _settings.WindowMode == "windowed" ? "borderless" : "windowed";
            _settings.ApplyWindow();
            SaveAndToast(_settings.WindowMode == "windowed" ? "Windowed" : "Full screen");
        }
        else if (e.IsActionPressed("fov_down") || e.IsActionPressed("fov_up"))
        {
            float step = e.IsActionPressed("fov_up") ? _view.Camera.FovStep_deg : -_view.Camera.FovStep_deg;
            _settings.FovDeg = Mathf.Clamp(_settings.FovDeg + step, _view.Camera.FovMin_deg, _view.Camera.FovMax_deg);
            SaveAndToast($"Field of view {_settings.FovDeg:0}° (horizontal)");
        }
        else if (e.IsActionPressed("sensitivity_down") || e.IsActionPressed("sensitivity_up"))
        {
            float step = e.IsActionPressed("sensitivity_up") ? _view.Look.SensitivityStep : -_view.Look.SensitivityStep;
            _settings.MouseSensitivityDegPerCount = Mathf.Clamp(_settings.MouseSensitivityDegPerCount + step, 0.005f, 1f);
            SaveAndToast($"Mouse sensitivity {_settings.MouseSensitivityDegPerCount:0.000}°/count");
        }
        else if (e.IsActionPressed("reload_data"))
        {
            GetTree().ReloadCurrentScene();
        }
    }

    /// <summary>A graphics preset: environment, shadows and MSAA, then the weeds, sunbeams and window lights.</summary>
    private void ApplyGraphics(GraphicsPresetDef preset)
    {
        Atmosphere.ApplyPreset(_environment, _sun, GetViewport(), preset);
        float scale = float.TryParse(Args.Value("--render-scale"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
            out float given) ? given : _settings.RenderScale;
        Atmosphere.ApplyRenderScale(GetViewport(), scale, _view.Graphics);
        _weeds.ApplyPreset(preset);
        _groundDetail.Visible = preset.GroundDetail;
        if (_ripples is not null)
        {
            _ripples.Visible = preset.GroundDetail;
        }

        if (_roofDrips is not null)
        {
            _roofDrips.Visible = preset.GroundDetail;
        }

        _floorDebris.Visible = preset.GroundDetail;
        _oldPaint.Visible = preset.OldPaint;
        // Ambient occlusion does their job where the preset has it.
        _contact.Visible = !preset.Ssao;
        _shafts.ApplyPreset(preset);
    }

    /// <summary>The level's plan for the briefing, with where you and your team start.</summary>
    private LevelMap BriefingMap()
    {
        var map = new LevelMap { Name = "Map" };
        var team = new List<Vector3>();
        foreach (OpponentSpawn mate in _starts?.Teammates ?? Array.Empty<OpponentSpawn>())
        {
            team.Add(mate.Position.ToGodot());
        }

        map.Configure(_level, new Vector2(380f, 320f), (_start.Position.ToGodot(), _start.Yaw), team, _teamColor);
        if (_match.Objective is { } objective)
        {
            map.MarkObjective(objective, Color.FromHtml(_view.Objectives.Color), _view.TeamColors);
        }

        return map;
    }

    private void SaveAndToast(string message)
    {
        _settings.Save();
        _hud.Toast(message);
    }

    private Aabb RenderBounds()
    {
        Pb.Sim.Collision.Aabb b = _level.Bounds;
        Vector3 min = b.Min.ToGodot() - new Vector3(5, 5, 5);
        Vector3 max = b.Max.ToGodot() + new Vector3(5, 5, 5);
        return new Aabb(min, max - min);
    }

    /// <summary>
    /// The menus' choice, else --level, --mode, --size and --tier, else the first area, the first mode at its
    /// default size, on "normal" (or the area's first tier). The place in the area is picked separately.
    /// </summary>
    private static (AreaEntryDef Entry, TierDef Tier, GameMode Mode, int Size, ObjectiveChoice Objective) PickRound(GameData data)
    {
        string? levelId = GameSession.LevelId ?? Args.Value("--level");
        AreaEntryDef? entry = levelId is not null
            ? data.Areas.Areas.FirstOrDefault(l => l.Id == levelId)
            : data.Areas.Areas.FirstOrDefault();
        if (entry is null)
        {
            throw new InvalidOperationException(levelId is null
                ? "No areas to play in."
                : $"No area '{levelId}' (known: {string.Join(", ", data.Levels.Keys)})");
        }

        MatchRules rules = data.Config.Rules;
        string modeId = GameSession.ModeId ?? Args.Value("--mode") ?? rules.Modes[0].Id;
        GameMode mode = rules.FindMode(modeId)
            ?? throw new InvalidOperationException($"No mode '{modeId}' (known: {string.Join(", ", rules.Modes.Select(m => m.Id))})");
        int size = GameSession.ModeId is not null && GameSession.Size is { } chosen ? chosen
            : int.TryParse(Args.Value("--size"), out int given) ? given
            : mode.DefaultSize;
        int smallest = mode.Kind == MatchModeKind.FreeForAll ? 2 : 1;
        if (size < smallest || mode.PlayersFor(size) > rules.MaxPlayers)
        {
            // A mode's size counts different people (opponents, everyone, or a side), so say which, and what fits.
            string counts = mode.Kind switch
            {
                MatchModeKind.Solo => "opponents",
                MatchModeKind.FreeForAll => "players, you included",
                _ => "players a side",
            };
            int largest = Enumerable.Range(smallest, rules.MaxPlayers).LastOrDefault(s => mode.PlayersFor(s) <= rules.MaxPlayers, smallest);
            throw new InvalidOperationException(size < smallest
                ? $"{mode.DisplayName} needs a size of at least {smallest} (its size counts {counts})"
                : $"{mode.DisplayName} can't be played at size {size}: its size counts {counts}, so that's {mode.PlayersFor(size)} people, " +
                  $"and a round holds at most {rules.MaxPlayers}. The largest {mode.DisplayName} round is size {largest}.");
        }

        // The objective: the menu's, or --objective; free-for-all is always eliminate.
        ObjectiveRules objectives = rules.Objectives;
        string objectiveId = (GameSession.LevelId is not null ? GameSession.ObjectiveId : null) ?? Args.Value("--objective") ?? RecordBook.Eliminate;
        ObjectiveChoice objective = objectives.Kinds.FirstOrDefault(k => RecordBook.IdOf(k.Kind) == objectiveId)
            ?? throw new InvalidOperationException($"No objective '{objectiveId}' (known: {string.Join(", ", objectives.Kinds.Select(k => RecordBook.IdOf(k.Kind)))})");
        if (mode.Kind == MatchModeKind.FreeForAll)
        {
            objective = objectives.Find(ObjectiveKind.Eliminate)!;
        }
        else if (!objective.PlayableIn(mode.Kind))
        {
            throw new InvalidOperationException($"{objective.DisplayName} is played by two teams: add --mode=teams");
        }
        else if (!data.Levels[entry.Id].Objectives.Offers(objective.Kind))
        {
            throw new InvalidOperationException($"{entry.DisplayName} has no places for {objective.DisplayName}");
        }

        TierDef[] tiers = entry.Tiers;
        string tierId = GameSession.TierId ?? Args.Value("--tier") ?? "normal";
        return (entry, tiers.FirstOrDefault(t => t.Id == tierId) ?? tiers[0], mode, size, objective);
    }
}
