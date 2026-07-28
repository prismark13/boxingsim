using BoxingSim.Core.Model;

namespace BoxingSim.Core.Career;

/// <summary>The dials for a universe: a world that runs on its own, with nobody in it to play.
///
/// Career mode fixes all of this — one division, one fighter, the sim's own numbers. A universe is the same
/// engine with the settings exposed, so a sport can be built that behaves differently from the default one:
/// busier or lazier, short careers or long ones, one division or all seventeen.</summary>
public sealed record UniverseSettings
{
    /// <summary>The year the world opens.</summary>
    public int StartYear { get; init; } = 1960;

    /// <summary>Which divisions exist. Empty means every division the era supports.</summary>
    public IReadOnlyList<WeightClass> Divisions { get; init; } = Array.Empty<WeightClass>();

    /// <summary>New professionals per division per year. The default sim uses 14–23; fewer makes a thin,
    /// top-heavy sport where the same men meet repeatedly, more makes a deep one full of unknowns.</summary>
    public int EntrantsPerYear { get; init; } = 18;

    /// <summary>Scales how long careers run. 1.0 is the default — a median of about 50 fights, nothing under 28
    /// or over 90. Halve it for a brutal sport where men are used up; double it for one where they go on.</summary>
    public double CareerLength { get; init; } = 1.0;

    /// <summary>Scales how often everybody fights. 1.0 is the default — five or six a year for a novice, three
    /// or four once established.</summary>
    public double Activity { get; init; } = 1.0;

    /// <summary>Whether the real roster seeds the world. Off, the sport is built entirely from generated
    /// fighters and shares nothing with history but its rules.</summary>
    public bool UseRealFighters { get; init; } = true;

    /// <summary>Years of history simulated before the first week, so the world opens with champions, contenders
    /// and records rather than a division of debutants.</summary>
    public int WarmupYears { get; init; } = 8;

    public int Seed { get; init; } = 0;
}

/// <summary>One bout as the universe reports it — enough to build a card from, without the ticks.</summary>
public sealed record WorldBout(DateOnly Date, WeightClass Division, string Region, string Country,
                               string Winner, string Loser, string Method, int Round,
                               bool Draw, string? Title)
{
    /// <summary>A belt was on the line. The tag also carries fights that are not for a title but are still the
    /// night's reason to be there - a superfight, an eliminator, a return - so the tag being present is not
    /// enough on its own; a title is a title.</summary>
    public bool IsTitle => Title is not null && Title.EndsWith("title", StringComparison.Ordinal);

    /// <summary>What this fight is, if it is anything: a belt, a superfight, an eliminator, a return.</summary>
    public string? Tag => Title;

    /// <summary>The result in one line. A draw has no winner, so it must not be written as though it did —
    /// "X beat Y, drew with" is the sort of thing that gets built when a winner is assumed.</summary>
    public string Headline => $"{Winner} {Joiner} {Loser}";

    /// <summary>The word between the two names, for a card that shows them separately so each can be clicked.</summary>
    public string Joiner => Draw ? "drew with" : "beat";

    /// <summary>How it ended, on its own.</summary>
    public string Verdict => Draw ? "draw"
                           : Method is "KO" or "TKO" or "cut" ? $"{Method} rd{Round}"
                           : Method;
}
