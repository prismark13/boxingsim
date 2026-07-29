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

        // What a young fighter has, and what he has not.
        //
        // He has his engine, his legs and his chin — those are physical and they arrive with him. What he
        // lacks is placement, ring craft and defence: the things that take rounds to learn. That is the
        // right way round, and it was the wrong way round here. Stamina and conditioning were the only two
        // attributes scaled by the bare development curve with no floor under them, so at eighteen a man
        // sat at 59% of his engine while his power was at 94% of his ceiling — and since anything under 48
        // shows as 1 on the 1-15 scale, 73% of all prospects had a tale of the tape reading STAMINA 1.
        // Every other attribute read 3 to 5. In his prime the same man reads 4.
        //
        // A raw eighteen-year-old is not a man with no gas tank. He is a man who does not know how to pace
        // himself, which is a different thing and is already modelled elsewhere — the engine reads work rate
        // and aggression, not stamina alone. Conditioning stays a little lower than stamina: he has the
        // lungs, but he has not had the camps. Past the peak both still erode on the bare curve, because
        // the engine is the first thing a fighter loses.
        var r = new Ratings
        {
            Power = Scale(Ceiling(18), young ? Lerp(dev, 1.0, 0.85) : dev),
            Speed = Scale(Ceiling(14), young ? Lerp(dev, 1.0, 0.82) : dev),
            Defense = Scale(Ceiling(15), Lerp(dev, 1.0, young ? 0.72 : 0.4)),
            Accuracy = Scale(Ceiling(14), Lerp(dev, 1.0, 0.5)),
            Stamina = Scale(Ceiling(14), young ? Lerp(dev, 1.0, 0.84) : dev),
            Conditioning = Scale(Ceiling(14), young ? Lerp(dev, 1.0, 0.75) : dev),
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

    /// <summary>A generated fighter's ceiling — where he tops out if his career runs its course.
    ///
    /// This used to be the minimum of two rolls in the 34–55 band, on the principle that "the contender,
    /// champion and elite tiers belong to the real fighters and the player". The effect was that no
    /// generated man could ever be better than a club fighter: measured in a running world, their median
    /// ceiling was 40 and their best was 55, against a median of 74 and a maximum of 99 across the real
    /// roster. Every generated fighter alive in the sport — 88% of it — sat in the bottom three classes.
    ///
    /// That is what breaks a career after a dozen fights and a universe after a decade. The matchmaker
    /// looks for a credible opponent, finds that the entire generated population is club-standard, and so
    /// reaches past it for the few real men and for other people's prospects. A world outlives its real
    /// fighters, and the belts end up on men rated 49.
    ///
    /// A sport needs the whole ladder, so the roll now spans it. The proportions are what a division looks
    /// like from underneath: mostly men who are there to lose, a solid fifth who are a real test without
    /// ever being champion, and a thinning tail that reaches the top. The all-time-great band is left to
    /// the real roster — a generated man can win titles, but he does not become Robinson.</summary>
    private int RollPotential()
    {
        double r = _rng.NextDouble();
        //  share   ceiling    what he becomes
        int p = r < 0.55 ? _rng.Next(42, 68)    // journeyman, opponent            class 1-3
              : r < 0.75 ? _rng.Next(68, 77)    // gatekeeper - a real night's work class 4-5
              : r < 0.90 ? _rng.Next(77, 83)    // contender                       class 6-8
              : r < 0.98 ? _rng.Next(83, 88)    // champion calibre                class 9-11
                         : _rng.Next(88, 92);   // a great, rarely                 class 12-13
        return Ratings.Clamp(p);
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
