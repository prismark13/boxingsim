namespace BoxingSim.Core.Model;

/// <summary>
/// Classic boxing archetypes (an original taxonomy for this project). Style is partly innate
/// and partly inferred from how a fighter actually performs (see StyleClassifier).
/// </summary>
public enum FightingStyle
{
    OutBoxer,
    BoxerPuncher,
    Slugger,
    Swarmer,
    CounterPuncher
}

public static class FightingStyles
{
    public static string DisplayName(this FightingStyle s) => s switch
    {
        FightingStyle.OutBoxer => "Out-Boxer",
        FightingStyle.BoxerPuncher => "Boxer-Puncher",
        FightingStyle.Slugger => "Slugger",
        FightingStyle.Swarmer => "Swarmer",
        FightingStyle.CounterPuncher => "Counter-Puncher",
        _ => s.ToString()
    };

    public static string Describe(this FightingStyle s) => s switch
    {
        FightingStyle.OutBoxer => "Fights at range behind the jab; movement and accuracy over power.",
        FightingStyle.BoxerPuncher => "Blends real boxing skill with genuine pop — adaptable.",
        FightingStyle.Slugger => "Huge power and comes to end it; defense is an afterthought.",
        FightingStyle.Swarmer => "Relentless pressure and volume; smothers opponents.",
        FightingStyle.CounterPuncher => "Patient and defensive; makes you miss, then makes you pay.",
        _ => ""
    };

    /// <summary>
    /// Style matchup advantage of <paramref name="a"/> over <paramref name="b"/>, in ~[-0.6, 0.6].
    /// Encodes the rock-paper-scissors of styles: the swarmer hunts the out-boxer, the out-boxer
    /// neutralises the slugger, the slugger walks through the swarmer, and the counter-puncher
    /// punishes aggression but has nothing to time against a pure out-boxer.
    /// </summary>
    public static double Advantage(FightingStyle a, FightingStyle b)
    {
        if (a == b) return 0;
        double v = Upper(a, b);
        if (v != -99) return v;
        v = Upper(b, a);
        return v == -99 ? 0 : -v;
    }

    private static double Upper(FightingStyle a, FightingStyle b) => (a, b) switch
    {
        (FightingStyle.OutBoxer, FightingStyle.Slugger) => 0.60,
        (FightingStyle.OutBoxer, FightingStyle.Swarmer) => -0.60,
        (FightingStyle.OutBoxer, FightingStyle.CounterPuncher) => 0.30,
        (FightingStyle.BoxerPuncher, FightingStyle.Slugger) => 0.20,
        (FightingStyle.BoxerPuncher, FightingStyle.Swarmer) => 0.10,
        (FightingStyle.Slugger, FightingStyle.Swarmer) => 0.60,
        (FightingStyle.Slugger, FightingStyle.CounterPuncher) => -0.50,
        (FightingStyle.Swarmer, FightingStyle.CounterPuncher) => -0.50,
        _ => -99 // not in the upper-triangle table
    };
}
