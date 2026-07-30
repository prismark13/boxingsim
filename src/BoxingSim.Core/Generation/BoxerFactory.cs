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
        double decline = (age - peakAge) * 0.045; // ~4.5% of potential lost per year past peak — a fading champion turns beatable sooner
        return Math.Max(0.45, 1.0 - decline);
    }

    /// <summary>A raw 18–21 year old: low current ability, the ceiling still ahead of them.
    /// <paramref name="maxPotential"/> caps the ceiling — generated filler stays journeyman-class.</summary>
    public Boxer CreateProspect(WeightClass wc, int maxPotential = 100, int year = 0)
    {
        int age = _rng.Next(18, 22);
        int potential = Math.Min(maxPotential, RollPotential());
        int peak = _rng.Next(26, 31);
        var b = Build(wc, age, peak, potential, year);
        return b;
    }

    /// <summary>A fighter somewhere in their career, with a record to match their level and age.</summary>
    public Boxer CreateExisting(WeightClass wc, int maxPotential = 100, int year = 0)
    {
        int age = _rng.Next(19, 37);
        int potential = Math.Min(maxPotential, RollPotential());
        int peak = _rng.Next(26, 31);
        var b = Build(wc, age, peak, potential, year);
        SeedRecord(b);
        return b;
    }

    private Boxer Build(WeightClass wc, int age, int peakAge, int potential, int year = 0)
    {
        double dev = Development(age, peakAge);
        bool young = age <= peakAge;

        // The shape of him — which attributes arrive with a young fighter and which he has to build — lives
        // in FighterShape, because the player is built from the same shape and the two used to drift apart.
        var r = FighterShape.Compose(_rng, potential, dev, young,
                                     FighterShape.GeneratedSpreads, FighterShape.GeneratedFloors);

        // The country comes FIRST and the name follows from it. It used to be the other way about - a name
        // drawn at random from every culture at once, then a country drawn independently - which is how the
        // sim produced men called Tomasz Ramirez boxing out of Argentina.
        string country = Countries[_rng.Next(Countries.Length)];
        string name = _names.Next(country, year > 0 ? year - age : 0);
        return new Boxer
        {
            Id = _nextId++,
            Name = name,
            Country = country,
            WeightClass = wc,
            Ratings = r,
            Reach = Physique.ReachInchesFor(wc, name),
            Age = age,
            PeakAge = peakAge,
            Potential = potential
        };
    }

    /// <summary>A generated fighter's ceiling — where he tops out if his career runs its course.
    ///
    /// Invented men are gatekeepers at best. The contender, champion and all-time-great tiers belong to the
    /// real roster and to the player, and a fighter the sim made up out of a first-name list and a surname
    /// list does not take a world title off Carlos Ortiz.
    ///
    /// The proportions are what a division looks like from underneath: four men in five are there to lose,
    /// and the remaining fifth are a real night's work without ever being more than that.
    ///
    /// The cost of this is real and worth stating plainly, because it has been measured. A world outlives
    /// its real fighters: thirty years into a universe they have all retired, and if nothing generated can
    /// rise above a gatekeeper then the belts end up on gatekeepers. The sport does not fall over — the
    /// rankings, the titles and the matchmaking all still work — but its ceiling drops away with the last
    /// of the historical roster. That is the trade being made here deliberately.</summary>
    private int RollPotential()
    {
        double r = _rng.NextDouble();
        //  share   ceiling    what he becomes
        int p = r < 0.80 ? _rng.Next(42, 68)    // journeyman, opponent   class 1-3
                         : _rng.Next(68, 76);   // gatekeeper             class 4-5
        return Ratings.Clamp(p);
    }

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
