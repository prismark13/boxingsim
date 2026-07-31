using System;
using System.Linq;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;
using Xunit;

namespace BoxingSim.Tests;

/// <summary>Time passing.
///
/// All of these once failed together, and the shared symptom was misleading. A fighter still eighteen at
/// 9-0-0 was not about the player: NOBODY aged. Four years passed (1 Mar 1972 to 26 Jan 1976) and the player
/// aged 0 and the world aged 0. Every link in the chain looked right — YearlyPass calls AgeRetireCrown,
/// AgeRetireCrown calls AdvanceOneYear, AdvanceOneYear increments Age first thing — because the chain was
/// never entered: the turn of the year was detected by asking whether THIS STEP crossed New Year, and the
/// step is not the only thing that moves the clock. See CatchUpYears.
///
/// The silent late career looked like the same bug and was not. It is kept here anyway, next to the tests it
/// was confused with, with its real cause written down.</summary>
public class AgeingTests
{
    [Fact]
    public void ThePlayerAgesWithTheCalendar()
    {
        var rng = new Random(3);
        var player = CareerGame.CreatePlayer(rng, "Probe Man", "USA", WeightClass.Middleweight, potential: 88);
        var g = new CareerGame(1972, player, Fixtures.Roster.ToList(), rng, WeightClass.Middleweight,
                               seedHistory: true);

        int startAge = g.Player.Age;
        var startDate = g.Date;

        int guard = 0;
        while (g.Date.Year - startDate.Year < 4 && !g.Player.Retired && guard++ < 4000)
        {
            if (g.Offer is null) { if (g.WaitAWeek() is null) break; continue; }
            g.TakeOffer();
        }

        int years = g.Date.Year - startDate.Year;
        int aged = g.Player.Age - startAge;
        Assert.True(aged >= years - 1,
                    $"{years} years passed ({startDate:d MMM yyyy} to {g.Date:d MMM yyyy}) "
                    + $"but the player aged {aged}: still {g.Player.Age} after {g.Player.History.Count} fights");
    }

    /// <summary>And the rest of the world with him, so nobody is ageing on a different clock.</summary>
    [Fact]
    public void TheWorldAgesTooAtRoughlyTheSameRate()
    {
        var rng = new Random(3);
        var player = CareerGame.CreatePlayer(rng, "Probe Man", "USA", WeightClass.Middleweight, potential: 88);
        var g = new CareerGame(1972, player, Fixtures.Roster.ToList(), rng, WeightClass.Middleweight,
                               seedHistory: true);

        var watch = g.EveryFighter.First(b => b.Id != g.Player.Id && !b.Retired);
        int was = watch.Age, playerWas = g.Player.Age;
        var from = g.Date;

        int guard = 0;
        while (g.Date.Year - from.Year < 4 && !g.Player.Retired && guard++ < 4000)
        {
            if (g.Offer is null) { if (g.WaitAWeek() is null) break; continue; }
            g.TakeOffer();
        }

        // Does ANYBODY age? This is what separates "the player is excluded" from "the yearly pass never runs".
        int movedOn = g.EveryFighter.Count(b => b.Age > 18);
        int years = g.Date.Year - from.Year;
        Assert.True(watch.Retired || watch.Age - was >= years - 1,
                    $"{years} years passed and the world aged {watch.Age - was}; "
                    + $"player aged {g.Player.Age - playerWas}; {movedOn} fighters are over 18");
    }

    /// <summary>And nobody ages FASTER than the calendar either.
    ///
    /// The fix for the world that never aged left the old detection in place on the universe path, so a step
    /// that crossed New Year ran the turn of the year twice — once for having crossed it, once for it not yet
    /// having been run. It hid behind the bug it sat next to: a world that had been ageing at zero now aged
    /// somewhat too fast, which reads as fixed. Sixteen and a half years passed and every tracked fighter in
    /// it aged twenty-one. Only steps that crossed the boundary themselves doubled, which is why it was half
    /// again rather than double.</summary>
    [Fact]
    public void AUniverseAgesNoFasterThanItsCalendar()
    {
        var u = new Universe(new UniverseSettings { StartYear = 1970, Seed = 7, WarmupYears = 2 },
                             Fixtures.Roster.ToList());
        try
        {
            var from = u.Date;
            var watched = u.World.EveryFighter.Where(b => !b.Retired).Take(8)
                           .Select(b => (Man: b, Was: b.Age)).ToList();

            for (int w = 0; w < 200; w++) u.PlayWeek();

            double years = (u.Date.DayNumber - from.DayNumber) / 365.2425;
            var over = watched.Where(x => x.Man.Age - x.Was > years + 1.5).ToList();
            Assert.True(over.Count == 0,
                        $"{years:0.0} calendar years passed and "
                        + string.Join("; ", over.Take(4).Select(x => $"{x.Man.Name} aged {x.Man.Age - x.Was}")));
        }
        finally { Universe.Release(); }   // the mileage dials are process-wide; don't leak them into the next test
    }

    /// <summary>Which path loses the year: waiting, or taking fights? Both go through AdvanceSome, and the
    /// year-end awards fire from the same block that ages the world — so if waiting ages and fighting does
    /// not, the pass is being skipped or undone somewhere on the fight path.</summary>
    [Fact]
    public void WaitingAloneStillAgesTheWorld()
    {
        var rng = new Random(3);
        var player = CareerGame.CreatePlayer(rng, "Probe Man", "USA", WeightClass.Middleweight, potential: 88);
        var g = new CareerGame(1972, player, Fixtures.Roster.ToList(), rng, WeightClass.Middleweight,
                               seedHistory: true);

        int was = g.Player.Age;
        var from = g.Date;
        // Never take a fight: just let the calendar run past two New Years.
        int guard = 0;
        while (g.Date.Year - from.Year < 2 && guard++ < 4000)
            if (g.WaitAWeek() is null) { g.DeclineOffer(); }

        Assert.True(g.Player.Age > was,
                    $"waited from {from:d MMM yyyy} to {g.Date:d MMM yyyy} and the player is still {g.Player.Age}");
    }

    /// <summary>Does the calendar ever go BACKWARDS during play?
    ///
    /// yearTurned is computed by comparing the new date with the old one. If something inside the step yanks
    /// the clock back, the comparison is made against a date that has already been rewound and the turn is
    /// never seen — which would explain a world that never ages while the year-end awards still fire.</summary>
    [Fact]
    public void TheCalendarNeverGoesBackwards()
    {
        var rng = new Random(3);
        var player = CareerGame.CreatePlayer(rng, "Probe Man", "USA", WeightClass.Middleweight, potential: 88);
        var g = new CareerGame(1972, player, Fixtures.Roster.ToList(), rng, WeightClass.Middleweight,
                               seedHistory: true);

        var prev = g.Date;
        var back = new System.Collections.Generic.List<string>();
        for (int i = 0; i < 300 && !g.Player.Retired; i++)
        {
            if (g.Offer is null) { g.WaitAWeek(); } else if (i % 5 == 0) { g.TakeOffer(); } else { g.WaitAWeek(); }
            if (g.Date < prev) back.Add($"{prev:d MMM yyyy} -> {g.Date:d MMM yyyy}");
            prev = g.Date;
        }
        Assert.True(back.Count == 0,
                    $"the clock went backwards {back.Count} times: " + string.Join("; ", back.Take(6)));
    }

    /// <summary>A long career does not go silent.
    ///
    /// Fourteen consecutive weeks of a fifteen-year-old world once returned no news at all, where a young one
    /// returns several a week. This test blamed the ageing bug, and was wrong: the world was as loud as ever —
    /// three thousand active fighters, a hundred headlines a year — and the reporting was broken. The log is
    /// capped at 1500 and drops from the front, and the step that gathered a week's news measured its position
    /// with the log's own Count. Once the cap was reached the Count stopped moving, so the difference across
    /// every subsequent step was zero and the feed died. Fifteen years is about fifteen hundred headlines,
    /// which is the whole of the "fifteen years in" symptom.
    ///
    /// So it asserts on what the step HANDS BACK, not on the length of the window, and checks the log really
    /// is at its cap — otherwise the test could pass by never getting far enough to be the case in
    /// question.</summary>
    [Fact]
    public void AnOldWorldStillMakesNews()
    {
        var rng = new Random(11);
        var player = CareerGame.CreatePlayer(rng, "Probe Man", "USA", WeightClass.Middleweight, potential: 90);
        var g = new CareerGame(1972, player, Fixtures.Roster.ToList(), rng, WeightClass.Middleweight,
                               seedHistory: true);

        // Fifteen years in.
        int guard = 0;
        while (g.Date.Year - 1972 < 15 && !g.Player.Retired && guard++ < 6000)
        {
            if (g.Offer is null) { if (g.WaitAWeek() is null) break; continue; }
            g.TakeOffer();
        }
        if (g.Player.Retired) return;   // he aged out, which is the system working

        int capped = g.Log.Count;
        int fed = 0;
        for (int w = 0; w < 14; w++) { var news = g.WaitAWeek(); if (news is null) break; fed += news.Count; }

        Assert.True(fed > 0,
                    $"fourteen weeks of a {g.Date.Year - 1972}-year-old world ({g.Date:yyyy}) fed nothing to the "
                    + $"build-up, with {g.EveryFighter.Count(b => !b.Retired)} active fighters still boxing in it");
        Assert.True(capped >= 1500,
                    $"this never reached the capped log ({capped} headlines), so it is not testing what it says");
    }
}
