using BoxingSim.Core.Model;

namespace BoxingSim.Core.Analysis;

/// <summary>How good the opposition / level a fighter operated at — seeds the rating estimate.</summary>
public enum FighterTier
{
    Journeyman,
    Gatekeeper,
    TopContender,
    Champion,
    Prospect
}

/// <summary>
/// Derives a plausible rating set from a fighter's RECORD plus a tier hint. Used to auto-rate
/// the supporting cast (journeymen, gatekeepers) when hand-authoring every card isn't practical.
/// Deterministic: the same inputs always give the same ratings.
/// </summary>
public static class RatingsEstimator
{
    public static Ratings FromRecord(int wins, int losses, int draws, int koWins,
        WeightClass wc, FighterTier tier, int age)
    {
        int fights = wins + losses + draws;
        double winRate = fights > 0 ? (wins + 0.5 * draws) / fights : 0.55;
        double koRate = wins > 0 ? (double)koWins / wins : 0.35;

        // How much they finish relative to their division's norm (heavyweights KO far more).
        double kf = wc.KnockoutFactor();
        double expectedKo = Math.Clamp(0.30 + (kf - 0.55) / 0.90 * 0.35, 0.25, 0.70);
        double koEdge = koRate - expectedKo;

        // Conservative bases + a low ceiling so an undefeated, padded record can't auto-rate a
        // limited fighter into the all-time elite — auto cards must sit BELOW the curated greats.
        double tierBase = tier switch
        {
            FighterTier.Champion => 73,
            FighterTier.TopContender => 68,
            FighterTier.Prospect => 60,
            FighterTier.Gatekeeper => 62,
            _ => 55 // Journeyman
        };
        double level = Math.Clamp(tierBase + (winRate - 0.65) * 14, 38, 76);

        int over30 = Math.Max(0, age - 30);
        double durable = tier is FighterTier.Gatekeeper ? 8 : 3; // gatekeepers are known for toughness
        double koBump = Math.Clamp(koEdge * 28, -12, 10);        // damped, capped KO-power influence

        var r = new Ratings
        {
            Power        = Clamp(level + koBump),
            Aggression   = Clamp(level + Math.Clamp(koEdge * 24, -10, 10)),
            Defense      = Clamp(level + (winRate - 0.65) * 16 - koEdge * 18),
            Accuracy     = Clamp(level + (winRate - 0.65) * 10),
            Speed        = Clamp(level - over30 * 0.8),
            Stamina      = Clamp(level - koEdge * 8),
            Conditioning = Clamp(level - over30 * 0.6),
            Chin         = Clamp(level + durable - over30 * 0.4),
            CutResistance= Clamp(level),
            Heart        = Clamp(level + durable * 0.6)
        };
        return r;
    }

    private static int Clamp(double v) => Ratings.Clamp((int)Math.Round(v));
}
