using BoxingSim.Core.Generation;
using BoxingSim.Core.Model;

namespace BoxingSim.Core.League;

/// <summary>The whole boxing world: every division, the calendar and the fighter pool.</summary>
public sealed class World
{
    private readonly Random _rng;
    public BoxerFactory Factory { get; }
    public CareerProgression Careers { get; }

    public int Year { get; set; } = 1;
    public IReadOnlyDictionary<WeightClass, Division> Divisions { get; }

    public World(Random rng)
    {
        _rng = rng;
        Factory = new BoxerFactory(rng);
        Careers = new CareerProgression(rng);
        var divisions = new Dictionary<WeightClass, Division>();
        foreach (var wc in WeightClasses.All)
            divisions[wc] = new Division(wc);
        Divisions = divisions;
    }

    public IEnumerable<Boxer> AllBoxers => Divisions.Values.SelectMany(d => d.Boxers);
    public int ActiveCount => Divisions.Values.Sum(d => d.Active.Count());

    /// <summary>Stand up a fresh world of <paramref name="total"/> fighters and crown champions.</summary>
    public void Seed(int total)
    {
        int perDivision = total / WeightClasses.All.Length;
        int remainder = total % WeightClasses.All.Length;

        foreach (var wc in WeightClasses.All)
        {
            int count = perDivision + (remainder-- > 0 ? 1 : 0);
            var div = Divisions[wc];
            for (int i = 0; i < count; i++)
            {
                var b = Factory.CreateExisting(wc);
                SeedRankPoints(b);
                div.Add(b);
            }
            // The best fighter in the room starts as champion.
            div.Champion = div.Ranked().FirstOrDefault();
            if (div.Champion is not null) div.Champion.IsChampion = true;
        }
    }

    /// <summary>Add fresh prospects spread across divisions to backfill the roster.</summary>
    public void InjectProspects(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var wc = WeightClasses.All[_rng.Next(WeightClasses.All.Length)];
            var b = Factory.CreateProspect(wc);
            SeedRankPoints(b);
            Divisions[wc].Add(b);
        }
    }

    /// <summary>What a fighter's standing is worth on ability alone. This is the spine of the ratings and the
    /// anchor a career's ranking points are pulled back toward, so the ratings track how good a man IS rather
    /// than how many times he's boxed.</summary>
    public static double AbilityAnchor(int overall) => 1000 + (overall - 50) * 16.0;

    /// <summary>Initial ranking points track a fighter's headline ability with a little noise.</summary>
    public static void SeedRankPoints(Boxer b) =>
        b.RankPoints = AbilityAnchor(b.Overall);
}
