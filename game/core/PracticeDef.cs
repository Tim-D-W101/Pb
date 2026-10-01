using Pb.Sim.Data;

namespace Pb.Game.Core;

/// <summary>Practice opponents (bots/practice.jsonc), until the bots of M2.5 replace them.</summary>
public sealed class PracticeDef : IValidatable
{
    public const string File = "bots/practice.jsonc";

    public int Count { get; set; }

    public float SightRange_m { get; set; }

    public float TurnSpeed_degps { get; set; }

    public float ReactionTime_s { get; set; }

    public float PullInterval_s { get; set; }

    public float AimError_deg { get; set; }

    public float WalkOffTime_s { get; set; }

    public void Validate(Validator v)
    {
        v.InRange(nameof(Count), Count, 0, 9);
        v.InRange(nameof(SightRange_m), SightRange_m, 1, 200);
        v.InRange(nameof(TurnSpeed_degps), TurnSpeed_degps, 1, 2000);
        v.InRange(nameof(ReactionTime_s), ReactionTime_s, 0, 10);
        v.InRange(nameof(PullInterval_s), PullInterval_s, 0.05, 10);
        v.InRange(nameof(AimError_deg), AimError_deg, 0, 30);
        v.InRange(nameof(WalkOffTime_s), WalkOffTime_s, 0.5, 60);
    }
}
