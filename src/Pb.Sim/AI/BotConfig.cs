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

    public required float LookTurnSpeed { get; init; }

    public required float RefillBelow { get; init; }

    public required float TeammateClearance { get; init; }

    public required float AimSettleTime { get; init; }

    public required float AimTolerance { get; init; }

    public required float WalkOffTime { get; init; }

    public required float CalloutCooldown { get; init; }
}

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
            LookTurnSpeed = brain.LookTurnSpeed_degps * Units.DegreesToRadians,
            RefillBelow = brain.RefillBelow,
            TeammateClearance = brain.TeammateClearance_m,
            AimSettleTime = brain.AimSettleTime_s,
            AimTolerance = brain.AimTolerance_deg * Units.DegreesToRadians,
            WalkOffTime = brain.WalkOffTime_s,
            CalloutCooldown = brain.CalloutCooldown_s,
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
