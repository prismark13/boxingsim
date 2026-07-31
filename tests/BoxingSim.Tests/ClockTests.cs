using System;
using System.Collections.Generic;
using System.Linq;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;
using Xunit;

namespace BoxingSim.Tests;

/// <summary>A step of the calendar is the length it says it is.
///
/// Resolving a bout used to move the world clock: ApplyOutcome opened by assigning to it, so the second fight
/// on a card began from wherever the first had pushed the date, and a card could carry the calendar months
/// forward on its own. A "week" of a universe advanced a median of 28 days, so the screen read WEEK 30 beside
/// a date more than two years on; a wait for fight night overshot the fight by anything up to eight weeks,
/// which is why the build-up ran out of weeks before it ran out of camp.
///
/// Bouts carry their own night now and the clock belongs to the calendar. These say so.
///
/// The two Universe tests below build their own worlds and have to: a Universe is not a CareerGame and drives
/// its own weeks, which is the thing being measured. Two warm-up years is cheap. The career tests take a warmed
/// copy — a stride is a property of the step, and the sixty-five years of history they used to seed first told
/// them nothing about it.</summary>
public class ClockTests
{
    [Fact]
    public void APlayWeekAdvancesAWeek()
    {
        var u = new Universe(new UniverseSettings { StartYear = 1970, Seed = 5, WarmupYears = 2 },
                             Fixtures.Roster.ToList());
        try
        {
            var strides = new List<int>();
            for (int w = 0; w < 60; w++)
            {
                var before = u.Date;
                u.PlayWeek();
                strides.Add(u.Date.DayNumber - before.DayNumber);
            }
            var wrong = strides.Where(d => d != 7).ToList();
            Assert.True(wrong.Count == 0,
                        $"{wrong.Count} of {strides.Count} weeks were not seven days: "
                        + string.Join(", ", wrong.Take(8)));
        }
        finally { Universe.Release(); }
    }

    /// <summary>And the week counter agrees with the calendar, which is what the universe screen shows.</summary>
    [Fact]
    public void AUniverseWeekCounterMatchesItsCalendar()
    {
        var u = new Universe(new UniverseSettings { StartYear = 1970, Seed = 6, WarmupYears = 2 },
                             Fixtures.Roster.ToList());
        try
        {
            var from = u.Date;
            for (int w = 0; w < 52; w++) u.PlayWeek();
            int days = u.Date.DayNumber - from.DayNumber;
            Assert.Equal(52, u.Week);
            Assert.Equal(52 * 7, days);
        }
        finally { Universe.Release(); }
    }

    /// <summary>Waiting never carries the player past his own fight.
    ///
    /// It did: an offer measured 169 days out was reached in three "weeks", because the cards run during each
    /// step were dragging the clock with them. The countdown on the camp page was counting something that was
    /// not happening.</summary>
    [Fact]
    public void AWaitNeverOvershootsFightNight()
    {
        var g = Worlds.Fresh(potential: 88, seed: 9);

        var overshot = new List<string>();
        var longStrides = new List<int>();
        int waited = 0;
        for (int i = 0; i < 200 && !g.Player.Retired; i++)
        {
            if (g.Offer is null) break;
            var before = g.Date;
            if (g.WaitAWeek() is null) { g.TakeOffer(); continue; }
            waited++;
            int moved = g.Date.DayNumber - before.DayNumber;
            if (moved > 7) longStrides.Add(moved);
            if (g.Date > g.OfferDate) overshot.Add($"{g.Date:d MMM yyyy} past fight night {g.OfferDate:d MMM yyyy}");
        }

        // Both lists below are empty in a world that never waited at all; say that a wait happened.
        Assert.True(waited > 50, $"only {waited} waits were made; the overshoot rule was barely exercised");
        Assert.True(overshot.Count == 0, "waiting went past the fight: " + string.Join("; ", overshot.Take(5)));
        Assert.True(longStrides.Count == 0,
                    $"{longStrides.Count} waits moved more than a week: " + string.Join(", ", longStrides.Take(8)));
    }

    /// <summary>No news arrives from the future.
    ///
    /// The build-up feed shows what a week produced, on the day it happened, beside a countdown to fight
    /// night. A headline dated after the day the world has reached is a fight the player is being told about
    /// before it has happened — and it sorts above everything real in a feed ordered by date.
    ///
    /// This is worth pinning because the bout scheduler is allowed to MOVE a fight: LegalNightFor pushes a
    /// bout off a night either man has already boxed on, and it pushes FORWARD — to 28 days after the man's
    /// previous bout, which from a card in the present is a date that has not arrived yet.
    ///
    /// It failed at 647 when the clock stopped being dragged along by every bout, which did not create the
    /// pushing — it revealed it. Closing it took five separate holes, each one a place where a fight was made
    /// first and rested afterwards: the card cadence, the card pools, the two shortcuts in PickChallenger
    /// that skip its own filter, a vacant title re-crowned mid-card, and DaysSinceLastBout reading the last
    /// bout APPENDED rather than the last one by date.</summary>
    [Fact]
    public void NoHeadlineIsDatedAfterTheDayTheWorldHasReached()
    {
        // Two hundred and sixty steps of one world, split across three. A headline from the future is a
        // property of the week that produced it, so three shorter walks read three sets of cards for the same
        // money — and it was a rare enough violation that seeing more worlds is worth more than seeing one
        // world for longer.
        var future = new List<string>();
        int weeks = 0;
        foreach (int seed in new[] { 9, 19, 29 })
        {
            var g = Worlds.Fresh(potential: 88, seed: seed);
            for (int i = 0; i < 90 && !g.Player.Retired; i++)
            {
                if (g.Offer is null) break;
                var week = g.WaitAWeek();
                if (week is null) { g.TakeOffer(); continue; }
                weeks++;
                foreach (var e in week)
                    if (e.On > g.Date)
                        future.Add($"seed {seed}: {e.On:d MMM yyyy} (world is on {g.Date:d MMM yyyy}, {(e.On.DayNumber - g.Date.DayNumber)}d ahead): {e.Text}");
            }
        }

        Assert.True(weeks > 100, $"only {weeks} weeks of news were read; this is not looking at enough of the world");
        Assert.True(future.Count == 0,
                    $"{future.Count} headlines arrived from the future: " + string.Join(" | ", future.Take(4)));
    }

    /// <summary>A camp has weeks in it. The point of the build-up is that it can be read a week at a time, and
    /// a wait that overshoots leaves a four-month camp with three steps in it.
    ///
    /// This used to give up quietly if no long camp came up on its one seed, which is a test that can pass by
    /// never running. It tries seeds until it finds one now, and says so if none of them had a long camp in
    /// it — the search costs nothing extra when the first seed obliges, which it does.</summary>
    [Fact]
    public void ALongCampHasAWeekForEveryWeek()
    {
        CareerGame? camp = null;
        foreach (int seed in new[] { 9, 19, 29 })
        {
            var g = Worlds.Fresh(potential: 88, seed: seed);
            // Find a camp with real time in it, then walk it out a week at a time.
            for (int i = 0; i < 40 && g.Offer is not null && g.DaysToFight < 42; i++) g.TakeOffer();
            if (g.Offer is not null && g.DaysToFight >= 42) { camp = g; break; }
        }
        Assert.True(camp is not null, "no seed produced a camp longer than six weeks to walk");

        int days = camp!.DaysToFight;
        int steps = 0;
        while (camp.WaitAWeek() is not null && steps < 200) steps++;

        int expected = (int)Math.Ceiling(days / 7.0);
        Assert.True(steps >= expected - 1,
                    $"a {days}-day camp ran out after {steps} weeks; it should take about {expected}");
    }
}
