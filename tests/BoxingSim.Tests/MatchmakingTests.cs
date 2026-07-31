using System;
using System.Linq;
using BoxingSim.Core.Career;
using BoxingSim.Core.Model;
using Xunit;

namespace BoxingSim.Tests;

/// <summary>Rules about when a man can be put in the ring, from bugs found by reading a fighter's record.</summary>
public class MatchmakingTests
{
    /// <summary>The year the world starts. Bouts dated before it came in with the roster — real fighters
    /// carry real records — and are not the simulation's to answer for.</summary>
    private const int StartYear = 1972;

    /// <summary>A warmed copy rather than a world built from scratch. Only bouts from StartYear on are judged
    /// here, so the decades of history this used to seed first were being filtered straight back out.</summary>
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

    /// <summary>Joe Frazier boxed Roberto Ribeiro and Muhammad Ali on the same 19 April.
    ///
    /// Bouts are staged in pass order but DATED across the year, so a man matched on one card could be handed
    /// a night he had already fought on. Nothing checked, because the only rest rule in the sim was on title
    /// bouts and measured from the champion's history alone.</summary>
    [Fact]
    public void NobodyBoxesTwiceInAMonth()
    {
        var g = RunAWorld(5, fights: 12);

        // Bouts against the PLAYER are excluded, and that is a known gap rather than a tidy-up: his dates come
        // from offers he agreed to, so ApplyOutcome deliberately will not move them — but the code that picks
        // his opponent asks only whether the man is injured, not whether he boxed last week. So an opponent can
        // still be double-booked around the player's fixed date. Guarding NPC against NPC is what the choke
        // point buys; closing this needs a recency check in opponent selection, which is its own change.
        var offenders = new System.Collections.Generic.List<string>();
        foreach (var b in g.EveryFighter)
        {
            var dates = b.History.Where(h => h.Date.Year >= StartYear && h.Opponent != g.Player.Name)
                                 .Select(h => h.Date).OrderBy(d => d).ToList();
            for (int i = 1; i < dates.Count; i++)
            {
                int gap = dates[i].DayNumber - dates[i - 1].DayNumber;
                if (gap < 28)
                    offenders.Add($"{b.Name}: {dates[i - 1]:d MMM yyyy} then {dates[i]:d MMM yyyy} ({gap}d)");
            }
        }

        Assert.True(offenders.Count == 0,
                    "fighters put in too soon after their last night:\n  " + string.Join("\n  ", offenders.Take(10)));
    }

    /// <summary>And never twice on the same DAY, which is the version of the above that cannot be argued
    /// about — a man is in one ring at a time.</summary>
    [Fact]
    public void NobodyBoxesTwiceOnTheSameDay()
    {
        var g = RunAWorld(9, fights: 12);
        foreach (var b in g.EveryFighter)
        {
            var doubled = b.History.Where(h => h.Date.Year >= StartYear && h.Opponent != g.Player.Name)
                                   .GroupBy(h => h.Date).FirstOrDefault(grp => grp.Count() > 1);
            Assert.True(doubled is null,
                        $"{b.Name} boxed {doubled?.Count()} times on {doubled?.Key:d MMM yyyy}");
        }
    }
}
