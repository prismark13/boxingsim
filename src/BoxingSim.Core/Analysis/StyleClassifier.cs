using BoxingSim.Core.Model;

namespace BoxingSim.Core.Analysis;

/// <summary>
/// Infers a fighter's <see cref="FightingStyle"/> from the shape of their ratings and the
/// evidence in their record (chiefly KO ratio). Profile features are measured relative to the
/// fighter's own average, so it's about the SHAPE of a fighter — not how good they are overall.
/// </summary>
public static class StyleClassifier
{
    /// <summary>The effective style: an authored override if present, otherwise inferred.</summary>
    public static FightingStyle Of(Boxer b) => b.DeclaredStyle ?? Classify(b);

    public static FightingStyle Classify(Boxer b)
    {
        var r = b.Ratings;
        double avg = (r.Power + r.Chin + r.Speed + r.Defense + r.Stamina +
                      r.Accuracy + r.Conditioning + r.Aggression + r.Heart) / 9.0;

        // Profile features, relative to the fighter's own average (so shape, not level).
        double p = (r.Power - avg) / 100.0;
        double ag = (r.Aggression - avg) / 100.0;
        double d = (r.Defense - avg) / 100.0;
        double s = (r.Speed - avg) / 100.0;
        double ac = (r.Accuracy - avg) / 100.0;
        double st = (r.Stamina - avg) / 100.0;
        double co = (r.Conditioning - avg) / 100.0;

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
        score[(int)FightingStyle.OutBoxer]       = 1.0 * d + 0.7 * s + 0.7 * ac - 1.0 * ag - 0.3 * p - 1.2 * ko;
        score[(int)FightingStyle.Swarmer]        = 1.0 * ag + 0.7 * st + 0.6 * co - 0.6 * d + 0.3 * ko;
        score[(int)FightingStyle.Slugger]        = 1.1 * p + 0.5 * ag - 0.6 * d - 0.3 * ac + 1.6 * ko;
        score[(int)FightingStyle.CounterPuncher] = 0.9 * d + 0.8 * ac + 0.6 * s - 0.8 * ag - 0.5 * ko;
        // Boxer-puncher is the balanced fallback — it wins when no archetype stands out.
        score[(int)FightingStyle.BoxerPuncher]   = 0.16 + 0.4 * Math.Max(0, ko);

        int best = 0;
        for (int i = 1; i < 5; i++)
            if (score[i] > score[best]) best = i;
        return (FightingStyle)best;
    }
}
