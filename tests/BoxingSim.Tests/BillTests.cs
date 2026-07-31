using System;
using System.Collections.Generic;
using System.Linq;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;
using Xunit;

namespace BoxingSim.Tests;

/// <summary>The card the player's fight is on.
///
/// It used to be one division deep: every supporting bout came from the player's own weight, so a
/// heavyweight's championship bill was seven more heavyweight fights. That is not a promotion, it is a
/// division's spare men in a room. And where the player stood on it — main event, chief support, opener — was
/// decided by a coin rather than by how big his fight was.</summary>
public class BillTests
{
    private static CareerGame Career(int seed = 4)
    {
        var rng = new Random(seed);
        var player = CareerGame.CreatePlayer(rng, "Probe Man", "USA", WeightClass.Middleweight, potential: 86);
        return new CareerGame(1972, player, Fixtures.Roster.ToList(), rng, WeightClass.Middleweight,
                              seedHistory: true);
    }

    [Fact]
    public void ABillRunsAcrossTheWeights()
    {
        var g = Career();
        int spanned = 0, looked = 0;
        for (int i = 0; i < 12 && g.Offer is not null && !g.Player.Retired; i++)
        {
            var bill = g.Bill;
            if (bill.Count > 2)
            {
                looked++;
                if (bill.Select(l => l.Div).Distinct().Count() > 1) spanned++;
            }
            g.TakeOffer();
        }

        Assert.True(looked > 0, "no card of any size came up to look at");
        Assert.True(spanned > 0,
                    $"{looked} cards and every one of them was a single division — a bill is not a division");
    }

    /// <summary>Most of a bill is still the main event's weight. A card drawn evenly from the whole sport is
    /// no more a promotion than one drawn from a single division.</summary>
    [Fact]
    public void MostOfTheBillIsNearTheMainEventsWeight()
    {
        var g = Career();
        int near = 0, total = 0;
        for (int i = 0; i < 12 && g.Offer is not null && !g.Player.Retired; i++)
        {
            foreach (var l in g.Bill)
            {
                total++;
                if (Math.Abs((int)l.Div - (int)g.Player.WeightClass) <= 2) near++;
            }
            g.TakeOffer();
        }
        Assert.True(total > 0);
        Assert.True(near > total / 2,
                    $"only {near} of {total} bouts were within two divisions of the main event");
    }

    [Fact]
    public void TheTopOfTheBillIsTheMainEvent()
    {
        var g = Career();
        for (int i = 0; i < 8 && g.Offer is not null && !g.Player.Retired; i++)
        {
            var bill = g.Bill;
            if (bill.Count > 2)
            {
                Assert.Equal("MAIN EVENT", bill[0].Slot);
                Assert.Equal("CHIEF SUPPORT", bill[1].Slot);
                Assert.Equal("OPENER", bill[^1].Slot);
            }
            g.TakeOffer();
        }
    }

    /// <summary>Where the player is billed agrees with where his fight actually sits on the card. These are
    /// two views of one fact and used to be able to disagree, because one of them was a dice roll.</summary>
    [Fact]
    public void WhereHeIsBilledIsWhereHeIsOnTheCard()
    {
        var g = Career();
        for (int i = 0; i < 12 && g.Offer is not null && !g.Player.Retired; i++)
        {
            var bill = g.Bill;
            int at = bill.ToList().FindIndex(l => l.IsPlayer);
            if (at >= 0)
            {
                var expected = at switch
                {
                    0 => Billing.MainEvent,
                    1 => Billing.ChiefSupport,
                    2 or 3 => Billing.SwingBout,
                    _ => Billing.Opener,
                };
                Assert.Equal(expected, g.Billing);
            }
            g.TakeOffer();
        }
    }

    /// <summary>A card is listed main event first and BOXED from the foot upward. The openers go on while the
    /// hall is filling; the main event goes last.</summary>
    [Fact]
    public void TheCardIsBoxedFromTheFootUpward()
    {
        var g = Career();
        for (int i = 0; i < 10 && g.Offer is not null && !g.Player.Retired; i++)
        {
            var poster = g.Bill.Where(l => !l.IsPlayer).Select(l => l.Fight).ToList();
            g.TakeOffer();
            var fought = g.Undercard.Select(u => $"{u.A} vs {u.B}").ToList();
            if (poster.Count < 2 || fought.Count < 2) continue;

            // Everything that was boxed has to appear in reverse poster order. Bouts the sim dropped (a man
            // hurt between the announcement and the night) are simply absent, so this is a subsequence check
            // rather than an equality one.
            var bottomUp = Enumerable.Reverse(poster).ToList();
            int k = -1;
            foreach (var f in fought)
            {
                int next = bottomUp.IndexOf(f, k + 1);
                Assert.True(next > k,
                            $"'{f}' was boxed out of order. Poster top-to-bottom: {string.Join(" | ", poster)}. "
                            + $"Boxed: {string.Join(" | ", fought)}");
                k = next;
            }
            return;   // one full night checked is enough
        }
    }
}
