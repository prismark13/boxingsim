using BoxingSim.Core.Model;

namespace BoxingSim.Core.Analysis;

/// <summary>A fighter's punch arsenal — the mix of shots he throws and how well he counters.
/// This is the single source of truth shared by the fight engine and the UI.</summary>
public static class PunchProfile
{
    /// <summary>Raw relative weights for the four power punches: cross, hook, uppercut, body.</summary>
    public static double[] PowerWeights(FightingStyle style, Ratings r)
    {
        double cross = 30 + r.Accuracy * 0.10;
        double hook = 22 + Math.Max(0, r.Power - 55) * 0.40;
        double upper = 10 + Math.Max(0, r.Power - 60) * 0.20;
        double body = 20;
        switch (style)
        {
            case FightingStyle.OutBoxer: cross += 8; break;
            case FightingStyle.BoxerPuncher: cross += 4; hook += 3; break;
            case FightingStyle.Slugger: hook += 12; upper += 6; break;
            case FightingStyle.Swarmer: body += 14; hook += 6; upper += 4; break;
            case FightingStyle.CounterPuncher: cross += 8; break;
        }
        return new[] { Math.Max(2, cross), Math.Max(2, hook), Math.Max(2, upper), Math.Max(2, body) };
    }

    /// <summary>The full arsenal as whole-number percentages (jab, cross, hook, uppercut, body).</summary>
    public static (int Jab, int Cross, int Hook, int Uppercut, int Body) Distribution(Boxer b)
    {
        var style = StyleClassifier.Of(b);
        double powerFrac = Math.Clamp(0.20 + b.Ratings.Power / 500.0, 0.0, 0.6);
        var w = PowerWeights(style, b.Ratings);
        double tot = w[0] + w[1] + w[2] + w[3];
        int jab = (int)Math.Round((1 - powerFrac) * 100);
        int cross = (int)Math.Round(powerFrac * w[0] / tot * 100);
        int hook = (int)Math.Round(powerFrac * w[1] / tot * 100);
        int upper = (int)Math.Round(powerFrac * w[2] / tot * 100);
        int body = (int)Math.Round(powerFrac * w[3] / tot * 100);
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
