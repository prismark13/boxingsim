using BoxingSim.Core.Engine;
using BoxingSim.Core.Generation;
using BoxingSim.Core.League;
using BoxingSim.Core.Model;

namespace BoxingSim.Core.Career;

/// <summary>Where fighters come from and what time does to them: the historical roster, the career arc,
/// ageing, retirement and the Hall of Fame.</summary>
public sealed partial class CareerGame
{
    // ---- historical seeding & aging ----

    private void InjectHistorical(Boxer proto, int ageNow, int debutAge, int peak, bool announce)
    {
        var prime = proto.Ratings.Clone();
        var b = new Boxer
        {
            Id = proto.Id,
            Name = proto.Name,
            Nickname = proto.Nickname,
            WeightClass = proto.WeightClass,
            TopWeight = proto.TopWeight,
            Country = proto.Country,
            DateOfBirth = proto.DateOfBirth,
            DebutYear = proto.DebutYear,
            Ratings = prime.Clone(),
            Age = ageNow,
            PeakAge = peak,
            Potential = proto.Overall
        };
        AgeHistorical(b, prime, peak);                              // set ratings to the right point on their arc
        SeedRecordFor(b, Math.Max(0, ageNow - debutAge));
        CapStarter(b);                                              // a debuting great is still just a starter
        World.SeedRankPoints(b);
        _historical[b.Id] = (prime, peak);
        AddActive(b);
        if (announce) LogEvent($"{b.Name} ({b.Country}) turns pro.", kind: "debut", div: b.WeightClass);
    }

    /// <summary>What a fighter was at each point of his career, so a card can show the arc rather than only
    /// today's snapshot. A 34-year-old ex-champion's current ratings say nothing about the fighter who won the
    /// title at 26, and that man is the one worth looking at.
    ///
    /// For anyone drawn from the real roster this costs nothing to produce: their ratings are a pure function
    /// of age against a stored prime, so any age on the arc can simply be evaluated. Fighters invented inside
    /// the save develop randomly year to year and cannot be rewound, so they have no arc to show - the player
    /// is the exception, because his own is recorded as he lives it.</summary>
    public IReadOnlyList<StagePoint> CareerArc(Boxer b)
    {
        int now = CareerMileage.Fights(b);
        var points = new List<StagePoint>();

        if (b.Id == Player.Id)
        {
            foreach (var (fights, age, r) in _playerArc.OrderBy(x => x.Fights))
                points.Add(new StagePoint(StageName(StageAtFights(b, fights)), fights, age, r, false));
        }
        else if (_historical.TryGetValue(b.Id, out var h))
        {
            // Probe his arc at the end of each stage. The curve is a pure function of mileage, so any point on
            // it can simply be evaluated - no history has to be stored for anyone off the real roster.
            foreach (int at in new[] { CareerMileage.StarterUntil(b), CareerMileage.PrePrimeUntil(b),
                                       (CareerMileage.PrePrimeUntil(b) + CareerMileage.PrimeUntil(b)) / 2,
                                       CareerMileage.PrimeUntil(b), CareerMileage.PostPrimeUntil(b) })
            {
                if (at <= 0 || at >= now) continue;   // not reached yet
                var was = new Ratings();
                PlaceOnArc(was, h.Prime, DevelopmentAt(b, at), at <= CareerMileage.PrimeUntil(b));
                points.Add(new StagePoint(StageName(StageAtFights(b, at)), at, 0, was, false));
            }
        }
        else return points;   // invented inside the save and not the player: nothing to reconstruct

        // Where he is today always closes the arc.
        points.Add(new StagePoint(StageName(CareerStages.Of(b)), now, b.Age, b.Ratings, true));
        return points.GroupBy(p => p.Fights).Select(g => g.Last()).OrderBy(p => p.Fights).ToList();
    }

    /// <summary>The stage a given fighter was in at a given fight count, using HIS boundaries.</summary>
    private static CareerStage StageAtFights(Boxer b, int fights) =>
        fights <= CareerMileage.StarterUntil(b) ? CareerStage.Starter :
        fights <= CareerMileage.PrePrimeUntil(b) ? CareerStage.PrePrime :
        fights <= CareerMileage.PrimeUntil(b) ? CareerStage.Prime :
        fights <= CareerMileage.PostPrimeUntil(b) ? CareerStage.PostPrime : CareerStage.End;

    private static string StageName(CareerStage s) => s switch
    {
        CareerStage.Starter => "Starter",
        CareerStage.PrePrime => "Pre-prime",
        CareerStage.Prime => "Prime",
        CareerStage.PostPrime => "Post-prime",
        _ => "Veteran"
    };

    // The player's own arc. His development is random year to year and cannot be recomputed, so it is recorded
    // as he lives it - keyed on the mileage he had at the time, which is what the stages are measured in.
    private readonly List<(int Fights, int Age, Ratings R)> _playerArc = new();

    /// <param name="peak">Kept for the seeding path, which positions a man on his arc before he has a record.
    /// Once he is in the world his place on it is set by his mileage like everybody else's.</param>
    private static void AgeHistorical(Boxer b, Ratings prime, int peak)
    {
        // Fights, not birthdays. A roster fighter who is not being matched does not decay on the calendar.
        double dev = CareerMileage.Fights(b) > 0
            ? CareerMileage.Development(b)
            : BoxerFactory.Development(b.Age, peak);
        PlaceOnArc(b.Ratings, prime, dev, CareerMileage.PastPrime(b) <= 0);
    }

    /// <summary>Write a fighter's ratings for a given point on his arc. Split out from <see cref="AgeHistorical"/>
    /// so the arc can be evaluated at a mileage the man is not currently at, WITHOUT building a stand-in boxer:
    /// a shallow clone shares his record object, and writing a probe mileage into it would corrupt the real
    /// fighter's record.</summary>
    private static void PlaceOnArc(Ratings r, Ratings prime, double dev, bool young)
    {
        // Young: power/defence/speed are near their ceiling already. Old: they decline normally.
        r.Power = Scale(prime.Power, young ? Lerp(dev, 0.85) : dev);
        r.Speed = Scale(prime.Speed, young ? Lerp(dev, 0.82) : dev);
        r.Defense = Scale(prime.Defense, Lerp(dev, young ? 0.72 : 0.55));
        r.Accuracy = Scale(prime.Accuracy, Lerp(dev, young ? 0.58 : 0.6));
        r.Stamina = Scale(prime.Stamina, dev);
        r.Conditioning = Scale(prime.Conditioning, dev);
        r.Chin = Scale(prime.Chin, Lerp(dev, 0.78));
        r.CutResistance = prime.CutResistance;
        r.Aggression = prime.Aggression;
        r.Heart = prime.Heart;
    }

    /// <summary>The development factor a fighter would have at a given mileage, without touching him.</summary>
    private static double DevelopmentAt(Boxer b, int fights)
    {
        int primeAt = CareerMileage.PrePrimeUntil(b);
        if (fights <= primeAt)
        {
            double t = primeAt <= 0 ? 1 : fights / (double)primeAt;
            return 0.55 + 0.45 * Math.Clamp(t, 0, 1);
        }
        return Math.Max(0.45, 1.0 - Math.Max(0, fights - CareerMileage.PrimeUntil(b)) * 0.010);
    }

    private void SeedRecordFor(Boxer b, int yearsActive)
    {
        int fights = (int)Math.Round(yearsActive * (2.0 + _rng.NextDouble() * 2.0));
        // Win rate reflects his true class (ceiling), not his half-formed current rating — so a future
        // great's early record is a run of wins, not a string of upsets against journeymen.
        int cls = Math.Max(b.Overall, b.Potential);
        double winRate = Math.Clamp(0.5 + (cls - 60) / 90.0, 0.4, 0.97);
        int span = Math.Max(30, (int)(yearsActive * 365));   // his pre-sim years, so bouts can be dated
        for (int i = 0; i < fights; i++)
        {
            // Record the result AND a dated ledger line vs a journeyman, oldest first, so the fight
            // history matches the win-loss record instead of starting blank at his sim debut.
            var when = b.DebutYear is int dy
                ? new DateOnly(Math.Clamp(dy + i / 3, dy, Date.Year - 1), 1 + _rng.Next(12), 1 + _rng.Next(28))
                : Date.AddDays(-span + (int)((i + 1.0) / (fights + 1) * span));
            string opp = _oppNames.Next();
            double roll = _rng.NextDouble();
            char rc; string method; int round = 0;
            if (roll < winRate)
            {
                b.Record.Wins++; rc = 'W';
                // The column follows the method rather than defaulting to KO: a stoppage that reads "TKO" in
                // the ledger must not turn up in the KO count, or a man's record contradicts his own history.
                if (_rng.NextDouble() < Ratings.KnockoutChance(b.Ratings.Power, 72, b.Overall - 64))
                {
                    method = _rng.NextDouble() < 0.5 ? "KO" : "TKO";
                    if (method == "KO") b.Record.KnockoutWins++; else b.Record.TechnicalKnockoutWins++;
                    round = 1 + _rng.Next(8);
                }
                else method = _rng.NextDouble() < 0.75 ? "UD" : "SD";
            }
            else if (roll < winRate + 0.08) { b.Record.Draws++; rc = 'D'; method = "D"; }
            else
            {
                b.Record.Losses++; rc = 'L';
                if (_rng.NextDouble() < 0.2) { b.Record.TechnicalKnockoutLosses++; method = "TKO"; round = 1 + _rng.Next(8); }
                else method = _rng.NextDouble() < 0.75 ? "UD" : "SD";
            }
            b.History.Add(new BoutLine { Date = when, Opponent = opp, Result = rc, Method = method, Round = round });
        }
    }

}
