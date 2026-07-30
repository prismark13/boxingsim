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
    /// <summary>How long careers run and how busy fighters are, as multipliers on the sim's own numbers.
    ///
    /// Career mode leaves these at 1 and behaves exactly as it always has. A universe sets them to build a
    /// different sport — a brutal one where men are used up in twenty fights, or a durable one where they go
    /// on into their fifties. They are process-wide because the mileage rules are pure functions of a fighter
    /// and have nowhere to carry a world with them; a universe sets them when it starts and career mode resets
    /// them, so the two never disagree within a session.</summary>
    public static double LengthScale { get; private set; } = 1.0;
    public static double ActivityScale { get; private set; } = 1.0;

    /// <summary>Set the dials for as long as the returned handle is held, and put them back when it is let go.
    ///
    /// They used to be plain settable statics with a separate ResetScales() that a caller had to remember —
    /// and if it did not, a universe's rules leaked silently into the next career and every fighter aged on
    /// the wrong schedule with nothing to show for it. The test suite had the same obligation and the same
    /// exposure. The scope makes forgetting impossible, and the previous values are restored rather than
    /// assumed to be the defaults, so nesting behaves.
    ///
    /// This does NOT make them not-global: two worlds still cannot run at once, and that remains the real fix
    /// — threading a settings object down from CareerGame, which is a change to some thirty-five call sites
    /// and deserves its own pass with the golden master as the net. What it does is close the leak, which is
    /// the part that can actually bite today.</summary>
    public static IDisposable Scale(double length, double activity)
    {
        var restore = new Restore(LengthScale, ActivityScale);
        LengthScale = Math.Clamp(length, 0.3, 3.0);
        ActivityScale = Math.Clamp(activity, 0.3, 3.0);
        return restore;
    }

    private sealed class Restore(double length, double activity) : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;          // idempotent: releasing twice must not undo somebody else's scope
            _done = true;
            LengthScale = length;
            ActivityScale = activity;
        }
    }

    /// <summary>Nobody's career is shorter than this unless an injury ends it — a man who can still fight
    /// keeps getting offers.</summary>
    public static int MinimumCareer => Math.Max(4, (int)Math.Round(28 * LengthScale));

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
    public static int StarterUntil(Boxer b) => Scaled(5 + Vary(b, "st", 4));            // 5-8, scaled

    /// <summary>Coming through: stepping up in class, still improving fight on fight.</summary>
    public static int PrePrimeUntil(Boxer b) => Scaled(21 + Vary(b, "pp", 9));          // 21-29, scaled

    /// <summary>The best of him. Everything after this is a man spending what he built.</summary>
    public static int PrimeUntil(Boxer b) => Scaled(46 + Vary(b, "pr", 14));            // 46-59, scaled

    /// <summary>Still competitive, but the edge has gone.</summary>
    public static int PostPrimeUntil(Boxer b) => Scaled(64 + Vary(b, "po", 12));        // 64-75, scaled

    /// <summary>Nobody goes beyond this. Long careers, but not endless ones.</summary>
    public static int CareerLimit(Boxer b) => Scaled(80 + Vary(b, "end", 11));          // 80-90, scaled

    public static int Fights(Boxer b) => b.Record.Wins + b.Record.Losses + b.Record.Draws;

    /// <summary>How busy a man keeps himself, roughly half to one and a half times a normal schedule.
    ///
    /// Averages were right but every fighter sat on them, so every champion defended at the same cadence and
    /// every contender boxed the same number of times. Real careers are not like that: one champion is out
    /// four times a year and another makes a single defence and disappears, and the difference is the man, not
    /// the year. This is a trait like the stage boundaries - fixed for life, taken from his own name, so it
    /// costs nothing to store and never shifts under him.</summary>
    public static double Activity(Boxer b) => (0.55 + Vary(b, "act", 95) / 100.0) * ActivityScale;

    /// <summary>Apply the world's career-length dial to a boundary, keeping it at least one bout.</summary>
    private static int Scaled(int fights) => Math.Max(1, (int)Math.Round(fights * LengthScale));

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
