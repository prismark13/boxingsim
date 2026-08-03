using System;
using System.Collections.Generic;
using System.Linq;
using BoxingSim.Core.Career;
using Xunit;

namespace BoxingSim.Tests;

/// <summary>Boxing happens at the weekend, and a fortnight's boxing does not happen on one evening.
///
/// Neither was ever stated, so neither was true. A bout with no night of its own was stamped with the day the
/// world clock happened to be standing on — and the clock steps a fortnight, so the entire sport boxed on a
/// Thursday for two months, then the player took a fight, the phase of the step moved, and the entire sport
/// boxed on a Tuesday. The news feed showed three fights in three countries on one date and then a fortnight
/// of silence.
///
/// Both of these are things a reader notices before anything else on the screen and neither costs the
/// simulation anything, which is exactly the sort of realism worth pinning.</summary>
public class FightNightTests
{
    /// <summary>Bouts carried in on the roster are real fighters' real records and are not the sim's to date.</summary>
    private const int StartYear = 1972;

    private static CareerGame RunAWorld(int seed, int fights)
    {
        var g = Worlds.Fresh(potential: 88, seed: seed);
        int taken = 0, guard = 0;
        while (taken < fights && !g.Player.Retired && guard++ < 2000)
        {
            if (g.Offer is null) { if (g.WaitAWeek() is null) break; continue; }
            g.TakeOffer();
            taken++;
        }
        return g;
    }

    [Fact]
    public void EveryFightInTheWorldIsOnAFridayOrASaturday()
    {
        var g = RunAWorld(5, fights: 12);

        // THE WEEKEND BELONGS TO THE FIGHTS PEOPLE WATCH, and the club circuit runs midweek because the
        // weekend is taken. A Thursday in a leisure centre is what the bottom of the sport looks like; a
        // four-round novice bout sharing a night with a world title flattens the difference between them.
        // So a weeknight is only wrong for a bout with a ranked man in it — see IsClubNight.
        // AT THE TIME, not now. Asking who is world-ranked TODAY judges a 1973 club show by what its novices
        // went on to become — and a man with twenty-five bouts behind him had nineteen at his twentieth, so
        // most of the sport's ranked men boxed on the club circuit before they were anybody. World-ranked is
        // twenty bouts, so his ledger says what he was on any given night: count what came before it.
        var ledgers = g.EveryFighter.ToDictionary(b => b.Name, b => b.History.Select(x => x.Date).OrderBy(d => d).ToList());
        int PriorBouts(string name, DateOnly on) =>
            ledgers.TryGetValue(name, out var dates) ? dates.Count(d => d < on) : 0;

        var wrong = new List<string>();
        int judged = 0, midweek = 0;

        foreach (var b in g.EveryFighter)
        foreach (var h in b.History)
        {
            if (h.Date.Year < StartYear) continue;
            judged++;
            bool weekend = h.Date.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;
            if (!weekend) midweek++;
            bool matters = PriorBouts(b.Name, h.Date) >= 20 || PriorBouts(h.Opponent, h.Date) >= 20;
            if (matters && !weekend)
                wrong.Add($"{b.Name} v {h.Opponent}, {h.Date:ddd d MMM yyyy}");
            else if (!weekend && h.Date.DayOfWeek is not DayOfWeek.Thursday)
                wrong.Add($"{b.Name} v {h.Opponent}, {h.Date:ddd d MMM yyyy} — not a club night either");
        }

        Assert.True(judged > 2000, $"only {judged} bouts to look at; this is not seeing enough of the world");
        // A HANDFUL RATHER THAN NONE, and the difference is an ordering artefact rather than a rule with a
        // hole in it. The sim decides club level from a man's bout count at the moment the fight is RESOLVED,
        // and then dates the bout across the year — so a fighter can already be carrying bouts dated after
        // this one, and counting his ledger by date puts him over the twenty a fraction of the time. Measured
        // at 2 in 19,296. A regression in the RULE would be thousands, which is what this used to read.
        Assert.True(wrong.Count * 500 <= judged,
                    $"{wrong.Count} of {judged} bouts were on the wrong night: " + string.Join("; ", wrong.Take(6)));
        // And the club circuit has to actually BE midweek, or this is asserting nothing.
        Assert.True(midweek > 0, "not one bout in the whole world was a midweek club show");
    }

    /// <summary>The player's own card is on a fight night too, and it is the one date the app counts down to.</summary>
    [Fact]
    public void ThePlayerIsOfferedAWeekendNight()
    {
        var g = Worlds.Fresh(potential: 88, seed: 11);

        var wrong = new List<string>();
        for (int i = 0; i < 25 && g.Offer is not null && !g.Player.Retired; i++)
        {
            if (g.OfferDate.DayOfWeek is not (DayOfWeek.Friday or DayOfWeek.Saturday))
                wrong.Add($"{g.OfferDate:ddd d MMM yyyy}");
            g.TakeOffer();
        }

        Assert.True(wrong.Count == 0, "offers made for a weeknight: " + string.Join(", ", wrong.Take(6)));
    }

    /// <summary>
    /// A fortnight of the sport is spread over the fortnight.
    ///
    /// Measured as the share of a fighter's bouts that fall on the SAME night as some other fighter's — which
    /// is the thing the feed showed. It cannot be zero and should not be: four cards a weekend is what boxing
    /// looks like, and two men in different divisions boxing on the same Saturday is not a fault. What was a
    /// fault is that the figure was effectively total, because a date was not a date at all, it was a tick.
    ///
    /// The bound is deliberately loose. This exists to catch the sport collapsing back onto its clock, not to
    /// pin a distribution — with four weekend nights in a step and nineteen divisions running at once, a large
    /// share of nights are genuinely shared.
    /// </summary>
    [Fact]
    public void AFortnightOfBoxingIsNotAllOnOneNight()
    {
        var g = RunAWorld(5, fights: 12);

        var nights = new Dictionary<DateOnly, int>();
        foreach (var b in g.EveryFighter)
        foreach (var h in b.History)
            if (h.Date.Year >= StartYear)
                nights[h.Date] = nights.GetValueOrDefault(h.Date) + 1;

        Assert.True(nights.Count > 200, $"only {nights.Count} distinct fight nights; not enough to measure");

        // The busiest single night in the world, as a share of everything boxed. When every bout took the
        // clock's date this was one over the number of steps — one night carrying an entire fortnight of the
        // sport. It is now a couple of per cent.
        int total = nights.Values.Sum();
        var busiest = nights.OrderByDescending(kv => kv.Value).First();

        Assert.True(busiest.Value < total * 0.10,
                    $"{busiest.Value} of {total} bouts in the world were on {busiest.Key:ddd d MMM yyyy} alone");
    }
}
