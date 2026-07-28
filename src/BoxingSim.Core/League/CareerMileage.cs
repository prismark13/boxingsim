using BoxingSim.Core.Model;
using BoxingSim.Core.Career;

namespace BoxingSim.Core.Generation;

/// <summary>Where a fighter is in his career, measured in FIGHTS rather than birthdays.
///
/// A boxer is not worn out by getting older, he is worn out by being hit. Two men of thirty are not the same
/// fighter if one has had eighteen bouts and the other seventy — and the sim used to treat them identically,
/// declining both on their age against a notional peak age while their mileage counted for nothing.
///
/// Every boundary here is in bouts, and every one is varied per fighter so no two men age on the same
/// schedule. The variation is derived from the man's own name, so it is stable across sessions and saves
/// without needing to be stored anywhere, and it never shifts under him mid-career.</summary>
public static class CareerMileage
{
    /// <summary>Nobody's career is shorter than this unless an injury ends it — a man who can still fight
    /// keeps getting offers.</summary>
    public const int MinimumCareer = 28;

    private static int Vary(Boxer b, string salt, int spread)
    {
        unchecked
        {
            int h = 17;
            foreach (char c in b.Name) h = h * 31 + c;
            foreach (char c in salt) h = h * 31 + c;
            return (h & 0x7FFFFFFF) % spread;
        }
    }

    /// <summary>Still learning the trade: a handful of six-rounders.</summary>
    public static int StarterUntil(Boxer b) => 5 + Vary(b, "st", 4);            // 5–8

    /// <summary>Coming through: stepping up in class, still improving fight on fight.</summary>
    public static int PrePrimeUntil(Boxer b) => 21 + Vary(b, "pp", 9);          // 21–29

    /// <summary>The best of him. Everything after this is a man spending what he built.</summary>
    public static int PrimeUntil(Boxer b) => 46 + Vary(b, "pr", 14);            // 46–59

    /// <summary>Still competitive, but the edge has gone.</summary>
    public static int PostPrimeUntil(Boxer b) => 64 + Vary(b, "po", 12);        // 64–75

    /// <summary>Nobody goes beyond this. Long careers, but not endless ones.</summary>
    public static int CareerLimit(Boxer b) => 80 + Vary(b, "end", 11);          // 80–90

    public static int Fights(Boxer b) => b.Record.Wins + b.Record.Losses + b.Record.Draws;

    /// <summary>How far past his best a man is, in bouts. Zero while he is still in it.</summary>
    public static int PastPrime(Boxer b) => Math.Max(0, Fights(b) - PrimeUntil(b));

    /// <summary>How far through his development he is, 0 at debut and 1 once he has arrived. Drives how much
    /// of his ceiling a fighter has grown into.</summary>
    public static double Development(Boxer b)
    {
        int f = Fights(b), primeAt = PrePrimeUntil(b);
        if (f <= primeAt)
        {
            double t = primeAt <= 0 ? 1 : f / (double)primeAt;
            return 0.55 + 0.45 * Math.Clamp(t, 0, 1);
        }
        // Past his best, ability bleeds away with the mileage rather than with the calendar.
        return Math.Max(0.45, 1.0 - PastPrime(b) * 0.010);
    }

    public static CareerStage StageOf(Boxer b)
    {
        int f = Fights(b);
        if (f <= StarterUntil(b)) return CareerStage.Starter;
        if (f <= PrePrimeUntil(b)) return CareerStage.PrePrime;
        if (f <= PrimeUntil(b)) return CareerStage.Prime;
        if (f <= PostPrimeUntil(b)) return CareerStage.PostPrime;
        return CareerStage.End;
    }
}
