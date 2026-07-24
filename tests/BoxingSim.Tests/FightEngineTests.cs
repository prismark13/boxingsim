using BoxingSim.Core.Engine;
using BoxingSim.Core.Model;
using Xunit;

namespace BoxingSim.Tests;

public class FightEngineTests
{
    private static Boxer Make(string name, int level, WeightClass wc = WeightClass.Middleweight) => new()
    {
        Id = name.GetHashCode(),
        Name = name,
        WeightClass = wc,
        Ratings = new Ratings
        {
            Power = level, Chin = level, Speed = level, Defense = level, Stamina = level,
            Accuracy = level, Conditioning = level, CutResistance = level, Aggression = level, Heart = level
        },
        Age = 28
    };

    [Fact]
    public void StrongFighterUsuallyBeatsWeakFighter()
    {
        var rng = new Random(1);
        var engine = new FightEngine(rng);
        var strong = Make("Strong", 88);
        var weak = Make("Weak", 45);

        int strongWins = 0, total = 300;
        for (int i = 0; i < total; i++)
        {
            var res = engine.Simulate(strong, weak, 10);
            if (res.Winner?.Id == strong.Id) strongWins++;
        }

        Assert.True(strongWins > total * 0.8, $"Strong won only {strongWins}/{total}");
    }

    [Fact]
    public void EvenlyMatchedFightersSplitRoughlyEvenly()
    {
        var rng = new Random(7);
        var engine = new FightEngine(rng);
        var a = Make("A", 70);
        var b = Make("B", 70);

        int aWins = 0, bWins = 0, total = 400;
        for (int i = 0; i < total; i++)
        {
            var res = engine.Simulate(a, b, 12);
            if (res.Winner?.Id == a.Id) aWins++;
            else if (res.Winner?.Id == b.Id) bWins++;
        }

        // Neither side should dominate a coin-flip matchup.
        Assert.InRange(aWins, total * 0.30, total * 0.70);
        Assert.InRange(bWins, total * 0.30, total * 0.70);
    }

    [Fact]
    public void ResultNeverEndsAfterScheduledRounds()
    {
        var rng = new Random(3);
        var engine = new FightEngine(rng);
        var a = Make("A", 75);
        var b = Make("B", 60);

        for (int i = 0; i < 100; i++)
        {
            var res = engine.Simulate(a, b, 12);
            Assert.True(res.EndRound >= 1 && res.EndRound <= 12);
            if (res.Outcome == FightOutcome.Decision)
                Assert.Equal(3, res.Scorecards.Count);
        }
    }

    [Fact]
    public void HeavyweightsScoreMoreKnockoutsThanFlyweights()
    {
        var rng = new Random(11);
        var engine = new FightEngine(rng);

        int HwKos = CountKos(engine, WeightClass.Heavyweight);
        int FlyKos = CountKos(engine, WeightClass.Flyweight);

        Assert.True(HwKos > FlyKos, $"Heavyweight KOs {HwKos} should exceed flyweight KOs {FlyKos}");
    }

    private static int CountKos(FightEngine engine, WeightClass wc)
    {
        int kos = 0;
        var a = Make("Banger", 80, wc);
        var b = Make("Target", 70, wc);
        for (int i = 0; i < 300; i++)
        {
            var res = engine.Simulate(a, b, 12);
            if (res.Outcome is FightOutcome.Knockout or FightOutcome.TechnicalKnockout) kos++;
        }
        return kos;
    }

    [Fact]
    public void SameSeedProducesIdenticalResult()
    {
        var a = Make("A", 72);
        var b = Make("B", 68);

        var r1 = new FightEngine(new Random(99)).Simulate(a, b, 12);
        var r2 = new FightEngine(new Random(99)).Simulate(a, b, 12);

        Assert.Equal(r1.Outcome, r2.Outcome);
        Assert.Equal(r1.EndRound, r2.EndRound);
        Assert.Equal(r1.Winner?.Id, r2.Winner?.Id);
    }
}
