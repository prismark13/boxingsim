using BoxingSim.Core.Generation;
using BoxingSim.Core.Model;

namespace BoxingSim.Core.Generation;

/// <summary>Handles year-over-year aging of fighters and retirement decisions.</summary>
public sealed class CareerProgression
{
    private readonly Random _rng;

    public CareerProgression(Random rng) => _rng = rng;

    /// <summary>Advance a fighter one year: improve a developing prospect, decline a worn one.
    ///
    /// What decides which is his MILEAGE, not his age. A fighter still short of his prime keeps improving
    /// however old he is, and one who has had sixty hard fights is on the slide at twenty-eight. That is the
    /// way round it works: a man is finished by what he has taken, not by how long he has been alive.</summary>
    public void AdvanceOneYear(Boxer b)
    {
        b.Age++;
        var r = b.Ratings;
        int pastPrime = CareerMileage.PastPrime(b);

        if (pastPrime <= 0)
        {
            // Climbing toward the ceiling — the further below potential, the faster the growth.
            double room = Math.Max(0, b.Potential - r.Overall);
            double growth = room * 0.18;
            Bump(r, physical: growth, skill: growth * 1.1, innate: growth * 0.4);
        }
        else
        {
            // Past his best. Athleticism erodes first and fastest; ring craft lingers. The rate is set by how
            // far past his prime the mileage has taken him, so a busy fighter falls away faster than a careful
            // one of the same age.
            double decl = 0.8 + pastPrime * 0.09;
            r.Speed = Drop(r.Speed, decl * 1.3);
            r.Stamina = Drop(r.Stamina, decl * 1.1);
            r.Power = Drop(r.Power, decl * 0.8);
            r.Conditioning = Drop(r.Conditioning, decl * 1.0);
            r.Chin = Drop(r.Chin, decl * 0.6);            // chins go late in a career
            r.Defense = Drop(r.Defense, decl * 0.5);      // experience offsets some loss
            r.Accuracy = Drop(r.Accuracy, decl * 0.5);
        }
    }

    /// <summary>A hard knockout loss leaves a mark — shave the chin a little, permanently.</summary>
    public void RegisterKnockoutLoss(Boxer b)
    {
        b.Ratings.Chin = Ratings.Clamp(b.Ratings.Chin - _rng.Next(1, 4));
    }

    /// <summary>Whether he hangs them up. Careers end because a man has had enough fights, not because he has
    /// had enough birthdays — so this counts bouts. Nobody's career is cut short below the minimum unless an
    /// injury ends it, and nobody goes past his own limit.</summary>
    /// <param name="holdsAWorldBelt">Whether he is a world champion RIGHT NOW — passed in, because a Boxer
    /// cannot tell you. Boxer.IsChampion is set for the primary belt only, so a WBC or IBF holder reads as an
    /// ordinary fighter: two of the three champions in every division were invisible to the one clause meant
    /// to keep a champion in the sport, and they were retiring out of it mid-reign as a result.</param>
    public bool ShouldRetire(Boxer b, bool holdsAWorldBelt = false)
    {
        int fights = CareerMileage.Fights(b);
        if (fights >= CareerMileage.CareerLimit(b)) return true;
        if (fights < CareerMileage.MinimumCareer) return false;

        // Most fighters do not box on until they are finished - they drift out of the sport somewhere in the
        // middle of a career, and that drift is what sets the typical length. Retirement pressure therefore
        // starts building from the minimum rather than waiting for a man to be visibly shot.
        double chance = 0;
        int overMin = fights - CareerMileage.MinimumCareer;
        if (overMin > 0) chance += (0.015 + overMin * 0.006) * DriftRelief(b, holdsAWorldBelt);
        int worn = fights - CareerMileage.PostPrimeUntil(b);
        // Visibly shot is visibly shot. No standing buys this off, or the great ones never leave.
        if (worn > 0) chance += 0.10 + worn * 0.05;
        else if (CareerMileage.PastPrime(b) > 0) chance += 0.05 * DriftRelief(b, holdsAWorldBelt);

        // A faded fighter, or one who has been stopped repeatedly, goes sooner.
        if (b.Overall < 40) chance += 0.18;
        if (b.Record.StoppageLosses >= 4) chance += 0.14;   // stopped is stopped, cut or counted out
        // And nobody boxes into their forties whatever the mileage says.
        if (b.Age >= 40) chance += 0.45;

        return _rng.NextDouble() < chance;
    }

    /// <summary>How much of the DRIFT a man's standing buys off.
    ///
    /// The drift term models a fighter quietly ceasing to get calls, which is how most careers actually end and
    /// is right for most of a roster. It was applied to everyone equally, and that is wrong at the top: a man
    /// the whole sport wants to see does not stop getting offers. It retired Tony Zale in 1943 — unbeaten at
    /// 33-0, rated 96, holding the world middleweight title with five defences, in the middle of a prime the
    /// roster documents as 1940-1948 — on a 4.5% roll, one fight after he had stepped up a division. Then it
    /// did the same to the next one, which is why moving up looked like a death sentence: you notice it when it
    /// takes somebody you were watching.
    ///
    /// Only the drift. Mileage, a fighter falling apart, repeated knockout losses and the forties all still
    /// count in full, because those are reasons to stop that being famous does not answer.</summary>
    private static double DriftRelief(Boxer b, bool holdsAWorldBelt) =>
        holdsAWorldBelt || b.IsChampion ? 0.15    // holding a world belt: he is not going anywhere
        : b.Overall >= 88 ? 0.30
        : b.Overall >= 80 ? 0.55
        : 1.0;                  // everybody else drifts out the way they always did

    private void Bump(Ratings r, double physical, double skill, double innate)
    {
        r.Power = Up(r.Power, physical);
        r.Speed = Up(r.Speed, physical);
        r.Stamina = Up(r.Stamina, physical);
        r.Conditioning = Up(r.Conditioning, physical);
        r.Defense = Up(r.Defense, skill);
        r.Accuracy = Up(r.Accuracy, skill);
        r.Chin = Up(r.Chin, innate);
        r.Heart = Up(r.Heart, innate * 0.5);
    }

    private int Up(int v, double amount)
    {
        double noisy = amount * (0.6 + _rng.NextDouble() * 0.8);
        return Ratings.Clamp(v + (int)Math.Round(noisy));
    }

    private int Drop(int v, double amount)
    {
        double noisy = amount * (0.6 + _rng.NextDouble() * 0.8);
        return Ratings.Clamp(v - (int)Math.Round(noisy));
    }
}
