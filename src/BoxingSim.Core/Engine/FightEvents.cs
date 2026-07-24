using BoxingSim.Core.Model;

namespace BoxingSim.Core.Engine;

/// <summary>A 15-second snapshot of the action within a round (12 per full round).</summary>
public sealed class FightTick
{
    public string Clock = "";                 // round clock remaining, e.g. "2:45"
    public int LandedA, LandedB;              // cumulative clean punches landed this round
    public int KnockdownsA, KnockdownsB;      // cumulative knockdowns suffered this round
    public double DamageA, DamageB;           // head damage 0..1.3
    public double BodyA, BodyB;               // body damage 0..1.3
    public double CutA, CutB, SwellA, SwellB;
    public bool BigA, BigB;                   // landed a clean power shot this segment
    public int RockA, RockB;                  // 0 = fine, 1 = hurt, 2 = badly wobbled
    public string? PunchA, PunchB;            // last power-shot type landed this segment
    public bool BodyShotA, BodyShotB;         // last power shot was to the body
    public bool DownBodyA, DownBodyB;         // a knockdown this segment was to the body
    public bool HandA, HandB;                 // a fighter hurt his hand this segment
    public bool StaggerA, StaggerB;           // a fighter was wobbled and got jumped on
    public bool CounterA, CounterB;           // a fighter landed a counter this segment
    public FoulEvent? Foul;
    public StopInfo? Fin;                     // set on the tick the fight ends
}

public sealed class FoulEvent
{
    public int Who;            // 0 = A, 1 = B
    public string Type = "";
    public bool Butt, Deduct, Dq;
}

public sealed class StopInfo
{
    public string Method = "";   // "KO", "TKO", "cut", "DQ"
    public int Winner;           // 0 = A, 1 = B
    public bool Body;            // stoppage came from body work
    public FoulEvent? Foul;
}

/// <summary>One completed round: the ticks, the base score, the three judges' cards.</summary>
public sealed class RoundResult
{
    public int Round { get; init; }
    public int LandedA { get; init; }
    public int LandedB { get; init; }
    public int KnockdownsA { get; init; }
    public int KnockdownsB { get; init; }
    public int ScoreA { get; init; }              // base 10-point-must (with deductions)
    public int ScoreB { get; init; }
    public IReadOnlyList<FightTick> Ticks { get; init; } = Array.Empty<FightTick>();
    public (int A, int B)[]? Judges { get; init; } // three judges; null on a stoppage round
    public double CutA { get; init; }
    public double CutB { get; init; }
    public bool CutNewA { get; init; }
    public bool CutNewB { get; init; }
    public bool CutWorseA { get; init; }   // an existing cut opened up further this round
    public bool CutWorseB { get; init; }
    public bool CutClosedA { get; init; }  // the cutman got a cut back under control between rounds
    public bool CutClosedB { get; init; }
    public string? CutLocA { get; init; }
    public string? CutLocB { get; init; }
    public bool SwellNewA { get; init; }
    public bool SwellNewB { get; init; }
    public string? SwellLocA { get; init; }
    public string? SwellLocB { get; init; }
    public bool SwellEyeA { get; init; }   // the swelling is closing an eye
    public bool SwellEyeB { get; init; }
    public bool HurtEndA { get; init; }
    public bool HurtEndB { get; init; }
}

/// <summary>An acute injury sustained in the fight, with a minimum layoff (career-sim hook).</summary>
public sealed class Injury
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public string Severity { get; init; } = "";
    public int LayoffDays { get; init; }
    public bool Retires { get; init; }
}

/// <summary>A permanent attribute change a fighter carries forward (career-sim hook).</summary>
public sealed class LastingEffect
{
    public required string Name { get; init; }
    public required string Note { get; init; }
    public required string Attr { get; init; }   // "Chin", "Power", "CutResistance"
    public int Delta { get; init; }
}
