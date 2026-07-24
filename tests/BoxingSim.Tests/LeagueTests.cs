using BoxingSim.Core.League;
using BoxingSim.Core.Model;
using Xunit;

namespace BoxingSim.Tests;

public class LeagueTests
{
    [Fact]
    public void SeedingFillsEveryDivisionAndCrownsChampions()
    {
        var world = new World(new Random(5));
        world.Seed(800);

        Assert.Equal(800, world.ActiveCount);
        foreach (var wc in WeightClasses.All)
            Assert.NotNull(world.Divisions[wc].Champion);
    }

    [Fact]
    public void RosterStaysNearTargetAcrossSeasons()
    {
        var rng = new Random(5);
        var world = new World(rng);
        world.Seed(400);
        var sim = new SeasonSimulator(world, rng);

        for (int i = 0; i < 15; i++)
            sim.RunSeason(400);

        Assert.Equal(400, world.ActiveCount);
        Assert.Equal(16, world.Year); // year advances each season
    }

    [Fact]
    public void SeasonsProduceFightsAndChampionsPersist()
    {
        var rng = new Random(8);
        var world = new World(rng);
        world.Seed(240);
        var sim = new SeasonSimulator(world, rng);

        var report = sim.RunSeason(240);

        Assert.True(report.TotalFights > 0);
        // Every division should still resolve to a champion after the season.
        foreach (var wc in WeightClasses.All)
            Assert.NotNull(world.Divisions[wc].Champion);
    }
}
