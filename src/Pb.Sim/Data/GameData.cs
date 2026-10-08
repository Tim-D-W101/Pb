using System.Numerics;
using Pb.Sim.AI;
using Pb.Sim.Ballistics;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Gear;
using Pb.Sim.Level;
using Pb.Sim.Match;
using Pb.Sim.Players;
using Pb.Sim.Range;

namespace Pb.Sim.Data;

/// <summary>
/// Loads sim.jsonc and every file it references, validates them, and converts them into the SI
/// parameter objects the simulation uses. Any problem throws a <see cref="DataException"/> naming
/// the file and key.
/// </summary>
public sealed class GameData
{
    public const string DefaultSimFile = "sim.jsonc";

    private GameData(SimConfig config, RangeLayout range, StressSettings stress, KitCatalog kit, AreaListDef areas,
        IReadOnlyDictionary<string, LevelLayout> levels, BotConfig bots)
    {
        Config = config;
        Bots = bots;
        Range = range;
        Stress = stress;
        Kit = kit;
        Areas = areas;
        Levels = levels;
    }

    public SimConfig Config { get; }

    /// <summary>Bot navigation, senses, behaviours and difficulty tiers.</summary>
    public BotConfig Bots { get; }

    public RangeLayout Range { get; }

    public StressSettings Stress { get; }

    public KitCatalog Kit { get; }

    /// <summary>The areas to play in, every one open, in menu order.</summary>
    public AreaListDef Areas { get; }

    /// <summary>Every area's level, built and validated at load (keyed by level id), the whole of it; <see cref="LevelLayout.ForPlace"/> gives a round in one of its places.</summary>
    public IReadOnlyDictionary<string, LevelLayout> Levels { get; }

    public static GameData Load(IDataSource source, string simFile = DefaultSimFile)
    {
        SimDef sim = Jsonc.Load<SimDef>(source, simFile);
        SimFilesDef files = sim.Files;
        ProjectileDef projectile = Jsonc.Load<ProjectileDef>(source, files.Projectile);
        MarkerDef marker = Jsonc.Load<MarkerDef>(source, files.Marker);
        LoaderDef loader = Jsonc.Load<LoaderDef>(source, files.Loader);
        AirDef air = Jsonc.Load<AirDef>(source, files.Air);
        BreakModelDef breakModel = Jsonc.Load<BreakModelDef>(source, files.BreakModel);
        MovementDef movement = Jsonc.Load<MovementDef>(source, files.Movement);
        HitboxesDef hitboxes = Jsonc.Load<HitboxesDef>(source, files.Hitboxes);
        RulesDef rules = Jsonc.Load<RulesDef>(source, files.Rules);
        RangeDef range = Jsonc.Load<RangeDef>(source, files.Range);
        StressDef stress = Jsonc.Load<StressDef>(source, files.Stress);
        BotConfig bots = BotConfig.From(
            Jsonc.Load<NavigationDef>(source, files.Navigation),
            Jsonc.Load<BrainDef>(source, files.Brain),
            Jsonc.Load<SensesDef>(source, files.Senses),
            Jsonc.Load<ArchetypesDef>(source, files.Archetypes),
            Jsonc.Load<DifficultyTiersDef>(source, files.Difficulty));

        var surfaces = new SurfaceRegistry(breakModel.Surfaces.Select(s => s.Name));
        var responses = breakModel.Surfaces
            .Select(s => new SurfaceResponse(s.Break50_mps, s.BreakWidth_mps, s.Restitution, s.TangentRetain))
            .ToList();

        var config = new SimConfig
        {
            TickRate = sim.TickRate_hz,
            MatchSeed = sim.MatchSeed,
            BallPoolCapacity = sim.BallPoolCapacity,
            Surfaces = surfaces,
            Projectile = ToProjectile(projectile),
            BreakModel = new BreakModel(surfaces, responses, breakModel.RestSpeed_mps, breakModel.MaxBounces),
            Shot = ToShot(marker),
            Fire = ToFire(marker),
            Loader = ToLoader(loader),
            Air = ToAir(air),
            Movement = ToMovement(movement, surfaces, files.Movement),
            Hitboxes = ToHitboxes(hitboxes, surfaces, files.Hitboxes),
            Rules = new MatchRules
            {
                Modes = rules.Modes.Select(m => new GameMode
                {
                    Id = m.Id,
                    DisplayName = m.DisplayName,
                    Description = m.Description,
                    Kind = m.Kind,
                    Sizes = m.Sizes,
                    DefaultSize = m.DefaultSize,
                    Roles = m.Roles.Select(r => (r.Role, r.Weight)).ToArray(),
                    RestlessAfter = m.RestlessAfter_s,
                }).ToArray(),
                MaxPlayers = rules.MaxPlayers,
                SettleTime = rules.SettleTime_s,
                TradeCountsAsClear = rules.TradeCountsAsClear,
                PickupRadius = rules.PickupRadius_m,
                AirPickupBelow = rules.AirPickupBelow,
                Spawning = new SpawnRules
                {
                    MinDistanceFromYou = rules.Spawning.MinDistanceFromYou_m,
                    MinSpacing = rules.Spawning.MinSpacing_m,
                    CoverShare = rules.Spawning.CoverShare,
                    CoverRoles = rules.Spawning.CoverRoles.Select(r => (r.Role, r.Weight)).ToArray(),
                    PatrolReach = rules.Spawning.PatrolReach_m,
                    FreeForAllSpacing = rules.Spawning.FreeForAllSpacing_m,
                    TeammatesWithin = rules.Spawning.TeammatesWithin_m,
                    TeammateSpacing = rules.Spawning.TeammateSpacing_m,
                    TeamSpread = rules.Spawning.TeamSpread_m,
                    ObjectiveClearance = rules.Spawning.ObjectiveClearance_m,
                },
                Doors = new DoorRules
                {
                    Reach = rules.Doors.Reach_m,
                    ConeCos = MathF.Cos(rules.Doors.Cone_deg * Units.DegreesToRadians),
                    HoldTime = rules.Doors.HoldTime_s,
                    EaseRate = rules.Doors.EaseRate,
                    Ajar = rules.Doors.Ajar,
                    RandomStart = rules.Doors.RandomStart.Select(c => (c.Start, c.Weight)).ToArray(),
                    BotPassOpen = rules.Doors.BotPassOpen,
                    BotRouteRound = rules.Doors.BotRouteRound,
                },
                Objectives = new ObjectiveRules
                {
                    Kinds = rules.Objectives.Kinds.Select(k => new ObjectiveChoice(k.Id, k.DisplayName, k.Description)).ToArray(),
                    GuardRole = rules.Objectives.GuardRole,
                    Retrieve = new RetrieveRules
                    {
                        PickupRadius = rules.Objectives.Retrieve.PickupRadius_m,
                        CarrierCanSprint = rules.Objectives.Retrieve.CarrierCanSprint,
                        ExitRadius = rules.Objectives.Retrieve.ExitRadius_m,
                        Guards = rules.Objectives.Retrieve.Guards,
                        NearShare = rules.Objectives.Retrieve.NearShare,
                        Near = rules.Objectives.Retrieve.Near_m,
                        AlarmInterval = rules.Objectives.Retrieve.AlarmInterval_s,
                        Chasers = rules.Objectives.Retrieve.Chasers,
                    },
                    Hold = new HoldRules
                    {
                        HoldTime = rules.Objectives.Hold.HoldTime_s,
                        Guards = rules.Objectives.Hold.Guards,
                        NearShare = rules.Objectives.Hold.NearShare,
                        Near = rules.Objectives.Hold.Near_m,
                        AlarmInterval = rules.Objectives.Hold.AlarmInterval_s,
                    },
                },
            },
        };
        CheckRoles(rules.Spawning.CoverRoles, "spawning.coverRoles", files.Rules, bots);
        if (!bots.Archetypes.ContainsKey(rules.Objectives.GuardRole))
        {
            throw new DataException(files.Rules, $"objectives.guardRole: '{rules.Objectives.GuardRole}' is not a bot behaviour");
        }

        for (int i = 0; i < rules.Modes.Length; i++)
        {
            CheckRoles(rules.Modes[i].Roles, $"modes[{i}].roles", files.Rules, bots);
        }

        KitCatalog kit = KitCatalog.Load(source, files.Kit, surfaces);
        AreaListDef areas = Jsonc.Load<AreaListDef>(source, files.Areas);
        var levels = new Dictionary<string, LevelLayout>(StringComparer.Ordinal);
        foreach (AreaEntryDef entry in areas.Areas)
        {
            LevelDef level = Jsonc.Load<LevelDef>(source, entry.File);
            if (level.Id != entry.Id)
            {
                throw new DataException(files.Areas, $"areas: entry '{entry.Id}' points at {entry.File}, whose id is '{level.Id}'");
            }

            LevelLayout built = LevelFactory.Build(level, entry.File, kit);
            CheckTiers(entry, built, files.Areas, bots);
            levels[entry.Id] = built;
        }

        return new GameData(config, ToRange(range, files.Range, surfaces), ToStress(stress), kit, areas, levels, bots);
    }

    public static ProjectileParams ToProjectile(ProjectileDef d) => new()
    {
        Radius = d.Diameter_mm * Units.MillimetresToMetres * 0.5f,
        Mass = d.Mass_g * Units.GramsToKilograms,
        DragCoefficient = d.DragCoefficient,
        AirDensity = d.AirDensity_kgm3,
        Gravity = d.Gravity_mps2,
        MaxLifetime = d.MaxLifetime_s,
    };

    public static ShotParams ToShot(MarkerDef d) => new()
    {
        MuzzleVelocity = d.MuzzleVelocity_mps,
        VelocityVariance = d.VelocityVariance_mps,
        DispersionHalfAngle = d.DispersionHalfAngle_deg * Units.DegreesToRadians,
        MovingDispersionPerSpeed = d.MovingDispersion_degPerMps * Units.DegreesToRadians,
        MaxDispersion = d.MaxDispersion_deg * Units.DegreesToRadians,
        InheritShooterVelocity = d.InheritShooterVelocity,
        MuzzleOffset = Validator.ToVector3(d.MuzzleOffset_m),
        MuzzleBlockedBreaks = d.MuzzleBlockedBreaks,
        ConvergenceDistance = d.ConvergenceDistance_m,
        MinAimDistance = d.MinAimDistance_m,
        MaxAimDistance = d.MaxAimDistance_m,
    };

    public static FireControlParams ToFire(MarkerDef d) => new()
    {
        RateCap = d.RateCap_bps,
        RampAfterShots = d.Ramping.AfterShots,
        RampMinPullRate = d.Ramping.MinPullRate_hz,
        BufferedPulls = d.BufferedPulls,
        DefaultMode = d.DefaultFireMode,
    };

    public static LoaderParams ToLoader(LoaderDef d) => new()
    {
        Capacity = d.Capacity,
        PodCount = d.Pods.Count,
        PodCapacity = d.Pods.Capacity,
        RefillTime = d.RefillTime_s,
        CancelOnFire = d.CancelOnFire,
        CancelOnSprint = d.CancelOnSprint,
        PartialOnInterrupt = d.PartialOnInterrupt,
    };

    public static AirParams ToAir(AirDef d) => new()
    {
        Volume = d.TankVolume_L * Units.LitresToCubicMetres,
        FillPressure = d.FillPressure_bar * Units.BarToPascals,
        RegulatorPressure = d.RegulatorOutput_bar * Units.BarToPascals,
        CutoffPressure = d.CutoffPressure_bar * Units.BarToPascals,
        GasPerShot = d.GasPerShot_barL * Units.BarLitresToPascalCubicMetres,
        VelocityExponent = d.VelocityExponent,
        LowWarningPressure = d.LowWarning_bar * Units.BarToPascals,
    };

    public static MovementParams ToMovement(MovementDef d, SurfaceRegistry surfaces, string file) => new()
    {
        WalkSpeed = d.WalkSpeed_mps,
        RunSpeed = d.RunSpeed_mps,
        SprintSpeed = d.SprintSpeed_mps,
        CrouchSpeed = d.CrouchSpeed_mps,
        GroundAcceleration = d.GroundAccel_mps2,
        GroundDeceleration = d.GroundDecel_mps2,
        AirAcceleration = d.AirAccel_mps2,
        StandEyeHeight = d.StandEyeHeight_m,
        CrouchEyeHeight = d.CrouchEyeHeight_m,
        StanceTransitionSpeed = d.StanceTransitionSpeed_mps,
        StandCapsuleHeight = d.StandCapsuleHeight_m,
        CrouchCapsuleHeight = d.CrouchCapsuleHeight_m,
        CapsuleRadius = d.CapsuleRadius_m,
        SprintMinForwardInput = d.SprintMinForwardInput,
        MaxPitch = d.MaxPitch_deg * Units.DegreesToRadians,
        Gravity = d.Gravity_mps2,
        LeanAngle = d.LeanAngle_deg * Units.DegreesToRadians,
        LeanPivotBelowEye = d.LeanPivotBelowEye_m,
        LeanInTime = d.LeanInTime_s,
        LeanReturnTime = d.LeanReturnTime_s,
        HeadRadius = d.HeadRadius_m,
        MaxHeadTurn = d.MaxHeadTurn_deg * Units.DegreesToRadians,
        ShoulderSwapTime = d.ShoulderSwapTime_s,
        SlideMinSpeed = d.SlideMinSpeed_mps,
        SlideBoost = d.SlideBoost_mps,
        SlideMaxSpeed = d.SlideMaxSpeed_mps,
        SlideFriction = d.SlideFriction_mps2,
        SlideEndSpeed = d.SlideEndSpeed_mps,
        SlideMaxTime = d.SlideMaxTime_s,
        SlideCooldown = d.SlideCooldown_s,
        SlideSteering = d.SlideSteering,
        SlideEyeHeight = d.SlideEyeHeight_m,
        SlideCapsuleHeight = d.SlideCapsuleHeight_m,
        JumpSpeed = d.JumpSpeed_mps,
        JumpCooldown = d.JumpCooldown_s,
        SprintRecoveryTime = d.SprintRecoveryTime_s,
        Climbing = new ClimbParams
        {
            Speed = d.Climbing.Speed_mps,
            Reach = d.Climbing.Reach_m,
            Standoff = d.Climbing.Standoff_m,
            GrabAngle = d.Climbing.GrabAngle_deg * Units.DegreesToRadians,
            StepOffSpeed = d.Climbing.StepOffSpeed_mps,
            LetGoSpeed = d.Climbing.LetGoSpeed_mps,
        },
        Footsteps = ToFootsteps(d.Footsteps, surfaces, file),
    };

    private static void CheckRoles(RoleWeightDef[] roles, string key, string file, BotConfig bots)
    {
        for (int i = 0; i < roles.Length; i++)
        {
            if (!bots.Archetypes.ContainsKey(roles[i].Role))
            {
                throw new DataException(file,
                    $"{key}[{i}].role: '{roles[i].Role}' is not a bot behaviour (known: {string.Join(", ", bots.Archetypes.Keys)})");
            }
        }
    }

    /// <summary>
    /// Every role of every opponent spawn must name a bot behaviour, every spawn the level's roster lists
    /// must exist, and each tier's bot difficulty must exist.
    /// </summary>
    private static void CheckTiers(AreaEntryDef entry, LevelLayout level, string areasFile, BotConfig bots)
    {
        var spawns = level.OpponentSpawns.ToDictionary(s => s.Id, StringComparer.Ordinal);

        // Random starts pick any of a spawn's roles, so every one must be a behaviour.
        foreach (OpponentSpawn spawn in level.OpponentSpawns)
        {
            foreach (string role in spawn.Roles)
            {
                if (!bots.Archetypes.ContainsKey(role))
                {
                    throw new DataException(entry.File,
                        $"opponentSpawns.{spawn.Id}.roles: '{role}' is not a bot behaviour (known: {string.Join(", ", bots.Archetypes.Keys)})");
                }
            }
        }

        foreach (string id in entry.Roster)
        {
            if (!spawns.ContainsKey(id))
            {
                throw new DataException(areasFile,
                    $"areas.{entry.Id}.roster: '{id}' is not an opponent spawn in {entry.File} (known: {string.Join(", ", spawns.Keys)})");
            }
        }

        foreach (TierDef tier in entry.Tiers)
        {
            if (!bots.Difficulty.ContainsKey(tier.Bots))
            {
                throw new DataException(areasFile,
                    $"areas.{entry.Id}.tiers.{tier.Id}.bots: unknown bot difficulty '{tier.Bots}' (known: {string.Join(", ", bots.Difficulty.Keys)})");
            }
        }
    }

    public static HitboxParams ToHitboxes(HitboxesDef d, SurfaceRegistry surfaces, string file)
    {
        if (!surfaces.TryGet(d.Surface, out SurfaceId surface))
        {
            throw new DataException(file, $"surface: unknown surface '{d.Surface}' (known: {string.Join(", ", surfaces.Names)})");
        }

        var lethal = new bool[Enum.GetValues<HitboxPart>().Length];
        foreach (HitboxPart part in d.LethalParts)
        {
            lethal[(int)part] = true;
        }

        static PartBox Box(PartBoxDef b) => new(Validator.ToVector3(b.Centre_m), Validator.ToVector3(b.Size_m) * 0.5f);

        return new HitboxParams
        {
            Surface = surface,
            LegsWidth = d.LegsWidth_m,
            LegsDepth = d.LegsDepth_m,
            TorsoWidth = d.TorsoWidth_m,
            TorsoDepth = d.TorsoDepth_m,
            TorsoTopBelowEye = d.TorsoTopBelowEye_m,
            Head = Box(d.Head),
            Mask = Box(d.Mask),
            Arms = Box(d.Arms),
            Marker = Box(d.Marker),
            Loader = Box(d.Loader),
            Tank = Box(d.Tank),
            EliminatedRaise = d.EliminatedRaise_m,
            EliminatedPitch = d.EliminatedPitch_deg * Units.DegreesToRadians,
            TuckMax = d.TuckMax_deg * Units.DegreesToRadians,
            TuckRate = d.TuckRate_degps * Units.DegreesToRadians,
            TuckStep = d.TuckStep_deg * Units.DegreesToRadians,
            TuckBarrelRadius = d.TuckBarrelRadius_m,
            ClimbArmsPitch = d.ClimbArmsPitch_deg * Units.DegreesToRadians,
            SlungBelowEye = d.SlungBelowEye_m,
            SlungBehind = d.SlungBehind_m,
            SlungPitch = d.SlungPitch_deg * Units.DegreesToRadians,
            SlungRoll = d.SlungRoll_deg * Units.DegreesToRadians,
            LethalParts = lethal,
            BallsInFlightCount = d.BallsInFlightCount,
            MaskSprayRadius = d.MaskSprayRadius_m,
        };
    }

    private static FootstepParams ToFootsteps(FootstepsDef d, SurfaceRegistry surfaces, string file)
    {
        var loudness = new float[surfaces.Count];
        Array.Fill(loudness, 1f);
        foreach ((string name, float value) in d.SurfaceLoudness)
        {
            if (!surfaces.TryGet(name, out SurfaceId id))
            {
                throw new DataException(file, $"footsteps.surfaceLoudness: unknown surface '{name}' (known: {string.Join(", ", surfaces.Names)})");
            }

            loudness[id.Value] = value;
        }

        return new FootstepParams
        {
            Stride = d.Stride_m,
            CrouchRadius = d.CrouchRadius_m,
            WalkRadius = d.WalkRadius_m,
            RunRadius = d.RunRadius_m,
            SprintRadius = d.SprintRadius_m,
            SlideRadius = d.SlideRadius_m,
            JumpRadius = d.JumpRadius_m,
            LandRadius = d.LandRadius_m,
            LandMinSpeed = d.LandMinSpeed_mps,
            ClimbStride = d.ClimbStride_m,
            ClimbRadius = d.ClimbRadius_m,
            SurfaceLoudness = loudness,
        };
    }

    public static StressSettings ToStress(StressDef d) => new()
    {
        TargetLiveBalls = d.TargetLiveBalls,
        MaxSpawnPerTick = d.MaxSpawnPerTick,
        Cannons = d.Cannons.Select(c => new CannonSpec
        {
            Position = Validator.ToVector3(c.Position_m),
            Yaw = c.Yaw_deg * Units.DegreesToRadians,
            YawSpread = c.YawSpread_deg * Units.DegreesToRadians,
            PitchMin = c.PitchMin_deg * Units.DegreesToRadians,
            PitchMax = c.PitchMax_deg * Units.DegreesToRadians,
        }).ToList(),
    };

    public static RangeLayout ToRange(RangeDef d, string file, SurfaceRegistry surfaces)
    {
        var errors = new Validator(file);

        SurfaceId Resolve(Validator scope, string property, string name)
        {
            if (surfaces.TryGet(name, out SurfaceId id))
            {
                return id;
            }

            scope.Error(property, $"unknown surface '{name}' (known: {string.Join(", ", surfaces.Names)})");
            return default;
        }

        SurfaceId ground = Resolve(errors.Scope(nameof(RangeDef.Ground)), nameof(GroundDef.Surface), d.Ground.Surface);

        var kinds = new Dictionary<string, TargetKindSpec>(StringComparer.Ordinal);
        for (int i = 0; i < d.TargetKinds.Length; i++)
        {
            TargetKindDef k = d.TargetKinds[i];
            kinds[k.Id] = new TargetKindSpec
            {
                Id = k.Id,
                Surface = Resolve(errors.Item(nameof(RangeDef.TargetKinds), i), nameof(TargetKindDef.Surface), k.Surface),
                Parts = k.Parts.Select(p => new TargetPartSpec
                {
                    Part = p.Part,
                    Kind = p.Shape,
                    From = Validator.ToVector3(p.From_m),
                    To = Validator.ToVector3(p.To_m),
                    Center = Validator.ToVector3(p.Center_m),
                    Radius = p.Radius_m,
                    Size = Validator.ToVector3(p.Size_m),
                }).ToList(),
            };
        }

        var targets = new List<TargetSpec>();
        foreach (TargetDef t in d.Targets)
        {
            if (!kinds.TryGetValue(t.Kind, out TargetKindSpec? kind))
            {
                continue; // already reported by RangeDef.Validate
            }

            targets.Add(new TargetSpec
            {
                Id = t.Id,
                Label = t.Label ?? t.Id,
                Kind = kind,
                BasePosition = Validator.ToVector3(t.Position_m),
                Yaw = t.Yaw_deg * Units.DegreesToRadians,
                Motion = t.Motion is null ? null : new TargetMotion
                {
                    Axis = VectorMath.NormalizeOr(Validator.ToVector3(t.Motion.Axis), Vector3.UnitX),
                    Amplitude = t.Motion.Amplitude_m,
                    Speed = t.Motion.Speed_mps,
                },
            });
        }

        var props = new List<PropSpec>();
        for (int i = 0; i < d.Props.Length; i++)
        {
            PropDef p = d.Props[i];
            Vector3 size = p.Shape == PropShapeKind.Cylinder
                ? new Vector3(p.Radius_m * 2f, p.Height_m, p.Radius_m * 2f)
                : Validator.ToVector3(p.Size_m);
            props.Add(new PropSpec
            {
                Id = p.Id,
                Kind = p.Shape,
                BasePosition = Validator.ToVector3(p.Position_m),
                Yaw = p.Yaw_deg * Units.DegreesToRadians,
                Size = size,
                SurfaceName = p.Surface,
                Surface = Resolve(errors.Item(nameof(RangeDef.Props), i), nameof(PropDef.Surface), p.Surface),
                Color = p.Color,
            });
        }

        errors.ThrowIfErrors();

        return new RangeLayout
        {
            Id = d.Id,
            Width = d.Ground.Width_m,
            Length = d.Ground.Length_m,
            BackMargin = d.Ground.BackMargin_m,
            GroundSurface = ground,
            GroundSurfaceName = d.Ground.Surface,
            BoundaryHeight = d.Boundary.Height_m,
            BoundaryMargin = d.Boundary.Margin_m,
            SpawnPosition = Validator.ToVector3(d.Spawn.Position_m),
            SpawnYaw = d.Spawn.Yaw_deg * Units.DegreesToRadians,
            MarkerSpacing = d.DistanceMarkers.Spacing_m,
            MarkerMax = d.DistanceMarkers.Max_m,
            MarkerLabels = d.DistanceMarkers.Labels_m ?? Enumerable.Range(1, (int)(d.DistanceMarkers.Max_m / d.DistanceMarkers.Spacing_m))
                .Select(i => i * d.DistanceMarkers.Spacing_m).ToArray(),
            Props = props,
            Targets = targets,
            Viewpoints = (d.Viewpoints ?? Array.Empty<ViewpointDef>())
                .Select(vp => new Viewpoint(vp.Name, Validator.ToVector3(vp.Position_m), vp.Yaw_deg * Units.DegreesToRadians, vp.Pitch_deg * Units.DegreesToRadians))
                .ToArray(),
        };
    }
}
