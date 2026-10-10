using Pb.Sim.Core;
using Pb.Sim.Data;

namespace Pb.Sim.AI;

/// <summary>Navigation grid and path following (SI), from bots/navigation.jsonc.</summary>
public sealed class NavParams
{
    public required float CellSize { get; init; }

    public required float AgentRadius { get; init; }

    public required float AgentHeight { get; init; }

    public required float StepHeight { get; init; }

    public required float WaypointRadius { get; init; }

    public required float StuckTime { get; init; }

    public required int SearchesPerTick { get; init; }

    public required int MaxSearchNodes { get; init; }

    public required float HeuristicWeight { get; init; }

    public required int Landmarks { get; init; }

    /// <summary>A search counts each metre climbed on a ladder as this much walking.</summary>
    public float LadderCostPerMetre { get; init; } = 3.5f;
}

/// <summary>How every bot thinks and acts (SI, angles in radians), from bots/brain.jsonc.</summary>
public sealed class BrainParams
{
    public required float SuspiciousTime { get; init; }

    public required float CrouchNearLead { get; init; }

    public required float OpenFireTime { get; init; }

    public required float OpenFireMinDistance { get; init; }

    public required float CoverSearchRadius { get; init; }

    public required float MinThreatDistance { get; init; }

    public required float PeekGiveUp { get; init; }

    public required float LostSightTime { get; init; }

    public required float GiveUpTime { get; init; }

    public required float SearchRadius { get; init; }

    public required int SearchHops { get; init; }

    public required float ScanAngle { get; init; }

    public required float ScanPeriod { get; init; }

    /// <summary>Sweeping a post, each look is held between these (s).</summary>
    public required float ScanHoldMin { get; init; }

    public required float ScanHoldMax { get; init; }

    public required float LookTurnSpeed { get; init; }

    /// <summary>The head turns this fast (rad/s), ahead of the body.</summary>
    public required float HeadTurnSpeed { get; init; }

    /// <summary>With nothing going on, a glance round every so often (s), so far off the aim (rad), held so long (s).</summary>
    public required float GlanceEveryMin { get; init; }

    public required float GlanceEveryMax { get; init; }

    public required float GlanceAngleMin { get; init; }

    public required float GlanceAngleMax { get; init; }

    public required float GlanceHoldMin { get; init; }

    public required float GlanceHoldMax { get; init; }

    public required float RefillBelow { get; init; }

    public required float TeammateClearance { get; init; }

    public required float AimSettleTime { get; init; }

    public required float AimTolerance { get; init; }

    public required float WalkOffTime { get; init; }

    /// <summary>A stroll's share of the walking speed (patrols, going back to a post, walking off).</summary>
    public required float StrollPace { get; init; }

    public required float CalloutCooldown { get; init; }

    /// <summary>Hunting, the next spot is picked at random from this many nearest unvisited ones.</summary>
    public required int HuntChoices { get; init; }

    /// <summary>Hunting, how long they look around at each spot.</summary>
    public required float HuntLookAround { get; init; }

    /// <summary>How far a contact shout carries to teammates (halved through walls).</summary>
    public required float CalloutRange { get; init; }

    public required float ContactError { get; init; }

    public required float ShareInterval { get; init; }

    /// <summary>How long a teammate's call is worth acting on.</summary>
    public required float ContactMemory { get; init; }

    /// <summary>Relocating means a spot at least this far from the one it leaves.</summary>
    public required float RelocateDistance { get; init; }

    /// <summary>A flanking spot is at least this far off the line between the enemy and the teammate who called them (rad).</summary>
    public required float FlankMinAngle { get; init; }

    /// <summary>An enemy facing within this of a flanker has noticed it (rad).</summary>
    public required float NoticedAngle { get; init; }

    /// <summary>A flanker at its spot looks out from it this long before it starts searching.</summary>
    public required float FlankLook { get; init; }

    /// <summary>Objectives: an escort keeps within this of the case's carrier.</summary>
    public required float Escort { get; init; }

    public required float GuardSpacing { get; init; }

    public required float ExitGuardInset { get; init; }

    public required int VantageRays { get; init; }

    public required float VantageRange { get; init; }

    /// <summary>The arc vantage rays cover (rad).</summary>
    public required float VantageArc { get; init; }

    public required float VantageHeightBonus { get; init; }

    /// <summary>Speedball: the back and middle shares of a side, the longest breakout run (s), the pull of a bot's own bunker
    /// when it picks cover, the lead in players that moves someone up and how often (s), and how often the buzzers are checked (s).</summary>
    public required SpeedballBrain Speedball { get; init; }

    /// <summary>Capture the flag: the share of a side guarding its flag, the chasers sent after a carrier and how often (s),
    /// and how near a dropped flag draws any bot (m).</summary>
    public required FlagBrain Flag { get; init; }
}

public sealed record SpeedballBrain(float BackShare, float MidShare, float BreakoutFor, float BunkerBias, int AdvanceMargin, float AdvanceEvery,
    float HangCheck);

public sealed record FlagBrain(float DefendShare, int Chasers, float AlarmInterval, float NearDropped, float PressOnAfter, float PressFor);

/// <summary>Sight, hearing and memory (SI, angles in radians), from bots/senses.jsonc.</summary>
public sealed class SenseParams
{
    /// <summary>Half of the field of view.</summary>
    public required float HalfFieldOfView { get; init; }

    /// <summary>Half of the focus angle.</summary>
    public required float HalfFocusAngle { get; init; }

    public required float PeripheralRate { get; init; }

    public required float FillRate { get; init; }

    public required float MinDistanceFactor { get; init; }

    public required float DarkFactor { get; init; }

    public required float CrouchFactor { get; init; }

    public required float MovingFactorPerMps { get; init; }

    public required float PartialFactor { get; init; }

    public required float DecayRate { get; init; }

    public required float SuspiciousAt { get; init; }

    public required float ShotHearing { get; init; }

    public required float BreakHearing { get; init; }

    public required float WallMuffle { get; init; }

    public required float Memory { get; init; }

    public required float ReacquireFactor { get; init; }

    public required float ReacquireWithin { get; init; }
}

/// <summary>One behaviour (SI), from bots/archetypes.jsonc.</summary>
public sealed class ArchetypeParams
{
    public required string Id { get; init; }

    public required BotIdle Idle { get; init; }

    public required float Leash { get; init; }

    public required bool UseCover { get; init; }

    public required float PeekTime { get; init; }

    public required float HideTime { get; init; }

    public required float FlankChance { get; init; }

    public required float PushChance { get; init; }

    public required bool FireOnMove { get; init; }

    public required float EngageRange { get; init; }

    public required float SearchTime { get; init; }

    public required BotGait MoveGait { get; init; }

    public float Vantage { get; init; }

    public float OverwatchReach { get; init; } = 30f;

    public bool SteadyShots { get; init; }

    public float CloseRange { get; init; } = 10f;

    public float PullScale { get; init; } = 1f;

    public int RelocateAfterShots { get; init; }

    public bool RelocateWhenShotAt { get; init; }

    public bool FlankOnContact { get; init; }

    public bool HoldFireWhileFlanking { get; init; }

    public float StealthWithin { get; init; }
}

/// <summary>One difficulty tier (SI, angles in radians), from bots/difficulty.jsonc.</summary>
public sealed class DifficultyParams
{
    public required string Id { get; init; }

    public required float ReactionTime { get; init; }

    public required float AimError { get; init; }

    public required float TrackingLag { get; init; }

    public required float DecisionInterval { get; init; }

    public required float SightRange { get; init; }

    public required float DetectionScale { get; init; }

    public required float HearingScale { get; init; }

    public required float TurnSpeed { get; init; }

    public required float PullInterval { get; init; }

    public required float Aggression { get; init; }
}

/// <summary>Everything bots need from the data files.</summary>
public sealed class BotConfig
{
    public required NavParams Navigation { get; init; }

    public required BrainParams Brain { get; init; }

    public required SenseParams Senses { get; init; }

    public required IReadOnlyDictionary<string, ArchetypeParams> Archetypes { get; init; }

    public required IReadOnlyDictionary<string, DifficultyParams> Difficulty { get; init; }

    /// <summary>The behaviour for a spawn: its first role that names one, else null.</summary>
    public ArchetypeParams? ArchetypeFor(IReadOnlyList<string> roles)
    {
        foreach (string role in roles)
        {
            if (Archetypes.TryGetValue(role, out ArchetypeParams? archetype))
            {
                return archetype;
            }
        }

        return null;
    }

    public static BotConfig From(NavigationDef nav, BrainDef brain, SensesDef senses, ArchetypesDef archetypes, DifficultyTiersDef difficulty) => new()
    {
        Brain = new BrainParams
        {
            SuspiciousTime = brain.SuspiciousTime_s,
            CrouchNearLead = brain.CrouchNearLead_m,
            OpenFireTime = brain.OpenFireTime_s,
            OpenFireMinDistance = brain.OpenFireMinDistance_m,
            CoverSearchRadius = brain.CoverSearchRadius_m,
            MinThreatDistance = brain.MinThreatDistance_m,
            PeekGiveUp = brain.PeekGiveUp_s,
            LostSightTime = brain.LostSightTime_s,
            GiveUpTime = brain.GiveUpTime_s,
            SearchRadius = brain.SearchRadius_m,
            SearchHops = brain.SearchHops,
            ScanAngle = brain.ScanAngle_deg * Units.DegreesToRadians,
            ScanPeriod = brain.ScanPeriod_s,
            ScanHoldMin = brain.ScanHoldMin_s,
            ScanHoldMax = brain.ScanHoldMax_s,
            LookTurnSpeed = brain.LookTurnSpeed_degps * Units.DegreesToRadians,
            HeadTurnSpeed = brain.HeadTurnSpeed_degps * Units.DegreesToRadians,
            GlanceEveryMin = brain.GlanceEveryMin_s,
            GlanceEveryMax = brain.GlanceEveryMax_s,
            GlanceAngleMin = brain.GlanceAngleMin_deg * Units.DegreesToRadians,
            GlanceAngleMax = brain.GlanceAngleMax_deg * Units.DegreesToRadians,
            GlanceHoldMin = brain.GlanceHoldMin_s,
            GlanceHoldMax = brain.GlanceHoldMax_s,
            RefillBelow = brain.RefillBelow,
            TeammateClearance = brain.TeammateClearance_m,
            AimSettleTime = brain.AimSettleTime_s,
            AimTolerance = brain.AimTolerance_deg * Units.DegreesToRadians,
            WalkOffTime = brain.WalkOffTime_s,
            StrollPace = brain.StrollPace,
            CalloutCooldown = brain.CalloutCooldown_s,
            HuntChoices = brain.HuntChoices,
            HuntLookAround = brain.HuntLookAround_s,
            CalloutRange = brain.CalloutRange_m,
            ContactError = brain.ContactError_m,
            ShareInterval = brain.ShareInterval_s,
            ContactMemory = brain.ContactMemory_s,
            RelocateDistance = brain.RelocateDistance_m,
            FlankMinAngle = brain.FlankMinAngle_deg * Units.DegreesToRadians,
            NoticedAngle = brain.NoticedAngle_deg * Units.DegreesToRadians,
            FlankLook = brain.FlankLook_s,
            Escort = brain.Escort_m,
            GuardSpacing = brain.GuardSpacing_m,
            ExitGuardInset = brain.ExitGuardInset_m,
            VantageRays = brain.VantageRays,
            VantageRange = brain.VantageRange_m,
            VantageArc = brain.VantageArc_deg * Units.DegreesToRadians,
            VantageHeightBonus = brain.VantageHeightBonus_perM,
            Speedball = new SpeedballBrain(brain.Speedball.BackShare, brain.Speedball.MidShare, brain.Speedball.BreakoutFor_s, brain.Speedball.BunkerBias,
                brain.Speedball.AdvanceMargin, brain.Speedball.AdvanceEvery_s, brain.Speedball.HangCheck_s),
            Flag = new FlagBrain(brain.Flag.DefendShare, brain.Flag.Chasers, brain.Flag.AlarmInterval_s, brain.Flag.NearDropped_m,
                brain.Flag.PressOnAfter_s, brain.Flag.PressFor_s),
        },
        Navigation = new NavParams
        {
            CellSize = nav.CellSize_m,
            AgentRadius = nav.AgentRadius_m,
            AgentHeight = nav.AgentHeight_m,
            StepHeight = nav.StepHeight_m,
            WaypointRadius = nav.WaypointRadius_m,
            StuckTime = nav.StuckTime_s,
            SearchesPerTick = nav.SearchesPerTick,
            MaxSearchNodes = nav.MaxSearchNodes,
            HeuristicWeight = nav.HeuristicWeight,
            Landmarks = nav.Landmarks,
            LadderCostPerMetre = nav.LadderCostPerMetre,
        },
        Senses = new SenseParams
        {
            HalfFieldOfView = senses.FieldOfView_deg * 0.5f * Units.DegreesToRadians,
            HalfFocusAngle = senses.FocusAngle_deg * 0.5f * Units.DegreesToRadians,
            PeripheralRate = senses.PeripheralRate,
            FillRate = senses.FillRate,
            MinDistanceFactor = senses.MinDistanceFactor,
            DarkFactor = senses.DarkFactor,
            CrouchFactor = senses.CrouchFactor,
            MovingFactorPerMps = senses.MovingFactorPerMps,
            PartialFactor = senses.PartialFactor,
            DecayRate = senses.DecayRate,
            SuspiciousAt = senses.SuspiciousAt,
            ShotHearing = senses.ShotHearing_m,
            BreakHearing = senses.BreakHearing_m,
            WallMuffle = senses.WallMuffle,
            Memory = senses.Memory_s,
            ReacquireFactor = senses.ReacquireFactor,
            ReacquireWithin = senses.ReacquireWithin_s,
        },
        Archetypes = archetypes.Archetypes.ToDictionary(a => a.Id, a => new ArchetypeParams
        {
            Id = a.Id,
            Idle = a.Idle,
            Leash = a.Leash_m,
            UseCover = a.UseCover,
            PeekTime = a.PeekTime_s,
            HideTime = a.HideTime_s,
            FlankChance = a.FlankChance,
            PushChance = a.PushChance,
            FireOnMove = a.FireOnMove,
            EngageRange = a.EngageRange_m,
            SearchTime = a.SearchTime_s,
            MoveGait = a.MoveGait,
            Vantage = a.Vantage,
            OverwatchReach = a.OverwatchReach_m,
            SteadyShots = a.SteadyShots,
            CloseRange = a.CloseRange_m,
            PullScale = a.PullScale,
            RelocateAfterShots = a.RelocateAfterShots,
            RelocateWhenShotAt = a.RelocateWhenShotAt,
            FlankOnContact = a.FlankOnContact,
            HoldFireWhileFlanking = a.HoldFireWhileFlanking,
            StealthWithin = a.StealthWithin_m,
        }, StringComparer.Ordinal),
        Difficulty = difficulty.Tiers.ToDictionary(t => t.Id, t => new DifficultyParams
        {
            Id = t.Id,
            ReactionTime = t.ReactionTime_s,
            AimError = t.AimError_deg * Units.DegreesToRadians,
            TrackingLag = t.TrackingLag_s,
            DecisionInterval = t.DecisionInterval_s,
            SightRange = t.SightRange_m,
            DetectionScale = t.DetectionScale,
            HearingScale = t.HearingScale,
            TurnSpeed = t.TurnSpeed_degps * Units.DegreesToRadians,
            PullInterval = t.PullInterval_s,
            Aggression = t.Aggression,
        }, StringComparer.Ordinal),
    };
}
