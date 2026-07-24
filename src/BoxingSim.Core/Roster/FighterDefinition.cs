using BoxingSim.Core.Analysis;
using BoxingSim.Core.Model;

namespace BoxingSim.Core.Roster;

/// <summary>
/// A hand-authored fighter "card": identity, ratings and an optional starting record.
/// This is the shape that loads from / saves to JSON, so you can build your own deck.
/// </summary>
public sealed class FighterDefinition
{
    public string Name { get; set; } = "Unnamed";
    public string? Nickname { get; set; }
    public WeightClass WeightClass { get; set; }
    /// <summary>Highest division this fighter campaigned in (for weight-movement). Null = single-division.</summary>
    public WeightClass? TopWeight { get; set; }
    public int Age { get; set; } = 28;
    public string? DateOfBirth { get; set; }
    public int? DebutYear { get; set; }
    public string? PrimeYears { get; set; }
    public string? Country { get; set; }
    public List<string>? Titles { get; set; }

    /// <summary>Optional authored style. If omitted, style is inferred from ratings + record.</summary>
    public FightingStyle? Style { get; set; }

    /// <summary>When true, ratings are derived from the record + <see cref="Tier"/> instead of the fields below.</summary>
    public bool AutoRate { get; set; }

    /// <summary>Level-of-competition hint used by auto-rating (ignored unless AutoRate is true).</summary>
    public FighterTier? Tier { get; set; }

    /// <summary>Informational flag from research: is this a famous fighter worth a curated card?</summary>
    public bool? Notable { get; set; }

    // Ratings, 1..100 (higher is always better).
    public int Power { get; set; } = 50;
    public int Chin { get; set; } = 50;
    public int Speed { get; set; } = 50;
    public int Defense { get; set; } = 50;
    public int Stamina { get; set; } = 50;
    public int Accuracy { get; set; } = 50;
    public int Conditioning { get; set; } = 50;
    public int CutResistance { get; set; } = 50;
    public int Aggression { get; set; } = 50;
    public int Heart { get; set; } = 50;

    // Optional starting record.
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public int KnockoutWins { get; set; }

    public Boxer ToBoxer(int id)
    {
        var ratings = AutoRate
            ? RatingsEstimator.FromRecord(Wins, Losses, Draws, KnockoutWins, WeightClass,
                                          Tier ?? FighterTier.Gatekeeper, Age)
            : new Ratings
            {
                Power = Ratings.Clamp(Power),
                Chin = Ratings.Clamp(Chin),
                Speed = Ratings.Clamp(Speed),
                Defense = Ratings.Clamp(Defense),
                Stamina = Ratings.Clamp(Stamina),
                Accuracy = Ratings.Clamp(Accuracy),
                Conditioning = Ratings.Clamp(Conditioning),
                CutResistance = Ratings.Clamp(CutResistance),
                Aggression = Ratings.Clamp(Aggression),
                Heart = Ratings.Clamp(Heart)
            };

        var b = new Boxer
        {
            Id = id,
            Name = Name,
            Nickname = Nickname,
            WeightClass = WeightClass,
            TopWeight = TopWeight,
            Age = Age,
            DateOfBirth = DateOfBirth,
            DebutYear = DebutYear,
            PrimeYears = PrimeYears,
            Country = Country,
            Titles = Titles,
            DeclaredStyle = Style,
            Potential = 0,
            Ratings = ratings
        };
        b.Potential = b.Overall;
        b.Record.Wins = Wins;
        b.Record.Losses = Losses;
        b.Record.Draws = Draws;
        b.Record.KnockoutWins = KnockoutWins;
        return b;
    }

    public static FighterDefinition FromBoxer(Boxer b) => new()
    {
        Name = b.Name,
        Nickname = b.Nickname,
        WeightClass = b.WeightClass,
        TopWeight = b.TopWeight,
        Age = b.Age,
        DateOfBirth = b.DateOfBirth,
        DebutYear = b.DebutYear,
        PrimeYears = b.PrimeYears,
        Country = b.Country,
        Titles = b.Titles,
        Power = b.Ratings.Power,
        Chin = b.Ratings.Chin,
        Speed = b.Ratings.Speed,
        Defense = b.Ratings.Defense,
        Stamina = b.Ratings.Stamina,
        Accuracy = b.Ratings.Accuracy,
        Conditioning = b.Ratings.Conditioning,
        CutResistance = b.Ratings.CutResistance,
        Aggression = b.Ratings.Aggression,
        Heart = b.Ratings.Heart,
        Wins = b.Record.Wins,
        Losses = b.Record.Losses,
        Draws = b.Record.Draws,
        KnockoutWins = b.Record.KnockoutWins
    };
}
