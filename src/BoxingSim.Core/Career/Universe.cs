using BoxingSim.Core.Generation;
using BoxingSim.Core.Model;

namespace BoxingSim.Core.Career;

/// <summary>A week's fights in one place, the way a boxing week actually reads.</summary>
public sealed record RegionCard(string Region, IReadOnlyList<CountryCard> Countries)
{
    public int Bouts => Countries.Sum(c => c.Bouts.Count);
    public int TitleBouts => Countries.Sum(c => c.Bouts.Count(b => b.IsTitle));
}

public sealed record CountryCard(string Country, IReadOnlyList<WorldBout> Bouts);

/// <summary>A sport with nobody playing it.
///
/// Career mode is one fighter's life and the world exists around him — it advances a fortnight at a time
/// between his bouts, and it stops when he retires. A universe is the other way round: the world IS the
/// subject. It runs a week at a time, every card everywhere is reported, and there is no prospect to follow,
/// so nothing is fixed to suit one man's story.
///
/// It is the same engine underneath. Divisions, titles, rankings, retirements, awards and the Hall of Fame all
/// work exactly as they do in a career — what changes is that the dials are yours: how many turn professional
/// each year, how long careers run, how often men fight.</summary>
public sealed class Universe
{
    private readonly CareerGame _world;
    private readonly Random _rng;

    public UniverseSettings Settings { get; }
    public DateOnly Date => _world.Date;
    public int Week { get; private set; }

    /// <summary>The world underneath, for the rankings, champions, hall of fame and news the UI already reads.</summary>
    public CareerGame World => _world;

    public Universe(UniverseSettings settings, IEnumerable<Boxer> roster)
    {
        Settings = settings;
        _rng = new Random(settings.Seed == 0 ? Environment.TickCount : settings.Seed);

        // The dials are process-wide because the mileage rules are pure functions of a fighter; set them before
        // anything is built so the warm-up years already obey this world's rules rather than the default sim's.
        CareerMileage.LengthScale = Math.Clamp(settings.CareerLength, 0.3, 3.0);
        CareerMileage.ActivityScale = Math.Clamp(settings.Activity, 0.3, 3.0);

        // A universe has no player. One is still needed to build the world - the whole sim is written around
        // there being one - so a placeholder is made and retired before the first week, which takes him out of
        // every pool, ranking and card. Nothing ever offers him a fight because nothing ever asks.
        // He is retired before anything runs, but he still has to belong to a division this world has, or the
        // sim is holding a man in a weight class that does not exist here.
        var home = settings.Divisions.Count > 0 ? settings.Divisions[0] : WeightClass.Heavyweight;
        var ghost = CareerGame.CreatePlayer(_rng, "—", "USA", home, 50);
        ghost.Retired = true;

        var protos = settings.UseRealFighters ? roster : Array.Empty<Boxer>();
        _world = new CareerGame(settings.StartYear, ghost, protos, _rng,
                                home, warmupYears: settings.WarmupYears,
                                seedHistory: settings.WarmupYears > 0,
                                universe: settings);
        ghost.Retired = true;      // seeding may have revived him; he stays out
        _world.WatchBouts();
    }

    /// <summary>Run one week and hand back its cards, grouped by region and then by country — which is how a
    /// week of boxing is actually followed: what happened in Britain, what happened in Mexico.</summary>
    public IReadOnlyList<RegionCard> PlayWeek()
    {
        Week++;
        _world.AdvanceWorld(7);
        var bouts = _world.DrainBouts();
        if (Settings.Divisions.Count > 0)
            bouts = bouts.Where(b => Settings.Divisions.Contains(b.Division)).ToList();

        return bouts
            .GroupBy(b => b.Region)
            .Select(g => new RegionCard(
                g.Key,
                g.GroupBy(b => string.IsNullOrEmpty(b.Country) ? "—" : b.Country)
                 .Select(c => new CountryCard(c.Key,
                     c.OrderByDescending(x => x.IsTitle).ThenByDescending(x => x.Tag is not null)
                      .ThenBy(x => x.Division).ToList()))
                 .OrderByDescending(c => c.Bouts.Count(x => x.IsTitle)).ThenByDescending(c => c.Bouts.Count)
                 .ToList()))
            .OrderByDescending(r => r.TitleBouts).ThenByDescending(r => r.Bouts)
            .ToList();
    }

    /// <summary>Put the process-wide dials back so a career started afterwards behaves normally.</summary>
    public static void Release() => CareerMileage.ResetScales();
}
