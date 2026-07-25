using BoxingSim.Core.Model;

namespace BoxingSim.Core.Analysis;

/// <summary>A fighter's punch arsenal — the mix of shots he throws and how well he counters.
/// This is the single source of truth shared by the fight engine and the UI.</summary>
public static class PunchProfile
{
    /// <summary>What fraction of a fighter's punches are power shots rather than jabs — driven mainly by his
    /// STYLE, then pulled further off the jab by punching power and aggression, and back toward it by a sharp jab
    /// (accuracy). A jabbing out-boxer sits near 18–22% power; a swarming pressure fighter pushes past 50%.</summary>
    public static double PowerFraction(FightingStyle style, Ratings r)
    {
        double jabBase = style switch
        {
            FightingStyle.OutBoxer       => 0.79,
            FightingStyle.CounterPuncher => 0.73,
            FightingStyle.BoxerPuncher   => 0.65,
            FightingStyle.Slugger        => 0.60,
            FightingStyle.Swarmer        => 0.55,
            _                            => 0.66,
        };
        double jab = jabBase
                   - Math.Max(0, r.Power - 68) * 0.0025
                   - Math.Max(0, r.Aggression - 68) * 0.0018
                   + (r.Accuracy - 75) * 0.0012;
        return 1 - Math.Clamp(jab, 0.45, 0.85);
    }

    /// <summary>Raw relative weights for the four power punches: cross, hook, uppercut, body.</summary>
    public static double[] PowerWeights(FightingStyle style, Ratings r)
    {
        double cross = 26 + r.Accuracy * 0.12;
        double hook = 20 + Math.Max(0, r.Power - 55) * 0.35;
        double upper = 8 + Math.Max(0, r.Power - 60) * 0.18;
        double body = 14;
        switch (style)
        {
            case FightingStyle.OutBoxer: cross += 12; break;
            case FightingStyle.CounterPuncher: cross += 10; break;
            case FightingStyle.BoxerPuncher: cross += 4; hook += 4; body += 6; break;
            case FightingStyle.Slugger: hook += 16; upper += 9; break;
            case FightingStyle.Swarmer: body += 46; hook += 8; upper += 5; break;   // relentless body attack
        }
        return new[] { Math.Max(2, cross), Math.Max(2, hook), Math.Max(2, upper), Math.Max(2, body) };
    }

    /// <summary>The full arsenal as whole-number percentages (jab, cross, hook, uppercut, body).</summary>
    public static (int Jab, int Cross, int Hook, int Uppercut, int Body) Distribution(Boxer b)
    {
        var style = StyleClassifier.Of(b);
        double powerFrac = PowerFraction(style, b.Ratings);
        var w = PowerWeights(style, b.Ratings);
        double tot = w[0] + w[1] + w[2] + w[3];
        int cross = (int)Math.Round(powerFrac * w[0] / tot * 100);
        int hook = (int)Math.Round(powerFrac * w[1] / tot * 100);
        int upper = (int)Math.Round(powerFrac * w[2] / tot * 100);
        int body = (int)Math.Round(powerFrac * w[3] / tot * 100);
        int jab = Math.Max(0, 100 - cross - hook - upper - body);   // jab absorbs the rounding so it always sums to 100
        return (jab, cross, hook, upper, body);
    }

    /// <summary>A 1–99 rating of how dangerous the fighter is on the counter.</summary>
    public static int CounterRating(Boxer b)
    {
        var r = b.Ratings;
        double skill = r.Defense * 0.35 + r.Speed * 0.35 + r.Accuracy * 0.30;
        double bonus = StyleClassifier.Of(b) switch
        {
            FightingStyle.CounterPuncher => 14,
            FightingStyle.OutBoxer => 7,
            FightingStyle.BoxerPuncher => 3,
            FightingStyle.Swarmer => -4,
            FightingStyle.Slugger => -8,
            _ => 0
        };
        return (int)Math.Clamp(skill + bonus, 1, 99);
    }
}
