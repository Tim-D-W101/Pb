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

    public int Landmarks { get; set; }

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
        v.InRange(nameof(Landmarks), Landmarks, 0, 32);
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

    public float ScanHoldMin_s { get; set; }

    public float ScanHoldMax_s { get; set; }

    public float LookTurnSpeed_degps { get; set; }

    public float HeadTurnSpeed_degps { get; set; }

    public float GlanceEveryMin_s { get; set; }

    public float GlanceEveryMax_s { get; set; }

    public float GlanceAngleMin_deg { get; set; }

    public float GlanceAngleMax_deg { get; set; }

    public float GlanceHoldMin_s { get; set; }

    public float GlanceHoldMax_s { get; set; }

    public float RefillBelow { get; set; }

    public float TeammateClearance_m { get; set; }

    public float AimSettleTime_s { get; set; }

    public float AimTolerance_deg { get; set; }

    public float WalkOffTime_s { get; set; }

    public float StrollPace { get; set; }

    public float CalloutCooldown_s { get; set; }

    public int HuntChoices { get; set; }

    public float HuntLookAround_s { get; set; }

    /// <summary>A bot that spots someone shouts it: teammates within this (halved through walls) get where they are.</summary>
    public float CalloutRange_m { get; set; }

    /// <summary>... give or take this much, either way.</summary>
    public float ContactError_m { get; set; }

    /// <summary>While it keeps them in sight it calls their position again this often.</summary>
    public float ShareInterval_s { get; set; }

    /// <summary>A call is worth acting on for this long, even if they've heard something of the enemy themselves since.</summary>
    public float ContactMemory_s { get; set; }

    /// <summary>Marksmen moving on go at least this far from the spot they leave.</summary>
    public float RelocateDistance_m { get; set; }

    /// <summary>A flanking spot is at least this far off the line between the enemy and the teammate who called them.</summary>
    public float FlankMinAngle_deg { get; set; }

    /// <summary>An enemy facing within this of a flanker holding its fire has noticed it.</summary>
    public float NoticedAngle_deg { get; set; }

    /// <summary>A flanker at its spot looks out from it this long before it starts searching.</summary>
    public float FlankLook_s { get; set; }

    /// <summary>Objectives: a bot escorting the case's carrier keeps within this of them.</summary>
    public float Escort_m { get; set; }

    /// <summary>Defenders guarding one spot stand this far apart.</summary>
    public float GuardSpacing_m { get; set; }

    /// <summary>A way out is guarded from this far inside it, towards the case.</summary>
    public float ExitGuardInset_m { get; set; }

    /// <summary>Vantage scoring: rays this many and this long across this arc of the side the cover faces, plus this per metre up.</summary>
    public int VantageRays { get; set; }

    public float VantageRange_m { get; set; }

    public float VantageArc_deg { get; set; }

    public float VantageHeightBonus_perM { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(CalloutRange_m), CalloutRange_m, 0, 200);
        v.InRange(nameof(ContactError_m), ContactError_m, 0, 20);
        v.InRange(nameof(ShareInterval_s), ShareInterval_s, 0.2, 60);
        v.InRange(nameof(ContactMemory_s), ContactMemory_s, 0.1, 60);
        v.InRange(nameof(RelocateDistance_m), RelocateDistance_m, 0, 100);
        v.InRange(nameof(FlankMinAngle_deg), FlankMinAngle_deg, 0, 150);
        v.InRange(nameof(NoticedAngle_deg), NoticedAngle_deg, 0, 180);
        v.InRange(nameof(FlankLook_s), FlankLook_s, 0, 30);
        v.InRange(nameof(Escort_m), Escort_m, 1, 50);
        v.InRange(nameof(GuardSpacing_m), GuardSpacing_m, 0.5, 20);
        v.InRange(nameof(ExitGuardInset_m), ExitGuardInset_m, 0, 50);
        v.InRange(nameof(VantageRays), VantageRays, 1, 64);
        v.InRange(nameof(VantageRange_m), VantageRange_m, 5, 300);
        v.InRange(nameof(VantageArc_deg), VantageArc_deg, 10, 360);
        v.InRange(nameof(VantageHeightBonus_perM), VantageHeightBonus_perM, 0, 1);
        v.InRange(nameof(HuntChoices), HuntChoices, 1, 8);
        v.InRange(nameof(HuntLookAround_s), HuntLookAround_s, 0, 30);
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
        v.InRange(nameof(ScanHoldMin_s), ScanHoldMin_s, 0.1, 30);
        v.InRange(nameof(ScanHoldMax_s), ScanHoldMax_s, ScanHoldMin_s, 60);
        v.InRange(nameof(LookTurnSpeed_degps), LookTurnSpeed_degps, 10, 3600);
        v.InRange(nameof(HeadTurnSpeed_degps), HeadTurnSpeed_degps, 10, 3600);
        v.InRange(nameof(GlanceEveryMin_s), GlanceEveryMin_s, 0.2, 120);
        v.InRange(nameof(GlanceEveryMax_s), GlanceEveryMax_s, GlanceEveryMin_s, 240);
        v.InRange(nameof(GlanceAngleMin_deg), GlanceAngleMin_deg, 0, 90);
        v.InRange(nameof(GlanceAngleMax_deg), GlanceAngleMax_deg, GlanceAngleMin_deg, 90);
        v.InRange(nameof(GlanceHoldMin_s), GlanceHoldMin_s, 0.05, 10);
        v.InRange(nameof(GlanceHoldMax_s), GlanceHoldMax_s, GlanceHoldMin_s, 20);
        v.InRange(nameof(RefillBelow), RefillBelow, 0, 1);
        v.InRange(nameof(TeammateClearance_m), TeammateClearance_m, 0, 5);
        v.InRange(nameof(AimSettleTime_s), AimSettleTime_s, 0.01, 30);
        v.InRange(nameof(AimTolerance_deg), AimTolerance_deg, 0.05, 45);
        v.InRange(nameof(WalkOffTime_s), WalkOffTime_s, 1, 600);
        v.InRange(nameof(StrollPace), StrollPace, 0.2, 1);
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

    /// <summary>Take the best vantage (a high, open view) within reach of the start and watch from it (marksmen).</summary>
    Overwatch,
}

public enum BotGait
{
    Walk,
    Run,

    /// <summary>A calm walk, slower than <see cref="Walk"/>: patrols, going back to a post, walking off.</summary>
    Stroll,
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

    /// <summary>How much a high, open view counts when picking cover (0: not at all).</summary>
    [Optional]
    public float Vantage { get; set; }

    /// <summary>Overwatch: how far from the start it goes for a vantage.</summary>
    [Optional]
    public float OverwatchReach_m { get; set; } = 30f;

    /// <summary>Beyond close range, it only fires once its aim has settled on a still target.</summary>
    [Optional]
    public bool SteadyShots { get; set; }

    /// <summary>Within this it fights like anyone else (snap shots, no relocating).</summary>
    [Optional]
    public float CloseRange_m { get; set; } = 10f;

    /// <summary>Scales the tier's time between trigger pulls (careful shooters above 1).</summary>
    [Optional]
    public float PullScale { get; set; } = 1f;

    /// <summary>After this many shots from one spot it moves to another (0: never), or when balls start landing near it.</summary>
    [Optional]
    public int RelocateAfterShots { get; set; }

    [Optional]
    public bool RelocateWhenShotAt { get; set; }

    /// <summary>A teammate's contact sends it round to the side instead of straight at the lead.</summary>
    [Optional]
    public bool FlankOnContact { get; set; }

    /// <summary>Flanking, it holds fire until it's in position unless the enemy turns its way or comes close.</summary>
    [Optional]
    public bool HoldFireWhileFlanking { get; set; }

    /// <summary>It crouch-walks the last this-many metres to a flanking spot.</summary>
    [Optional]
    public float StealthWithin_m { get; set; }

    public void Validate(Validator v)
    {
        v.NotEmpty(nameof(Id), Id);
        v.InRange(nameof(Vantage), Vantage, 0, 100);
        v.InRange(nameof(OverwatchReach_m), OverwatchReach_m, 1, 300);
        v.InRange(nameof(CloseRange_m), CloseRange_m, 0, 100);
        v.InRange(nameof(PullScale), PullScale, 0.2, 10);
        v.InRange(nameof(RelocateAfterShots), RelocateAfterShots, 0, 1000);
        v.InRange(nameof(StealthWithin_m), StealthWithin_m, 0, 100);
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
