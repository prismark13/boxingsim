using System.Linq;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;
using Xunit;

namespace BoxingSim.Tests;

/// <summary>A man's standing is one fact, and every screen has to print the same one.
///
/// Two bugs in one evening came from this not being true. The rankings page numbers the way a sanctioning
/// body does — champions sit above the list and take no number, so the best contender is #1 — while the "#N"
/// on the fight you were offered counted raw indexes into the same board, champions included. With two
/// champions in the division the page said #3 and the offer said #5.
///
/// Neither was catchable by anything that existed: they are presentation, so no property test looked at them,
/// and the golden master does not record a label. These do.</summary>
public class RankingLabelTests
{
    /// <summary>The number printed anywhere is the number the rankings page shows for the same man.</summary>
    [Fact]
    public void APlaceIsTheSameNumberWhereverItIsPrinted()
    {
        var g = Worlds.Fresh(potential: 88, seed: 4);

        // Enough boxing that the division has champions sitting above its contenders, which is the only
        // arrangement in which the two numberings can disagree.
        for (int i = 0; i < 12 && g.Offer is not null && !g.Player.Retired; i++) g.TakeOffer();

        foreach (var wc in g.LiveDivisions)
        {
            // The board as the page now lays it out: a block of champions, and a numbered list of contenders
            // under it. The two used to be one list with the champions taking the first rows, which is what
            // this test was written against — every number was right and the page still read as wrong, because
            // with three champions up top the man labelled #5 sits on the eighth row.
            var (champions, contenders) = g.BoardOf(wc, 15);
            foreach (var man in champions)
                Assert.True(g.PlaceOf(man) == 0,
                            $"{man.Name} holds a belt in {wc}, so he sits above the numbering — but a place "
                            + $"of #{g.PlaceOf(man)} would be printed for him on a bill.");
            int expected = 0;
            foreach (var man in contenders)
            {
                expected++;
                Assert.True(g.PlaceOf(man) == expected,
                            $"{man.Name} is #{expected} on the {wc} rankings page but #{g.PlaceOf(man)} "
                            + "everywhere a place is printed. Two screens disagreeing about where a man stands "
                            + "makes both of them untrustworthy.");
            }
        }
    }

    /// <summary>Nobody outside the board carries a number at all.</summary>
    [Fact]
    public void AManOffTheBoardHasNoNumber()
    {
        var g = Worlds.Fresh(potential: 86, seed: 9);
        for (int i = 0; i < 8 && g.Offer is not null && !g.Player.Retired; i++) g.TakeOffer();

        var wc = g.Player.WeightClass;
        var (champs, ranked) = g.BoardOf(wc, 15);
        var onTheBoard = champs.Concat(ranked).Select(b => b.Id).ToHashSet();
        int checkedOff = 0;

        foreach (var b in g.EveryFighter.Where(b => !b.Retired && b.WeightClass == wc && !onTheBoard.Contains(b.Id)))
        {
            Assert.True(g.PlaceOf(b) == 0,
                        $"{b.Name} is not on the {wc} board, yet #{g.PlaceOf(b)} would be printed beside his name.");
            if (++checkedOff >= 40) break;   // a division holds hundreds; a sample settles it
        }
        Assert.True(checkedOff > 0, "no unranked fighters were found to check, so this proved nothing");
    }
}
