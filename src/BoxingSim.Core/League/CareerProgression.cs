using BoxingSim.Core.Model;

namespace BoxingSim.Core.Generation;

/// <summary>Handles year-over-year aging of fighters and retirement decisions.</summary>
public sealed class CareerProgression
{
    private readonly Random _rng;

    public CareerProgression(Random rng) => _rng = rng;

    /// <summary>Advance a fighter one year: improve a developing prospect, decline a veteran.</summary>
    public void AdvanceOneYear(Boxer b)
    {
        b.Age++;
        var r = b.Ratings;

        if (b.Age <= b.PeakAge)
        {
            // Climbing toward the ceiling — the further below potential, the faster the growth.
            double room = Math.Max(0, b.Potential - r.Overall);
            double growth = room * 0.18;
            Bump(r, physical: growth, skill: growth * 1.1, innate: growth * 0.4);
        }
        else
        {
            // Past the peak. Athleticism erodes first and fastest; ring craft lingers.
            int yearsPast = b.Age - b.PeakAge;
            double decl = 0.8 + yearsPast * 0.45;
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

    public bool ShouldRetire(Boxer b)
    {
        if (b.Age >= 42) return true;
        double chance = 0;
        if (b.Age >= 40) chance += 0.55;
        else if (b.Age >= 37) chance += 0.25;
        else if (b.Age >= 34) chance += 0.08;

        // Faded fighters and those taking sustained punishment hang them up sooner — but only once
        // they're past their physical prime; a young fighter rebuilds rather than retires.
        if (b.Age >= 31)
        {
            if (b.Overall < 40) chance += 0.20;
            if (b.Record.KnockoutLosses >= 4) chance += 0.15;
        }

        return _rng.NextDouble() < chance;
    }

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
