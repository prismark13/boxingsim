using BoxingSim.Core.Generation;
using BoxingSim.Core.Model;

namespace BoxingSim.Core.Career;

/// <summary>The five stages of a fighter's career arc (then retirement).</summary>
public enum CareerStage { Starter, PrePrime, Prime, PostPrime, End }

/// <summary>One point on a fighter's career arc: what he was, and at what age.</summary>
public sealed record StagePoint(string Stage, int Fights, int Age, Ratings Ratings, bool IsNow)
{
    public int Overall => Ratings.Overall;
    public int Class => Ratings.Class;
}

public static class CareerStages
{
    /// <summary>Where a fighter sits on the career arc, measured in bouts. It used to be read off his age
    /// against a notional peak age, with the fight count only ever able to pull him further back - so two
    /// thirty-year-olds were the same fighter whether one had eighteen bouts behind him or seventy. A boxer is
    /// worn down by being hit, not by birthdays. See <see cref="CareerMileage"/>, where every boundary is in
    /// fights and varied per man so no two age on the same schedule.</summary>
    public static CareerStage Of(Boxer b) => CareerMileage.StageOf(b);

    /// <summary>Roughly how many bouts a fighter takes in a year — busy early, picky in the prime, sparse at the end.</summary>
    public static int FightsPerYear(CareerStage s) => s switch
    {
        CareerStage.Starter => 9,     // club-show grind — a fight every few weeks
        CareerStage.PrePrime => 5,
        CareerStage.Prime => 3,       // fewer, bigger nights
        CareerStage.PostPrime => 3,
        CareerStage.End => 2,
        _ => 2
    };

    public static string Label(CareerStage s) => s switch
    {
        CareerStage.Starter => "Starter",
        CareerStage.PrePrime => "Pre-prime",
        CareerStage.Prime => "Prime",
        CareerStage.PostPrime => "Post-prime",
        CareerStage.End => "End of career",
        _ => ""
    };
}

/// <summary>A bout offered to the player fighter — opponent, length and what's at stake.</summary>
public sealed class FightOffer
{
    public required Boxer Opponent { get; init; }
    public int Rounds { get; init; }
    public bool TitleFight { get; init; }
    public string? Belt { get; init; }            // the sanctioning body at stake (e.g. "WBA"), null for a non-title bout
    public string Context { get; init; } = "";   // "stay-busy", "step-up", "eliminator", "WORLD TITLE"
}

/// <summary>A spell as world champion: when it was won, when it was lost (null = still holds it), and defences.</summary>
public sealed class TitleReign
{
    public string Belt { get; set; } = "";
    public DateOnly Won { get; set; }
    public DateOnly? Lost { get; set; }
    public int Defenses { get; set; }
}

/// <summary>One line in the career timeline (debuts, title changes, retirements, the player's own bouts).</summary>
public sealed class CareerEvent
{
    public DateOnly On { get; init; }
    public required string Text { get; init; }
    public bool PlayerBout { get; init; }
    public string? Kind { get; init; }   // "title", "upset", "ko", "debut", "retire", "streak", "hof", "move" — for the news feed
    public WeightClass? Div { get; init; }   // which division this event belongs to (for the news filter)

    /// <summary>Who the men in this headline are, as they stood on the night: records and where each sat on the
    /// division board. A result without it is two names and a round number.</summary>
    public string? Detail { get; init; }
    public int Year => On.Year;
    public string DateLabel => On.ToString("d MMM yyyy");

    /// <summary>The bout this headline reports, when it reports one. A result in the feed is a fight that
    /// happened, and it should be openable rather than only readable.</summary>
    public BoutRef? Bout { get; init; }
}

/// <summary>One placing in an annual award (a fighter/bout with a one-line citation).</summary>
public sealed class AwardWinner
{
    public required string Name { get; init; }
    public required string Detail { get; init; }
    public WeightClass Div { get; init; }
    public string Commentary { get; init; } = "";   // a fuller sentence for the hover tooltip

    /// <summary>The fight this honour was given for, so it can be opened and watched rather than only read
    /// about. Null for Fighter of the Year, which is awarded for a whole season rather than one night - it
    /// points at the best win of that season instead.</summary>
    public BoutRef? Bout { get; init; }
}

/// <summary>Enough to find a bout again in either man's record: both names and the date it happened.</summary>
public sealed record BoutRef(string Winner, string Loser, DateOnly Date);

/// <summary>The end-of-year honours: the top three in each category.</summary>
public sealed class AwardsYear
{
    public int Year { get; init; }
    public List<AwardWinner> FighterOfYear { get; init; } = new();
    public List<AwardWinner> UpsetOfYear { get; init; } = new();
    public List<AwardWinner> KnockoutOfYear { get; init; } = new();
    public List<AwardWinner> FightOfYear { get; init; } = new();
}

/// <summary>A retired great enshrined in the Hall of Fame — a self-contained snapshot taken at induction so
/// it survives even after the fighter is pruned from the active roster on save.</summary>
public sealed class HallOfFamer
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public string? Nickname { get; init; }
    public string? Country { get; init; }
    public WeightClass Division { get; init; }
    public string Record { get; init; } = "";
    public int PeakOverall { get; init; }
    public int PeakClass { get; init; }    // best class (1–15 scale) reached — matches the ranking pills
    public int Defenses { get; init; }     // total world-title defences across every belt and division
    public bool WasChampion { get; init; }
    public int WeightTitles { get; init; } // number of distinct weight classes he won a world belt in
    public List<WeightClass> TitleDivisions { get; init; } = new();   // the specific divisions he won a world belt in
    public int Age { get; init; }          // age at retirement
    public int Year { get; init; }         // year inducted (= year retired)
    public List<BoutLine> History { get; init; } = new();   // his fight ledger, snapshotted at induction

    /// <summary>Ranking weight for the Hall: a world belt is the entry ticket, then multi-division reigns,
    /// defences and pure ability — a two- or three-weight champion outranks a one-belt titlist.</summary>
    public int Prestige => (WasChampion ? 1000 : 0) + Math.Max(0, WeightTitles - 1) * 60 + Defenses * 8 + PeakOverall;
}

/// <summary>One division's whole championship picture, for the champions list: each sanctioned belt with its
/// holder and defence count, the lineal ("Ring") champion, and the undisputed man if one exists.</summary>
public sealed record DivisionChampions(
    WeightClass Division,
    Boxer? Wba, int WbaDefenses,
    Boxer? Wbc, int WbcDefenses,
    Boxer? Ibf, int IbfDefenses,
    Boxer? Lineal, int LinealDefenses,
    Boxer? Undisputed);

/// <summary>What a fighter has actually WON — the credentials behind his pound-for-pound placing.</summary>
public sealed record Achievements(
    IReadOnlyList<string> Belts,   // sanctioned world belts held right now
    bool Lineal,                   // holds the lineal ("Ring") championship
    bool Undisputed,               // holds every belt going in his division
    int Defences,                  // longest current reign, in defences
    int WeightTitles,              // divisions he's won a world belt in
    int TitleWins);                // world title bouts won across his career

/// <summary>One supporting bout on the player's own show, as the card reads.</summary>
public sealed record UndercardBout(string A, string B, string? Winner, string Method, int EndRound, int Rounds,
                                   string Note = "")
{
    public bool IsDraw => Winner is null;
    public string Line => IsDraw ? $"{A} drew with {B}" : $"{Winner} beat {(Winner == A ? B : A)}";
    public string Verdict => IsDraw ? "draw"
                           : Method is "KO" or "TKO" or "cut" ? $"{Method} rd{EndRound}"
                           : Method;
}

/// <summary>One line of a bill: the fight, the distance, and — once the night has happened — what became
/// of it. The player's own bout is marked so the card can show it as his.</summary>
public sealed record BillLine(string Fight, int Rounds, bool IsPlayer, string What,
                              string Verdict, string Result, string Note,
                              // Each corner separately, so a bill can read like a bill: who, from where, and
                              // what he has done. "A vs B" alone told you nothing about either man, which on
                              // an undercard of seven names you have never heard of is no information at all.
                              string AName = "", string ACountry = "", string ARecord = "",
                              string BName = "", string BCountry = "", string BRecord = "",
                              bool IsTitle = false, WeightClass Div = WeightClass.Heavyweight, string ARank = "", string BRank = "")
{
    public bool Fought => Verdict.Length > 0;
    public string Distance => $"{Rounds} rds";
}
