using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Pb.Game.Ai;
using Pb.Game.Audio;
using Pb.Game.Ballistics;
using Pb.Game.Player;
using Pb.Game.Ui;
using Pb.Game.World;
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
///   --level=ID            which level to load (default: the first playable one in the ladder); given to
///                         the main scene, it skips the menu once (exported builds always start there)
///   --mode=ID             which mode (rules.jsonc "modes": solo, ffa, teams; default: the first)
///   --size=N              how many: opponents (solo), players (free-for-all) or players a side (teams);
///                         default: the mode's default size
///   --tier=ID             which difficulty tier (default: "normal", or the level's first)
///   --smoke-test[=ticks]  headless CI check: walk in through the gate firing, exit code 0/1
///   --shots               camera tour of the level's viewpoints (screenshots with --write-movie); --views=…
///                         gives your own instead ("x,y,z,yaw,pitch" or "x,y,z>tx,ty,tz", separated by ";")
///   --posture-demo        scripted lean / shoulder swap / muzzle-in-cover sequence at a wall corner
///   --duel-demo           scripted elimination of an opponent, then of you (mask spray, spectator view)
///   --duel-distance=M     how far from the bot the duel starts (default 6 m; 2 for a close-up)
///   --round-tour          the round's screens in order: briefing, pause menu, a duel, spectator view, summary
///   --bot-demo            bots fighting you from cover, seen from above with the F3 overlay, then through your eyes
///   --gait-demo           an opponent walks, runs, sprints, strafes, backs off and walks crouched, seen from the side
///   --bot-match           CI: a bot plays your slot (it hunts round the opponent spawns) until the round ends
///   --time-limit=SECONDS  overrides the tier's time limit (keeps the bot match short in CI)
///   --preset=NAME         uses that graphics preset instead of the saved one (for comparing their cost)
///   --seed=N              deals round N's starts and randomness (rounds normally get a new random seed; scripted
///                         runs use the data's seed and, in solo, the level's roster, so they play out the same every time)
///   --random-spawns       deals random starts in a scripted solo run too (CI's bot match; other modes always do)
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
    private LadderLevelDef _entry = null!;
    private LadderTierDef _tier = null!;
    private GameMode _mode = null!;
    private int _size;
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
    private BotSquad _squad = null!;
    private BotDebugOverlay _botDebug = null!;
    private LevelSmokeTest? _smoke;
    private SpectatorView? _spectator;
    private PickupVisuals _pickups = null!;
    private WeedField _weeds = null!;
    private GroundDetail _groundDetail = null!;
    private FloorDebris _floorDebris = null!;
    private OldPaint _oldPaint = null!;
    private ContactShadows _contact = null!;
    private Creepers _creepers = null!;
    private LightShafts _shafts = null!;
    private string? _hitBy;
    private bool _scripted;
    private bool _botMatch;
    private SpawnPlan? _starts;
    private SpawnPoint _start;
    private Color _teamColor;
    private bool _summaryShown;
    private bool _summaryEarly;
    private bool _ready;

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
            InputSetup.Apply(Jsonc.Load<InputDef>(source, InputDef.File));
            (_entry, _tier, _mode, _size) = PickRound(_data);
            _level = _data.Levels[_entry.Id];
            _round = new RoundInfo(_level, _tier, _mode, _size);
        }
        catch (Exception ex) when (ex is DataException or InvalidOperationException)
        {
            GD.PushError(ex.Message);
            _hud.ShowFatal(ex.Message);
            if (Args.Has("--smoke-test"))
            {
                GetTree().Quit(1);
            }

            return;
        }

        _settings = GameSettings.Load(_view);
        _settings.ApplyVolume();
        DisplayServer.WindowSetVsyncMode(_settings.Vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);

        // Quiet opponents for the screenshot tours; the smoke test turns them hostile when it's ready.
        bool botDemo = Args.Has("--bot-demo");
        bool gaitDemo = Args.Has("--gait-demo");
        _botMatch = Args.Has("--bot-match");
        _scripted = Args.Has("--shots") || Args.Has("--posture-demo") || Args.Has("--duel-demo") || Args.Has("--smoke-test") || botDemo ||
                    gaitDemo || _botMatch;
        bool roundTour = Args.Has("--round-tour");

        // Every round deals a new seed and random starts, so you can't learn where everyone is. Scripted
        // runs keep the data's seed and, in solo, the level's roster, so they play out the same every time.
        bool repeatable = _scripted || roundTour;
        ulong seed = ulong.TryParse(Args.Value("--seed"), out ulong given) ? given
            : repeatable ? _data.Config.MatchSeed
            : (ulong)System.Random.Shared.NextInt64();
        _sim = new SimWorld(_data.Config, seed);
        _sim.LoadLevel(_level);
        var navWatch = Stopwatch.StartNew();
        _squad = BotSquad.ForLevel(_sim, _data.Bots, _level);
        double navMs = navWatch.Elapsed.TotalMilliseconds;
        _starts = !repeatable || Args.Has("--random-spawns") || _mode.Kind != MatchModeKind.Solo
            ? SpawnPlanner.Plan(_level, _squad.Cover, _sim.Collision, _data.Config.Rules.Spawning, _data.Bots,
                RoundShape.Of(_mode, _size), _data.Config.Movement.StandEyeHeight, seed)
            : null;
        SpawnPoint start = _starts?.You ?? _level.PlayerSpawns[0];
        _start = start;
        if (_starts is not null)
        {
            static string Describe(OpponentSpawn o) => $"{o.Id} {o.Roles[0]}{(o.Patrol is null ? "" : " on " + o.Patrol.Id)}";
            GD.Print($"Starts (seed {seed}, {_round.Line}): you at {start.Position}" +
                     (_starts.Teammates.Count > 0 ? "; with you: " + string.Join(", ", _starts.Teammates.Select(Describe)) : "") +
                     "; against you: " + string.Join(", ", _starts.Opponents.Select(Describe)));
        }

        PlayerState state = _sim.AddPlayer(0, 0, start.Position, start.Yaw);
        state.Name = "You";
        if (_view.TeamColors.Length < _mode.PlayersFor(_size) && _mode.Kind == MatchModeKind.FreeForAll)
        {
            GD.PushWarning($"presentation.jsonc has {_view.TeamColors.Length} team colours for a {_mode.PlayersFor(_size)}-player free-for-all: some players share one");
        }

        Color teamColor = TeamColor(state.Team);
        _teamColor = teamColor;

        GraphicsPresetDef preset = _view.Graphics.Find(Args.Value("--preset") ?? _settings.GraphicsPreset);
        _world.Build(_level, new MaterialLibrary(_level.Materials), preset.AmbientProbes, _view.Horizon, _view.Woods);
        var dressWatch = Stopwatch.StartNew();
        _weeds = new WeedField { Name = "Weeds" };
        AddChild(_weeds);
        _weeds.Build(_level, _sim.Collision, _view.Weeds);
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
        var birds = new Birds { Name = "Birds" };
        AddChild(birds);
        Pb.Sim.Collision.Aabb bounds = _level.Bounds;
        birds.Build(new Vector3((bounds.Min.X + bounds.Max.X) * 0.5f, 0f, (bounds.Min.Z + bounds.Max.Z) * 0.5f), LevelBuilder.StableHash(_level.Id), _view.Birds, _level, _sim.Collision);
        _shafts = new LightShafts { Name = "LightShafts" };
        AddChild(_shafts);
        _shafts.Build(_level, _sim.Collision, _view.Lighting, _view.Shafts, _view.Dust, _view.WindowLight);
        GD.Print($"Level dressing: {_weeds.TuftCount} weed tufts, {_groundDetail.CardCount} things on the ground, {_floorDebris.Count} on the floors indoors, {fittings.Count} manholes and drains, {_oldPaint.SplatCount} old paint splats, {_creepers.PatchCount} creepers, {runOff.Count} run-off streaks, {markings.CardCount} marking cards, {_contact.Count} contact shadows, {cobwebs.Count} cobwebs, {hangings.Count} things on the walls, {birds.Count} birds, {_shafts.BeamCount} sunbeams, {_shafts.LightCount} window and bounce lights " +
                 $"in {dressWatch.Elapsed.TotalMilliseconds:0} ms");
        Atmosphere.ApplyLighting(_environment, _sun, _view.Lighting);
        ApplyGraphics(preset);

        _player.Initialize(_sim, state, _view, _settings, teamColor);
        SpawnBots(hostile: botDemo || _botMatch || (!_scripted && !roundTour));
        _botDebug = new BotDebugOverlay { Name = "BotDebug" };
        AddChild(_botDebug);
        _botDebug.Initialize(_squad);
        _balls.Initialize(_sim.Ballistics, _view, state.Id, _player.VisualMuzzlePosition, RenderBounds());
        _splats.Initialize(_view, SplatParent);
        var fx = GetNode<ImpactFx>("ImpactFx");
        fx.Initialize(_view);
        _arc.Initialize(_sim, state, _view);
        _arc.Enabled = _view.ArcPreview.EnabledOnStart;

        bool headless = DisplayServer.GetName() == "headless";
        var audio = GetNode<AudioDirector>("Audio");
        if (!headless)
        {
            audio.Initialize(state, _view);
        }

        _hud.Initialize(_sim, state, _driver, _settings, _view, () => (_splats.ActiveCount, _splats.Capacity), () => _arc.Summary);
        _hud.InitializeMatch(_view, MatchClock);
        _hud.ShowHelp = false;
        MatchSetup setup = MatchSetup.From(_tier, state.Id, _mode.Kind);
        if (float.TryParse(Args.Value("--time-limit"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float limit))
        {
            setup = new MatchSetup
            {
                HeroId = setup.HeroId, Mode = setup.Mode, TimeLimit = limit, StartPods = setup.StartPods, BotPods = setup.BotPods, Pickups = setup.Pickups,
            };
        }

        _match = _sim.StartMatch(setup);
        _pickups = new PickupVisuals { Name = "Pickups" };
        AddChild(_pickups);
        _pickups.Build(_sim.Pickups);
        _overlays = new CanvasLayer { Name = "Overlays", Layer = 8 };
        AddChild(_overlays);
        _pause = new PauseMenu { Name = "Pause" };
        AddChild(_pause);
        _pause.Build(_settings, _view, s => ApplyGraphics(_view.Graphics.Find(s.GraphicsPreset)),
            restart: () => GetTree().ReloadCurrentScene());

        var maskSpray = new MaskSprayOverlay { Name = "MaskSpray" };
        AddChild(maskSpray);
        maskSpray.Initialize(_view, state.Id);

        _driver.Initialize(_sim);
        _driver.AddDriver(_player);
        foreach (OpponentPawn pawn in _pawns)
        {
            _driver.AddDriver(pawn);
        }

        _driver.AddListener(maskSpray);
        _driver.AddListener(_pickups);
        _driver.AddListener(_balls);
        _driver.AddListener(_splats);
        _driver.AddListener(fx);
        if (!headless)
        {
            _driver.AddListener(audio);
        }

        _driver.AddListener(_hud);
        _driver.AddListener(this);

        if (_scripted)
        {
            _sim.GoLive();
        }

        if (Args.Ticks("--smoke-test", 1800) is { } ticks)
        {
            _smoke = new LevelSmokeTest(this, _sim, _driver, _player, _world, ticks, _pawns, _bots);
            _player.AutoPilot = _smoke.Pilot;
        }
        else if (Args.Has("--posture-demo"))
        {
            _player.Teleport(PostureDemo.Position, PostureDemo.Yaw);
            _player.AutoPilot = new PostureDemo(this, _sim);
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
            _player.AutoPilot = new BotPilot(brain);
            _hud.ShowPerf = false;
            GD.Print($"BOT MATCH a hunter bot plays your slot in {_round.Line} with {_bots.Count} {_tier.Bots} bots, {setup.TimeLimit:0} s on the clock");
        }
        else if (gaitDemo)
        {
            var demo = new GaitDemo { Name = "GaitDemo" };
            AddChild(demo);
            demo.Start(_sim, _pawns, _hud, _view.Camera.FarClip_m);
        }
        else if (botDemo)
        {
            var demo = new BotDemo { Name = "BotDemo" };
            AddChild(demo);
            demo.Start(_sim, _player, _pawns, _botDebug, _hud, _view.Camera.FarClip_m);
        }
        else if (roundTour)
        {
            ShowOverlay(RoundScreens.Briefing(_round, BeginRound, BackToLevelSelect, BriefingMap()));
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
        else
        {
            ShowOverlay(RoundScreens.Briefing(_round, BeginRound, BackToLevelSelect, BriefingMap()));
        }

        GD.Print($"Level {_level.Id} ({_round.Line}, {_pawns.Count} {_tier.Bots} bots): {_level.Primitives.Count} primitives, " +
                 $"{_world.MeshCount} meshes ({_world.ShapeCount} props built in code, {_world.FramedOpenings} framed openings and {_world.DressedBuildings} buildings with gutters or trusses, {_world.DressedWalls} dressed walls, {_world.SkirtedFaces} skirted wall faces, {_world.SceneryCount} pylons and poles beyond, {_world.ShapeTriangles} triangles), " +
                 $"{_world.ColliderCount} walking colliders, {_sim.Collision.Colliders.Count} paint colliders, " +
                 $"{_squad.Grid.SpanCount} nav spans and {_squad.Cover.Points.Count} cover points in {navMs:0} ms, preset {preset.Name}, " +
                 $"art {(ArtFiles.Disabled ? "off" : "on")} ({_pawns.Count(o => o.Visual.HasModel)} bots drawn as models)");
        _ready = true;
    }

    public void OnSimEvent(in SimEvent e)
    {
        _squad.Hear(e);
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
            string[] roster = _entry.Roster ?? Array.Empty<string>();
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
            pawn.Initialize(_sim, state, TeamColor(team), new BotPilot(brain), _view.Characters, i);
            _pawns.Add(pawn);
            _bots.Add(brain);
        }

        _calloutsSeen = Enumerable.Repeat(-1, _bots.Count).ToArray();
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
        OpponentPawn? pawn = _pawns.FirstOrDefault(o => o.State.Id == id);
        return pawn?.Visual.PartNode((Pb.Sim.Collision.HitboxPart)part, point);
    }

    private void OnEliminated(in SimEvent e)
    {
        PlayerState? victim = _sim.FindPlayer(e.TargetId);
        PlayerState? shooter = _sim.FindPlayer(e.PlayerId);
        if (victim is null)
        {
            return;
        }

        string part = SpectatorView.PartName((Pb.Sim.Collision.HitboxPart)e.Extra);
        if (victim == _player.State)
        {
            StartSpectating(victim, shooter);
            return;
        }

        _pawns.FirstOrDefault(o => o.State == victim)?.CallHit();
        if (shooter == _player.State && victim.Team == shooter.Team)
        {
            _hud.Toast($"You put out your teammate {victim.Name}!", 3.0);
        }
        else if (shooter == _player.State)
        {
            float distance = System.Numerics.Vector3.Distance(shooter.EyePosition, victim.EyePosition);
            int left = _sim.Players.Count(p => p.Alive && p.Team != _player.State.Team);
            _hud.Toast($"You eliminated {victim.Name} · {part} · {distance:0} m   ({left} left)", 3.0);
        }
    }

    private void StartSpectating(PlayerState victim, PlayerState? shooter)
    {
        if (_spectator is not null)
        {
            return;
        }

        if (shooter is not null)
        {
            float distance = System.Numerics.Vector3.Distance(shooter.EyePosition, victim.EyePosition);
            _hitBy = $"{shooter.Name} got you: {SpectatorView.PartName(victim.EliminatedPart)}, {distance:0} m.";
        }

        _player.AutoPilot = new IdlePilot();
        _player.ViewModel.Visible = false;
        _hud.Visible = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _spectator = new SpectatorView { Name = "Spectator" };
        AddChild(_spectator);
        // Once you've seen who got you: watch the players still in while the round goes on without you, or
        // the summary (scripted runs just end; the smoke test and the bot match end themselves).
        _spectator.Start(victim, shooter, _view.Spectator, _view.Camera.FarClip_m, () =>
        {
            if (_scripted)
            {
                if (_smoke is null && !_botMatch)
                {
                    GetTree().Quit();
                }
            }
            else if (_match.Phase != MatchPhase.Ended && Watchable().Count > 0)
            {
                _spectator.Follow(Watchable, TeamColor, ShowSummary, _sim.Collision);
            }
            else
            {
                ShowSummary();
            }
        });
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

    /// <summary>The bot match is over: report how it went, pass if nothing went wrong on the way.</summary>
    private void FinishBotMatch()
    {
        PlayerStats you = _match.StatsFor(_player.State.Id)!;
        int errors = _driver.ErrorCount;
        bool ok = errors == 0 && _match.Outcome != RoundOutcome.None;
        int botOnBot = _sim.Players.Count(p => !p.Alive && p != _player.State && p.EliminatedBy > 0);
        GD.Print($"SMOKE {(ok ? "PASS" : "FAIL")}: bot match ({_round.Line}) {_match.Outcome} after {_match.Elapsed:0} s: you put out " +
                 $"{you.Eliminations} of {_sim.Players.Count(p => p.Team != _player.State.Team)}, {you.Shots} shots, {you.Hits} hits, " +
                 $"{you.Pickups} pickups; bots put out by bots: {botOnBot}; simErrors={errors} avgStepMs={_driver.AverageStepMs:0.000}");
        GetTree().Quit(ok ? 0 : 1);
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
        PlayerState? winner = _mode.Kind == MatchModeKind.FreeForAll && over && _match.Outcome == RoundOutcome.Eliminated
            ? players.FirstOrDefault(p => p.Alive)
            : null;
        var facts = new SummaryFacts
        {
            Over = over,
            YouAreOut = !you.Alive,
            HitBy = _hitBy,
            Opponents = players.Count(p => p.Team != you.Team),
            OursLeft = players.Count(p => p.Alive && p.Team == you.Team),
            OthersLeft = players.Count(p => p.Alive && p.Team != you.Team),
            Placing = _match.Placing(you.Id),
            Players = players.Count,
            Winner = winner?.Name,
        };
        ShowOverlay(RoundScreens.Summary(_round, _match, _match.StatsFor(you.Id)!, facts,
            retry: () => GetTree().ReloadCurrentScene(),
            levelSelect: BackToLevelSelect,
            mainMenu: () => GetTree().ChangeSceneToFile(GameSession.MainScene)));
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

    /// <summary>Shows what the bots shout as subtitles, when you're close enough to hear it.</summary>
    public override void _Process(double delta)
    {
        if (!_ready)
        {
            return;
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
            float distance = System.Numerics.Vector3.Distance(bot.Self.Position, _player.State.Position);
            if (lines.Length > 0 && distance <= hud.SubtitleRange_m)
            {
                _hud.Subtitle(bot.Self.Name, bot.Self.Team, lines[(bot.Self.Id * 31 + bot.CalloutTick) % lines.Length]);
            }
        }
    }

    /// <summary>No input: what an eliminated local player sends while spectating.</summary>
    private sealed class IdlePilot : ICommandSource
    {
        public InputCommand Next(int tick, PlayerState state) => new() { Tick = tick, Yaw = state.Yaw, Pitch = state.Pitch };
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!_ready)
        {
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
            DisplayServer.WindowSetVsyncMode(_settings.Vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
            SaveAndToast(_settings.Vsync ? "V-sync on" : "V-sync off");
        }
        else if (e.IsActionPressed("cycle_graphics"))
        {
            GraphicsPresetDef[] presets = _view.Graphics.Presets;
            int index = Array.FindIndex(presets, p => p.Name == _settings.GraphicsPreset);
            GraphicsPresetDef next = presets[(index + 1) % presets.Length];
            _settings.GraphicsPreset = next.Name;
            ApplyGraphics(next);
            SaveAndToast($"Graphics: {next.Name}");
        }
        else if (e.IsActionPressed("toggle_invert_y"))
        {
            _settings.InvertY = !_settings.InvertY;
            SaveAndToast(_settings.InvertY ? "Invert Y on" : "Invert Y off");
        }
        else if (e.IsActionPressed("toggle_fullscreen"))
        {
            bool full = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen;
            DisplayServer.WindowSetMode(full ? DisplayServer.WindowMode.Windowed : DisplayServer.WindowMode.Fullscreen);
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
        Atmosphere.ApplyRenderScale(GetViewport(), _settings.RenderScale, _view.Graphics);
        _weeds.ApplyPreset(preset);
        _groundDetail.Visible = preset.GroundDetail;
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
    /// The menus' choice, else --level, --mode, --size and --tier, else the first playable level, the first
    /// mode at its default size, on "normal" (or the level's first tier).
    /// </summary>
    private static (LadderLevelDef Entry, LadderTierDef Tier, GameMode Mode, int Size) PickRound(GameData data)
    {
        string? levelId = GameSession.LevelId ?? Args.Value("--level");
        LadderLevelDef? entry = levelId is not null
            ? data.Ladder.Levels.FirstOrDefault(l => l.Id == levelId && data.Levels.ContainsKey(l.Id))
            : data.Ladder.Levels.FirstOrDefault(l => data.Levels.ContainsKey(l.Id));
        if (entry is null)
        {
            throw new InvalidOperationException(levelId is null
                ? "The ladder has no playable level."
                : $"No playable level '{levelId}' (known: {string.Join(", ", data.Levels.Keys)})");
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
            throw new InvalidOperationException($"{mode.DisplayName} can't be played at size {size} (at most {rules.MaxPlayers} people in a round)");
        }

        LadderTierDef[] tiers = entry.Tiers!;
        string tierId = GameSession.TierId ?? Args.Value("--tier") ?? "normal";
        return (entry, tiers.FirstOrDefault(t => t.Id == tierId) ?? tiers[0], mode, size);
    }
}
