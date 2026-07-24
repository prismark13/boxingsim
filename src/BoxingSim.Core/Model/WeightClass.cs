namespace BoxingSim.Core.Model;

/// <summary>The weight classes, lightest to heaviest. The junior/intermediate divisions were established
/// over time; each carries a founding year so it only comes into being in the right era.</summary>
public enum WeightClass
{
    Flyweight,
    Bantamweight,
    Featherweight,
    Lightweight,
    LightWelterweight,   // junior welterweight / super lightweight, 140 lb — established 1959
    Welterweight,
    LightMiddleweight,   // junior middleweight / super welterweight, 154 lb — established 1962
    Middleweight,
    LightHeavyweight,
    Cruiserweight,       // 190/200 lb — established 1980
    Heavyweight
}

public static class WeightClasses
{
    public static readonly WeightClass[] All = System.Enum.GetValues<WeightClass>();

    /// <summary>Upper weight limit in pounds, used for flavour/display.</summary>
    public static int WeightLimitLbs(this WeightClass wc) => wc switch
    {
        WeightClass.Flyweight => 112,
        WeightClass.Bantamweight => 118,
        WeightClass.Featherweight => 126,
        WeightClass.Lightweight => 135,
        WeightClass.LightWelterweight => 140,
        WeightClass.Welterweight => 147,
        WeightClass.LightMiddleweight => 154,
        WeightClass.Middleweight => 160,
        WeightClass.LightHeavyweight => 175,
        WeightClass.Cruiserweight => 200,
        WeightClass.Heavyweight => 999,
        _ => 0
    };

    public static string DisplayName(this WeightClass wc) => wc switch
    {
        WeightClass.LightHeavyweight => "Light Heavyweight",
        WeightClass.LightWelterweight => "Light Welterweight",
        WeightClass.LightMiddleweight => "Light Middleweight",
        _ => wc.ToString()
    };

    /// <summary>The year this division came into being. The traditional eight are "always" (0); the
    /// junior/intermediate divisions only exist from their founding year onward.</summary>
    public static int FoundedYear(this WeightClass wc) => wc switch
    {
        WeightClass.LightWelterweight => 1959,
        WeightClass.LightMiddleweight => 1962,
        WeightClass.Cruiserweight => 1980,
        _ => 0
    };

    /// <summary>
    /// How much a knockout "matters" by division. Heavier fighters end nights early; the lighter classes go
    /// the distance far more often. Compressed to match real-world stoppage rates.
    /// </summary>
    public static double KnockoutFactor(this WeightClass wc) => wc switch
    {
        WeightClass.Flyweight => 0.70,
        WeightClass.Bantamweight => 0.77,
        WeightClass.Featherweight => 0.83,
        WeightClass.Lightweight => 0.90,
        WeightClass.LightWelterweight => 0.94,
        WeightClass.Welterweight => 0.97,
        WeightClass.LightMiddleweight => 1.00,
        WeightClass.Middleweight => 1.04,
        WeightClass.LightHeavyweight => 1.12,
        WeightClass.Cruiserweight => 1.17,
        WeightClass.Heavyweight => 1.22,
        _ => 1.0
    };
}
