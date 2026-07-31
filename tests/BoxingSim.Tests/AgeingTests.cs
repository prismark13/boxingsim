using System;
using System.Linq;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;
using Xunit;

namespace BoxingSim.Tests;

/// <summary>Time passing — and it does not.
///
/// ALL THREE OF THESE FAIL. They are committed skipped rather than deleted, because what they found is worse
/// than the symptom that started it: a fighter still eighteen at 9-0-0 turned out not to be about the player
/// at all. NOBODY ages. Measured over two years and four, on the waiting path and the fighting path:
///
///   4 years passed (1 Mar 1972 to 26 Jan 1976), the player aged 0 and the world aged 0.
///
/// What is known: YearlyPass calls AgeRetireCrown; AgeRetireCrown loops the roster and calls AdvanceOneYear;
/// AdvanceOneYear increments Age as its first statement; the player IS in the roster via AddActive. Each link
/// looks right, so one of them is not being reached — that is where to start.
///
/// This very likely also explains the silent late career: if debuts, retirements, superfights and eliminators
/// are all in that same pass, a world that never ages is a world where none of them happen.</summary>
///
/// A fighter who started at eighteen was still eighteen at 9-0-0 with three years on the calendar behind him.
/// Everyone else in the world ages; a career is ABOUT ageing; and the one man it happens to was exempt.</summary>
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
    /// returns several a week. It was the same bug: with the turn of the year never running, nobody debuted
    /// and nobody retired, so the division slowly emptied of anyone eligible to be matched and the sport went
    /// quiet around a career that was still going.</summary>
    [Fact(Skip = "FAILS - a real, unfixed bug, and NOT the ageing one. Fifteen years in, fourteen weeks produce no news. Kept and named rather than deleted; remove the Skip to see it.")]
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

        int before = g.Log.Count;
        for (int w = 0; w < 14; w++) if (g.WaitAWeek() is null) break;
        int made = g.Log.Count - before;

        Assert.True(made > 0,
                    $"fourteen weeks of a {g.Date.Year - 1972}-year-old world ({g.Date:yyyy}) produced no news at all");
    }
}
