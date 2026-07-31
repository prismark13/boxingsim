using System;
using System.Linq;
using BoxingSim.Core.League;
using BoxingSim.Core.Model;
using Xunit;

namespace BoxingSim.Tests;

/// <summary>How big a fight is — the orderings that have to hold, whatever the numbers are tuned to.
///
/// The constants in BoutValue are a judgement and will be argued with. These are not about the constants;
/// they are about the RELATIONS that have to survive any retuning. If one of these breaks, a card will be
/// ordered wrongly in a way a boxing person would notice immediately.</summary>
public class BoutValueTests
{
    private static Boxer Man(int overall, int wins = 20, int losses = 3, int streak = 0,
                             WeightClass wc = WeightClass.Middleweight)
    {
        var b = new Boxer
        {
            Id = overall * 1000 + wins + losses,
            Name = $"Fighter {overall}/{wins}-{losses}",
            WeightClass = wc,
            Age = 27,
            Ratings = Flat(overall),
        };
        b.Record.Wins = wins; b.Record.Losses = losses;
        for (int i = 0; i < streak; i++)
            b.History.Add(new BoutLine { Opponent = "x", Result = 'W', Method = "UD", Date = new DateOnly(1970, 1, 1) });
        return b;
    }

    /// <summary>A rating profile that lands on the wanted Overall, so the tests can talk about "an 85".</summary>
    private static Ratings Flat(int v)
    {
        var r = new Ratings
        {
            Power = v, Chin = v, Speed = v, Defense = v, Stamina = v,
            Accuracy = v, Conditioning = v, Aggression = v, Heart = v, CutResistance = v,
        };
        return r;
    }

    private static double V(Boxer a, Boxer b, BoutStakes s, WeightClass at, int ra = 0, int rb = 0, bool grudge = false)
        => BoutValue.Of(a, b, s, at, ra, rb, grudge);

    /// <summary>The decision the whole scale turns on: stakes beat poundage. Get this wrong and a flyweight
    /// cannot headline his own world title night because a heavyweight six-rounder outranks it.</summary>
    [Fact]
    public void AFlyweightWorldTitleOutranksAHeavyweightSixRounder()
    {
        var flyA = Man(78, wc: WeightClass.Flyweight);
        var flyB = Man(76, wc: WeightClass.Flyweight);
        var heavyA = Man(58, wc: WeightClass.Heavyweight);
        var heavyB = Man(56, wc: WeightClass.Heavyweight);

        Assert.True(V(flyA, flyB, BoutStakes.WorldTitle, WeightClass.Flyweight)
                  > V(heavyA, heavyB, BoutStakes.None, WeightClass.Heavyweight));
    }

    /// <summary>All else equal, the heavier division is the bigger night. This is what was asked for, and it
    /// still has to hold underneath the rule above.</summary>
    [Fact]
    public void TheSameFightIsWorthMoreAtHeavyweight()
    {
        var a = Man(80); var b = Man(78);
        Assert.True(V(a, b, BoutStakes.WorldTitle, WeightClass.Heavyweight)
                  > V(a, b, BoutStakes.WorldTitle, WeightClass.Flyweight));
    }

    [Fact]
    public void MoreAtStakeIsWorthMore()
    {
        var a = Man(82); var b = Man(80);
        var none = V(a, b, BoutStakes.None, WeightClass.Middleweight);
        var reg = V(a, b, BoutStakes.Regional, WeightClass.Middleweight);
        var elim = V(a, b, BoutStakes.Eliminator, WeightClass.Middleweight);
        var world = V(a, b, BoutStakes.WorldTitle, WeightClass.Middleweight);
        var unif = V(a, b, BoutStakes.Unification, WeightClass.Middleweight);
        var undis = V(a, b, BoutStakes.Undisputed, WeightClass.Middleweight);

        Assert.True(none < reg && reg < elim && elim < world && world < unif && unif < undis,
                    $"{none} {reg} {elim} {world} {unif} {undis}");
    }

    /// <summary>A star is a draw whoever he is in with. This is the one the first version got backwards: it
    /// scored a bout on its weaker man, so a great fighter in a showcase rated below two unknowns having a
    /// competitive fight. People go to see the great fighter.</summary>
    [Fact]
    public void AStarAgainstAJourneymanStillBeatsTwoNobodies()
    {
        var great = Man(95); var bum = Man(40, wins: 8, losses: 14);
        var nobodyA = Man(48, wins: 9, losses: 7); var nobodyB = Man(47, wins: 8, losses: 8);

        Assert.True(V(great, bum, BoutStakes.None, WeightClass.Middleweight)
                  > V(nobodyA, nobodyB, BoutStakes.None, WeightClass.Middleweight),
                    "a name in a showcase is rating below two men nobody has heard of");
    }

    /// <summary>A prospect is the other kind of draw — worth watching for what he might become, before he has
    /// beaten anybody. An ordinary fighter the same age is not.</summary>
    [Fact]
    public void AProspectIsADrawBeforeHeHasDoneAnything()
    {
        var prospect = Man(66, wins: 9, losses: 0); prospect.Age = 22; prospect.Potential = 92;
        var ordinary = Man(66, wins: 9, losses: 0); ordinary.Age = 22; ordinary.Potential = 62;
        var foe = Man(52, wins: 10, losses: 9);

        Assert.True(V(prospect, foe, BoutStakes.None, WeightClass.Middleweight)
                  > V(ordinary, foe, BoutStakes.None, WeightClass.Middleweight));
    }

    /// <summary>Being competitive is worth a lot ON TOP of the names — two good men closely matched is the
    /// night people remember. So an even fight between two contenders still outranks a star in a walkover.</summary>
    [Fact]
    public void AnEvenFightBetweenTwoContendersBeatsAWalkover()
    {
        var great = Man(95); var bum = Man(40, wins: 8, losses: 14);
        var goodA = Man(84); var goodB = Man(83);

        Assert.True(V(goodA, goodB, BoutStakes.None, WeightClass.Middleweight)
                  > V(great, bum, BoutStakes.None, WeightClass.Middleweight));
    }

    /// <summary>And the closer the matchup, the better — at equal quality, an even fight beats a lopsided one.</summary>
    [Fact]
    public void TheCloserTheMatchTheBigger()
    {
        var a = Man(88);
        Assert.True(V(a, Man(86), BoutStakes.None, WeightClass.Middleweight)
                  > V(a, Man(66), BoutStakes.None, WeightClass.Middleweight));
    }

    [Fact]
    public void TwoAtTheTopOfTheBoardBeatTwoAtTheBottom()
    {
        var a = Man(80); var b = Man(79);
        Assert.True(V(a, b, BoutStakes.None, WeightClass.Middleweight, 1, 2)
                  > V(a, b, BoutStakes.None, WeightClass.Middleweight, 14, 15));
    }

    [Fact]
    public void AnUnbeatenRecordAndALongRunAreWorthSomething()
    {
        var plain = Man(80, wins: 20, losses: 3);
        var unbeaten = Man(80, wins: 24, losses: 0, streak: 12);
        var foe = Man(79);

        Assert.True(V(unbeaten, foe, BoutStakes.None, WeightClass.Middleweight)
                  > V(plain, foe, BoutStakes.None, WeightClass.Middleweight));
    }

    /// <summary>A 3-0 novice is not "unbeaten" in any sense a crowd cares about.</summary>
    [Fact]
    public void ANoviceIsNotAnUnbeatenFighter()
    {
        var novice = Man(60, wins: 3, losses: 0);
        var alsoNovice = Man(60, wins: 2, losses: 1);
        var foe = Man(60);

        Assert.Equal(V(alsoNovice, foe, BoutStakes.None, WeightClass.Middleweight),
                     V(novice, foe, BoutStakes.None, WeightClass.Middleweight), 3);
    }

    [Fact]
    public void AReturnWithAQuestionOverItIsWorthMore()
    {
        var a = Man(80); var b = Man(79);
        Assert.True(V(a, b, BoutStakes.None, WeightClass.Middleweight, grudge: true)
                  > V(a, b, BoutStakes.None, WeightClass.Middleweight));
    }

    /// <summary>A bill runs up to its main event: smallest first, biggest last.</summary>
    [Fact]
    public void ABillBuildsToTheBiggestFight()
    {
        var order = BoutValue.RunningOrder(new[] { 500.0, 90.0, 1200.0, 300.0 }, x => x);
        Assert.Equal(new[] { 90.0, 300.0, 500.0, 1200.0 }, order);
    }

    /// <summary>Nothing is worth less than nothing — a value is a size, and a negative one would sort a truly
    /// terrible fight above a merely poor one once anything multiplies it.</summary>
    [Fact]
    public void NoFightIsWorthLessThanNothing()
    {
        var awful = Man(40, wins: 0, losses: 12);
        var worse = Man(40, wins: 0, losses: 20);
        Assert.True(BoutValue.Of(awful, worse, BoutStakes.None, WeightClass.Flyweight) >= 0);
    }
}
