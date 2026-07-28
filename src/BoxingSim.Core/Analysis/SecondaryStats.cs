using BoxingSim.Core.Model;

namespace BoxingSim.Core.Analysis;

/// <summary>
/// Secondary ratings (1–99) derived from the ten primary attributes. They name the qualities the
/// fight engine actually keys on — finishing, durability, recovery, pressure, countering — so the
/// same numbers can be shown on a fighter's card and used in the ring.
/// </summary>
public static class SecondaryStats
{
    /// <summary>Killer instinct: how hard he jumps on a hurt man to finish it.</summary>
    public static int KillerInstinct(Ratings r) => C(r.Aggression * 0.60 + r.Power * 0.25 + r.Heart * 0.15);

    /// <summary>Durability: how well he stays up and weathers a storm.</summary>
    public static int Durability(Ratings r) => C(r.Chin * 0.50 + r.Heart * 0.30 + r.Conditioning * 0.20);

    /// <summary>Recovery: beating the count and getting back between rounds.</summary>
    public static int Recovery(Ratings r) => C(r.Conditioning * 0.55 + r.Heart * 0.45);

    /// <summary>Pressure: crowding and smothering an opponent.</summary>
    public static int Pressure(Ratings r) => C(r.Aggression * 0.70 + r.Stamina * 0.30);

    /// <summary>Dirtiness: how far past the rules he is willing to go.
    ///
    /// Every fighter in the sim fouled at exactly the same rate, so a mauling swarmer who lives on the inside
    /// and a disciplined out-boxer were equally likely to throw a low blow. They are not. This is a man's own
    /// trait: the style he fights sets the base - work on the inside brings heads and elbows with it, boxing
    /// at range does not - aggression pushes it up, and the rest is him, taken from his name so it is fixed
    /// for life and costs nothing to store.
    ///
    /// It drives how often he fouls, and therefore how often he loses a point or gets thrown out.</summary>
    public static int Dirtiness(Boxer b)
    {
        double style = StyleClassifier.Of(b) switch
        {
            FightingStyle.Swarmer => 62,          // lives on the inside, where fouls happen
            FightingStyle.Slugger => 52,
            FightingStyle.BoxerPuncher => 42,
            FightingStyle.CounterPuncher => 34,
            FightingStyle.OutBoxer => 28,         // no reason to maul at range
            _ => 42
        };
        style += (b.Ratings.Aggression - 55) * 0.30;

        // The man himself, stable from his name: some fighters are simply chancers.
        unchecked
        {
            int h = 17;
            foreach (char c in b.Name) h = h * 31 + c;
            foreach (char c in "dirt") h = h * 31 + c;
            style += ((h & 0x7FFFFFFF) % 41) - 20;
        }
        return C(style);
    }

    /// <summary>Counter: how dangerous he is catching a man who misses.</summary>
    public static int Counter(Boxer b)
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
        return C(skill + bonus);
    }

    private static int C(double v) => (int)System.Math.Clamp(System.Math.Round(v), 1, 99);
}
