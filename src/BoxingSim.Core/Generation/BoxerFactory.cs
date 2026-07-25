using BoxingSim.Core.Model;

namespace BoxingSim.Core.Generation;

/// <summary>Creates fighters: fresh prospects and a believable starting roster.</summary>
public sealed class BoxerFactory
{
    private readonly Random _rng;
    private readonly NameGenerator _names;
    private int _nextId = 1;

    public BoxerFactory(Random rng)
    {
        _rng = rng;
        _names = new NameGenerator(rng);
    }

    /// <summary>Reserve names the generator must never produce (e.g. the real historical roster).</summary>
    public void Reserve(IEnumerable<string> names) => _names.Reserve(names);

    /// <summary>Start id assignment at <paramref name="firstId"/> so generated fighters never collide with the
    /// historical roster's ids (both would otherwise start at 1 — a collision corrupts every id-keyed map).</summary>
    public void StartIdsAt(int firstId) { if (firstId > _nextId) _nextId = firstId; }

    // A spread of boxing nations across the three regional-belt territories (USA weighted heavily,
    // as in the real heavyweight scene) so the NABF / Commonwealth / European titles are all contested.
    private static readonly string[] Countries =
    {
        "USA", "USA", "USA", "USA", "USA", "Mexico", "Mexico", "Cuba", "Argentina", "Puerto Rico",
        "Canada", "Brazil", "Panama", "Venezuela", "Colombia",                                       // NABF
        "England", "England", "Ireland", "Australia", "Nigeria", "South Africa", "Ghana", "Jamaica", // Commonwealth
        "Germany", "Italy", "Russia", "Ukraine", "Poland", "France", "Spain", "Sweden", "Kazakhstan" // European
    };

    /// <summary>Fraction of a fighter's potential that is realised at a given age.</summary>
    public static double Development(int age, int peakAge)
    {
        if (age <= peakAge)
        {
            double t = (age - 17.0) / (peakAge - 17.0);
            return 0.55 + 0.45 * Math.Clamp(t, 0, 1);
        }
        double decline = (age - peakAge) * 0.035; // ~3.5% of potential lost per year past peak
        return Math.Max(0.45, 1.0 - decline);
    }

    /// <summary>A raw 18–21 year old: low current ability, the ceiling still ahead of them.
    /// <paramref name="maxPotential"/> caps the ceiling — generated filler stays journeyman-class.</summary>
    public Boxer CreateProspect(WeightClass wc, int maxPotential = 100)
    {
        int age = _rng.Next(18, 22);
        int potential = Math.Min(maxPotential, RollPotential());
        int peak = _rng.Next(26, 31);
        var b = Build(wc, age, peak, potential);
        return b;
    }

    /// <summary>A fighter somewhere in their career, with a record to match their level and age.</summary>
    public Boxer CreateExisting(WeightClass wc, int maxPotential = 100)
    {
        int age = _rng.Next(19, 37);
        int potential = Math.Min(maxPotential, RollPotential());
        int peak = _rng.Next(26, 31);
        var b = Build(wc, age, peak, potential);
        SeedRecord(b);
        return b;
    }

    private Boxer Build(WeightClass wc, int age, int peakAge, int potential)
    {
        double dev = Development(age, peakAge);
        bool young = age <= peakAge;

        // Per-attribute ceilings scatter around the headline potential so fighters
        // develop distinct identities (banger, boxer, iron chin, glass jaw, etc.).
        int Ceiling(int spread) => Ratings.Clamp(potential + _rng.Next(-spread, spread + 1));

        // Power, defence, speed and the punch arsenal arrive early; stamina, conditioning and
        // timing are what a young fighter still has to build. Past the peak they all erode.
        var r = new Ratings
        {
            Power = Scale(Ceiling(18), young ? Lerp(dev, 1.0, 0.85) : dev),
            Speed = Scale(Ceiling(14), young ? Lerp(dev, 1.0, 0.82) : dev),
            Defense = Scale(Ceiling(15), Lerp(dev, 1.0, young ? 0.72 : 0.4)),
            Accuracy = Scale(Ceiling(14), Lerp(dev, 1.0, 0.5)),
            Stamina = Scale(Ceiling(14), dev),
            Conditioning = Scale(Ceiling(14), dev),
            Chin = Scale(Ceiling(16), Lerp(dev, 1.0, 0.6)),   // chin is fairly innate
            CutResistance = Ceiling(20),                        // innate, age-independent
            Aggression = Ceiling(22),                           // temperament, age-independent
            Heart = Ceiling(18)                                 // innate, age-independent
        };

        string name = _names.Next();
        return new Boxer
        {
            Id = _nextId++,
            Name = name,
            Country = Countries[_rng.Next(Countries.Length)],
            WeightClass = wc,
            Ratings = r,
            Reach = Physique.ReachInchesFor(wc, name),
            Age = age,
            PeakAge = peakAge,
            Potential = potential
        };
    }

    /// <summary>Potential is right-skewed: most fighters are journeymen, a few are special.</summary>
    private int RollPotential()
    {
        // Average of three rolls clusters around the middle; a rare high roll spikes it.
        int baseRoll = (_rng.Next(35, 96) + _rng.Next(35, 96) + _rng.Next(35, 96)) / 3;
        if (_rng.NextDouble() < 0.04) baseRoll += _rng.Next(6, 14); // occasional phenom
        return Ratings.Clamp(baseRoll);
    }

    private static int Scale(int ceiling, double dev) => Ratings.Clamp((int)Math.Round(ceiling * dev));

    private static double Lerp(double dev, double atDev, double floor) =>
        floor + (atDev - floor) * dev;

    /// <summary>Give an established fighter a plausible record for their level and age.</summary>
    private void SeedRecord(Boxer b)
    {
        int careerLength = Math.Max(0, b.Age - 18);
        int fights = (int)Math.Round(careerLength * (1.5 + _rng.NextDouble() * 2.0));
        double winRate = 0.35 + (b.Overall - 50) / 120.0; // better fighters win more
        winRate = Math.Clamp(winRate, 0.2, 0.92);

        for (int i = 0; i < fights; i++)
        {
            double roll = _rng.NextDouble();
            if (roll < winRate)
            {
                b.Record.Wins++;
                if (_rng.NextDouble() < Ratings.KnockoutChance(b.Ratings.Power, 72, b.Overall - 64)) b.Record.KnockoutWins++;
            }
            else if (roll < winRate + 0.10)
            {
                b.Record.Draws++;
            }
            else
            {
                b.Record.Losses++;
                if (_rng.NextDouble() < 0.25) b.Record.KnockoutLosses++;
            }
        }
    }
}
