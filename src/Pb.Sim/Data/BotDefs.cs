namespace Pb.Sim.Data;

/// <summary>How bots find their way (bots/navigation.jsonc).</summary>
public sealed class NavigationDef : IValidatable
{
    public float CellSize_m { get; set; }

    public float AgentRadius_m { get; set; }

    public float AgentHeight_m { get; set; }

    public float StepHeight_m { get; set; }

    public float WaypointRadius_m { get; set; }

    public float StuckTime_s { get; set; }

    public int SearchesPerTick { get; set; }

    public int MaxSearchNodes { get; set; }

    public float HeuristicWeight { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(CellSize_m), CellSize_m, 0.1, 1);
        v.InRange(nameof(AgentRadius_m), AgentRadius_m, 0.05, 1);
        v.InRange(nameof(AgentHeight_m), AgentHeight_m, 0.8, 3);
        v.InRange(nameof(StepHeight_m), StepHeight_m, 0.05, 1);
        v.InRange(nameof(WaypointRadius_m), WaypointRadius_m, 0.1, 3);
        v.InRange(nameof(StuckTime_s), StuckTime_s, 0.1, 10);
        v.InRange(nameof(SearchesPerTick), SearchesPerTick, 1, 64);
        v.InRange(nameof(MaxSearchNodes), MaxSearchNodes, 1000, 10_000_000);
        v.InRange(nameof(HeuristicWeight), HeuristicWeight, 1, 5);
    }
}

/// <summary>How every bot thinks and acts (bots/brain.jsonc).</summary>
public sealed class BrainDef : IValidatable
{
    public float SuspiciousTime_s { get; set; }

    public float CrouchNearLead_m { get; set; }

    public float OpenFireTime_s { get; set; }

    public float OpenFireMinDistance_m { get; set; }

    public float CoverSearchRadius_m { get; set; }

    public float MinThreatDistance_m { get; set; }

    public float PeekGiveUp_s { get; set; }

    public float LostSightTime_s { get; set; }

    public float GiveUpTime_s { get; set; }

    public float SearchRadius_m { get; set; }

    public int SearchHops { get; set; }

    public float ScanAngle_deg { get; set; }

    public float ScanPeriod_s { get; set; }

    public float LookTurnSpeed_degps { get; set; }

    public float RefillBelow { get; set; }

    public float TeammateClearance_m { get; set; }

    public float AimSettleTime_s { get; set; }

    public float AimTolerance_deg { get; set; }

    public float WalkOffTime_s { get; set; }

    public float CalloutCooldown_s { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(SuspiciousTime_s), SuspiciousTime_s, 0, 30);
        v.InRange(nameof(CalloutCooldown_s), CalloutCooldown_s, 0, 120);
        v.InRange(nameof(CrouchNearLead_m), CrouchNearLead_m, 0, 100);
        v.InRange(nameof(OpenFireTime_s), OpenFireTime_s, 0, 30);
        v.InRange(nameof(OpenFireMinDistance_m), OpenFireMinDistance_m, 0, 100);
        v.InRange(nameof(CoverSearchRadius_m), CoverSearchRadius_m, 2, 100);
        v.InRange(nameof(MinThreatDistance_m), MinThreatDistance_m, 0, 50);
        v.InRange(nameof(PeekGiveUp_s), PeekGiveUp_s, 0.1, 30);
        v.InRange(nameof(LostSightTime_s), LostSightTime_s, 0.1, 60);
        v.InRange(nameof(GiveUpTime_s), GiveUpTime_s, LostSightTime_s, 600);
        v.InRange(nameof(SearchRadius_m), SearchRadius_m, 0, 100);
        v.InRange(nameof(SearchHops), SearchHops, 0, 20);
        v.InRange(nameof(ScanAngle_deg), ScanAngle_deg, 0, 180);
        v.InRange(nameof(ScanPeriod_s), ScanPeriod_s, 0.5, 120);
        v.InRange(nameof(LookTurnSpeed_degps), LookTurnSpeed_degps, 10, 3600);
        v.InRange(nameof(RefillBelow), RefillBelow, 0, 1);
        v.InRange(nameof(TeammateClearance_m), TeammateClearance_m, 0, 5);
        v.InRange(nameof(AimSettleTime_s), AimSettleTime_s, 0.01, 30);
        v.InRange(nameof(AimTolerance_deg), AimTolerance_deg, 0.05, 45);
        v.InRange(nameof(WalkOffTime_s), WalkOffTime_s, 1, 600);
    }
}

/// <summary>What bots notice (bots/senses.jsonc).</summary>
public sealed class SensesDef : IValidatable
{
    public float FieldOfView_deg { get; set; }

    public float FocusAngle_deg { get; set; }

    public float PeripheralRate { get; set; }

    public float FillRate { get; set; }

    public float MinDistanceFactor { get; set; }

    public float DarkFactor { get; set; }

    public float CrouchFactor { get; set; }

    public float MovingFactorPerMps { get; set; }

    public float PartialFactor { get; set; }

    public float DecayRate { get; set; }

    public float SuspiciousAt { get; set; }

    public float ShotHearing_m { get; set; }

    public float BreakHearing_m { get; set; }

    public float WallMuffle { get; set; }

    public float Memory_s { get; set; }

    public float ReacquireFactor { get; set; }

    public float ReacquireWithin_s { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(FieldOfView_deg), FieldOfView_deg, 30, 360);
        v.InRange(nameof(FocusAngle_deg), FocusAngle_deg, 1, FieldOfView_deg);
        v.InRange(nameof(PeripheralRate), PeripheralRate, 0, 1);
        v.InRange(nameof(FillRate), FillRate, 0.01, 100);
        v.InRange(nameof(MinDistanceFactor), MinDistanceFactor, 0, 1);
        v.InRange(nameof(DarkFactor), DarkFactor, 0, 1);
        v.InRange(nameof(CrouchFactor), CrouchFactor, 0, 1);
        v.InRange(nameof(MovingFactorPerMps), MovingFactorPerMps, 0, 2);
        v.InRange(nameof(PartialFactor), PartialFactor, 0, 1);
        v.InRange(nameof(DecayRate), DecayRate, 0, 10);
        v.InRange(nameof(SuspiciousAt), SuspiciousAt, 0.01, 0.99);
        v.InRange(nameof(ShotHearing_m), ShotHearing_m, 0, 500);
        v.InRange(nameof(BreakHearing_m), BreakHearing_m, 0, 100);
        v.InRange(nameof(WallMuffle), WallMuffle, 0, 1);
        v.InRange(nameof(Memory_s), Memory_s, 1, 600);
        v.InRange(nameof(ReacquireFactor), ReacquireFactor, 0, 1);
        v.InRange(nameof(ReacquireWithin_s), ReacquireWithin_s, 0, 600);
    }
}

public enum BotIdle
{
    /// <summary>Hold the spawn and scan.</summary>
    Post,

    /// <summary>Walk the spawn's patrol route (a spawn without one holds its post).</summary>
    Patrol,

    /// <summary>Sweep the level's opponent spawns one by one, then start again (the bot in your slot in the headless match).</summary>
    Hunt,
}

public enum BotGait
{
    Walk,
    Run,
}

/// <summary>One bot behaviour (bots/archetypes.jsonc).</summary>
public sealed class ArchetypeDef : IValidatable
{
    public string Id { get; set; } = "";

    public BotIdle Idle { get; set; }

    public float Leash_m { get; set; }

    public bool UseCover { get; set; }

    public float PeekTime_s { get; set; }

    public float HideTime_s { get; set; }

    public float FlankChance { get; set; }

    public float PushChance { get; set; }

    public bool FireOnMove { get; set; }

    public float EngageRange_m { get; set; }

    public float SearchTime_s { get; set; }

    public BotGait MoveGait { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.InRange(nameof(Leash_m), Leash_m, 0, 500);
        v.InRange(nameof(PeekTime_s), PeekTime_s, 0.1, 30);
        v.InRange(nameof(HideTime_s), HideTime_s, 0.1, 30);
        v.InRange(nameof(FlankChance), FlankChance, 0, 1);
        v.InRange(nameof(PushChance), PushChance, 0, 1);
        v.InRange(nameof(EngageRange_m), EngageRange_m, 2, 100);
        v.InRange(nameof(SearchTime_s), SearchTime_s, 0, 300);
    }
}

public sealed class ArchetypesDef : IValidatable
{
    public ArchetypeDef[] Archetypes { get; set; } = Array.Empty<ArchetypeDef>();

    public void Validate(Validator v)
    {
        if (Archetypes.Length == 0)
        {
            v.Error(nameof(Archetypes), "needs at least one behaviour");
        }

        LevelDefChecks.UniqueIds(v, nameof(Archetypes), Archetypes, a => a.Id);
    }
}

/// <summary>One difficulty tier (bots/difficulty.jsonc).</summary>
public sealed class DifficultyDef : IValidatable
{
    public string Id { get; set; } = "";

    public float ReactionTime_s { get; set; }

    public float AimError_deg { get; set; }

    public float TrackingLag_s { get; set; }

    public float DecisionInterval_s { get; set; }

    public float SightRange_m { get; set; }

    public float DetectionScale { get; set; }

    public float HearingScale { get; set; }

    public float TurnSpeed_degps { get; set; }

    public float PullInterval_s { get; set; }

    public float Aggression { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.InRange(nameof(ReactionTime_s), ReactionTime_s, 0, 10);
        v.InRange(nameof(AimError_deg), AimError_deg, 0, 45);
        v.InRange(nameof(TrackingLag_s), TrackingLag_s, 0.01, 10);
        v.InRange(nameof(DecisionInterval_s), DecisionInterval_s, 0.02, 10);
        v.InRange(nameof(SightRange_m), SightRange_m, 1, 500);
        v.InRange(nameof(DetectionScale), DetectionScale, 0.01, 10);
        v.InRange(nameof(HearingScale), HearingScale, 0, 10);
        v.InRange(nameof(TurnSpeed_degps), TurnSpeed_degps, 10, 3600);
        v.InRange(nameof(PullInterval_s), PullInterval_s, 0.05, 10);
        v.InRange(nameof(Aggression), Aggression, 0, 1);
    }
}

public sealed class DifficultyTiersDef : IValidatable
{
    public DifficultyDef[] Tiers { get; set; } = Array.Empty<DifficultyDef>();

    public void Validate(Validator v)
    {
        if (Tiers.Length == 0)
        {
            v.Error(nameof(Tiers), "needs at least one tier");
        }

        LevelDefChecks.UniqueIds(v, nameof(Tiers), Tiers, t => t.Id);
    }
}
