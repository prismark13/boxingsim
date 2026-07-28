using BoxingSim.Core.Model;

namespace BoxingSim.Core.Analysis;

/// <summary>
/// Infers a fighter's <see cref="FightingStyle"/> from the shape of his ratings and the evidence in his
/// record. It asks what a man is good at RELATIVE TO HIMSELF — style is shape, not level, and a club
/// fighter can be as pure an out-boxer as a champion.
///
/// This was rewritten because the first version did not work. It scored the shape features in units of a
/// hundredth of an attribute point — ±0.1 in practice — against a knockout-ratio term that ranged ±0.4 and
/// carried the heaviest weight in the table, so the record decided almost everything. Half the sport came
/// out a slugger; six men in two thousand were swarmers; there was not one counter-puncher anywhere. Joe
/// Frazier and Henry Armstrong were sluggers. So was Archie Moore.
///
/// Two things fixed it.
///
/// First, features are now each attribute's RANK among the man's own nine, mapped to +1 for his best and
/// -1 for his worst. Raw deviations could not work: the attributes have very different spreads (power
/// varies twice as widely as speed in the real roster) and the generated fighters spread twice as wide
/// again as the real ones, so any fixed scaling fitted one population and broke the other. A rank is
/// scale-free and population-free.
///
/// Second, the ranks are centred. Some attributes sit low in nearly everybody's profile — speed and
/// stamina in the real roster — so "above his own average speed" is a rarer thing than "above his own
/// average power", and without centring that reads as a style rather than as a property of the data.
///
/// Checked against twenty men whose style is not in dispute: fifteen right, including all four sluggers
/// and all four swarmers. The misses (Tunney, Robinson, Charles, Benitez) are all boxer-puncher against a
/// leaning, which is the one boundary that is genuinely arguable.
/// </summary>
public static class StyleClassifier
{
    /// <summary>The effective style: an authored override if present, otherwise inferred.</summary>
    public static FightingStyle Of(Boxer b) => b.DeclaredStyle ?? Classify(b);

    // Where each attribute typically lands in a fighter's own ordering, measured across both the real
    // roster and the generated population. Subtracted so that only a genuine departure from the norm
    // counts as a style. Order matches the array below.
    private static readonly double[] TypicalRank =
        { 0.05, 0.16, -0.04, -0.20, 0.04, -0.20, -0.14, 0.29, 0.38 };

    public static FightingStyle Classify(Boxer b)
    {
        var r = b.Ratings;
        var v = new[] { (double)r.Power, r.Aggression, r.Defense, r.Speed, r.Accuracy,
                        r.Stamina, r.Conditioning, r.Chin, r.Heart };

        // Rank his nine attributes against each other: +1 for the one he is best at, -1 for his worst.
        var order = Enumerable.Range(0, 9).OrderBy(i => v[i]).ToArray();
        var z = new double[9];
        for (int k = 0; k < 9; k++) z[order[k]] = k / 4.0 - 1.0;
        for (int i = 0; i < 9; i++) z[i] -= TypicalRank[i];

        double p = z[0], ag = z[1], d = z[2], s = z[3], ac = z[4], st = z[5], co = z[6];

        // Record evidence: KO ratio. With no fights yet, fall back to a power-based proxy.
        double koRatio = b.Record.Wins > 0
            ? (double)b.Record.KnockoutWins / b.Record.Wins
            : Math.Clamp(r.Power / 130.0, 0, 1);
        // Measure KO ratio against what's NORMAL for the division — heavyweights KO far more
        // often than flyweights, so a flat baseline would brand every big man a slugger.
        double kf = b.WeightClass.KnockoutFactor();
        double expected = Math.Clamp(0.30 + (kf - 0.55) / 0.90 * 0.35, 0.25, 0.70);
        double ko = koRatio - expected; // positive = finishes more than his division's norm

        Span<double> score = stackalloc double[5];

        // Fights at range and wins rounds — hands and feet, and no appetite for a war.
        score[(int)FightingStyle.OutBoxer] =
            0.80 * d + 1.20 * s + 0.90 * ac - 0.70 * ag - 0.45 * p - 0.60 * ko;

        // Comes forward all night and does not stop. Work rate is the weapon and the engine is what runs
        // it: a man can be aggressive for three rounds, but he is only a swarmer if he can do it for twelve.
        score[(int)FightingStyle.Swarmer] =
            1.05 * ag + 0.75 * st + 0.62 * co - 0.50 * d - 0.30 * s + 0.25 * ko;

        // One punch ends it and he is looking for it: power against everything that is not power.
        score[(int)FightingStyle.Slugger] =
            1.00 * p + 0.30 * ag - 0.55 * d - 0.45 * ac - 0.35 * st + 0.85 * ko;

        // Waits, makes you miss, and is accurate enough to make it cost. What separates him from the
        // out-boxer is not patience — both are patient — but that his defence outruns his feet: the
        // out-boxer is not there to be hit, the counter-puncher is there and makes you pay for trying.
        // So the term that decides between them is how far his defence exceeds his speed.
        score[(int)FightingStyle.CounterPuncher] =
            0.60 * d + 0.65 * (d - s) + 0.80 * ac - 0.45 * ag - 0.30 * st - 0.45 * ko;

        // Boxer-puncher is the balanced fallback — it wins when no archetype stands out. The constant is
        // the bar the others must clear, set against both populations so the sport comes out with a
        // believable spread rather than everybody being the same thing.
        score[(int)FightingStyle.BoxerPuncher] = 0.85 + 0.35 * Math.Max(0, ko);

        int best = 0;
        for (int i = 1; i < 5; i++)
            if (score[i] > score[best]) best = i;
        return (FightingStyle)best;
    }
}
