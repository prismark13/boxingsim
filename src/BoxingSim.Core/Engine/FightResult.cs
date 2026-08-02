using BoxingSim.Core.Model;

namespace BoxingSim.Core.Engine;

public enum FightOutcome
{
    Knockout,
    TechnicalKnockout,
    Decision,
    Draw
}

/// <summary>One judge's view of a single round.</summary>
public readonly record struct RoundCard(int ScoreA, int ScoreB);

/// <summary>The full outcome of a bout.</summary>
public sealed class FightResult
{
    public required Boxer A { get; init; }
    public required Boxer B { get; init; }

    /// <summary>Winner, or null for a draw.</summary>
    public Boxer? Winner { get; init; }
    public Boxer? Loser { get; init; }

    public FightOutcome Outcome { get; init; }
    public int ScheduledRounds { get; init; }
    public int EndRound { get; init; }

    public int KnockdownsA { get; init; }
    public int KnockdownsB { get; init; }

    /// <summary>Final scorecards (three judges) for bouts that go to the cards.</summary>
    public IReadOnlyList<(int A, int B)> Scorecards { get; init; } = Array.Empty<(int, int)>();

    public IReadOnlyList<RoundResult> Rounds { get; init; } = Array.Empty<RoundResult>();

    /// <summary>Acute injuries sustained (with minimum layoffs) — career-sim hooks.</summary>
    public IReadOnlyList<Injury> Injuries { get; init; } = Array.Empty<Injury>();

    /// <summary>Permanent attribute changes a fighter carries forward — rare; career-sim hooks.</summary>
    public IReadOnlyList<LastingEffect> Lasting { get; init; } = Array.Empty<LastingEffect>();

    /// <summary>True when the bout was a genuine war (3+ knockdowns, both down, or sustained punishment).</summary>
    public bool War { get; init; }

    /// <summary>True when a stoppage came primarily from body punishment.</summary>
    public bool BodyStop { get; init; }

    public bool IsDraw => Winner is null;

    /// <summary>Short method string: "KO", "TKO", "cut", "DQ", "UD", "SD", "MD", "D".</summary>
    public string Method { get; init; } = "";

    /// <summary>The bout ended inside the distance, by any route — a knockout or a technical knockout.
    ///
    /// Which of the two it was is <see cref="Outcome"/>'s business and matters to the RECORD, where they sit
    /// in separate columns: a knockout is a man counted out, a technical knockout is the referee stepping in
    /// or a cut ending it. Summing them into one KO column is what gave fighters with no power gaudy KO
    /// tallies — a cut stoppage reads the loser's cut resistance and never the winner's punch.</summary>
    public bool IsStoppage => Outcome is FightOutcome.Knockout or FightOutcome.TechnicalKnockout;

    public string Headline()
    {
        if (IsDraw)
            return $"{A.Name} drew with {B.Name} ({Method}) after {EndRound} rounds";
        return Outcome switch
        {
            FightOutcome.Knockout =>
                $"{Winner!.Name} def. {Loser!.Name} by KO, round {EndRound}",
            FightOutcome.TechnicalKnockout =>
                $"{Winner!.Name} def. {Loser!.Name} by TKO, round {EndRound}",
            _ => $"{Winner!.Name} def. {Loser!.Name} by decision ({Method})"
        };
    }
}
